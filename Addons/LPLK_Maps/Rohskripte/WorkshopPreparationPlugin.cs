using System.Text.Json;
using LPLK.Common;

namespace LPLK_Maps;

public sealed record AutomaticMapAttempt(string Id,bool Pending,DateTimeOffset At,string Outcome);
public sealed partial class Plugin
{
 private WorkshopMaintenance? maintenance;
 private long maintenancePublishedRevision=-1,nextMaintenanceStatus;
 private string maintenancePublishedState="";
 private readonly HashSet<string> automaticBlocked=[];
 private Task<bool>? automaticJournalTask;
 private Task<bool> automaticJournalTail=Task.FromResult(true);
 private string journalFor="",journalAttempt="";
 private string MaintenancePath=>Path.Combine(RoundFileStore.DataPath(Core),"workshop-readiness.json");
 private string AutomaticJournalPath=>Path.Combine(RoundFileStore.DataPath(Core),"automatic-map-attempt.json");
 private void LoadWorkshopMaintenance(bool hotReload)
 {
  maintenance=new(new SteamWorkshopMaintenanceBackend(WorkshopRoot,settings.MinimumFreeDiskGiB,settings.MaxWorkshopGiB));
  automaticBlocked.UnionWith(settings.AutomaticMapExcludedIds);
  if(RamFile.Exists(AutomaticJournalPath))
  {
   try{var attempt=JsonSerializer.Deserialize<AutomaticMapAttempt>(RamFile.ReadAllText(AutomaticJournalPath));if(attempt is {Pending:true}&&MapPolicy.WorkshopId(attempt.Id)==attempt.Id)automaticBlocked.Add(attempt.Id);}
   catch(JsonException){startupRotationStatus="Vorheriger automatischer Start nicht lesbar; automatische Startrotation ausgesetzt";startupMapGate.Complete();}
  }
  if(RamFile.Exists(MaintenancePath))
  {
   try{using var doc=JsonDocument.Parse(RamFile.ReadAllText(MaintenancePath));if(doc.RootElement.TryGetProperty("maps",out var rows))maintenance.Restore(JsonSerializer.Deserialize<WorkshopCheck[]>(rows.GetRawText())??[]);if(doc.RootElement.TryGetProperty("automaticBlocked",out var blocked))foreach(var id in JsonSerializer.Deserialize<string[]>(blocked.GetRawText())??[])if(MapPolicy.WorkshopId(id)==id)automaticBlocked.Add(id);}
   catch(JsonException){}
  }
 }
 private bool PreparedMapReady(MapEntry map)=>CameraMapReady(map)&&(!map.Workshop||!settings.WorkshopMaintenanceEnabled||maintenance?.Ready(map.Id,map.WorkshopUpdated,DateTimeOffset.UtcNow)==true);
 private void TickWorkshopMaintenance(long now)
 {
  if(maintenance is null)return;
  string current=maps.FirstOrDefault(m=>MapPolicy.Confirmed(m,CurrentMap,Workshop))?.Id??Workshop;
  var pool=maps.Where(m=>!settings.ExcludedWorkshopIds.Contains(m.Id)).ToArray();
  maintenance.Tick(now,DateTimeOffset.UtcNow,pool,current,pending?.Id??nextMap?.Id??preparedStartupMap,AnyHuman(),settings.Enabled&&settings.WorkshopMaintenanceEnabled&&!settings.BuiltinMapsOnly,testDownload is {Ready:false,Failed:false});
  if(now<nextMaintenanceStatus)return;
  if(maintenance.Revision==maintenancePublishedRevision&&maintenance.State==maintenancePublishedState&&now<nextMaintenanceStatus+25000)return;
  nextMaintenanceStatus=now+5000;maintenancePublishedRevision=maintenance.Revision;maintenancePublishedState=maintenance.State;
  var completed=maintenance.Results.Where(r=>r.State is "current" or "updated").ToArray();
  foreach(var row in completed)
  {
   var map=maps.FirstOrDefault(m=>m.Id==row.Id);if(map is null)continue;
   installed.Add(row.Id);
   // Keep administrator limits; invalidate physical measurements on a new revision.
   if(row.RemoteRevision>map.WorkshopUpdated||map.SourceFolder!=row.Source)
   {
    var updated=map with{WorkshopUpdated=row.RemoteRevision,SourceFolder=row.Source,RemoteMapVerified=true};
    if(map.WorkshopUpdated>0&&row.RemoteRevision>map.WorkshopUpdated)updated=updated with{MeasuredAt=null,LastProbeAt=null,ProbeError=null,TSpawns=null,CtSpawns=null,BombSites=null,Hostages=null};
    ReplaceMap(updated);
   }
  }
  DeferredJson.Queue(MaintenancePath,new{updatedAt=DateTimeOffset.UtcNow,version="0.4.19",state=maintenance.State,activeId=maintenance.ActiveId,preparedId=preparedStartupMap,selectedId=nextMap?.Id??pending?.Id,lionfortCameraProtection=CameraProtectionReady(),automaticBlocked=automaticBlocked.Order().ToArray(),maps=maintenance.Results.OrderBy(r=>r.Name).ToArray()});
 }
 private Task<bool> QueueAutomaticJournal(AutomaticMapAttempt record)
 {
  string path=AutomaticJournalPath;byte[] data=JsonSerializer.SerializeToUtf8Bytes(record);var previous=automaticJournalTail;
  return automaticJournalTail=Task.Run(async()=>
  {
   try{await previous.ConfigureAwait(false);}catch{}
   return await RoundFileStore.RunAfterRound(()=>
   {
    var info=new System.IO.FileInfo(path);if(info.LinkTarget is not null)throw new IOException("Verlinktes Mapjournal");
    var directory=new System.IO.DirectoryInfo(Path.GetDirectoryName(path)!);if(directory.LinkTarget is not null)throw new IOException("Verlinktes Mapverzeichnis");
    string temp=path+"."+Guid.NewGuid().ToString("N")+".incoming";using(var f=new System.IO.FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(data);f.Flush(true);}System.IO.File.Move(temp,path,true);return true;
   }).ConfigureAwait(false);
  });
 }
 private bool PrepareAutomaticJournal(MapEntry map)
 {
  if(!idleTransition)return true;
  if(journalFor!=map.Id){journalFor=map.Id;journalAttempt=map.Id;automaticJournalTask=QueueAutomaticJournal(new(map.Id,true,DateTimeOffset.UtcNow,"Automatischer Mapstart angefordert"));return false;}
  if(automaticJournalTask?.IsCompleted!=true)return false;
  try{return automaticJournalTask.GetAwaiter().GetResult();}
  catch{lastTransition="Automatischer Wechsel wartet: Rückfalljournal konnte nicht gespeichert werden";return false;}
 }
 private void CompleteAutomaticJournal(string outcome)
 {
  if(journalAttempt.Length==0)return;
  _=QueueAutomaticJournal(new(journalAttempt,false,DateTimeOffset.UtcNow,outcome));journalFor="";journalAttempt="";automaticJournalTask=null;
 }
}
