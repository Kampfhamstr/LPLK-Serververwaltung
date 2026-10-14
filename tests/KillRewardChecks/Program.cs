using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;
const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
var settings=new EconomySettings{ResetMode="Never"};
void Seed(Ledger db){foreach(var id in new[]{a,b,c}){db.Open(id,id[^1..]);db.Adjust("seed:"+id,1,a,id,100000,"set");}}
if(args is ["--crash",var crashFile,var stage])
{
    bool armed=false;using var db=new Ledger(crashFile,settings,phase=>{if(armed&&phase==stage)Environment.Exit(27);});Seed(db);
    if(stage=="after-kill-awarded"){armed=true;db.AwardKillReward("event",a,b,"B",15000,false);}
    else{db.AwardKillReward("event",a,b,"B",15000,false);if(stage=="after-kill-paid"){armed=true;db.PayKillReward("pay","event",a,"s",15000,1000,500);}else{db.PayKillReward("pay","event",a,"s",15000,1000,500);armed=true;db.ResolveCash("pay",true,"native");}}return;
}
int checks=0;void Check(bool result,string name){checks++;if(!result)throw new Exception(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(BankRejected){rejected=true;}Check(rejected,name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"KillRewardChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? rules=null)=>new(Path.Combine(root,name+".sqlite3"),rules??settings);
foreach(int cash in new[]{0,1,999,1000,1001,1099,10000,16000,20000,1000000})foreach(int percent in new[]{0,1,10,100,int.MaxValue})foreach(int max in new[]{-1,0,2000})
{
    var s=settings.KillReward with{PercentOfVictimCash=percent,MaxReward=max};decimal expected=decimal.Floor(cash*(decimal)percent/100);if(expected<100)expected=0;if(max>=0)expected=Math.Min(expected,max);Check(KillRewardPolicy.Amount(cash,s)==Math.Min(int.MaxValue,expected),"threshold then cap, exact integer and no overflow");
}
using(var db=Open("claim"))
{
    Seed(db);Check(db.AwardKillReward("kill",a,b,"B",16000,false)==1600,"ten percent awarded");Check(db.Balance(b)==100000&&db.Balance(a)==100000,"no victim debit or premature payout");Check(db.Balance("system:killgrant")==-1600,"explicit reward source");
    Check(db.AwardKillReward("kill",a,b,"B",5000,false)==0&&db.KillRewardClaims().Length==1,"duplicate event ignores changed cash snapshot");Reject(()=>db.AwardKillReward("kill",c,b,"B",16000,false),"conflicting killer rejected");Reject(()=>db.AwardKillReward("self",a,a,"A",16000,false),"self kill rejected");
    Check(db.AwardKillReward("team",a,b,"B",16000,true)==0,"teamkill ignored");Reject(()=>db.AwardKillReward("bad",a,b,"B",-1,false),"unknown cash rejected");Reject(()=>db.AwardKillReward("bad",a,b,"B",1000001,false),"implausible cash rejected");
    var i=db.PayKillReward("pay","kill",a,"s",15000,1000,600)!;Check(i.After==16000&&db.Balance(a)==100600&&db.PendingCount==1,"cash and bank split atomic");db.Verify();
    db.PayKillReward("pay","kill",a,"s",15000,1000,600);Check(db.Balance(a)==100600,"repeat payout once");Reject(()=>db.PayKillReward("pay","kill",a,"x",15000,1000,600),"session conflict rejected");
    db.ResolveCash("pay",false,"checked failed native write");Check(db.KillRewardClaims() is [{Remaining:1000}]&&db.Balance(a)==100600,"cash cancellation restores only unpaid cash portion");db.PayKillReward("retry","kill",a,"bank",0,0,1000);Check(db.Balance(a)==101600&&db.KillRewardClaims().Length==0,"restored amount paid once");db.Verify();
    Check(db.AwardKillReward("small",a,b,"B",999,false)==0&&db.AwardKillReward("small",a,b,"B",16000,false)==0,"below threshold remains nonpayable on duplicate");
}
using(var db=Open("pending"))
{
    Seed(db);db.PrepareCash("uncertain",1,a,"s",1000,2000);db.AwardKillReward("kill",a,b,"B",15000,false);Check(db.KillRewardClaims() is [{Remaining:1500}],"reward retained while previous cash uncertain");Reject(()=>db.PayKillReward("pay","kill",a,"bank",0,0,1500),"payout waits for uncertain cash");db.ResolveCash("uncertain",false,"checked");db.PayKillReward("pay","kill",a,"bank",0,0,1500);db.Verify();
}
using(var db=Open("cap",settings with{MaxBank=100000}))
{
    Seed(db);db.AwardKillReward("kill",a,b,"B",15000,false);Reject(()=>db.PayKillReward("full","kill",a,"bank",0,0,1500),"full bank cannot lose claim");Check(db.KillRewardClaims() is [{Remaining:1500}],"full claim retained");db.Adjust("space",1,a,a,500,"remove");db.PayKillReward("part","kill",a,"bank",0,0,500);Check(db.KillRewardClaims() is [{Remaining:1000}],"remaining claim retained after partial payment");db.Verify();
}
foreach(string mode in new[]{"Cash","Bank","CashThenBankOverflow"})using(var db=Open(mode,settings with{KillReward=settings.KillReward with{RewardMode=mode}}))
{
    Seed(db);db.AwardKillReward("kill",a,b,"B",16000,false);
    if(mode=="Bank"){Reject(()=>db.PayKillReward("bad","kill",a,"s",15000,1000,600),"bank mode cannot become cash");db.PayKillReward("pay","kill",a,"bank",0,0,1600);}
    else{Reject(()=>db.PayKillReward("bad","kill",a,"s",16000,int.MaxValue,0),"cash integer overflow rejected");if(mode!="Cash")Reject(()=>db.PayKillReward("discard","kill",a,"s",16000,0,0,1600),"overflow cannot silently disappear");db.PayKillReward("pay","kill",a,"s",15000,1000,mode=="Cash"?0:600,mode=="Cash"?600:0);db.ResolveCash("pay",true,"native");db.ResolveCash("pay",true,"native");Reject(()=>db.ResolveCash("pay",false,"conflict"),"conflicting resolution rejected");Check(db.Balance("system:killcap")== (mode=="Cash"?600:0),"explicit cash-only cap loss");}
    Check(db.KillRewardClaims().Length==0&&db.PendingCount==0,"mode settled exactly once");db.Verify();
}
using(var db=Open("switches"))
{
    Seed(db);db.Configure(settings with{BankEnabled=false});Check(db.AwardKillReward("no-bank",a,b,"B",10000,false)==1000,"legacy automatic reward independent of bank menu switch");db.Configure(settings with{KillReward=settings.KillReward with{Enabled=false}});Check(db.AwardKillReward("disabled",a,b,"B",10000,false)==0,"disabled no new grant");db.PayKillReward("owed","no-bank",a,"bank",0,0,1000);Check(db.Balance(a)==101000,"accepted rewards settle while disabled");
    db.Configure(settings with{Enabled=false});Check(db.AwardKillReward("off",a,b,"B",10000,false)==0,"global off no grant");db.Configure(settings with{KillReward=settings.KillReward with{IgnoreTeamKills=false,IgnoreBots=false}});Check(db.AwardKillReward("team",a,b,"B",10000,true)==1000,"configured teamkill accepted for humans");db.Verify();
}
using(var db=Open("reset"))
{
    Seed(db);db.AwardKillReward("kill",a,b,"B",10000,false);Reject(()=>db.Reset("reset",1,a,settings,DateTime.Now),"reset cannot erase outstanding reward");Check(db.Period==1,"blocked reset no period change");db.PayKillReward("pay","kill",a,"bank",0,0,1000);db.Reset("reset",1,a,settings,DateTime.Now);Check(db.Period==2&&db.KillRewardClaims().Length==0,"settled reward permits reset");db.Verify();
}
foreach(string phase in new[]{"after-kill-awarded","after-kill-paid","after-kill-cash"})
{
    string file=Path.Combine(root,phase+".sqlite3");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};foreach(string argument in new[]{Assembly.GetExecutingAssembly().Location,"--crash",file,phase})start.ArgumentList.Add(argument);using var child=Process.Start(start)!;if(!child.WaitForExit(30000)){child.Kill();throw new Exception("Crash timeout");}Check(child.ExitCode==27,"actual process interrupted "+phase);
    using var db=new Ledger(file,settings);db.Verify();
    if(phase=="after-kill-awarded")Check(db.KillRewardClaims().Length==0&&db.Balance("system:killpool")==0,"award and funding roll back together");
    else if(phase=="after-kill-paid")Check(db.KillRewardClaims() is [{Remaining:1500}]&&db.PendingCount==0&&db.Balance(a)==100000,"split payout rolls back together");
    else{Check(db.PendingCount==1&&db.Balance(a)==100500&&db.Balance("system:escrow")==1000,"uncertain native confirmation retains pending escrow");db.ResolveCash("pay",true,"verified");db.ResolveCash("pay",true,"verified");Check(db.Balance(a)==100500&&db.PendingCount==0&&db.KillRewardClaims().Length==0,"restart settles once");}db.Verify();
}
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus")};Directory.CreateDirectory(world.DataDirectory);var plugin=new Plugin(world.Core);plugin.Load(false);
void Message(params string[] args)=>plugin.MessagesCommand(Proxy.Make<ICommandContext>((m,x)=>m.Name switch{"get_Sender"=>world.Player,"get_Args"=>args,_=>Proxy.Empty(m.ReturnType)}));
bool Muted()=>((Ledger)typeof(Plugin).GetField("bank",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!).Muted("76561198000000000");
foreach(string off in new[]{"aus","off","0","false","no","nein"}){Message(off);Check(Muted(),"legacy explicit off "+off);Message(off);Check(Muted(),"repeat off cannot toggle");}
Message();Check(Muted()&&world.Messages.Last().Contains("aktuell aus"),"no-argument message command only reports");Message("invalid");Check(Muted(),"invalid command keeps preference");
foreach(string on in new[]{"an","on","ein","1","true","yes","ja"}){Message(on);Check(!Muted(),"legacy explicit on "+on);}
async Task OpenRewards(){plugin.BankCommand(world.Context);await world.Click("Weitere Funktionen");while(!world.Labels.Any(x=>x.Contains("Kill-Belohnungen")))await world.Click("Nächste");}
await OpenRewards();var oldInfo=world.Find("Kill-Belohnungen");await world.Click(oldInfo);Check(world.Messages.Last().Contains("offene Ansprüche"),"enabled info reports reward rules and claims");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);await world.Click("Einstellungen");while(!world.Labels.Any(x=>x.Contains("Kill-Belohnungen")))await world.Click("Nächste");await world.Click("Kill-Belohnungen");var toggle=world.Find("Kill-Belohnung: AN");world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.KillReward.Enabled,"lost admin permission cannot switch");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);int priorMessages=world.Messages.Count;await world.Click(oldInfo);Check(world.Messages.Count==priorMessages||world.Messages.Last().Contains("ausgeschaltet"),"old public callback cannot report enabled reward after deactivation");
plugin.BankCommand(world.Context);await world.Click("Weitere Funktionen");for(int n=0;n<5;n++){Check(!world.Labels.Any(x=>x.Contains("Kill-Belohnungen")),"inactive info hidden");if(!world.Labels.Any(x=>x.Contains("Nächste")))break;await world.Click("Nächste");}
Check(world.AllMenus.All(m=>m.Options.Count<=5),"all reward pages at most five rows");plugin.Unload();
var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"state","killreward-reference.json")));
var methods=typeof(Plugin).GetMethods().Where(m=>m.GetCustomAttribute<Command>() is not null).ToArray();int aliases=0;
foreach(var group in reference.RootElement.GetProperty("settings").GetProperty("Commands").EnumerateObject())foreach(var value in group.Value.EnumerateArray())
{
    string alias=value.GetString()!;aliases++;Check(methods.Any(m=>m.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias&&x.RegisterRaw)),"actual legacy raw alias "+alias);Check(methods.Any(m=>m.GetCustomAttribute<Command>()!.Name==alias||m.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias&&!x.RegisterRaw)),"actual legacy prefixed alias "+alias);
}
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,preservedClaims=true,thresholdVerified=true,legacyAliases=aliases,legacyMessageSyntaxVerified=true,clientAcceptancePending=true};File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"KillRewardChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
