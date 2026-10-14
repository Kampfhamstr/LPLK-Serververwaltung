using LPLK.Common;
namespace LPLK_Statistics;
public static class WebExport
{
 public static object Player(ulong id,PlayerStats p,StatisticsSettings s,DateTimeOffset generated)=>new
 {
  schema=1,steamId=id.ToString(),profile="standard",generatedAt=generated,lastSeen=p.LastSeen,
  name=p.Name,pvp=new{p.Pvp.Shots,p.Pvp.Kills,p.Pvp.Headshots,p.Pvp.Deaths,kd=p.Pvp.Deaths>0?(double?)p.Pvp.Kills/p.Pvp.Deaths:null,headshotPercent=p.Pvp.Kills>0?(double?)100*p.Pvp.Headshots/p.Pvp.Kills:null,rating=StatPolicy.Eligible(p.Pvp,s.MinShots,s.MinKills)?(double?)StatPolicy.Rating(p.Pvp,s):null},
  bots=p.Bots,extra=p.Extra,weapons=p.Weapons,collection=new{s.Enabled,s.PvpEnabled,s.BotTrainingEnabled,s.WeaponTracking,s.MinShots,s.MinKills,s.MinBotShots,s.MinBotKills},
  definitions=new{specialKills="Feindliche menschliche Spieler außerhalb des Warmups; Merkmale können sich überschneiden",pickup="Beobachteter Besitzerwechsel derselben Waffeninstanz; unbekannte Herkunft bleibt unbekannt",rounds="Vom Rundenstart bis Rundenende verbunden und im selben Team",playTime="T/CT außerhalb des Warmups; einschließlich Bottraining",damage="Gemeldeter Gesundheitsschaden gegen menschliche Gegner; kein Rüstungsschaden"}
 };
}
