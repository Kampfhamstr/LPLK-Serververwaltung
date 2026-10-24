using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using Microsoft.Data.Sqlite;
using SwiftlyS2.Shared.Commands;

const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
var settings=new EconomySettings();var rules=new BettingSettings();
if(args is ["--crash",var crashPath])
{
    bool armed=false;using var crashBank=new Ledger(crashPath,settings,phase=>{if(armed&&phase=="after-posting")Environment.Exit(24);});
    crashBank.Open(a,"A");crashBank.StartBetRound("crash",rules);crashBank.PrepareBetStake("stake","crash",a,"session",2,2000,1000);armed=true;crashBank.ResolveCash("stake",true,"test");return;
}
int checks=0;void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
void Reject(Action work,string name){bool rejected=false;try{work();}catch(BankRejected){rejected=true;}Check(rejected,name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BettingChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? config=null,Action<string>? fault=null)=>new(Path.Combine(root,name+".sqlite3"),config??settings,fault);
void Stake(Ledger db,string round,string account,int side,int amount){db.Open(account,account);db.PrepareBetStake(round+account,round,account,"session",side,16000,amount);db.ResolveCash(round+account,true,"test");}
using(var db=Open("normal"))
{
    db.StartBetRound("round",rules with{HouseCutPercent=10});Stake(db,"round",a,2,600);Stake(db,"round",b,2,400);Stake(db,"round",c,3,1000);
    Check(db.Balance(a)==0&&db.Balance("system:betpool")==2000,"cash-funded pool does not debit bank");
    Reject(()=>db.PrepareBetStake("second","round",a,"session",3,15000,100),"one fixed bet per round");
    Reject(()=>db.Reset("reset",1,a,settings,DateTime.Now),"accepted bets block period reset");
    db.CloseBetRound("round",2);db.CloseBetRound("round",3);
    Check(db.BetTickets("round").Single(t=>t.Account==a).Claim==1140&&db.BetTickets("round").Single(t=>t.Account==b).Claim==760,"proportional integer payouts");
    Check(db.Balance("system:betfees")==100&&db.BetClaims==1900,"house cut and total conserved");
    var payout=db.PayBetClaim("pay","round",a,"session",15000,500,640)!;
    Check(payout.After==15500&&db.Blocked(a)&&db.Balance(a)==640&&db.Balance("system:escrow")==500,"cash and bank payout staged atomically");
    Reject(()=>db.PayBetClaim("pay","round",a,"session",15000,500,600),"changed repeated bank portion rejected");
    db.ResolveCash("pay",false,"admin-test");Check(db.BetTickets("round").Single(t=>t.Account==a).Claim==500&&db.Balance(a)==640,"cancel only restores unconfirmed cash portion");
    db.PayBetClaim("pay-rest","round",a,"bank",0,0,500);db.PayBetClaim("pay-b","round",b,"bank",0,0,760);
    Check(db.BetClaims==0&&db.Balance(a)==1140&&db.Balance("system:betpool")==0,"offline payout clears claims once");
    db.Verify();db.Reset("reset",1,a,settings,DateTime.Now);Check(db.Period==2,"settled bets permit reset");
}
foreach(int cut in new[]{0,1,17,50,100})foreach(int first in new[]{100,101,999,5000,16000})foreach(int second in new[]{100,333,5000})
{
    string round=$"split-{cut}-{first}-{second}";using var db=Open(round);db.StartBetRound(round,rules with{HouseCutPercent=cut});Stake(db,round,a,2,first);Stake(db,round,b,2,second);Stake(db,round,c,3,1399);db.CloseBetRound(round,2);
    var tickets=db.BetTickets(round);long expectedA=first+(long)decimal.Floor((1399-(long)decimal.Floor(1399*cut/100m))*first/(decimal)(first+second));
    Check(tickets.Single(t=>t.Account==a).Payout==expectedA,"payout formula without float drift");Check(db.BetClaims+db.Balance("system:betfees")==first+second+1399,"pools conserve every dollar including rounding");db.Verify();
}
foreach(int winner in new[]{0,2,3,9})foreach(bool refund in new[]{false,true})
{
    using var db=Open($"one-{winner}-{refund}");db.StartBetRound("r",rules with{RefundOnDrawOrRestart=refund,RefundIfNoWinningBets=refund});Stake(db,"r",a,2,1000);db.CloseBetRound("r",winner);
    bool returns=winner is 2 or 9||refund;Check(db.BetClaims==(returns?1000:0),"draw/no winners/no opposing bets refund policy");db.Verify();
}
using(var db=Open("uncertain"))
{
    db.Open(a,"A");db.Open(b,"B");db.StartBetRound("r",rules);db.PrepareBetStake("uncertain","r",a,"session",2,2000,1000);Stake(db,"r",b,3,1000);db.CloseBetRound("r",2);
    Check(db.BettingRound("r")!.Status=="closing"&&db.BetClaims==0,"unconfirmed stake delays payout calculation");db.Verify();
}
using(var db=Open("uncertain"))
{
    db.RecoverBetRounds();Check(db.Blocked(a),"uncertain native effect survives reopen");db.ResolveCash("uncertain",true,"admin-test");
    Check(db.BetTickets("r").Single(t=>t.Account==a).Claim==2000,"late confirmed stake settles original winner");
    db.ResolveCash("uncertain",true,"same-again");Check(db.BetClaims==2000,"duplicate resolution cannot pay twice");db.Verify();
}
using(var db=Open("restart")){db.StartBetRound("r",rules);Stake(db,"r",a,2,1000);}
using(var db=Open("restart")){db.RecoverBetRounds();Check(db.BetClaims==1000,"unfinished round refunds after restart");db.RecoverBetRounds();Check(db.BetClaims==1000,"repeat recovery cannot double claims");db.PayBetClaim("offline","r",a,"bank",0,0,1000);db.Verify();}
using(var db=Open("limit",settings with{MaxBank=500}))
{
    db.StartBetRound("r",rules);Stake(db,"r",a,2,1000);db.CloseBetRound("r",2);Reject(()=>db.PayBetClaim("too-much","r",a,"bank",0,0,1000),"bank cap rejects whole over-limit transaction");
    Check(db.BetClaims==1000&&db.Balance(a)==0,"failed payout leaves claim intact");db.PayBetClaim("half","r",a,"bank",0,0,500);Check(db.BetClaims==500,"overflow waits instead of disappearing");db.Verify();
}
using(var db=Open("queue",settings with{MaxBank=100}))
{
    db.Open(a,"A");db.Adjust("bank-full",1,a,a,100,"set");
    for(int n=0;n<130;n++){string id="q"+n;db.StartBetRound(id,rules);Stake(db,id,a,2,100);db.CloseBetRound(id,2);}
    db.StartBetRound("z-last",rules);Stake(db,"z-last",b,2,100);db.CloseBetRound("z-last",2);
    Check(db.BankPayableClaims().Single().Account==b,"full bank claims do not starve later eligible accounts");db.Verify();
}
using(var db=Open("empty-reset"))
{
    db.Open(a,"A");db.StartBetRound("r",rules);db.Reset("reset",1,a,settings,DateTime.Now);
    Reject(()=>db.PrepareBetStake("stale","r",a,"session",2,1000,100),"empty old round closed by bank reset");db.Verify();
}
bool armedFault=false;using(var db=Open("rollback",fault:phase=>{if(armedFault&&phase=="after-posting")throw new IOException("simulated");}))
{
    db.Open(a,"A");db.StartBetRound("r",rules);db.PrepareBetStake("stake","r",a,"session",2,1000,1000);armedFault=true;
    try{db.ResolveCash("stake",true,"test");throw new Exception("missing failure");}catch(IOException){}armedFault=false;
    Check(db.Blocked(a)&&db.Balance("system:betpool")==0&&db.BetTickets("r")[0].Status=="pending","stake journal and ticket roll back together");db.Verify();
}
string crash=Path.Combine(root,"crash.sqlite3");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--crash");start.ArgumentList.Add(crash);
using(var child=Process.Start(start)!){await child.WaitForExitAsync();Check(child.ExitCode==24,"real process killed during stake commit");}
using(var db=new Ledger(crash,settings)){Check(db.Blocked(a)&&db.Balance("system:betpool")==0,"crash does not leave a half-committed bet");db.RecoverBetRounds();db.ResolveCash("stake",true,"review");Check(db.BetClaims==1000,"crashed round refunds reviewed cash after restart");db.Verify();}
// Schema 1 upgrade preserves a real bank ledger and prevents unsafe old-version access.
string migration=Path.Combine(root,"migration.sqlite3");using(var db=new Ledger(migration,settings)){db.Open(a,"A");db.Adjust("seed",1,a,a,1234,"set");}
using(var sql=new SqliteConnection($"Data Source={migration};Pooling=False")){sql.Open();using var cmd=sql.CreateCommand();cmd.CommandText="DROP TABLE bet_cash; DROP TABLE bet_tickets; DROP TABLE bet_rounds; PRAGMA user_version=1;";cmd.ExecuteNonQuery();}
using(var db=new Ledger(migration,settings)){Check(db.Balance(a)==1234,"schema migration preserves bank balance and ledger");db.Verify();}
var context=new BetContext(true,true,false,true,0,2,2);
foreach(int t in Enumerable.Range(0,7))foreach(int ct in Enumerable.Range(0,7))foreach(bool warmup in new[]{false,true})foreach(bool active in new[]{false,true})
    Check((BettingPolicy.Denied(rules,context with{T=t,CT=ct,Warmup=warmup,Active=active}) is null)==(t>=2&&ct>=2&&!warmup&&active),"live human threshold / round / warmup");
foreach(double seconds in new[]{-1,0,59.999,60,60.001,180})foreach(bool freeze in new[]{false,true})
    Check((BettingPolicy.Denied(rules with{OpenMode="Time"},context with{Seconds=seconds,Freeze=freeze}) is null)==(seconds>=0&&seconds<=60),"time starts at round_start including freeze");
Check(BettingPolicy.Amount("all",16000,rules)==16000,"all intentionally bypasses normal max");Reject(()=>BettingPolicy.Amount("5001",16000,rules),"explicit amount respects max");
Check(BettingPolicy.Denied(rules with{Enabled=false},context) is not null,"disabled betting rejected");
Check(BettingPolicy.Denied(rules,context with{Known=false}) is not null,"unknown round never accepts bets");
Check(BettingPolicy.Denied(rules with{OpenMode="Time"},context with{Seconds=double.NaN}) is not null,"invalid clock rejected");
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus"),RejectEntityAccess=true};var plugin=new Plugin(world.Core);plugin.Load(false);Check(world.EntityAccesses==0,"cold load does not inspect native entities");
plugin.BetCommand(world.Context);Check(world.Labels.Count==5,"bet main four actions and back");await world.Click(world.FindExact("T"));
for(int page=0;page<5;page++){Check(world.Current!.Configuration.MaxVisibleItems==5,"bet amount pages bounded");if(!world.Labels.Any(x=>x.Contains("Nächste")))break;await world.Click("Nächste");}
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);var toggle=world.Find("Wetten: AN");world.Permissions.Clear();await world.Click(toggle);
Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.Betting.Enabled,"revoked admin cannot switch betting");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);
Check(!JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.Betting.Enabled,"betting can be disabled independently");plugin.BankCommand(world.Context);Check(world.Labels.Any(x=>x.Contains("Einzahlen")),"bank remains available when betting off");
Check(world.AllMenus.All(m=>m.Configuration.MaxVisibleItems==5&&(m.Configuration.MaxVisibleItems==5||m.Configuration.Title.Contains("Bank & Bankraub"))),"all Economy pages including settings at most five rows");plugin.Unload();
var staleWorld=new MenuWorld{DataDirectory=Path.Combine(root,"stale-session")};var stalePlugin=new Plugin(staleWorld.Core);stalePlugin.Load(false);
var staleBank=(Ledger)typeof(Plugin).GetField("bank",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(stalePlugin)!;
string player=staleWorld.Player.SteamID.ToString();staleBank.Open(player,"Player");staleBank.StartBetRound("stale",rules);var pending=staleBank.PrepareBetStake("stale-cash","stale",player,staleWorld.Session.ToString(),2,1000,100);staleWorld.Session++;
try{typeof(Plugin).GetMethod("ExecuteBetCash",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(stalePlugin,[staleWorld.Player,pending,0u,(byte)2,(Func<bool>)(()=>true)]);throw new Exception("stale native cash unexpectedly executed");}
catch(TargetInvocationException e) when(e.InnerException is BankRejected){}
Check(staleBank.Blocked(player)&&staleBank.Balance("system:betpool")==0,"old connection cannot mutate native cash or finalize its stake");stalePlugin.Unload();
var resetWorld=new MenuWorld{DataDirectory=Path.Combine(root,"deferred-reset"),RejectEntityAccess=true};
using(var db=new Ledger(Path.Combine(resetWorld.DataDirectory,"economy.sqlite3"),settings))
{
    db.CheckReset(settings,DateTime.Now.AddMonths(-1));db.Open(a,"A");db.StartBetRound("old",rules);db.PrepareBetStake("unclear","old",a,"session",2,1000,100);
}
var resetPlugin=new Plugin(resetWorld.Core);resetPlugin.Load(false);
using(var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(resetWorld.DataDirectory,"status.json"))))
    Check(status.RootElement.GetProperty("healthy").GetBoolean()&&status.RootElement.GetProperty("resetDelay").ValueKind==JsonValueKind.String,"pending reset after restart does not falsely mark database corrupt");
var resetBank=(Ledger)typeof(Plugin).GetField("bank",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(resetPlugin)!;
resetBank.ResolveCash("unclear",false,"review");resetPlugin.Tick();
using(var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(resetWorld.DataDirectory,"status.json"))))
    Check(status.RootElement.GetProperty("resetDelay").ValueKind==JsonValueKind.Null&&resetBank.Period==2,"reviewed pending operation unblocks calendar reset and clears delay");
resetPlugin.Unload();
Check(new[]{"bt","wette","wetten"}.All(alias=>typeof(Plugin).GetMethod(nameof(Plugin.BetCommand))!.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias)),"reference betting aliases preserved");
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,schemaMigrationVerified=true,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string()};
File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BettingChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
