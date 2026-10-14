using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LPLK.Common;

namespace LPLK_Statistics;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatisticsSettings
{
 public bool Enabled {get;init;}=true;
 public bool PvpEnabled {get;init;}=true;
 public bool BotTrainingEnabled {get;init;}=true;
 public bool OnlyBotEnemies {get;init;}=true;
 public bool CountPvpWarmup {get;init;}=true;
 public bool WeaponTracking {get;init;}=true;
 public bool CountWeaponWarmup {get;init;}
 public bool CountTeamKills {get;init;}=true; // Legacy HeadshotOnly and StatTrak both counted these.
 public bool InspectHud {get;init;}=true;
 public bool DefaultHud {get;init;}=true;
 public bool AnnounceGrenadeKill {get;init;}=true;
 public int InspectCooldownMs {get;init;}=500;
 public int HudDurationMs {get;init;}=2500;
 public int InfernoSeconds {get;init;}=30;
 public string UnknownInferno {get;init;}="molotov";
 public int TopCount {get;init;}=10;
 public int MinShots {get;init;}=250;
 public int MinKills {get;init;}=25;
 public int MinBotShots {get;init;}=250;
 public int MinBotKills {get;init;}=25;
 public double HeadshotWeight {get;init;}=.35;
 public double KillEfficiencyWeight {get;init;}=.30;
 public double HeadshotEfficiencyWeight {get;init;}=.20;
 public double VolumeWeight {get;init;}=.15;
 public double FullKillEfficiency {get;init;}=.20;
 public double FullHeadshotEfficiency {get;init;}=.15;
 public int FullHeadshots {get;init;}=1000;
 public bool DamageSummary {get;init;}=true;
 public bool DamageOnDeath {get;init;}=true;
 public bool DamageConsole {get;init;}=true;
 public bool DamageCenter {get;init;}
 public bool ShowFriendlyDamage {get;init;}
 public bool ShowOnlyKiller {get;init;}
 public bool ShowAllDamages {get;init;}
 public bool NoRoundsMode {get;init;}
 public static void Validate(StatisticsSettings s)
 {
  if(s.TopCount is <1 or >100 || s.MinShots<1||s.MinKills<1||s.MinBotShots<1||s.MinBotKills<1||s.FullHeadshots<1||
    s.InspectCooldownMs is <100 or >10000||s.HudDurationMs is <250 or >10000||s.InfernoSeconds is <1 or >120||s.UnknownInferno is not ("molotov" or "incgrenade" or "inferno"))throw new InvalidDataException("Ungültige Statistikgrenzen");
  double[] values=[s.HeadshotWeight,s.KillEfficiencyWeight,s.HeadshotEfficiencyWeight,s.VolumeWeight,s.FullKillEfficiency,s.FullHeadshotEfficiency];
  if(values.Any(x=>!double.IsFinite(x)||x<0)||s.FullKillEfficiency<=0||s.FullHeadshotEfficiency<=0||values.Take(4).Sum()<=0)throw new InvalidDataException("Ungültige Gewichtung");
 }
}
public sealed class Counters
{
 public long Shots {get;set;} public long Hits {get;set;} public long HeadHits {get;set;}
 public long Kills {get;set;} public long Headshots {get;set;} public long Deaths {get;set;}
 public void Validate(){if(new[]{Shots,Hits,HeadHits,Kills,Headshots,Deaths}.Any(x=>x<0||x>2_000_000_000_000)||Headshots>Kills)throw new InvalidDataException("Ungültige Zähler");}
}
public sealed class PlayerStats
{
 public string Name {get;set;}="Spieler";
 public DateTimeOffset LastSeen {get;set;}
 public Counters Pvp {get;set;}=new(); public Counters Bots {get;set;}=new();
 public Dictionary<string,long> Weapons {get;set;}=[];
 public bool? Hud {get;set;}
 public ExtraStats Extra {get;set;}=new();
}
public sealed class ExtraStats
{
 public DateTimeOffset? Since {get;set;}
 public long Assists {get;set;}public long FlashAssists {get;set;}public long Damage {get;set;}public long DamageTaken {get;set;}
 public long Rounds {get;set;}public long Wins {get;set;}public long PlaySeconds {get;set;}public long BombsPlanted {get;set;}public long BombsDefused {get;set;}
 public long NoScope {get;set;}public long Wallbang {get;set;}public long ThroughSmoke {get;set;}public long Blind {get;set;}public long InAir {get;set;}
 public long EnemyWeapon {get;set;}public long TeamWeapon {get;set;}public long UnknownWeaponOrigin {get;set;}public long BestPickupStreak {get;set;}
 public void Validate(){if(new[]{Assists,FlashAssists,Damage,DamageTaken,Rounds,Wins,PlaySeconds,BombsPlanted,BombsDefused,NoScope,Wallbang,ThroughSmoke,Blind,InAir,EnemyWeapon,TeamWeapon,UnknownWeaponOrigin,BestPickupStreak}.Any(x=>x<0||x>2_000_000_000_000)||Wins>Rounds||FlashAssists>Assists)throw new InvalidDataException("Ungültige erweiterte Statistik");}
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StatsData
{
 public int Schema {get;set;}=1;
 public Dictionary<ulong,PlayerStats> Players {get;set;}=[];
 public static StatsData Read(string path)
 {
  if(!File.Exists(path))return new();
  string raw=File.ReadAllText(path);using var json=JsonDocument.Parse(raw);
  if(!json.RootElement.TryGetProperty("Schema",out var schema)||schema.GetInt32()!=1||!json.RootElement.TryGetProperty("Players",out var players)||players.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Unvollständige Statistik");
  var data=JsonSerializer.Deserialize<StatsData>(raw)??throw new InvalidDataException("Leere Statistik");
  data.Validate();return data; // Corruption must never silently reset/overwrite rankings.
 }
 public void Validate()
 {
  if(Schema!=1||Players is null||Players.Count>100000)throw new InvalidDataException("Unbekannte Statistikversion");
  foreach(var (id,p) in Players){if(id==0||p is null||p.Name is null||p.Pvp is null||p.Bots is null||p.Weapons is null||p.Extra is null||p.Weapons.Count>300)throw new InvalidDataException("Ungültiger Datensatz");p.Pvp.Validate();p.Bots.Validate();p.Extra.Validate();if(p.Weapons.Any(k=>StatPolicy.Weapon(k.Key)!=k.Key||k.Value<0||k.Value>2_000_000_000_000))throw new InvalidDataException("Ungültige Waffenstatistik");}
 }
 public PlayerStats Touch(ulong id,string name){if(!Players.TryGetValue(id,out var p))Players[id]=p=new();p.Name=StatPolicy.Clean(name);p.LastSeen=DateTimeOffset.UtcNow;return p;}
}
public static class StatPolicy
{
 public static readonly string[] Grenades=["hegrenade","flashbang","smokegrenade","decoy","molotov","incgrenade","inferno"];
 public static string Clean(string value)=>new(value.Where(c=>!char.IsControl(c)).Take(48).ToArray());
 public static string Html(string value)=>WebUtility.HtmlEncode(Clean(value));
 public static string? Weapon(string? value)
 {
  string s=(value??"").Trim().ToLowerInvariant();if(s.StartsWith("weapon_"))s=s[7..];if(s.EndsWith("_projectile"))s=s[..^11];
  s=s switch{"incendiary" or "incendiarygrenade"=>"incgrenade","decoygrenade"=>"decoy","he"=>"hegrenade","planted_c4"=>"c4",_=>s};
  if(s.StartsWith("knife")||s=="bayonet")return "knife";
  return WeaponCatalog.Find("weapon_"+s)is not null||Grenades.Contains(s)||s is "taser" or "c4"?s:null;
 }
 public static double Ratio(long n,long d)=>d>0?(double)n/d:0;
 public static bool Eligible(Counters c,int shots,int kills)=>c.Shots>=shots&&c.Kills>=kills;
 public static double Rating(Counters c,StatisticsSettings s)
 {
  if(!Eligible(c,s.MinShots,s.MinKills))return 0;
  double score=Math.Clamp(Ratio(c.Headshots,c.Kills),0,1)*s.HeadshotWeight+Math.Clamp(Ratio(c.Kills,c.Shots)/s.FullKillEfficiency,0,1)*s.KillEfficiencyWeight+
   Math.Clamp(Ratio(c.Headshots,c.Shots)/s.FullHeadshotEfficiency,0,1)*s.HeadshotEfficiencyWeight+Math.Clamp(Math.Log10(1+(double)c.Headshots)/Math.Log10(1+(double)s.FullHeadshots),0,1)*s.VolumeWeight;
  return 100*score/(s.HeadshotWeight+s.KillEfficiencyWeight+s.HeadshotEfficiencyWeight+s.VolumeWeight);
 }
}
// Per connection/life, not slot: one hit/head-hit per firearm shot; grenade/old-life damage cannot inflate accuracy.
public sealed class ShotLedger
{
 private sealed record Shot(string Weapon,long At,bool Bot){public bool Hit;public bool Head;}
 private readonly Dictionary<string,Shot> shots=[];
 public void Fire(string session,string weapon,long now,bool bot)=>shots[session]=new(weapon,now,bot);
 public (bool Hit,bool Head) Hurt(string session,string weapon,long now,bool head,bool bot)
 {
  if(!shots.TryGetValue(session,out var s)||s.Weapon!=weapon||s.Bot!=bot||now<s.At||now-s.At>1000)return(false,false);
  bool hit=!s.Hit,hs=head&&!s.Head;s.Hit=true;s.Head|=head;return(hit,hs);
 }
 public void Remove(string session)=>shots.Remove(session);
 public void Clear()=>shots.Clear();
}
public sealed class FireLedger
{
 private readonly Dictionary<string,(string Type,long At)> thrown=[],active=[];
 public void Throw(string session,string type,long now){if(type is "molotov" or "incgrenade")thrown[session]=(type,now);}
 public void Detonate(string session,long now,int seconds,string fallback)=>active[session]=(thrown.TryGetValue(session,out var t)&&now>=t.At&&now-t.At<=seconds*1000?t.Type:fallback,now);
 public string Resolve(string session,long now,int seconds,string fallback)
 {
  if(active.TryGetValue(session,out var a)&&now>=a.At&&now-a.At<=seconds*1000)return a.Type;
  return thrown.TryGetValue(session,out var t)&&now>=t.At&&now-t.At<=seconds*1000?t.Type:fallback;
 }
 public void Remove(string session){thrown.Remove(session);active.Remove(session);}
 public void Clear(){thrown.Clear();active.Clear();}
}
