using System.Text.Json;
using LPLK_Models;
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
var settings=JsonSerializer.Deserialize<ModelSettings>(File.ReadAllText(args[0]))!;
Check(!new ModelSettings().Enabled,"models off by default");
Check(!settings.Enabled,"deployment catalog defaults off");
ModelSettings.Validate(settings);
var models=settings.Models;
Check(models.Length==204,"reviewed AG2 catalog");
Check(models.Count(m=>m.Team==2)==103&&models.Count(m=>m.Team==3)==101,"exclusive pools");
Check(models.All(m=>m.Workshop=="3803459186"),"only replacement pack");
Check(models.All(m=>!ModelPolicy.Vehicle(m.Path)),"no vehicles");
Check(models.All(m=>!ModelPolicy.ForTeam(m,1)&&!ModelPolicy.ForTeam(m,0)),"no spectator team");
Check(models.Where(m=>Path.GetFileNameWithoutExtension(m.Path).EndsWith("_ct")).All(m=>m.Team==3),"CT variants");
var free=ModelPolicy.FreeTeams(models,"2026-10-27",10,true);
Check(free.Count==10,"ten total");
foreach(int team in new[]{2,3}){
 var pool=models.Where(m=>free.Contains(m.Id)&&ModelPolicy.ForTeam(m,team)).ToArray();
 Check(pool.Length==5,"five per team");
 Check(pool.All(m=>m.Team==team&&m.Requirement==""&&m.DailyPrice==100000),"bot and player free eligibility");
 Check(pool.Count(ModelPolicy.HalloweenModel)==Math.Min(5,models.Count(m=>m.Team==team&&m.Enabled&&m.Requirement==""&&m.DailyPrice<=100000&&ModelPolicy.HalloweenModel(m))),"available Halloween models preferred, other models fill five slots");
}
Check(free.SetEquals(ModelPolicy.FreeTeams(models,"2026-10-27",10,true)),"stable for map");
Check(!free.SetEquals(ModelPolicy.FreeTeams(models,"2026-10-28",10,true)),"next-day rotation");
string steam="76561198000000000",t=ModelPolicy.SelectionKey(steam,2),ct=ModelPolicy.SelectionKey(steam,3);
var tmodel=models.First(m=>m.Team==2);var ctmodel=models.First(m=>m.Team==3);
var migrated=ModelPolicy.MigrateSelections(new Dictionary<string,string>{{steam,ctmodel.Id}},models);
Check(!migrated.ContainsKey(steam)&&migrated[ct]==ctmodel.Id&&!migrated.ContainsKey(t),"legacy migrates to assigned team");
var preserved=ModelPolicy.MigrateSelections(new Dictionary<string,string>{{steam,ctmodel.Id},{ct,"existing"},{t,tmodel.Id}},models);
Check(preserved[ct]=="existing"&&preserved[t]==tmodel.Id,"existing team preferences win");
preserved.Remove(t);Check(preserved[ct]=="existing","Standard T preserves CT");
Check(ModelPolicy.MigrateSelections(new Dictionary<string,string>{{steam,"unknown"}},models)[steam]=="unknown","unknown historical selection retained");
var life=new ModelLife();life.ConfigureModels(models.Select(m=>m.Path));var id=new ModelIdentity(1,"session",1,123,2,1);
const string vanilla="characters/models/tm_phoenix/tm_phoenix.vmdl";
life.Capture(id,vanilla,true);Check(life.Original(id)==vanilla,"remember actual default");
life.Capture(id,tmodel.Path,false);Check(life.Original(id)==vanilla,"custom model cannot replace default");
life.Capture(id,tmodel.Path,true);Check(life.Original(id)==vanilla,"custom spawn cannot poison baseline");
foreach(var model in models){life.Capture(id,model.Path.ToUpperInvariant(),true);Check(life.Original(id)==vanilla,"every replacement model excluded from vanilla baseline");}
foreach(var changed in new[]{id with{Session="other"},id with{Steam=2},id with{Pawn=124},id with{Team=3},id with{Map=2},id with{Slot=2}})
 Check(life.Original(changed)==null,"baseline not reused across identity");
life.Capture(id,"characters/new_default.vmdl",true);Check(life.Original(id)=="characters/new_default.vmdl","natural respawn refreshes baseline");
long select=life.Queue(1),standard=life.Queue(1);Check(!life.Current(1,select)&&life.Current(1,standard),"Standard supersedes pending selection");
long selectAgain=life.Queue(1);Check(!life.Current(1,standard)&&life.Current(1,selectAgain),"new selection supersedes pending Standard");
life.Remove(1);Check(life.Original(id)==null&&!life.Current(1,selectAgain),"disconnect invalidates state");
life.Capture(id,vanilla,true);long pending=life.Queue(1);life.Clear();Check(life.Original(id)==null&&!life.Current(1,pending),"map unload invalidates state");
Check(life.Queue(1)!=pending,"revision not reused after map unload");
Console.WriteLine(JsonSerializer.Serialize(new{passed=count,models=models.Length,teams=new[]{103,101},free=new[]{5,5}}));
