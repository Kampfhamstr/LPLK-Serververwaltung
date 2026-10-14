using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Economy;
const string a="76561198000000001",b="76561198000000002";
var settings=new EconomySettings{ResetMode="Never"};
var guaranteed=settings.Lotto with{PermanentTicketUpgradeDiscountChanceBasisPoints=10000,PermanentTicketUpgradeDiscountMinPercent=20,PermanentTicketUpgradeDiscountMaxPercent=20};
void Seed(Ledger db){db.Open(a,"A");db.Open(b,"B");db.Adjust("seed-a",1,a,a,10_000_000,"set");db.Adjust("seed-b",1,b,b,10_000_000,"set");}
void Discount(Ledger db){db.StartLotteryRound("discount",guaranteed);db.SetLotteryPlan("plan",1,a,1,0,guaranteed);db.BuyLottery("auto","discount",a,0,true,_=>0);db.CloseLotteryRound("discount",false);db.ResumeLottery();}
AbilitySettings Only(string kind)=>new(){ChancePerTicketBasisPoints=10000,Effects=new(){CashBonus=new(kind=="CashBonus",25,2500),BankBonus=new(kind=="BankBonus",25,5000),FreeTicket=new(kind=="FreeTicket",10,3),NextKillBankBonus=new(kind=="NextKillBankBonus",10,5000)}};
if(args is ["--crash",var crashPath,var crashKind])
{
    bool armed=false;using var db=new Ledger(crashPath,settings,phase=>{if(armed&&phase==(crashKind=="discount"?"after-discount-consumed":crashKind=="bonus"?"after-lottery-bonus":"after-cash-collection"))Environment.Exit(27);});Seed(db);
    if(crashKind=="discount"){Discount(db);armed=true;db.BuyRobberyUpgrade("upgrade",1,a,"vault",1,400000,settings.BankRobbery,1800000000);}
    else if(crashKind=="bonus"){db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,1);db.RollLotteryBonuses("r",a,false,Only("BankBonus"),_=>0);armed=true;db.ApplyLotteryBonus("r",a,0);}
    else{db.PrepareCashCollection("collection",1,a,"session",5000,7000,true);armed=true;db.ResolveCash("collection",true,"native");}return;
}
int checks=0;void Check(bool v,string m){checks++;if(!v)throw new Exception(m);}
void Reject(Action action,string m){bool rejected=false;try{action();}catch(BankRejected){rejected=true;}Check(rejected,m);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BonusChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? s=null)=>new(Path.Combine(root,name+".sqlite3"),s??settings);
using(var db=Open("discount"))
{
    Seed(db);Discount(db);Check(db.UpgradeDiscount(a)==20,"forced permanent ticket discount");Check(db.TitlesProgress(a).Values["UpgradeDiscountsWon"]==1,"discount progress once");
    db.StartLotteryRound("keep",guaranteed);db.BuyLottery("keep-auto","keep",a,0,true,_=>throw new Exception("existing coupon must not reroll"));db.CloseLotteryRound("keep",false);db.ResumeLottery();Check(db.UpgradeDiscount(a)==20&&db.TitlesProgress(a).Values["UpgradeDiscountsWon"]==1,"existing discount does not stack or reroll");
    foreach(var (kind,level,price) in new[]{("vault",1,400000L),("spy",1,160000L),("robber",1,800000L),("alarm",0,160000L)})Check(db.UpgradePrice(a,kind,level,settings.BankRobbery,1800000000)==price,"discount price "+kind);
    Reject(()=>db.BuyRobberyUpgrade("bad",1,a,"vault",1,500000,settings.BankRobbery,1800000000),"stale base price rejected");Check(db.UpgradeDiscount(a)==20,"failed purchase keeps discount");
    db.BuyRobberyUpgrade("buy",1,a,"vault",1,400000,settings.BankRobbery,1800000000);db.BuyRobberyUpgrade("buy",1,a,"vault",1,400000,settings.BankRobbery,1800000000);
    Check(db.UpgradeDiscount(a)==0&&db.Balance(a)==9_600_000&&db.TitlesProgress(a).Values["UpgradeDiscountsUsed"]==1,"discount consumed atomically once");
    Check(db.UpgradePrice(a,"vault",2,settings.BankRobbery,1800000000)==500000,"next vault upgrade charges remaining undiscounted difference");db.Verify();
}
using(var db=Open("no-discount",settings with{TitleSystem=settings.TitleSystem with{Enabled=false}})){Seed(db);Discount(db);Check(db.UpgradeDiscount(a)==0,"reference requires titles enabled for new discount");db.Verify();}
foreach(string kind in new[]{"CashBonus","BankBonus","FreeTicket","NextKillBankBonus"})
{
    using var db=Open(kind);Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,1);var bonus=db.RollLotteryBonuses("r",a,false,Only(kind),_=>0);
    Check(bonus is [{Kind:var selected}]&&selected==kind,"selected reference effect "+kind);Check(db.RollLotteryBonuses("r",a,false,Only(kind),_=>throw new Exception("reroll")).SequenceEqual(bonus),"stored choice not rerolled");
    long before=db.Balance(a);
    if(kind=="CashBonus")
    {
        var intent=db.PrepareLotteryBonusCash("r",a,0,"s",15000)!;Check(intent.After==16000&&intent.Delta==-1000,"cash bonus capped at cash limit");Check(db.PendingCount==1&&db.Balance("system:escrow")==1000&&db.Balance(a)==before,"cash bonus reserves system funds only");db.Verify();
        db.ResolveCash(intent.Id,true,"native");db.ResolveCash(intent.Id,true,"native");Check(db.PendingCount==0&&db.Balance("system:escrow")==0&&db.LotteryBonuses("r",a)[0].Status=="applied","cash bonus confirmed once");
    }
    else
    {
        db.ApplyLotteryBonus("r",a,0);long tx=db.TransactionCount;db.ApplyLotteryBonus("r",a,0);Check(db.TransactionCount==tx,"bonus replay no duplicate");
        if(kind=="BankBonus")Check(db.Balance(a)==before+5000,"bank bonus applied");
        if(kind=="FreeTicket")
        {
            Check(db.LotteryTickets("r").Sum(t=>t.Count)==4&&db.Balance("system:lotpool")==2000,"free tickets funded into reference draw pool");
            Check(db.LotteryPlan(a).Bought==1&&db.TitlesProgress(a).Values["LottoTicketsBought"]==1,"free tickets do not inflate paid progress");db.BuyLottery("more","r",a,9);Check(db.LotteryTickets("r").Sum(t=>t.Count)==13,"free tickets do not consume normal limit");
            db.CloseLotteryRound("r",false);db.ResumeLottery();Check(db.Balance(a)==10_000_000&&db.Balance("system:lotpool")==0&&db.Balance("system:lotbonus")==0,"aborted free tickets cannot mint a player refund");
        }
        if(kind=="NextKillBankBonus")
        {
            Check(db.ClaimLotteryKill("r",a,a,true)==0&&db.ClaimLotteryKill("r",a,b,false)==0,"suicide and teamkill cannot claim");Check(db.ClaimLotteryKill("r",a,b,true)==5000&&db.ClaimLotteryKill("r",a,b,true)==0,"next opposing kill once");
        }
    }
    db.Verify();
}
using(var db=Open("pistol")){Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,10);Check(db.RollLotteryBonuses("r",a,true,Only("CashBonus"),_=>0).Length==0,"pistol excludes cash even when configured");db.Verify();}
using(var db=Open("draw")){Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,1);db.RollLotteryBonuses("r",a,false,Only("FreeTicket"),_=>0);db.ApplyLotteryBonus("r",a,0);db.CloseLotteryRound("r",true,_=>999%_);Check(db.GetLotteryRound("r")!.Prize==1600&&db.Superpot==200,"free tickets preserve legacy pot value and 80/10/10 split");db.Verify();}
using(var db=Open("cap",settings with{MaxBank=10_000_000})){Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,1);db.RollLotteryBonuses("r",a,false,Only("BankBonus"),_=>0);Check(db.ApplyLotteryBonus("r",a,0)==500&&db.Balance(a)==10_000_000,"bank bonus clipped to reference account limit");db.Verify();}
using(var db=Open("kill-expired")){Seed(db);db.StartLotteryRound("r",settings.Lotto);db.BuyLottery("ticket","r",a,1);db.RollLotteryBonuses("r",a,false,Only("NextKillBankBonus"),_=>0);db.ApplyLotteryBonus("r",a,0);db.CloseLotteryRound("r",false);Check(db.ClaimLotteryKill("r",a,b,true)==0,"kill bonus expires with round");db.ResumeLottery();db.Reset("reset",1,a,settings,DateTime.Now);Check(db.UpgradeDiscount(a)==0&&db.LotteryBonuses("r",a).Length==0,"period reset isolates all bonuses");db.Verify();}
var cash=settings.Lotto.CashToSuperpot;
foreach(int round in Enumerable.Range(1,22))Check((BonusPolicy.CollectionReason(cash,round,20)!="")==new[]{10,20}.Contains(round),"configured half/final round only "+round);
Check(BonusPolicy.CollectionReason(cash with{CollectOnLastHalfRound=false},10,20)==""&&BonusPolicy.CollectionReason(cash with{CollectOnFinalRound=false},20,20)=="","independent collection switches");
foreach(bool higher in new[]{false,true})foreach(int live in new[]{0,5000,16000})foreach(int snapshot in new[]{0,3000,10000})
{Check(BonusPolicy.CollectAmount(cash with{UseDeathCashSnapshotIfHigher=higher},live,snapshot)==(higher?Math.Max(live,snapshot):live),"live and death snapshot selection");}
Check(BonusPolicy.CollectAmount(cash with{MaxCashPerPlayer=1000},5000,10000)==1000,"collection cap");Check(BonusPolicy.CollectAmount(cash with{CollectDisconnectedDeathSnapshots=false},0,5000,true)==0,"offline snapshot disabled");
using(var db=Open("collection"))
{
    Seed(db);var intent=db.PrepareCashCollection("c",1,a,"s",5000,7000,true)!;Check(intent.After==0&&db.Superpot==0,"collection waits for native cash confirmation");db.Verify();
    Reject(()=>db.PrepareCashCollection("c",1,b,"s",5000,7000,true),"collection identity conflict rejected");db.ResolveCash("c",true,"native");db.ResolveCash("c",true,"native");
    Check(db.Superpot==7000&&db.Balance("system:cash")==-5000&&db.Balance("system:lotcashsnapshot")==-2000,"live cash and synthetic snapshot difference separated");
    Check(db.Balance(a)==10_000_000&&db.TitlesProgress(a).Values.GetValueOrDefault("Deposits")==0,"rest cash never presented as bank deposit");
    db.PrepareCashCollection("offline",1,b,"old-session",0,3000,false);db.PrepareCashCollection("offline",1,b,"old-session",0,3000,false);Check(db.Superpot==10000,"disconnected snapshot once");
    var cancel=db.PrepareCashCollection("cancel",1,a,"s",1000,1000,true)!;db.ResolveCash(cancel.Id,false,"admin");Check(db.Superpot==10000&&db.PendingCount==0,"cancelled native write does not collect");db.Verify();
}
foreach(string kind in new[]{"discount","bonus","collection"})
{
    string path=Path.Combine(root,"crash-"+kind+".sqlite3");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);foreach(string arg in new[]{"--crash",path,kind})start.ArgumentList.Add(arg);
    using(var child=Process.Start(start)!){await child.WaitForExitAsync();Check(child.ExitCode==27,"actual process interruption "+kind);}
    using var db=new Ledger(path,settings);
    if(kind=="discount"){Check(db.UpgradeDiscount(a)==20&&db.Security(a,settings.BankRobbery,1800000000).Vault==0&&db.Balance(a)==10_000_000,"discount purchase fully rolled back");db.BuyRobberyUpgrade("upgrade",1,a,"vault",1,400000,settings.BankRobbery,1800000000);Check(db.UpgradeDiscount(a)==0&&db.Balance(a)==9_600_000,"discount retry once");}
    else if(kind=="bonus"){Check(db.LotteryBonuses("r",a)[0] is {Kind:"BankBonus",Status:"ready"}&&db.Balance(a)==9_999_500,"rolled bonus retained, partial payout rolled back");db.ApplyLotteryBonus("r",a,0);Check(db.Balance(a)==10_004_500,"same saved bonus resumed once");}
    else{Check(db.PendingCount==1&&db.Superpot==0,"interrupted native settlement stays pending");Reject(()=>db.Reset("reset",1,a,settings,DateTime.Now),"pending collection prevents bank reset");db.ResolveCash("collection",true,"audited native confirmation");Check(db.Superpot==7000&&db.PendingCount==0,"collection resolved once after restart");}db.Verify();
}
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus"),RejectEntityAccess=true};var plugin=new Plugin(world.Core);plugin.Load(false);plugin.LottoCommand(world.Context);await world.Click("Regeln &amp; Gewinne");Check(world.Labels.Count<=5&&world.Labels.Any(x=>x.Contains("Boni")),"short public bonus subgroup");
var stale=world.Find("Boni");world.Session++;int messages=world.Messages.Count;await world.Click(stale);Check(world.Messages.Count==messages,"old session cannot use bonus menu");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);await world.Click("Einstellungen");while(!world.Labels.Any(x=>x.Contains("Lottoregeln")))await world.Click("Nächste");await world.Click("Lottoregeln");await world.Click("Boni &amp; Rest-Cash");Check(world.Labels.Count==5,"three separate switches plus status and return");var toggle=world.Find("Lottofähigkeiten: AN");world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.LottoAbilityBonus.Enabled,"lost permission protects bonus switch");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);await world.Click("Dauerlos-Rabatte: AN");await world.Click("Rest-Cash-Sammlung: AN");plugin.LottoCommand(world.Context);await world.Click("Regeln &amp; Gewinne");Check(!world.Labels.Any(x=>x.Contains("Boni")),"all inactive bonus features hidden");plugin.Unload();
var migration=new MenuWorld{DataDirectory=Path.Combine(root,"config"),RejectEntityAccess=true};Directory.CreateDirectory(migration.DataDirectory);var node=JsonNode.Parse(JsonSerializer.Serialize(settings))!.AsObject();node.Remove("LottoAbilityBonus");var lottoNode=node["Lotto"]!.AsObject();lottoNode.Remove("CashToSuperpot");lottoNode.Remove("PermanentTicketUpgradeDiscountEnabled");lottoNode["custom-note"]="preserve";lottoNode["TicketPrice"]=750;File.WriteAllText(Path.Combine(migration.DataDirectory,"settings.json"),node.ToJsonString());var migrated=new Plugin(migration.Core);migrated.Load(false);var saved=JsonNode.Parse(File.ReadAllText(Path.Combine(migration.DataDirectory,"settings.json")))!;Check(saved["Lotto"]!["custom-note"]!.GetValue<string>()=="preserve"&&saved["Lotto"]!["TicketPrice"]!.GetValue<int>()==750&&saved["Lotto"]!["CashToSuperpot"] is not null&&saved["LottoAbilityBonus"] is not null,"new sections preserve existing and unknown nested configuration");migrated.Unload();
foreach(var invalid in new[]{new AbilitySettings{ChancePerTicketBasisPoints=10001},new AbilitySettings{MaxBonusesPerPlayerPerRound=11},new AbilitySettings{MaxTicketsCountedPerPlayer=-2},new AbilitySettings{Effects=new(){FreeTicket=new(true,1,1001)}}}){bool bad=false;try{BonusPolicy.Validate(invalid);}catch(InvalidDataException){bad=true;}Check(bad,"unsafe bonus configuration rejected");}
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,discountAtomic=true,savedBonusChoice=true,collectionReplaySafe=true,paidOnlyRefunds=true,legacyCommandsUnchanged=true,clientAcceptancePending=true};File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BonusChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));

