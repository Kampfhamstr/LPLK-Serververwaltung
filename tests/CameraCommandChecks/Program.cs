using System.Numerics;
using System.Reflection;
using System.Text.Json;
using LPLK_ThirdPerson;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Core.Menus.OptionsBase;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
foreach(string name in new[]{"tp","thirdperson","camera","x_1","x-y.z",new string('a',40)})foreach(string value in new[]{name,"sw_"+name," SW_"+name.ToUpperInvariant()+" "})Check(CameraCommandName.Normalize(value)==name,"Canonical name/prefix once");
Check(CameraCommandName.Normalize("")==""&&CameraCommandName.Normalize(" ")=="","Empty means no extra alias");
foreach(string? name in new[]{null,"sw_","sw_sw_tp","!tp","/tp","tp x","tp;quit","tp\nquit","ä",new string('x',41),"lplk_thirdperson"}){bool denied=false;try{CameraCommandName.Normalize(name);}catch(InvalidDataException){denied=true;}Check(denied,"Malformed/reserved name");}
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"CameraCommandChecks","test-data"),RejectEntityAccess=true};Directory.CreateDirectory(w.DataDirectory);
string path=Path.Combine(w.DataDirectory,"settings.json");CameraSettings initial=new(){CustomTPCommand="camera"};File.WriteAllText(path,JsonSerializer.Serialize(initial));
var occupied=new HashSet<string>(["sw_tp","sw_thirdperson","sw_lplk_thirdperson","sw_admin","raw"]);var registered=new Dictionary<Guid,(string Name,ICommandService.CommandListener Handler)>();string? failRegister=null,failRemove=null;bool missingConfirmation=false,cvar=true;int cvarWrites=0;
var commands=Proxy.Make<ICommandService>((m,a)=>{
 if(m.Name=="IsCommandRegistered")return occupied.Contains((string)a![0]!)||(!missingConfirmation&&registered.Values.Any(x=>"sw_"+x.Name==(string)a[0]!));
 if(m.Name=="RegisterCommand"){string name=(string)a![0]!;Check(!(bool)a[2]!&&(string)a[3]! == "","Prefix added by framework, current use permission in handler");if(failRegister==name)throw new IOException("register failed");Check(!registered.Values.Any(x=>x.Name==name)&&!occupied.Contains("sw_"+name),"No foreign command replaced");Guid id=Guid.NewGuid();registered.Add(id,(name,(ICommandService.CommandListener)a[1]!));return id;}
 if(m.Name=="UnregisterCommand"){Check(a![0] is Guid,"Unregister only owned GUID");var id=(Guid)a[0]!;Check(registered.ContainsKey(id),"Known registration");if(failRemove==registered[id].Name)throw new IOException("remove failed");registered.Remove(id);return null;}
 return m.Invoke(w.Core.Command,a);
});
object? Convar(MethodInfo m,object?[]? a)=>m.Name is "Find" or "CreateOrFind"?Proxy.Make(m.ReturnType,(op,v)=>{if(op.Name=="get_Value")return cvar;if(op.Name=="set_Value"){cvar=(bool)v![0]!;cvarWrites++;return null;}return Proxy.Empty(op.ReturnType);}):Proxy.Empty(m.ReturnType);
object? Hooks(MethodInfo m,object?[]? a)=>m.ReturnType.IsInterface?Proxy.Make(m.ReturnType,Hooks):Proxy.Empty(m.ReturnType);
var core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name switch{"get_Command"=>commands,"get_ConVar"=>Proxy.Make(m.ReturnType,Convar),"get_GameHooks"=>Proxy.Make(m.ReturnType,Hooks),_=>m.Invoke(w.Core,a)});
var p=new Plugin(core);p.Load(true);CameraSettings Settings()=>Field<CameraSettings>(p,"settings");
var world=new FakeCamera(()=>Settings(),w);var cameras=new CameraController(world);Set(p,"cameras",cameras);
void Save(CameraSettings s)=>File.WriteAllText(path,JsonSerializer.Serialize(s));void Name(string value)=>Save(Settings() with{CustomTPCommand=value});void Refresh(){Set(p,"nextStatus",0L);p.Tick();}
ICommandService.CommandListener Handler(string name)=>registered.Values.Single(x=>x.Name==name).Handler;
Check(registered.Count==1&&registered.Values.Single().Name=="camera","Configured command installed");Handler("camera")(w.Context);Check(cameras.Count==0,"Unprivileged camera denied");w.Permissions.Add("thirdperson.use");Handler("camera")(w.Context);Check(cameras.Count==1,"Custom command opens owned camera");Handler("camera")(w.Context);Check(cameras.Count==0,"Custom command toggles off");
var ids=registered.Keys.ToHashSet();File.AppendAllText(path,"\n");Refresh();Name("SW_CAMERA");Refresh();Check(ids.SetEquals(registered.Keys),"Formatting and equivalent prefix preserve registration");
Save(Settings() with{UseTpPermission="new.use"});Handler("camera")(w.Context);Check(cameras.Count==0&&Settings().UseTpPermission=="new.use","Callback reloads current permission before action");w.Permissions.Add("new.use");Handler("camera")(w.Context);Check(cameras.Count==1,"Changed permission works");Handler("camera")(w.Context);
var stale=Handler("camera");Name("view");stale(w.Context);Check(cameras.Count==0&&registered.Values.Single().Name=="view","Rename during invocation invalidates old callback");stale(w.Context);Check(cameras.Count==0,"Retired callback inert");Handler("view")(w.Context);Check(cameras.Count==1,"New command functional");Handler("view")(w.Context);
ids=registered.Keys.ToHashSet();foreach(string conflict in new[]{"admin","raw"}){Name(conflict);Refresh();Check(ids.SetEquals(registered.Keys)&&Field<string?>(p,"commandError") is not null,"Foreign prefixed/raw command conflict keeps previous alias");}
Name("fresh");failRegister="fresh";Refresh();Check(ids.SetEquals(registered.Keys),"Registration failure retains previous alias");failRegister=null;missingConfirmation=true;Refresh();Check(ids.SetEquals(registered.Keys),"Unconfirmed registration removed, old retained");missingConfirmation=false;Refresh();Check(registered.Values.Single().Name=="fresh"&&Field<string?>(p,"commandError") is null,"Retry succeeds");
stale=Handler("fresh");failRemove="fresh";Name("last");Refresh();stale(w.Context);Check(cameras.Count==0&&registered.Count==2&&Field<string?>(p,"commandError") is not null,"Failed old removal inert and reported");failRemove=null;Refresh();Check(registered.Count==1&&Field<string?>(p,"commandError") is null,"Owned retirement retry");
var previous=Settings();Save(previous with{CustomTPCommand="!bad",Enabled=false});Refresh();Check(ReferenceEquals(Settings(),previous)&&Field<string?>(p,"reloadError") is not null,"Invalid config preserves last valid rules and alias");Save(previous);
Handler("last")(w.Context);Check(cameras.Count==1,"Camera opens before native ConVar disable");cvar=false;Handler("last")(w.Context);Check(cameras.Count==0,"ConVar disabled releases owned camera");Handler("last")(w.Context);Check(cameras.Count==0&&cvarWrites==0,"Custom alias cannot override ConVar");cvar=true;
foreach(string name in new[]{"tp","thirdperson",""}){Name(name);Refresh();Check(registered.Count==0,"Fixed or empty alias creates no duplicate");p.Toggle(w.Context);Check(cameras.Count==1,"Fixed entry remains usable");p.Toggle(w.Context);Check(cameras.Count==0,"Fixed toggle preserved");}
// Real admin input: current file edits, revoked authority, connection and length guards.
w.Permissions.Add("lplk.admin");p.Administration(w.Context);await w.Click(w.FindExact("Befehl: keiner"));var input=(InputMenuOption)w.Current!.Options[0];Check(!input.SetValue(w.Player,new string('x',45)),"Overlong input rejected rather than truncated into valid alias");Save(Settings() with{ThirdPersonHeight=91});Check(input.SetValue(w.Player,"sw_menu_camera"),"Admin alias input accepted");Check(Settings().ThirdPersonHeight==91&&registered.Values.Single().Name=="menu_camera","Menu preserves newer camera settings");
await w.Click(w.FindExact("Befehl: menu_camera"));input=(InputMenuOption)w.Current!.Options[0];string bytes=File.ReadAllText(path);w.Permissions.Remove("lplk.admin");input.SetValue(w.Player,"denied");Check(File.ReadAllText(path)==bytes,"Revoked admin cannot change alias");w.Permissions.Add("lplk.admin");w.Session++;input.SetValue(w.Player,"reconnected");Check(File.ReadAllText(path)==bytes,"Stale connection input inert");
stale=Handler("menu_camera");p.MapUnload(null!);stale(w.Context);Check(cameras.Count==0,"Unready map blocks alias");p.Unload();stale(w.Context);p.Toggle(w.Context);Check(registered.Count==0&&cameras.Count==0&&occupied.SetEquals(["sw_tp","sw_thirdperson","sw_lplk_thirdperson","sw_admin","raw"]),"Unload blocks stale callbacks and retains foreign commands");
Check(w.EntityAccesses==0&&cvarWrites==0,"No native entities or ConVar writes");Console.WriteLine($"CameraCommandChecks: {checks} passed; actual aliases/admin inputs/reload with fake command and camera world. Client acceptance pending.");
sealed class FakeCamera(Func<CameraSettings> settings,MenuWorld menus):ICameraWorld
{
 private uint view=uint.MaxValue,next=10;private Dictionary<uint,Vector3> entities=[];
 public CameraPlayer? Player(int slot)=>new(new(slot,1,"session",2,2),true,settings().UseTpPermission.Length==0||menus.Permissions.Contains(settings().UseTpPermission),false,view,new(),new(0,0,64),0,0);
 public uint Create(){entities[++next]=new();return next;}public Vector3? Position(uint id)=>entities.GetValueOrDefault(id);
 public void Move(uint id,Vector3 pos,CameraPlayer p)=>entities[id]=pos;public bool Attach(CameraPlayer p,uint id){view=id;return true;}
 public void Restore(CameraLease l){view=uint.MaxValue;}public void Destroy(uint id)=>entities.Remove(id);public Vector3 Clip(CameraPlayer p,Vector3 from,Vector3 to)=>to;
}
