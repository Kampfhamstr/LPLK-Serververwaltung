using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;

const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
var settings=new EconomySettings();
void Seed(Ledger db,long first=1_000_000,long second=2_000_000){db.Open(a,"A");db.Open(b,"B");db.Adjust("seed-a",1,a,a,first,"set");db.Adjust("seed-b",1,b,b,second,"set");}
if(args is ["--crash",var file,var crashKind])
{
    bool armed=false;using var db=new Ledger(file,settings,phase=>{if(armed&&phase==(crashKind=="transfer"?"after-title-progress":"after-title-round"))Environment.Exit(25);});Seed(db);armed=true;
    if(crashKind=="transfer")db.Transfer("transfer",1,a,b,100);else db.ApplyTitleRound("tax",1,[a,b],true);return;
}
int checks=0;void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
void Reject(Action action,string message){bool bad=false;try{action();}catch(BankRejected){bad=true;}Check(bad,message);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"TitlesChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? rules=null)=>new(Path.Combine(root,name+".sqlite3"),rules??settings);
using(var db=Open("bank"))
{
    Seed(db);Check(db.OwnedTitles(a).Any(t=>t.Id=="wealth_1000000"),"wealth threshold at exact balance");
    Check(!db.OwnedTitles(a).Any(t=>t.Id=="wealth_2500000"),"no future wealth title");Check(db.PioneerOwners().Values.Contains(a),"first bank milestone belongs to first actual booking");
    db.SelectTitle("select",1,a,"wealth_1000000");db.SelectTitle("select",1,a,"wealth_1000000");Check(db.ActiveTitle(a)=="Millionär","active title and idempotent selection");
    Reject(()=>db.SelectTitle("not-owned",1,a,"wealth_2500000"),"unowned title rejected");db.Transfer("tx",1,a,b,100);db.Transfer("tx",1,a,b,100);
    Check(db.TitlesProgress(a).Values["TransfersSent"]==1&&db.TitlesProgress(b).Values["TransfersReceived"]==1,"transfer progresses both accounts once");
    Check(db.TitlesProgress(a).Values["HighestBank"]==1_000_000&&db.ActiveTitle(a)=="Millionär","spending preserves historical wealth title");
    db.PrepareCash("deposit",1,a,"session",1000,500);db.ResolveCash("deposit",true,"native");db.ResolveCash("deposit",true,"native");
    db.PrepareCash("withdraw",1,a,"session",500,600);db.ResolveCash("withdraw",true,"native");db.PrepareCash("cancel",1,a,"session",600,700);db.ResolveCash("cancel",false,"native");
    Check(db.TitlesProgress(a).Values["BankMovements"]==2&&db.TitlesProgress(a).Values["Deposits"]==1&&db.TitlesProgress(a).Values["Withdrawals"]==1,"only confirmed cash changes progress");
    db.Reset("reset",1,a,settings,DateTime.Now);Check(db.OwnedTitles(a).Length==0&&db.ActiveTitle(a)==""&&db.PioneerOwners().Count==0,"period reset clears titles pioneers active choice and progress");
    Reject(()=>db.SelectTitle("stale",1,a,""),"stale period selection rejected");db.Verify();
}
using(var db=Open("leaders"))
{
    Seed(db,100000,100000);Check(db.OwnedTitles(a).Any(t=>t.Id=="ruler_bank")&&!db.OwnedTitles(b).Any(t=>t.Id=="ruler_bank"),"earlier equal balance wins");
    db.SelectTitle("king",1,a,"ruler_bank");db.Adjust("lead-b",1,b,b,1,"add");Check(db.ActiveTitle(a)==""&&db.OwnedTitles(b).Any(t=>t.Id=="ruler_bank"),"dynamic lost ruler title no longer displayed");
    db.Adjust("equal-a",1,a,a,1,"add");Check(!db.OwnedTitles(a).Any(t=>t.Id=="ruler_bank"),"later equal score cannot steal leadership");
    Check(db.RulerSlots(b)==1&&db.PermanentLotteryLimit(b,settings.Lotto,0)==6,"one ruler adds one slot");
    db.SetLotteryPlan("six",1,b,6,0,settings.Lotto);Check(db.LotteryPlan(b).Count==6,"ruler bonus accepted by subscription transaction");
    db.Transfer("ruler-transfer",1,b,a,1000);Check(db.TitlesProgress(b).Values["PermanentLottoTicketsActivated"]==6,"activated amount tracked");
    db.Configure(settings with{TitleSystem=settings.TitleSystem with{Enabled=false}});Check(db.OwnedTitles(b).Length==0&&db.PermanentLotteryLimit(b,settings.Lotto,0)==5,"disabled titles remove bonus slots");db.Verify();
}
using(var db=Open("lotto"))
{
    Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("a","r",a,2);db.BuyLottery("b","r",b,1);db.BuyLottery("a","r",a,2);db.CloseLotteryRound("r",true,_=>0);db.CloseLotteryRound("r",true,_=>0);
    Check(db.TitlesProgress(a).Values["LottoTicketsBought"]==2&&db.TitlesProgress(a).Values["LottoWins"]==1,"lottery progress and win exactly once");
    long revision=db.LotteryPlan(a).Revision;db.SetLotteryPlan("plan",1,a,3,revision,settings.Lotto);db.SetLotteryPlan("plan",1,a,3,revision,settings.Lotto);Check(db.TitlesProgress(a).Values["PermanentLottoTicketsActivated"]==3,"subscription retry cannot inflate title stats");db.Verify();
}
using(var db=Open("betting"))
{
    Seed(db);db.StartBetRound("r",settings.Betting);db.PrepareBetStake("a","r",a,"s",2,1000,1000,true);db.ResolveCash("a",true,"native");db.PrepareBetStake("b","r",b,"s",3,1000,500);db.ResolveCash("b",true,"native");db.CloseBetRound("r",2);db.CloseBetRound("r",2);
    Check(db.TitlesProgress(a).Values["AllInBets"]==1&&db.TitlesProgress(a).Values["BetsWon"]==1&&db.TitlesProgress(b).Values["BetsLost"]==1,"all-in flag and winning side recorded once");
    db.StartBetRound("refund",settings.Betting);db.PrepareBetStake("c","refund",a,"s",2,1000,100);db.ResolveCash("c",true,"native");db.CloseBetRound("refund",0);Check(db.TitlesProgress(a).Values["BetsWon"]==1,"refunded round not counted as win");db.Verify();
}
using(var db=Open("robbery"))
{
    Seed(db,5_000_000,60_000_000);const long now=1_800_000_000;var rules=settings.BankRobbery;
    db.PaySpy("spy",1,a,20000,rules);db.PaySpy("spy",1,a,20000,rules);db.BuyRobberyUpgrade("vault",1,a,"vault",1,500000,rules,now);db.BuyRobberyUpgrade("skill",1,a,"robber",1,1000000,rules,now);
    db.OrderRobbery("order",1,a,b,rules,now,100000);db.MarkRobberyRound(rules,[]);db.ResumeRobbery(now,_=>0);db.ResumeRobbery(now);
    Check(db.TitlesProgress(a).Values["SpyOrders"]==1&&db.TitlesProgress(a).Values["RobberyOrders"]==1&&db.TitlesProgress(a).Values["SuccessfulRobberies"]==1,"spy order robbery order and success once");
    Check(db.TitlesProgress(b).Values["RobberyVictimCount"]==1&&db.TitlesProgress(a).Values["SecurityBuys"]==1&&db.OwnedTitles(a).Any(t=>t.Id=="robber_skill_1"),"victim security and current robber skill progress");db.Verify();
}
Check(TitlePolicy.InterestRate(settings.InterestSystem,1,0)==1,"one normal title yields 0.01 percent");Check(TitlePolicy.InterestRate(settings.InterestSystem,0,1)==10,"one ruler yields 0.1 percent");Check(TitlePolicy.InterestRate(settings.InterestSystem,500,20)==100,"cap applies before divisor: maximum 1 percent");
foreach(var tier in settings.LuxuryTax.TaxTiers)
{
    Check(TitlePolicy.TaxRate(settings.LuxuryTax,tier.Threshold)==tier.TaxBasisPoints,"exact tax threshold");Check(TitlePolicy.TaxRate(settings.LuxuryTax,tier.Threshold-1)<tier.TaxBasisPoints,"lower tax boundary");
    using var db=Open("tax-"+tier.Threshold);Seed(db,tier.Threshold,tier.Threshold);var paid=db.ApplyTitleRound("tax",1,[a,b],true);long expected=(long)((decimal)tier.Threshold*tier.TaxBasisPoints/10000);
    Check(paid.Length==2&&paid.All(p=>p.Amount==expected)&&db.Balance(a)==tier.Threshold-expected,"highest tier taxes entire balance");
    Check(db.Superpot==2*(expected/10)&&db.Balance("system:tax")==2*(expected-expected/10),"ten percent to superpot and ninety removed balanced");
    long tx=db.TransactionCount;Check(db.ApplyTitleRound("tax",1,[b,a],true).Length==0&&db.TransactionCount==tx,"duplicate round independent of player ordering");db.Verify();
}
using(var db=Open("interest"))
{
    Seed(db,10_000_000,20_000_000);int rateA=db.TitleInterestRate(a),rateB=db.TitleInterestRate(b);var paid=db.ApplyTitleRound("interest",1,[a,b],false);
    Check(paid.Single(x=>x.Account==a).Amount==10_000_000L*rateA/10000&&paid.Single(x=>x.Account==b).Amount==20_000_000L*rateB/10000,"interest uses frozen pre-payment balances and title counts");
    long before=db.Balance(a);db.ApplyTitleRound("interest",1,[a,b],false);Check(db.Balance(a)==before,"no interest duplication");Reject(()=>db.ApplyTitleRound("interest",1,[a],false),"different replay participant set rejected");
    db.Open(c,"Offline");db.Adjust("offline",1,c,c,50_000_000,"set");db.ApplyTitleRound("next",1,[a,b],false);Check(db.Balance(c)==50_000_000,"offline accounts neither paid nor taxed");
    Check(db.ApplyTitleRound("short",1,[a],false).Length==0,"minimum active player count enforced");db.PrepareCash("uncertain",1,a,"s",1000,1100);before=db.Balance(a);db.ApplyTitleRound("blocked",1,[a,b],false);Check(db.Balance(a)==before,"uncertain cash account excluded");db.Verify();
}
using(var db=Open("overflow",settings with{MaxBank=1_000_000}))
{
    Seed(db,1_000_000,999_999);db.ApplyTitleRound("interest",1,[a,b],false);Check(db.Balance(a)==1_000_000&&db.Balance(b)==1_000_000,"interest obeys account cap as reference");db.Verify();
}
using(var db=Open("large"))
{
    Seed(db,9_000_000_000_000_000,9_000_000_000_000_000);var paid=db.ApplyTitleRound("tax",1,[a,b],true);Check(paid.All(p=>p.Amount==90_000_000_000_000)&&db.Superpot==18_000_000_000_000,"tax multiplication cannot overflow long at technical cap");db.Verify();
}
foreach(string kind in new[]{"transfer","tax"})
{
    string path=Path.Combine(root,"crash-"+kind+".sqlite3");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--crash");start.ArgumentList.Add(path);start.ArgumentList.Add(kind);
    using(var child=Process.Start(start)!){await child.WaitForExitAsync();Check(child.ExitCode==25,"real child process interrupted during "+kind);}
    using var db=new Ledger(path,settings);Check(db.Balance(a)==1_000_000&&db.Balance(b)==2_000_000&&db.Superpot==0&&db.TitlesProgress(a).Values.GetValueOrDefault("TransfersSent")==0,"interruption cannot leave money/progress partially committed");
    if(kind=="transfer"){db.Transfer("transfer",1,a,b,100);db.Transfer("transfer",1,a,b,100);Check(db.TitlesProgress(a).Values["TransfersSent"]==1,"retry records one transfer");}else{db.ApplyTitleRound("tax",1,[a,b],true);db.ApplyTitleRound("tax",1,[a,b],true);Check(db.Balance(a)==999800&&db.Balance(b)==1999600,"retry collects tax once");}db.Verify();
}
Check(TitleCatalog.All.Select(t=>t.Id).Distinct().Count()==TitleCatalog.All.Length&&TitleCatalog.All.Length>120,"unique reference title catalog");
Check(TitleCatalog.All.Where(t=>t.Id.StartsWith("bounty_")||t.Id=="ruler_bounty_claim").All(t=>TitlePolicy.Group(t)=="Kopfgeld"),"all deferred bounty titles stay out of public progress categories");
foreach(var title in TitleCatalog.All)Check(title.Text.Length>0&&title.Category.Length>0&&title.Required>=0,"valid reference title "+title.Id);
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menu"),RejectEntityAccess=true};var plugin=new Plugin(world.Core);plugin.Load(false);plugin.TitleCommand(world.Context);Check(world.Labels.Count==5,"four title categories and return");
var stale=world.Find("Freigeschaltete");world.Session++;int menus=world.AllMenus.Count;await world.Click(stale);Check(world.AllMenus.Count==menus,"old player session cannot reuse title menu");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);await world.Click("Einstellungen");while(!world.Labels.Any(x=>x.Contains("Titel &amp; Wirtschaft")))await world.Click("Nächste");await world.Click("Titel &amp; Wirtschaft");await world.Click("Titel &amp; Hinweise");var toggle=world.Find("Titel: AN");world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.TitleSystem.Enabled,"lost admin permissions block title switch");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);plugin.TitleCommand(world.Context);Check(world.Messages.Last().Contains("ausgeschaltet"),"disabled titles reject old command");plugin.Unload();
var method=typeof(Plugin).GetMethod(nameof(Plugin.TitleCommand))!;foreach(string name in new[]{"titel","title","titles"})Check(method.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==name&&x.RegisterRaw),"legacy title console entry "+name);
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,atomicProgressVerified=true,roundReplayVerified=true,legacyTitleCommandsVerified=true,referenceTitleCount=TitleCatalog.All.Length,clientAcceptancePending=true};File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"TitlesChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));

