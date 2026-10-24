using LPLK_Economy;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

const string a="76561198000000001",b="76561198000000002";
var settings=new EconomySettings{Profile="standard-network-test",ResetMode="Never",StartBank=1000};
if(args.FirstOrDefault()=="rules")
{
    foreach(string server in new[]{"153ae3e6","8528b30f","3a587f9e","cc75df67","f222196f","ceda276a"}){
        bool standard=server is not ("f222196f" or "ceda276a");
        var migrated=LegacySettings.Convert(File.ReadAllText(Path.Combine(args[1],"fleet-rules-"+server+".json")),standard?"standard-network":server=="f222196f"?"surf":"zombie",standard);
        Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],server+".settings.json"),JsonSerializer.Serialize(migrated,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(new{server,validated=true,migrated.Profile,migrated.ResetMode,migrated.StartBank,migrated.Limit}));
    }
    return;
}
if(args.FirstOrDefault()=="samples")
{
    string sampleRoot=args[1];string output=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"NetworkBankChecks","migration",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
    var reports=new List<object>();
    foreach(string rule in new[]{"sum","maximum"})
    {
        string samplePath=Path.Combine(output,rule+".sqlite3");var expected=new Dictionary<string,long>();int imported=0;long final=0;
        foreach(string server in new[]{"153ae3e6","8528b30f","3a587f9e","cc75df67"})
        {
            var files=Directory.GetFiles(Path.Combine(sampleRoot,server),"*.json").ToDictionary(p=>Path.GetFileName(p),File.ReadAllText);
            using(var parsed=JsonDocument.Parse(files["bank.json"]))foreach(var p in parsed.RootElement.GetProperty("Balances").EnumerateObject())expected[p.Name]=rule=="sum"?expected.GetValueOrDefault(p.Name)+p.Value.GetInt64():Math.Max(expected.GetValueOrDefault(p.Name),p.Value.GetInt64());
            var sourceSettings=settings with{StartBank=0,ResetMode="Monthly"};
            using var ledger=new Ledger(samplePath,sourceSettings,server:server,provision:imported==0);
            var receipt=ledger.ImportLegacyBundle(files,Ledger.LegacyBundleHash(files),rule);long transactions=ledger.TransactionCount;
            if(ledger.ImportLegacyBundle(files,Ledger.LegacyBundleHash(files),rule)!=receipt||ledger.TransactionCount!=transactions)throw new Exception("Import replay changed data");
            if(ledger.CheckReset(sourceSettings,new DateTime(2026,10,24)))throw new Exception("Initial calendar binding must not wipe imported balances");
            ledger.Verify();imported++;final=ledger.Accounts(int.MaxValue).Sum(p=>p.Balance);
            if(final!=expected.Values.Sum()||expected.Any(p=>ledger.Balance(p.Key)!=p.Value))throw new Exception("Imported aggregate differs from source contributions");
            reports.Add(new{rule,receipt,replaySafe=true});
        }
        reports.Add(new{rule,sources=imported,players=expected.Count,total=final});
    }
    foreach(string server in new[]{"f222196f","ceda276a"})
    {
        var files=Directory.GetFiles(Path.Combine(sampleRoot,server),"*.json").ToDictionary(p=>Path.GetFileName(p),File.ReadAllText);
        using var ledger=new Ledger(Path.Combine(output,server+".sqlite3"),settings with{StartBank=0,Profile=server});
        var receipt=ledger.ImportLegacyBundle(files,Ledger.LegacyBundleHash(files),"sum");ledger.Verify();reports.Add(new{isolated=true,receipt});
    }
    Console.WriteLine(JsonSerializer.Serialize(new{dryRun=true,frozenSource=false,productionChanged=false,reports,directory=output}));return;
}
if(args.Length>0)
{
    using var worker=new Ledger(args[1],settings,args[0]=="crash"?p=>{if(p=="after-posting")Environment.Exit(23);}:null,args[2]);
    if(args[0]=="crash")worker.Transfer("crash",worker.Period,a,b,100);
    else for(int n=0;n<100;n++)worker.Transfer("parallel-"+n,worker.Period,args[2]=="aaaaaaaa"?a:b,args[2]=="aaaaaaaa"?b:a,1);
    return;
}
int checks=0;
void Check(bool yes,string label){if(!yes)throw new Exception("FAIL: "+label);checks++;}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is BankRejected or InvalidDataException or IOException){checks++;return;}throw new Exception("FAIL expected rejection: "+label);}
string dir=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"NetworkBankChecks","data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"economy.sqlite3");
Reject(()=>{using var x=new Ledger(path,settings,server:"aaaaaaaa");},"missing mount never creates a new bank");
using(var x=new Ledger(path,settings,server:"aaaaaaaa",provision:true))
using(var y=new Ledger(path,settings,server:"bbbbbbbb"))
{
    x.Open(a,"A");y.Open(a,"A");y.Open(b,"B");Check(x.Balance(a)==1000&&y.Balance(a)==1000,"opening credit exactly once across servers");
    Check(x.NetworkIdentity==y.NetworkIdentity&&x.NetworkIdentity?.Length==32,"stable shared database identity");
    Reject(()=>{using var duplicate=new Ledger(path,settings,server:"aaaaaaaa");},"only one live process per server scope");
    x.Transfer("same-id",1,a,b,100);y.Transfer("same-id",1,b,a,40);
    Check(x.Balance(a)==940&&y.Balance(b)==1060,"shared balances and namespaced request ids");
    Check(x.Transfer("same-id",1,a,b,100).Duplicate,"idempotent retry");
    x.PrepareCash("cash",1,a,"session-a",0,200);
    Check(y.Blocked(a)&&x.Balance(a)==740,"global reservation locks account");
    Reject(()=>y.PrepareCash("cash",1,a,"session-b",0,200),"no second withdrawal on another server");
    Reject(()=>y.Transfer("blocked",1,a,b,1),"other-server transfer blocked during native uncertainty");
    Check(y.Intent("cash") is null,"another server cannot resolve the local cash intent");
    x.ResolveCash("cash",false,"test");Check(!y.Blocked(a)&&y.Balance(a)==940,"cancel returns reserved money once");
    x.ResolveCash("cash",false,"test");Check(y.Balance(a)==940,"repeat cancel not a credit");
    x.StartBetRound("round",new());y.StartBetRound("round",new());
    x.RecoverBetRounds();Check(y.BettingRound("round")?.Status=="open","recovery does not close another server's round");
    y.PrepareBetStake("stake","round",a,"b",2,1000,100);y.ResolveCash("stake",true,"test");
    Reject(()=>x.Reset("reset",1,a,settings,DateTime.UtcNow),"global reset refuses another server's active bet");
    y.CloseBetRound("round",0);y.PayBetClaim("claim","round",a,"b",900,0,100);
    Check(x.Balance(a)==1040&&x.Superpot==0&&y.Superpot==0,"other-server bet bank payout");
    y.CheckReset(settings,DateTime.UtcNow);Check(!x.CheckReset(settings,DateTime.UtcNow),"single shared calendar");
    var protectedState=new RobberySecurity{Vault=3,VaultUntil=long.MaxValue,ProtectedAt=1,ProtectedUntil=long.MaxValue,Protections=new(){["alarm"]=long.MaxValue}};
    x.Book(new("protection",1,"LPLK_Economy.Robbery","Test protection",[],[new("security:"+a,0,JsonSerializer.Serialize(protectedState)),new("pair:"+b+":"+a,0,"9999999999")]));
    Check(y.Security(a,settings.BankRobbery,100).Vault==3&&y.Security(a,settings.BankRobbery,100).Protections.ContainsKey("alarm"),"bank protection survives changing server");
    Check(y.State("LPLK_Economy.Robbery","pair:"+b+":"+a).Json=="9999999999","same target cooldown shared");
    Reject(()=>y.Book(new("stale-protection",1,"LPLK_Economy.Robbery","Test",[],[new("security:"+a,0,"{}") ])),"concurrent security updates use shared revision");
    Reject(()=>y.Configure(settings with{StartBank=2000}),"different opening credits refused");
    Reject(()=>y.Configure(settings with{ResetMode="Monthly"}),"different reset rules refused");
    x.Reset("reset",1,a,settings,DateTime.UtcNow);Check(y.Period==2&&y.Balance(a)==1000&&x.Balance(b)==1000,"global reset changes all scopes once");
    Check(y.Security(a,settings.BankRobbery,100).Vault==0&&x.State("LPLK_Economy.Robbery","pair:"+b+":"+a).Json is null,"shared protection and cooldown reset with bank period");
    Reject(()=>y.Reset("reset",1,a,settings,DateTime.UtcNow),"stale reset refuses second reset");
    x.Verify();y.Verify();checks+=2;
}
Process Start(string mode,string server)
{
    var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    info.ArgumentList.Add(mode);info.ArgumentList.Add(path);info.ArgumentList.Add(server);return Process.Start(info)!;
}
using(var first=Start("parallel","aaaaaaaa"))using(var second=Start("parallel","bbbbbbbb"))
{
    first.WaitForExit();second.WaitForExit();Check(first.ExitCode==0,"first process: "+first.StandardError.ReadToEnd());Check(second.ExitCode==0,"second process: "+second.StandardError.ReadToEnd());
}
using(var crash=Start("crash","aaaaaaaa")){crash.WaitForExit();Check(crash.ExitCode==23,"actual process crash inside posting transaction");}
using(var x=new Ledger(path,settings,server:"aaaaaaaa"))using(var y=new Ledger(path,settings,server:"bbbbbbbb"))
{
    Check(x.Balance(a)==1000&&y.Balance(b)==1000,"200 parallel transactions and crash preserve all balances");x.Verify();y.Verify();checks+=2;
    Reject(()=>{using var local=new Ledger(path,settings);},"old local implementation rejects shared schema");
    Reject(()=>{using var wrong=new Ledger(path,settings with{Profile="surf"},server:"cccccccc");},"surf cannot open standard bank");
}
var monthly=settings with{ResetMode="Monthly"};
using(var x=new Ledger(Path.Combine(dir,"monthly.sqlite3"),monthly,server:"aaaaaaaa",provision:true))
using(var y=new Ledger(Path.Combine(dir,"monthly.sqlite3"),monthly,server:"bbbbbbbb"))
{
    x.Open(a,"A");x.Open(b,"B");x.CheckReset(monthly,new DateTime(2026,10,24));x.Transfer("oldmonth",1,a,b,123);
    Check(y.Balance(b)==1123&&!y.CheckReset(monthly,new DateTime(2026,10,31)),"same month preserves money on every standard server");
    Check(y.CheckReset(monthly,new DateTime(2026,11,1))&&x.Balance(b)==1000&&x.Period==2,"monthly reset applies once across all servers");
    Check(!x.CheckReset(monthly,new DateTime(2026,11,1))&&x.Period==2,"second server cannot repeat calendar reset");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,parallelTransactions=200,processCrashVerified=true,productionChanged=false,dataDirectory=dir}));
