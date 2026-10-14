using System.Text.Json;
namespace LPLK_Statistics;
// Explicit offline conversion into a NEW empty profile only. Runtime never discovers or copies production data.
public static class LegacyImport
{
 public static StatsData Convert(string headshotJson,string nadeJson)
 {
  var result=new StatsData();using var head=JsonDocument.Parse(headshotJson);using var nade=JsonDocument.Parse(nadeJson);
  static long Count(JsonElement o,string key)=>o.TryGetProperty(key,out var v)?v.GetInt64():0;
  if(head.RootElement.TryGetProperty("Players",out var players))foreach(var entry in players.EnumerateObject())
  {
   ulong id=ulong.Parse(entry.Name);var old=entry.Value;var p=result.Touch(id,old.GetProperty("Name").GetString()??"Spieler");
   p.Pvp=new(){Shots=Count(old,"Shots"),Kills=Count(old,"Kills"),Headshots=Count(old,"HeadshotKills"),Deaths=Count(old,"Deaths")};
   p.Bots=new(){Shots=Count(old,"BotTrainingShots"),Hits=Count(old,"BotTrainingHits"),HeadHits=Count(old,"BotTrainingHeadHits"),Kills=Count(old,"BotTrainingKills"),Headshots=Count(old,"BotTrainingHeadshotKills")};
   if(old.TryGetProperty("LastSeenUtc",out var seen)&&seen.ValueKind==JsonValueKind.String&&DateTimeOffset.TryParse(seen.GetString(),out var at))p.LastSeen=at;
  }
  if(nade.RootElement.TryGetProperty("Players",out players))foreach(var entry in players.EnumerateObject())
  {
   ulong id=ulong.Parse(entry.Name);var old=entry.Value;if(!result.Players.TryGetValue(id,out var p))p=result.Touch(id,old.GetProperty("LastName").GetString()??"Spieler");
   if(old.TryGetProperty("HudEnabled",out var hud)&&hud.ValueKind is JsonValueKind.True or JsonValueKind.False)p.Hud=hud.GetBoolean();
   if(old.TryGetProperty("WeaponKills",out var weapons))foreach(var w in weapons.EnumerateObject())
   {string key=StatPolicy.Weapon(w.Name)??throw new InvalidDataException("Unbekannte Altwafe: "+w.Name);long count=w.Value.GetInt64();if(count<0)throw new InvalidDataException("Negativer Zähler");p.Weapons[key]=checked(p.Weapons.GetValueOrDefault(key)+count);}
   foreach(var (field,key) in new[]{("HeKills","hegrenade"),("FlashKills","flashbang"),("SmokeKills","smokegrenade"),("DecoyKills","decoy"),("MolotovKills","molotov"),("IncendiaryKills","incgrenade")})
   {long count=Count(old,field);if(count<0)throw new InvalidDataException("Negativer Zähler");if(p.Weapons.TryGetValue(key,out var mapped)&&mapped!=count)throw new InvalidDataException("Abweichende doppelte Granatenwerte: "+key);p.Weapons[key]=count;}
  }
  result.Validate();return result;
 }
}
