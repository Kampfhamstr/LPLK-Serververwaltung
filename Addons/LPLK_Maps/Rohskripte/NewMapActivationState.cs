using System.Text.Json;
namespace LPLK_Maps;

// One authoritative document keeps the migration baseline and first activation
// dates together. Entries are retained after removal so re-adding cannot renew NEW.
public sealed class NewMapActivationState
{
 public sealed record Document(int Schema,DateTimeOffset InitializedAt,string[] BaselineIds,Dictionary<string,DateTimeOffset> ActivatedAt);
 private readonly HashSet<string> baseline;
 public DateTimeOffset InitializedAt {get;}
 public NewMapMarkers Markers {get;}
 private NewMapActivationState(Document saved)
 {
  if(saved.Schema!=1||saved.InitializedAt==default||saved.BaselineIds is null||saved.ActivatedAt is null||saved.BaselineIds.Length>2000||saved.BaselineIds.Any(id=>!NewMapMarkers.ValidId(id))||saved.BaselineIds.Distinct().Count()!=saved.BaselineIds.Length)throw new InvalidDataException("Ungültiger NEW-Aktivierungsstatus.");
  baseline=saved.BaselineIds.ToHashSet();Markers=new(new(saved.ActivatedAt));InitializedAt=saved.InitializedAt;
 }
 public static NewMapActivationState Migrate(IEnumerable<string> existingActiveIds,NewMapMarkers legacy,DateTimeOffset now)=>new(new(1,now,existingActiveIds.Distinct().Order().ToArray(),legacy.Snapshot()));
 public static NewMapActivationState Parse(string json)=>new(JsonSerializer.Deserialize<Document>(json)??throw new InvalidDataException("Leerer NEW-Aktivierungsstatus."));
 public bool Observe(IEnumerable<string> readyIds,DateTimeOffset now)=>Markers.Observe(readyIds.Where(id=>!baseline.Contains(id)),now);
 public Document Snapshot()=>new(1,InitializedAt,baseline.Order().ToArray(),Markers.Snapshot());
}
