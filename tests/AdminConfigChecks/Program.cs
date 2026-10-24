using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Admin;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object? v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
object? Invoke(object p,string n,params object?[] a)=>p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,a);
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminConfigChecks","test-data"),IncludePlayer=true,RejectEntityAccess=true};
Directory.CreateDirectory(w.DataDirectory);string cfg=Path.Combine(w.DataDirectory,"settings.json"),status=Path.Combine(w.DataDirectory,"status.json");
void Save(AdminSettings s){var root=JsonSerializer.SerializeToNode(s)!;root["Extra"]=new JsonObject{["keep"]=42};File.WriteAllText(cfg,root.ToJsonString());}
Save(new(){Reasons=["Regelbruch","Testgrund"],Durations=[5,10,0]});
int closes=0;var menus=Proxy.Make<IMenuManagerAPI>((m,a)=>{if(m.Name=="CloseMenuForPlayer"){Check(ReferenceEquals(w.Current,a![1]),"Only current owned menu closed");w.Current=null;closes++;return null;}return m.Invoke(w.Core.MenusAPI,a);});
var core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_MenusAPI"?menus:m.Invoke(w.Core,a));
var p=new Plugin(core);p.Load(false);p.UseSharedInterface(w.Interfaces);
AdminSettings Settings()=>Field<AdminSettings>(p,"settings");
void Refresh(){Set(p,"nextSettingsCheck",0L);p.SettingsTick();}
async Task Click(string label)=>await w.Click(w.FindExact(label));
async Task Paged(string label){for(int i=0;i<30;i++){try{await Click(label);return;}catch(InvalidOperationException){await Click("Nächste Seite");}}throw new Exception(label);}
async Task Open(){p.Admin(w.Context);await Click("Serververwaltung");await Paged("Einstellungen");}
void Input(string value,bool valid=true)=>Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,value)==valid,"Input validated: "+value);
w.Permissions.Add("admin.kick");p.Admin(w.Context);Check(!w.Labels.Any(s=>s.Contains("Einstellungen")),"Kick privilege does not grant config editing");
w.Permissions.Clear();w.Permissions.Add("admin.root");await Open();Check(w.Labels.Count==5,"Root reaches settings without other grants");
await Click("Gründe");await Click("Testgrund");await Click("Ändern");foreach(string bad in new[]{""," ","a\nb",new string('x',151),new string('x',1000)})Input(bad,false);Input("Neuer Grund");
Check(Settings().Reasons.SequenceEqual(["Regelbruch","Neuer Grund"]),"Reason edits preserve order");Check((int)JsonNode.Parse(File.ReadAllText(cfg))!["Extra"]!["keep"]! ==42,"Unknown fields preserved");
await Click("Hinzufügen");Input(new string('r',150));Check(Settings().Reasons.Last().Length==150,"Full reason length reachable");
await Open();await Click("Dauern");await Click("10 Minuten");await Click("Ändern");foreach(string bad in new[]{"-1","525601","1234567","1.5","no"})Input(bad,false);Input("525600");Check(Settings().Durations.SequenceEqual([5,525600,0]),"Full duration range and permanent preserved");
await Click("Hinzufügen");Input("1");await Paged("1 Minuten");await Click("Entfernen");string before=File.ReadAllText(cfg);await Click("Zurück");Check(File.ReadAllText(cfg)==before,"Removal cancelled without write");await Paged("1 Minuten");await Click("Entfernen");await Click("Bestätigen");Check(Settings().Durations.SequenceEqual([5,525600,0]),"Confirmed removal only selected entry");
await Open();await Click("Gründe");await Click("Regelbruch");await Click("Ändern");var staleInput=(InputMenuOption)w.Current!.Options[0];before=File.ReadAllText(cfg);w.Permissions.Clear();staleInput.SetValue(w.Player,"denied");Check(File.ReadAllText(cfg)==before,"Input rechecks permissions");w.Permissions.Add("lplk.admin");w.Session++;staleInput.SetValue(w.Player,"reconnected");Check(File.ReadAllText(cfg)==before,"Reconnect invalidates input");
await Open();await Click("Gründe");await Click("Regelbruch");await Click("Ändern");staleInput=(InputMenuOption)w.Current!.Options[0];Save(Settings() with{Reasons=["Extern"]});staleInput.SetValue(w.Player,"obsolete");Check(Settings().Reasons.SequenceEqual(["Extern"])&&w.Current is null,"Changed lists reject stale input and close owned menu");
// A real pending kick confirmation must not invoke the shared backend after a config change.
w.Permissions.Add("admin.kick");Action<IPlayer> back=_=>{};
void Confirm()=>Invoke(p,"Confirm",w.Player,"Kick",w.Session.ToString(),"kick",w.Player.SteamID.ToString(),new Dictionary<string,object>{{"reason","Extern"}},back);
Confirm();var confirm=w.FindExact("Bestätigen");int calls=w.AdminCalls.Count;Save(Settings() with{Durations=[7]});await w.Click(confirm);Check(w.AdminCalls.Count==calls&&w.Current is null,"Stale kick confirmation cannot execute");
Confirm();confirm=w.FindExact("Bestätigen");before=File.ReadAllText(cfg);File.WriteAllText(cfg,"\n"+before+"\n");Refresh();await w.Click(confirm);Check(w.AdminCalls.Count==calls+1&&w.AdminCalls.Last().StartsWith("kick:"),"Formatting-only edit preserves real permitted confirmation");
Confirm();confirm=w.FindExact("Bestätigen");w.Permissions.Clear();await w.Click(confirm);Check(w.AdminCalls.Count==calls+1,"Action permission still rechecked");w.Permissions.Add("lplk.admin");
// An unrelated module's menu must survive semantic Admin configuration changes.
var foreign=w.Core.MenusAPI.CreateBuilder().Build();w.Current=foreign;int priorCloses=closes;DateTime stamp=File.GetLastWriteTimeUtc(cfg);Save(Settings() with{Durations=[8]});File.SetLastWriteTimeUtc(cfg,stamp);Refresh();Check(Settings().Durations[0]==8&&ReferenceEquals(w.Current,foreign)&&closes==priorCloses,"Same-mtime reload leaves other plugin menu open");
string good=File.ReadAllText(cfg);var valid=AdminSettingsFile.Copy(Settings());
foreach(string bad in new[]{"null","[]","{\"Reasons\":null}","{\"Reasons\":[]}","{\"Durations\":[]}","{\"Durations\":[-1]}","{\"Durations\":[525601]}","{\"Reasons\":[\"\"]}","{\"Reasons\":[\"a\"],\"reasons\":[\"b\"]}",new string(' ',AdminSettingsFile.MaxBytes+1)})
{File.WriteAllText(cfg,bad);Refresh();Check(AdminSettingsFile.Same(Settings(),valid)&&Field<string?>(p,"settingsError") is not null,"Invalid file retains last accepted lists");await Open();await Click("Gründe");await Click("Hinzufügen");Input("Rejected");Check(File.ReadAllText(cfg)==bad&&AdminSettingsFile.Same(Settings(),valid),"Menu refuses invalid file instead of replacing it");}
File.Delete(cfg);Refresh();Check(!File.Exists(cfg)&&AdminSettingsFile.Same(Settings(),valid),"Missing file not recreated at runtime");File.WriteAllText(cfg,good);Refresh();Check(Field<string?>(p,"settingsError") is null,"Valid configuration recovers");
await Open();await Click("Gründe");await Click("Extern");await Click("Entfernen");await Click("Bestätigen");Check(Settings().Reasons.Length==1,"Last reason cannot be removed");await Open();await Click("Dauern");await Click("8 Minuten");await Click("Entfernen");await Click("Bestätigen");Check(Settings().Durations.Length==1,"Last duration cannot be removed");
Save(new(){Reasons=Enumerable.Range(1,30).Select(n=>"Grund "+n).ToArray(),Durations=Enumerable.Range(0,20).ToArray()});Refresh();await Open();await Click("Gründe");await Paged("Grund 30");await Click("Ändern");Input("Letzter Grund");Check(Settings().Reasons[29]=="Letzter Grund","Last reason reachable on paginated menu");await Open();await Click("Dauern");await Paged("19 Minuten");await Click("Ändern");Input("20");Check(Settings().Durations[19]==20,"Last duration reachable");
var store=new AdminSettingsFile(cfg);before=File.ReadAllText(cfg);bool rejected=false;try{store.Change(s=>{s.Reasons[0]="";return s;});}catch(InvalidDataException){rejected=true;}Check(rejected&&store.Current.Reasons[0]=="Grund 1"&&File.ReadAllText(cfg)==before,"Invalid mutable edit cannot leak into active state");
rejected=false;try{store.Change(s=>{File.AppendAllText(cfg,"\n");return s with{Durations=[3]};});}catch(IOException){rejected=true;}Check(rejected&&File.ReadAllText(cfg)==before+"\n"&&store.Current.Durations.Length==20,"Intervening external edit refused");
File.WriteAllText(cfg,"{\"reasons\":[\"lower\"],\"durations\":[0],\"extra\":true}");store.Reload();store.Change(s=>s with{Durations=[2]});var node=JsonNode.Parse(File.ReadAllText(cfg))!.AsObject();Check(node.ContainsKey("reasons")&&!node.ContainsKey("Reasons")&&(bool)node["extra"]!,"Case and extra properties retained");Refresh();
File.Delete(status);Directory.CreateDirectory(status);Save(Settings() with{Durations=[4]});Refresh();Check(Settings().Durations[0]==4&&Field<bool>(p,"settingsStatusFailed"),"Status failure does not interrupt reload");Directory.Delete(status);Refresh();Check(File.Exists(status)&&!Field<bool>(p,"settingsStatusFailed"),"Status recovers");
foreach(var menu in w.AllMenus.Where(m=>!ReferenceEquals(m,foreign))){Check(menu.Configuration.MaxVisibleItems==5,"At most five visible rows");foreach(var b in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(b.Text,@"\d+[.)]"),"Back unnumbered");}
p.Unload();Check(w.EntityAccesses==0,"No native entity operations");Console.WriteLine($"AdminConfigChecks: {checks} passed; actual file/menu/permission/confirmation paths with a fake engine; no client acceptance or live sanctions.");
