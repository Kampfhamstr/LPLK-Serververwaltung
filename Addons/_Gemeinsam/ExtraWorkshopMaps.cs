using System.Text;
namespace LPLK.Common;

// Same reader in the normal map service and Surf. No automatic map switch.
public static class ExtraWorkshopMapList
{
 public const int MaximumBytes=128*1024,MaximumIds=2000;
 public const string RelativePath="addons/swiftlys2/configs/workshop-extra-maps.txt";
 public static string[] Parse(string text)
 {
  if(Encoding.UTF8.GetByteCount(text)>MaximumBytes)throw new InvalidDataException("Zusatzliste ist größer als 128 KiB.");
  var ids=new HashSet<string>(StringComparer.Ordinal);int line=0;
  foreach(string raw in text.TrimStart('\ufeff').Split('\n'))
  {
   line++;string value=raw.Trim();int comment=value.IndexOf('#');if(comment>=0)value=value[..comment].Trim();
   comment=value.IndexOf("//",StringComparison.Ordinal);if(comment>=0)value=value[..comment].Trim();
   if(value.Length==0)continue;
   if(value.Length>20||value.Any(c=>!char.IsAsciiDigit(c))||!ulong.TryParse(value,out ulong id)||id==0)
    throw new InvalidDataException($"Zusatzliste: ungültige Workshop-ID in Zeile {line}. Eine ID pro Zeile.");
   ids.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
   if(ids.Count>MaximumIds)throw new InvalidDataException("Zusatzliste enthält mehr als 2000 IDs.");
  }
  return ids.Order(StringComparer.Ordinal).ToArray();
 }
 public static string[] Read(string path)
 {
  path=Path.GetFullPath(path);
  for(var d=new DirectoryInfo(Path.GetDirectoryName(path)!);d is not null;d=d.Parent)
   if(d.LinkTarget is not null)throw new IOException("Verlinktes Verzeichnis der Zusatzliste.");
  var f=new FileInfo(path);if(f.LinkTarget is not null)throw new IOException("Zusatzliste darf keine Verknüpfung sein.");
  if(!f.Exists)return [];
  if(f.Length>MaximumBytes)throw new InvalidDataException("Zusatzliste ist größer als 128 KiB.");
  long length=f.Length,stamp=f.LastWriteTimeUtc.Ticks;
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
  using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);
  var chars=new char[MaximumBytes+1];int total=0,n;
  while(total<chars.Length&&(n=reader.Read(chars,total,chars.Length-total))>0)total+=n;
  f.Refresh();if(!f.Exists||f.Length!=length||f.LastWriteTimeUtc.Ticks!=stamp)throw new IOException("Zusatzliste wurde während des Lesens geändert; erneuter Versuch folgt.");
  return Parse(new string(chars,0,total));
 }
 public static (string[] Ids,string[] FileOnly) Merge(IEnumerable<string> collection,IEnumerable<string> configured,IEnumerable<string> seed,IEnumerable<string> extra,IEnumerable<string> previousFileOnly,bool useSeed)
 {
  var baseline=collection.Concat(configured).ToHashSet(StringComparer.Ordinal);
  if(useSeed)baseline.UnionWith(seed.Except(previousFileOnly,StringComparer.Ordinal));
  var additions=extra.ToHashSet(StringComparer.Ordinal);var only=additions.Except(baseline,StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
  baseline.UnionWith(additions);if(baseline.Count>MaximumIds)throw new InvalidDataException("Mehr als 2000 Maps in Kollektion und Zusatzlisten.");
  return(baseline.Order(StringComparer.Ordinal).ToArray(),only);
 }
}
public sealed class ExtraWorkshopMapFile(string path)
{
 private Task<(string[]? Ids,string? Error)>? pending;private long nextRead;
 public string[] Ids {get;private set;}=[];
 public string? Error {get;private set;}
 public DateTimeOffset? CheckedAt {get;private set;}
 public long Revision {get;private set;}
 public object Status=>new{file="game/csgo/"+ExtraWorkshopMapList.RelativePath,count=Ids.Length,error=Error,checkedAt=CheckedAt,revision=Revision};
 private (string[]? Ids,string? Error) Read()
 {
  try{return(ExtraWorkshopMapList.Read(path),null);}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException){return(null,e.Message);}
 }
 private bool Apply((string[]? Ids,string? Error) result)
 {
  CheckedAt=DateTimeOffset.UtcNow;Error=result.Error;
  if(result.Ids is null||Ids.SequenceEqual(result.Ids))return false;
  Ids=result.Ids;Revision++;return true;
 }
 public void Load()=>Apply(Read()); // Called in the permitted plugin load scope.
 public bool Poll(long now,Func<Func<(string[]? Ids,string? Error)>,Task<(string[]? Ids,string? Error)>> queue)
 {
  bool changed=false;
  if(pending?.IsCompleted==true){changed=Apply(pending.GetAwaiter().GetResult());pending=null;}
  if(pending is null&&now>=nextRead){nextRead=now+30000;pending=queue(Read);}
  return changed;
 }
}
