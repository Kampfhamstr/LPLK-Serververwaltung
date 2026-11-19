using System.Reflection;
using System.Text.Json;
using LPLK_Maps;
using LPLK_Teams;
using SwiftlyS2.Shared.Players;
int checks=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
var gate=new StartupMapGate();
gate.Arm(true,false,0);
Check(!gate.Due(6000,true,false,false,false),"no startup change before a real map exists");
gate.MapLoaded(6000);
Check(!gate.Due(10999,true,false,true,false)&&gate.Due(11000,true,false,true,false),"five seconds after actual map load");
gate.MapLoaded(11000);
Check(!gate.Due(15000,true,false,true,false)&&!gate.Due(17000,true,false,true,true)&&gate.Due(17000,true,false,true,false),"another bootstrap map and active work postpone rotation");
gate.Complete();Check(!gate.Due(20000,true,false,true,false),"one transition only, no map loop");
gate.Arm(true,true,0);Check(!gate.Due(10000,true,false,true,false),"hot reload does not change map");
gate.Arm(true,false,0);Check(!gate.Due(1000,true,true,false,false)&&!gate.Due(10000,true,false,true,false),"joining human cancels whole startup even before readiness");
gate.Arm(true,false,0);Check(!gate.Due(120000,true,false,true,false)&&!gate.Pending,"no late surprise change after timeout");
gate.Arm(true,false,0);Check(!gate.Due(6000,false,false,true,false)&&!gate.Pending,"disabled policy cancels startup");
var idle=new EmptyMapClock();
Check(!idle.Due(0,false,40)&&!idle.Due(2399999,false,40)&&idle.Due(2400000,false,40),"exact real forty-minute deadline");
Check(!idle.Due(2400001,true,40)&&!idle.Due(2400002,false,40)&&!idle.Due(4800001,false,40)&&idle.Due(4800002,false,40),"spectator/human restarts complete idle period");

var world=new MenuWorld();var values=new Dictionary<string,object>{{"bot_quota_mode","fill"},{"bot_quota",4}};int writes=0,commands=0;
world.ServiceOverride=(m,a)=>{
 if(m.Name=="Find"&&a?[0] is string name&&values.ContainsKey(name))return(true,Proxy.Make(m.ReturnType,(op,args)=>{
  if(op.Name=="get_Value")return values[name];
  if(op.Name=="set_Value"){values[name]=args![0]!;writes++;return null;}
  return Proxy.Empty(op.ReturnType);
 }));
 if(m.Name=="ExecuteCommand"){commands++;return(true,null);}
 return(false,null);
};
var plugin=new LPLK_Teams.Plugin(world.Core);
void Set(string name,object value)=>typeof(LPLK_Teams.Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,value);
void Call(string name,params object[] args)=>typeof(LPLK_Teams.Plugin).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plugin,args);
Set("settings",new TeamSettings{Enabled=false,BotFillSlots=4});
var excluded=new[]{(valid:true,fake:true,team:2),(true,true,3),(true,true,1),(true,false,1),(true,false,0),(false,false,2)};
int Count(IEnumerable<(bool valid,bool fake,int team)> clients)=>BotPopulationPolicy.CountPlayingHumans(clients,p=>p.valid,p=>p.fake,p=>p.team);
Check(Count(excluded)==0,"bots, SourceTV, spectators, unassigned and invalid clients excluded");
foreach(int humans in new[]{0,1,2,3,4,5,2,0}){
 var clients=excluded.Concat(Enumerable.Range(0,humans).Select(i=>(true,false,2+i%2)));
 int counted=Count(clients);Check(counted==humans,"only valid humans in T and CT count: "+humans);
 Call("UpdateBotQuota",counted);Check((int)values["bot_quota"]==Math.Max(0,4-humans),"playing humans reduce quota: "+humans);
 Check((string)values["bot_quota_mode"]=="normal","engine receives explicit remaining bot count");
 int before=writes;Call("UpdateBotQuota",counted);Check(writes==before,"unchanged population never repeats convar writes");
}
int currentTeam=1;
var connected=new[]{(valid:true,fake:false)};
int DynamicCount()=>BotPopulationPolicy.CountPlayingHumans(connected,p=>p.valid,p=>p.fake,p=>currentTeam);
foreach(int team in new[]{1,2,3,1,0}){
 currentTeam=team;Call("UpdateBotQuota",DynamicCount());Check((int)values["bot_quota"]==(team is 2 or 3?3:4),"joining or leaving a playing team adjusts quota: "+team);
}
Check(BotPopulationPolicy.CountPlayingHumans(new[]{0},_=>false,_=>throw new Exception("invalid player read"),_=>throw new Exception("invalid controller read"))==0,"invalid controller never read");
Check(BotPopulationPolicy.CountPlayingHumans(new[]{0},_=>true,_=>true,_=>throw new Exception("SourceTV controller read"))==0,"SourceTV controller never read");
Check(commands==0,"never use bot_kick or touch SourceTV");
Call("RestoreBotQuota");Check((string)values["bot_quota_mode"]=="fill"&&(int)values["bot_quota"]==4,"unload restores former native policy");
Call("UpdateBotQuota",0);values["bot_quota"]=7;Call("RestoreBotQuota");Check((int)values["bot_quota"]==7,"external admin value preserved on unload");
foreach(int bad in new[]{-1,31}){bool rejected=false;try{TeamSettings.Validate(new(){BotFillSlots=bad});}catch(InvalidDataException){rejected=true;}Check(rejected,"bot target bounds");}
var entries=new[]{
 new BotEntry("human",true,false,false,2,true),new BotEntry("tv-team",true,true,true,2,false),
 new BotEntry("tv-spec",true,true,true,1,false),new BotEntry("spectator",true,false,false,1,false),
 new BotEntry("invalid",false,true,false,2,false),new BotEntry("unassigned",true,true,false,0,false),
 new BotEntry("ct-alive",true,true,false,3,true),new BotEntry("t-dead",true,true,false,2,false),
 new BotEntry("t-alive",true,true,false,2,true),new BotEntry("ct-dead",true,true,false,3,false)};
bool Eligible(BotEntry p)=>BotPopulationPolicy.IsPlayingBot(p.Valid,p.Fake,p.Tv,p.Team);
foreach(int target in new[]{0,1,2,3,4,5}){
 var connectedBots=entries.ToList();var removed=new List<string>();
 for(int tick=0;tick<6;tick++){
  var selectedBot=BotPopulationPolicy.SelectExcessBot(connectedBots,target,Eligible,p=>p.Alive);
  if(selectedBot is not null){removed.Add(selectedBot.Name);connectedBots.Remove(selectedBot);}
 }
 Check(connectedBots.Count(Eligible)==Math.Min(4,target),"actual surplus removed even when engine quota does not act: "+target);
 Check(removed.All(n=>n is "t-dead" or "ct-dead" or "t-alive" or "ct-alive"),"no human, spectator or TV kicked: "+target);
 if(target<4)Check(removed[0]=="t-dead","dead bot removed first");
}
Check(BotPopulationPolicy.SelectExcessBot(entries.Take(6),0,Eligible,p=>p.Alive) is null,"no kick when only protected clients remain");
// Regression: population can be correct while all bots oppose the lone human.
Check(BotPopulationPolicy.TeamTargets(3,1,0,0,3)==(1,2),"one T human gets one teammate bot against two CT bots");
Check(BotPopulationPolicy.TeamTargets(3,0,1,3,0)==(2,1),"mirrored CT case");
Check(BotPopulationPolicy.TeamTargets(4,0,0,4,0)==(2,2),"empty server splits four bots evenly");
Check(BotPopulationPolicy.TeamTargets(2,2,0,2,0)==(0,2),"two humans together get two opposing bots");
Check(BotPopulationPolicy.TeamTargets(2,1,1,2,0)==(1,1),"one human on each side gets one bot each");
Check(BotPopulationPolicy.TeamTargets(0,3,1,0,0)==(0,0),"no extra bots to compensate human-only imbalance");
Check(BotPopulationPolicy.TeamTargets(1,3,0,1,0)==(0,1),"best possible split without moving humans");
Check(BotPopulationPolicy.TeamTargets(3,0,0,1,2)==(1,2),"odd population does not oscillate");
Check(BotPopulationPolicy.TeamTargets(3,0,0,2,1)==(2,1),"opposite odd population remains stable");
Check(!BotPopulationPolicy.MayMoveBot(true,false),"alive bot never changes team during combat");
Check(BotPopulationPolicy.MayMoveBot(false,false)&&BotPopulationPolicy.MayMoveBot(true,true),"dead bots or pre-spawn boundary can balance");
var imbalance=new[]{new BotEntry("lone-human",true,false,false,2,true),new BotEntry("teammate-dead",true,true,false,2,false),new BotEntry("opponent-alive",true,true,false,3,true),new BotEntry("opponent-dead",true,true,false,3,false),new BotEntry("opponent-other",true,true,false,3,true),new BotEntry("SourceTV",true,true,true,3,false)};
Check(BotPopulationPolicy.SelectExcessBot(imbalance,3,Eligible,p=>p.Alive,p=>p.Team,3)?.Name=="opponent-dead","surplus removed from overfilled team, not lone teammate");
Check(BotPopulationPolicy.SelectExcessBot(imbalance.Where(p=>p.Name!="opponent-dead"),2,Eligible,p=>p.Alive,p=>p.Team,3)?.Name=="opponent-alive","team balance takes priority over dead bot on smaller team");
for(int ht=0;ht<=5;ht++)for(int hc=0;hc<=5;hc++)for(int bt=0;bt<=4;bt++){
 int desired=BotPopulationPolicy.DesiredBots(4,ht+hc);var target=BotPopulationPolicy.TeamTargets(desired,ht,hc,bt,4-bt);
 Check(target.T+target.Ct==desired&&target.T>=0&&target.Ct>=0,"bot budget retained");
 int difference=Math.Abs(ht+target.T-hc-target.Ct);
 Check(difference==Enumerable.Range(0,desired+1).Min(t=>Math.Abs(ht+t-hc-(desired-t))),"minimal team imbalance without human moves");
 Check(BotPopulationPolicy.TeamTargets(desired,ht,hc,target.T,target.Ct)==target,"converged allocation is stable");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,result="passed",nativeEngineMocked=true,realPlayerJoinNotSimulated=true}));

record BotEntry(string Name,bool Valid,bool Fake,bool Tv,int Team,bool Alive);
