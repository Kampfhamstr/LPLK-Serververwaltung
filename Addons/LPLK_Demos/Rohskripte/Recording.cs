using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LPLK.Common;
namespace LPLK_Demos;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DemoSettings
{
 public bool Enabled {get;init;}=true;
 public bool FullMapRecording {get;init;}=true;
 public bool RamRecording {get;init;}
 public bool AutoRecord {get;init;}
 public bool CropRounds {get;init;}
 public bool RecordWarmup {get;init;}=true;
 public bool RequestsEnabled {get;init;}
 public bool PrintRequests {get;init;}=true;
 public bool DeleteUnrequested {get;init;}=true;
 public bool StopOnIdle {get;init;}
 public int IdlePlayerThreshold {get;init;}=1;
 public int IdleSeconds {get;init;}=300;
 public int MinimumSeconds {get;init;}=5;
 public bool CleanupEnabled {get;init;}=true;
 public int RetentionHours {get;init;}=72;
 public static void Validate(DemoSettings s){if(s.IdlePlayerThreshold is <1 or >64||s.IdleSeconds is <5 or >86400||s.MinimumSeconds is <0 or >3600||s.RetentionHours is <1 or >8760)throw new InvalidDataException("Ungültige Demogrenzen");}
}
public sealed class DemoRecord
{
 public string File {get;set;}="";public string Map {get;set;}="";public DateTimeOffset Started {get;set;}=DateTimeOffset.UtcNow;
 public DateTimeOffset? Stopped {get;set;} public double Seconds {get;set;} public long Bytes {get;set;}
 public string State {get;set;}="starting";public string? Failure {get;set;}
 public bool InRam {get;set;}
 public int IndexVersion {get;set;}public List<DemoParticipant> Players {get;set;}=[];
 public string? TimelineFile {get;set;}public int KillCount {get;set;}
 public string Kind {get;set;}="round";public bool IsTest {get;set;}public bool CompleteMap {get;set;}
 public int StartTick {get;set;}public int StartRound {get;set;}public string? StopReason {get;set;}
 public List<DemoRound> Rounds {get;set;}=[];public List<DemoKill> Kills {get;set;}=[];public bool TimelineTruncated {get;set;}
 public DemoContents? Contents {get;set;}
 public DemoRecord Copy(){var v=(DemoRecord)MemberwiseClone();v.Players=Players.Select(p=>p.Copy()).ToList();v.Rounds=Rounds.Select(r=>r.Copy()).ToList();v.Kills=[..Kills];return v;}
 public bool Requested {get;set;}public int Requests {get;set;}
 public bool DeleteUnrequested {get;set;}public int MinimumSeconds {get;set;}
}
public sealed class DemoArchive(string root,string index,bool buffered=false)
{
 public List<DemoRecord> Records {get;private set;}=[];
 public static bool OwnedName(string name)=>Regex.IsMatch(name,@"^lplk_[0-9]{8}_[0-9]{6}_[a-f0-9]{32}\.dem$",RegexOptions.CultureInvariant);
 public string PathFor(string name)
 {
  if(!OwnedName(name))throw new InvalidDataException("Fremder Demopfad");
  string full=Path.GetFullPath(root);for(var d=new DirectoryInfo(full);d is not null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Verlinkter Demopfad");
  string path=Path.Combine(full,name);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Verlinkte Demo");return path;
 }
 public void Load()
 {
  Directory.CreateDirectory(root);
  for(var parent=new DirectoryInfo(Path.GetFullPath(root));parent is not null;parent=parent.Parent)if(parent.Exists&&parent.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Verlinkter Demopfad");
  Records=File.Exists(index)?JsonSerializer.Deserialize<List<DemoRecord>>(File.ReadAllText(index))??throw new InvalidDataException("Leerer Demoindex"):[];
  if(Records.Count>100000||Records.Any(x=>x is null||!OwnedName(x.File))||Records.Select(x=>x.File).Distinct().Count()!=Records.Count)throw new InvalidDataException("Ungültiger Demoindex");
  foreach(var r in Records)if(r.State is "starting" or "recording" or "finalizing"){r.State="interrupted";r.Failure="Aufnahme durch Neustart/Entladen unterbrochen; Datei erhalten";}
  Save();
 }
 private bool dirty;private Task writer=Task.CompletedTask;private DemoRecord[]? written;public string? SaveError {get;private set;}
 public void Save(){if(buffered)dirty=true;else AtomicJson.Write(index,Records);}
 public void FlushBuffered()
 {
  if(!writer.IsCompleted)return;
  if(written is {} persisted){written=null;foreach(var saved in persisted){var live=Records.FirstOrDefault(r=>r.File==saved.File);if(live is null)continue;live.TimelineFile=saved.TimelineFile;live.KillCount=Math.Max(live.KillCount,saved.KillCount);if(live.State=="complete"&&saved.State=="complete"&&saved.TimelineFile is not null){live.Kills.Clear();foreach(var round in live.Rounds)round.Players.Clear();}}}
  if(!buffered||(!dirty&&SaveError is null))return;
  var snapshot=Records.Select(r=>r.Copy()).ToArray();dirty=false;
  writer=Task.Run(()=>{try{
   foreach(var record in snapshot){
    record.KillCount=Math.Max(record.KillCount,record.Kills.Count);
    if(record.Kills.Count>0||record.Rounds.Any(round=>round.Players.Count>0)){
     string path=RamExporter.SafePath(root,record.File)+".timeline.json";
     WritePhysical(path,JsonSerializer.Serialize(new{record.File,record.Rounds,record.Kills}));record.TimelineFile=record.File+".timeline.json";
    }
    record.Kills.Clear();foreach(var round in record.Rounds)round.Players.Clear();
   }
   WritePhysical(index,JsonSerializer.Serialize(snapshot));written=snapshot;SaveError=null;
  }catch(Exception e){SaveError=e.GetType().Name;}});
 }
 private static void WritePhysical(string path,string json)
 {
  string temp=path+".timeline-incoming";foreach(string p in new[]{path,temp})if(new System.IO.FileInfo(p).LinkTarget is not null)throw new IOException("Verlinkter Indexpfad");
  System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);System.IO.File.WriteAllText(temp,json);System.IO.File.Move(temp,path,true);
 }
 public void Drain(){writer.GetAwaiter().GetResult();dirty=true;FlushBuffered();writer.GetAwaiter().GetResult();}
 public bool ValidHeader(DemoRecord r)
 {
  string p=PathFor(r.File);if(!File.Exists(p))return false;
  try{using var stream=LPLK.Common.RamFile.Open(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);Span<byte> b=stackalloc byte[8];return stream.Read(b)==8&&b.SequenceEqual("PBDEMS2\0"u8);}
  catch(IOException){return false;} // The engine exclusively locks recordings until tv_stoprecord.
 }
 public void Cleanup(DateTimeOffset now,DemoSettings s)
 {
  if(!s.CleanupEnabled)return;
  foreach(var r in Records.Where(r=>r.State=="complete"&&r.Stopped is {} at&&now-at>TimeSpan.FromHours(s.RetentionHours)))
  {string p=PathFor(r.File);if(File.Exists(p))File.Delete(p);r.State="expired";}
  Save();
 }
 public void Finish(DemoRecord r,long bytes)
 {
  if(!ValidHeader(r)||bytes<16)throw new InvalidDataException("Keine gültige CS2-Demo");
  FinishVerified(r,bytes);
 }
 internal void FinishVerified(DemoRecord r,long bytes)
 {
  if(bytes<16)throw new InvalidDataException("Keine gültige CS2-Demo");
  r.Bytes=bytes;r.State="complete";
  // Only a verified, finalized file explicitly owned by this recorder is eligible.
  if(r.Seconds<r.MinimumSeconds||(r.DeleteUnrequested&&!r.Requested)){File.Delete(PathFor(r.File));r.State=r.Seconds<r.MinimumSeconds?"too-short":"unrequested";}
  Save();
 }
}
