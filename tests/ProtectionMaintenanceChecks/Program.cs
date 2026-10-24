using System.Reflection;
using System.Text.Json;
using System.Numerics;
using LPLK.Common;
using LPLK_AntiCamp;
using LPLK_AntiTeamFlash;
using SwiftlyS2.Shared.Menus;
int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"ProtectionMaintenanceChecks","test-data");
MenuWorld World<T>(string id,T initial){var world=new MenuWorld{DataDirectory=Path.Combine(root,id),RejectEntityAccess=true};Directory.CreateDirectory(world.DataDirectory);File.WriteAllText(Path.Combine(world.DataDirectory,"settings.json"),JsonSerializer.Serialize(initial));world.Permissions.Add("lplk.admin");return world;}
T Settings<T>(object plugin) where T:class,new()=>((WatchedSettings<T>)plugin.GetType().GetField("config",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!).Current;
void Refresh(object plugin){foreach(string field in new[]{"nextStatus","nextCheck","nextReload"})plugin.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(plugin,0d);plugin.GetType().GetMethod("Tick")!.Invoke(plugin,null);}
string PathFor(MenuWorld world)=>Path.Combine(world.DataDirectory,"settings.json");
async Task Choose(MenuWorld world,string label){for(int i=0;i<100;i++){IMenuOption option;try{option=world.FindExact(label);}catch(InvalidOperationException){if(!world.Labels.Any(x=>x.Contains("Nächste")))throw;await world.Click("Nächste");continue;}await world.Click(option);return;}throw new Exception("Paging cycle");}
var campWorld=World("camp",new CampSettings{CampTime=75,WarningSeconds=22,FirstPunishDelay=7.25,PunishFrequency=44.5,MinHealthReserve=97,SlapDamage=83,CampCheckRadius=83.5});
var camp=new LPLK_AntiCamp.Plugin(campWorld.Core);camp.Load(true);Refresh(camp);Check(campWorld.EntityAccesses==0,"No native GameRules access before observed round");
camp.Command(campWorld.Context);Check(campWorld.Current!.Configuration.MaxVisibleItems==5&&campWorld.Labels.Any(l=>l.Contains("Campzeit")),"AntiCamp settings directly available");await Choose(campWorld,"Campzeit: 75s");
Check(!campWorld.Labels.Any(x=>x.Contains(">5<")||x.Contains(">10<")),"Camp choices cannot invalidate current warning");await Choose(campWorld,"75");Check(Settings<CampSettings>(camp).CampTime==75,"Existing custom camp time selectable");
await Choose(campWorld,$"Radius: {83.5:0.##}");await Choose(campWorld,"100");Check(Settings<CampSettings>(camp) is {CampCheckRadius:100,WarningSeconds:22,MinHealthReserve:97,PunishFrequency:44.5},"Radius edit preserves all custom rules");
await Choose(campWorld,"Schaden: 83");await Choose(campWorld,"100");await Choose(campWorld,"HP-Reserve: 97");await Choose(campWorld,"97");Check(Settings<CampSettings>(camp) is {SlapDamage:100,MinHealthReserve:97},"Full damage range with nonstandard reserve preserved");
await Choose(campWorld,$"Intervall: {44.5:0.##}s");await Choose(campWorld,44.5.ToString("0.##"));await Choose(campWorld,$"Erste Strafe: {7.25:0.##}s");await Choose(campWorld,"30");Check(Settings<CampSettings>(camp) is {FirstPunishDelay:30,PunishFrequency:44.5},"Full valid delay range preserves fractional interval");
await Choose(campWorld,"Art: Slap");await Choose(campWorld,"Markierung");Check(Settings<CampSettings>(camp).SlapORSlayPunishment==0,"Explicit punishment choice");await Choose(campWorld,"T / Geiseln: True");Check(!Settings<CampSettings>(camp).AllowTCampHostageMap,"Existing objective exemption configurable");
File.WriteAllText(PathFor(campWorld),JsonSerializer.Serialize(Settings<CampSettings>(camp) with{CampTime=180}));await Choose(campWorld,"T / Bombe gelegt: True");Check(Settings<CampSettings>(camp).CampTime==180&&!Settings<CampSettings>(camp).AllowTCampBombPlanted,"Menu refresh preserves unseen valid external edit");
File.WriteAllText(PathFor(campWorld),"{\"CampTime\":0}");await Choose(campWorld,"CT / Bombenmap: True");Check(Settings<CampSettings>(camp).AllowCTCampBombMap&&File.ReadAllText(PathFor(campWorld)).Contains(":0"),"Bad external config cannot be overwritten by menu");
File.WriteAllText(PathFor(campWorld),JsonSerializer.Serialize(Settings<CampSettings>(camp) with{PluginEnabled=false}));Refresh(camp);Check(!Settings<CampSettings>(camp).PluginEnabled&&campWorld.EntityAccesses==0,"Disabled AntiCamp avoids native entity access");
var flashWorld=World("flash",new FlashSettings{AccessPermission="custom.vip"});var flash=new LPLK_AntiTeamFlash.Plugin(flashWorld.Core);flash.Load(true);
var history=(FlashHistory)typeof(LPLK_AntiTeamFlash.Plugin).GetField("history",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(flash)!;
var identity=new FlashIdentity(1,7,10,2);Check(!history.ShouldClear(identity,false,10,15),"Start an allowed enemy flash");
flash.Command(flashWorld.Context);await Choose(flashWorld,"Eigenblendung: True");Check(!Settings<FlashSettings>(flash).FlashOwner&&Settings<FlashSettings>(flash).AccessPermission=="custom.vip","Flash option keeps custom permission");Check(!history.ShouldClear(identity,true,11,1),"Menu change cannot erase enemy-flash history");
File.WriteAllText(PathFor(flashWorld),JsonSerializer.Serialize(Settings<FlashSettings>(flash) with{Enabled=false}));Refresh(flash);Check(!Settings<FlashSettings>(flash).Enabled&&!history.ShouldClear(identity,true,12,1),"External disable preserves current overlap");
File.WriteAllText(PathFor(flashWorld),"{\"AccessPermission\":\"bad right\"}");Refresh(flash);await Choose(flashWorld,"Eigenblendung: False");Check(!Settings<FlashSettings>(flash).FlashOwner&&!history.ShouldClear(identity,true,13,1),"Invalid edit retains config and flash history");
File.WriteAllText(PathFor(flashWorld),JsonSerializer.Serialize(Settings<FlashSettings>(flash) with{AccessPermission="other.vip",Enabled=true}));await Choose(flashWorld,"Eigenblendung: False");Check(Settings<FlashSettings>(flash) is {Enabled:true,AccessPermission:"other.vip",FlashOwner:true},"Menu edits latest corrected config");
var stale=flashWorld.FindExact("Aktiv: True");flashWorld.Session++;await flashWorld.Click(stale);Check(Settings<FlashSettings>(flash).Enabled,"Old session rejected");flash.Command(flashWorld.Context);var revoked=flashWorld.FindExact("Aktiv: True");flashWorld.Permissions.Clear();await flashWorld.Click(revoked);Check(Settings<FlashSettings>(flash).Enabled,"Permission revoked after opening");flashWorld.Permissions.Add("lplk.admin");
foreach(var item in new (object Plugin,MenuWorld World)[]{(camp,campWorld),(flash,flashWorld)}){
 string config=PathFor(item.World),good=File.ReadAllText(config);
 foreach(string bad in new[]{"null","[]","{\"Unknown\":true}",new string(' ',65537)}){File.WriteAllText(config,bad);Refresh(item.Plugin);Check(File.ReadAllText(Path.Combine(item.World.DataDirectory,"status.json")).Contains("reloadError")&&!File.ReadAllText(Path.Combine(item.World.DataDirectory,"status.json")).Contains("\"reloadError\": null"),"Malformed edit visible, still loaded");}
 File.WriteAllText(config,good);Refresh(item.Plugin);Check(File.ReadAllText(Path.Combine(item.World.DataDirectory,"status.json")).Contains("\"reloadError\": null"),"Corrected file recovers");
 string status=Path.Combine(item.World.DataDirectory,"status.json");File.Delete(status);Directory.CreateDirectory(status);Refresh(item.Plugin);Check(Directory.Exists(status),"Status write failure does not break tick");Directory.Delete(status);Refresh(item.Plugin);Check(File.Exists(status),"Status writing recovers");
 foreach(var menu in item.World.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"At most five visible options");foreach(var back in menu.Options.Where(x=>x.Text.Contains("Zurück")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Back unnumbered");}
}
camp.Unload();flash.Unload();Check(history.Count==0,"Unload clears flash history");
Console.WriteLine($"ProtectionMaintenanceChecks: {checks} checks passed; real menu callbacks/config files, no synthetic entity spawn.");
