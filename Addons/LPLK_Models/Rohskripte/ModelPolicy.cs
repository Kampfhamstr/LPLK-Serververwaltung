using System.Security.Cryptography;
using System.Text;
namespace LPLK_Models;
public sealed record ModelEntry(string Id,string Name,string Path,string Workshop,string Category,bool Enabled=true,long DailyPrice=100000,string Requirement="",long Required=0,int Team=2);
public sealed record ModelSettings
{
 public bool Enabled {get;init;}=false;
 public int FreeCount {get;init;}=10;
 public bool TestBotProbe {get;init;}
 public bool Halloween {get;init;}=true;
 public bool BotModels {get;init;}=true;
 public bool AnnouncementsEnabled {get;init;}=true;
 public int AnnouncementMinutes {get;init;}=10;
 public ModelEntry[] Models {get;init;}=[];
 public static void Validate(ModelSettings s)
 {
  if(s.FreeCount is <1 or >30||s.Models is null||s.Models.Length>512||s.AnnouncementMinutes is <3 or >120)throw new InvalidDataException("Ungültiger Modellkatalog.");
  var ids=new HashSet<string>(StringComparer.Ordinal);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var m in s.Models){
   if(m is null||m.Path is null||m.Name is null||m.Category is null)throw new InvalidDataException("Unvollständiges Modell.");
   if(m.Team is not (2 or 3))throw new InvalidDataException("Modell benötigt genau ein Team: 2=T, 3=CT.");
   if(ModelPolicy.Vehicle(m.Path))throw new InvalidDataException("Fahrzeugmodelle sind ausgeschlossen.");
   if(m.DailyPrice is <1 or >1_000_000_000||m.Requirement is not ("" or "RobberyOrders" or "LottoTicketsBought" or "BountiesSet")||m.Required is <0 or >1000000||m.Requirement!=""&&m.Required<1)throw new InvalidDataException("Ungültiger Preis oder Freischaltung.");
   if(m is null||m.Id!=ModelPolicy.Id(m.Path)||!ids.Add(m.Id)||!paths.Add(m.Path)||m.Name.Length is <1 or >64||m.Name.Any(c=>char.IsControl(c)||c is '<' or '>')||m.Category.Length is <1 or >32||!ModelPolicy.Workshops.Contains(m.Workshop)||!(m.Path.StartsWith("characters/",StringComparison.Ordinal)||m.Path.StartsWith("agents/models/",StringComparison.Ordinal))||!m.Path.EndsWith(".vmdl",StringComparison.Ordinal)||m.Path.Length>240||m.Path.Contains("..")||m.Path.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '/' or '_' or '.' or '-')))throw new InvalidDataException("Ungültiger oder doppelter Modellpfad.");
  }
 }
}
public static class ModelPolicy
{
 public static bool Vehicle(string path)=>System.Text.RegularExpressions.Regex.IsMatch(path,@"(?:^|[/_])(?:t34|lada\d*|bombardino|tank|car|truck|vehicle|helicopter|airplane|aircraft|motorcycle|tractor|bus|train|ferrari|lamborghini|kart|tesla_cybertruck|bmw_m5|mercedes_csl|porsche_911|matiz|land_cruiser)(?:[_/\.]|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
 public static bool HalloweenModel(ModelEntry m)=>new[]{"skeleton","zombie","scream","chucky","clown","freddy","vanny","huggy","dark_teletub","purgatory","mephisto","infernal","trollface"}.Any(x=>m.Path.Contains(x,StringComparison.OrdinalIgnoreCase));
 public static readonly string[] Workshops=["3803459186"];
 public static string Id(string path)=>"model-"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..24];
 // UTC day is fixed for an entire map. New selection never removes an active
 // player's model in the middle of a life; entitlement is checked at next spawn.
 public static string Day(DateTimeOffset now)=>now.UtcDateTime.ToString("yyyy-MM-dd");
 public static bool ForTeam(ModelEntry m,int team)=>team is 2 or 3&&m.Team==team;
 public static string SelectionKey(string steam,int team)=>steam+":"+team;
 public static Dictionary<string,string> MigrateSelections(IReadOnlyDictionary<string,string> prior,IEnumerable<ModelEntry> models)
 {
  var result=prior.ToDictionary(x=>x.Key,x=>x.Value);var catalog=models.ToDictionary(m=>m.Id);
  foreach(var entry in prior.Where(e=>e.Key.Length==17&&e.Key.All(char.IsAsciiDigit)))if(catalog.TryGetValue(entry.Value,out var m)){
   result.TryAdd(SelectionKey(entry.Key,m.Team),entry.Value);result.Remove(entry.Key);
  }return result;
 }
 public static HashSet<string> FreeTeams(IEnumerable<ModelEntry> entries,string day,int count,bool halloween)
 {
  var models=entries.ToArray();var result=Free(models.Where(m=>m.Team==2),day,(count+1)/2,halloween);
  result.UnionWith(Free(models.Where(m=>m.Team==3),day,count/2,halloween));return result;
 }
 public static HashSet<string> Free(IEnumerable<ModelEntry> entries,string day,int count,bool halloween=false)=>entries.Where(m=>m.Enabled&&m.Requirement==""&&m.DailyPrice<=100000)
  .OrderBy(m=>halloween&&!HalloweenModel(m)?1:0)
  .ThenBy(m=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(day+":"+m.Id))),StringComparer.Ordinal).ThenBy(m=>m.Id,StringComparer.Ordinal).Take(count).Select(m=>m.Id).ToHashSet(StringComparer.Ordinal);
}
