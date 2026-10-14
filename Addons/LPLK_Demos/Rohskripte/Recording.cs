using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LPLK.Common;
namespace LPLK_Demos;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DemoSettings
{
 public bool Enabled {get;init;}=true;
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
 public bool CleanupEnabled {get;init;}
 public int RetentionHours {get;init;}=48;
 public static void Validate(DemoSettings s){if(s.IdlePlayerThreshold is <1 or >64||s.IdleSeconds is <5 or >86400||s.MinimumSeconds is <0 or >3600||s.RetentionHours is <1 or >8760)throw new InvalidDataException("Ungültige Demogrenzen");}
}
public sealed class DemoRecord
{
 public string File {get;set;}="";public string Map {get;set;}="";public DateTimeOffset Started {get;set;}=DateTimeOffset.UtcNow;
 public DateTimeOffset? Stopped {get;set;} public double Seconds {get;set;} public long Bytes {get;set;}
 public string State {get;set;}="starting";public string? Failure {get;set;}
 public bool Requested {get;set;}public int Requests {get;set;}
 public bool DeleteUnrequested {get;set;}public int MinimumSeconds {get;set;}
}
public sealed class DemoArchive(string root,string index)
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
  Records=File.Exists(index)?JsonSerializer.Deserialize<List<DemoRecord>>(File.ReadAllText(index))??throw new InvalidDataException("Leerer Demoindex"):[];
  if(Records.Count>100000||Records.Any(x=>x is null||!OwnedName(x.File))||Records.Select(x=>x.File).Distinct().Count()!=Records.Count)throw new InvalidDataException("Ungültiger Demoindex");
  foreach(var r in Records)if(r.State is "starting" or "recording" or "finalizing"){r.State="interrupted";r.Failure="Aufnahme durch Neustart/Entladen unterbrochen; Datei erhalten";}
  Save();
 }
 public void Save()=>AtomicJson.Write(index,Records);
 public bool ValidHeader(DemoRecord r)
 {
  string p=PathFor(r.File);if(!File.Exists(p))return false;
  try{using var stream=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);Span<byte> b=stackalloc byte[8];return stream.Read(b)==8&&b.SequenceEqual("PBDEMS2\0"u8);}
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
  r.Bytes=bytes;r.State="complete";
  // Only a verified, finalized file explicitly owned by this recorder is eligible.
  if(r.Seconds<r.MinimumSeconds||(r.DeleteUnrequested&&!r.Requested)){File.Delete(PathFor(r.File));r.State=r.Seconds<r.MinimumSeconds?"too-short":"unrequested";}
  Save();
 }
}
