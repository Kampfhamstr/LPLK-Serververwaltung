using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using Microsoft.Data.Sqlite;
using SwiftlyS2.Shared.Commands;

const string a="76561198000000001",b="76561198000000002";
var settings=new EconomySettings{TitleSystem=new(){Enabled=false}}; // Core Lotto tests isolate title bonuses; TitlesChecks covers their integration.
var rules=new LotterySettings();
if(args is ["--crash",var file,var operation])
{
    bool armed=false;using var db=new Ledger(file,settings,phase=>{if(armed&&phase=="after-posting")Environment.Exit(25);});
    db.Open(a,"A");db.Adjust("seed",1,a,a,10000,"set");db.StartLotteryRound("r",rules);
    if(operation=="buy"){armed=true;db.BuyLottery("ticket","r",a,2);}
    else{db.BuyLottery("ticket","r",a,2);armed=true;db.CloseLotteryRound("r",true,_=>0);}return;
}
int checks=0;void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
void Reject(Action action,string why){bool bad=false;try{action();}catch(BankRejected){bad=true;}Check(bad,why);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"LotteryChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? config=null,Action<string>? fault=null)=>new(Path.Combine(root,name+".sqlite3"),config??settings,fault);
void Seed(Ledger db,long amount=10000){db.Open(a,"A");db.Open(b,"B");db.Adjust("a",1,a,a,amount,"set");db.Adjust("b",1,b,b,amount,"set");}
using(var db=Open("basic"))
{
    Seed(db);db.StartLotteryRound("r",rules);db.BuyLottery("one","r",a,2);db.BuyLottery("two","r",b,1);
    long n=db.TransactionCount;db.BuyLottery("one","r",a,2);Check(n==db.TransactionCount,"repeat buy does not debit twice");Reject(()=>db.BuyLottery("one","r",a,3),"changed duplicate rejected");
    Check(db.Balance(a)==9000&&db.Balance("system:lotpool")==1500,"atomic ticket debit and pool");Reject(()=>db.BuyLottery("over","r",a,9),"single limit includes existing buys");
    Reject(()=>db.Reset("reset",1,a,settings,DateTime.Now),"open tickets block reset");
    db.CloseLotteryRound("r",true,_=>0);Check(db.Balance(a)==10200&&db.Superpot==150&&db.Balance("system:lotfees")==150,"reference 80/10/10 split");
    n=db.TransactionCount;db.CloseLotteryRound("r",false);Check(n==db.TransactionCount&&db.GetLotteryRound("r")!.Status=="drawn","repeated changed outcome ignored");
    db.Verify();db.Reset("reset",1,a,settings,DateTime.Now);Check(db.Superpot==0&&db.Period==2,"bank reset includes superpot");Reject(()=>db.BuyLottery("one","r",a,2),"old period replay rejected");db.Verify();
}
// Every possible ticket position selects its actual owner, including split buys; percentages conserve money.
foreach(int price in new[]{1,7,499,500})foreach(int share in new[]{0,10,33,100})foreach(int roll in Enumerable.Range(0,6))
{
    using var db=Open($"distribution-{price}-{share}-{roll}");Seed(db);
    var s=rules with{TicketPrice=price,SuperpotPercent=share,HouseCutPercent=0,RoundPrizePercent=100-share,JackpotEnabled=false};db.StartLotteryRound("r",s);
    db.BuyLottery("a1","r",a,1);db.BuyLottery("b1","r",b,3);db.BuyLottery("c1","r",a,2);
    int calls=0;db.CloseLotteryRound("r",true,_=>calls++==0?roll:9999);var r=db.GetLotteryRound("r")!;
    Check(r.Winner==(roll==0||roll>=4?a:b),"weighted winner exact ticket intervals");
    Check(db.Balance(a)+db.Balance(b)+db.Superpot+db.Balance("system:lotfees")==20000,"all money conserved including rounding");db.Verify();
}
using(var db=Open("jackpot",settings with{MaxBank=1000}))
{
    Seed(db,1000);db.StartLotteryRound("r",rules with{JackpotChanceBasisPoints=10000,MinSuperpotForJackpot=0});db.BuyLottery("one","r",a,1);db.BuyLottery("two","r",b,1);
    db.CloseLotteryRound("r",true,_=>0);Check(db.Balance(a)==1000&&db.Superpot==400&&db.Balance(b)==500&&db.Balance("system:lotfees")==100,"bank cap and jackpot overflow remain in superpot as reference");db.Verify();
}
using(var db=Open("pending"))
{
    Seed(db);db.StartLotteryRound("r",rules);db.BuyLottery("one","r",a,1);db.BuyLottery("two","r",b,1);db.PrepareCash("cash",1,a,"session",1000,1100);
    db.CloseLotteryRound("r",true,_=>0);Check(db.GetLotteryRound("r")!.Status=="closing"&&db.Superpot==0,"uncertain winning account postpones settlement");
    db.CloseLotteryRound("r",false);db.ResolveCash("cash",false,"review");db.ResumeLottery();Check(db.GetLotteryRound("r")!.Winner==a&&db.Balance(a)==10300,"original draw survives delayed cash resolution");db.Verify();
}
using(var db=Open("refund",settings with{MaxBank=1000}))
{
    Seed(db,1000);db.StartLotteryRound("r",rules);db.BuyLottery("one","r",a,1);db.Adjust("full",1,a,a,1000,"set");db.ResumeLottery(true);
    Check(db.LotteryRefunds==500&&db.Balance(a)==1000,"refund retained at cap instead of clipped");db.Adjust("room",1,a,a,200,"remove");db.PayLotteryRefunds();Check(db.LotteryRefunds==300&&db.Balance(a)==1000,"partial refund preserves rest");
    db.Adjust("room2",1,a,a,500,"remove");db.PayLotteryRefunds();db.PayLotteryRefunds();Check(db.LotteryRefunds==0&&db.Balance(a)==800,"refund once after room available");db.Verify();
}
using(var db=Open("no-refund"))
{
    Seed(db);db.StartLotteryRound("r",rules with{RefundOnInvalidRoundEnd=false});db.BuyLottery("one","r",a,1);db.ResumeLottery(true);Check(db.Balance(a)==9500&&db.Balance("system:lotfees")==500,"configured invalid-round forfeiture journaled");db.Verify();
}
using(var db=Open("permanent"))
{
    Seed(db,1000);var s=rules with{PermanentUnlockTicketThresholds=[2,4]};db.SetLotteryPlan("plan",1,a,5,0,s);db.StartLotteryRound("r",s);db.BuyLottery("auto","r",a,0,true);
    Check(db.LotteryTickets("r").Single().Count==2&&db.Balance(a)==0,"automatic buys only affordable count");Check(s.PermanentLimit(db.LotteryPlan(a).Bought)==6,"purchased auto tickets unlock slots");
    long n=db.TransactionCount;db.BuyLottery("auto","r",a,0,true);db.BuyLottery("other-auto","r",a,0,true);Check(db.TransactionCount==n,"automatic subscription once per round");
    Reject(()=>db.SetLotteryPlan("stale",1,a,6,1,s),"buy progress invalidates stale plan revision");
    db.SetLotteryPlan("cancel",1,a,0,db.LotteryPlan(a).Revision,s);Check(db.LotteryTickets("r").Length==1,"cancelling subscription preserves purchased tickets");
    db.CloseLotteryRound("r",false);db.PayLotteryRefunds();db.SetLotteryPlan("six",1,a,6,db.LotteryPlan(a).Revision,s);
    db.Reset("reset",1,a,settings with{Lotto=s},DateTime.Now);Check(db.LotteryPlan(a).Count==5&&db.LotteryPlan(a).Bought==0,"reset clears unlock progress and trims to base slots");db.Verify();
}
using(var db=Open("preserve-reset"))
{
    Seed(db);var s=rules with{ResetSuperpotWithBankReset=false,ResetPermanentUnlocksWithBankReset=false};db.StartLotteryRound("r",s);db.BuyLottery("one","r",a,1);db.CloseLotteryRound("r",true,_=>0);
    db.Reset("reset",1,a,settings with{Lotto=s},DateTime.Now);Check(db.Superpot==50&&db.LotteryPlan(a).Bought==1,"configured cross-period pot and progress preserved");db.Verify();
}
// An actual process exits after a posting; committed draw choice is replayed, incomplete postings roll back.
foreach(string action in new[]{"buy","draw"})
{
    string path=Path.Combine(root,"crash-"+action+".sqlite3");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
    if(Path.GetFileNameWithoutExtension(Environment.ProcessPath) is "dotnet")start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--crash");start.ArgumentList.Add(path);start.ArgumentList.Add(action);
    using(var child=Process.Start(start)!){await child.WaitForExitAsync();Check(child.ExitCode==25,"actual process interrupted in "+action);}
    using var db=new Ledger(path,settings);Check(db.Balance(a)==(action=="buy"?10000:9000),"partial account postings rolled back");
    if(action=="buy")Check(db.LotteryTickets("r").Length==0,"no free or charged phantom ticket after interruption");
    else {Check(db.GetLotteryRound("r")!.Status=="closing","draw choice committed before interrupted payout");db.ResumeLottery(true);Check(db.Balance(a)==9800&&db.Superpot==100,"saved draw finishes once after process crash");}
    db.Verify();
}
string migration=Path.Combine(root,"schema2.sqlite3");using(var db=new Ledger(migration,settings)){Seed(db);db.StartBetRound("bet",new());db.PrepareBetStake("stake","bet",a,"s",2,1000,100);}
using(var sql=new SqliteConnection($"Data Source={migration};Pooling=False")){sql.Open();using var cmd=sql.CreateCommand();cmd.CommandText="DROP TABLE lot_refunds; DROP TABLE lot_subscriptions; DROP TABLE lot_tickets; DROP TABLE lot_rounds; PRAGMA user_version=2;";cmd.ExecuteNonQuery();}
using(var db=new Ledger(migration,settings)){Check(db.Balance(a)==10000&&db.PendingCount==1&&db.OpenBetRounds==1,"schema2 migration preserves bank and uncertain wager");db.Verify();}
foreach(bool enabled in new[]{false,true})foreach(bool known in new[]{false,true})foreach(bool open in new[]{false,true})foreach(bool warmup in new[]{false,true})foreach(int players in new[]{0,1,2,10})
    Check((LotteryPolicy.Denied(rules with{Enabled=enabled},known,open,warmup,players) is null)==(enabled&&known&&open&&!warmup&&players>=2),"purchase gate matrix");
foreach(bool noWinner in new[]{false,true})foreach(bool warmup in new[]{false,true})foreach(int players in new[]{0,1,2,10})foreach(int winner in new[]{0,2,3})
    Check(LotteryPolicy.DrawAllowed(rules with{ProcessEconomyOnRoundEndWithoutWinner=noWinner},warmup,players,winner)==(!warmup&&players>=2&&(winner>0||noWinner)),"draw validity including inherited winnerless policy");
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus"),RejectEntityAccess=true};var plugin=new Plugin(world.Core);plugin.Load(false);
Check(world.EntityAccesses==0,"cold load independent of native game objects");plugin.LottoCommand(world.Context);Check(world.Labels.Count==5,"lotto main short page");await world.Click("Lose kaufen");
for(int i=0;i<5;i++){Check(world.Labels.Count<=5,"ticket menus max five rows");if(!world.Labels.Any(x=>x.Contains("Nächste")))break;await world.Click("Nächste");}
var callback=world.Current!.Options.First(o=>o.Text.Contains("Lose"));int menusBefore=world.AllMenus.Count;world.Session++;await world.Click(callback);Check(world.AllMenus.Count==menusBefore&&world.EntityAccesses==0,"old connection menu cannot advance or touch engine");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);await world.Click("Einstellungen");Check(world.Labels.Count<=5,"four settings plus back fit");while(!world.Labels.Any(x=>x.Contains("Lottoregeln")))await world.Click("Nächste");await world.Click("Lottoregeln");
var toggle=world.Find("Lotto: AN");world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.Lotto.Enabled,"lost admin permission cannot toggle lottery");
world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);plugin.LottoCommand(world.Context);Check(world.Messages.Last().Contains("nicht verfügbar"),"disabled lotto rejects public command");
await world.Click("Lose &amp; Dauerlose");Check(world.Labels.Count==5,"ticket settings short page");await world.Click("Lospreis");
for(int i=0;i<4;i++){Check(world.Labels.Count<=5,"price choices bounded");if(!world.Labels.Any(x=>x.Contains("Nächste")))break;await world.Click("Nächste");}
plugin.Unload();
var configWorld=new MenuWorld{DataDirectory=Path.Combine(root,"config-upgrade"),RejectEntityAccess=true};Directory.CreateDirectory(configWorld.DataDirectory);
var configNode=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(settings))!.AsObject();configNode.Remove("Lotto");configNode["local-note"]="preserve-me";
File.WriteAllText(Path.Combine(configWorld.DataDirectory,"settings.json"),configNode.ToJsonString());var configPlugin=new Plugin(configWorld.Core);configPlugin.Load(false);
using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(configWorld.DataDirectory,"settings.json"))))Check(doc.RootElement.GetProperty("Lotto").GetProperty("TicketPrice").GetInt64()==500&&doc.RootElement.GetProperty("local-note").GetString()=="preserve-me","missing lotto config added preserving other keys");configPlugin.Unload();
foreach(var invalid in new[]{rules with{TicketPrice=0},rules with{HouseCutPercent=11},rules with{JackpotChanceBasisPoints=10001},rules with{PermanentUnlockTicketThresholds=[2,2]},rules with{PermanentUnlockTicketThresholds=[2,1]},rules with{MaxPermanentTicketsTotal=-2}})
{bool failed=false;try{LotterySettings.Validate(invalid);}catch(InvalidDataException){failed=true;}Check(failed,"invalid economic configuration rejected");}
Check(new[]{"los","lotterie"}.All(alias=>typeof(Plugin).GetMethod(nameof(Plugin.LottoCommand))!.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias)),"reference lottery aliases");
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,schemaMigrationVerified=true,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string()};
File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"LotteryChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));




