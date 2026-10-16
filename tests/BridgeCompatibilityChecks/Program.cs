using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging.Abstractions;
using Mono.Cecil;
using SerververwaltungBridge;
using SwiftlyS2.Shared;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new Exception(label); }
var world = new World();
world.Call("ExecuteServerCommand", "  status  ");
Check(world.Commands.SequenceEqual(new[]{"status"}), "Command trim and dispatch");
foreach (var command in new[]{"", "  ", "status\nquit", "status\rquit", "status\0quit"})
    Reject(() => world.Call("ExecuteServerCommand", command), "Reject invalid server command");
Check(world.Commands.Count == 1, "Rejected commands never execute");
world.Call("SetCvar", "mp_maxrounds", "17");
Check(world.Commands.Last() == "mp_maxrounds \"17\"", "ConVar fallback uses common API");
world.CvarExists = true;
world.Call("SetCvar", "mp_maxrounds", "19");
Check(world.CvarValue == "19" && world.Commands.Count == 2, "Existing ConVar uses direct setter");
world.CvarThrows = true;
world.Call("SetCvar", "mp_maxrounds", "20");
Check(world.Commands.Last() == "mp_maxrounds \"20\"", "ConVar lookup exception fallback");
foreach (var name in new[]{"", "mp_maxrounds;quit", "mp_maxrounds\nquit"})
    Reject(() => world.Call("SetCvar", name, "1"), "Invalid cvar name rejected");
Reject(() => world.Call("SetCvar", "hostname", "x\ny"), "Invalid cvar value rejected");

foreach (bool workshop in new[]{false,true}) {
    world = new World(); world.Ready();
    var target = new MapSwitchTarget{IsWorkshop=workshop,WorkshopId=workshop?"3149392523":null,MapName="de_dust2",SourceFolder="de_nuke_2016",DisplayName="Test"};
    world.Call("QueueMapSwitchDispatch", 7L, target);
    Check(world.Commands.Count == 0 && world.Next.Count == 1, "No engine command outside scheduled update");
    world.Tick();
    Check(world.Commands.SequenceEqual(new[]{"nextlevel \"\""}) && world.Next.Count==1, "Preparation in first world update");
    world.Tick();
    Check(world.Commands.Count==3 && world.Commands[1] == (workshop?"nextlevel \"de_nuke_2016\"":"nextlevel \"de_dust2\""), "Nextlevel before map dispatch");
    Check(world.Commands[2] == (workshop?"host_workshop_map 3149392523":"changelevel \"de_dust2\""), "Map command last");
    Check(world.Timers.Count==1 && world.Get<bool>("_mapSwitchIssued"), "Watchdog armed");
    world.Call("CommitMapSwitch",7L,target);
    Check(world.Commands.Count==3, "No repeated commit");
    world.Timers[0]();
    Check(!world.Get<bool>("_mapSwitchInFlight") && !world.Get<bool>("_changingMap"), "Watchdog releases failed transition");
}
foreach (int staleAt in new[]{1,2}) {
    world = new World(); world.Ready();
    var target=new MapSwitchTarget{MapName="de_dust2",DisplayName="Dust2"};
    world.Call("QueueMapSwitchDispatch",7L,target);
    if(staleAt==2)world.Tick();
    world.Set("_mapLifecycleToken",8L); world.Tick();
    Check(world.Commands.Count==staleAt-1,"Stale lifecycle cannot switch map");
}
foreach (int failAt in new[]{1,2,3}) {
    world=new World{FailAt=failAt};world.Ready();
    world.Call("QueueMapSwitchDispatch",7L,new MapSwitchTarget{MapName="de_dust2",DisplayName="Dust2"});
    world.Tick();if(world.Next.Count>0)world.Tick();
    Check(!world.Get<bool>("_mapSwitchInFlight")&&!world.Get<bool>("_changingMap")&&world.Get<bool>("_mapStable"),"Engine error recovers transition");
    Check(world.Commands.Count==failAt-1,"No later command after engine error");
}
world=new World();world.Ready();
world.Call("QueueMapSwitchDispatch",7L,new MapSwitchTarget{IsWorkshop=true,WorkshopId="3149392523",SourceFolder="bad;quit",DisplayName="Test"});
world.Tick();world.Tick();
Check(world.Commands.SequenceEqual(new[]{"nextlevel \"\"","host_workshop_map 3149392523"}),"Unsafe workshop nextlevel never dispatched");

// Inspect the single stable-built DLL against the real API loaded by each harness.
using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(AppContext.BaseDirectory);
using var module=ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location,new ReaderParameters{AssemblyResolver=resolver});
int refs=0;
bool Framework(TypeReference t)=>t.GetElementType().Scope?.Name.StartsWith("SwiftlyS2.",StringComparison.Ordinal)==true;
foreach(var type in module.GetTypeReferences().Where(Framework)){Check(type.Resolve()!=null,"Framework type: "+type.FullName);refs++;}
foreach(var member in module.GetMemberReferences().Where(m=>Framework(m.DeclaringType))){
    Check(!member.Name.Contains("ExecuteCommandWithBuffer"),"Removed buffered API reference");
    if(member is MethodReference method)Check(method.Resolve()!=null,"Framework method: "+method.FullName);
    if(member is FieldReference field)Check(field.Resolve()!=null,"Framework field: "+field.FullName);
    refs++;
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{checks,frameworkRefs=refs,api=typeof(ISwiftlyCore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,plugin=typeof(Plugin).Assembly.GetName().Version?.ToString(),pluginSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Plugin).Assembly.Location))).ToLowerInvariant(),result="passed"}));

public class Proxy:DispatchProxy {
    public Func<MethodInfo,object?[]?,object?> Handler=null!;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!,args);
    public static object Make(Type type,Func<MethodInfo,object?[]?,object?> handler){var p=Create(type,typeof(Proxy));((Proxy)p).Handler=handler;return p;}
    public static object? Empty(Type t)=>t==typeof(void)?null:t.IsValueType?Activator.CreateInstance(t):null;
}
public sealed class World {
    public List<string> Commands=[];public Queue<Action> Next=[];public List<Action> Timers=[];
    public bool CvarExists,CvarThrows;public string? CvarValue;public int FailAt;private int attempts;
    public Plugin Plugin;
    public World(){
        var services=new Dictionary<Type,object>();
        var core=(ISwiftlyCore)Proxy.Make(typeof(ISwiftlyCore),(m,a)=>{
            if(m.Name=="get_Logger")return NullLogger.Instance;
            if(m.Name=="get_PluginDataDirectory"||m.Name=="get_GameFilesDirectory")return Path.GetTempPath();
            if(!services.TryGetValue(m.ReturnType,out var service))services[m.ReturnType]=service=Proxy.Make(m.ReturnType,Service);
            return service;
        });Plugin=new Plugin(core);
    }
    private object? Service(MethodInfo method,object?[]? args){
        switch(method.Name){
            case "ExecuteCommandWithBuffer":throw new Exception("Removed API must never be called");
            case "ExecuteCommand":if(++attempts==FailAt)throw new InvalidOperationException("engine fixture error");Commands.Add((string)args![0]!);return null;
            case "NextWorldUpdate":Next.Enqueue((Action)args![0]!);return null;
            case "DelayBySeconds":Timers.Add((Action)args![1]!);return new CancellationTokenSource();
            case "FindAsString":if(CvarThrows)throw new InvalidOperationException("cvar fixture error");return !CvarExists?null:Proxy.Make(method.ReturnType,(m,a)=>{if(m.Name=="set_ValueAsString")CvarValue=(string)a![0]!;return Proxy.Empty(m.ReturnType);});
            default:return Proxy.Empty(method.ReturnType);
        }
    }
    public void Ready(){Set("_mapLifecycleToken",7L);foreach(var f in new[]{"_mapLoaded","_mapStable","_mapSwitchInFlight","_mapSwitchPending"})Set(f,true);}
    public void Tick()=>Next.Dequeue()();
    public void Set(string name,object value)=>typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Plugin,value);
    public T Get<T>(string name)=>(T)typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Plugin)!;
    public void Call(string name,params object[] args){try{typeof(Plugin).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Plugin,args);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();}}
}
