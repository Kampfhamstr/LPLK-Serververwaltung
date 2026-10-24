using System.Text.Json;
using LPLK_Economy;
using FleetBankMigration;

if(args.Length is <1 or >2)throw new ArgumentException("workspace and optional verified old DLL required");
int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
void Reject(Action work,string name){try{work();}catch(Exception e)when(e is InvalidDataException or IOException){checks++;return;}throw new Exception("Expected refusal: "+name);}
string dir=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"BankRollbackChecks","fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"economy.sqlite3");
string sampleRoot=Path.Combine(args[0],"private","fleet-migration-review");var rules=new Dictionary<string,EconomySettings>();var expected=new Dictionary<string,long>();
string[] names=["bank.json","lotto.json","titles.json","bank_robbery.json","bounties.json","spy_reports.json"];
bool first=true;
foreach(string server in LegacyBankExport.Standard)
{
 var settings=JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(args[0],"state","fleet-prepared-settings",server+".settings.json")))!;rules[server]=settings;
 var files=names.ToDictionary(n=>n,n=>File.ReadAllText(Path.Combine(sampleRoot,server,n)));
 using var bank=JsonDocument.Parse(files["bank.json"]);foreach(var p in bank.RootElement.GetProperty("Balances").EnumerateObject())expected[p.Name]=Math.Max(expected.GetValueOrDefault(p.Name),p.Value.GetInt64());
 using var ledger=new Ledger(path,settings,server:server,provision:first);first=false;ledger.ImportLegacyBundle(files,Ledger.LegacyBundleHash(files),"maximum");ledger.CheckReset(settings,new DateTime(2026,10,24));
 Reject(()=>LegacyBankExport.Read(path),"running game scope cannot be exported");
}
var initial=LegacyBankExport.Read(path);Check(initial.Count==4,"all four exports");
long Total(Dictionary<string,Dictionary<string,string>> exports,string server){using var b=JsonDocument.Parse(exports[server]["bank.json"]);return b.RootElement.GetProperty("Balances").EnumerateObject().Sum(p=>p.Value.GetInt64());}
Check(Total(initial,"153ae3e6")==expected.Values.Sum(),"initial max balances on Atlas");Check(LegacyBankExport.Standard.Skip(1).All(s=>Total(initial,s)==0),"no other bank duplicates funds");
const string added="76561198000000099";string donor=expected.MaxBy(p=>p.Value).Key;const long credit=123456,transfer=10000;
using(var bank=new Ledger(path,rules["8528b30f"],server:"8528b30f"))
{
 bank.Open(added,"Rollback fixture");bank.Book(new("after-migration",bank.Period,"test","Real post-migration credit fixture",[new(added,credit),new("system:fixture",-credit)]));bank.Transfer("new-transfer",bank.Period,donor,added,transfer);bank.Verify();
}
var current=LegacyBankExport.Read(path);Check(Total(current,"153ae3e6")==expected.Values.Sum()+credit,"post-cutover credit preserved");Check(LegacyBankExport.Standard.Skip(1).All(s=>Total(current,s)==0),"post-cutover balance never multiplied");
string oldDll=args.Length==2?Path.GetFullPath(args[1]):Path.GetFullPath(Path.Combine(args[0],"..","Surfserver Swiftly2","BetBank","BetBank_v1_4_44_MenuNoInput_ResetPeriodUpgrades_Source","BetBank_v1_4_44_MenuNoInput_ResetPeriodUpgrades","build","publish","BetBank","BetBank.dll"));
Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldDll)))=="10BD0288193039AA6961028FD215C4ABFE3DB0E4255BA2B45B23FB3752DBB9B8","actual deployed old BetBank DLL identity");
string localOldDll=Path.Combine(dir,"BetBank.dll");File.Copy(oldDll,localOldDll);var oldAssembly=System.Reflection.Assembly.LoadFrom(localOldDll);var oldTypes=new Dictionary<string,string>{{"bank.json","BankStore"},{"lotto.json","LottoStore"},{"titles.json","TitleStore"},{"bank_robbery.json","BankRobberyStore"},{"bounties.json","BountyStore"},{"spy_reports.json","SpyReportStore"}};
var oldOptions=new JsonSerializerOptions{Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
foreach(var bundle in current)foreach(var type in oldTypes)Check(JsonSerializer.Deserialize(bundle.Value[type.Key],oldAssembly.GetType("BetBank."+type.Value,true)!,oldOptions) is not null,"old deployed model accepts rollback "+bundle.Key+"/"+type.Key);
using(var b=JsonDocument.Parse(current["153ae3e6"]["bank.json"])){Check(b.RootElement.GetProperty("Balances").GetProperty(added).GetInt64()==credit+transfer,"new account and transfer exported");Check(b.RootElement.GetProperty("Balances").GetProperty(donor).GetInt64()==expected[donor]-transfer,"donor debit retained");Check(b.RootElement.GetProperty("LastResetMode").GetString()=="Monthly"&&b.RootElement.GetProperty("LastResetKey").GetString()=="2026-10","monthly period retained");}
foreach(string server in LegacyBankExport.Standard)
{
 var six=current[server].Where(p=>names.Contains(p.Key)).ToDictionary();using var bank=new Ledger(Path.Combine(dir,server+"-restored.sqlite3"),rules[server] with{Profile=server});
 bank.ImportLegacyBundle(six,Ledger.LegacyBundleHash(six),"sum");bank.Verify();Check(bank.Accounts(int.MaxValue).Sum(a=>a.Balance)==(server=="153ae3e6"?expected.Values.Sum()+credit:0),"legacy bundle reimport balanced: "+server);
 Check(current[server].ContainsKey("lplk-rollback-history.json"),"module history retained: "+server);
}
using(var bank=new Ledger(path,rules["3a587f9e"],server:"3a587f9e"))bank.PrepareCash("uncertain",bank.Period,added,"session",0,100);
Reject(()=>LegacyBankExport.Read(path),"uncertain native cash must not be lost");
using(var bank=new Ledger(path,rules["3a587f9e"],server:"3a587f9e")){bank.ResolveCash("uncertain",false,"fixture");bank.Book(new("pending-order",bank.Period,"LPLK_Economy.Robbery","Fixture",[],[new("order:fixture",0,JsonSerializer.Serialize(new RobberyOrder{Id="fixture",Period=bank.Period,Status="queued"}))]));}
Reject(()=>LegacyBankExport.Read(path),"queued paid robbery must settle before rollback");
using(var bank=new Ledger(path,rules["3a587f9e"],server:"3a587f9e"))bank.Book(new("done-order",bank.Period,"LPLK_Economy.Robbery","Fixture",[],[new("order:fixture",1,JsonSerializer.Serialize(new RobberyOrder{Id="fixture",Period=bank.Period,Status="done"}))]));
Check(Total(LegacyBankExport.Read(path),"153ae3e6")==expected.Values.Sum()+credit,"resolved export remains current");
using(var bank=new Ledger(path,rules["153ae3e6"],server:"153ae3e6"))bank.Reset("next-month",bank.Period,"fixture",rules["153ae3e6"],new DateTime(2026,11,1));
Check(LegacyBankExport.Standard.All(s=>Total(LegacyBankExport.Read(path),s)==0),"rollback after monthly reset does not resurrect old balances");
string captureDir=Path.Combine(dir,"capture");Directory.CreateDirectory(captureDir);var frozen=new Dictionary<string,FrozenServer>();
foreach(var server in LegacyBankImport.Servers)
{
 string target=Path.Combine(captureDir,server.Key);Directory.CreateDirectory(target);var hashes=new Dictionary<string,string>();
 foreach(string name in LegacyBankImport.Files)
 {
  string source=name=="settings.json"?Path.Combine(args[0],"state","fleet-prepared-settings",server.Key+".settings.json"):Path.Combine(sampleRoot,server.Key,name);
  File.Copy(source,Path.Combine(target,name));hashes[name]=LegacyBankImport.Hash(Path.Combine(target,name));
 }
 frozen[server.Key]=new(server.Value,"exited",DateTimeOffset.UtcNow.AddMinutes(-1),hashes);
}
var capture=new FrozenCapture(1,"all-standard-writers-stopped",DateTimeOffset.UtcNow,"Europe/Berlin",frozen);
string captureFile=Path.Combine(captureDir,"capture.json");void SaveCapture(FrozenCapture value)=>File.WriteAllText(captureFile,JsonSerializer.Serialize(value));SaveCapture(capture);
string imported=Path.Combine(dir,"imported");LegacyBankImport.Import(captureDir,imported,DateTimeOffset.UtcNow);
Check(File.Exists(Path.Combine(imported,"import.json")),"publish import receipt only after verification");
var exported=LegacyBankExport.Read(Path.Combine(imported,"economy.sqlite3"));Check(Total(exported,"153ae3e6")==expected.Values.Sum(),"frozen import independent maximum reconciliation");
Check(LegacyBankExport.Standard.Skip(1).All(s=>Total(exported,s)==0),"frozen import rollback no duplicate balances");
Reject(()=>LegacyBankImport.Import(captureDir,imported,DateTimeOffset.UtcNow),"existing destination never overwritten");
SaveCapture(capture with{Mode="live-sample"});Reject(()=>LegacyBankImport.Import(captureDir,Path.Combine(dir,"reject-live"),DateTimeOffset.UtcNow),"live capture rejected");
SaveCapture(capture with{CapturedAt=DateTimeOffset.UtcNow.AddHours(-3)});Reject(()=>LegacyBankImport.Import(captureDir,Path.Combine(dir,"reject-stale"),DateTimeOffset.UtcNow),"stale capture rejected");
SaveCapture(capture with{TimeZone="UTC"});Reject(()=>LegacyBankImport.Import(captureDir,Path.Combine(dir,"reject-zone"),DateTimeOffset.UtcNow),"reset time zone mismatch rejected");
SaveCapture(capture);string settingsFile=Path.Combine(captureDir,"153ae3e6","settings.json");File.AppendAllText(settingsFile," ");
Reject(()=>LegacyBankImport.Import(captureDir,Path.Combine(dir,"reject-changed"),DateTimeOffset.UtcNow),"changed source or settings rejected");
foreach(var isolated in new[]{(Server:"f222196f",Profile:"surf"),(Server:"ceda276a",Profile:"zombie")})
{
 var settings=JsonSerializer.Deserialize<EconomySettings>(File.ReadAllText(Path.Combine(args[0],"state","fleet-prepared-settings",isolated.Server+".settings.json")))!;
 var files=names.ToDictionary(n=>n,n=>File.ReadAllText(Path.Combine(sampleRoot,isolated.Server,n)));
 using var oldBank=JsonDocument.Parse(files["bank.json"]);long total=oldBank.RootElement.GetProperty("Balances").EnumerateObject().Sum(p=>p.Value.GetInt64());
 string localPath=Path.Combine(dir,isolated.Profile+".sqlite3");
 using(var bank=new Ledger(localPath,settings))
 {
  bank.ImportLegacyBundle(files,Ledger.LegacyBundleHash(files),"maximum");bank.CheckReset(settings,new DateTime(2026,10,24));bank.Verify();
  Check(!bank.Networked&&bank.Accounts(int.MaxValue).Sum(a=>a.Balance)==total,"isolated initial import preserves "+isolated.Profile);
  bank.Open(added,"isolated fixture");bank.Book(new("after-import",bank.Period,"test","post-import funds",[new(added,credit),new("system:fixture",-credit)]));
 }
 var export=LegacyBankExport.Read(localPath,isolated.Profile);
 Check(export.Count==1&&Total(export,isolated.Profile)==total+credit,"isolated rollback preserves current funds only on same server");
 foreach(var type in oldTypes)Check(JsonSerializer.Deserialize(export[isolated.Profile][type.Key],oldAssembly.GetType("BetBank."+type.Value,true)!,oldOptions) is not null,"old models accept isolated rollback "+type.Key);
 Reject(()=>LegacyBankExport.Read(localPath),"isolated bank cannot enter standard rollback");
 Reject(()=>LegacyBankExport.Read(localPath,isolated.Profile=="surf"?"zombie":"surf"),"wrong isolated profile refused");
 var six=export[isolated.Profile].Where(p=>names.Contains(p.Key)).ToDictionary();
 using(var restored=new Ledger(Path.Combine(dir,isolated.Profile+"-restored.sqlite3"),settings)){restored.ImportLegacyBundle(six,Ledger.LegacyBundleHash(six),"maximum");restored.Verify();Check(restored.Accounts(int.MaxValue).Sum(a=>a.Balance)==total+credit,"isolated current rollback reimports balanced");}
 using(var bank=new Ledger(localPath,settings))bank.PrepareCash("pending",bank.Period,added,"local-session",0,100);
 Reject(()=>LegacyBankExport.Read(localPath,isolated.Profile),"unsettled isolated cash blocks rollback");
 string isolatedCapture=Path.Combine(dir,isolated.Profile+"-capture");Directory.CreateDirectory(isolatedCapture);var isolatedHashes=new Dictionary<string,string>();
 foreach(string file in LegacyBankImport.Files){string source=file=="settings.json"?Path.Combine(args[0],"state","fleet-prepared-settings",isolated.Server+".settings.json"):Path.Combine(sampleRoot,isolated.Server,file);string dest=Path.Combine(isolatedCapture,file);File.Copy(source,dest);isolatedHashes[file]=LegacyBankImport.Hash(dest);}
 var frozenIsolated=new FrozenIsolatedCapture(1,"isolated-writer-stopped",DateTimeOffset.UtcNow,"Europe/Berlin",isolated.Server,new(IsolatedBankImport.Servers[isolated.Server].Uuid,"exited",DateTimeOffset.UtcNow.AddSeconds(-10),isolatedHashes));
 string isolatedManifest=Path.Combine(isolatedCapture,"capture.json");void SaveIsolated(FrozenIsolatedCapture c)=>File.WriteAllText(isolatedManifest,JsonSerializer.Serialize(c));SaveIsolated(frozenIsolated);
 string localImport=Path.Combine(dir,isolated.Profile+"-imported");IsolatedBankImport.Import(isolatedCapture,localImport,DateTimeOffset.UtcNow);
 Check(Total(LegacyBankExport.Read(Path.Combine(localImport,"economy.sqlite3"),isolated.Profile),isolated.Profile)==total&&!File.Exists(Path.Combine(localImport,"network.json")),"frozen isolated import stays separate and reconciles");
 Reject(()=>IsolatedBankImport.Import(isolatedCapture,localImport,DateTimeOffset.UtcNow),"existing isolated destination retained");
 SaveIsolated(frozenIsolated with{Source=frozenIsolated.Source with{Status="running"}});Reject(()=>IsolatedBankImport.Import(isolatedCapture,localImport+"-live",DateTimeOffset.UtcNow),"live isolated writer rejected");
 SaveIsolated(frozenIsolated with{CapturedAt=DateTimeOffset.UtcNow.AddHours(-3)});Reject(()=>IsolatedBankImport.Import(isolatedCapture,localImport+"-stale",DateTimeOffset.UtcNow),"stale isolated capture rejected");
 SaveIsolated(frozenIsolated with{Server="153ae3e6"});Reject(()=>IsolatedBankImport.Import(isolatedCapture,localImport+"-standard",DateTimeOffset.UtcNow),"standard server cannot import as isolated");
 SaveIsolated(frozenIsolated);File.AppendAllText(Path.Combine(isolatedCapture,"settings.json")," ");Reject(()=>IsolatedBankImport.Import(isolatedCapture,localImport+"-changed",DateTimeOffset.UtcNow),"modified isolated rules rejected");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,result="passed",fixtureDirectory=dir,productionChanged=false}));
