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
[PluginMetadata(Id="LPLK_Demos",Name="LPLK_Demos",Version="0.1.3",Author="LPLK; references K4ryuu/Chicken",Description="Isolierte lokale Demoaufnahmen und Anfragen",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
 private ModuleSupport module=null!;private DemoSettings settings=new();private DemoArchive archive=null!;private DemoRecord? recording;
 private readonly Dictionary<string,(long Size,long Stable,long Deadline)> finalizing=[];private readonly HashSet<ulong> requesters=[];
 private string directory="",statusPath="";private WatchedSettings<DemoSettings> config=null!;private bool statusWriteFailed;private long nextTick,startAt,lastHuman,nextCleanup,retryAt;private bool mapReady,matchEnded;private int errors;private string? reloadError,lastError;
 private bool Human(IPlayer p)=>p.IsValid&&p.IsAuthorized&&!p.IsFakeClient&&p.SteamID!=0;
 // SourceTV is not returned as a normal valid player by every framework build.
 // A newly created owned file proves engine activity; validate its header AFTER
 // tv_stoprecord releases the engine's exclusive file lock.
 private bool TvReady()=>Core.ConVar.Find<bool>("tv_enable")?.Value==true;
 private bool PublicAllowed(IPlayer p)=>module.Active&&settings.Enabled&&settings.RequestsEnabled&&Human(p)&&mapReady&&recording?.State=="recording";
 public override void Load(bool hotReload)
 {
  module=new(Core,"LPLK_Demos","Demos","lplk.demos.admin",OpenAdmin);config=new(module.SettingsPath,DemoSettings.Validate);settings=config.Current;
  directory=Path.Combine(Core.GameFilesDirectory,"demos","LPLK_Demos");statusPath=Path.Combine(Core.PluginDataDirectory,"status.json");archive=new(directory,Path.Combine(Core.PluginDataDirectory,"index.json"));archive.Load();module.Active=true;
  // Normal plugin load runs before a valid entity system exists. MapLoad/round
  // events establish readiness; only hot reload may inspect an existing world.
  mapReady=false;if(hotReload){try{mapReady=Core.EntitySystem.GetGameRules()is not null;}catch(InvalidOperationException){mapReady=false;}}
  Core.GameEvent.HookPost<EventRoundStart>(_=>{mapReady=true;matchEnded=false;if(settings.CropRounds||settings.RequestsEnabled)Stop(false);return HookResult.Continue;});
  Core.GameEvent.HookPost<EventCsWinPanelMatch>(_=>{matchEnded=true;Stop(false);return HookResult.Continue;});
  WriteStatus();Core.Logger.LogInformation("LPLK_Demos 0.1.3 geladen; Auto={Auto}; Anfragen={Requests}; nur lokaler Speicher",settings.AutoRecord,settings.RequestsEnabled);
 }
 public override void ConfigureSharedInterface(IInterfaceManager manager)
 {
  module.ConfigureSharedInterface(manager);manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Demos.v1",new(){["allowed"]=(Func<IPlayer,string,bool>)((p,c)=>c=="sw_demo"&&PublicAllowed(p))});
 }
 public override void Unload(){Stop(false);module.Active=false;archive.Save();}
 [EventListener<EventDelegates.OnMapLoad>]public void MapLoad(IOnMapLoadEvent e){mapReady=true;matchEnded=false;retryAt=Environment.TickCount64+3000;}
 [EventListener<EventDelegates.OnMapUnload>]public void MapUnload(IOnMapUnloadEvent e){mapReady=false;Stop(true);}
 private void Start()
 {
  long now=Environment.TickCount64;
  if(!module.Active||!settings.Enabled||!mapReady||matchEnded||recording is not null||now<retryAt)return;
  if(!TvReady()){lastError="SourceTV nicht bereit (tv_enable / Mapstart prüfen)";retryAt=now+30000;WriteStatus();return;}
  if(!settings.RecordWarmup&&Core.EntitySystem.GetGameRules()?.WarmupPeriod!=false)return;
  var record=new DemoRecord{File=$"lplk_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.dem",Map=Core.Engine.GlobalVars.MapName,MinimumSeconds=settings.MinimumSeconds,DeleteUnrequested=settings.RequestsEnabled&&settings.DeleteUnrequested};
  archive.PathFor(record.File);archive.Records.Add(record);archive.Save();
  // Fixed directory and generated filename; no player input can become a console command.
  recording=record;startAt=lastHuman=now;requesters.Clear();lastError=null;
  Core.Engine.ExecuteCommand("tv_record demos/LPLK_Demos/"+Path.GetFileNameWithoutExtension(record.File));WriteStatus();
 }
 private void Stop(bool unloaded)
 {
  if(recording is not {} r)return;
  // During map unload never touch game rules, players or the native engine.
  if(!unloaded&&mapReady&&File.Exists(archive.PathFor(r.File)))Core.Engine.ExecuteCommand("tv_stoprecord");
  r.Seconds=Math.Max(0,(Environment.TickCount64-startAt)/1000.0);r.Stopped=DateTimeOffset.UtcNow;r.State="finalizing";
  finalizing[r.File]=(-1,Environment.TickCount64,Environment.TickCount64+30000);recording=null;requesters.Clear();archive.Save();WriteStatus();
 }
 [EventListener<EventDelegates.OnTick>]
 public void Tick()
 {
  long now=Environment.TickCount64;if(!module.Active||now<nextTick)return;nextTick=now+1000;
  config.Reload();reloadError=config.Error;
  try
  {
   SyncSettings();
   if(recording is {} r)
   {
    if(r.State=="starting")
    {
     if(File.Exists(archive.PathFor(r.File))&&new FileInfo(archive.PathFor(r.File)).Length>=8){r.State="recording";archive.Save();}
     else if(now-startAt>10000){r.State="failed";r.Failure=lastError="Engine hat keine gültige Demo angelegt";recording=null;errors++;retryAt=now+60000;archive.Save();}
    }
    if(recording is not null&&mapReady)
    {
     if(Core.PlayerManager.GetAllValidPlayers().Count(Human)>=settings.IdlePlayerThreshold)lastHuman=now;
     if(!settings.Enabled||(settings.StopOnIdle&&now-lastHuman>=settings.IdleSeconds*1000))Stop(false);
    }
   }
   foreach(var (file,state) in finalizing.ToArray())
   {
    var record=archive.Records.Single(x=>x.File==file);string path=archive.PathFor(file);long size=File.Exists(path)?new FileInfo(path).Length:-1;
    if(size!=state.Size&&now<state.Deadline){finalizing[file]=(size,now,state.Deadline);continue;}
    if(size>=16&&now-state.Stable>=3000&&archive.ValidHeader(record)){archive.Finish(record,size);finalizing.Remove(file);}
    else if(now>=state.Deadline){record.State="failed";record.Failure=lastError="Demo nach Aufnahmeende nicht bestätigt; vorhandene Datei erhalten";errors++;finalizing.Remove(file);archive.Save();}
   }
   if(mapReady&&settings.Enabled&&(settings.AutoRecord||settings.RequestsEnabled)&&!matchEnded&&recording is null&&Core.PlayerManager.GetAllValidPlayers().Any(Human))Start();
   if(now>=nextCleanup){nextCleanup=now+3600000;archive.Cleanup(DateTimeOffset.UtcNow,settings);}
  }catch(Exception e){errors++;lastError="Demo-Dateiverarbeitung fehlgeschlagen";Core.Logger.LogError(e,"LPLK_Demos: Datei bleibt zur Prüfung erhalten");retryAt=now+60000;}
  WriteStatus();
 }
 private void SyncSettings()
 {
  var next=config.Current;if(settings==next){settings=next;return;}
  // Each record retains the cleanup/minimum-duration rules captured when it started.
  if(recording is not null)Stop(false);settings=next;
 }
 private void WriteStatus()
 {
  try{AtomicJson.Write(statusPath,new{updatedAt=DateTimeOffset.UtcNow,version="0.1.3",settings.Enabled,settings.AutoRecord,settings.RequestsEnabled,settings.RecordWarmup,settings.CleanupEnabled,recording=recording?.State??"idle",currentFile=recording?.File,complete=archive.Records.Count(r=>r.State=="complete"),finalizing=finalizing.Count,last=archive.Records.LastOrDefault(),errors,lastError,reloadError,externalUploads=false});statusWriteFailed=false;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Demos: Status nicht schreibbar.");statusWriteFailed=true;}
 }
 private void Save(IPlayer actor,Func<DemoSettings,DemoSettings> edit)
 {
  if(!module.CanAdmin(actor))return;
  try{config.Change(edit);reloadError=null;}
  catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
  {reloadError=config.Error??e.GetType().Name;actor.SendChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
  SyncSettings();WriteStatus();
 }
 [Command("demo")]public void Request(ICommandContext c)
 {
  if(c.Sender is not {} p||!PublicAllowed(p)||recording is not {} r){c.Reply("[LPLK] Keine anfragbare Aufnahme aktiv.");return;}
  bool added=requesters.Add(p.SteamID);if(!added){p.SendChat("[LPLK] Deine Anfrage ist bereits gespeichert.");return;}r.Requested=true;r.Requests=requesters.Count;archive.Save();
  if(settings.PrintRequests&&r.Requests==1)foreach(var human in Core.PlayerManager.GetAllValidPlayers().Where(Human))human.SendChat("[LPLK] Die aktuelle Demo wurde zur Aufbewahrung markiert.");else p.SendChat("[LPLK] Demo zur Aufbewahrung markiert.");WriteStatus();
 }
 [Command("lplk_demos")]public void Status(ICommandContext c)
 {
  if(c.Sender is {} p){OpenAdmin(p);return;}
  // Console path serves the permission-checked existing webpanel. No filenames or commands accepted.
  if(c.Args.Length==1&&c.Args[0]=="start")Start();else if(c.Args.Length==1&&c.Args[0]=="stop")Stop(false);
  WriteStatus();c.Reply($"LPLK_Demos 0.1.3: enabled={settings.Enabled}; auto={settings.AutoRecord}; requests={settings.RequestsEnabled}; recording={recording?.State??"idle"}; complete={archive.Records.Count(r=>r.State=="complete")}; errors={errors}; error={lastError??"none"}");
 }
}
