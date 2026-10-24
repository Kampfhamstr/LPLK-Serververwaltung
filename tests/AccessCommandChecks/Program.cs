using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Whitelist;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object? v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
foreach(string name in new[]{"wl","uwl","a","abc_123","a-b.c",new string('a',40)})foreach(string input in new[]{name,"sw_"+name," SW_"+name.ToUpperInvariant()+" "})Check(AccessCommandNames.Normalize(input)==name,"Prefix once and canonical case");
foreach(string input in new[]{""," ","sw_","sw_sw_wl","!wl","/wl","wl value","wl;quit","wl\nquit","ä",new string('a',41)}){bool denied=false;try{AccessCommandNames.Normalize(input);}catch(InvalidDataException){denied=true;}Check(denied,"Invalid names refused: "+input);}
Check(AccessCommandNames.Read("{}") ==new AccessCommandNames("wl","uwl"),"Default names");
Check(AccessCommandNames.Read("{\"WhitelistCommand\":\"old_add\",\"UnWhitelistCommand\":\"old_remove\"}")==new AccessCommandNames("old_add","old_remove"),"Original v1.0 field names");
Check(AccessCommandNames.Read("{\"Whitelist\":{\"AddCommand\":\"sw_add\",\"WhitelistCommand\":\"ADD\",\"RemoveCommand\":\"remove\"}}")==new AccessCommandNames("add","remove"),"Equivalent old/new fields");
foreach(string body in new[]{"\"AddCommand\":null","\"AddCommand\":4","\"AddCommand\":\"a\",\"WhitelistCommand\":\"b\"","\"AddCommand\":\"a\",\"RemoveCommand\":\"sw_a\"","\"AddCommand\":\"uwl\"","\"RemoveCommand\":\"wl\"","\"AddCommand\":\"lplk_whitelist\""}){bool denied=false;try{AccessCommandNames.Read("{"+body+"}");}catch(InvalidDataException){denied=true;}Check(denied,"Ambiguous or reserved config refused");}
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AccessCommandChecks","test-data"),IncludePlayer=true,RejectEntityAccess=true};Directory.CreateDirectory(w.DataDirectory);
string cfg=Path.Combine(w.DataDirectory,"addons/swiftlys2/configs/plugins/LPLK_Whitelist/config.jsonc"),list=Path.Combine(w.DataDirectory,"whitelist.txt");Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
string actor="76561198000000000",extra="76561198000000001";File.WriteAllText(list,actor+"\n");int kicks=0;var basePlayer=w.Player;w.Player=Proxy.Make<IPlayer>((m,a)=>{if(m.Name=="Kick"){kicks++;return null;}return m.Invoke(basePlayer,a);});
JsonObject config=new(){["Whitelist"]=new JsonObject{["Enabled"]=true,["Mode"]=1,["PermissionForCommands"]="access.manage",["AddCommand"]="allow",["RemoveCommand"]="deny",["Future"]=42}};
void Save()=>File.WriteAllText(cfg,config.ToJsonString());void Names(string add,string remove){config["Whitelist"]!["AddCommand"]=add;config["Whitelist"]!["RemoveCommand"]=remove;Save();}
Save();var registered=new Dictionary<Guid,(string Name,ICommandService.CommandListener Handler)>();var occupied=new HashSet<string>(["sw_wl","sw_uwl","sw_lplk_wl_add","sw_lplk_wl_remove","sw_lplk_whitelist","sw_admin"]);var removed=new List<Guid>();string? failRegister=null,failRemove=null;int registrations=0;
var commands=Proxy.Make<ICommandService>((m,a)=>
{
 if(m.Name=="IsCommandRegistered")return occupied.Contains((string)a![0]!)||registered.Values.Any(x=>"sw_"+x.Name==(string)a[0]!);
 if(m.Name=="RegisterCommand")
 {string name=(string)a![0]!;Check(!(bool)a[2]!&&(string)a[3]! == "","Use framework prefix and existing handler authority");if(name==failRegister)throw new IOException("Register failed");Check(!occupied.Contains("sw_"+name)&&!registered.Values.Any(x=>x.Name==name),"No existing command overwritten");var id=Guid.NewGuid();registered.Add(id,(name,(ICommandService.CommandListener)a[1]!));registrations++;return id;}
 if(m.Name=="UnregisterCommand")
 {Check(a![0] is Guid,"Only GUID-owned unregister");var id=(Guid)a[0]!;Check(registered.ContainsKey(id),"GUID belongs to this plugin");if(registered[id].Name==failRemove)throw new IOException("Unregister failed");registered.Remove(id);removed.Add(id);return null;}
 return m.Invoke(w.Core.Command,a);
});
var core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_Command"?commands:m.Invoke(w.Core,a));var p=new Plugin(core);p.Load(false);
void Refresh(){Set(p,"nextPoll",0L);p.Tick();}
HashSet<string> Ids()=>Field<HashSet<string>>(p,"ids");
ICommandContext Context(string[] args,bool console=false)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch{"get_Sender"=>console?null:w.Player,"get_IsSentByPlayer"=>!console,"get_Args"=>args,"Reply"=>Reply((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
object? Reply(string text){w.Messages.Add(text);return null;}
ICommandService.CommandListener Handler(string name)=>registered.Values.Single(x=>x.Name==name).Handler;
void Run(string name,string[] args,bool console=false)=>Handler(name)(Context(args,console));
Check(registered.Count==2&&kicks==0&&Ids().SetEquals([actor]),"Custom names registered, access unaffected");Run("allow",[extra]);Check(!Ids().Contains(extra),"Unprivileged custom command denied");w.Permissions.Add("access.manage");Run("allow",[]);Check(w.Messages.Last()=="allow <SteamID64>","Usage uses configured name");Run("allow",[extra]);Check(Ids().Contains(extra),"Custom add reaches persisted change");Run("deny",[extra],true);Check(!Ids().Contains(extra),"Custom remove retains console path");
var ids=registered.Keys.ToHashSet();File.AppendAllText(cfg,"\n");Refresh();Check(ids.SetEquals(registered.Keys),"No churn for formatting-only reload");Names("SW_allow","sw_deny");Refresh();Check(ids.SetEquals(registered.Keys),"Already prefixed names do not double register");
config["Whitelist"]!["PermissionForCommands"]="new.manage";Save();Run("allow",[extra]);Check(!Ids().Contains(extra),"Current permission rechecked inside custom callback");w.Permissions.Add("lplk.admin");Run("allow",[extra]);Check(Ids().Contains(extra),"Existing global admin authority retained");Run("deny",[extra]);
var stale=Handler("allow");Names("new_allow","new_deny");stale(Context([extra]));Check(!Ids().Contains(extra)&&registered.Values.All(x=>x.Name.StartsWith("new_")),"Callback rejects rename discovered during its own reload");stale(Context([extra]));Check(!Ids().Contains(extra),"Retired callback inert");
stale=Handler("new_allow");Names("new_deny","new_allow");stale(Context([extra]));Check(!Ids().Contains(extra),"Action reversal invalidates current callback");Run("new_deny",[extra]);Check(Ids().Contains(extra),"Reversed mapping applies only next invocation");Run("new_allow",[extra]);Check(!Ids().Contains(extra),"Reversed removal correct");
ids=registered.Keys.ToHashSet();config["Whitelist"]!["Mode"]=2;Names("admin","unused");int oldKicks=kicks;Refresh();Check(kicks==oldKicks+1&&ids.SetEquals(registered.Keys)&&Field<string?>(p,"commandError") is not null,"Foreign name conflict retains commands but applies access rules");Check(occupied.Contains("sw_admin"),"Foreign command retained");
config["Whitelist"]!["Mode"]=1;Names("fresh_add","fresh_remove");failRegister="fresh_remove";Refresh();Check(ids.SetEquals(registered.Keys)&&Field<string?>(p,"commandError") is not null,"Partial registration rolled back, old pair retained");failRegister=null;Refresh();Check(registered.Values.Select(x=>x.Name).ToHashSet().SetEquals(["fresh_add","fresh_remove"])&&Field<string?>(p,"commandError") is null,"Retry installs full new pair");
var failedOld=Handler("fresh_add");failRemove="fresh_add";Names("final_add","final_remove");Refresh();Check(Field<string?>(p,"commandError") is not null,"Failed owned unregistration reported");failedOld(Context([extra]));Check(!Ids().Contains(extra),"Failed native removal cannot execute stale action");failRemove=null;Refresh();Check(registered.Count==2&&Field<string?>(p,"commandError") is null,"Owned cleanup retries by GUID");
ids=registered.Keys.ToHashSet();config["Whitelist"]!["AddCommand"]=null;config["Whitelist"]!["Enabled"]=false;Save();Refresh();Check(!Field<AccessSettings>(p,"settings").Enabled&&ids.SetEquals(registered.Keys)&&Field<string?>(p,"commandError") is not null,"Malformed optional name does not disable access reload");
string good=File.ReadAllText(cfg);File.WriteAllText(cfg,"invalid");Refresh();Check(ids.SetEquals(registered.Keys)&&Field<string?>(p,"reloadError") is not null,"Invalid entire config preserves aliases and rules");File.WriteAllText(cfg,good);
config["Whitelist"]!.AsObject().Remove("AddCommand");config["Whitelist"]!.AsObject().Remove("RemoveCommand");config["Whitelist"]!["WhitelistCommand"]="legacy_allow";config["Whitelist"]!["UnWhitelistCommand"]="legacy_deny";Save();Refresh();Run("legacy_allow",[extra]);Check(Ids().Contains(extra),"v1.0 legacy field executes");Run("legacy_deny",[extra]);
var legacy=Handler("legacy_allow");config["Whitelist"]!.AsObject().Remove("WhitelistCommand");config["Whitelist"]!.AsObject().Remove("UnWhitelistCommand");Names("wl","uwl");Refresh();Check(registered.Count==0&&Field<string?>(p,"commandError") is null,"Defaults use original attributes without duplicate registration");legacy(Context([extra]));Check(!Ids().Contains(extra),"Obsolete legacy name inert");p.Add(Context([extra]));Check(Ids().Contains(extra),"Original add remains usable");p.Remove(Context([extra]));Check(!Ids().Contains(extra),"Original remove remains usable");
Names("keep_add","keep_remove");Refresh();var final=Handler("keep_add");p.Unload();final(Context([extra]));p.Add(Context([extra],true));Check(registered.Count==0&&!Ids().Contains(extra)&&occupied.SetEquals(["sw_wl","sw_uwl","sw_lplk_wl_add","sw_lplk_wl_remove","sw_lplk_whitelist","sw_admin"]),"Unload cleans only owned custom registrations and blocks stale mutations");
Check((int)JsonNode.Parse(File.ReadAllText(cfg))!["Whitelist"]!["Future"]! ==42,"Unrelated configuration retained");Check(w.EntityAccesses==0,"No native entities used");Console.WriteLine($"AccessCommandChecks: {checks} passed; actual command registration, callbacks, current permissions, config reload, conflicts and unload with fake engine. No live access entries changed.");
