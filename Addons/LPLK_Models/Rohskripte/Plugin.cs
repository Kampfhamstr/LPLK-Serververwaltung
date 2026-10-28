using System.Globalization;
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
using File=LPLK.Common.RamFile;
namespace LPLK_Models;

[PluginMetadata(Id="LPLK_Models",Name="LPLK_Models",Version="0.1.5",Author="LPLK",Description="Modellauswahl und zeitlich begrenzte Bankmiete",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
 private ModuleSupport module=null!;
 private ModelSettings settings=new();
 private IInterfaceManager? interfaces;
 private IReadOnlyDictionary<string,Delegate>? registeredBank;
 private Dictionary<string,string> selections=[];
 private readonly ModelLife lives=new();
 private readonly Dictionary<string,ModelEntry> available=[];
 private HashSet<string> free=[];
 private string rotationDay="";
 private bool ready;
 private int errors,applied,botApplied,restored,mapEpoch;
 private string? failure;
 private long nextStatus;
 private long nextAnnouncement;
 private static readonly TimeZoneInfo Berlin=TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
 private static string Until(long unix)=>TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unix),Berlin).ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture)+" (Berlin)";
 private readonly Dictionary<ulong,long> welcomes=[];
 private readonly List<string> missing=[];
 private string SelectionPath=>Path.Combine(RoundFileStore.DataPath(Core),"selections.json");
 private bool Allowed(IPlayer p)=>module.Active&&settings.Enabled&&p is {IsValid:true,IsAuthorized:true,IsFakeClient:false};
 private static string Steam(IPlayer p)=>p.SteamID.ToString(CultureInfo.InvariantCulture);
 public override void Load(bool hotReload)
 {
  RoundFileStore.BeginLoad(Core,this);
  try{
   ChatTheme.Initialize("LPLK_Models",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
   module=new(Core,"LPLK_Models","Modelle","lplk.models.admin",Admin);
   settings=module.ReadSettings<ModelSettings>(ModelSettings.Validate);
   lives.ConfigureModels(settings.Models.Select(m=>m.Path));
   if(File.Exists(SelectionPath))selections=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(SelectionPath))??[];
   var migrated=ModelPolicy.MigrateSelections(selections,settings.Models);
   if(!selections.OrderBy(x=>x.Key).SequenceEqual(migrated.OrderBy(x=>x.Key))){selections=migrated;SaveSelection();}
   module.Active=true;
   Core.Event.OnPrecacheResource+=Precache;
   Core.Event.OnMapLoad+=MapLoad;Core.Event.OnMapUnload+=MapUnload;
   Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)Spawn(p,natural:true);return HookResult.Continue;});
   Core.Event.OnClientDisconnected+=Disconnected;
   Core.GameEvent.HookPost<EventPlayerConnectFull>(e=>{if(e.UserIdPlayer is {} p&&Allowed(p))Welcome(p);return HookResult.Continue;});
   Core.GameEvent.HookPost<EventRoundStart>(_=>{RoundAnnouncement();return HookResult.Continue;});
   Core.GameEvent.HookPost<EventRoundFreezeEnd>(_=>{EndCenter();return HookResult.Continue;});
   Core.GameEvent.HookPost<EventRoundEnd>(_=>{EndCenter();return HookResult.Continue;});
   Core.Event.OnTick+=Tick;
   // Hot reload deliberately waits for the next resource-precache boundary.
   WriteStatus();RoundFileStore.EndLoad();
  }catch{RoundFileStore.AbortLoad();throw;}
 }
 public override void UseSharedInterface(IInterfaceManager manager){interfaces=manager;registeredBank=null;Bank();}
 public override void ConfigureSharedInterface(IInterfaceManager manager)
 {
  module.ConfigureSharedInterface(manager);
  manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Models.v1",new(){["allowed"]=(Func<IPlayer,string,bool>)((p,c)=>Allowed(p)&&c=="sw_models")});
 }
 private IReadOnlyDictionary<string,Delegate>? Bank()
 {
  if(interfaces is null||Core.PluginManager.GetAllPluginStatuses().GetValueOrDefault("LPLK_Economy").ToString()!="Loaded"||!interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Economy.v1",out var api))return null;
  if(!ReferenceEquals(registeredBank,api)&&api.GetValueOrDefault("registerModels") is Action<string> register){register(JsonSerializer.Serialize(settings.Models.Where(m=>m.Enabled).Select(m=>new{m.Id,m.Name,m.DailyPrice,m.Requirement,m.Required})));registeredBank=api;}
  return api;
 }
 private long Progress(IPlayer p,ModelEntry model)=>model.Requirement==""?0:Bank()?.GetValueOrDefault("modelProgress") is Func<string,string,long> get?get(Steam(p),model.Requirement):0;
 private bool Unlocked(IPlayer p,ModelEntry model)=>model.Requirement==""||Progress(p,model)>=model.Required;
 private static string Requirement(ModelEntry m)=>m.Requirement switch{"RobberyOrders"=>"Bankraub starten","LottoTicketsBought"=>"Lose kaufen","BountiesSet"=>"Kopfgelder ausstellen",_=>"Keine Voraussetzung"};
 private long Lease(IPlayer p,string model)=>Bank()?.GetValueOrDefault("modelLease") is Func<string,string,long> get?get(Steam(p),model):0;
 private bool Owned(IPlayer p,string id)=>free.Contains(id)||Lease(p,id)>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
 private bool RentalReady()=>Bank()?.GetValueOrDefault("modelsReady") is Func<bool> get&&get();
 private long Price()=>Bank()?.GetValueOrDefault("modelPrice") is Func<long> get?get():0;
 private void Precache(IOnPrecacheResourceEvent e)
 {
  ready=false;available.Clear();free.Clear();missing.Clear();lives.Clear();centerRecipients.Clear();halloweenHtml="";mapEpoch++;
  if(!module.Active)return;
  foreach(var m in settings.Models.Where(m=>m.Enabled)){
   // Engine filesystem access is confined to map resource preparation.
   if(!Core.GameFileSystem.FileExists(m.Path+"_c","GAME")){missing.Add(m.Id);continue;}
   e.AddItem(m.Path);available.Add(m.Id,m);
  }
  rotationDay=ModelPolicy.Day(DateTimeOffset.UtcNow);free=ModelPolicy.FreeTeams(available.Values,rotationDay,settings.FreeCount,settings.Halloween);
  halloweenHtml=HalloweenBanner.Create(free.Count,free.Count(id=>available[id].Team==2),free.Count(id=>available[id].Team==3));
  ready=available.Count>0;
 }
 private void MapLoad(IOnMapLoadEvent e){ready=available.Count>0;WriteStatus();}
 private void MapUnload(IOnMapUnloadEvent e){ready=false;lives.Clear();centerRecipients.Clear();halloweenHtml="";mapEpoch++;announcementRound++;}
 private void Disconnected(IOnClientDisconnectedEvent e){lives.Remove(e.PlayerId);centerRecipients.Remove(e.PlayerId);}
 private static string TeamName(int team)=>team==2?"Terroristen":"CT";
 private string Key(IPlayer p,int team)=>ModelPolicy.SelectionKey(Steam(p),team);
 private void Spawn(IPlayer p,bool announce=false,bool natural=false,bool standard=false)
 {
  bool bot=p is {IsValid:true,IsFakeClient:true}&&(standard||settings.BotModels)&&!p.Controller.IsHLTV&&p.Controller.TeamNum is 2 or 3;
  bool human=p is {IsValid:true,IsAuthorized:true,IsFakeClient:false};
  if(!(bot||human)||!module.Active||(!standard&&(!settings.Enabled||!ready))||p.PlayerPawn is not {IsValid:true} pawn||p.Controller.ControllingBot||p.Controller.TeamNum is not (2 or 3))return;
  int epoch=mapEpoch,slot=p.PlayerID,team=p.Controller.TeamNum;string session=p.SessionId.ToString();ulong steam=p.SteamID;uint handle=Core.EntitySystem.GetRefEHandle(pawn).Raw;
  var owner=new ModelIdentity(slot,session,steam,handle,team,epoch);long revision=lives.Queue(slot);
  Core.Scheduler.NextWorldUpdate(()=>{
   var current=Core.PlayerManager.GetPlayer(slot);
   if(!lives.Current(slot,revision)||epoch!=mapEpoch||!module.Active||(!standard&&(!settings.Enabled||!ready))||current is not {IsValid:true}||current.Controller.IsHLTV||current.IsFakeClient!=bot||(!bot&&!current.IsAuthorized)||current.SessionId.ToString()!=session||current.SteamID!=steam||!current.IsAlive||current.Controller.ControllingBot||current.Controller.TeamNum!=team||current.PlayerPawn is not {IsValid:true} live||Core.EntitySystem.GetRefEHandle(live).Raw!=handle)return;
   try{
    lives.Capture(owner,live.GetSkeletonInstance()?.ModelState.ModelName.ToString()??"",natural);
    if(standard){
     if(lives.Original(owner) is not {} original)throw new InvalidOperationException("Ursprüngliches Spielermodell noch unbekannt.");
     live.SetModel(original);restored++;if(announce)current.SendLplkChat("Standardmodell ist jetzt aktiv.");return;
    }
    if(bot){var pool=free.Where(id=>ModelPolicy.ForTeam(available[id],team)).Order().ToArray();if(settings.BotModels&&pool.Length>0){var botId=pool[(slot+Math.Max(0,Core.EntitySystem.GetGameRules()?.TotalRoundsPlayed??0))%pool.Length];live.SetModel(available[botId].Path);botApplied++;}return;}
    if(selections.TryGetValue(Key(current,team),out var id)&&available.TryGetValue(id,out var model)&&ModelPolicy.ForTeam(model,team)&&Owned(current,id)){live.SetModel(model.Path);applied++;if(announce)current.SendLplkChat($"{model.Name} ist jetzt aktiv. Mit !tp ansehen.");}
   }catch(Exception ex){errors++;failure=ex.GetType().Name;Core.Logger.LogWarning(ex,"Modell konnte nicht angewendet werden");if(announce)current.SendLplkChat("Modell konnte jetzt nicht angewendet werden; Auswahl bleibt für den nächsten Spawn gespeichert.");}
  });
 }
 [Command("models")][CommandAlias("modelle")]
 public void OpenCommand(ICommandContext ctx){if(ctx.Sender is {} p&&p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}){if(Allowed(p))Open(p);else p.SendLplkChat("Modelle sind auf diesem Server ausgeschaltet.");}}
 private void Menu(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> entries,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,Allowed,entries,back,autoCloseSeconds:0);
 private void Open(IPlayer p)=>OpenTeam(p,p.Controller.TeamNum is 2 or 3?p.Controller.TeamNum:2);
 private void OpenTeam(IPlayer p,int team)
 {
  if(!ready){p.SendLplkChat("Modelle werden beim nächsten Mapstart vorbereitet.");return;}
  Menu(p,$"{(settings.Halloween?"Halloween":"Modelle")} · {TeamName(team)}",a=>new List<(string,Action<IPlayer>)>{("Kostenlos",b=>List(b,team,"Kostenlos",m=>free.Contains(m.Id))),("Mieten",b=>Categories(b,team)),("Meine Modelle",b=>List(b,team,"Meine Modelle",m=>Lease(b,m.Id)>DateTimeOffset.UtcNow.ToUnixTimeSeconds())),("Standard",b=>Standard(b,team)),(team==2?"CT-Auswahl":"T-Auswahl",b=>OpenTeam(b,team==2?3:2))},a=>a.ExecuteCommand("sw_menu"));
 }
 private void Standard(IPlayer p,int team)
 {
  selections.Remove(Key(p,team));SaveSelection();
  if(p.Controller.TeamNum==team&&p.IsAlive&&!p.Controller.ControllingBot)Spawn(p,true,standard:true);
  else p.SendLplkChat($"Standard für {TeamName(team)} gewählt; aktiv beim nächsten eigenen Spawn in diesem Team.");
  OpenTeam(p,team);
 }
 private void Categories(IPlayer p,int team)=>Menu(p,$"Mieten · {TeamName(team)}",_=>available.Values.Where(m=>ModelPolicy.ForTeam(m,team)).Select(m=>m.Category).Distinct().Order().Select(c=>(c,(Action<IPlayer>)(a=>List(a,team,c,m=>m.Category==c)))).ToArray(),a=>OpenTeam(a,team));
 private void List(IPlayer p,int team,string title,Func<ModelEntry,bool> filter)=>Menu(p,$"{title} · {TeamName(team)}",_=>available.Values.Where(m=>ModelPolicy.ForTeam(m,team)).Where(filter).OrderBy(m=>m.Name).Select(m=>(m.Name,(Action<IPlayer>)(a=>Detail(a,m.Id)))).ToArray(),a=>OpenTeam(a,team));
 private void Detail(IPlayer p,string id)
 {
  if(!ready||!available.TryGetValue(id,out var model))return;
  long expires=Lease(p,id);bool entitled=Owned(p,id);
  if(expires>DateTimeOffset.UtcNow.ToUnixTimeSeconds())p.SendLplkChat($"{model.Name} gemietet bis {Until(expires)}.");
  Menu(p,$"{model.Name} · {(model.Team==2?"T":"CT")}",a=>{
   var rows=new List<(string,Action<IPlayer>)>();
   if(entitled)rows.Add(("Auswählen",b=>Select(b,id)));
   if(model.Requirement!="")rows.Add(($"Fortschritt [{Math.Min(Progress(a,model),model.Required)}/{model.Required}]",b=>b.SendLplkChat($"{model.Name}: {Requirement(model)} [{Math.Min(Progress(b,model),model.Required)}/{model.Required}]. Miete [{model.DailyPrice:N0} $/Tag].")));
   if(RentalReady()&&Unlocked(a,model))foreach(int days in new[]{1,3,7,30}){int d=days;long price=model.DailyPrice;rows.Add(($"{d} Tage [{d*price:N0} $]",b=>Confirm(b,id,d,price)));}
   return rows;
  },a=>OpenTeam(a,model.Team));
 }
 private void Select(IPlayer p,string id)
 {
  if(!ready||!available.TryGetValue(id,out var m)||!Owned(p,id)){p.SendLplkChat("Modell nicht mehr verfügbar; Auswahl erneut öffnen.");return;}
  selections[Key(p,m.Team)]=id;SaveSelection();ApplySelected(p,m);OpenTeam(p,m.Team);
 }
 private void ApplySelected(IPlayer p,ModelEntry model)
 {
  if(p.IsAlive&&p.Controller.TeamNum==model.Team&&!p.Controller.ControllingBot&&p.PlayerPawn is {IsValid:true})Spawn(p,true);
  else p.SendLplkChat($"{model.Name} für {TeamName(model.Team)} gewählt. Aktiv beim nächsten eigenen Spawn in diesem Team.");
 }
 private void Confirm(IPlayer p,string id,int days,long price)
 {
  if(Bank()?.GetValueOrDefault("period") is not Func<long> period||!available.TryGetValue(id,out var m))return;
  long expectedPeriod=period();string token=Guid.NewGuid().ToString("N");
  Menu(p,$"{(m.Team==2?"T":"CT")} · {days} Tage [{days*price:N0} $]",_=>new (string,Action<IPlayer>)[]{("Mieten",a=>{
   if(!ready||!available.ContainsKey(id)||!RentalReady()){a.SendLplkChat("Modellmiete derzeit nicht verfügbar.");return;}
   try{
    if(Bank()?.GetValueOrDefault("rentModel") is not Func<IPlayer,string,int,string,long,long,long> rent)throw new InvalidOperationException("Bank nicht bereit.");
    long expiry=rent(a,id,days,token,expectedPeriod,price);
    selections[Key(a,m.Team)]=id;SaveSelection();a.SendLplkChat($"{m.Name} gemietet [{days*price:N0} $]. Bis {Until(expiry)}.");ApplySelected(a,m);OpenTeam(a,m.Team);
   }catch(Exception ex){a.SendLplkChat(ex is InvalidOperationException?ex.Message:"Modellmiete abgelehnt: "+ex.Message);}
  }),("Abbrechen",a=>Detail(a,id))},a=>Detail(a,id));
 }
 private void SaveSelection()=>AtomicJson.Write(SelectionPath,selections);
 private string Announcement()=>$"Halloween-Special: [{free.Count} kostenlose Gruselmodelle], getrennt für T und CT! Mit !modelle auswählen. Bekannte Figuren durch Bankraub, Lose oder Kopfgelder freischalten und mit Bankguthaben mieten.";
 private void Welcome(IPlayer p)
 {
  if(!settings.Halloween||!settings.AnnouncementsEnabled||Environment.TickCount64<welcomes.GetValueOrDefault(p.SteamID))return;
  ulong steam=p.SteamID;int slot=p.PlayerID;string session=p.SessionId.ToString();
  Core.Scheduler.DelayBySeconds(12,()=>{var current=Core.PlayerManager.GetPlayer(slot);if(current is null||!Allowed(current)||!ready||!settings.Halloween||!settings.AnnouncementsEnabled||current.SteamID!=steam||current.SessionId.ToString()!=session)return;welcomes[steam]=Environment.TickCount64+600000;current.SendLplkChat(Announcement());});
 }
 private void Announce()
 {
  if(!module.Active||!ready||!settings.Enabled||!settings.Halloween||!settings.AnnouncementsEnabled||Environment.TickCount64<nextAnnouncement)return;
  var people=Core.PlayerManager.GetAllPlayers().Where(Allowed).ToArray();if(people.Length==0)return;
  nextAnnouncement=Environment.TickCount64+settings.AnnouncementMinutes*60000L;
  foreach(var p in people)p.SendLplkChat(Announcement());
 }
 private bool HasOriginal(IPlayer p)=>p.PlayerPawn is {IsValid:true} pawn&&lives.Original(new(p.PlayerID,p.SessionId.ToString(),p.SteamID,Core.EntitySystem.GetRefEHandle(pawn).Raw,p.Controller.TeamNum,mapEpoch)) is not null;
 private void SetEnabled(bool enabled)
 {
  settings=settings with{Enabled=enabled};module.SaveSettings(settings);if(!enabled)EndCenter();
  foreach(var p in Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&p.IsAlive&&!p.Controller.IsHLTV&&!p.Controller.ControllingBot&&p.Controller.TeamNum is 2 or 3)){
   if(enabled)Spawn(p);else if(HasOriginal(p))Spawn(p,standard:true);
  }
  WriteStatus();
 }
 private void Admin(IPlayer p)=>module.Menu(p,$"Modelle · {(settings.Enabled?"Ein":"Aus")}",("Einschalten",a=>{SetEnabled(true);a.SendLplkChat(ready?"Modelle eingeschaltet.":"Modelle eingeschaltet; Vorbereitung beim nächsten Mapstart.");Admin(a);}),("Ausschalten",a=>{SetEnabled(false);a.SendLplkChat("Modelle ausgeschaltet. Standardmodelle werden wiederhergestellt.");Admin(a);}),($"Halloween: {(settings.Halloween?"Ja":"Nein")}",a=>{settings=settings with{Halloween=!settings.Halloween};module.SaveSettings(settings);if(!settings.Halloween)EndCenter();a.SendLplkChat("Neue Rotation ab dem nächsten Mapstart.");Admin(a);}),($"Bots: {(settings.BotModels?"Ja":"Nein")}",a=>{settings=settings with{BotModels=!settings.BotModels};module.SaveSettings(settings);if(settings.Enabled)foreach(var bot in Core.PlayerManager.GetAllPlayers().Where(b=>b.IsValid&&b.IsFakeClient&&!b.Controller.IsHLTV)){if(settings.BotModels)Spawn(bot);else if(HasOriginal(bot))Spawn(bot,standard:true);}Admin(a);}),("Vorschau",PreviewCenter),("Status",a=>{a.SendLplkChat($"Vorbereitet [{available.Count}] · Kostenlos [{free.Count}] · Fehlend [{missing.Count}].");Admin(a);}));
 [Command("lplk_models_toggle_probe")]
 public void ToggleProbe(ICommandContext ctx){if(ctx.Sender is not null||!settings.TestBotProbe||ctx.Args.Length!=1||ctx.Args[0] is not ("on" or "off")||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))return;SetEnabled(ctx.Args[0]=="on");ctx.Reply("LPLK_MODELS "+JsonSerializer.Serialize(new{toggle=settings.Enabled}));}
 private void Tick(){if(!module.Active||Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+10000;WriteStatus();}
 private object Status()=>new{version="0.1.5",at=DateTimeOffset.UtcNow,enabled=settings.Enabled,ready,models=available.Count,catalog=settings.Models.Length,free=free.Count,teams=new[]{2,3}.Select(t=>new{team=t,models=available.Values.Count(m=>m.Team==t),free=free.Count(id=>available[id].Team==t)}),rotationDay,halloween=settings.Halloween,botModels=settings.BotModels,freeModels=free.Select(id=>new{name=available[id].Name,team=available[id].Team}),missing,applied,botApplied,restored,errors,failure,bank=RentalReady(),dailyPrice=Price(),policy="RAM; RoundEnd+1000ms",modelChanges="immediate including Standard; own team only",resourceLoading="map precache only",centerAnnouncement=new{timing="round start; freeze only",durationMs=HalloweenBanner.DurationMs,stopOnFreezeEnd=true,menuPriority=true,shown=centerShown,htmlBytes=System.Text.Encoding.UTF8.GetByteCount(halloweenHtml)}};
 private void WriteStatus()=>DeferredJson.Queue(Path.Combine(RoundFileStore.DataPath(Core),"status.json"),Status());
 [Command("lplk_models")]
 public void StatusCommand(ICommandContext ctx){if(ctx.Sender is null){ctx.Reply("LPLK_MODELS "+JsonSerializer.Serialize(Status()));ctx.Reply("LPLK_MODELS "+JsonSerializer.Serialize(new{bots=Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&p.IsFakeClient&&!p.Controller.IsHLTV&&p.PlayerPawn is {IsValid:true}).Select(p=>new{slot=p.PlayerID,name=p.Name,team=p.Controller.TeamNum,model=p.PlayerPawn!.GetSkeletonInstance()?.ModelState.ModelName.ToString()}).ToArray()}));}}
 [Command("lplk_models_bots")]
 public void BotProbe(ICommandContext ctx){if(ctx.Sender is not null||!settings.TestBotProbe||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))return;foreach(var bot in Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&p.IsFakeClient&&!p.Controller.IsHLTV))Spawn(bot);}
 [Command("lplk_models_restore_probe")]
 public void RestoreProbe(ICommandContext ctx)
 {
  if(ctx.Sender is not null||!settings.TestBotProbe||!ready||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))return;
  foreach(var bot in Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&p.IsFakeClient&&!p.Controller.IsHLTV&&p.IsAlive&&p.Controller.TeamNum is 2 or 3&&p.PlayerPawn is {IsValid:true})){
   int slot=bot.PlayerID,team=bot.Controller.TeamNum;var pawn=bot.PlayerPawn!;uint handle=Core.EntitySystem.GetRefEHandle(pawn).Raw;string session=bot.SessionId.ToString();
   var owner=new ModelIdentity(slot,session,bot.SteamID,handle,team,mapEpoch);string? original=lives.Original(owner);string? before=pawn.GetSkeletonInstance()?.ModelState.ModelName.ToString();int health=pawn.Health;
   Spawn(bot,standard:true);
   Core.Scheduler.NextWorldUpdate(()=>{
    var current=Core.PlayerManager.GetPlayer(slot);
    if(current is not {IsValid:true,IsFakeClient:true}||current.Controller.IsHLTV||current.SessionId.ToString()!=session||current.Controller.TeamNum!=team||current.PlayerPawn is not {IsValid:true} live)return;
    string? after=live.GetSkeletonInstance()?.ModelState.ModelName.ToString();
    Core.Logger.LogInformation("LPLK_MODELS {Probe}",JsonSerializer.Serialize(new{restoreProbe=new{slot,team,before,original,after,restored=original is not null&&after==original,samePawn=Core.EntitySystem.GetRefEHandle(live).Raw==handle,sameHealth=live.Health==health,previousTeamValid=available.Values.Any(m=>m.Path==before&&m.Team==team)}}));
    Spawn(current);
   });
  }
 }
 public override void Unload()
 {
  EndCenter();module.Active=false;ready=false;mapEpoch++;lives.Clear();Core.Event.OnTick-=Tick;Core.Event.OnPrecacheResource-=Precache;Core.Event.OnMapLoad-=MapLoad;Core.Event.OnMapUnload-=MapUnload;Core.Event.OnClientDisconnected-=Disconnected;
  try{RoundData.Drain();DeferredJson.Drain();RoundFileStore.Drain();}finally{RoundFileStore.EndUnload();}
 }
}
