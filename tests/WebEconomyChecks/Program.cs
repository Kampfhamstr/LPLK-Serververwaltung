using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LPLK_Economy;
using SwiftlyS2.Shared.Commands;
const string target="76561198000000001",actor="76561198000000002";
var rules=new EconomySettings{ResetMode="Never",MaxBank=100000};
var now=DateTimeOffset.UtcNow;
PanelBankRequest Request(Ledger db,string id="one")=>new("web:"+id,rules.Profile,db.Period,target,db.AccountRevision(target),7,actor,100,"add",500,"Geprüfte Korrektur",now.AddMinutes(5));
if(args is ["--crash",var path])
{
    bool armed=false;using var db=new Ledger(path,rules,phase=>{if(armed&&phase=="after-posting")Environment.Exit(29);});db.Open(target,"Testspieler");armed=true;db.PanelAdjust(Request(db),now,50000);return;
}
int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
void Reject(Action action,string why){try{action();}catch(BankRejected){Check(true,why);return;}throw new Exception(why);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"WebEconomyChecks","data");Directory.CreateDirectory(root);
string dbPath=Path.Combine(root,Guid.NewGuid().ToString("N")+".sqlite3");
using(var db=new Ledger(dbPath,rules))
{
    db.Open(target,"Testspieler");var r=Request(db);var receipt=db.PanelAdjust(r,now,50000);
    Check(receipt.Before==0&&receipt.After==500&&db.Balance(target)==500,"credit with durable receipt");
    Check(db.PanelAdjust(r,now.AddHours(1),50000)==receipt,"expired retry returns same receipt");
    foreach(var altered in new[]{r with{Amount=100},r with{UserId=8},r with{Reason="Andere Begründung"},r with{ActorRank=10},r with{Revision=r.Revision+1}})Reject(()=>db.PanelAdjust(altered,now,50000),"changed duplicate rejected");
    var stale=Request(db,"stale");db.Adjust("away",1,actor,target,100,"add");db.Adjust("back",1,actor,target,100,"remove");
    Reject(()=>db.PanelAdjust(stale,now,50000),"ABA rejects even unchanged balance");
    foreach(var invalid in new[]{Request(db,"negative") with{Amount=-1},Request(db,"zero-user") with{UserId=0},Request(db,"rank") with{ActorRank=101},Request(db,"bad-steam") with{Account="../other"},Request(db,"expired") with{ExpiresAt=now},Request(db,"future") with{ExpiresAt=now.AddHours(1)},Request(db,"reason") with{Reason="x"},Request(db,"control") with{Reason="bad\nreason"},Request(db,"overdraw") with{Operation="remove",Amount=501},Request(db,"cap") with{Amount=100000},Request(db,"manualcap") with{Operation="set",Amount=50001},Request(db,"period") with{Period=2},Request(db,"profile") with{Profile="production"}})Reject(()=>db.PanelAdjust(invalid,now,50000),"invalid request rejected");
    db.PrepareCash("cash",1,target,"session",1000,900);Reject(()=>db.PanelAdjust(Request(db,"blocked"),now,50000),"pending cash protects balance");db.ResolveCash("cash",false,"test");
    var set=Request(db,"set") with{Operation="set",Amount=0};db.PanelAdjust(set,now,50000);Check(db.Balance(target)==0,"explicit set zero permitted");
    Check(db.PanelReceipt(r.Id,1)==receipt,"original audit preserved");
    db.Reset("reset",1,actor,rules,DateTime.Now);Check(db.PanelAdjust(r,now,50000)==receipt&&db.Balance(target)==0,"retry after reset cannot credit new period");
    db.Verify();
    string fixtures=Path.Combine(Directory.GetCurrentDirectory(),"..","sERVERVERWALTUNG","work","webpanel","Serververwaltung_v1.15.2.4","tests","fixtures");Directory.CreateDirectory(fixtures);
    File.WriteAllText(Path.Combine(fixtures,"lplk-economy.json"),JsonSerializer.Serialize(db.WebPlayer(target,now)));
}
bool armedFault=false;string rollback=Path.Combine(root,Guid.NewGuid().ToString("N")+".sqlite3");
using(var db=new Ledger(rollback,rules,phase=>{if(armedFault&&phase=="before-commit")throw new IOException("test interruption");}))
{
    db.Open(target,"Testspieler");armedFault=true;try{db.PanelAdjust(Request(db),now,50000);}catch(IOException){}armedFault=false;
    Check(db.Balance(target)==0&&db.PanelReceipt("web:one",1)is null,"receipt and postings roll back together");db.Verify();
}
string crash=Path.Combine(root,Guid.NewGuid().ToString("N")+".sqlite3");var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};foreach(string a in new[]{Assembly.GetExecutingAssembly().Location,"--crash",crash})start.ArgumentList.Add(a);
using(var child=Process.Start(start)!){if(!child.WaitForExit(30000)){child.Kill();throw new Exception("crash timeout");}Check(child.ExitCode==29,"real process interrupted");}
using(var db=new Ledger(crash,rules)){Check(db.Balance(target)==0&&db.PanelReceipt("web:one",1)is null,"crash leaves no partial booking");var r=Request(db);db.PanelAdjust(r,now,50000);db.PanelAdjust(r,now,50000);Check(db.Balance(target)==500,"recovery books once");db.Verify();}
EconomySettings.Validate(rules with{MenuAutoCloseSeconds=0,AdminSetButtons=[0,500]});Check(true,"custom menus valid");
Check(BankPolicy.MenuAmounts([500,500,1000],100,750).SequenceEqual([500L]),"menu amounts deduplicate and respect ceiling");
Check(BankPolicy.MenuAmounts([500],100,300).SequenceEqual([100L]),"menu offers valid fallback");
Check(BankPolicy.MenuAmounts([500],100,50).Length==0,"no invalid choice when unaffordable");
var world=new MenuWorld{DataDirectory=Path.Combine(root,"plugin-"+Guid.NewGuid().ToString("N")),PermissionsByPlayer=[]};Directory.CreateDirectory(world.DataDirectory);
var webRules=rules with{WebBank=new(){AdminChangesEnabled=true}};File.WriteAllText(Path.Combine(world.DataDirectory,"settings.json"),JsonSerializer.Serialize(webRules));
var plugin=new Plugin(world.Core);plugin.Load(false);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var live=(Ledger)typeof(Plugin).GetField("bank",flags)!.GetValue(plugin)!;live.Open(target,"Testkonto");
string cmdId=Guid.NewGuid().ToString("N");var req=Request(live,cmdId);
Directory.CreateDirectory(Path.Combine(world.DataDirectory,"web-requests"));File.WriteAllText(Path.Combine(world.DataDirectory,"web-requests",cmdId+".json"),JsonSerializer.Serialize(req));
ICommandContext Context(bool player)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch{"get_Sender"=>player?world.Player:null,"get_Args"=>new[]{cmdId},"Reply"=>null,_=>Proxy.Empty(m.ReturnType)});
plugin.WebBankCommand(Context(true));Check(live.Balance(target)==0,"player cannot invoke panel command");
world.PermissionsByPlayer![ulong.Parse(target)]=["admin.root"];plugin.WebBankCommand(Context(false));Check(live.Balance(target)==0,"native founder immunity preserved");world.PermissionsByPlayer[ulong.Parse(target)]=[];
plugin.WebBankCommand(Context(false));plugin.WebBankCommand(Context(false));Check(live.Balance(target)==500,"console consumes exact request once");
using(var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(world.DataDirectory,"web-results",cmdId+".json"))))Check(result.RootElement.GetProperty("state").GetString()=="applied","only applied receipt reports success");
plugin.BankCommand(world.Context);Check(world.Current?.Configuration.AutoCloseAfter==25,"configured menu auto-close reaches SDK builder");
typeof(Plugin).GetMethod("ExportWeb",flags)!.Invoke(plugin,[]);Check(File.Exists(Path.Combine(world.DataDirectory,"web","ranking.json")),"actual plugin exports ranking");
plugin.Unload();
Console.WriteLine(JsonSerializer.Serialize(new{checks,passed=true,realCrash=true,productionTouched=false}));
