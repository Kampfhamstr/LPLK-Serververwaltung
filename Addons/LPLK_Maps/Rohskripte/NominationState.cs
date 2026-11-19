using System.Text.Json;
namespace LPLK_Maps;
public sealed record NominationSession(string Process,string Map,string Workshop,int ServerCount);
public sealed record NominationState(int Schema,NominationSession Session,Dictionary<ulong,string> Entries)
{
 public static NominationState Parse(string json)
 {
  var value=JsonSerializer.Deserialize<NominationState>(json)??throw new InvalidDataException("Leerer Nominierungsstatus.");
  if(value.Schema!=1||value.Session is null||string.IsNullOrWhiteSpace(value.Session.Process)||string.IsNullOrWhiteSpace(value.Session.Map)||value.Session.Workshop is null||value.Entries is null||value.Entries.Count>128||value.Entries.Any(x=>x.Key==0||!NewMapMarkers.ValidId(x.Value)))throw new InvalidDataException("Ungültiger Nominierungsstatus.");
  return value;
 }
 public Dictionary<ulong,string> Restore(NominationSession session)=>Session==session?new(Entries):[];
}
