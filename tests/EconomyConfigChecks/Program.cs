using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Economy;
using SwiftlyS2.Shared.Players;

int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
object? Call(object p,string n,params object?[] values){try{return p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,values);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
void Reject(Action action,string name){bool denied=false;try{action();}catch(Exception e)when(e is BankRejected or InvalidDataException or IOException){denied=true;}Check(denied,name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"EconomyConfigChecks","current");Directory.CreateDirectory(root);
// These exact locally generated fixture files are disposable; never inspect or mutate a server DB.
foreach(string name in new[]{"economy.sqlite3","economy.sqlite3-wal","economy.sqlite3-shm"}){string file=Path.Combine(root,name);if(File.Exists(file)){if(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)||File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))throw new Exception("Linked fixture");File.Delete(file);}}
var w=new MenuWorld{DataDirectory=root,RejectEntityAccess=true};w.Permissions.Add("betbank.admin");
var initial=new EconomySettings{Profile="config-checks",ResetMode="Never",MaxBank=1000,WebBank=new(){ExportEnabled=false}};string path=Path.Combine(root,"settings.json");var document=JsonSerializer.SerializeToNode(initial)!.AsObject();document["Future"]=42;document["Betting"]!["FutureBet"]="keep";string original=document.ToJsonString();File.WriteAllText(path,original);
var p=new Plugin(w.Core);p.Load(false);var db=Field<Ledger>(p,"bank");Check(File.ReadAllText(path)==original,"Startup does not rewrite existing config");
p.ConfigureSharedInterface(w.Interfaces);
Check(w.Interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.AdminSections.Economy.v1",out var sections),"Dedicated admin sections exported");
var openLotto=(Action<IPlayer>)sections!["lotto"];openLotto(w.Player);
Check(w.Labels.Any(x=>x.Contains("Lospreis:"))&&w.Labels.Any(x=>x.Contains("Jackpotchance:"))&&w.Labels.Count>10,"Lotto directly opens ticket, draw and bonus controls");
await w.Click(w.FindExact("Zurück"));Check(w.Commands.Last()=="sw_admin","Lotto returns using player authority");
Call(p,"OpenAdmin",w.Player);Check(w.Labels.Any(x=>x.Contains("Konten"))&&!w.Labels.Any(x=>x.Contains("Lotto")),"Bank root is separate");
Check(w.Labels.Any(x=>x.Contains("Bank: AN"))&&w.Labels.Any(x=>x.Contains("Bankraub: AN"))&&!w.Labels.Any(x=>x.Contains("Einstellungen")||x.Contains("Nächste Seite"))&&w.Current!.Configuration.MaxVisibleItems==5,"Bank and robbery directly reachable in one five-visible-row scrolling menu");
foreach(string detail in new[]{"Aufträge","Spion-Log:","Guthabenlimit","Wetten: AN","Titel: AN","Kopfgeld: AN"})
{
    Check(w.Labels.Any(x=>x.Contains(detail)),"Setting directly available without a category wrapper: "+detail);
}
await w.Click(w.FindExact("Guthabenlimit"));await w.Click(w.FindExact("Zurück"));Check(w.Labels.Any(x=>x.Contains("Bank: AN")),"Value selector returns directly to combined bank menu");
await w.Click(w.FindExact("Zurück"));Check(w.Commands.Last()=="sw_admin","Bank root retains return after recreation");
w.Permissions.Clear();var priorMenu=w.Current;openLotto(w.Player);Check(ReferenceEquals(w.Current,priorMenu),"Dedicated Lotto checks current permission");w.Permissions.Add("betbank.admin");
Check(File.ReadAllText(path)==original,"Category navigation preserves exact settings");
string implicitPath=Path.Combine(root,"implicit-defaults.json");var implicitNode=document.DeepClone().AsObject();implicitNode.Remove("Betting");implicitNode.Remove("BankEnabled");string implicitText=implicitNode.ToJsonString();File.WriteAllText(implicitPath,implicitText);var implicitFile=new EconomySettingsFile(implicitPath);Check(File.ReadAllText(implicitPath)==implicitText&&implicitFile.Current.BankEnabled&&implicitFile.Current.Betting.Enabled,"Historic implicit betting/bank defaults do not rewrite existing file");
string realPath=Path.Combine(root,"real-settings.json");var real=JsonNode.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"state/economy-config-before-hashes.json")))!["settings"]!["LPLK_Economy"]!.ToJsonString();File.WriteAllText(realPath,real);_=new EconomySettingsFile(realPath);Check(File.ReadAllText(realPath)==real,"Actual previous test-server configuration stays byte-identical");
EconomySettings Settings()=>Field<EconomySettings>(p,"settings");void Refresh()=>Call(p,"RefreshSettings");void Save(Func<EconomySettings,EconomySettings> edit)=>Call(p,"Save",w.Player,edit);void External(EconomySettings s){var j=JsonSerializer.SerializeToNode(s)!.AsObject();j["Future"]=42;j["Betting"]!["FutureBet"]="keep";File.WriteAllText(path,j.ToJsonString());}
DateTime stamp=File.GetLastWriteTimeUtc(path);External(Settings() with{BuyTimeSeconds=37});File.SetLastWriteTimeUtc(path,stamp);Refresh();Check(Settings().BuyTimeSeconds==37,"Same timestamp content change detected");
Call(p,"OpenAdmin",w.Player);var stale=w.FindExact("Bank: AN");External(Settings() with{BuyTimeSeconds=48,BankEnabled=false});await w.Click(stale);Check(Settings().BankEnabled&&Settings().BuyTimeSeconds==48,"Actual stale menu uses current toggle and preserves external field");
Check((int)JsonNode.Parse(File.ReadAllText(path))!["Future"]! ==42&&(string)JsonNode.Parse(File.ReadAllText(path))!["Betting"]!["FutureBet"]! =="keep","Unknown root/nested values survive save");
const string account="76561198000000000";db.Open(account,"Local config fixture");db.Adjust("seed",db.Period,account,account,700,"set");db.PrepareCash("pending",db.Period,account,"local",1000,900);
var baseline=(db.Period,db.AccountCount,db.TransactionCount,db.PendingCount,db.Balance(account));
string good=File.ReadAllText(path);using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>Save(s=>s with{MaxBank=2000}),"Locked file rejects edit");
Check(db.Limit==1000&&Settings().Limit==1000&&File.ReadAllText(path)==good,"Write failure keeps disk, active settings and ledger coherent despite pending cash");Check(Field<string?>(p,"failure") is null,"Configuration IO failure does not poison bank");
Save(s=>s with{MaxBank=2000});Check(db.Limit==2000&&Settings().Limit==2000,"Successful save activates new limit");good=File.ReadAllText(path);Reject(()=>Save(s=>s with{MaxBank=1500}),"Pending cash still prevents lowered limit");Check(db.Limit==2000&&File.ReadAllText(path)==good,"Rejected lower limit has no persistence");
External(Settings() with{MaxBank=1500});Refresh();Check(db.Limit==2000&&Settings().Limit==2000&&Field<string?>(p,"reloadError") is not null,"Rejected external ledger transition not accepted");File.WriteAllText(path,good);Refresh();Check(Field<string?>(p,"reloadError") is null,"Restoring accepted bytes clears error");
External(Settings() with{Profile="another-profile"});Refresh();Check(Settings().Profile=="config-checks"&&db.Profile=="config-checks","Foreign profile refused");File.WriteAllText(path,good);Refresh();
foreach(string bad in new[]{"null","[]","{","{\"Profile\":null}","{\"Lotto\":null}","{\"MaxBank\":-2}","{\"AdminAddButtons\":null}",new string(' ',262145)})
{var prior=Settings();File.WriteAllText(path,bad);Refresh();Check(ReferenceEquals(prior,Settings())&&Field<string?>(p,"reloadError") is not null,"Bad file retains accepted rules");Reject(()=>Save(s=>s with{BankEnabled=false}),"Menu cannot overwrite malformed file");Check(File.ReadAllText(path)==bad&&Field<string?>(p,"failure") is null,"Malformed file retained, bank not failed");}
File.WriteAllText(path,good);Refresh();File.Delete(path);Refresh();Reject(()=>Save(s=>s with{BankEnabled=false}),"Missing runtime file not silently recreated");Check(!File.Exists(path),"Missing stays missing");File.WriteAllText(path,good);Refresh();
var before=Settings();Reject(()=>Save(s=>{s.AdminAddButtons[0]=-1;return s;}),"Invalid mutable edit rejected");Check(Settings().AdminAddButtons[0]==before.AdminAddButtons[0]&&Settings().AdminAddButtons[0]>=0,"Edit uses detached arrays");
string external=JsonSerializer.Serialize(Settings() with{BuyTimeSeconds=51});Reject(()=>Save(s=>{File.WriteAllText(path,external);return s with{BuyTimeSeconds=52};}),"Mid-edit content race rejected");Check(File.ReadAllText(path)==external&&Settings().BuyTimeSeconds!=52,"Concurrent contents not overwritten");Refresh();Check(Settings().BuyTimeSeconds==51,"External write later accepted");
Call(p,"OpenAdmin",w.Player);stale=w.FindExact("Bank: AN");External(Settings() with{AdminPermission="changed.admin"});await w.Click(stale);Check(Settings().BankEnabled,"Old admin permission cannot act after current config reload");Reject(()=>Save(s=>s with{BankEnabled=false}),"Direct save rechecks current authority");w.Permissions.Add("changed.admin");Call(p,"OpenAdmin",w.Player);stale=w.FindExact("Bank: AN");w.Session++;await w.Click(stale);Check(Settings().BankEnabled,"Reconnected player cannot reuse old menu");
Call(p,"LimitMenu",w.Player);stale=w.Current!.Options[0];External(Settings() with{AllowChangeMaxBank=false});good=File.ReadAllText(path);await w.Click(stale);Check(File.ReadAllText(path)==good&&db.Limit==2000,"Revoked limit-edit permission honored by old menu");
Call(p,"ResetModeMenu",w.Player);stale=w.Current!.Options[0];External(Settings() with{AllowChangeResetMode=false});good=File.ReadAllText(path);await w.Click(stale);Check(File.ReadAllText(path)==good&&db.Period==baseline.Period,"Revoked reset-mode setting cannot reset period");
// Existing rounds retain their stored rules when settings change or are reformatted.
db.StartBetRound("preserve-bet",Settings().Betting);db.StartLotteryRound("preserve-lotto",Settings().Lotto);string bet=JsonSerializer.Serialize(db.BettingRound("preserve-bet")),lotto=JsonSerializer.Serialize(db.GetLotteryRound("preserve-lotto"));
Save(s=>s with{Betting=s.Betting with{BetOnlyFirstSeconds=90},Lotto=s.Lotto with{Enabled=!s.Lotto.Enabled}});File.AppendAllText(path,"\n");Refresh();Check(JsonSerializer.Serialize(db.BettingRound("preserve-bet"))==bet&&JsonSerializer.Serialize(db.GetLotteryRound("preserve-lotto"))==lotto,"Saved in-flight round rules untouched");
Check((db.Period,db.AccountCount,db.TransactionCount,db.PendingCount,db.Balance(account))==baseline,"Configuration changes preserve accounts, balances, transactions, pending cash and period");
// Status persistence is observational and cannot fail a valid config change.
string status=Path.Combine(root,"status.json");File.Delete(status);Directory.CreateDirectory(status);Save(s=>s with{BuyTimeSeconds=55});Check(Settings().BuyTimeSeconds==55&&Field<string?>(p,"failure") is null&&Field<bool>(p,"statusWriteFailed"),"Unwritable status does not fail banking");Directory.Delete(status);Call(p,"WriteStatus");Check(File.Exists(status)&&!Field<bool>(p,"statusWriteFailed"),"Status persistence recovers");
foreach(var menu in w.AllMenus)Check(menu.Configuration.MaxVisibleItems==5,"Compact menu row limit");
p.Unload();Reject(()=>Save(s=>s with{BankEnabled=false}),"Unloaded handler cannot save");Check(w.EntityAccesses==0,"No native entities accessed");Console.WriteLine($"EconomyConfigChecks: {checks} passed; actual menus/config files and real local SQLite with pending cash, no server mutations.");
