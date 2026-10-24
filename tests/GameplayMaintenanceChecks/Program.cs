using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Parachute;
using LPLK_BombWires;
using SwiftlyS2.Shared.Menus;

int checks=0;void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
void Reject(Action run,string message){bool failed=false;try{run();}catch(InvalidDataException){failed=true;}Check(failed,message);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"GameplayMaintenanceChecks","test-data");Directory.CreateDirectory(root);
string path=Path.Combine(root,"watch.json");File.WriteAllText(path,JsonSerializer.Serialize(new ParachuteSettings()));
var watch=new WatchedSettings<ParachuteSettings>(path,ParachuteSettings.Validate);
Check(watch.Current.FallSpeed==85&&!watch.Reload(),"Stable file does not replace settings");
foreach(string bad in new[]{"null","{","{\"FallSpeed\":0}","{\"FallSpeed\":501}","{\"BrakePerTick\":-1}","{\"BrakePerTick\":101}","{\"UsePermission\":null}","{\"UsePermission\":\"bad right\"}","{\"Unknown\":1}","[]",new string(' ',65537)})
{
 File.WriteAllText(path,bad);Check(!watch.Reload()&&watch.Error is not null&&watch.Current.FallSpeed==85,"Invalid external edit retains last rules");
 Reject(()=>watch.Change(s=>s with{Enabled=false}),"Menu cannot overwrite invalid external edit");Check(File.ReadAllText(path)==bad,"Bad input retained for correction");
}
File.Delete(path);Check(!watch.Reload()&&watch.Current.Enabled&&watch.Error is not null,"Deleted config does not reset running rules");
File.WriteAllText(path,JsonSerializer.Serialize(new ParachuteSettings{FallSpeed=95,UsePermission="vip",BrakePerTick=42}));
Check(watch.Reload()&&watch.Error is null&&watch.Current.FallSpeed==95,"Corrected config recovers");
var beforeTimestamp=File.GetLastWriteTimeUtc(path);File.WriteAllText(path,File.ReadAllText(path).Replace("95","75"));File.SetLastWriteTimeUtc(path,beforeTimestamp);
Check(watch.Reload()&&watch.Current.FallSpeed==75,"Same length and timestamp content edit seen");
File.WriteAllText(path,JsonSerializer.Serialize(watch.Current with{FallSpeed=123}));
watch.Change(s=>s with{Enabled=false});Check(!watch.Current.Enabled&&watch.Current.FallSpeed==123&&watch.Current.UsePermission=="vip"&&watch.Current.BrakePerTick==42,"Menu edit preserves latest external fields");
var persisted=File.ReadAllText(path);Reject(()=>watch.Change(s=>s with{FallSpeed=0}),"Bad menu values rejected");Check(File.ReadAllText(path)==persisted,"Bad menu edit leaves disk unchanged");
bool race=false;try{watch.Change(s=>{File.WriteAllText(path,JsonSerializer.Serialize(s with{FallSpeed=144}));return s with{FallSpeed=155};});}catch(IOException){race=true;}
Check(race&&File.ReadAllText(path).Contains("144"),"Detected external edit during menu mutation preserved");

var wire=new WireRound();Check(!wire.Plant(0)&&!wire.Planted,"Hot-load planted event without observed begin ignored");
for(int color=0;color<4;color++)
{
 wire.Begin(42);int generation=wire.Generation;Check(!wire.Choose(43,generation,color),"Other planter rejected");
 Check(wire.Choose(42,generation,color),"Planter chooses color");Check(wire.Plant((color+1)%4),"Observed planting finalized");
 Check(!wire.Plant((color+2)%4)&&wire.Color==color,"Duplicate planted event cannot change chosen color");
 Check(wire.Cut(generation,color)==true&&wire.Cut(generation,color) is null,"One successful resolution");
 wire.Begin(42);generation=wire.Generation;Check(wire.Plant(color)&&wire.Color==color,"No-choice fallback retained");
 Check(wire.Cut(generation,(color+1)%4)==false,"Wrong wire still explodes");
 wire.Reset();Check(!wire.Plant(color)&&wire.Cut(generation,color) is null,"Abort/round reset invalidates pending actions");
}
wire.Begin(42);Check(!wire.Plant(-1)&&!wire.Plant(4)&&wire.Planting,"Invalid fallback cannot arm bomb");
Check(FallPolicy.Brake(-91,new())==-76&&FallPolicy.Brake(-20,new())==-85,"Original v5 tick behavior preserved");
Check(FallPolicy.ReleaseGravity(.1f,.4f)==.4f&&FallPolicy.ReleaseGravity(.2f,.4f)==.2f,"Original and foreign gravity preserved");

async Task Choose(MenuWorld world,string label)
{
 for(int i=0;i<60;i++)
 {
  IMenuOption option;try{option=world.FindExact(label);}catch(InvalidOperationException){if(!world.Labels.Any(x=>x.Contains("Nächste")))throw;await world.Click("Nächste");continue;}
  await world.Click(option);return;
 }
 throw new Exception("Pagination cycle");
}
T Settings<T>(object plugin) where T:class,new() =>((WatchedSettings<T>)plugin.GetType().GetField("config",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!).Current;

void Refresh(object plugin){plugin.GetType().GetField("nextConfigCheck",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,0L);plugin.GetType().GetMethod("Tick")!.Invoke(plugin,null);}
string SettingsPath(MenuWorld world)=>Path.Combine(world.DataDirectory,"settings.json");
var parachuteWorld=new MenuWorld{DataDirectory=Path.Combine(root,"parachute")};Directory.CreateDirectory(parachuteWorld.DataDirectory);parachuteWorld.Permissions.Add("lplk.admin");
File.WriteAllText(SettingsPath(parachuteWorld),JsonSerializer.Serialize(new ParachuteSettings{FallSpeed=123,BrakePerTick=42,UsePermission="vip"}));
var parachute=new LPLK_Parachute.Plugin(parachuteWorld.Core);parachute.Load(false);parachute.Command(parachuteWorld.Context);
Check(parachuteWorld.Current!.Configuration.MaxVisibleItems==5&&parachuteWorld.Labels.Any(l=>l.Contains("Falltempo")),"Parachute settings directly available");await Choose(parachuteWorld,"Falltempo: 123");await Choose(parachuteWorld,"123");
Check(Settings<ParachuteSettings>(parachute).FallSpeed==123,"Custom existing speed remains selectable");await Choose(parachuteWorld,"Falltempo: 123");await Choose(parachuteWorld,"100");
Check(Settings<ParachuteSettings>(parachute) is {FallSpeed:100,BrakePerTick:42,UsePermission:"vip"},"Menu speed change preserves unrelated settings");
await Choose(parachuteWorld,"Bremsschritt: 42");await Choose(parachuteWorld,"0");Check(Settings<ParachuteSettings>(parachute).BrakePerTick==0,"Immediate brake selectable");
var stale=parachuteWorld.FindExact("Gleichmäßig: True");parachuteWorld.Session++;await parachuteWorld.Click(stale);Check(Settings<ParachuteSettings>(parachute).Linear,"Old session cannot mutate");
parachute.Command(parachuteWorld.Context);
var forbidden=parachuteWorld.FindExact("Gleichmäßig: True");parachuteWorld.Permissions.Clear();await parachuteWorld.Click(forbidden);Check(Settings<ParachuteSettings>(parachute).Linear,"Revoked permission cannot mutate");parachuteWorld.Permissions.Add("lplk.admin");
File.WriteAllText(SettingsPath(parachuteWorld),"{");await parachuteWorld.Click(forbidden);Check(Settings<ParachuteSettings>(parachute).Linear&&File.ReadAllText(SettingsPath(parachuteWorld))=="{","Invalid external file remains intact after menu attempt");
Refresh(parachute);Check(File.ReadAllText(Path.Combine(parachuteWorld.DataDirectory,"status.json")).Contains("JsonException"),"Reload problem visible in status");
File.WriteAllText(SettingsPath(parachuteWorld),JsonSerializer.Serialize(new ParachuteSettings{FallSpeed=111,Enabled=false,UsePermission="vip"}));Refresh(parachute);
Check(Settings<ParachuteSettings>(parachute) is {Enabled:false,FallSpeed:111},"Valid external parachute reload applies");
Check(!File.ReadAllText(Path.Combine(parachuteWorld.DataDirectory,"status.json")).Contains("JsonException"),"Status recovers after correction");
var bombWorld=new MenuWorld{DataDirectory=Path.Combine(root,"bombwires")};Directory.CreateDirectory(bombWorld.DataDirectory);bombWorld.Permissions.Add("lplk.admin");File.WriteAllText(SettingsPath(bombWorld),JsonSerializer.Serialize(new WireSettings()));
var bomb=new LPLK_BombWires.Plugin(bombWorld.Core);bomb.Load(false);bomb.Command(bombWorld.Context);
var liveRound=(WireRound)typeof(LPLK_BombWires.Plugin).GetField("round",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(bomb)!;liveRound.Begin(42);liveRound.Choose(42,liveRound.Generation,2);liveRound.Plant(0);
await Choose(bombWorld,"Menüzeit: 5s");await Choose(bombWorld,"10s");
Check(Settings<WireSettings>(bomb).MenuSeconds==10&&liveRound.Planted&&liveRound.Color==2,"Admin timing edit preserves planted bomb and chosen wire");
await Choose(bombWorld,"Mischen: False");Check(Settings<WireSettings>(bomb).RandomizeOrder&&liveRound.Color==2,"Random display order does not reroll wire");
File.WriteAllText(SettingsPath(bombWorld),JsonSerializer.Serialize(Settings<WireSettings>(bomb) with{MenuSeconds=3}));Refresh(bomb);Check(Settings<WireSettings>(bomb).MenuSeconds==3&&liveRound.Color==2,"External timing edit preserves active round");
File.WriteAllText(SettingsPath(bombWorld),"{\"MenuSeconds\":0}");Refresh(bomb);Check(Settings<WireSettings>(bomb).MenuSeconds==3&&liveRound.Planted,"Bad external timing retains round and rules");
File.WriteAllText(SettingsPath(bombWorld),JsonSerializer.Serialize(Settings<WireSettings>(bomb) with{Enabled=false}));Refresh(bomb);Check(!Settings<WireSettings>(bomb).Enabled&&!liveRound.Planted,"Disabling invalidates current wire state");
foreach(var world in new[]{parachuteWorld,bombWorld})foreach(var menu in world.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"Compact menu at most five entries");foreach(var back in menu.Options.Where(x=>x.Text.Contains("Zurück")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Return has no number");}
foreach(var item in new (object Plugin,MenuWorld World)[]{(parachute,parachuteWorld),(bomb,bombWorld)})
{
 string status=Path.Combine(item.World.DataDirectory,"status.json");File.Delete(status);Directory.CreateDirectory(status);
 Refresh(item.Plugin);Check(Directory.Exists(status),"Read-only status target does not break game tick");Directory.Delete(status);Refresh(item.Plugin);Check(File.Exists(status),"Status recovers when writable");
}
parachute.Unload();bomb.Unload();
Console.WriteLine($"GameplayMaintenanceChecks: {checks} checks passed; no native entity spawning.");
