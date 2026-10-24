using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

const string actor="76561198000000000",target="76561198000000001",other="76561198000000002";
var settings=new EconomySettings{ResetMode="Never",MaxBank=1_000_000,MaxManualBankSet=50_000};
if(args is ["--crash",var crashFile])
{
    bool armed=false;using var db=new Ledger(crashFile,settings,phase=>{if(armed&&phase=="after-posting")Environment.Exit(29);});
    db.Open(target,"Target");db.Adjust("seed",1,actor,target,1000,"set");armed=true;db.AdjustLegacy("crash",1,actor,target,long.MaxValue,"add",50000);return;
}
int checks=0;void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(BankRejected){rejected=true;}Check(rejected,name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"LegacyEconomyChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
foreach(var sample in new[]{("1k","1000"),("1.5K","1500"),("2m","2000000"),("3b","3000000000"),(" 1 k ","1000"),("ALL","all"),("alles","all"),("MAX","all"),("0","0")})Check(Plugin.LegacyAmount(sample.Item1)==sample.Item2,"legacy amount "+sample.Item1);
foreach(string value in new[]{"-1","NaN","1e5","1kk","9223372036854775808","999999999999999999999999999999999m"})Reject(()=>Plugin.LegacyAmount(value),"invalid/overflow "+value);
foreach(string value in new[]{"all","alles","MAX"})Reject(()=>Plugin.LegacyAdminAmount(value),"admin all cannot clear account");
foreach(string side in new[]{"t","terror","terrorist","terrorists","tt","terroristen"})Check(BettingPolicy.Side(" "+side.ToUpperInvariant()+" ")==2,"T spelling "+side);
foreach(string side in new[]{"ct","counter","counterterrorist","counterterrorists"})Check(BettingPolicy.Side(" "+side.ToUpperInvariant()+" ")==3,"CT spelling "+side);
Reject(()=>BettingPolicy.Side("spectator"),"invalid betting side");
using(var db=new Ledger(Path.Combine(root,"limits.sqlite3"),settings))
{
    db.Open(target,"Target");db.AdjustLegacy("set",1,actor,target,100000,"set",50000);Check(db.Balance(target)==50000,"manual set capped");
    db.AdjustLegacy("add",1,actor,target,100000,"add",50000);Check(db.Balance(target)==150000,"add ignores manual set cap");
    db.AdjustLegacy("cap",1,actor,target,long.MaxValue,"add",50000);Check(db.Balance(target)==1000000,"huge credit clamps without overflow");
    db.AdjustLegacy("sub",1,actor,target,200000,"remove",50000);Check(db.Balance(target)==800000,"remove ignores manual set cap");
    db.AdjustLegacy("cap",1,actor,target,long.MaxValue,"add",50000);Check(db.Balance(target)==800000,"same original clipped request cannot book twice after balance changes");
    Reject(()=>db.AdjustLegacy("cap",1,other,target,long.MaxValue,"add",50000),"different actor cannot reuse transaction");
    Reject(()=>db.AdjustLegacy("cap",1,actor,target,1,"add",50000),"different amount cannot reuse transaction");
    db.AdjustLegacy("empty",1,actor,target,long.MaxValue,"remove",50000);Check(db.Balance(target)==0,"remove clamps at zero");
    db.AdjustLegacy("unlimited-manual",1,actor,target,long.MaxValue,"set",-1);Check(db.Balance(target)==1000000,"unlimited manual set still respects bank limit");
    db.Configure(settings with{MaxBank=100000});db.AdjustLegacy("over-limit-add",1,actor,target,1,"add",50000);Check(db.Balance(target)==1000000,"lowered limit does not confiscate existing balance on add");
    db.AdjustLegacy("over-limit-remove",1,actor,target,1,"remove",50000);Check(db.Balance(target)==999999,"debit still possible above lowered limit");
    db.Configure(settings);db.PrepareCash("pending",1,target,"s",500,1000);long before=db.Balance(target);
    Reject(()=>db.AdjustLegacy("blocked",1,actor,target,0,"set",50000),"pending cash cannot be overwritten");Check(db.Balance(target)==before,"blocked adjustment preserves balance");db.ResolveCash("pending",false,"test");
    Reject(()=>db.AdjustLegacy("stale",2,actor,target,0,"set",50000),"stale period rejected");
    Reject(()=>db.Adjust("strict",1,actor,target,long.MaxValue,"remove"),"strict ledger still rejects overdraft");db.Verify();
}
string crashPath=Path.Combine(root,"crash.sqlite3");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};foreach(string arg in new[]{Assembly.GetExecutingAssembly().Location,"--crash",crashPath})start.ArgumentList.Add(arg);
using(var child=Process.Start(start)!){if(!child.WaitForExit(30000)){child.Kill();throw new Exception("Crash timeout");}Check(child.ExitCode==29,"real process interrupted during admin posting");}
using(var db=new Ledger(crashPath,settings)){Check(db.Balance(target)==1000,"no half-written admin credit after process crash");db.AdjustLegacy("crash",1,actor,target,long.MaxValue,"add",50000);db.AdjustLegacy("crash",1,actor,target,long.MaxValue,"add",50000);Check(db.Balance(target)==1000000,"retry after crash books once");db.Verify();}

var world=new MenuWorld{DataDirectory=Path.Combine(root,"commands"),RejectEntityAccess=true,PermissionsByPlayer=[]};Directory.CreateDirectory(world.DataDirectory);
File.WriteAllText(Path.Combine(world.DataDirectory,"settings.json"),JsonSerializer.Serialize(settings));var plugin=new Plugin(world.Core);plugin.Load(false);
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var ledger=(Ledger)typeof(Plugin).GetField("bank",flags)!.GetValue(plugin)!;
void Rules(EconomySettings rules){File.WriteAllText(Path.Combine(world.DataDirectory,"settings.json"),JsonSerializer.Serialize(rules));typeof(Plugin).GetMethod("RefreshSettings",flags)!.Invoke(plugin,[]);}
void Rights(params string[] values)=>world.PermissionsByPlayer![ulong.Parse(actor)]=values.ToHashSet();
ledger.Open(actor,"Prüfspieler");ledger.Open(target,"Jane Doe");ledger.Open(other,"Jane Other");
ICommandContext Context(bool console,params string[] values)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch
{"get_Sender"=>console?null:world.Player,"get_IsSentByPlayer"=>!console,"get_Args"=>values,"Reply"=>Capture((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
object? Capture(string text){world.Messages.Add(text);return null;}
var methods=typeof(Plugin).GetMethods().Where(m=>m.GetCustomAttribute<Command>() is not null).ToArray();
MethodInfo Handler(string alias,bool raw)=>methods.Single(m=>raw?m.GetCustomAttributes<CommandAlias>().Any(a=>a.RegisterRaw&&a.Alias==alias):m.GetCustomAttribute<Command>()!.Name==alias||m.GetCustomAttributes<CommandAlias>().Any(a=>!a.RegisterRaw&&a.Alias==alias));
void Call(string alias,string[]? values=null,bool console=false,bool raw=false){world.Current=null;world.Messages.Clear();Handler(alias,raw).Invoke(plugin,[Context(console,values??[])]);Check(typeof(Plugin).GetField("failure",flags)!.GetValue(plugin) is null,"handler healthy "+alias+": "+world.Errors.LastOrDefault());}
bool Said(string part)=>world.Messages.Any(m=>m.Contains(part,StringComparison.OrdinalIgnoreCase));
using var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"state","legacy-economy-reference.json")));
int aliases=0,groups=0;
foreach(var group in reference.RootElement.GetProperty("settings").GetProperty("Commands").EnumerateObject())
{
    groups++;
    foreach(var value in group.Value.EnumerateArray())
    {
        string alias=value.GetString()!;aliases++;Check(Handler(alias,true)==Handler(alias,false),"same raw/prefixed handler "+alias);
        foreach(bool raw in new[]{false,true})
        {
            Rules(settings);Rights();Call(alias,raw:raw);Check(world.Current is not null||world.Messages.Count>0,"legacy entry responds "+alias);
            if(group.Name=="Admin")Check(world.Current?.Configuration.Title.Contains("Bank · ")==true,"ordinary player gets bank fallback "+alias);
            if(group.Name is "BankSet" or "BankAdd" or "BankRemove" or "BankResetPlayer")
            {
                long balance=ledger.Balance(target);Call(alias,group.Name=="BankResetPlayer"?[target]:[target,"1k"],raw:raw);Check(ledger.Balance(target)==balance&&Said("Rechte"),"no permission cannot edit "+alias);
            }
            long count=ledger.TransactionCount;Call(alias,console:true,raw:raw);Check(world.Messages.Count>0&&ledger.TransactionCount==count,"console responds without player changes "+alias);
            Rules(settings with{Enabled=false});Call(alias,raw:raw);Check(world.Current is null,"disabled public entry opens no menu "+alias);
        }
    }
}
Check(groups==18&&aliases==46,"actual reference groups and aliases");Check(reference.RootElement.GetProperty("settings").GetProperty("Admin").GetProperty("AdminSteamIdCount").GetInt32()==0,"no omitted explicit reference administrators");Rules(settings);
foreach(var entry in new (string[] Commands,EconomySettings Rules)[]{
    (["bank","konto","deposit","einzahlen","withdraw","abheben","transfer","ueberweisen","überweisen","topbank","banktop","rich","ranking","bounty","kopfgeld","titel","title","titles","spionlog","spylog","bankraublog"],settings with{BankEnabled=false}),
    (["bet","bt","wette","wetten","bets","pool","wettenstatus"],settings with{Betting=settings.Betting with{Enabled=false}}),
    (["lotto","lotterie","los"],settings with{Lotto=settings.Lotto with{Enabled=false}}),
    (["titel","title","titles"],settings with{TitleSystem=settings.TitleSystem with{Enabled=false}}),
    (["bounty","kopfgeld"],settings with{Bounty=settings.Bounty with{Enabled=false}}),
    (["spionlog","spylog","bankraublog"],settings with{BankRobbery=settings.BankRobbery with{Enabled=false}}),
    (["transfer","ueberweisen","überweisen"],settings with{TransfersEnabled=false})})
{
    Rules(entry.Rules);foreach(string alias in entry.Commands){Call(alias);Check(world.Current is null,"individual disabled feature hides command menu "+alias);}
}
Rules(settings);
foreach(string permission in new[]{"betbank.admin","lplk.economy.admin","lplk.admin"})
{
    Rights(permission);Call("bankset",["Jane","Doe","100k"]);Check(ledger.Balance(target)==50000&&Said("Bank geändert"),"set name with spaces and cap "+permission);
    Call("bankadd",[target,"100k"]);Check(ledger.Balance(target)==150000,"add legacy permission "+permission);
    Call("bankremove",[target,"100k"]);Check(ledger.Balance(target)==50000,"remove legacy permission "+permission);
    Call("bankdel",[target,"1b"]);Check(ledger.Balance(target)==0,"remove alias clamps at zero "+permission);
    Call("bankadd",[target,"1k"]);Call("bankset",[target,"all"]);Check(ledger.Balance(target)==1000&&Said("konkreten Betrag"),"all does not wipe balance "+permission);
    Call("bankset",["Jane","1k"]);Check(ledger.Balance(target)==1000&&Said("nicht eindeutig"),"ambiguous name cannot edit "+permission);
    Call("bankresetkonto",["Jane","Doe"]);Check(ledger.Balance(target)==0,"reset multiword name "+permission);
    Call("bbadmin");Check(world.Current?.Configuration.Title.Contains("Bank & Bankraub")==true,"authorized legacy admin menu "+permission);
    Rules(settings with{Enabled=false});Call("bankadmin");Check(world.Current is not null,"administrator can reach disabled addon "+permission);Rules(settings);
}
Rights("betbank.admin");world.PermissionsByPlayer![ulong.Parse(target)]=["betbank.admin"];Call("bankadd",[target,"1000"]);Check(ledger.Balance(target)==0&&Said("Kontoberechtigung"),"equal admin target protected");
world.PermissionsByPlayer[ulong.Parse(target)]=["lplk.economy.admin"];Call("bankadd",[target,"1000"]);Check(ledger.Balance(target)==0&&Said("Kontoberechtigung"),"new module administrator target also protected");
world.PermissionsByPlayer[ulong.Parse(target)]=["admin.root"];Call("bankadd",[target,"1000"]);Check(ledger.Balance(target)==0&&Said("Kontoberechtigung"),"higher admin target protected");world.PermissionsByPlayer.Remove(ulong.Parse(target));
Rules(settings with{AllowEditPlayerBank=false});Call("bankadd",[target,"1000"]);Check(ledger.Balance(target)==0,"editing switch applies to direct commands");Rules(settings);
ledger.Adjust("target-seed",ledger.Period,actor,target,1000,"set");ledger.PrepareCash("pending-command",ledger.Period,target,"s",1000,500);long pendingBalance=ledger.Balance(target);Call("bankresetplayer",[target]);Check(ledger.Balance(target)==pendingBalance&&Said("gesperrt"),"direct admin cannot override pending cash");ledger.ResolveCash("pending-command",false,"test");
typeof(Plugin).GetMethod("AdjustConfirm",flags)!.Invoke(plugin,[world.Player,target,100000L,"add"]);var confirmation=world.Find("Bestätigen");Rights();await world.Click(confirmation);Check(ledger.Balance(target)==1000,"revoked permission blocks saved admin confirmation");
Rights("betbank.admin");typeof(Plugin).GetMethod("AdjustConfirm",flags)!.Invoke(plugin,[world.Player,target,100000L,"add"]);confirmation=world.Find("Bestätigen");await world.Click(confirmation);Check(ledger.Balance(target)==101000,"menu add also ignores manual set cap");await world.Click(confirmation);Check(ledger.Balance(target)==101000,"repeated confirmation cannot book twice");
typeof(Plugin).GetMethod("AdjustConfirm",flags)!.Invoke(plugin,[world.Player,target,100000L,"add"]);confirmation=world.Find("Bestätigen");world.Session++;await world.Click(confirmation);Check(ledger.Balance(target)==101000,"replacement session cannot reuse confirmation");
Rights();foreach(string alias in new[]{"deposit","einzahlen","withdraw","abheben"})
{
    Call(alias,["garbage","ignored"]);Check(world.Current is null&&Said("Ungültiger Betrag"),"cash first argument validated despite extras "+alias);
    Call(alias,["1k","ignored"]);Check(world.Current is null&&world.Messages.Count>0,"cash extra arguments keep direct action "+alias);
}
foreach(string alias in new[]{"bet","bt","wette","wetten"})
{
    Call(alias,["terrorists","1k","ignored"]);Check(world.Current is null&&!Said("Nutzung")&&world.Messages.Count>0,"bet extra args reach wager rules "+alias);
    Call(alias,["counterterrorists"]);Check(Said("Nutzung"),"missing bet amount rejected "+alias);
}
foreach(string alias in new[]{"lotto","lotterie","los"})
{
    Call(alias,["unknown"]);Check(world.Current is not null&&Said("Nutzung"),"unknown lotto action shows help and menu "+alias);
    Call(alias,[" INFO "]);Check(Said("Dauerlose"),"lotto action trims whitespace "+alias);
    foreach(string action in new[]{"kaufen","buy","los","lose","ticket","tickets","dauerlos","auto","dauerlose","abo","permanent"}){Call(alias,[action,"1"]);Check(!Said("Nutzung")&&world.Current is null,"legacy lotto action recognized "+action);}
    foreach(string action in new[]{"kündigen","kuendigen","cancel","stop","aus"}){Call(alias,[action]);Check(ledger.LotteryPlan(actor).Count==0&&!Said("Nutzung"),"legacy cancellation "+action);}
}
ledger.Adjust("title-seed",ledger.Period,actor,actor,100000,"set");var owned=ledger.OwnedTitles(actor);Check(owned.Length>1,"title test has actual earned titles");
foreach(string alias in new[]{"titel","title","titles"})
{
    Call(alias,["2"]);Check(ledger.TitlesProgress(actor).Active==owned[1].Id,"legacy title index keeps catalog order "+alias);
    foreach(string clear in new[]{"aus","off","clear","none"}){Call(alias,["1"]);Call(alias,[" "+clear.ToUpperInvariant()+" "]);Check(ledger.TitlesProgress(actor).Active=="","title clear alias "+clear);}
    Call(alias,["99999"]);Check(Said("Titelnummer"),"invalid title index rejected");
}
Rules(settings with{TransfersEnabled=false});Call("transfer");Check(world.Current is null&&Said("ausgeschaltet"),"disabled transfer command hides targets");
Rules(settings with{BankEnabled=false,Betting=settings.Betting with{Enabled=false},Lotto=settings.Lotto with{Enabled=false}});Call("help");Check(world.Current is not null&&world.Labels.All(s=>!s.Contains("Bankbefehle")&&!s.Contains("Wetten")&&!s.Contains("Lotterie")&&!s.Contains("Kopfgeld")&&!s.Contains("Spionberichte")&&!s.Contains("Titel")),"help hides all unavailable areas including bank-dependent ones");
Rules(settings);Call("help");var betHelp=world.Find("Wetten");Rules(settings with{Betting=settings.Betting with{Enabled=false}});world.Messages.Clear();await world.Click(betHelp);Check(Said("ausgeschaltet")&&!Said("!bet"),"old help callback rechecks feature");
Rules(settings);ledger.PrepareCash("help-pending",ledger.Period,actor,"s",1000,500);Call("bbhelp");Check(world.Current is not null,"help stays available with pending cash");ledger.ResolveCash("help-pending",false,"test");
Check(world.EntityAccesses==0,"cold commands never access unready native entities");Check(world.AllMenus.All(m=>m.Configuration.MaxVisibleItems==5),"every tested menu has at most five visible rows");ledger.Verify();plugin.Unload();
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,legacyGroups=groups,legacyAliases=aliases,rawAndPrefixedHandlers=true,permissionsVerified=true,argumentCorrectionsVerified=true,processCrashVerified=true,clientAcceptancePending=true};File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"LegacyEconomyChecks","result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
