using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Shared;
namespace LPLK_Maps;
public static class CameraMapPolicy
{
 public static bool Allowed(string id,string source,bool ready)=>ready||!(id=="3075144323"||source.Equals("de_lionfort",StringComparison.OrdinalIgnoreCase));
}
public sealed partial class Plugin
{
 private IInterfaceManager? cameraInterfaces;
 public override void UseSharedInterface(IInterfaceManager manager)=>cameraInterfaces=manager;
 private bool CameraProtectionReady()=>cameraInterfaces is not null&&cameraInterfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Demos.CameraSafety.v1",out var api)&&api.TryGetValue("ready",out var call)&&call is Func<bool> ready&&ready();
 private bool CameraMapReady(MapEntry map)=>CameraMapPolicy.Allowed(map.Id,map.SourceFolder,false)||CameraProtectionReady();
 private string BlockLionfort()
 {
  automaticBlocked.Add("3075144323");
  if(pending?.Id=="3075144323"&&!transitionIssued){CompleteAutomaticJournal("Lionfort vor Rückfall gesperrt");pending=null;idleTransition=false;}
  if(nextMap?.Id=="3075144323")nextMap=null;
  if(preparedStartupMap=="3075144323")preparedStartupMap="";
  maintenancePublishedRevision=-1;nextMaintenanceStatus=0;TickWorkshopMaintenance(Environment.TickCount64);WriteStatus();
  return "Lionfort für automatische Auswahl gesperrt";
 }
 private string ReleaseLionfort()
 {
  if(AnyHuman())return "Freigabe wartet: Spieler verbunden";
  if(!CameraProtectionReady())return "Lionfort bleibt gesperrt: SourceTV-Kameraschutz nicht verfügbar";
  if(settings.AutomaticMapExcludedIds.Contains("3075144323"))return "Lionfort bleibt manuell gesperrt";
  if(pending is not null||transitionIssued)return "Freigabe wartet: Mapwechsel läuft";
  if(RamFile.Exists(AutomaticJournalPath))
  {
   var attempt=JsonSerializer.Deserialize<AutomaticMapAttempt>(RamFile.ReadAllText(AutomaticJournalPath));
   if(attempt is {Id:"3075144323",Pending:true})_=QueueAutomaticJournal(new("3075144323",false,DateTimeOffset.UtcNow,"SourceTV-Kameraschutz geprüft; Lionfort freigegeben"));
  }
  automaticBlocked.Remove("3075144323");maintenancePublishedRevision=-1;nextMaintenanceStatus=0;
  TickWorkshopMaintenance(Environment.TickCount64);PrepareStartupMap();WriteStatus();
  return "Lionfort für automatische Auswahl freigegeben; SourceTV-Kameraschutz wird vor jedem Wechsel erneut geprüft";
 }
}
