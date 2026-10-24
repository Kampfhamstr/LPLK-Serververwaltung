extern alias zombie;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.ExceptionServices;
using LPLK_Surf;
using LPLK.Common;
using Z=zombie::LPLK_Zombie;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Microsoft.Data.Sqlite;

if(args.Contains("--api"))
{
    foreach(var type in typeof(ISwiftlyCore).Assembly.GetTypes().Where(t=>t.FullName!.Contains("GameHooks")&&(t.Name.Contains("CanUse")||t.Name.Contains("TakeDamage"))))
        Console.WriteLine(type.FullName+" "+string.Join(";",type.GetProperties().Select(p=>p.PropertyType.Name+" "+p.Name)));
    return;
}
int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception e)when(e is JsonException or IOException or InvalidDataException or UnauthorizedAccessException){rejected=true;}Check(rejected,name);}
var flags=BindingFlags.NonPublic|BindingFlags.Instance;
void Set(object p,string n,object? value)=>p.GetType().GetField(n,flags)!.SetValue(p,value);
T Get<T>(object p,string n)=>(T)p.GetType().GetField(n,flags)!.GetValue(p)!;
object? Call(object p,string n,params object?[] values){try{return p.GetType().GetMethod(n,flags)!.Invoke(p,values);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
string workspace=Path.GetFullPath(args[0]),build=Path.GetFullPath(args[1]);
string sample=Path.Combine(workspace,"private/fleet-mode-review"),root=Path.Combine(build,"ModeReplacementChecks/current");Directory.CreateDirectory(root);
string surfText=File.ReadAllText(Path.Combine(sample,"f222196f/configs/plugins/swiftsurf/config.jsonc"));
var surf=SurfConfiguration.Read(surfText);Check(surf.Enabled&&surf.Timer.HudIntervalMs==25&&surf.Server.NoBlock,"Live surf profile retains effective rules");
var options=new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};
foreach(string key in new[]{"Timer","Server","MapVote","Rankings","AdminSteamIds"}){var node=JsonNode.Parse(surfText,documentOptions:options)!;node["SwiftSurf"]![key]=null;Reject(()=>SurfConfiguration.Read(node.ToJsonString()),"Reject null "+key);}
foreach((string group,string key,int bad) in new[]{("Timer","MaxRunMinutes",0),("Server","MaxVelocity",-1),("MapVote","MapChangeMaxAttempts",0),("MapVote","RtvPercentage",101)}){var node=JsonNode.Parse(surfText,documentOptions:options)!;node["SwiftSurf"]![group]![key]=bad;Reject(()=>SurfConfiguration.Read(node.ToJsonString()),"Reject invalid "+key);}
Check(SurfConfiguration.Read("// comment\n"+surfText).Server.AirAccelerate==150,"JSONC supported");
var scope=new ModeScope();int runs=0;var inactive=scope.Bind(()=>runs++);inactive();Check(runs==0,"Inactive callback");scope.Start();var old=scope.Bind(()=>runs++);old();Check(runs==1,"Live callback");scope.Advance();old();Check(runs==1,"Old map callback");var unload=scope.Bind(()=>runs++);scope.Stop();unload();scope.Start();unload();Check(runs==1,"Unload and reload never revive callbacks");

string configDir=Path.Combine(sample,"ceda276a/configs/plugins/HanZombieRiotS2");
var profiles=new Z.ZombieProfile();
foreach(string map in new[]{"unknown","zombiesweden","workshop/123/zombiesweden"})foreach(bool high in new[]{false,true}){profiles.Reload(configDir,map,high);Check(profiles.Days.Days.Count>0&&profiles.Zombies.ZombieList.Count>0,"Real zombie paired profile "+map+" "+high);}
var originalDays=profiles.Days;var originalZombies=profiles.Zombies;
foreach(string bad in new[]{"../secret","/etc/passwd","..","a/b","x\\y","x;quit","workshop/x/map"})Reject(()=>profiles.Reload(configDir,bad,false),"Reject map path "+bad);
Check(ReferenceEquals(originalDays,profiles.Days)&&ReferenceEquals(originalZombies,profiles.Zombies),"Bad paths preserve complete pair");
string brokenDir=Path.Combine(root,"paired");Directory.CreateDirectory(brokenDir);File.Copy(Path.Combine(configDir,"HanZriotDayConfig.jsonc"),Path.Combine(brokenDir,"HanZriotDayConfig.jsonc"),true);File.WriteAllText(Path.Combine(brokenDir,"ZombieDataConfig.jsonc"),"{");
Reject(()=>profiles.Reload(brokenDir,"unknown",false),"Invalid zombie half rejected");Check(ReferenceEquals(originalDays,profiles.Days)&&ReferenceEquals(originalZombies,profiles.Zombies),"Atomic pair retains both originals");
foreach(string bad in new[]{"0","-1","de_dust2;quit","../../map","123 456","#comment",""})Check(!Z.ZombieProfile.SafeMapTarget(bad),"Unsafe map command rejected");
foreach(string good in new[]{"de_dust2","zombiesweden","3070192462"})Check(Z.ZombieProfile.SafeMapTarget(good),"Map target retained");
var zombieConfig=Z.ZombieProfile.ReadMain(File.ReadAllText(Path.Combine(configDir,"HanZriotCFG.jsonc")));Check(zombieConfig.SpawnProtectCount==20&&zombieConfig.AdminCommandPermission=="admin.dex"&&!zombieConfig.Enabled,"Live zombie rules retained, activation explicit");

// Execute the real Surf database code against a copy of the existing records.
string surfData=Path.Combine(root,"surf");Directory.CreateDirectory(surfData);
SqliteConnection.ClearAllPools();File.Copy(Path.Combine(sample,"f222196f/data/swiftsurf/records.db"),Path.Combine(surfData,"records.db"),true);
foreach(string file in new[]{"player_preferences.json","map_history.json"})File.Copy(Path.Combine(sample,"f222196f/data/swiftsurf",file),Path.Combine(surfData,file),true);
string surfConfig=Path.Combine(surfData,"config.jsonc");var disabled=JsonNode.Parse(surfText,documentOptions:options)!;disabled["SwiftSurf"]!["Enabled"]=false;string disabledText=disabled.ToJsonString();File.WriteAllText(surfConfig,disabledText);
var queue=new List<Action>();var world=new MenuWorld{DataDirectory=surfData,RejectEntityAccess=true};
world.ServiceOverride=(m,a)=>m.Name switch{"GetConfigPath"=>(true,surfConfig),"NextWorldUpdate"=>(true,Add(queue,(Action)a![0]!)),_=>(false,null)};
object? Add(List<Action> list,Action callback){list.Add(callback);return null;}
var plugin=new Plugin(world.Core);plugin.Load(false);
Check(world.EntityAccesses==0&&world.Commands.Count==0&&queue.Count==0,"Disabled Surf load never changes game state");
Check(File.ReadAllText(surfConfig)==disabledText,"Surf load preserves operator configuration bytes");
long Scalar(string sql){using var db=new SqliteConnection("Data Source="+Path.Combine(surfData,"records.db"));db.Open();using var c=db.CreateCommand();c.CommandText=sql;return Convert.ToInt64(c.ExecuteScalar());}
Check(Scalar("SELECT COUNT(*) FROM course_records")==4&&Scalar("SELECT COUNT(*) FROM course_runs")==14,"Existing records and all run history retained");
Set(plugin,"_map","test_course");Call(plugin,"SaveCourseRecord",world.Player,"main",40.5,new List<double>{10,20});Call(plugin,"SaveCourseRecord",world.Player,"main",41.5,new List<double>{11,21});Call(plugin,"SaveCourseRecord",world.Player,"main",39.5,new List<double>{9,19});
Check(Scalar("SELECT COUNT(*) FROM course_runs WHERE map='test_course'")==3&&Scalar("SELECT finishes FROM course_records WHERE map='test_course'")==3,"Three real finishes recorded");
Check(Scalar("SELECT CAST(best_seconds*10 AS INTEGER) FROM course_records WHERE map='test_course'")==395,"Best-time update preserves slower finish count");
using(var db=new SqliteConnection("Data Source="+Path.Combine(surfData,"records.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TRIGGER reject_finish BEFORE UPDATE ON course_records BEGIN SELECT RAISE(ABORT,'probe rollback'); END;";c.ExecuteNonQuery();}
try{Call(plugin,"SaveCourseRecord",world.Player,"main",38.5,new List<double>{8,18});throw new Exception("Expected SQLite abort");}catch(SqliteException){}
Check(Scalar("SELECT COUNT(*) FROM course_runs WHERE map='test_course'")==3,"Rejected record update also rolls back inserted run");
Reject(()=>Call(plugin,"SaveCourseRecord",world.Player,"main",double.NaN,new List<double>()),"Invalid run rejected");
Check((string)Call(plugin,"Args",Proxy.Make<ICommandContext>((m,a)=>m.Name=="get_Args"?new[]{"5"}:Proxy.Empty(m.ReturnType)))! =="5","First command argument retained");
var lifetime=Get<ModeScope>(plugin,"_lifetime");Set(plugin,"_config",surf);
int effects=0;Call(plugin,"Next",(Action)(()=>effects++));lifetime.Advance();queue[^1]();Check(effects==0,"Real Surf deferred action rejected after map generation changes");
var playerAction=(Action)Call(plugin,"PlayerAction",world.Player,(Action)(()=>effects++))!;world.Session++;playerAction();Check(effects==0,"Reconnected player cannot receive stale Surf action");
File.WriteAllText(surfConfig,"{");Call(plugin,"RefreshConfiguration");Check(ReferenceEquals(Get<Config>(plugin,"_config"),surf)&&Get<string?>(plugin,"_configError") is not null,"Invalid live configuration keeps last valid Surf state");
File.WriteAllText(surfConfig,surfText);Call(plugin,"RefreshConfiguration");Check(Get<string?>(plugin,"_configError") is null,"Surf configuration recovers");
plugin.Unload();Check(!lifetime.Active,"Surf unload invalidates callbacks");

// A stale menu must not apply its zone/practice action to a different map.
lifetime.Start();Call(plugin,"OpenTrackedMenu",world.Player,Call(plugin,"BuildSettingsMenu",world.Player)!);
var hudButton=world.Find("Timer-HUD");var preferences=Get<Dictionary<ulong,PlayerPrefs>>(plugin,"_prefs");bool hud=preferences[world.Player.SteamID].Hud;
lifetime.Advance();await world.Click(hudButton);Check(preferences[world.Player.SteamID].Hud==hud,"Previous-map Surf menu cannot change player settings");
Call(plugin,"OpenTrackedMenu",world.Player,Call(plugin,"BuildSettingsMenu",world.Player)!);await world.Click("Timer-HUD");queue[^1]();Check(preferences[world.Player.SteamID].Hud!=hud,"Current-map Surf menu remains usable");
plugin.Unload();

string zombieData=Path.Combine(root,"zombie");Directory.CreateDirectory(zombieData);
foreach(string name in new[]{"HanZriotDayConfig.jsonc","ZombieDataConfig.jsonc"})File.Copy(Path.Combine(configDir,name),Path.Combine(zombieData,name),true);
string zombieMain=Path.Combine(zombieData,"HanZriotCFG.jsonc");var zombieNode=JsonNode.Parse(File.ReadAllText(Path.Combine(configDir,"HanZriotCFG.jsonc")),documentOptions:options)!;zombieNode["ZriotCFG"]!["Enabled"]=false;File.WriteAllText(zombieMain,zombieNode.ToJsonString());
var zw=new MenuWorld{DataDirectory=zombieData,RejectEntityAccess=true};zw.ServiceOverride=(m,a)=>m.Name switch{"GetConfigPath"=>(true,zombieMain),_=>(false,null)};
var zp=new Z.Plugin(zw.Core);zp.Load(false);zp.Unload();Check(zw.Commands.Count==0&&zw.EntityAccesses==0,"Actual disabled Zombie load/unload leaves native gameplay untouched");

var timerActions=new List<Action>();var tokens=new List<CancellationTokenSource>();world.ServiceOverride=(m,a)=>m.Name switch{"NextWorldUpdate"=>(true,Add(timerActions,(Action)a![0]!)),"DelayBySeconds" or "RepeatBySeconds" or "DelayAndRepeatBySeconds"=>(true,CreateTimer((Action)a![^1]!)),_=>(false,null)};
CancellationTokenSource CreateTimer(Action action){timerActions.Add(action);var token=new CancellationTokenSource();tokens.Add(token);return token;}
var scheduler=new Z.ZombieScheduler(world.Core);scheduler.Scope.Start();scheduler.DelayBySeconds(1,()=>effects++);scheduler.RepeatBySeconds(1,()=>effects++);scheduler.Advance();foreach(var action in timerActions)action();Check(effects==0&&tokens.All(t=>t.IsCancellationRequested),"Zombie map/round reset cancels timers and guards queued native callbacks");
timerActions.Clear();scheduler.NextTick(()=>effects++);scheduler.Dispose();timerActions[0]();Check(effects==0,"Zombie unload rejects queued next-frame callback");

Console.WriteLine($"ModeReplacementChecks: {checks} passed; real SQLite finish transactions, existing profiles and guarded callbacks; native gameplay pending.");
