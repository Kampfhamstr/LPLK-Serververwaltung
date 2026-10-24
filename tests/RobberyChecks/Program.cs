using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;

const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
const long now=1_800_000_000;
var settings=new EconomySettings();var rules=settings.BankRobbery;
void Seed(Ledger db,long attacker=5_000_000,long target=60_000_000){foreach(string id in new[]{a,b,c})db.Open(id,id);db.Adjust("seed-a",db.Period,a,a,attacker,"set");db.Adjust("seed-b",db.Period,a,b,target,"set");}
if(args is ["--crash",var path,var crashPhase])
{
    bool armed=false;using var db=new Ledger(path,settings,p=>{if(armed&&p=="after-posting")Environment.Exit(25);});Seed(db);
    if(crashPhase=="buy"){armed=true;db.OrderRobbery("one",1,a,b,rules,now,rules.RobberyCost);}
    else {db.OrderRobbery("one",1,a,b,rules,now,rules.RobberyCost);db.MarkRobberyRound(rules,[]);armed=true;db.ResumeRobbery(now,_=>0);}return;
}
int checks=0;void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
void Reject(Action action,string why){bool denied=false;try{action();}catch(BankRejected){denied=true;}Check(denied,why);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"RobberyChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
Ledger Open(string name,EconomySettings? s=null)=>new(Path.Combine(root,name+".sqlite3"),s??settings);
using(var db=Open("basic"))
{
    Seed(db);var quote=db.QuoteRobbery(a,b,rules,now);Check(quote.Cost==100000&&quote.Chance==50,"reference base cost and chance");
    var order=db.OrderRobbery("one",1,a,b,rules,now,quote.Cost);long count=db.TransactionCount;db.OrderRobbery("one",1,a,b,rules,now,quote.Cost);
    Check(count==db.TransactionCount&&db.Balance(a)==4_900_000,"duplicate order never charges twice");Reject(()=>db.OrderRobbery("one",1,a,c,rules,now,quote.Cost),"changed duplicate rejected");
    Reject(()=>db.OrderRobbery("two",1,a,b,rules,now,quote.Cost),"open sponsor and target limits");db.ResumeRobbery(now,_=>throw new Exception("queued must not draw"));Check(db.GetRobbery("one")!.Status=="queued","only valid round releases order");
    db.MarkRobberyRound(rules,[]);db.ResumeRobbery(now,_=>0);var done=db.GetRobbery("one")!;
    Check(done.Success&&done.Stolen==6_000_000&&db.Balance(a)==10_900_000&&db.Balance(b)==54_000_000,"10 percent loot and balanced payout");
    Check(db.Balance("system:robfees")==100000,"fee remains removed from player economy");
    count=db.TransactionCount;db.ResumeRobbery(now,_=>throw new Exception("must not reroll"));db.MarkRobberyRound(rules,[]);Check(count==db.TransactionCount,"duplicate rounds cannot settle twice");
    Check(db.Security(b,rules,now).ProtectedUntil==now+86400,"24 hour success protection");Reject(()=>db.QuoteRobbery(a,b,rules,now+900),"same sponsor cooldown still applies");
    db.Adjust("c-funds",1,a,c,5_000_000,"set");Reject(()=>db.QuoteRobbery(c,b,rules,now+899),"15 minute absolute protection");
    Check(db.QuoteRobbery(c,b,rules,now+900).BypassCost==1_000_000,"bypass opens exactly at 15 minutes");
    db.OrderRobbery("bypass",1,c,b,rules,now+900,1_100_000);Check(db.Superpot==100000&&db.Balance("system:robfees")==1_100_000,"10 percent bypass to superpot, rest fees");
    db.MarkRobberyRound(rules,[]);db.ResumeRobbery(now+901,_=>99);Check(!db.GetRobbery("bypass")!.Success&&db.Security(b,rules,now).ProtectedUntil==now+901+3600,"failure protection one hour");
    Check(db.Security(b,rules,now).Bypasses==1,"bypass count durable");db.Adjust("below",1,a,b,49_000_000,"set");db.Adjust("above",1,a,b,60_000_000,"set");Check(db.Security(b,rules,now).Bypasses==0,"falling below threshold permanently resets bypass counter");db.Verify();
}
foreach(int count in Enumerable.Range(0,25))Check(RobberyPolicy.BypassCost(rules,count)==Math.Min(50_000_000,1_000_000L*(1L<<Math.Min(count,20))),"bounded exponential bypass cost");
foreach(int vault in Enumerable.Range(0,4))foreach(int protections in Enumerable.Range(0,5))foreach(int skill in new[]{0,1,5})foreach(bool bypass in new[]{false,true})
{
    var sec=new RobberySecurity{Vault=vault,Protections=Enumerable.Range(0,protections).ToDictionary(i=>i.ToString(),_=>long.MaxValue)};
    Check(RobberyPolicy.Chance(rules,sec,skill,100_000,60_000_000,bypass)==Math.Clamp(50+skill*8+15-(bypass?0:protections*10),5,100),"skill, security and Robin Hood chance");
    Check(RobberyPolicy.MaxLoot(rules,sec,60_000_000,bypass)==new[]{80,60,40,25}[vault],"paid bypass retains vault by default");
}
Check(RobberyPolicy.Percent(9_000_000_000_000_000,10000,10000)==9_000_000_000_000_000,"large percentages cannot overflow");
using(var db=Open("upgrades"))
{
    Seed(db);db.BuyRobberyUpgrade("v1",1,a,"vault",1,500000,rules,now);Check(db.UpgradePrice(a,"vault",3,rules,now)==1_000_000,"vault upgrade charges difference");
    db.BuyRobberyUpgrade("v3",1,a,"vault",3,1_000_000,rules,now);long count=db.TransactionCount;db.BuyRobberyUpgrade("v3",1,a,"vault",3,1_000_000,rules,now);Check(count==db.TransactionCount,"duplicate upgrade idempotent");
    Reject(()=>db.BuyRobberyUpgrade("bad",1,a,"vault",2,500000,rules,now),"downgrade refused");
    foreach(var x in new[]{("alarm",200000L),("dogs",250000L),("tower",300000L),("guard",400000L)})db.BuyRobberyUpgrade(x.Item1,1,a,x.Item1,0,x.Item2,rules,now);
    Check(db.Security(a,rules,now+366L*86400).Protections.Count==4&&db.Security(a,rules,now+366L*86400).Vault==3,"upgrades persist beyond dates until bank reset");
    Reject(()=>db.BuyRobberyUpgrade("again",1,a,"alarm",0,200000,rules,now),"already active protection cannot be purchased again");
    db.Reset("reset",1,a,settings,DateTime.Now);Check(db.Security(a,rules,now).Vault==0&&db.Security(a,rules,now).Protections.Count==0,"bank reset clears period security");
    Reject(()=>db.BuyRobberyUpgrade("stale",1,a,"vault",1,500000,rules,now),"old period menu rejected");db.Verify();
}
using(var db=Open("time-upgrades"))
{
    Seed(db);var s=rules with{UpgradesValidUntilBankReset=false};db.BuyRobberyUpgrade("v",1,a,"vault",1,500000,s,now);db.BuyRobberyUpgrade("p",1,a,"alarm",0,200000,s,now);
    Check(db.Security(a,s,now+29*86400).Vault==1&&db.Security(a,s,now+30*86400).Vault==0&&db.Security(a,s,now+30*86400).Protections.Count==0,"legacy duration mode exact expiry");
}
using(var db=Open("cash-and-cap",settings with{MaxBank=60_000_000}))
{
    Seed(db);db.OrderRobbery("one",1,a,b,rules,now,100000);db.PrepareCash("pending",1,a,"s",1000,1100);db.MarkRobberyRound(rules,[]);
    db.ResumeRobbery(now,_=>throw new Exception("pending cash must postpone draw"));Check(db.GetRobbery("one")!.Status=="eligible","uncertain cash blocks draw");db.ResolveCash("pending",false,"review");db.Adjust("full",1,a,a,60_000_000,"set");
    db.ResumeRobbery(now,_=>0);Check(db.GetRobbery("one")!.Claim==6_000_000&&db.Balance("system:robclaims")==6_000_000&&db.Balance(b)==54_000_000,"full account retains all stolen money as claim");
    Reject(()=>db.Reset("reset",1,a,settings,DateTime.Now),"reset cannot destroy claims");db.Adjust("space",1,a,a,55_000_000,"set");db.ResumeRobbery(now);Check(db.GetRobbery("one")!.Claim==1_000_000&&db.Balance(a)==60_000_000,"claim partially pays available headroom");db.Adjust("space2",1,a,a,59_000_000,"set");db.ResumeRobbery(now);Check(db.RobberyClaims==0&&db.Balance("system:robclaims")==0,"claim paid exactly once");db.Verify();
}
using(var db=Open("spy"))
{
    Seed(db);db.Adjust("spyfund",1,a,c,2_000_000,"set");for(int level=1;level<=5;level++)db.BuyRobberyUpgrade("spy"+level,1,c,"spy",level,200000,rules,now);
    db.OrderRobbery("one",1,a,b,rules,now,100000);db.MarkRobberyRound(rules,[c]);db.ResumeRobbery(now,_=>0);
    var log=db.SpyLog(c,rules,now,8);Check(log.Length==1&&log[0].Contains("Zielbank")&&log[0].Contains(a),"level5 successful passive observation reveals complete event");
    var low=rules with{MaxSpySkillLevel=1};Check(!db.SpyLog(c,low,now,8)[0].Contains("Räuber")&&!db.SpyLog(c,low,now,8)[0].Contains("Beute"),"current lower spy level restricts details");Reject(()=>db.SpyLog(a,rules,now,8),"no spy level no log");
    db.BuyRobberyUpgrade("spy-a",1,a,"spy",1,200000,rules,now);Check(db.SpyLog(a,rules,now,8)[0].Contains("Unbekannt"),"later purchase cannot retrospectively reveal events");
    Check(db.SpyLog(a,rules with{SpyLogRecordUnknownEvents=false},now,8).Length==0,"unrevealed history hidden when configured");db.Verify();
}
foreach(bool known in new[]{false,true})foreach(bool warmup in new[]{false,true})foreach(int players in new[]{0,1,2})foreach(int winner in new[]{-1,0,2,3})
    Check(RobberyPolicy.ValidRound(rules,known,warmup,players,winner)==(known&&!warmup&&(winner is 2 or 3||winner==0&&players>=2)),"reference winner and winnerless-round policy");
foreach(string phase in new[]{"buy","settle"})
{
    string file=Path.Combine(root,"crash-"+phase+".sqlite3");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
    if(Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet")start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("--crash");start.ArgumentList.Add(file);start.ArgumentList.Add(phase);
    using(var child=Process.Start(start)!){await child.WaitForExitAsync();Check(child.ExitCode==25,"actual process interrupted during "+phase);}
    using var db=new Ledger(file,settings);
    if(phase=="buy")Check(db.GetRobbery("one") is null&&db.Balance(a)==5_000_000,"no half charged order after crash");
    else{Check(db.GetRobbery("one")!.Status=="rolled"&&db.Balance(a)==4_900_000&&db.Balance(b)==60_000_000,"outcome committed but partial transfer rolled back");db.ResumeRobbery(now,_=>throw new Exception("cannot reroll"));Check(db.Balance(a)==10_900_000&&db.Balance(b)==54_000_000,"saved result resumes exactly once");}db.Verify();
}
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menu"),RejectEntityAccess=true};var plugin=new Plugin(world.Core);plugin.Load(false);plugin.RobberyCommand(world.Context);
Check(world.Labels.Any(l=>l.Contains("Tresore"))&&world.Labels.Any(l=>l.Contains("Spionberichte")),"robbery actions directly reachable");var oldMenu=world.Find("Schutz");world.Session++;int menuCount=world.AllMenus.Count;await world.Click(oldMenu);Check(menuCount==world.AllMenus.Count,"old session cannot open upgrades");
plugin.RobberyCommand(world.Context);await world.Click("Schutzmaßnahmen");Check(world.Labels.Count==5,"four protections and return");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);var toggle=world.Find("Bankraub: AN");
world.Permissions.Clear();await world.Click(toggle);Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.BankRobbery.Enabled,"lost admin rights cannot toggle");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);plugin.RobberyCommand(world.Context);Check(world.Messages.Last().Contains("ausgeschaltet"),"disabled robbery direct command blocked");plugin.Unload();
foreach(var pair in new[]{(nameof(Plugin.RankingCommand),new[]{"banktop","rich","ranking"}),(nameof(Plugin.HelpCommand),new[]{"bbhelp","betbankhelp"}),(nameof(Plugin.SpyLogCommand),new[]{"spylog","bankraublog"})})Check(pair.Item2.All(alias=>typeof(Plugin).GetMethod(pair.Item1)!.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias==alias)),"legacy aliases retained: "+pair.Item1);
foreach(var pair in new[]{("1k","1000"),("2.5m","2500000"),("1b","1000000000"),("alles","all"),("max","all"),(" 500 ","500")})Check(Plugin.LegacyAmount(pair.Item1)==pair.Item2,"legacy compact amount "+pair.Item1);
Reject(()=>Plugin.LegacyAmount("99999999999999999999999999b"),"compact amount overflow rejected");
foreach(var method in typeof(Plugin).GetMethods().Where(m=>m.GetCustomAttribute<Command>() is not null))
{
    var command=method.GetCustomAttribute<Command>()!;var aliases=method.GetCustomAttributes<CommandAlias>().ToArray();
    foreach(string name in aliases.Where(x=>!x.RegisterRaw).Select(x=>x.Alias).Append(command.Name))Check(aliases.Any(x=>x.RegisterRaw&&x.Alias==name),"raw console bind preserved: "+name);
}
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,periodSecurityVerified=true,payoutClaimVerified=true,legacyAliasesVerified=true,clientAcceptancePending=true};
File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"RobberyChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
