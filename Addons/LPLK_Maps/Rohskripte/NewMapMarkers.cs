using System.Text.Json;
namespace LPLK_Maps;
public sealed class NewMapMarkers
{
 public const int Days=14;
 private readonly Dictionary<string,DateTimeOffset> firstSeen;
 public NewMapMarkers(Dictionary<string,DateTimeOffset>? saved=null)
 {
  firstSeen=saved is null?[]:new(saved);
  if(firstSeen.Count>2000||firstSeen.Any(p=>!ValidId(p.Key)||p.Value==default))throw new InvalidDataException("Ungültiger NEW-Mapstatus.");
 }
 public static NewMapMarkers Parse(string json)=>new(JsonSerializer.Deserialize<Dictionary<string,DateTimeOffset>>(json)??throw new InvalidDataException("Leerer NEW-Mapstatus."));
 public bool Observe(IEnumerable<string> ids,DateTimeOffset now)
 {
  var additions=ids.Distinct().Where(id=>!firstSeen.ContainsKey(id)).ToArray();
  if(additions.Any(id=>!ValidId(id))||firstSeen.Count+additions.Length>2000)throw new InvalidDataException("Ungültige neue Map-IDs.");
  foreach(var id in additions)firstSeen[id]=now;
  return additions.Length>0;
 }
 public bool IsNew(string id,DateTimeOffset now)=>firstSeen.TryGetValue(id,out var at)&&now>=at&&now-at<TimeSpan.FromDays(Days);
 public static bool ValidId(string id)=>id is not null&&((ulong.TryParse(id,out var n)&&n>0&&n.ToString()==id)||System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[a-zA-Z][a-zA-Z0-9_]{1,99}\z"));
 // Stable grouping preserves the existing order within both groups. Eligibility
 // is decided by the caller; NEW never adds a blocked or unavailable map.
 public List<MapEntry> OrderNominations(IEnumerable<MapEntry> eligible,DateTimeOffset now)=>eligible.OrderByDescending(map=>IsNew(map.Id,now)).ToList();
 public string Label(string id,string existing,DateTimeOffset now)=>(IsNew(id,now)?"<font color='#90EE90'><b>NEW</b></font> ":"")+existing;
 public Dictionary<string,DateTimeOffset> Snapshot()=>new(firstSeen);
}
