using System.Reflection;
using LPLK_Teams;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Menus;
int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
var world=new MenuWorld();var plugin=new Plugin(world.Core);
void Set(string name,object? value)=>typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(plugin,value);
T Get<T>(string name)=>(T)typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plugin)!;
object? Call(string name,params object?[] args)=>typeof(Plugin).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(plugin,args);
var sessions=new Dictionary<ulong,ulong>{{1,1},{2,2},{3,3}};var teams=new Dictionary<ulong,int>{{1,2},{2,3},{3,1}};
IPlayer Player(ulong id)=>Proxy.Make<IPlayer>((m,a)=>m.Name switch{
 "get_IsValid" or "get_IsAuthorized"=>true,"get_IsFakeClient"=>false,"get_SteamID"=>id,"get_SessionId"=>sessions[id],"get_PlayerID" or "get_Slot"=>(int)id,
 _=>Proxy.Empty(m.ReturnType)});
var a=Player(1);var b=Player(2);var spectator=Player(3);world.OnlinePlayers=[a,b];Set("loaded",true);
var currentMenus=new Dictionary<int,IMenuAPI>();
world.ServiceOverride=(m,args)=>
{
 if(m.Name=="GetPlayer")return(true,world.OnlinePlayers!.FirstOrDefault(p=>p.PlayerID==(int)args![0]!));
 if(m.Name=="OpenMenuForPlayer")currentMenus[((IPlayer)args![0]!).PlayerID]=(IMenuAPI)args[1]!;
 if(m.Name=="GetCurrentMenu")return(true,currentMenus.GetValueOrDefault(((IPlayer)args![0]!).PlayerID));
 if(m.Name=="CloseMenuForPlayer"){int id=((IPlayer)args![0]!).PlayerID;if(currentMenus.GetValueOrDefault(id)==args[1])currentMenus.Remove(id);return(true,null);}
 return(false,null);
};
// Native ref-return schema access is represented by the fixture's team values.
Set("botVoteEligibility",(Func<IPlayer,bool>)(p=>teams[p.SteamID] is 2 or 3));
Set("settings",new TeamSettings{BotFillSlots=4,TwoPlayerBotVoteEnabled=true});
Check(((IPlayer[])Call("BotVotePlayers")!).Length==2,"Two playing humans participate");
world.OnlinePlayers=[a,b,spectator];Check(((IPlayer[])Call("BotVotePlayers")!).Length==0,"Additional spectator prevents only-two-player offer");world.OnlinePlayers=[a,b];
var vote=Get<TwoPlayerBotVote>("botVote");double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
Check(vote.Start([1,2],now),"Start actual menu test");Call("OpenBotVote",a);var first=world.Current!;Call("OpenBotVote",b);var second=world.Current!;
Check(first.Options.Count==2&&second.Options.Count==2&&first.Configuration.MaxVisibleItems==5,"Both participants get numbered yes/no menu");
await first.Options[0].OnClickAsync(a);Check(vote.Active&&vote.Yes==1&&!vote.Suppressed,"Real first click cannot disable bots");
await first.Options[0].OnClickAsync(a);Check(vote.Yes==1,"Repeated menu click cannot add a second vote");
await second.Options[0].OnClickAsync(b);Check(vote.Suppressed&&!vote.Active,"Real second participant click accepts");
vote.NewMap();vote.Start([1,2],now);Call("OpenBotVote",a);first=world.Current!;Call("OpenBotVote",b);second=world.Current!;
sessions[2]++;await first.Options[0].OnClickAsync(a);Check(vote.Yes==0&&!vote.Suppressed,"Reconnected other participant invalidates stale menu clicks");
Call("TickBotVote");Check(!vote.Active&&!vote.Suppressed,"Tick cancels changed connection");
vote.NewMap();vote.Start([1,2],now);Call("OpenBotVote",a);first=world.Current!;Call("OpenBotVote",b);
await first.Options[1].OnClickAsync(a);Check(!vote.Active&&!vote.Suppressed,"Real no button retains bots");
Set("previousBotQuota",4);Set("previousBotMode","normal");var cvars=new Dictionary<string,object>{{"bot_quota",4},{"bot_quota_mode","normal"}};
var menuServices=world.ServiceOverride;
world.ServiceOverride=(m,args)=>m.Name=="Find"&&args?[0] is string key&&cvars.ContainsKey(key)?(true,Proxy.Make(m.ReturnType,(op,values)=>{if(op.Name=="get_Value")return cvars[key];if(op.Name=="set_Value"){cvars[key]=values![0]!;return null;}return Proxy.Empty(op.ReturnType);} )):menuServices(m,args);
vote.Restore(true,true);Call("UpdateBotQuota",2);Check((int)cvars["bot_quota"]==0,"Accepted vote controls actual quota path");
Call("UpdateBotQuota",3);Check((int)cvars["bot_quota"]==0,"Third human after acceptance does not end full-map pause");
Call("ResetBotVote");Call("UpdateBotQuota",2);Check((int)cvars["bot_quota"]==2,"Map reset restores normal fill target");
var data=Path.Combine(Path.GetTempPath(),"LplkBotStart-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);world.DataDirectory=data;
try
{
 var path=Path.Combine(data,"two-player-bot-vote.json");var old=new BotVoteSession("old-process","de_dust2","",1);var current=old with{Process="current-process"};
 File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new BotVoteSaved(1,old,true,true,10,"normal")));
 plugin=new Plugin(world.Core);Call("LoadBotVote",new object?[]{null});
 Check(!Get<bool>("botVoteStateReady")&&Get<string?>("botVoteError") is null,"Cold start waits without treating missing map as corrupt data");
 Call("LoadBotVote",current);Check(Get<bool>("botVoteStateReady")&&!Get<TwoPlayerBotVote>("botVote").Suppressed,"Later map readiness rejects previous-process suppression");
 Get<TwoPlayerBotVote>("botVote").Restore(true,true);Set("previousBotQuota",10);Set("previousBotMode","normal");Call("SaveBotVote");
 typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);
 Check(System.Text.Json.JsonSerializer.Deserialize<BotVoteSaved>(File.ReadAllText(path))!.Suppressed,"Save uses cached session with native globals unavailable");
 cvars["bot_quota"]=0;plugin=new Plugin(world.Core);Call("LoadBotVote",current);
 Check(Get<TwoPlayerBotVote>("botVote").Suppressed&&Get<int?>("previousBotQuota")==10,"Same-session reload restores accepted pause and original quota");
 plugin=new Plugin(world.Core);Call("LoadBotVote",current with{ServerCount=2});Check(!Get<TwoPlayerBotVote>("botVote").Suppressed,"Next map clears suppression even when map name is unchanged");
 typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);
 File.WriteAllText(path,"{broken");plugin=new Plugin(world.Core);Call("LoadBotVote",current);
 Check(Get<bool>("botVoteStateBlocked")&&!Get<bool>("botVoteStateReady"),"Corrupt state remains distinct from temporary engine unavailability");
 Call("LoadBotVote",current with{ServerCount=3});Check(File.ReadAllText(path)=="{broken","Readiness retries preserve corrupted state for investigation");
}
finally{typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);Directory.Delete(data,true);}
Console.WriteLine($"{checks} BotVote menu/session/quota/startup checks passed");
