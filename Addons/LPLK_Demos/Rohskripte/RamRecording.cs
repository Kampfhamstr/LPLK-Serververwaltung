using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using LPLK.Common;
using SwiftlyS2.Shared.Events;
namespace LPLK_Demos;
public sealed partial class Plugin
{
 // Source 2 can buffer recording output until tv_stoprecord. File absence during
 // a round is not proof of a failed recorder; always stop our owned recording.
 public static string RamRecordCommand(string directory,string file)
 {
  if(!DemoArchive.OwnedName(file))throw new InvalidDataException("Fremder Demopfad");
  string path=Path.ChangeExtension(Path.Combine(directory,file),null);
  if(path.IndexOfAny(['"','\r','\n'])>=0)throw new InvalidDataException("Ungültiger Aufnahmepfad");
  return "tv_record \""+path+"\"";
 }
 private RamExporter? ramExporter;private string ramDirectory="";private bool roundOpen,roundAttempted,ramReady;
 private long ramFree,nextExportRetry;private readonly Dictionary<string,long> exportRetry=[];
 private Task<(bool Ready,long Free,string? Error)>? ramInspection;private long ramCheckedAt;
 private readonly Dictionary<string,string> savedTv=[];
 private void InitializeRam()
 {
  ramDirectory=Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"demos","LPLK_RAM");
  ramExporter=new(ramDirectory,directory,()=>new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace>=10L*1024*1024*1024);
  if(!settings.RamRecording)return;
  var state=RamMount.Inspect(ramDirectory);ramReady=state.Ready;ramFree=state.Free;lastError=state.Error;ramCheckedAt=Environment.TickCount64;
  if(!ramReady)return;
  // Only generated filenames in our bounded mount are recoverable. Orphans are
  // retained after failures/reloads, never replaced by an empty archive index.
  foreach(string path in Directory.EnumerateFiles(ramDirectory,"lplk_*.dem")){
   string file=Path.GetFileName(path);if(!DemoArchive.OwnedName(file))continue;
   var record=archive.Records.FirstOrDefault(r=>r.File==file);
   if(record is null){record=new(){File=file,InRam=true,State="interrupted",Stopped=DateTimeOffset.UtcNow};archive.Records.Add(record);}
   record.InRam=true;ScheduleExport(record);
  }
 }
 private string RecordingPath(DemoRecord record)=>record.InRam?Path.Combine(ramDirectory,record.File):archive.PathFor(record.File);
 private bool RamAvailable()
 {
  if(ramInspection?.IsCompleted==true){var state=ramInspection.GetAwaiter().GetResult();ramInspection=null;ramReady=state.Ready;ramFree=state.Free;lastError=state.Error;ramCheckedAt=Environment.TickCount64;}
  if(ramInspection is null){string path=ramDirectory;ramInspection=Task.Run(()=>RamMount.Inspect(path));}
  if(ramReady&&ramFree>RamMount.Reserve&&Environment.TickCount64-ramCheckedAt<5000)return true;
  lastError??="RAM-Reserve oder Hintergrundprüfung nicht bereit; Aufnahme pausiert";return false;
 }
 private void ConfigureTv()
 {
  if(!settings.RamRecording){RestoreTv();return;}
  foreach(var (name,value) in new[]{("tv_enable",true),("tv_autorecord",false)}){
   var cvar=Core.ConVar.Find<bool>(name);if(cvar is null)continue;
   savedTv.TryAdd(name,cvar.Value?"1":"0");cvar.Value=value;
  }
  var immediate=Core.ConVar.Find<int>("tv_record_immediate");if(immediate is not null){savedTv.TryAdd("tv_record_immediate",immediate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));immediate.Value=1;}
 }
 private void RestoreTv(){foreach(var item in savedTv){if(item.Key=="tv_record_immediate"){var number=Core.ConVar.Find<int>(item.Key);if(number is not null)number.Value=int.Parse(item.Value,System.Globalization.CultureInfo.InvariantCulture);}else{var cvar=Core.ConVar.Find<bool>(item.Key);if(cvar is not null)cvar.Value=item.Value=="1";}}savedTv.Clear();}
 private void ScheduleExport(DemoRecord record)
 {
  record.State="finalizing";long due=Environment.TickCount64+1000;exportRetry[record.File]=due;if(!humansPlaying)ramExporter!.Enqueue(record.File,due,record.Seconds);
 }
 private void RetryExports(long now)
 {
  if(humansPlaying&&(roundOpen||lastRoundEnd==0||now-lastRoundEnd<1000))return;
  if(now<nextExportRetry)return;nextExportRetry=now+5000;
  foreach(var item in exportRetry.ToArray())if(now>=item.Value&&recording?.File!=item.Key){ramExporter!.Enqueue(item.Key,now,archive.Records.First(x=>x.File==item.Key).Seconds);exportRetry[item.Key]=now+30000;}
 }
 private void CollectExports()
 {
  bool changed=false;
  while(ramExporter is not null&&ramExporter.TryResult(out var result)){
   if(result is null)continue;var record=archive.Records.FirstOrDefault(x=>x.File==result.File);if(record is null)continue;
   if(result.Error is not null){record.Failure=lastError=result.Error;errors++;if(record.Stopped<DateTimeOffset.UtcNow.AddMinutes(-2)){record.State="failed";exportRetry.Remove(record.File);}else exportRetry[record.File]=Environment.TickCount64+30000;}
   else{record.Bytes=result.Bytes;record.Contents=result.Contents;record.State="complete";record.Failure=null;exportRetry.Remove(record.File);}
   changed=true;
  }
  if(changed){archive.Save();RoundData.Flush();WriteStatus();}
 }
 // Export completion and retries must run on empty/hibernating servers as well.
 [EventListener<EventDelegates.OnWorldUpdate>]public void RamWorldUpdate()
 {
  if(!module.Active||ramExporter is null)return;CollectExports();RetryExports(Environment.TickCount64);MaintainArchive(Environment.TickCount64);
 }
}
