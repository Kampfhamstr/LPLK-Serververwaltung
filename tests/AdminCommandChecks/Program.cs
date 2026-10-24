using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Admin;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
void Denied(Action call,string label){bool failed=false;try{call();}catch(Exception e)when(e is InvalidDataException or ArgumentException){failed=true;}Check(failed,label);}
foreach(var input in new[]{"76561198000000000","STEAM_0:0:19867136","STEAM_1:0:19867136","[U:1:39734272]"})
    Check(LegacyAdminTargets.TrySteamId(input,out var id)&&id=="76561198000000000","Legacy SteamID canonicalized");
foreach(var input in new[]{"123","xSTEAM_1:0:1","STEAM_1:2:1","STEAM_2:0:1","STEAM_1:0:2147483648","STEAM_1:0:99999999999999999999","[U:1:-1]","[U:1:4294967296]","[U:2:10]","[U:1:10]junk","76561198000000000;quit",""})
    Check(!LegacyAdminTargets.TrySteamId(input,out _),"Invalid ID cannot target someone");
Check(LegacyAdminTargets.TrySteamId("[U:1:4294967295]",out _),"Maximum account fits");
Check(LegacyAdminTargets.RestartDelay([])==5,"Existing LPLK restart default retained");
foreach(int n in new[]{0,1,2,30,60})Check(LegacyAdminTargets.RestartDelay([n.ToString()])==n,"Explicit countdown preserved");
foreach(var input in new[]{new[]{"-1"},new[]{"61"},new[]{"1.5"},new[]{"1","2"},new[]{"999999999999"}})Denied(()=>LegacyAdminTargets.RestartDelay(input),"Reject out-of-range countdown rather than silently clamp");
foreach(var name in new[]{"custom","custom-name","x.y","abc_123",new string('x',40)})foreach(var prefix in new[]{"","sw_"})
    Check(AdminCommandNames.Normalize(prefix+name)==name,"Exactly one prefix");
foreach(var name in new[]{""," ","sw_sw_name","!ban","/ban","b;quit","b x","x\nx","ä",new string('x',41)})
    Denied(()=>AdminCommandNames.Normalize(name),"Invalid aliases");
foreach(var invalidConfig in new Dictionary<string,string[]>[]{new(){{"Unknown",["x"]}},new(){{"Ban",["kick"]}},new(){{"Ban",["quit"]}},new(){{"Ban",["menu"]}},new(){{"Ban",["x"]},{"Mute",["x"]}},new(){{"Ban",["x"]},{"ban",["y"]}},new(){{"Ban",null!}},new(){{"Ban",Enumerable.Range(0,17).Select(x=>"x"+x).ToArray()}}})
    Denied(()=>AdminCommandNames.Wanted(invalidConfig),"Invalid/ambiguous config refused");
var unsupported=AdminCommandNames.Wanted(new(){{"AddAdmin",["makeadmin"]},{"ChangeMap",["custommap"]}});
Check(unsupported["makeadmin"]=="AddAdmin"&&unsupported["custommap"]=="ChangeMap","Role and map aliases use guarded central/legacy handlers");
Check(AdminCommandNames.Same(new(){{"Ban",["sw_custom","custom"]}},new(){{"ban",["CUSTOM"]}}),"Semantic alias equivalence");
var original=new AdminSettings{Commands=new(){{"Ban",["custom"]}}};var clone=AdminSettingsFile.Copy(original);clone.Commands["Ban"][0]="other";Check(original.Commands["Ban"][0]=="custom","Deep clone protects mutable alias arrays");

var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminCommandChecks","current"),IncludePlayer=true,RejectEntityAccess=true};
Directory.CreateDirectory(w.DataDirectory);string cfg=Path.Combine(w.DataDirectory,"settings.json");
JsonObject config=new(){["Reasons"]=new JsonArray("Regelverstoß"),["Durations"]=new JsonArray(5,0),["Future"]=42,["Commands"]=new JsonObject()};
void Save()=>File.WriteAllText(cfg,config.ToJsonString());
void Names(string group,params string[] names){config["Commands"]![group]=new JsonArray(names.Select(x=>JsonValue.Create(x)).ToArray());Save();}
Save();string startConfig=File.ReadAllText(cfg);
var registrations=new Dictionary<Guid,(string Name,ICommandService.CommandListener Handler)>();
var foreign=new HashSet<string>(["say","sw_foreign"]);var removed=new List<Guid>();string? failRegister=null,failRemove=null;int registerCalls=0;
var fixedNames=AdminCommandNames.Groups.Values.SelectMany(x=>x).Select(x=>"sw_"+x).ToHashSet();
var fixedInfo=typeof(Plugin).GetMethods().Select(m=>m.GetCustomAttribute<Command>())
    .Where(c=>c is not null).Select(c=>new CommandInfo{CommandName=c!.Name,RegisterRaw=c.RegisterRaw,Permission=c.Permission,HelpText=c.HelpText}).ToList();
bool ownFixedVisible=true;
CommandInfo RawInfo(string name)=>new(){CommandName=name,RegisterRaw=true,Permission="",HelpText=""};
var commands=Proxy.Make<ICommandService>((m,a)=>
{
    if(m.Name=="GetCommandsByPlugin")return (string)a![0]! =="LPLK_Admin"&&ownFixedVisible?fixedInfo.Concat(registrations.Values.Select(x=>RawInfo(x.Name))).ToList():new List<CommandInfo>();
    if(m.Name=="GetAllCommandsInfo")return fixedInfo.Concat(foreign.Select(RawInfo)).Concat(registrations.Values.Select(x=>RawInfo(x.Name))).ToList();
    if(m.Name=="IsCommandRegistered")
    {
        string name=(string)a![0]!;
        bool Exists(string n)=>foreign.Contains(n)||fixedNames.Contains(n)||registrations.Values.Any(x=>x.Name==n);
        return Exists(name)||Exists("sw_"+name);
    }
    if(m.Name=="RegisterCommand")
    {
        var name=(string)a![0]!;Check((bool)a[2]!&&(string)a[3]! == "","Raw exact name; authorization in original handler");registerCalls++;
        if(name==failRegister)throw new InvalidOperationException("register failed");
        Check(!foreign.Contains(name)&&!registrations.Values.Any(x=>x.Name==name),"No command overwritten");
        var guid=Guid.NewGuid();registrations.Add(guid,(name,(ICommandService.CommandListener)a[1]!));return guid;
    }
    if(m.Name=="UnregisterCommand")
    {
        var guid=(Guid)a![0]!;Check(registrations.ContainsKey(guid),"Unregister only own GUID");
        if(registrations[guid].Name==failRemove)throw new InvalidOperationException("remove failed");
        removed.Add(guid);registrations.Remove(guid);return null;
    }
    return Proxy.Empty(m.ReturnType);
});
bool protectTarget=false;
var permissions=Proxy.Make(typeof(ISwiftlyCore).GetProperty("Permission")!.PropertyType,(m,a)=>
    m.Name=="PlayerHasPermission"&&protectTarget&&(ulong)a![0]! ==76561198000000002UL?(string)a[1]! =="admin.root":m.Invoke(w.Core.Permission,a));
var core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_Command"?commands:m.Name=="get_Permission"?permissions:m.Invoke(w.Core,a));
var p=new Plugin(core);
bool Occupied(string name)=>(bool)typeof(Plugin).GetMethod("CommandNameOccupied",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,[name])!;
Check(!Occupied("kick"),"Native sw_ fallback to own attributed command is not an exact collision");
Check(!Occupied("adminmenu"),"Native fallback to own attributed alias is not an exact collision");
ownFixedVisible=false;Check(Occupied("kick"),"Unknown ownership remains occupied");ownFixedVisible=true;
foreign.Add("kick");Check(Occupied("kick"),"Exact foreign command wins over own prefixed counterpart");foreign.Remove("kick");
Check(Occupied("foreign"),"Foreign prefixed alias is never shadowed by new raw alias");
p.Load(false);p.UseSharedInterface(w.Interfaces);
ICommandContext Context(string[] args,bool console=false)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch
{"get_Sender"=>console?null:w.Player,"get_IsSentByPlayer"=>!console,"get_Args"=>args,"Reply"=>Reply((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
object? Reply(string value){w.Messages.Add(value);return null;}
ICommandService.CommandListener Handler(string name)=>registrations.Values.Single(x=>x.Name==name).Handler;
void Run(string name,string[] args,bool console=false)=>Handler(name)(Context(args,console));
void Refresh(){Set(p,"nextSettingsCheck",0L);p.SettingsTick();}
Check(File.ReadAllText(cfg)==startConfig,"Load preserves existing settings byte-for-byte");
foreach(var bad in new[]{"{\"Commands\":{\"Ban\":[\"x\"],\"Ban\":[\"y\"]}}","{\"Commands\":{\"Ban\":[\"x\"],\"ban\":[\"y\"]}}","{\"Commands\":null}"})
{
    File.WriteAllText(cfg,bad);Refresh();Check(Field<string?>(p,"settingsError") is not null,"Ambiguous JSON alias groups keep prior rules");
}
File.WriteAllText(cfg,startConfig);Refresh();Check(Field<string?>(p,"settingsError") is null,"Valid config recovers after malformed alias groups");
foreach(var group in AdminCommandNames.Groups)foreach(var name in group.Value)
    Check(name=="say"||registrations.Values.Any(x=>x.Name==name),"All unoccupied default raw binds registered: "+name);
Check(!registrations.Values.Any(x=>x.Name.StartsWith("sw_",StringComparison.Ordinal)),"Existing prefixed attributes not duplicated");
Check(Field<string?>(p,"commandError")!.Contains("say"),"Foreign default collision visible");
foreign.Remove("say");Refresh();Check(Field<string?>(p,"commandError") is null,"Released foreign name can be reconciled");
int count=w.AdminCalls.Count;Run("ban",["STEAM_1:0:19867136","5"]);Check(w.AdminCalls.Count==count,"Alias does not grant ban permission");
w.Permissions.Add("admin.ban");Run("ban",["STEAM_1:0:19867136","5","Testgrund"]);Check(w.AdminCalls.Count==count+1&&w.AdminCalls.Last().Contains("76561198000000000"),"Raw bind invokes canonical ID with existing rights");
protectTarget=true;count=w.AdminCalls.Count;Run("ban",["76561198000000002","5"]);Check(w.AdminCalls.Count==count,"Offline target immunity preserved on raw bind");protectTarget=false;
Run("addban",["[U:1:39734272]","0"]);Check(w.AdminCalls.Last().Contains("\"minutes\":0"),"Offline legacy ID and permanent duration retained");
Run("ban",["1","7"]);Check(w.AdminCalls.Last().Contains("76561198000000000"),"Bare slot number resolves as old T3");
Run("ban",["#1","7"]);Check(w.AdminCalls.Last().Contains("76561198000000000"),"Existing explicit slot retained");
Run("ban",["@me","7"]);Check(w.AdminCalls.Last().Contains("76561198000000000"),"Existing self selector retained");
Names("Ban","sw_myban");Refresh();Run("myban",["[U:1:39734272]","2"]);Run("sw_myban",["[U:1:39734272]","2"]);Check(registrations.Values.Count(x=>x.Name.EndsWith("myban"))==2,"Custom raw and prefixed variants use same action");
count=w.AdminCalls.Count;w.Permissions.Clear();Run("myban",["76561198000000000","5"]);Check(w.AdminCalls.Count==count,"Current rights rechecked");
w.Permissions.Add("admin.ban");var stale=Handler("myban");Names("Ban","newban");stale(Context(["76561198000000000","5"]));Check(w.AdminCalls.Count==count&&!registrations.Values.Any(x=>x.Name=="myban"),"Invocation refresh invalidates removed name");
Names("Mute","newban");Refresh();Check(Field<string?>(p,"settingsError") is not null,"Ambiguous cross-action aliases rejected");
Run("newban",["76561198000000000","5"]);Check(w.AdminCalls.Last().StartsWith("ban:"),"Invalid config retains prior valid action");
config["Commands"]!.AsObject().Remove("Ban");Save();count=w.AdminCalls.Count;var previous=Handler("newban");previous(Context(["76561198000000000","5"]));Check(w.AdminCalls.Count==count,"Changed action cannot execute through an in-flight old handler");
Run("newban",["76561198000000000","5"]);Check(w.AdminCalls.Count==count,"Remapping alias cannot bypass mute rights");
w.Permissions.Add("admin.mute");Run("newban",["76561198000000000","5"]);Check(w.AdminCalls.Last().StartsWith("mute:"),"Remapped alias uses original destination policy");
Names("Ban","foreign");Refresh();Check(!registrations.Values.Any(x=>x.Name=="sw_foreign")&&foreign.Contains("sw_foreign"),"Existing foreign prefixed handler retained");
Names("Ban","fails");failRegister="sw_fails";Refresh();Check(Field<string?>(p,"commandError")!.Contains("sw_fails"),"Registration failure reported");failRegister=null;Refresh();Check(registrations.Values.Any(x=>x.Name=="sw_fails"),"Transient registration failure recovered");
var retired=Handler("fails");failRemove="fails";Names("Ban","replacement");Refresh();count=w.AdminCalls.Count;retired(Context(["76561198000000000","5"]));Check(w.AdminCalls.Count==count,"Failed removal leaves obsolete handler inert");failRemove=null;Refresh();Check(!registrations.Values.Any(x=>x.Name=="fails"),"Own retired registration cleaned after recovery");
w.Permissions.Add("admin.generic");Run("rr",["12"]);Check(w.AdminCalls.Last().Contains("\"countdown_seconds\":12"),"Restart argument reaches same Bridge");Run("restart",[]);Check(w.AdminCalls.Last().Contains("\"countdown_seconds\":5"),"Default countdown unchanged");
count=w.AdminCalls.Count;Run("rr",["61"]);Check(w.AdminCalls.Count==count,"No silent Bridge clamp of invalid countdown");
w.Permissions.Clear();w.Permissions.Add("lplk.admin");Run("rcon",["status"]);Check(w.AdminCalls.Count==count,"General admin still cannot run rcon");
w.Permissions.Add("admin.rcon");Run("rcon",["status"]);Check(w.AdminCalls.Last().StartsWith("server_command:"),"Separate rcon grant required");
// Menu editor uses the existing configuration privilege, validated names and confirmations.
p.Admin(w.Context);await w.Click(w.FindExact("Serververwaltung"));
for(int i=0;i<25;i++){try{await w.Click(w.FindExact("Einstellungen"));break;}catch(InvalidOperationException){await w.Click(w.FindExact("Nächste Seite"));}}
await w.Click(w.FindExact("Befehle"));await w.Click(w.FindExact("Adminmenü"));await w.Click(w.FindExact("Hinzufügen"));
Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,"control"),"Admin alias input");
Check(registrations.Values.Any(x=>x.Name=="control")&&registrations.Values.Any(x=>x.Name=="sw_control"),"Menu creates same validated variants");
await w.Click(w.FindExact("control"));await w.Click(w.FindExact("Entfernen"));await w.Click(w.FindExact("Bestätigen"));
Check(!registrations.Values.Any(x=>x.Name=="control"),"Menu confirms removal of custom alias");
Check((int)JsonNode.Parse(File.ReadAllText(cfg))!["Future"]! ==42,"Unknown configuration fields survive editor");
p.CommandStatus(Context([],true));Check(w.Messages.Last().Contains("\"version\":\"0.10.4\""),"Own diagnostic reports version and registered names");
var last=Handler("replacement");p.Unload();count=w.AdminCalls.Count;last(Context(["76561198000000000","5"],true));p.Ban(Context(["76561198000000000","5"],true));
Check(w.AdminCalls.Count==count&&registrations.Count==0&&foreign.SetEquals(["sw_foreign"]),"Unload blocks stale console callbacks and keeps foreign handler");
Check(w.EntityAccesses==0,"No synthetic native entities");
Console.WriteLine($"AdminCommandChecks: {checks} passed; real registration/menu/command/config paths with fake engine and Bridge; client and sanction-data migration remain pending.");
