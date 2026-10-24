using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;
const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
var settings=new EconomySettings{ResetMode="Never"};
void Seed(Ledger db,long amount=100000){foreach(var id in new[]{a,b,c}){db.Open(id,id[^1..]);db.Adjust("seed:"+id,1,a,id,amount,"set");}}
if(args is ["--crash",var path,var crashKind])
{
    bool armed=false;using var db=new Ledger(path,settings,phase=>{if(armed&&phase==crashKind)Environment.Exit(27);});Seed(db);
    if(crashKind=="after-bounty-funded"){armed=true;db.PlaceBounty("o",1,a,b,5000,"Bank",settings.Bounty);}
    else if(crashKind=="after-bounty-claimed"){db.PlaceBounty("o",1,a,b,5000,"Bank",settings.Bounty);armed=true;db.ClaimBounty("e",c,b,false);}
    else if(crashKind=="after-bounty-refund"){db.PlaceBounty("o",1,a,b,5000,"Bank",settings.Bounty);armed=true;db.RefundBounties(b,"disconnect");}
    else if(crashKind=="after-bounty-paid"){db.PlaceBounty("o",1,a,b,5000,"Bank",settings.Bounty);db.ClaimBounty("e",c,b,false);armed=true;db.PayBountyClaim("pay","kill:e",c,"s",15000,1000,4000);}
    else{db.PlaceBounty("o",1,a,b,5000,"Cash",settings.Bounty,false,"s",10000);armed=true;db.ResolveCash("o",true,"native");}return;
}
int checks=0;void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
void Reject(Action action,string message){bool rejected=false;try{action();}catch(BankRejected){rejected=true;}Check(rejected,message);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BountyChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? s=null)=>new(Path.Combine(root,name+".sqlite3"),s??settings);
using(var db=Open("bank"))
{
    Seed(db);var o=db.PlaceBounty("one",1,a,b,20000,"Bank",settings.Bounty);Check(o.Status=="active"&&db.Balance(a)==80000,"large bounty ignores old10000 max");db.PlaceBounty("one",1,a,b,20000,"Bank",settings.Bounty);Check(db.Balance(a)==80000&&db.TitlesProgress(a).Values["BountiesSet"]==1,"same order funded/progress once");
    Reject(()=>db.PlaceBounty("one",1,a,c,20000,"Bank",settings.Bounty),"different target same ID rejected");Reject(()=>db.PlaceBounty("self",1,a,a,2000,"Bank",settings.Bounty),"self blocked");Reject(()=>db.PlaceBounty("poor",1,a,c,90000,"Bank",settings.Bounty),"cannot overdraw");
    db.PlaceBounty("two",1,c,b,3000,"Bank",settings.Bounty);Check(db.BountyTotal(b)==23000,"stacked sponsor total");Reject(()=>db.PlaceBounty("no-stack",1,a,b,1000,"Bank",settings.Bounty with{AllowStacking=false}),"stacking disabled includes existing orders");
    Check(db.ClaimBounty("tk",c,b,true)==0&&db.BountyTotal(b)==23000,"teamkill no claim");Check(db.ClaimBounty("suicide",b,b,false)==0,"suicide no claim");
    Check(db.ClaimBounty("kill",c,b,false)==23000&&db.BountyTotal(b)==0,"one killer receives all active orders");Check(db.ClaimBounty("kill",c,b,false)==0,"duplicate kill no claim");Reject(()=>db.ClaimBounty("kill",a,b,false),"conflicting event rejected");
    Check(db.BountyClaims() is [{Remaining:23000}]&&db.TitlesProgress(c).Values["BountiesClaimed"]==1,"claim count per kill not per sponsor");Check(db.OwnedTitles(c).Any(t=>t.Id=="ruler_bounty_claim")&&db.OwnedTitles(a).Any(t=>t.Id=="bounty_set_1"),"bounty progress unlocks original titles and ruler");
    var intent=db.PayBountyClaim("pay","kill:kill",c,"s",15000,1000,22000)!;Check(intent.After==16000&&db.Balance(c)==119000&&db.PendingCount==1,"cash first and bank overflow reserved atomically");db.Verify();
    db.PayBountyClaim("pay","kill:kill",c,"s",15000,1000,22000);Check(db.Balance(c)==119000,"duplicate payout not repeated");Reject(()=>db.PayBountyClaim("pay","kill:kill",a,"s",15000,1000,22000),"payout identity conflict");
    db.ResolveCash("pay",false,"admin");Check(db.BountyClaims() is [{Remaining:1000}]&&db.Balance("system:escrow")==0,"rejected cash restores only cash claim, not bank already paid");db.PayBountyClaim("retry","kill:kill",c,"bank",0,0,1000);Check(db.Balance(c)==120000&&db.BountyClaims().Length==0,"restored claim paid once");db.Verify();
}
using(var db=Open("cash"))
{
    Seed(db);db.PlaceBounty("cash",1,a,b,3000,"Cash",settings.Bounty,false,"session",8000);Check(db.Balance(a)==100000&&db.BountyTotal(b)==0&&db.PendingCount==1,"cash funding pending does not charge bank or advertise bounty");db.Verify();
    Reject(()=>db.PlaceBounty("second",1,a,c,1000,"Bank",settings.Bounty),"pending cash blocks other debit");Reject(()=>db.PlaceBounty("cash",1,a,b,3000,"Cash",settings.Bounty,false,"different",8000),"cash session conflict");
    Reject(()=>db.PlaceBounty("cash",1,a,b,3000,"Cash",settings.Bounty,false,"session",9000),"same cash request cannot change original cash value");
    db.ResolveCash("cash",true,"native");db.ResolveCash("cash",true,"native");Check(db.BountyTotal(b)==3000&&db.Balance(a)==100000&&db.TitlesProgress(a).Values["BountiesSet"]==1,"confirmed cash uses cash source, one title event");
    db.RefundBounties(b,"disconnect");db.RefundBounties(b,"disconnect");Check(db.BountyClaims() is [{Remaining:3000,Mode:"CashThenBankOverflow",Kind:"refund"}],"cash refund saved once and retains mode");
    db.PayBountyClaim("offline","refund:cash",a,"bank",0,0,3000);Check(db.Balance(a)==103000,"offline cash sponsor refunded to bank");db.Verify();
}
foreach(bool confirmed in new[]{false,true})using(var db=Open("late-refund"+confirmed))
{
    Seed(db);db.PlaceBounty("p",1,a,b,5000,"Cash",settings.Bounty,false,"s",5000);db.RefundBounties(b,"disconnect");Check(db.PendingCount==1&&db.BountyClaims().Length==0,"disconnect while uncertain does not invent refund");db.Verify();
    db.ResolveCash("p",confirmed,"admin");Check(db.BountyTotal(b)==0&&db.BountyClaims().Sum(x=>x.Remaining)==(confirmed?5000:0),"confirmed delayed stake becomes refund; cancelled stake creates no money");Check(db.TitlesProgress(a).Values.GetValueOrDefault("BountiesSet")== (confirmed?1:0),"unconfirmed placement not counted");db.Verify();
}
using(var db=Open("death-before-confirmation")){Seed(db);db.PlaceBounty("p",1,a,b,2000,"Cash",settings.Bounty,false,"s",3000);Check(db.ClaimBounty("death",c,b,false)==0,"unconfirmed bounty not awarded");db.ResolveCash("p",true,"admin");Check(db.BountyClaims() is [{Account:a,Kind:"refund",Remaining:2000}],"late confirmed stake refunded to sponsor after victim death");db.Verify();}
using(var db=Open("reset"))
{
    Seed(db);db.PlaceBounty("carry",1,a,b,10000,"Bank",settings.Bounty);db.BountySurvivors("r",1,[]);db.BountySurvivors("r",1,[]);Check(db.TitlesProgress(b).Values["BountySurvivedRounds"]==1,"survivor counted once per round");db.BountySurvivors("dead",1,[b]);Check(db.TitlesProgress(b).Values["BountySurvivedRounds"]==1,"death disqualifies survivor");
    db.Reset("reset",1,a,settings,DateTime.Now);Check(db.Period==2&&db.BountyTotal(b)==10000&&db.Balance(a)==0,"paid bounty carried across bank period as actual reference");Check(db.TitlesProgress(a).Values.GetValueOrDefault("BountiesSet")==0&&db.TitlesProgress(b).Values.GetValueOrDefault("BountySurvivedRounds")==0,"title progress resets independently");
    Check(db.ClaimBounty("after-reset",c,b,false)==10000,"carried order claimable in new period");Reject(()=>db.Reset("blocked",2,a,settings,DateTime.Now),"unpaid claim delays reset");db.PayBountyClaim("bank-pay","kill:after-reset",c,"bank",0,0,10000);db.Verify();
}
using(var db=Open("settings"))
{
    Seed(db);var small=settings.Bounty with{LargeBountyAmountsEnabled=false};Reject(()=>db.PlaceBounty("limit",1,a,b,15000,"Bank",small),"configured maximum honored when large disabled");db.PlaceBounty("percent",1,a,b,15000,"Bank",small,true);Check(db.BountyTotal(b)==15000,"bank percent bypasses fixed max as reference");
    Reject(()=>db.PlaceBounty("badpercent",1,a,b,2000,"Cash",small,true,"s",5000),"percent cannot use cash");db.Configure(settings with{Bounty=settings.Bounty with{Enabled=false}});Check(db.ClaimBounty("disabled",c,b,false)==0,"disabled new claim deferred");db.RefundBounties(b,"round end");db.PayBountyClaim("refund","refund:percent",a,"bank",0,0,15000);Check(db.Balance(a)==100000,"accepted refund finishes while disabled");db.Verify();
}
using(var db=Open("cap",settings with{MaxBank=100000}))
{
    Seed(db);db.PlaceBounty("a",1,a,b,10000,"Bank",settings.Bounty);db.ClaimBounty("e",c,b,false);Reject(()=>db.PayBountyClaim("full","kill:e",c,"bank",0,0,10000),"bank cap preserves whole claim");Check(db.BountyClaims()[0].Remaining==10000,"claim retained after full account");db.Adjust("space",1,a,c,5000,"remove");db.PayBountyClaim("part","kill:e",c,"bank",0,0,5000);Check(db.BountyClaims()[0].Remaining==5000,"partial payment retains remainder");db.Verify();
}
foreach(string mode in new[]{"Bank","Cash","CashThenBankOverflow"})using(var db=Open("mode"+mode,settings with{Bounty=settings.Bounty with{RewardMode=mode}}))
{
    Seed(db);db.PlaceBounty("o",1,a,b,5000,"Bank",settings.Bounty);db.ClaimBounty("e",c,b,false);
    if(mode=="Bank"){Reject(()=>db.PayBountyClaim("wrong","kill:e",c,"s",15000,1000,4000),"bank reward cannot become cash");db.PayBountyClaim("pay","kill:e",c,"bank",0,0,5000);}
    else{var intent=db.PayBountyClaim("pay","kill:e",c,"s",15000,1000,mode=="Cash"?0:4000,mode=="Cash"?4000:0);db.ResolveCash(intent!.Id,true,"native");Check(db.Balance("system:bountycap")== (mode=="Cash"?4000:0),"cash-only overflow explicitly journalled");}
    Check(db.BountyClaims().Length==0,"mode settled "+mode);db.Verify();
}
using(var db=Open("grant")){Seed(db);db.PlaceBounty("grant",1,a,b,2000,"Bank",settings.Bounty with{TakeMoneyImmediately=false});Check(db.Balance(a)==100000&&db.Balance("system:bountygrant")==-2000,"non-immediate legacy mode explicitly system-funded");db.Verify();}
using(var db=Open("int-stack")){Seed(db,10_000_000_000);db.PlaceBounty("large1",1,a,b,int.MaxValue,"Bank",settings.Bounty);db.PlaceBounty("large2",1,c,b,int.MaxValue,"Bank",settings.Bounty);Check(db.BountyTotal(b)==2L*int.MaxValue,"stacked total cannot overflow legacy int");Check(db.ClaimBounty("big",c,b,false)==2L*int.MaxValue,"large aggregate claim safe");db.PayBountyClaim("big-pay","kill:big",c,"bank",0,0,2L*int.MaxValue);db.Verify();}
foreach(bool bankFirst in new[]{false,true})foreach(bool warmup in new[]{false,true})foreach(bool all in new[]{false,true})
{
    var s=settings.Bounty with{PreferBankForDirectBountyCommands=bankFirst};Check(BountyPolicy.Choose(s,10000,5000,2000,all,warmup)==(bankFirst||warmup?"Bank":"Cash"),"source preference respects warmup and all");
}
Check(BountyPolicy.Choose(settings.Bounty,500,5000,3000,false,false)=="Cash","fall back from insufficient bank to cash");Reject(()=>BountyPolicy.Choose(settings.Bounty,2000,2000,3000,false,false),"never combine bank and cash for direct request");
foreach(var balance in new long[]{0,1000,99999,9_000_000_000_000_000})foreach(int percent in new[]{10,20,30,40,50,100})Check(BountyPolicy.Percent(balance,percent)==Math.Min(int.MaxValue,(long)decimal.Floor(balance*(decimal)percent/100)),"percent exact bounded integer");
Check(Plugin.LegacyAmount("1m")=="1000000"&&Plugin.LegacyAmount("50k")=="50000"&&Plugin.LegacyAmount("alles")=="all","legacy compact/all amounts");
foreach(string kind in new[]{"after-bounty-funded","after-bounty-claimed","after-bounty-refund","after-bounty-paid","after-bounty-cash"})
{
    string file=Path.Combine(root,kind+".sqlite3");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--crash");start.ArgumentList.Add(file);start.ArgumentList.Add(kind);
    using var child=Process.Start(start)!;if(!child.WaitForExit(30000)){child.Kill();throw new Exception("Crash process timeout");}Check(child.ExitCode==27,"real process terminated "+kind);
    using var db=new Ledger(file,settings);db.Verify();
    if(kind=="after-bounty-funded")Check(db.BountyTotal(b)==0&&db.Balance(a)==100000,"funding and order roll back together");
    else if(kind is "after-bounty-claimed" or "after-bounty-refund")Check(db.BountyTotal(b)==5000&&db.BountyClaims().Length==0,"claim/refund and source order roll back together");
    else if(kind=="after-bounty-paid")Check(db.BountyClaims() is [{Remaining:5000}]&&db.PendingCount==0&&db.Balance(c)==100000,"payout bank+cash and claim roll back together");
    else{Check(db.PendingCount==1&&db.BountyTotal(b)==0,"interrupted cash confirmation stays pending");db.ResolveCash("o",true,"checked");db.ResolveCash("o",true,"checked");Check(db.BountyTotal(b)==5000&&db.TitlesProgress(a).Values["BountiesSet"]==1,"cash recovery settles once");}db.Verify();
}
// Repeat varied settlement sequences with invariant verification between every stage.
using(var db=Open("sequence"))
{
    Seed(db,1000000);for(int n=0;n<24;n++)
    {
        string id="sequence"+n;long amount=1000+n*37;db.PlaceBounty(id,1,a,b,amount,"Bank",settings.Bounty);db.Verify();
        if(n%2==0)db.RefundBounties(b,"disconnect");else db.ClaimBounty(id,c,b,false);db.Verify();var claim=db.BountyClaims().Single();db.PayBountyClaim("pay"+n,claim.Id,claim.Account,"bank",0,0,claim.Remaining);db.Verify();Check(db.BountyClaims().Length==0&&db.BountyTotal(b)==0,"balanced repeated sequence "+n);
    }
}
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus")};Directory.CreateDirectory(world.DataDirectory);var plugin=new Plugin(world.Core);plugin.Load(false);plugin.BountyCommand(world.Context);Check(world.Labels.Any(x=>x.Contains("Kopfgeld aussetzen"))&&world.Current!.Configuration.MaxVisibleItems==5,"short public root with independent groups");
var command=typeof(Plugin).GetMethod(nameof(Plugin.BountyCommand))!;foreach(string alias in new[]{"kopfgeld","bounty"})Check(command.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias&&x.RegisterRaw),"legacy raw console registration "+alias);Check(command.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias=="bounty"&&!x.RegisterRaw),"prefixed bounty alias retained");
plugin.TitleCommand(world.Context);await world.Click("Fortschritt");while(!world.Labels.Any(x=>x.Contains("Kopfgeld"))&&world.Labels.Any(x=>x.Contains("Nächste")))await world.Click("Nächste");Check(world.Labels.Any(x=>x.Contains("Kopfgeld"))&&TitleCatalog.All.Count(t=>TitlePolicy.Group(t)=="Kopfgeld")==16,"all sixteen bounty titles now accessible in own subgroup");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);var toggle=world.Find("Kopfgeld: AN");world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.Bounty.Enabled,"permission removed blocks old admin callback");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);plugin.BountyCommand(world.Context);Check(world.Messages.Last().Contains("ausgeschaltet"),"old command obeys inactive feature");
Check(world.AllMenus.All(m=>m.Configuration.MaxVisibleItems==5),"all bounty admin pages show five rows");plugin.Unload();
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,atomicFunding=true,preservedClaims=true,periodCarryVerified=true,legacyCommandsVerified=true,clientAcceptancePending=true};File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BountyChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
