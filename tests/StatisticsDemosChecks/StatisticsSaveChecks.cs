using System.Reflection;
using LPLK_Statistics;
using SwiftlyS2.Shared;
static class StatisticsSaveChecks
{
 public static void Run(Action<bool,string> check,string directory)
 {
  var scheduled=new Queue<Action>();
  ISwiftlyCore Core(MenuWorld w){object? Scheduler(MethodInfo m,object?[]? a){if(m.Name!="DelayBySeconds")return m.Invoke(w.Core.Scheduler,a);check(Convert.ToDouble(a![0])==1,"Round save waits one second");scheduled.Enqueue(a.OfType<Action>().Single());return new CancellationTokenSource();}object? Hooks(MethodInfo m,object?[]? a)=>m.ReturnType.IsInterface?Proxy.Make(m.ReturnType,Hooks):Proxy.Empty(m.ReturnType);return Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_GameHooks"?Proxy.Make(m.ReturnType,Hooks):m.Name=="get_Scheduler"?Proxy.Make(m.ReturnType,Scheduler):m.Invoke(w.Core,a));}
  directory=Path.Combine(directory,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var world=new MenuWorld{DataDirectory=directory};var plugin=new LPLK_Statistics.Plugin(Core(world));plugin.Load(false);
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=plugin.GetType();
  void Set(string name,object value)=>type.GetField(name,flags)!.SetValue(plugin,value);
  object? Call(string name,params object[] args)=>type.GetMethod(name,flags)!.Invoke(plugin,args);
  const ulong id=76561198000000001;var data=new StatsData();var player=data.Touch(id,"Before");player.Pvp.Kills=1;player.Weapons["ak47"]=1;player.Extra.Damage=100;
  var copy=data.Copy();player.Name="After";player.Pvp.Kills=2;player.Bots.Shots=1;player.Weapons["ak47"]=2;player.Extra.Damage=200;
  check(copy.Players[id].Name=="Before"&&copy.Players[id].Pvp.Kills==1&&copy.Players[id].Weapons["ak47"]==1&&copy.Players[id].Extra.Damage==100&&copy.Players[id].Bots.Shots==0,"Worker snapshot detaches every mutable player field");
  Set("data",data);Set("dirty",true);((HashSet<ulong>)type.GetField("pendingExports",flags)!.GetValue(plugin)!).Add(id);Call("RequestKillSave");
  check(type.GetField("statisticsWrite",flags)!.GetValue(plugin) is null&&!File.Exists(Path.Combine(directory,"statistics.json")),"Kill only marks dirty state; no serialization task or file write");
  Call("RequestRoundSave");Call("RequestRoundSave");check(scheduled.Count==1&&!File.Exists(Path.Combine(directory,"statistics.json")),"Repeated round requests remain in RAM until the delayed callback");scheduled.Dequeue()();Call("CompleteStatisticsWrite",true);
  string path=Path.Combine(directory,"statistics.json");check(StatsData.Read(path).Players[id].Pvp.Kills==2&&File.Exists(Path.Combine(directory,"web",id+".json")),"Background save commits snapshot and matching web export");
  using(var ranking=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"web","ranking.json"))))check(ranking.RootElement.GetProperty("boards").GetProperty("kills")[0].GetProperty("kills").GetInt64()==2,"Background worker writes ranking from the same detached round snapshot");
  player.Pvp.Kills=3;Set("dirty",true);Call("RequestRoundSave");player.Pvp.Kills=4;Set("dirty",true);plugin.Unload();
  check(StatsData.Read(path).Players[id].Pvp.Kills==4,"Unload drains background writer then commits newer live changes");
  scheduled.Clear();
  var failureWorld=new MenuWorld{DataDirectory=Path.Combine(directory,"failure")};Directory.CreateDirectory(failureWorld.DataDirectory);var failing=new LPLK_Statistics.Plugin(Core(failureWorld));failing.Load(false);
  type.GetField("data",flags)!.SetValue(failing,data);type.GetField("dirty",flags)!.SetValue(failing,true);string blocked=Path.Combine(failureWorld.DataDirectory,"blocked");Directory.CreateDirectory(blocked);type.GetField("dataPath",flags)!.SetValue(failing,blocked);
  type.GetMethod("RequestRoundSave",flags)!.Invoke(failing,null);scheduled.Dequeue()();type.GetMethod("CompleteStatisticsWrite",flags)!.Invoke(failing,[true]);
  check((bool)type.GetField("dirty",flags)!.GetValue(failing)!&&type.GetField("saveError",flags)!.GetValue(failing) is string,"Background failure retains dirty counters for recovery");
  string recovered=Path.Combine(failureWorld.DataDirectory,"recovered.json");type.GetField("dataPath",flags)!.SetValue(failing,recovered);failing.Unload();check(StatsData.Read(recovered).Players[id].Pvp.Kills==4,"Shutdown retry preserves all counters after write failure");
 }
}
