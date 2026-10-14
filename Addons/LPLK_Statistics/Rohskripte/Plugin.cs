using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Statistics;
[PluginMetadata(Id="LPLK_Statistics",Name="LPLK_Statistics",Version="0.2.2",Author="LPLK; references Michael Zauche, K4ryuu",Description="PvP, Bottraining, Waffen-StatTrak und Schadenübersicht",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
 private ModuleSupport module=null!;private StatisticsSettings settings=new();private StatsData data=new();
 private readonly ShotLedger shots=new();private readonly FireLedger fires=new();
 private readonly Dictionary<string,(bool Held,long At)> inspect=[];
 private readonly Dictionary<int,string> sessions=[];private readonly HashSet<ulong> pendingExports=[];
 private WeaponRankingCapture? weaponCapture;private Task<long>? weaponExportTask;private string? weaponExportError;private long weaponExportMs;
 private string dataPath="";private DateTime stamp;private long nextStatus,nextSave;private bool dirty;private int errors;private string? reloadError,saveError;
 private static bool Human(IPlayer? p)=>p is {IsValid:true,IsAuthorized:true,IsFakeClient:false,SteamID:>0};
 private static string Session(IPlayer p)=>p.SteamID+":"+p.SessionId;
 private bool Live=>module.Active&&settings.Enabled;
 private bool Allowed(IPlayer p)=>Live&&Human(p);
 private bool Warmup()=>Core.EntitySystem.GetGameRules()?.WarmupPeriod!=false;
 private bool Ffa()=>Core.ConVar.Find<bool>("mp_teammates_are_enemies")?.Value==true;
 private static int Team(IPlayer p)=>p.Controller.IsValid?p.Controller.TeamNum:0;
 private PlayerStats Get(IPlayer p){sessions[p.PlayerID]=Session(p);dirty=true;pendingExports.Add(p.SteamID);return data.Touch(p.SteamID,p.Controller.PlayerName);}
 public override void Load(bool hotReload)
 {
  module=new(Core,"LPLK_Statistics","Statistik","lplk.statistics.admin",OpenAdmin);
  settings=module.ReadSettings<StatisticsSettings>(StatisticsSettings.Validate);stamp=File.GetLastWriteTimeUtc(module.SettingsPath);
  dataPath=Path.Combine(Core.PluginDataDirectory,"statistics.json");data=StatsData.Read(dataPath);foreach(ulong id in data.Players.Keys)pendingExports.Add(id);dirty=true;module.Active=true;
  Core.GameHooks.Movement.RunCommand.Pre+=Inspect;
  Core.GameEvent.HookPost<EventWeaponFire>(Fire);Core.GameEvent.HookPost<EventPlayerHurt>(Hurt);Core.GameEvent.HookPost<EventPlayerDeath>(Death);
  Core.GameEvent.HookPost<EventGrenadeThrown>(e=>{if(Live&&Human(e.UserIdPlayer))fires.Throw(Session(e.UserIdPlayer!),StatPolicy.Weapon(e.Weapon)??"",Environment.TickCount64);return HookResult.Continue;});
  Core.GameEvent.HookPost<EventMolotovDetonate>(e=>{if(Live&&Human(e.UserIdPlayer))fires.Detonate(Session(e.UserIdPlayer!),Environment.TickCount64,settings.InfernoSeconds,settings.UnknownInferno);return HookResult.Continue;});
  Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(Human(e.UserIdPlayer)){string s=Session(e.UserIdPlayer!);shots.Remove(s);inspect.Remove(s);shown.Remove(s);}return HookResult.Continue;});
  Core.GameEvent.HookPost<EventRoundStart>(_=>{ClearTransient();BeginRound();return HookResult.Continue;});
  Core.GameEvent.HookPost<EventRoundEnd>(e=>{EndRound(e);if(Live&&settings.DamageSummary)foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))Summary(p,null);Flush();return HookResult.Continue;});
  RegisterExtended();WriteStatus();Core.Logger.LogInformation("LPLK_Statistics 0.2.2 geladen; Datensätze={Count}; InspectOnly=True",data.Players.Count);
 }
 public override void ConfigureSharedInterface(IInterfaceManager manager)
 {
  module.ConfigureSharedInterface(manager);manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Statistics.v1",new(){["allowed"]=(Func<IPlayer,string,bool>)((p,c)=>Allowed(p)&&c=="sw_stats")});
 }
 public override void Unload(){module.Active=false;Core.GameHooks.Movement.RunCommand.Pre-=Inspect;Flush();weaponCapture=null;CompleteWeaponExport(true);ClearTransient();ownership.Clear();knownWeapons.Clear();playClocks.Clear();roundPlayers.Clear();}
 [EventListener<EventDelegates.OnMapUnload>]
 public void MapUnload(IOnMapUnloadEvent e){Flush();ClearTransient();ownership.Clear();knownWeapons.Clear();playClocks.Clear();roundPlayers.Clear();}
 [EventListener<EventDelegates.OnClientDisconnected>]
 public void Disconnected(IOnClientDisconnectedEvent e)
 {
  if(sessions.Remove(e.PlayerId,out var s)){shots.Remove(s);fires.Remove(s);inspect.Remove(s);shown.Remove(s);foreach(var k in damages.Keys.Where(k=>k.From==s||k.To==s).ToArray())damages.Remove(k);}Flush();
 }
 private void ClearTransient(){shots.Clear();fires.Clear();inspect.Clear();damages.Clear();shown.Clear();}
 private bool BotTraining(IPlayer p)
 {
  if(!settings.BotTrainingEnabled||Team(p)is not (2 or 3))return false;
  bool ffa=Ffa();var enemies=Core.PlayerManager.GetAllValidPlayers().Where(q=>q.PlayerID!=p.PlayerID&&Team(q)is 2 or 3&&(ffa||Team(q)!=Team(p))).ToArray();
  return enemies.Any(q=>q.IsFakeClient)&&(!settings.OnlyBotEnemies||enemies.All(q=>q.IsFakeClient));
 }
 private HookResult Fire(EventWeaponFire e)
 {
  var p=e.UserIdPlayer;if(!Live||!Human(p)||(!settings.CountPvpWarmup&&Warmup()))return HookResult.Continue;
  string? w=StatPolicy.Weapon(e.Weapon);if(w is null||WeaponCatalog.Find("weapon_"+w)is null)return HookResult.Continue;
  bool bot=BotTraining(p!);if(!bot&&!settings.PvpEnabled)return HookResult.Continue;
  var s=Get(p!);(bot?s.Bots:s.Pvp).Shots++;shots.Fire(Session(p!),w,Environment.TickCount64,bot);return HookResult.Continue;
 }
 private HookResult Hurt(EventPlayerHurt e)
 {
  if(!Live)return HookResult.Continue;
  var a=e.AttackerPlayer;var v=e.UserIdPlayer;if(a is not {IsValid:true}||v is not {IsValid:true}||a.PlayerID==v.PlayerID)return HookResult.Continue;
  if(Human(a)&&v.IsFakeClient&&BotTraining(a)&& (settings.CountPvpWarmup||!Warmup()))
  {
   var hit=shots.Hurt(Session(a),StatPolicy.Weapon(e.Weapon)??"",Environment.TickCount64,e.ActualHitGroup==HitGroup_t.HITGROUP_HEAD,true);if(hit.Hit||hit.Head){var c=Get(a).Bots;if(hit.Hit)c.Hits++;if(hit.Head)c.HeadHits++;}
  }
  ExtendedHurt(a,v,Math.Max(0,(int)e.ActualDmgHealth));
  if(!Warmup()&&(Ffa()||Team(a)!=Team(v)||settings.ShowFriendlyDamage))TrackDamage(a,v,Math.Max(0,(int)e.ActualDmgHealth));
  return HookResult.Continue;
 }
 private HookResult Death(EventPlayerDeath e)
 {
  if(!Live)return HookResult.Continue;var a=e.AttackerPlayer;var v=e.UserIdPlayer;bool warm=Warmup();
  if(v is {IsValid:true})ownership.EndLife(Session(v));if(Human(v)){if(settings.PvpEnabled&&(settings.CountPvpWarmup||!warm))Get(v!).Pvp.Deaths++;if(!warm&&settings.DamageOnDeath)Summary(v!,a is {IsValid:true}?Session(a):null);shots.Remove(Session(v!));}
  if(!Human(a)||v is not {IsValid:true}||a!.PlayerID==v.PlayerID||(!settings.CountTeamKills&&!Ffa()&&Team(a)==Team(v))){Flush();return HookResult.Continue;}
  ExtendedDeath(e,a,v);var s=Get(a);
  if(settings.CountPvpWarmup||!warm)
  {
   Counters? c=v.IsFakeClient&&BotTraining(a)?s.Bots:Human(v)&&settings.PvpEnabled?s.Pvp:null;
   if(c is not null){c.Kills++;if(e.Headshot)c.Headshots++;}
  }
  if(settings.WeaponTracking&&Human(v)&&(settings.CountWeaponWarmup||!warm)&&StatPolicy.Weapon(e.Weapon)is {} w)
  {
   if(w=="inferno")w=fires.Resolve(Session(a),Environment.TickCount64,settings.InfernoSeconds,settings.UnknownInferno);
   s.Weapons[w]=s.Weapons.GetValueOrDefault(w)+1;
   if(settings.AnnounceGrenadeKill&&StatPolicy.Grenades.Contains(w))a.SendChat($"[LPLK] {w}: {s.Weapons[w]} Kills.");
  }
  Flush();return HookResult.Continue;
 }
 private void Inspect(ref RunCommandMovementPreContext ctx)
 {
  var p=ctx.Params.Player;if(!Allowed(p))return;string key=Session(p);long now=Environment.TickCount64;
  bool held=(ctx.Params.UserCmd.ButtonState.ButtonStates[0]&(1UL<<35))!=0;
  var old=inspect.GetValueOrDefault(key);inspect[key]=(held,old.At);sessions[p.PlayerID]=key;
  if(!held||old.Held||now-old.At<settings.InspectCooldownMs||!settings.WeaponTracking||!settings.InspectHud||!p.IsAlive||Core.MenusAPI.GetCurrentMenu(p)is not null)return;
  if(data.Players.TryGetValue(p.SteamID,out var existing)&&existing.Hud==false||existing?.Hud is null&&!settings.DefaultHud)return;
  var weapon=p.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;if(weapon is not {IsValid:true})return;
  string? w=StatPolicy.Weapon(Core.Helpers.GetClassnameByDefinitionIndex(weapon.AttributeManager.Item.ItemDefinitionIndex));if(w is null)return;
  inspect[key]=(held,now);long kills=existing?.Weapons.GetValueOrDefault(w)??0;
  var ranking=data.Players.OrderByDescending(x=>x.Value.Weapons.GetValueOrDefault(w)).ThenBy(x=>x.Key).Where(x=>x.Value.Weapons.GetValueOrDefault(w)>0).ToArray();
  int rank=Array.FindIndex(ranking,x=>x.Key==p.SteamID)+1;string leader=ranking.Length>0?StatPolicy.Html(ranking[0].Value.Name):"–";
  p.SendCenterHTML($"<font color='#D8A63A'>StatTrak · {StatPolicy.Html(w)}</font><br>Kills: {kills} · Rang: {(rank>0?rank.ToString():"–")}<br>Führend: {leader}",settings.HudDurationMs);
 }
 private void Flush()
 {
  if(!dirty)return;try{data.Validate();AtomicJson.Write(dataPath,data);foreach(ulong id in pendingExports)AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"web",id+".json"),WebExport.Player(id,data.Players[id],settings,DateTimeOffset.UtcNow));pendingExports.Clear();dirty=false;saveError=null;}
  catch(Exception e){if(saveError is null){errors++;Core.Logger.LogError(e,"LPLK_Statistics: Speichern fehlgeschlagen; Daten bleiben im Speicher");}saveError="Speichern fehlgeschlagen";}
 }
 [EventListener<EventDelegates.OnTick>]
 public void Tick()
 {
  if(!module.Active)return;ExtendedTick(Environment.TickCount64);AdvanceWeaponCapture();if(Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+2000;
  try{var changed=File.GetLastWriteTimeUtc(module.SettingsPath);if(changed!=stamp){var next=JsonSerializer.Deserialize<StatisticsSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();StatisticsSettings.Validate(next);settings=next;stamp=changed;reloadError=null;ClearTransient();MarkExports();}}
  catch(Exception e){if(reloadError is null)Core.Logger.LogWarning(e,"LPLK_Statistics: letzter gültiger Stand bleibt aktiv");reloadError="Ungültige Dateiänderung";}
  if(Environment.TickCount64>=nextSave){nextSave=Environment.TickCount64+30000;Flush();FlushRanking();}WriteStatus();
 }
 private void MarkExports(){dirty=true;foreach(ulong id in data.Players.Keys)pendingExports.Add(id);nextSave=0;}
 private void CompleteWeaponExport(bool wait=false)
 {
  if(weaponExportTask is null||(!wait&&!weaponExportTask.IsCompleted))return;
  try{weaponExportMs=weaponExportTask.GetAwaiter().GetResult();weaponExportError=null;}
  catch{weaponExportError="Waffenranglistenexport fehlgeschlagen";}
  weaponExportTask=null;
 }
 private void FlushRanking()
 {
  try{AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"web","ranking.json"),RankingExport.Create(data,settings,DateTimeOffset.UtcNow));}catch{saveError="Ranglistenexport fehlgeschlagen";}
  CompleteWeaponExport();if(weaponExportTask is not null||weaponCapture is not null)return;
  weaponCapture=new WeaponRankingCapture(data,settings);
 }
 private void AdvanceWeaponCapture()
 {
  if(weaponCapture is null||!weaponCapture.Step(data))return;
  // At most 32 players / a 0.5 ms work budget per tick; the worker receives detached values only.
  var snapshot=weaponCapture.Finish(DateTimeOffset.UtcNow);weaponCapture=null;
  string path=Path.Combine(Core.PluginDataDirectory,"web","weapons.json");
  weaponExportTask=Task.Run(()=>{var timer=System.Diagnostics.Stopwatch.StartNew();AtomicJson.Write(path,WeaponRankingExport.Create(snapshot));return timer.ElapsedMilliseconds;});
 }
 private void WriteStatus(){CompleteWeaponExport();AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{version="0.2.2",updatedAt=DateTimeOffset.UtcNow,settings.Enabled,settings.PvpEnabled,settings.BotTrainingEnabled,settings.WeaponTracking,settings.InspectHud,trigger="InspectOnly",settings.DamageCenter,settings.DamageConsole,players=data.Players.Count,dirty,errors,saveError,reloadError,weaponExportError,weaponExportMs,weaponExportRunning=weaponExportTask is not null,weaponSnapshotRunning=weaponCapture is not null});}
 private void Save(StatisticsSettings next){StatisticsSettings.Validate(next);module.SaveSettings(next);settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);ClearTransient();MarkExports();reloadError=null;WriteStatus();}
 [Command("lplk_statistics")]
 public void Status(ICommandContext c){if(c.Sender is {} p){OpenAdmin(p);return;}WriteStatus();c.Reply($"LPLK_Statistics 0.2.2: enabled={settings.Enabled}; players={data.Players.Count}; InspectOnly=True; errors={errors}; saveError={saveError??"none"}; reloadError={reloadError??"none"}");}
}
