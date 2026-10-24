using System.Numerics;
using System.Reflection;
using System.Text.Json;
using LPLK_ThirdPerson;
using SwiftlyS2.Shared;
using SwiftlyS2.Core.Menus.OptionsBase;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object value)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,value);
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"CameraMaintenanceChecks","test-data"),RejectEntityAccess=true};
Directory.CreateDirectory(w.DataDirectory);string path=Path.Combine(w.DataDirectory,"settings.json");
var initial=new CameraSettings{ThirdPersonDistance=137.25f,ThirdPersonHeight=77.5f,SmoothCameraSpeed=.17f};
File.WriteAllText(path,JsonSerializer.Serialize(initial));w.Permissions.Add("lplk.admin");
bool cvar=true;int cvarWrites=0;
object? ConvarCall(MethodInfo m,object?[]? a)
{
 if(m.Name is "Find" or "CreateOrFind")return Proxy.Make(m.ReturnType,(op,v)=>{if(op.Name=="get_Value")return cvar;if(op.Name=="set_Value"){cvar=(bool)v![0]!;cvarWrites++;return null;}return Proxy.Empty(op.ReturnType);});
 return Proxy.Empty(m.ReturnType);
}
object? Hooks(MethodInfo m,object?[]? a)=>m.ReturnType.IsInterface?Proxy.Make(m.ReturnType,Hooks):Proxy.Empty(m.ReturnType);
var core=Proxy.Make<ISwiftlyCore>((m,a)=>{
 if(m.Name=="get_ConVar")return Proxy.Make(m.ReturnType,ConvarCall);
 if(m.Name=="get_GameHooks")return Proxy.Make(m.ReturnType,Hooks);
 if(m.Name=="get_EntitySystem"){w.EntityAccesses++;throw new InvalidOperationException("No native entities in local tests");}
 return m.Invoke(w.Core,a);
});
var plugin=new Plugin(core);plugin.Load(true);var cameraWorld=new CameraWorld();var cameras=new CameraController(cameraWorld);Set(plugin,"cameras",cameras);
CameraSettings Settings()=>Field<CameraSettings>(plugin,"settings");
void Refresh(){Set(plugin,"nextStatus",0L);plugin.Tick();}
void Save(CameraSettings s)=>File.WriteAllText(path,JsonSerializer.Serialize(s));
async Task Click(string s)=>await w.Click(w.FindExact(s));
void Input(string s,bool expected=true)=>Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,s)==expected,"Input "+s);
void Start(){cameraWorld.Reset();Check(cameras.Toggle(1,Settings(),true)=="Kamera AN","Start fake-world camera");}
string F(float value,string format="0.##")=>value.ToString(format);
Start();plugin.Administration(w.Context);
await Click("Abstand: "+F(137.25f));foreach(string bad in new[]{"19.99","250.01","NaN","Infinity","1e20","bad"})Input(bad,false);Input("249,75");
Check(Settings().ThirdPersonDistance==249.75f&&Settings().ThirdPersonHeight==77.5f&&cameras.Count==1,"Full fractional range changes distance without replacing camera lease");
await Click("Höhe: "+F(77.5f));Input("120");await Click("Glättung: "+F(.17f,"0.00"));Input("0.009",false);Input("0,01");
Check(Settings() is {ThirdPersonHeight:120,SmoothCameraSpeed:.01f},"Full height and smoothing bounds");
await Click("Abstand: "+F(249.75f));Input("20");await Click("Höhe: 120");Input("20");await Click("Glättung: "+F(.01f,"0.00"));Input("1");Check(Settings() is {ThirdPersonDistance:20,ThirdPersonHeight:20,SmoothCameraSpeed:1},"Other endpoints reachable");
await Click("Abstand: 20");Save(Settings() with{ThirdPersonHeight=99.5f,DamageMode="none"});Input("33.3");Check(Settings() is {ThirdPersonDistance:33.3f,ThirdPersonHeight:99.5f,DamageMode:"none"},"Stale input preserves newer independent fields");
Save(Settings() with{UseSmoothCam=false});await Click("Weiche Bewegung: AN");Check(Settings().UseSmoothCam,"Toggle reads latest external setting before editing");
DateTime stamp=File.GetLastWriteTimeUtc(path);Save(Settings() with{ThirdPersonDistance=123});File.SetLastWriteTimeUtc(path,stamp);Refresh();Check(Settings().ThirdPersonDistance==123,"Same timestamp content change detected");
// External ConVar remains authoritative; ordinary config edits must never override it.
cvar=false;Refresh();Check(cameras.Count==0&&cameraWorld.Destroyed>0&&cvarWrites==0,"ConVar off releases camera without rewriting ConVar");
plugin.Administration(w.Context);await Click("Abstand: 123");Input("124");Check(!cvar&&cvarWrites==0,"Numeric edit preserves external ConVar");
plugin.Administration(w.Context);string good=File.ReadAllText(path);File.WriteAllText(path,"{\"DamageMode\":\"bad\"}");await Click("Aktiv: AUS");Check(!cvar&&cvarWrites==0&&File.ReadAllText(path).Contains("bad"),"Failed activation neither overwrites invalid file nor enables ConVar");
File.WriteAllText(path,good);await Click("Aktiv: AUS");Check(cvar&&cvarWrites==1&&Settings().Enabled,"Explicit activation updates ConVar only after successful save");
Start();plugin.Administration(w.Context);await Click("Aktiv: AN");Check(!cvar&&!Settings().Enabled&&cameras.Count==0,"Disable releases current owned camera");
await Click("Aktiv: AUS");Start();
plugin.Administration(w.Context);await Click("Recht: thirdperson.use");Input("bad;command",false);Input("custom.tp");Check(Settings().UseTpPermission=="custom.tp","Permission input preserved");
await Click("Alle erlauben");Check(Settings().UseTpPermission=="","Explicit unrestricted-use choice preserved");
plugin.Administration(w.Context);await Click("Schadensmodus: none");Check(Settings().DamageMode=="all","Original damage mode transition");await Click("Messerhinweise: AN");Check(!Settings().EnableKnifeWarnings,"Knife warning toggle retained");
// Captured input/click callbacks retain permission and connection guards.
plugin.Administration(w.Context);await Click("Abstand: 124");var input=(InputMenuOption)w.Current!.Options[0];string bytes=File.ReadAllText(path);w.Permissions.Clear();input.SetValue(w.Player,"210");Check(File.ReadAllText(path)==bytes,"Revoked admin input cannot save");
w.Permissions.Add("lplk.admin");w.Session++;input.SetValue(w.Player,"211");Check(File.ReadAllText(path)==bytes,"Reconnected slot cannot reuse old input");
plugin.Administration(w.Context);var stale=w.FindExact("Weiche Bewegung: AN");w.Permissions.Clear();await w.Click(stale);Check(File.ReadAllText(path)==bytes,"Revoked toggle cannot save");w.Permissions.Add("lplk.admin");
good=File.ReadAllText(path);var prior=Settings();
foreach(string bad in new[]{"null","[]","{\"Unknown\":true}","{\"ThirdPersonDistance\":0}","{\"UseTpPermission\":null}",new string(' ',65537)})
{File.WriteAllText(path,bad);Refresh();Check(ReferenceEquals(prior,Settings())&&Field<string?>(plugin,"reloadError") is not null&&cameras.Count==1,"Invalid file retains last valid rules and camera");}
File.Delete(path);Refresh();Check(!File.Exists(path)&&ReferenceEquals(prior,Settings()),"Missing external file is not recreated during runtime");
File.WriteAllText(path,good);Refresh();Check(Field<string?>(plugin,"reloadError") is null,"Corrected config recovers");
// Status-file failures must not prevent real lifecycle policy from running.
string status=Path.Combine(w.DataDirectory,"status.json");File.Delete(status);Directory.CreateDirectory(status);Save(Settings() with{Enabled=false});Refresh();Check(cameras.Count==0&&Directory.Exists(status),"Unwritable status cannot prevent disabled-camera cleanup");Directory.Delete(status);Refresh();Check(File.Exists(status)&&!Field<bool>(plugin,"statusWriteFailed"),"Status persistence recovers");
foreach(var menu in w.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"At most five menu rows");foreach(var back in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Back unnumbered");}
plugin.MapUnload(null!);plugin.Tick();plugin.Unload();Check(w.EntityAccesses==0,"Maintenance tests never enter native entity system");
Console.WriteLine($"CameraMaintenanceChecks: {checks} passed; real menu/input/reload/ConVar paths and camera lifecycle with fake engine world. Client acceptance remains pending.");

sealed class CameraWorld:ICameraWorld
{
 public CameraPlayer Current=null!;public Dictionary<uint,Vector3> Cameras=[];public int Destroyed;private uint next=100;
 public CameraWorld()=>Reset();
 public void Reset()=>Current=new(new(1,1,"camera-session",1,2),true,true,false,uint.MaxValue,new(),new(0,0,64),0,0);
 public CameraPlayer? Player(int slot)=>Current;
 public uint Create(){Cameras[++next]=new();return next;}
 public Vector3? Position(uint id)=>Cameras.TryGetValue(id,out var v)?v:null;
 public void Move(uint id,Vector3 position,CameraPlayer player)=>Cameras[id]=position;
 public bool Attach(CameraPlayer p,uint camera){Current=p with{View=camera};return true;}
 public void Restore(CameraLease lease){if(Current.Owner==lease.Owner&&Current.View==lease.Camera)Current=Current with{View=uint.MaxValue};}
 public void Destroy(uint id){Cameras.Remove(id);Destroyed++;}
 public Vector3 Clip(CameraPlayer p,Vector3 from,Vector3 to)=>to;
}
