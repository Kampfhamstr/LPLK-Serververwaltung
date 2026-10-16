using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using LPLK_Maps;
using Microsoft.Extensions.Logging.Abstractions;
using Mono.Cecil;
using SwiftlyS2.Shared;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
foreach(bool workshop in new[]{false,true})foreach(bool nextLevel in new[]{false,true})foreach(bool installed in new[]{false,true}){
 using var w=new World();
 var map=new MapEntry{Id=workshop?"3070192462":"de_dust2",Name="Test",SourceFolder=workshop?"de_drawbridge":""};
 w.Set("pending",map);w.Set("settings",new MapSettings{SetNextLevel=nextLevel});
 if(installed)w.Get<HashSet<string>>("installed").Add(map.Id);
 long before=Environment.TickCount64;w.Call("IssuePending");
 string command=workshop?"host_workshop_map 3070192462":"changelevel de_dust2";
 Check(w.Commands.SequenceEqual(workshop&&nextLevel?new[]{"nextlevel de_drawbridge",command}:new[]{command}),"Native dispatch order");
 Check(w.Get<int>("attempts")==1&&w.Get<bool>("transitionIssued"),"Transition issued once");
 Check(w.Get<long>("transitionDue")>=before+(installed?8:240)*1000L,"Installed/download deadline");
 Check(w.Get<MapEntry>("pending")==map&&w.Get<string>("lastTransition").StartsWith("Angefordert:"),"Dispatch is not success confirmation");
 w.Workshop="999";w.Call("VerifyPending");
 Check(w.Get<int>("attempts")==2&&w.Get<MapEntry>("pending")==map,"Wrong map retries");
 if(workshop){w.Workshop=map.Id;w.Call("VerifyPending");Check(w.Get<object?>("pending")==null&&w.Get<string>("lastTransition")=="Bestätigt: "+map.Id,"Actual workshop ID confirms transition");}
 else{w.Call("VerifyPending");w.Call("VerifyPending");Check(w.Get<object?>("pending")==null&&w.Get<string>("lastTransition").StartsWith("Fehlgeschlagen:"),"Retry bound releases failed transition");}
}
foreach(string folder in new[]{"","bad;quit","../bad","bad\nquit"}){
 using var w=new World();w.Set("pending",new MapEntry{Id="3070192462",Name="Test",SourceFolder=folder});w.Call("IssuePending");
 Check(w.Commands.SequenceEqual(new[]{"host_workshop_map 3070192462"}),"Unsafe/absent nextlevel never executes");
}
using(var w=new World()){w.Call("IssuePending");Check(w.Commands.Count==0,"No pending map does nothing");}
foreach(string id in new[]{"de_dust2;quit","de_dust2\nquit","3070192462 quit"}){
 try{MapPolicy.Validate(new[]{new MapEntry{Id=id,Name="Test"}});throw new Exception("Unsafe map accepted");}catch(InvalidDataException){checks++;}
}
// Resolve every framework reference in the identical stable-built delivery DLL.
using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(AppContext.BaseDirectory);
using var module=ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location,new ReaderParameters{AssemblyResolver=resolver});
int refs=0;
bool Framework(TypeReference t)=>t.GetElementType().Scope?.Name.StartsWith("SwiftlyS2.",StringComparison.Ordinal)==true;
foreach(var type in module.GetTypeReferences().Where(Framework)){Check(type.Resolve()!=null,"Framework type "+type.FullName);refs++;}
foreach(var member in module.GetMemberReferences().Where(m=>Framework(m.DeclaringType))){
 Check(!member.Name.Contains("ExecuteCommandWithBuffer"),"Removed API reference");
 if(member is MethodReference method)Check(method.Resolve()!=null,"Framework method "+method.FullName);
 if(member is FieldReference field)Check(field.Resolve()!=null,"Framework field "+field.FullName);refs++;
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,frameworkRefs=refs,api=typeof(ISwiftlyCore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,plugin=typeof(Plugin).Assembly.GetName().Version?.ToString(),pluginSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(Plugin).Assembly.Location))).ToLowerInvariant(),result="passed"}));

public class Proxy:DispatchProxy {
 public Func<MethodInfo,object?[]?,object?> Handler=null!;
 protected override object? Invoke(MethodInfo? m,object?[]? a)=>Handler(m!,a);
 public static object Make(Type t,Func<MethodInfo,object?[]?,object?> h){var p=Create(t,typeof(Proxy));((Proxy)p).Handler=h;return p;}
 public static object? Empty(Type t)=>t==typeof(void)?null:t.IsValueType?Activator.CreateInstance(t):null;
}
public sealed class World:IDisposable {
 public List<string> Commands=[];public string Workshop="";public Plugin Plugin;
 private readonly string directory=Path.Combine(Path.GetTempPath(),"LplkMapsChecks-"+Guid.NewGuid().ToString("N"));
 public World(){
  Directory.CreateDirectory(directory);var services=new Dictionary<Type,object>();
  var core=(ISwiftlyCore)Proxy.Make(typeof(ISwiftlyCore),(m,a)=>{
   if(m.Name=="get_Logger")return NullLogger.Instance;
   if(m.Name=="get_PluginDataDirectory")return directory;
   if(!services.TryGetValue(m.ReturnType,out var s))services[m.ReturnType]=s=Proxy.Make(m.ReturnType,Service);return s;
  });Plugin=new Plugin(core);
 }
 private object? Service(MethodInfo m,object?[]? a){
  switch(m.Name){
   case "ExecuteCommandWithBuffer":throw new Exception("Removed API called");
   case "ExecuteCommand":Commands.Add((string)a![0]!);return null;
   case "get_GlobalVars":throw new NotSupportedException("Native globals unavailable in fixture");
   case "get_WorkshopId":return Workshop;
   default:return Proxy.Empty(m.ReturnType);
  }
 }
 public void Set(string n,object? v)=>typeof(Plugin).GetField(n,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Plugin,v);
 public T Get<T>(string n)=>(T)typeof(Plugin).GetField(n,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Plugin)!;
 public void Call(string n){try{typeof(Plugin).GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Plugin,null);}catch(TargetInvocationException e)when(e.InnerException!=null){ExceptionDispatchInfo.Capture(e.InnerException).Throw();}}
 public void Dispose(){var p=Path.GetFullPath(directory);if(Path.GetDirectoryName(p)!=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)||!Path.GetFileName(p).StartsWith("LplkMapsChecks-")||new DirectoryInfo(p).LinkTarget!=null)throw new Exception("Fixture cleanup scope");Directory.Delete(p,true);}
}
