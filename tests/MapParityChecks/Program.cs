using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Maps;

int checks=0;
void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
void Reject(Action action,string label){bool bad=false;try{action();}catch(InvalidDataException){bad=true;}Check(bad,label);}
foreach(string name in new[]{"surf_test","Workshop Surf_Test","kz_checkmate","xc_test","bkz_test","de_dust2"})Check(MapPolicy.Category(name)==(name.Contains("surf",StringComparison.OrdinalIgnoreCase)?MapCategory.Surf:name.Contains("_test")||name.StartsWith("kz")?MapCategory.Kz:MapCategory.Other),"category "+name);
var settings=new MapSettings{MeasurementOnly=false,RescanBeforeVote=false,AllowSpectatorsToVote=true,ExtendEnabled=true,ExtendMinPlayers=0,EndVoteAllowExtend=true};
var configured=new MapEntry{Id="3618493746",Name="Zombie Sweden",MinPlayers=1,MaxPlayers=30,ConfiguredMaxPlayers=30};
Check(!MapPolicy.LimitsUsable(configured,settings),"PvP still requires measured approved limits");
Check(MapPolicy.LimitsUsable(configured,settings with{UseConfiguredZombieLimits=true})&&!MapPolicy.Measured(configured)&&!MapPolicy.LimitsApproved(configured),"Zombie configured limits are usable without fabricating measured approval");
Check(!MapPolicy.LimitsUsable(configured with{MaxPlayers=64},settings with{UseConfiguredZombieLimits=true}),"Configured Zombie ceiling cannot be raised implicitly");
Check(MapPolicy.LimitsUsable(configured,settings with{UseConfiguredMapLimits=true})&&!MapPolicy.LimitsApproved(configured),"Imported explicit map bounds preserve eligibility without measured approval");
Check(!MapPolicy.LimitsUsable(configured with{ConfiguredMaxPlayers=null},settings with{UseConfiguredMapLimits=true}),"Unconfigured discovered maps do not acquire migrated eligibility");
foreach(int minute in new[]{0,15,30})foreach(int rounds in new[]{0,20,24})foreach(int wins in new[]{0,10,16})
{
 if(minute+rounds+wins==0){Reject(()=>MapPolicy.Extension(settings,0,minute,rounds,wins),"no active limits cannot extend");continue;}
 var result=MapPolicy.Extension(settings,0,minute,rounds,wins);Check(result==new MapExtension(minute>0?minute+15:0,rounds>0?rounds+5:0,wins>0?wins+3:0),"extend only active limits");
}
Reject(()=>MapPolicy.Extension(settings,3,30,24,13),"bounded extensions");Reject(()=>MapPolicy.Extension(settings,0,float.NaN,24,13),"unknown time rejected");
Check(MapPolicy.Extension(settings with{ExtendInitializeRoundLimits=true},0,0,0,0)==new MapExtension(0,29,18),"explicit old MapChooser zero-limit behavior preserved");
Reject(()=>MapSettings.Validate(settings with{ExtendPercent=101}),"invalid threshold");Reject(()=>MapSettings.Validate(settings with{SetNextMapPermission=""}),"empty permission rejected");
MapSettings.Validate(settings with{RtvMinPlayers=0,ExtendMinPlayers=0,DirectVotemapMinPlayers=0});checks++;
var pool=new[]{new MapEntry{Id="surf_one",Name="Surf One"},new MapEntry{Id="surf_two",Name="Surf Two"},new MapEntry{Id="kz_one",Name="KZ One"},new MapEntry{Id="de_one",Name="Dust One"}};
Check(MapPolicy.CandidateSelection(pool,new HashSet<string>{"surf_two"},3,true).Select(m=>m.Id).SequenceEqual(new[]{"surf_two","surf_one","kz_one"}),"nomination first, category round robin");
Check(MapPolicy.Resolve(pool,"One")==null,"ambiguous partial selection rejected");Check(MapPolicy.Resolve(pool,"surf_two")==pool[1],"exact ID wins");
Check(MapPolicy.NextCycle(pool,new HashSet<string>{"surf_one","de_one"},"surf_one")==pool[3],"cycle skips disallowed maps");Check(MapPolicy.NextCycle(pool,new HashSet<string>{"surf_one"},"de_one")==pool[0],"cycle wraps");
using(var w=new World(settings))
{
 w.Limits["mp_timelimit"]=30f;w.Limits["mp_maxrounds"]=24;w.Limits["mp_winlimit"]=13;
 Check((bool)w.Call("ExtendMap",false)!,"native boundary setters extend");Check(w.Get<int>("extensions")==1&&w.Limits["mp_timelimit"].Equals(45f)&&w.Limits["mp_maxrounds"].Equals(29)&&w.Limits["mp_winlimit"].Equals(16),"all limits verified");
 Check(w.Get<long>("autoVotePostponedUntil")>=Environment.TickCount64+14*60000L,"extension postpones elapsed-time automatic vote");
 w.Limits["mp_maxrounds"]=99;w.Call("ResetParity");Check(w.Limits["mp_timelimit"].Equals(30f)&&w.Limits["mp_maxrounds"].Equals(99)&&w.Limits["mp_winlimit"].Equals(13),"restore only owned values preserves external change");
}
using(var w=new World(settings))
{
 w.Limits["mp_timelimit"]=30f;w.Limits["mp_maxrounds"]=24;w.IgnoreSet="mp_maxrounds";
 Check(!(bool)w.Call("ExtendMap",false)!,"failed native setter never confirms extension");Check(w.Get<int>("extensions")==0&&w.Limits["mp_timelimit"].Equals(30f)&&w.Limits["mp_maxrounds"].Equals(24),"partial write rolled back");
}
using(var w=new World(settings with{MeasurementOnly=true}))
{
 w.Limits["mp_maxrounds"]=24;Check(!(bool)w.Call("ExtendMap",false)!,"measurement mode cannot extend");Check(w.Limits["mp_maxrounds"].Equals(24),"measurement mode untouched");
}
using(var w=new World(settings))
{
 var map=MapPolicy.CaptureMeasurement(new MapEntry{Id="de_one",Name="One"},16,16,0,0);map=MapPolicy.ApproveLimits(map,map.LimitProposal!.Signature);
 w.Set("maps",new List<MapEntry>{map});w.Set("installed",new HashSet<string>{map.Id});w.Set("candidates",new List<MapEntry>{map});w.Set("voteEnds",Environment.TickCount64+60000);
 w.Call("ShowVote",w.Menu.Player);Check(w.Menu.Labels.Any(x=>x.Contains("One")),"actual vote opens");
 var button=w.Menu.Find("One");w.Menu.Session++;await w.Menu.Click(button);Check(w.Get<Dictionary<ulong,string>>("ballots").Count==0,"old session cannot vote");
 w.Call("ShowVote",w.Menu.Player);w.Set("maps",new List<MapEntry>{map with{Enabled=false}});await w.Menu.Click("One");Check(w.Get<Dictionary<ulong,string>>("ballots").Count==0,"click rechecks changed map eligibility");
 w.Set("maps",new List<MapEntry>{map});w.Call("ShowVote",w.Menu.Player);await w.Menu.Click("One");Check(w.Get<Dictionary<ulong,string>>("ballots")[w.Menu.Player.SteamID]==map.Id,"eligible current player ballot saved");
}
using(var w=new World(settings))
{
 var maps=new[]{"de_one","de_two","de_three"}.Select(id=>{var m=MapPolicy.CaptureMeasurement(new MapEntry{Id=id,Name=id},16,16,0,0);return MapPolicy.ApproveLimits(m,m.LimitProposal!.Signature);}).ToList();
 w.Set("maps",maps);w.Set("installed",maps.Select(m=>m.Id).ToHashSet());w.Menu.Permissions.Add("admin.mapsvote");
 var selected=new HashSet<string>{"de_one","de_three"};w.Call("CustomVoteMenu",w.Menu.Player,selected);var start=w.Menu.Find("Abstimmung starten");
 w.Menu.Permissions.Clear();await w.Menu.Click(start);Check(w.Get<long>("voteEnds")==0,"custom admin vote rechecks permission");
 w.Menu.Permissions.Add("admin.mapsvote");w.Call("CustomVoteMenu",w.Menu.Player,selected);w.Set("maps",maps.Select(m=>m.Id=="de_three"?m with{Enabled=false}:m).ToList());
 await w.Menu.Click("Abstimmung starten");Check(w.Get<List<MapEntry>>("candidates").Select(m=>m.Id).SequenceEqual(new[]{"de_one"}),"custom vote preserves selected pool and rechecks eligibility");
 Check(w.Get<long>("voteEnds")>Environment.TickCount64,"custom map vote actually starts");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,result="passed",nativeEngineMocked=true,clientAcceptancePending=true}));

sealed class World:IDisposable
{
 public MenuWorld Menu=new(){DataDirectory=Path.Combine(Path.GetTempPath(),"LplkMapParity-"+Guid.NewGuid().ToString("N"))};
 public Plugin Plugin;public Dictionary<string,object> Limits=new(){["mp_timelimit"]=0f,["mp_maxrounds"]=0,["mp_winlimit"]=0};public string? IgnoreSet;
 public World(MapSettings settings)
 {
  Directory.CreateDirectory(Menu.DataDirectory);Menu.OnlinePlayers=[Menu.Player];
  Menu.ServiceOverride=(m,a)=>
  {
   if(m.Name=="Find"&&a?[0] is string name&&Limits.ContainsKey(name))return(true,Proxy.Make(m.ReturnType,(op,values)=>op.Name switch{"get_Value"=>Limits[name],"set_Value"=>SetLimit(name,values![0]!),_=>Proxy.Empty(op.ReturnType)}));
   if(m.Name=="get_GlobalVars")throw new NotSupportedException("No native globals in fixture");
   if(m.Name=="get_WorkshopId")return(true,"");
   if(m.Name=="CloseMenuForPlayer"){if(Menu.Current==a![1])Menu.Current=null;return(true,null);}
   return(false,null);
  };
  Plugin=new(Menu.Core);Set("settings",settings);Set("module",new ModuleSupport(Menu.Core,"LPLK_Maps","Maps","lplk.maps.admin",_=>{}){Active=true});Set("warmup",false);
 }
 private object? SetLimit(string name,object value){if(name!=IgnoreSet)Limits[name]=value;return null;}
 public void Set(string name,object? value)=>typeof(Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Plugin,value);
 public T Get<T>(string name)=>(T)typeof(Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Plugin)!;
 public object? Call(string name,params object?[] args)=>typeof(Plugin).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Plugin,args);
 public void Dispose(){Plugin.Unload();Directory.Delete(Menu.DataDirectory,true);}
}
