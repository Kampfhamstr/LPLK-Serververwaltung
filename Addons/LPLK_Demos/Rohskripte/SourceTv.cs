using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.ProtobufDefinitions;
namespace LPLK_Demos;

public sealed partial class Plugin
{
 // Engine regression also addressed by SlynxCZ/GameFixes_mm (demo_record.cpp,
 // GPL-3.0). Keep the fix local to the unwanted console kick of the TV client.
 // Real players, shutdowns and other disconnect reasons always reach the engine.
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
 private delegate void DisconnectClient(nint client,ENetworkDisconnectionReason reason,nint detail);
 private IUnmanagedFunction<DisconnectClient>? tvDisconnect;
 private Guid tvHook;private bool tvGuard;private long tvKicksPrevented;private string? tvGuardError;
 public static bool ProtectTv(bool active,bool enabled,bool ram,bool isTv,ENetworkDisconnectionReason reason)
  =>active&&enabled&&ram&&isTv&&reason==ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED;
 private void InstallTvGuard()
 {
  if(!OperatingSystem.IsLinux())return;
  try{
   // SDK CServerSideClientBase: Disconnect directly follows Reconnect.
   // Resolve through installed gamedata rather than a hard-coded ABI offset.
   int reconnect=checked((int)Core.GameData.GetOffset("CServerSideClientBase::Reconnect"));
   if(reconnect!=7)throw new InvalidOperationException("Unbekannte SourceTV-ABI");
   nint table=Core.Memory.GetVTableAddress(Library.Engine,"CServerSideClient")??throw new InvalidOperationException("SourceTV-VTable fehlt");
   tvDisconnect=Core.Memory.GetUnmanagedFunctionByVTable<DisconnectClient>(table,reconnect+1);
   tvHook=tvDisconnect.AddHook(next=>(client,reason,detail)=>{
    if(reason==ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED&&client!=0&&
       ProtectTv(module.Active,settings.Enabled,settings.RamRecording,Core.Memory.ToServerSideClient(client).IsHLTV,reason))
    {Interlocked.Increment(ref tvKicksPrevented);return;}
    next()(client,reason,detail);
   });tvGuard=true;
  }catch(Exception e){tvGuardError=e.Message;Core.Logger.LogError(e,"LPLK_Demos: SourceTV-Schutz nicht verfügbar");}
 }
 private void RemoveTvGuard(){if(tvDisconnect is not null&&tvGuard)tvDisconnect.RemoveHook(tvHook);tvGuard=false;tvDisconnect=null;}
 private string RepairEmptyServer()
 {
  if(!tvGuard||!mapReady||!settings.RamRecording)return "SourceTV-Schutz nicht bereit";
  if(Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))return "Spieler verbunden; Aktivierung beim nächsten regulären Mapstart";
  ulong.TryParse(Core.Engine.WorkshopId,out ulong workshop);
  if(workshop>0){Core.Engine.ExecuteCommand("host_workshop_map "+workshop.ToString(System.Globalization.CultureInfo.InvariantCulture));return "Leere aktuelle Workshopmap mit SourceTV-Schutz neu laden";}
  string map=currentMap;
  if(map.Length==0){try{map=Core.Engine.GlobalVars.MapName;}catch(InvalidOperationException){return "Map noch nicht initialisiert; auf nächsten Mapstart warten";}}
  if(!System.Text.RegularExpressions.Regex.IsMatch(map,@"^[A-Za-z0-9_/-]{1,128}$"))return "Mapname nicht freigegeben";
  // One explicit maintenance action, only after a fresh native empty-server check.
  // A normal map reload recreates a previously kicked TV client without a process restart.
  Core.Engine.ExecuteCommand("changelevel \""+map+"\"");return "Leere aktuelle Map mit aktivem SourceTV-Schutz neu laden";
 }
}
