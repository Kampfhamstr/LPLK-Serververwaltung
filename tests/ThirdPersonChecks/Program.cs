using System.Numerics;
using System.Reflection;
using LPLK_ThirdPerson;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftlyS2.Shared;

int checks=0;
void Check(bool b,string label){if(!b)throw new Exception(label);checks++;}
void Bad(CameraSettings s){bool rejected=false;try{CameraSettings.Validate(s);}catch(InvalidDataException){rejected=true;}Check(rejected,"Reject invalid settings "+s);}
var settings=new CameraSettings();CameraSettings.Validate(settings);
foreach(float v in new[]{float.NaN,float.PositiveInfinity,0,19,251})Bad(settings with{ThirdPersonDistance=v});
foreach(float v in new[]{float.NaN,float.NegativeInfinity,19,121})Bad(settings with{ThirdPersonHeight=v});
foreach(float v in new[]{float.NaN,float.PositiveInfinity,0,-1,1.01f})Bad(settings with{SmoothCameraSpeed=v});
foreach(string v in new[]{"bad;cmd","with space","<admin>",new string('x',129)})Bad(settings with{UseTpPermission=v});
Bad(settings with{UseTpPermission=null!});Bad(settings with{DamageMode="unknown"});
foreach(string mode in new[]{"back","none","all"}){CameraSettings.Validate(settings with{DamageMode=mode,UseTpPermission=""});checks++;}
Check(MathF.Abs(CameraMath.SmoothFactor(.3f,1f/64)-.3f)<.00001f,"Legacy smoothing at 64 Hz");
float a=CameraMath.SmoothFactor(.3f,1f/128);Check(MathF.Abs(1-(1-a)*(1-a)-.3f)<.00001f,"Tick-rate independent smoothing");
Check(CameraMath.StopBefore(Vector3.Zero,new(100,0,0),new(3,0,0))==Vector3.Zero,"No pushing behind trace start");
Check(CameraMath.StopBefore(Vector3.Zero,new(100,0,0),new(50,0,0))==new Vector3(45,0,0),"Wall clearance");
Check(CameraMath.StopBefore(Vector3.One,Vector3.One,Vector3.Zero)==Vector3.One,"Zero-length trace");
foreach(float yaw in new[]{0,45,90,180,270})
{
 var forward=-CameraMath.Backward(yaw);Check(CameraMath.BlockDamage("back",Vector3.Zero,-forward*30,yaw),"Behind damage blocked");
 Check(!CameraMath.BlockDamage("back",Vector3.Zero,forward*30,yaw),"Forward damage allowed");
 Check(CameraMath.BlockDamage("none",Vector3.Zero,forward*30,yaw),"None blocks damage");Check(!CameraMath.BlockDamage("all",Vector3.Zero,-forward*30,yaw),"All preserves damage");
}
var world=new World();var controller=new CameraController(world);
void Reset(){controller.Clear(e=>throw e);world.Reset();}
void Start(){Check(controller.Toggle(1,settings,true)=="Kamera AN","Start camera");Check(controller.Count==1&&world.Cameras.Count==1&&controller.Owns(world.Current!),"Attached lease");}
void Tick(bool enabled=true,CameraSettings? cfg=null)=>controller.Tick(cfg??settings,enabled,1f/64,e=>throw e);
foreach(var p in new[]{world.Current! with{Alive=false},world.Current! with{Allowed=false},world.Current! with{ForeignView=true},world.Current! with{Owner=world.Current.Owner with{Team=1}},world.Current! with{Yaw=float.NaN}})
{world.Current=p;Check(controller.Toggle(1,settings,true).Contains("nicht freigegeben")&&world.Cameras.Count==0,"Invalid player denied");world.Reset();}
Check(controller.Toggle(1,settings,false).Contains("nicht freigegeben"),"Global switch blocks activation");
Check(controller.Toggle(1,settings with{Enabled=false},true).Contains("nicht freigegeben"),"Settings switch blocks activation");
world.Current=null;Check(controller.Toggle(1,settings,true).Contains("nicht freigegeben"),"Disconnected denied");world.Reset();
Start();Check(controller.Toggle(1,settings,true)=="Kamera AUS"&&world.Current!.View==uint.MaxValue&&world.Cameras.Count==0,"Toggle restores and removes");
foreach(string reason in new[]{"permission","death","team","session","pawn","steam","disconnect","foreign","disabled","global","missing"})
{
 Reset();Start();var original=world.Current!;
 world.Current=reason switch{
 "permission"=>original with{Allowed=false},"death"=>original with{Alive=false},"team"=>original with{Owner=original.Owner with{Team=3}},
 "session"=>original with{Owner=original.Owner with{Session="replacement"},View=888},"pawn"=>original with{Owner=original.Owner with{Pawn=999},View=888},
 "steam"=>original with{Owner=original.Owner with{Steam=5},View=888},"disconnect"=>null,"foreign"=>original with{ForeignView=true,View=888},_=>original};
 if(reason=="missing")world.Cameras.Clear();
 Tick(reason!="global",reason=="disabled"?settings with{Enabled=false}:null);
 Check(controller.Count==0&&world.Cameras.Count==0,"Cleanup "+reason);
 if(reason is "session" or "pawn" or "steam" or "foreign")Check(world.Current!.View==888,"Do not overwrite replacement/foreign camera "+reason);
 else if(world.Current is not null)Check(world.Current.View==uint.MaxValue,"Restore own view "+reason);
}
Reset();world.AttachFails=true;try{controller.Toggle(1,settings,true);}catch(InvalidOperationException){}
Check(controller.Count==0&&world.Cameras.Count==0,"Attach failure rolls back camera");
Reset();world.Clipper=(_,from,to)=>from;Check(controller.Toggle(1,settings,true)=="Zu nah an einer Wand"&&world.Cameras.Count==0,"Front wall blocks before entity creation");
Reset();Start();world.Current=world.Current! with{Origin=new(100,0,0),Eye=new(100,0,64)};
world.ClipCalls=0;Tick();Check(world.ClipCalls==4,"Both offset and smoothed paths collision checked");
Check(world.Cameras.Values.All(CameraMath.Finite),"Finite smoothed position");
world.FailMove=true;int errors=0;controller.Tick(settings,true,.016f,_=>errors++);Check(errors==1&&controller.Count==0&&world.Cameras.Count==0,"Engine error cleans camera");
Reset();Start();int accesses=world.Accesses;controller.Forget();Check(world.Accesses==accesses&&controller.Count==0,"MapUnload forgets without engine calls");world.Cameras.Clear();world.Reset();
// Native engine must never be touched by cold load or map unload.
var boundary=new Boundary();var plugin=new Plugin(boundary.Core);plugin.Load(false);Check(boundary.EntityAccesses==0,"Cold load avoids entity system");
plugin.MapLoad(null!);plugin.MapUnload(null!);plugin.Tick();plugin.Unload();Check(boundary.EntityAccesses==0,"MapUnload and following tick/unload avoid engine access");
Console.WriteLine($"{checks} ThirdPerson checks passed (camera lifecycle, ownership, geometry, policies, cold load).");

sealed class World:ICameraWorld
{
 public CameraPlayer? Current;public Dictionary<uint,Vector3> Cameras=[];public bool AttachFails,FailMove;public int ClipCalls,Accesses;private uint next=100;
 public Func<CameraPlayer,Vector3,Vector3,Vector3>? Clipper;
 public World()=>Reset();
 public void Reset(){Current=new(new(1,76561198000000000,"session",42,2),true,true,false,uint.MaxValue,Vector3.Zero,new(0,0,64),0,0);AttachFails=FailMove=false;Clipper=null;}
 public CameraPlayer? Player(int slot){Accesses++;return Current;}
 public uint Create(){Accesses++;Cameras[++next]=Vector3.Zero;return next;}
 public Vector3? Position(uint id){Accesses++;return Cameras.TryGetValue(id,out var p)?p:null;}
 public void Move(uint id,Vector3 pos,CameraPlayer p){Accesses++;if(FailMove)throw new InvalidOperationException();Cameras[id]=pos;}
 public bool Attach(CameraPlayer p,uint id){Accesses++;if(AttachFails)return false;Current=p with{View=id};return true;}
 public void Restore(CameraLease l){Accesses++;if(Current is {} p&&p.Owner.Slot==l.Owner.Slot&&p.Owner.Steam==l.Owner.Steam&&p.Owner.Session==l.Owner.Session&&p.Owner.Pawn==l.Owner.Pawn&&p.View==l.Camera)Current=p with{View=uint.MaxValue};}
 public void Destroy(uint id){Accesses++;Cameras.Remove(id);}
 public Vector3 Clip(CameraPlayer p,Vector3 from,Vector3 to){Accesses++;ClipCalls++;return Clipper?.Invoke(p,from,to)??to;}
}
public class BoundaryProxy:DispatchProxy
{
 public Func<MethodInfo,object?[]?,object?> Handler=null!;
 protected override object? Invoke(MethodInfo? m,object?[]? a)=>Handler(m!,a);
 public static object Make(Type t,Func<MethodInfo,object?[]?,object?> h){var p=Create(t,typeof(BoundaryProxy));((BoundaryProxy)p).Handler=h;return p;}
}
sealed class Boundary
{
 public int EntityAccesses;public ISwiftlyCore Core;
 public Boundary()=>Core=(ISwiftlyCore)BoundaryProxy.Make(typeof(ISwiftlyCore),Call);
 private object? Call(MethodInfo m,object?[]? a)
 {
  if(m.Name=="get_EntitySystem"){EntityAccesses++;throw new InvalidOperationException("Entity system unavailable");}
  if(m.Name=="get_Logger")return NullLogger.Instance;
  if(m.Name=="get_PluginDataDirectory")return Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"ThirdPersonChecks","test-data");
  if(m.Name=="get_Value"&&m.ReturnType==typeof(bool))return true;
  if(m.ReturnType==typeof(void))return null;
  if(m.ReturnType.IsInterface)return BoundaryProxy.Make(m.ReturnType,Call);
  return m.ReturnType.IsValueType?Activator.CreateInstance(m.ReturnType):null;
 }
}
