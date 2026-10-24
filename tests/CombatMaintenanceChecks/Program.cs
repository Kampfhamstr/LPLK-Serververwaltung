using System.Reflection;
using System.Text.Json;
using System.Runtime.ExceptionServices;
using LPLK_CombatRules;
using SwiftlyS2.Shared;
int checks=0;void Check(bool v,string n){checks++;if(!v)throw new Exception(n);}
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,flags)!.GetValue(p)!;
object? Call(object p,string n,params object?[] a){try{return p.GetType().GetMethod(n,flags)!.Invoke(p,a);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
void Reject(Action a,string n){bool bad=false;try{a();}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or JsonException or UnauthorizedAccessException){bad=true;}Check(bad,n);}
var root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"CombatMaintenanceChecks","current");Directory.CreateDirectory(root);
var w=new MenuWorld{DataDirectory=root,RejectEntityAccess=true};w.Permissions.Add("lplk.combat.admin");
string path=Path.Combine(root,"settings.json");var initial=new CombatSettings{Rules=new(){["weapon_ak47"]=new(){Damage="*2"},["weapon_awp"]=new(){Damage="+10"}}};string text=JsonSerializer.Serialize(initial);File.WriteAllText(path,text);
object Hooks(Type type)=>Proxy.Make(type,(m,a)=>m.ReturnType.IsInterface?Hooks(m.ReturnType):Proxy.Empty(m.ReturnType));
var underlying=w.Core;w.Core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_GameHooks"?Hooks(m.ReturnType):m.Invoke(underlying,a));
var p=new Plugin(w.Core);p.Load(false);Check(File.ReadAllText(path)==text,"Load preserves settings bytes");
CombatSettings Settings()=>Field<CombatSettings>(p,"settings");void Refresh()=>Call(p,"RefreshSettings");void External(CombatSettings s)=>File.WriteAllText(path,JsonSerializer.Serialize(s));void Save(Func<CombatSettings,CombatSettings> edit)=>Call(p,"Save",edit);
typeof(Plugin).GetField("headshotsBlocked",flags)!.SetValue(p,17);Field<Dictionary<int,long>>(p,"alerts")[1]=999;
var stamp=File.GetLastWriteTimeUtc(path);External(Settings() with{MinimumLeadRounds=321});File.SetLastWriteTimeUtc(path,stamp);Refresh();Check(Settings().MinimumLeadRounds==321,"Equal timestamp content accepted");
Call(p,"HeadshotMenu",w.Player);var old=w.Find("Nur Kopftreffer");External(Settings() with{HeadshotOnly=true,MinimumLeadRounds=123});await w.Click(old);Check(!Settings().HeadshotOnly&&Settings().MinimumLeadRounds==123,"Stale toggle reads latest value and preserves external field");
Call(p,"RuleMenu",w.Player,"weapon_ak47");old=w.Find("Nur Kopftreffer");External(Settings() with{Rules=new(){["weapon_ak47"]=new(){Damage="*3"},["weapon_awp"]=new(){Damage="+42"}}});await w.Click(old);Check(Settings().Rules["weapon_ak47"]==new DamageRule{OnlyHeadshot=true,Damage="*3"}&&Settings().Rules["weapon_awp"].Damage=="+42","Rule edit preserves newest target and other rules");
Call(p,"ConfirmRemove",w.Player,"weapon_ak47");old=w.FindExact("Ja");External(Settings() with{Rules=new(){["weapon_ak47"]=new(){Damage="*4"},["weapon_awp"]=new(){Damage="+42"}}});await w.Click(old);Check(Settings().Rules.ContainsKey("weapon_ak47"),"Changed rule cannot be deleted by old confirmation");
Call(p,"ConfirmRemove",w.Player,"weapon_ak47");await w.Click("Nein");Check(Settings().Rules.ContainsKey("weapon_ak47"),"Cancellation preserves rule");Call(p,"ConfirmRemove",w.Player,"weapon_ak47");await w.Click("Ja");Check(!Settings().Rules.ContainsKey("weapon_ak47")&&Settings().Rules["weapon_awp"].Damage=="+42","Fresh confirmation removes only chosen rule");
string good=File.ReadAllText(path);
foreach(string bad in new[]{"null","[]","{","{\"Rules\":null}","{\"GoldenWeapons\":null}","{\"MinimumLeadRounds\":0}","{\"Unknown\":1}","{\"Rules\":{\"weapon_awp\":{\"Damage\":\"/0\"}}}",new string(' ',65537)})
{var previous=Settings();File.WriteAllText(path,bad);Refresh();Check(ReferenceEquals(previous,Settings())&&Field<string?>(p,"reloadError") is not null,"Invalid config retains rules");Reject(()=>Save(s=>s with{Enabled=false}),"Invalid config blocks save");Check(File.ReadAllText(path)==bad,"Invalid and unknown contents preserved");}
File.WriteAllText(path,good);Refresh();File.Delete(path);Refresh();Reject(()=>Save(s=>s with{Enabled=false}),"Missing file not replaced");Check(!File.Exists(path),"Missing file stays missing");File.WriteAllText(path,good);Refresh();
var before=Settings();Reject(()=>Save(s=>{s.GoldenWeapons[0]="invalid";return s;}),"Mutating invalid edit rejected");Check(Settings().GoldenWeapons[0]==before.GoldenWeapons[0],"Detached edit protects active array");
using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>Save(s=>s with{MinimumLeadRounds=999}),"Write failure rejected");Check(File.ReadAllText(path)==good&&Settings().MinimumLeadRounds==123,"Write failure changes no settings");
Call(p,"HeadshotMenu",w.Player);old=w.Find("Nur Kopftreffer");w.Permissions.Clear();await w.Click(old);Check(!Settings().HeadshotOnly,"Revoked admin cannot edit");w.Permissions.Add("lplk.combat.admin");Call(p,"HeadshotMenu",w.Player);old=w.Find("Nur Kopftreffer");w.Session++;await w.Click(old);Check(!Settings().HeadshotOnly,"Old connection callback blocked");
string status=Path.Combine(root,"status.json");File.Delete(status);Directory.CreateDirectory(status);Save(s=>s with{MinimumLeadRounds=456});Check(Settings().MinimumLeadRounds==456&&Field<bool>(p,"statusWriteFailed"),"Status IO does not prevent accepted rules");Directory.Delete(status);Call(p,"WriteStatus");Check(!Field<bool>(p,"statusWriteFailed"),"Status recovers");
Check(Field<int>(p,"headshotsBlocked")==17&&Field<Dictionary<int,long>>(p,"alerts")[1]==999,"Config changes preserve counters and alert history");
foreach(var menu in w.AllMenus)Check(menu.Configuration.MaxVisibleItems==5,"Compact pages");
Check(w.EntityAccesses==0,"No native entities accessed");p.Unload();Reject(()=>Save(s=>s with{Enabled=false}),"Unloaded handler cannot save");Console.WriteLine($"CombatMaintenanceChecks: {checks} passed; actual menus/files/permissions and retained counters; no native damage simulation.");
