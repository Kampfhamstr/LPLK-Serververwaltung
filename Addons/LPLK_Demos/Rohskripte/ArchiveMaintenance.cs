namespace LPLK_Demos;
public sealed record ArchiveSnapshot(long Bytes,long FreeBytes,int Files,string[] Expired,string? Error);
public static class DemoMaintenance
{
 public static ArchiveSnapshot Run(string root,DemoRecord[] records,DateTimeOffset now,DemoSettings settings,string? ramRoot=null)
 {
  var expired=new List<string>();long bytes=0,free=0;int count=0;
  try{
   foreach(var r in records){
    if(!settings.CleanupEnabled||r.State is "recording" or "starting" or "finalizing" or "expired"||r.Stopped is not {} at||now-at<TimeSpan.FromHours(settings.RetentionHours))continue;
    string path=RamExporter.SafePath(root,r.File);if(File.Exists(path))File.Delete(path);string timeline=path+".timeline.json";if(File.Exists(timeline)&&new FileInfo(timeline).LinkTarget is null)File.Delete(timeline);
    if(r.InRam&&ramRoot is not null){string ram=RamExporter.SafePath(ramRoot,r.File);if(File.Exists(ram))File.Delete(ram);}expired.Add(r.File);
   }
   foreach(string path in Directory.EnumerateFiles(root,"lplk_*.dem")){
    if(!DemoArchive.OwnedName(Path.GetFileName(path))||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
    bytes+=new FileInfo(path).Length;count++;
   }
   free=new DriveInfo(root).AvailableFreeSpace;return new(bytes,free,count,expired.ToArray(),null);
  }catch(Exception e)when(e is IOException or UnauthorizedAccessException){return new(bytes,free,count,expired.ToArray(),e.Message);}
 }
}
public sealed partial class Plugin
{
 private Task<ArchiveSnapshot>? maintenance;private ArchiveSnapshot? storage;private long lastRoundEnd;private bool humansPlaying;
 private void MaintainArchive(long now)
 {
  if(maintenance?.IsCompleted==true){storage=maintenance.GetAwaiter().GetResult();maintenance=null;foreach(string file in storage.Expired){var r=archive.Records.FirstOrDefault(x=>x.File==file);if(r is not null){r.State="expired";r.Kills.Clear();r.Rounds.Clear();r.Players.Clear();}exportRetry.Remove(file);}if(storage.Expired.Length>0){archive.Records.RemoveAll(r=>r.State=="expired"&&r.Stopped<DateTimeOffset.UtcNow.AddDays(-7));archive.Save();LPLK.Common.RoundData.Flush();}}
  if(maintenance is not null||now<nextCleanup||recording is not null||humansPlaying&&(roundOpen||lastRoundEnd==0||now-lastRoundEnd<1000))return;
  nextCleanup=now+60000;var snapshot=archive.Records.Select(r=>new DemoRecord{File=r.File,State=r.State,Stopped=r.Stopped,InRam=r.InRam}).ToArray();var rules=settings;string path=directory,ram=ramDirectory;
  maintenance=Task.Run(()=>DemoMaintenance.Run(path,snapshot,DateTimeOffset.UtcNow,rules,ram));
 }
}
