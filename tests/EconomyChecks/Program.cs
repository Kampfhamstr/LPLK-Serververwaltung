using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LPLK_Economy;
using Microsoft.Data.Sqlite;

const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
var settings=new EconomySettings{Profile="unit-test",ResetMode="Monthly"};
if(args.Length>0&&args[0]=="crash")
{
    using var db=new Ledger(args[1],settings,point=>{if(point=="after-posting")Environment.Exit(23);});
    db.Transfer("crash-transfer",db.Period,a,b,750);return;
}
int checks=0;
void Check(bool yes,string label){if(!yes)throw new Exception("FAIL: "+label);checks++;}
void Reject(Action action,string label){try{action();}catch(BankRejected){checks++;return;}catch(InvalidDataException){checks++;return;}throw new Exception("FAIL expected rejection: "+label);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"EconomyChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string PathFor(string name)=>Path.Combine(root,name+".sqlite3");
using(var db=new Ledger(PathFor("main"),settings))
{
    db.Open(a,"A");db.Open(b,"B");db.Open(c,"C");
    Check(db.Balance(a)==0&&db.AccountCount==3,"isolated new accounts");
    db.Adjust("seed",1,a,a,8_000_000_000,"set");
    var t=db.Transfer("first",1,a,b,3_000_000_000);
    Check(db.Balance(a)==5_000_000_000&&db.Balance(b)==3_000_000_000,"64-bit balances");
    Check(db.Transfer("first",1,a,b,3_000_000_000).Duplicate,"same request idempotent");
    Reject(()=>db.Transfer("first",1,a,b,4_000_000_000),"same id different amount");
    Reject(()=>db.Transfer("overdraft",1,b,a,4_000_000_000),"overdraft");
    Reject(()=>db.Transfer("negative",1,a,b,-1),"negative");
    Reject(()=>db.Transfer("self",1,a,a,1),"self transfer");
    Reject(()=>db.Transfer("stale",0,a,b,1),"stale period");
    Reject(()=>db.Book(new("unbalanced",1,"LPLK_Betting","bad",[new(a,100),new(b,-50)])),"unbalanced");
    Reject(()=>db.Book(new("doubleaccount",1,"LPLK_Betting","bad",[new(a,100),new(a,-100)])),"duplicate posting account");
    Check(db.Balance(a)==5_000_000_000&&db.Balance(b)==3_000_000_000,"rejections unchanged");
    db.Configure(settings with{MaxBank=5_000_000_000});
    Reject(()=>db.Transfer("cap",1,b,a,1),"recipient cap");
    db.Configure(settings with{MaxBank=10000});
    Check(db.Balance(a)==5_000_000_000,"lower limit preserves existing balance");
    db.Transfer("spend-after-lower",1,a,c,10000);Check(db.Balance(c)==10000,"debit over cap allowed");
    db.Configure(settings);
    Parallel.For(0,100,i=>db.Transfer("concurrent:"+i,1,a,b,100));
    Check(db.Balance(a)==4_999_980_000&&db.Balance(b)==3_000_010_000,"concurrent serialization");
    Parallel.For(0,30,_=>db.Transfer("same-concurrent",1,a,b,250));
    Check(db.Balance(b)==3_000_010_250,"concurrent repeats once");
    var purchase=new Booking("buy",1,"LPLK_Lottery","Los",[new(a,-500),new("pool:lottery",500)],[new("tickets:"+a,0,"{\"tickets\":1}")]);
    db.Book(purchase);Check(db.State("LPLK_Lottery","tickets:"+a).Revision==1&&db.Balance("pool:lottery")==500,"money and module state commit together");
    Reject(()=>db.Book(purchase with{Id="stale-purchase"}),"stale state revision");
    Check(db.Balance("pool:lottery")==500,"state conflict rolls money back");
    Check(db.Book(purchase).Duplicate,"purchase replay before state check");
    db.Verify();checks++;
    Check(!db.CheckReset(settings,new DateTime(2026,10,31)),"new reset schedule anchors");
    Check(db.CheckReset(settings,new DateTime(2026,11,1)),"monthly reset");
    Check(db.Period==2&&db.Balance(a)==0&&db.Balance(b)==0&&db.Balance("pool:lottery")==0,"bank and pot reset atomic");
    Check(db.State("LPLK_Lottery","tickets:"+a).Revision==0,"module progression period-bound");
    Check(!db.CheckReset(settings,new DateTime(2026,10,1))&&db.Period==2,"clock rollback no reset");
    Reject(()=>db.Book(purchase),"old module request cannot affect new period");
    db.Reset("manual",2,a,settings,new DateTime(2026,11,1));
    Reject(()=>db.Reset("manual",2,a,settings,new DateTime(2026,11,1)),"duplicate manual reset");
    Check(db.Period==3,"reset once");db.Verify();checks++;
}
using(var db=new Ledger(PathFor("main"),settings)){Check(db.Period==3,"period persisted");db.Verify();checks++;}
Reject(()=>{using var wrong=new Ledger(PathFor("main"),settings with{Profile="production"});},"profile isolation");
bool inject=false;
using(var db=new Ledger(PathFor("fault"),settings,p=>{if(inject&&p=="after-posting")throw new IOException("simulated write failure");}))
{
    db.Open(a,"A");db.Open(b,"B");db.Adjust("seed",1,a,a,5000,"set");inject=true;
    try{db.Transfer("failed",1,a,b,1000);throw new Exception("fault missing");}catch(IOException){checks++;}
    inject=false;Check(db.Balance(a)==5000&&db.Balance(b)==0,"mid-posting exception rolls back both sides");
    Check(!db.Transfer("failed",1,a,b,1000).Duplicate,"failed id reusable");db.Verify();checks++;
}
using(var db=new Ledger(PathFor("crash"),settings)){db.Open(a,"A");db.Open(b,"B");db.Adjust("seed",1,a,a,1000,"set");}
var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
foreach(var arg in new[]{"crash",PathFor("crash")})start.ArgumentList.Add(arg);
using(var child=Process.Start(start)!){Check(child.WaitForExit(15000)&&child.ExitCode==23,"real process abort during write");}
using(var db=new Ledger(PathFor("crash"),settings))
{
    Check(db.Balance(a)==1000&&db.Balance(b)==0,"crash recovery atomic");db.Transfer("crash-transfer",1,a,b,750);db.Verify();checks++;
    db.PrepareCash("withdraw",1,b,"s",1000,1500);
    Check(db.Balance(b)==250&&db.PendingCount==1,"withdraw reserves funds before native cash");
    Reject(()=>db.Transfer("locked",1,b,a,100),"unresolved cash blocks bank use");
    Reject(()=>db.Reset("locked-reset",1,a,settings,DateTime.Now),"unresolved cash blocks reset");
    Reject(()=>db.Configure(settings with{MaxBank=5000}),"unresolved deposit protects config limits");
}
using(var db=new Ledger(PathFor("crash"),settings))
{
    Check(db.PendingCount==1&&db.Intent("withdraw")?.Before==1000,"pending recovered, never reissued automatically");
    Check(db.PrepareCash("withdraw",1,b,"s",1000,1500).Status=="pending","pending duplicate same data");
    Reject(()=>db.PrepareCash("withdraw",1,b,"other-session",1000,1500),"session mismatch");
    db.ResolveCash("withdraw",true,"verified-native");Check(db.Balance(b)==250&&db.PendingCount==0,"withdraw finalized without second debit");
    Check(db.ResolveCash("withdraw",true,"admin").Duplicate,"cash finish repeat once");
    Reject(()=>db.ResolveCash("withdraw",false,"admin"),"contradictory finish");
    db.PrepareCash("deposit",1,b,"s",1000,500);Check(db.Balance(b)==250,"deposit not credited before native cash");
    db.ResolveCash("deposit",true,"verified-native");Check(db.Balance(b)==750,"deposit finalized");
    db.PrepareCash("cancel",1,b,"s",1000,1200);db.ResolveCash("cancel",false,"admin");Check(db.Balance(b)==750,"cancel withdrawal refunds reserve");
    db.PrepareCash("cancel-deposit",1,b,"s",1000,500);db.ResolveCash("cancel-deposit",false,"admin");Check(db.Balance(b)==750,"cancel deposit creates no money");db.Verify();checks++;
}
string legacy=JsonSerializer.Serialize(new{Balances=new Dictionary<string,long>{{a,6_000_000_000},{b,17}},Names=new Dictionary<string,string>{{a,"<b>Ä & A"}},BankMessagesDisabled=new Dictionary<string,bool>{{a,true},{b,false}},LastResetMode="Monthly",LastResetKey="2026-10"});
string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
using(var db=new Ledger(PathFor("import"),settings))
{
    Reject(()=>db.ImportLegacy(legacy,"wrong"),"migration hash");db.ImportLegacy(legacy,Hash(legacy));
    Check(db.Balance(a)==6_000_000_000&&db.Balance(b)==17,"migration exact integer values");db.ImportLegacy(legacy,Hash(legacy));Check(db.AccountCount==2,"migration repeat idempotent");
    Check(db.Accounts()[0].Name=="bÄ & A","migration name markup stripped");db.Verify();checks++;
    Check(db.Muted(a)&&!db.Muted(b),"migration preserves message preferences");
}
foreach(var invalid in new[]{"{\"Balances\":{\""+a+"\":1,\""+a+"\":2}}","{\"Balances\":{\""+a+"\":-1}}","{\"Balances\":{\""+a+"\":1.25}}","{\"Balances\":{\"0\":1}}"})
{
    using var db=new Ledger(PathFor(Guid.NewGuid().ToString("N")),settings);Reject(()=>db.ImportLegacy(invalid,Hash(invalid)),"invalid migration");Check(db.AccountCount==0,"invalid migration leaves zero accounts");
}
var corrupt=PathFor("corrupt");File.WriteAllText(corrupt,"damaged-user-data");string beforeHash=Hash(File.ReadAllText(corrupt));
try{using var db=new Ledger(corrupt,settings);throw new Exception("corrupt accepted");}catch(SqliteException){checks++;}
Check(Hash(File.ReadAllText(corrupt))==beforeHash,"corrupt data preserved");
using(var connection=new SqliteConnection("Data Source="+PathFor("import"))){connection.Open();using var cmd=connection.CreateCommand();cmd.CommandText="UPDATE accounts SET balance=0 WHERE id='"+a+"'";cmd.ExecuteNonQuery();}
Reject(()=>{using var db=new Ledger(PathFor("import"),settings);},"ledger tamper detected");

Check(BankPolicy.ResetKey("Weekly",new DateTime(2027,1,1))=="2026-W53","ISO week-year");
var context=new CashContext(true,false,true,false,1,0,true,false,false,false,"de_dust2");
foreach(int round in Enumerable.Range(1,40))foreach(bool freeze in new[]{false,true})foreach(double seconds in new[]{-1d,0,24.9,25,25.1,100})
{
    var x=context with{Round=round,Freeze=freeze,ActiveRound=!freeze,Seconds=seconds};string? error=BankPolicy.CashDenied(settings,x,false);
    bool pistol=round is 1 or 11;bool time=freeze||seconds>=0&&seconds<=25;
    Check((error is null)==(!pistol&&time),$"round/timing {round}/{freeze}/{seconds}");
}
foreach(var x in new[]{context with{Known=false},context with{Alive=false},context with{ControllingBot=true},context with{Died=true},context with{MapBlocked=true},context with{Map="ar_shoots"},context with{Warmup=true}})
    Check(BankPolicy.CashDenied(settings,x,true) is not null,"cash exploit barrier");
Check(BankPolicy.CashDenied(settings,context with{Round=2,Freeze=false,ActiveRound=false,Seconds=1},false) is not null,"round end closes withdrawal");
foreach(var input in new[]{"-100","1e3","1.2","9223372036854775808"," 100","100$","0"})Reject(()=>BankPolicy.Amount(input,1000,100,2000),"amount parsing");
Check(BankPolicy.Amount("all",9_000_000_000,100,9_000_000_000)==9_000_000_000,"all long amount");
var world=new MenuWorld{DataDirectory=Path.Combine(root,"menus"),RejectEntityAccess=true};
var plugin=new Plugin(world.Core);plugin.Load(false);Check(world.EntityAccesses==0,"cold load avoids entity system");
plugin.BankCommand(world.Context);Check(world.Labels.Count==5,"bank root four functions plus back");
await world.Click("Bargeld");await world.Click("Einzahlen");
for(int page=0;page<8;page++){Check(world.Current!.Options.Count<=5,"amount page bounded");if(!world.Labels.Any(x=>x.Contains("Nächste")))break;await world.Click("Nächste");}
plugin.Administration(world.Context);Check(!world.Labels.Any(x=>x.Contains("Konten")),"ordinary player denied admin");
world.Permissions.Add("lplk.economy.admin");plugin.Administration(world.Context);Check(world.Labels.Count==4,"admin root reserves parent back row");
await world.Click("Einstellungen");await world.Click("Bankregeln");var toggle=world.Find("Bank: AN");world.Permissions.Clear();await world.Click(toggle);
var saved=JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!;
Check(saved.BankEnabled,"revoked admin cannot toggle stale menu");world.Permissions.Add("lplk.economy.admin");await world.Click(toggle);
saved=JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!;Check(!saved.BankEnabled,"permitted toggle saved");
plugin.Administration(world.Context);Check(world.Labels.Count==4,"disabled module still administrable");await world.Click("Einstellungen");await world.Click("Bankregeln");await world.Click("Bank: AUS");
plugin.Administration(world.Context);await world.Click("Bankreset");await world.Click("Zurücksetzen");var confirmation=world.Find("Bestätigen");
world.Permissions.Clear();await world.Click(confirmation);world.Permissions.Add("lplk.economy.admin");
var ledger=(Ledger)typeof(Plugin).GetField("bank",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
Check(ledger.Period==1,"reset confirmation rechecks permissions");await world.Click(confirmation);Check(ledger.Period==2,"confirmed reset");await world.Click(confirmation);Check(ledger.Period==2,"stale reset callback cannot reset twice");
plugin.Administration(world.Context);await world.Click("Einstellungen");await world.Click("Bankregeln");var staleToggle=world.Find("Bank: AN");world.Session++;await world.Click(staleToggle);
Check(JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(world.DataDirectory,"settings.json")))!.BankEnabled,"changed session rejects callback");
Check(world.AllMenus.All(m=>m.Options.Count<=5),"all Economy menu options fit five rows");plugin.Unload();
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,processCrashVerified=true,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),dataDirectory=root};
File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"EconomyChecks","result.json"),JsonSerializer.Serialize(report));
Console.WriteLine(JsonSerializer.Serialize(report));
