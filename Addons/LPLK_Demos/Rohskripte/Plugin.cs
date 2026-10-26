using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
namespace LPLK_Demos;
[PluginMetadata(Id="LPLK_Demos",Name="LPLK_Demos",Version="0.2.3",Author="LPLK; references K4ryuu/Chicken",Description="Isolierte lokale Demoaufnahmen und Anfragen",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
 private ModuleSupport module=null!;private DemoSettings settings=new();private DemoArchive archive=null!;private DemoRecord? recording;private string currentMap="";
 private readonly Dictionary<string,(long Size,long Stable,long Deadline)> finalizing=[];private readonly HashSet<ulong> requesters=[];
 private readonly Dictionary<string,Task<bool>> headerChecks=[];
 private string directory="",statusPath="";private WatchedSettings<DemoSettings> config=null!;private bool statusWriteFailed;private long nextTick,startAt,lastHuman,nextCleanup,retryAt;private bool mapReady,matchEnded;private int errors;private string? reloadError,lastError;
 private bool Human(IPlayer p)=>p.IsValid&&p.IsAuthorized&&!p.IsFakeClient&&p.SteamID!=0;
 // SourceTV is not returned as a normal valid player by every framework build.
 // A newly created owned file proves engine activity; validate its header AFTER
 // tv_stoprecord releases the engine's exclusive file lock.
 private bool TvReady()=>Core.ConVar.Find<bool>("tv_enable")?.Value==true;
 private bool PublicAllowed(IPlayer p)=>module.Active&&settings.Enabled&&settings.RequestsEnabled&&Human(p)&&mapReady&&recording?.State=="recording";
 public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
 { LPLK.Common.RoundData.Attach(Core); ChatTheme.Initialize("LPLK_Demos",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
  module=new(Core,"LPLK_Demos","Demos","lplk.demos.admin",OpenAdmin);config=new(module.SettingsPath,DemoSettings.Validate);settings=config.Current;
  directory=Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"demos","LPLK_Demos");statusPath=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json");archive=new(directory,Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"index.json"),buffered:true);archive.Load();module.Active=true;
  // Normal plugin load runs before a valid entity system exists. MapLoad/round
  // events establish readiness; only hot reload may inspect an existing world.
  InstallTvGuard();InitializeRam();mapReady=false;if(hotReload){try{mapReady=Core.EntitySystem.GetGameRules()is not null;}catch(InvalidOperationException){mapReady=false;}}
  Core.GameEvent.HookPost<EventRoundStart>(_=>{OnRoundStart();return HookResult.Continue;});
  Core.GameEvent.HookPost<EventRoundEnd>(e=>{OnRoundEnd(e);return HookResult.Continue;});
  Core.GameEvent.HookPost<EventPlayerDeath>(e=>{IndexDeath(e);return HookResult.Continue;});
  Core.Scheduler.NextWorldUpdate(()=>{if(module.Active)ConfigureTv();});
  Core.GameEvent.HookPost<EventCsWinPanelMatch>(_=>{matchEnded=true;if(recording is {} done){done.CompleteMap=done.StartRound==1&&done.Rounds.FirstOrDefault()?.Partial==false;done.StopReason="match-end";}Stop(false);return HookResult.Continue;});
  WriteStatus();Core.Logger.LogInformation("LPLK_Demos 0.2.3 geladen; Auto={Auto}; Anfragen={Requests}; nur lokaler Speicher",settings.AutoRecord,settings.RequestsEnabled);
 }
 public override void ConfigureSharedInterface(IInterfaceManager manager)
 {
  module.ConfigureSharedInterface(manager);manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Demos.v1",new(){["allowed"]=(Func<IPlayer,string,bool>)((p,c)=>c=="sw_demo"&&PublicAllowed(p))});
 }
 public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){Stop(false);RestoreBotTest();module.Active=false;foreach(var file in exportRetry.Keys.ToArray())ramExporter?.Enqueue(file,Environment.TickCount64+1000,archive.Records.First(r=>r.File==file).Seconds);RemoveTvGuard();maintenance?.GetAwaiter().GetResult();ramExporter?.Drain();CollectExports();RestoreTv();archive.Save();archive.Drain(); LPLK.Common.RoundData.Drain(); }
 [EventListener<EventDelegates.OnMapLoad>]public void MapLoad(IOnMapLoadEvent e){currentMap=e.MapName;mapReady=true;matchEnded=false;roundOpen=false;roundAttempted=false;ConfigureTv();retryAt=Environment.TickCount64+3000;}
 [EventListener<EventDelegates.OnMapUnload>]public void MapUnload(IOnMapUnloadEvent e){if(recording is {} done){done.CompleteMap=done.StartRound==1&&done.Rounds.FirstOrDefault()?.Partial==false;done.StopReason="map-end";}EndRoundIndex(capture:false);mapReady=false;roundOpen=false;Stop(true);RestoreBotTest();}
 private void Start()
 {
  long now=Environment.TickCount64;
  if(!module.Active||!settings.Enabled||!mapReady||matchEnded||recording is not null||now<retryAt)return;
  if(!BotTest&&!Core.PlayerManager.GetAllValidPlayers().Any(PlayingHuman))return;
  if(settings.RamRecording&&((!BotTest&&!roundOpen)||(!MapRecording&&roundAttempted)||!RamAvailable()))return;
  if(!TvReady()){lastError="SourceTV nicht bereit (tv_enable / Mapstart prüfen)";retryAt=now+30000;WriteStatus();return;}
  if(!BotTest&&!settings.RecordWarmup&&Core.EntitySystem.GetGameRules()?.WarmupPeriod!=false)return;
  if(currentMap.Length==0){try{currentMap=Core.Engine.GlobalVars.MapName;}catch(InvalidOperationException){lastError="Map noch nicht initialisiert";return;}}
  var record=new DemoRecord{IndexVersion=2,Kind=BotTest?"test":MapRecording?"map":"round",IsTest=BotTest,StartTick=EngineTick(),StartRound=EngineRound(),File=$"lplk_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.dem",Map=currentMap,InRam=settings.RamRecording,MinimumSeconds=settings.MinimumSeconds,DeleteUnrequested=settings.RequestsEnabled&&settings.DeleteUnrequested};
  archive.PathFor(record.File);archive.Records.Add(record);archive.Save();
  // Fixed directory and generated filename; no player input can become a console command.
  recording=record;participantLookup.Clear();if(record.InRam)roundAttempted=true;startAt=lastHuman=now;requesters.Clear();lastError=null;
  string command=record.InRam?RamRecordCommand(ramDirectory,record.File):"tv_record demos/LPLK_Demos/"+Path.GetFileNameWithoutExtension(record.File);
  CapturePlayers();Core.Engine.ExecuteCommand(command);BeginRoundIndex(partial:BotTest||lastRoundStart==0||now-lastRoundStart>100);WriteStatus();
 }
 private void Stop(bool unloaded)
 {
  if(recording is not {} r)return;
  if(!unloaded){CapturePlayers();EndRoundIndex();}
  r.StopReason??=BotTest?"test-end":"idle-or-manual";
  // During map unload never touch game rules, players or the native engine.
  if(!unloaded&&mapReady&&(r.InRam||File.Exists(RecordingPath(r))))Core.Engine.ExecuteCommand("tv_stoprecord");
  nextIndexFlush=Environment.TickCount64+1000;
  r.Seconds=Math.Max(0,(Environment.TickCount64-startAt)/1000.0);r.Stopped=DateTimeOffset.UtcNow;r.State="finalizing";
  if(r.InRam){recording=null;requesters.Clear();ScheduleExport(r);archive.Save();WriteStatus();return;}
  finalizing[r.File]=(-1,Environment.TickCount64,Environment.TickCount64+30000);recording=null;requesters.Clear();archive.Save();WriteStatus();
 }
 [EventListener<EventDelegates.OnTick>]
 public void Tick()
 {
  long now=Environment.TickCount64;if(!module.Active||now<nextTick)return;nextTick=now+1000;
  config.Reload();reloadError=config.Error;
  try
  {
   SyncSettings();TestAndIndexTick(now);if(mapReady)humansPlaying=Core.PlayerManager.GetAllValidPlayers().Any(PlayingHuman);CapturePlayers();CollectExports();RetryExports(now);MaintainArchive(now);
   if(recording is {} r)
   {
    if(r.InRam&&!RamAvailable())Stop(false);
    if(recording is not null&&r.State=="starting")
    {
     if(File.Exists(RecordingPath(r))&&new FileInfo(RecordingPath(r)).Length>=8){r.State="recording";archive.Save();}
     else if(!r.InRam&&now-startAt>10000){r.State="failed";r.Failure=lastError="Engine hat keine gültige Demo angelegt";recording=null;errors++;retryAt=now+60000;archive.Save();}
    }
    if(recording is not null&&mapReady)
    {
     if(Core.PlayerManager.GetAllValidPlayers().Count(Human)>=settings.IdlePlayerThreshold)lastHuman=now;
     if(!settings.Enabled||(!BotTest&&!Core.PlayerManager.GetAllValidPlayers().Any(PlayingHuman))||(!BotTest&&settings.StopOnIdle&&now-lastHuman>=settings.IdleSeconds*1000))Stop(false);
    }
   }
   foreach(var (file,state) in finalizing.ToArray())
   {
    var record=archive.Records.Single(x=>x.File==file);string path=archive.PathFor(file);long size=File.Exists(path)?new FileInfo(path).Length:-1;
    if(size!=state.Size&&now<state.Deadline){finalizing[file]=(size,now,state.Deadline);continue;}
    if(size>=16&&now-state.Stable>=3000&&HeaderReady(record)){archive.FinishVerified(record,size);finalizing.Remove(file);headerChecks.Remove(file);}
    else if(now>=state.Deadline){record.State="failed";record.Failure=lastError="Demo nach Aufnahmeende nicht bestätigt; vorhandene Datei erhalten";errors++;finalizing.Remove(file);archive.Save();}
   }
   if(mapReady&&settings.Enabled&&(settings.AutoRecord||settings.RequestsEnabled)&&!matchEnded&&recording is null&&Core.PlayerManager.GetAllValidPlayers().Any(PlayingHuman))Start();
  }catch(Exception e){errors++;lastError="Demo-Dateiverarbeitung fehlgeschlagen";Core.Logger.LogError(e,"LPLK_Demos: Datei bleibt zur Prüfung erhalten");retryAt=now+60000;}
  WriteStatus();
 }
 private bool HeaderReady(DemoRecord record)
 {
  if(!headerChecks.TryGetValue(record.File,out var task)){var sample=new DemoRecord{File=record.File};headerChecks[record.File]=task=RoundFileStore.RunAfterRound(()=>archive.ValidHeader(sample));}
  if(!task.IsCompleted)return false;bool ready=task.GetAwaiter().GetResult();if(!ready)headerChecks.Remove(record.File);return ready;
 }
 private void SyncSettings()
 {
  var next=config.Current;if(settings==next){settings=next;return;}
  if(next with{RetentionHours=settings.RetentionHours,CleanupEnabled=settings.CleanupEnabled}==settings){settings=next;nextCleanup=0;return;}
  // Each record retains the cleanup/minimum-duration rules captured when it started.
  if(recording is not null)Stop(false);bool changedRam=settings.RamRecording!=next.RamRecording;settings=next;if(changedRam){roundOpen=false;ConfigureTv();}
 }
 private void WriteStatus()
 {
  try{LPLK.Common.DeferredJson.Queue(statusPath,new{updatedAt=DateTimeOffset.UtcNow,version="0.2.3",settings.Enabled,settings.AutoRecord,settings.RequestsEnabled,settings.RecordWarmup,settings.CleanupEnabled,settings.RamRecording,ramLimitMiB=2048,ramReserveMiB=256,ramFreeBytes=ramFree,ramReady,exportPending=ramExporter?.Pending??0,exportDelayMilliseconds=1000,roundOpen,recording=recording?.State??"idle",currentFile=recording?.File,complete=archive.Records.Count(r=>r.State=="complete"),finalizing=finalizing.Count,last=archive.Records.LastOrDefault() is {} recent?new{recent.File,recent.State,recent.Map,recent.Kind,recent.IsTest,rounds=recent.Rounds.Count,kills=recent.Kills.Count}:null,indexSaveError=archive.SaveError,errors,lastError,reloadError,externalUploads=false,settings.RetentionHours,tvGuard,tvGuardError,tvKicksPrevented,humansPlaying,storage});statusWriteFailed=false;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Demos: Status nicht schreibbar.");statusWriteFailed=true;}
 }
 private void Save(IPlayer actor,Func<DemoSettings,DemoSettings> edit)
 {
  if(!module.CanAdmin(actor))return;
  try{config.Change(edit);reloadError=null;}
  catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
  {reloadError=config.Error??e.GetType().Name;actor.SendLplkChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
  SyncSettings();WriteStatus();
 }
 [Command("demo")]public void Request(ICommandContext c)
 {
  if(c.Sender is not {} p||!PublicAllowed(p)||recording is not {} r){c.ReplyLplk("[LPLK] Keine anfragbare Aufnahme aktiv.");return;}
  bool added=requesters.Add(p.SteamID);if(!added){p.SendLplkChat("[LPLK] Deine Anfrage ist bereits gespeichert.");return;}r.Requested=true;r.Requests=requesters.Count;archive.Save();
  if(settings.PrintRequests&&r.Requests==1)foreach(var human in Core.PlayerManager.GetAllValidPlayers().Where(Human))human.SendLplkChat("[LPLK] Die aktuelle Demo wurde zur Aufbewahrung markiert.");else p.SendLplkChat("[LPLK] Demo zur Aufbewahrung markiert.");WriteStatus();
 }
 [Command("lplk_demos")]public void Status(ICommandContext c)
 {
  if(c.Sender is {} p){OpenAdmin(p);return;}
  // Console path serves the permission-checked existing webpanel. No filenames or commands accepted.
  if(c.Args.Length==2&&c.Args[0]=="test300"&&long.TryParse(c.Args[1],out var requestTime))c.ReplyLplk(StartBotTest(requestTime));else if(c.Args.Length==1&&c.Args[0]=="start")Start();else if(c.Args.Length==1&&c.Args[0]=="stop")Stop(false);else if(c.Args.Length==1&&c.Args[0]=="repair")c.ReplyLplk(RepairEmptyServer());
  WriteStatus();c.ReplyLplk($"LPLK_Demos 0.2.3: enabled={settings.Enabled}; auto={settings.AutoRecord}; requests={settings.RequestsEnabled}; recording={recording?.State??"idle"}; complete={archive.Records.Count(r=>r.State=="complete")}; errors={errors}; humansPlaying={humansPlaying}; tvGuard={tvGuard}; tvKicksPrevented={tvKicksPrevented}; guardError={tvGuardError??"none"}; error={lastError??"none"}");
 }
}
