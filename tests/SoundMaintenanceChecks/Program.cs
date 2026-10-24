using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Sounds;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object? v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
object? Invoke(object p,string n,params object?[] a)=>p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,a);
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"SoundMaintenanceChecks","test-data")};
Directory.CreateDirectory(w.DataDirectory);File.Copy(Path.Combine(AppContext.BaseDirectory,"catalog.json"),Path.Combine(w.DataDirectory,"catalog.json"),true);
string cfg=Path.Combine(w.DataDirectory,"addons","swiftlys2","configs","plugins","LPLK_Sounds","config.json"),prefs=Path.Combine(w.DataDirectory,"sound-listening.json"),status=Path.Combine(w.DataDirectory,"status.json");Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
void Save(SoundsConfig c){var node=JsonSerializer.SerializeToNode(c)!;node["Extra"]=new JsonObject{["keep"]=42};File.WriteAllText(cfg,node.ToJsonString());}
Save(new(){RequireAddonsManager=false,AutoCatalogFromAddon=false,BroadcastCooldownSeconds=2.25});
File.WriteAllText(prefs,"{\"76561198000000000\":{\"Enabled\":false,\"VolumePercent\":37,\"HearAlive\":false,\"HearDead\":true,\"MutedSounds\":[],\"BlockedSoundSenders\":[]}}");string prefBytes=File.ReadAllText(prefs);
int resourceReads=0;var core=Proxy.Make<ISwiftlyCore>((m,a)=>{
 if(m.Name=="get_GameFileSystem")return Proxy.Make(m.ReturnType,(op,v)=>{resourceReads++;return op.Name switch{"FileExists"=>true,"GetFileSize"=>Proxy.Empty(op.ReturnType),"ReadFile"=>null,_=>Proxy.Empty(op.ReturnType)};});
 return m.Invoke(w.Core,a);
});
w.Permissions.Add("lplk.admin");var p=new Plugin(core);p.Load(false);
SoundsConfig Config()=>Field<SoundsConfig>(p,"config");
void Refresh(){Set(p,"nextConfig",0L);Set(p,"nextStatus",0L);Invoke(p,"OnTick");}
List<string> added=[];var precache=Proxy.Make<IOnPrecacheResourceEvent>((m,a)=>{if(m.Name=="AddItem")added.Add((string)a![0]!);return Proxy.Empty(m.ReturnType);});
void Precache()=>Invoke(p,"OnPrecache",precache);
async Task Click(string label)=>await w.Click(w.FindExact(label));
void Input(string value,bool valid=true)=>Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,value)==valid,"Input "+value);
void Open(){p.Status(w.Context);}
ICommandContext ConsoleCommand(string value)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch{"get_IsSentByPlayer"=>false,"get_Args"=>new[]{value},"Reply"=>Reply((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
object? Reply(string value){w.Messages.Add(value);return null;}
Precache();int total=Field<AssetCatalog>(p,"catalog").Sounds.Count;Check(total==88&&Field<HashSet<string>>(p,"precachedSounds").Count==88&&added.All(x=>x!=SoundResources.LegacyLibrary),"Existing 88-sound catalog safely precached");
var rounds=Field<SoundRoundLimit>(p,"soundRoundLimit");var sends=Field<SoundSendLimit>(p,"soundSendLimit");rounds.TryStart(w.Player.SteamID,()=>true);sends.TryStart(w.Player.SteamID,w.Player.SteamID+1,()=>true);Set(p,"lastBroadcast",12345L);
SoundBroadcast Active(){var b=SoundBroadcast.Start("test",Environment.TickCount64,600,[],_=>1,(_,_,_)=>{});Set(p,"broadcast",b);return b;}
var active=Active();string good=File.ReadAllText(cfg);File.WriteAllText(cfg,"\n"+good+"\n");Refresh();Check(ReferenceEquals(Field<SoundBroadcast?>(p,"broadcast"),active),"Format-only change leaves playback running");
Open();await Click("Soundpause: "+2.25.ToString("0.###")+"s");foreach(string bad in new[]{"0","60.01","NaN","Infinity","bad","1e100"})Input(bad,false);
var newer=SoundConfigFile.Clone(Config());newer.DeadPlayersOnlyToDead=false;Save(newer);Input("59,75");Check(Config().BroadcastCooldownSeconds==59.75&&!Config().DeadPlayersOnlyToDead&&Field<SoundBroadcast?>(p,"broadcast") is null,"Stale numeric menu preserves latest other field and stops old playback");
Check((int)JsonNode.Parse(File.ReadAllText(cfg))!["Extra"]!["keep"]! ==42,"Extra config fields retained");
await Click("Soundpause: "+59.75.ToString("0.###")+"s");Input("1");await Click("Soundpause: 1s");Input("60");Check(Config().BroadcastCooldownSeconds==60,"Full existing cooldown range reachable");
Check(rounds.Remaining(w.Player.SteamID)==2&&sends.TryStart(w.Player.SteamID,w.Player.SteamID+1,()=>throw new Exception("Already-used private budget"))==SoundStartResult.LimitReached&&Field<long?>(p,"lastBroadcast")==12345L,"Settings edits retain both round budgets and cooldown history");
good=File.ReadAllText(cfg);active=Active();
foreach(string bad in new[]{"null","[]","{\"WorkshopIds\":null}","{\"BroadcastCooldownSeconds\":0}","{\"Enabled\":true,\"enabled\":false}",new string(' ',SoundConfigFile.MaxBytes+1)})
{File.WriteAllText(cfg,bad);Refresh();Check(Config().BroadcastCooldownSeconds==60&&ReferenceEquals(Field<SoundBroadcast?>(p,"broadcast"),active)&&Field<string?>(p,"reloadError") is not null,"Invalid config leaves active rules and sound");p.DeadOnlyCommand(ConsoleCommand("on"));Check(File.ReadAllText(cfg)==bad&&!Config().DeadPlayersOnlyToDead,"Console refuses entire invalid configuration");}
File.Delete(cfg);Refresh();Check(!File.Exists(cfg)&&ReferenceEquals(Field<SoundBroadcast?>(p,"broadcast"),active),"Missing configuration not recreated during runtime");File.WriteAllText(cfg,good);Refresh();Check(Field<string?>(p,"reloadError") is null,"Valid file recovers");
DateTime stamp=File.GetLastWriteTimeUtc(cfg);newer=SoundConfigFile.Clone(Config());newer.BroadcastCooldownSeconds=3.5;Save(newer);File.SetLastWriteTimeUtc(cfg,stamp);Refresh();Check(Config().BroadcastCooldownSeconds==3.5,"Same timestamp edit reloaded");
newer=SoundConfigFile.Clone(Config());newer.BroadcastCooldownSeconds=4.5;Save(newer);p.DeadOnlyCommand(ConsoleCommand("on"));Check(Config().DeadPlayersOnlyToDead&&Config().BroadcastCooldownSeconds==4.5,"Console reloads and preserves independent current fields");
active=Active();p.DeadOnlyCommand(ConsoleCommand("on"));Check(ReferenceEquals(Field<SoundBroadcast?>(p,"broadcast"),active),"No-op console edit leaves sound playing");
Open();await Click("Soundpause: "+4.5.ToString("0.###")+"s");var input=(InputMenuOption)w.Current!.Options[0];good=File.ReadAllText(cfg);w.Permissions.Clear();input.SetValue(w.Player,"7");Check(File.ReadAllText(cfg)==good,"Revoked input rejected");w.Permissions.Add("lplk.admin");w.Session++;input.SetValue(w.Player,"8");Check(File.ReadAllText(cfg)==good,"Reconnected slot rejects captured input");
// Changed assets are blocked until the true precache boundary, never added on a tick.
int beforeReads=resourceReads,beforeAdded=added.Count;var catalog=Field<AssetCatalog>(p,"catalog");newer=SoundConfigFile.Clone(Config());newer.WorkshopIds=["123456"];Save(newer);Refresh();Check(Field<bool>(p,"assetReloadPending")&&ReferenceEquals(catalog,Field<AssetCatalog>(p,"catalog"))&&added.Count==beforeAdded&&resourceReads==beforeReads,"Asset edit remains pending without filesystem/precache work on tick");
Check(((string?)Invoke(p,"CommonUnavailable"))?.Contains("Mapstart")==true,"Pending assets block playback");Precache();Check(!Field<bool>(p,"assetReloadPending")&&Field<HashSet<string>>(p,"precachedSounds").Count==88,"Real precache boundary activates resource configuration");
newer=SoundConfigFile.Clone(Config());newer.AutoCatalogFromAddon=true;Save(newer);Refresh();Check(Field<bool>(p,"assetReloadPending")&&Field<WorkshopCatalogSync?>(p,"workshopCatalog") is null,"Catalog source switch waits");Precache();Check(!Field<bool>(p,"assetReloadPending")&&Field<WorkshopCatalogSync?>(p,"workshopCatalog") is not null&&Field<HashSet<string>>(p,"precachedSounds").Count==88,"Workshop source activates only at precache with existing fallback behavior");
var store=new SoundConfigFile(cfg);string unchanged=File.ReadAllText(cfg);bool rejected=false;try{store.Change(c=>{c.WorkshopIds[0]="invalid";});}catch(InvalidDataException){rejected=true;}Check(rejected&&File.ReadAllText(cfg)==unchanged&&store.Current.WorkshopIds[0]=="123456","Rejected mutable edit cannot leak into accepted config");
rejected=false;try{store.Change(c=>{File.AppendAllText(cfg,"\n");c.Enabled=false;});}catch(IOException){rejected=true;}Check(rejected&&store.Current.Enabled&&File.ReadAllText(cfg)==unchanged+"\n","Intervening external edit detected");
File.Delete(status);Directory.CreateDirectory(status);newer=SoundConfigFile.Clone(Config());newer.Enabled=false;Save(newer);active=Active();Refresh();Check(Field<SoundBroadcast?>(p,"broadcast") is null&&Field<bool>(p,"statusWriteFailed"),"Status I/O failure cannot stop disabled-rule cleanup");Directory.Delete(status);Refresh();Check(File.Exists(status)&&!Field<bool>(p,"statusWriteFailed"),"Status writing recovers");
Check(File.ReadAllText(prefs)==prefBytes&&!Field<SoundPreferenceStore>(p,"soundPreferences").Get(w.Player.SteamID.ToString()).Enabled,"Listener preferences byte-identical");
foreach(var menu in w.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"Admin menu at most five rows");foreach(var back in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Back unnumbered");}
p.Unload();Check(w.EntityAccesses==0,"No native entities used");
Console.WriteLine($"SoundMaintenanceChecks: {checks} passed; actual menu/console/reload/asset-boundary paths with a fake engine. No audible client acceptance.");
