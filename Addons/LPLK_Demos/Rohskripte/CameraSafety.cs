using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Misc;
namespace LPLK_Demos;

// Lionfort retains invalid point_viewcontrol pointers in the native TV director.
// Never inspect/dereference those entities. Bypass fixed cameras only on this map.
public static class CameraSafety
{
 public static bool Affected(string map)=>map.Equals("de_lionfort",StringComparison.OrdinalIgnoreCase)||map.Equals("3075144323",StringComparison.Ordinal);
 public static bool Matches(ReadOnlySpan<byte> bytes,string hex)=>bytes.SequenceEqual(Convert.FromHexString(hex));
}
public sealed partial class Plugin
{
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DirectorCall(nint director);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FixedShot(nint director,byte force);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CameraShot(nint director,int camera,int target);
 private IUnmanagedFunction<DirectorCall>? analyzeCameras,playerCamera;
 private IUnmanagedFunction<FixedShot>? fixedShot;
 private IUnmanagedFunction<CameraShot>? cameraShot;
 private Guid analyzeHook,fixedHook,cameraHook;
 private bool cameraGuard,playerFallback;
 private string? cameraGuardError;
 private long cameraAnalysesPrevented,cameraShotsReplaced;
 private bool GuardCamera=>module.Active&&CameraSafety.Affected(currentMap);
 private static void VerifyCameraFunction(nint table,int slot,string expected)
 {
  var ptr=Marshal.ReadIntPtr(table,slot*IntPtr.Size);if(ptr==0)throw new InvalidOperationException("Leere SourceTV-Kamerafunktion");
  var bytes=new byte[expected.Length/2];Marshal.Copy(ptr,bytes,0,bytes.Length);
  if(!CameraSafety.Matches(bytes,expected))throw new InvalidOperationException("Unbekannte SourceTV-Kamera-ABI, Slot "+slot);
 }
 private void InstallCameraGuard()
 {
  if(!OperatingSystem.IsLinux())return;
  try
  {
   nint table=Core.Memory.GetVTableAddress(Library.Server,"CCSHLTVDirector")??throw new InvalidOperationException("SourceTV-Kameratabelle fehlt");
   // Confirm all four entry points before touching code. Unknown engine builds
   // fail closed through the map-selection capability, rather than guessing slots.
   VerifyCameraFunction(table,38,"8b87a4000000b90001000039c80f4ec885c00f8ef1080000");
   VerifyCameraFunction(table,32,"8b87a400000085c07e0e400fb6f6e99dfdffff0f1f440000");
   VerifyCameraFunction(table,33,"5531c0b9200000004889e541574156488d95d0feffff4155");
   VerifyCameraFunction(table,34,"554863f64889e54157415641554189d54154534889fb4883");
   analyzeCameras=Core.Memory.GetUnmanagedFunctionByVTable<DirectorCall>(table,38);
   fixedShot=Core.Memory.GetUnmanagedFunctionByVTable<FixedShot>(table,32);
   cameraShot=Core.Memory.GetUnmanagedFunctionByVTable<CameraShot>(table,34);
   playerCamera=Core.Memory.GetUnmanagedFunctionByVTable<DirectorCall>(table,33);
   analyzeHook=analyzeCameras.AddHook(next=>self=>{if(GuardCamera){cameraAnalysesPrevented++;return;}next()(self);});
   fixedHook=fixedShot.AddHook(next=>(self,force)=>{if(GuardCamera){SafePlayerCamera(self);return;}next()(self,force);});
   cameraHook=cameraShot.AddHook(next=>(self,index,target)=>{if(GuardCamera){SafePlayerCamera(self);return;}next()(self,index,target);});
   cameraGuard=true;
  }
  catch(Exception e){RemoveCameraGuard();cameraGuardError=e.Message;Core.Logger.LogError(e,"Lionfort: SourceTV-Kameraschutz nicht verfügbar; automatische Auswahl bleibt gesperrt");}
 }
 private void SafePlayerCamera(nint self)
 {
  // Native player selection can fall back to fixed cameras when nobody is alive.
  // Do not re-enter that cycle; retain the previous shot until a player is usable.
  if(playerFallback)return;
  playerFallback=true;cameraShotsReplaced++;
  try{playerCamera!.Call(self);}finally{playerFallback=false;}
 }
 private void RemoveCameraGuard()
 {
  if(analyzeHook!=Guid.Empty)analyzeCameras?.RemoveHook(analyzeHook);
  if(fixedHook!=Guid.Empty)fixedShot?.RemoveHook(fixedHook);
  if(cameraHook!=Guid.Empty)cameraShot?.RemoveHook(cameraHook);
  analyzeHook=fixedHook=cameraHook=Guid.Empty;cameraGuard=false;
 }
}
