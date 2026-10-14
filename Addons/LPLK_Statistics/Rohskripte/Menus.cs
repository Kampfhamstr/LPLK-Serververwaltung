using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> entries,Action<IPlayer> back,Func<IPlayer,bool>? allowed=null)=>CompactMenu.Open(Core,p,"Statistik · "+title,()=>module.Active,allowed??Allowed,entries,back);
 private void Back(IPlayer p){if(Core.Command.IsCommandRegistered("sw_menu"))p.ExecuteCommand("sw_menu");}
 private void OpenStats(IPlayer p)=>Page(p,"Übersicht",_=>
 {
  var rows=new List<(string,Action<IPlayer>)>();if(settings.PvpEnabled)rows.Add(("PvP",Pvp));if(settings.BotTrainingEnabled)rows.Add(("Bottraining",Bots));if(settings.WeaponTracking)rows.Add(("Waffen",Weapons));return rows;
 },Back);
 private void Pvp(IPlayer p)=>Page(p,"PvP",_=>new (string,Action<IPlayer>)[]{("Meine Werte",a=>ShowCounters(a,false)),("Top Headshots",a=>Ranking(a,"Headshots",x=>x.Pvp.Headshots,x=>x.Pvp.Headshots>0,Pvp)),("Top Gesamtwertung",a=>Ranking(a,"Gesamtwertung",x=>StatPolicy.Rating(x.Pvp,settings),x=>StatPolicy.Eligible(x.Pvp,settings.MinShots,settings.MinKills),Pvp)),("Top Kill-Effizienz",a=>Ranking(a,"Kills pro Schuss",x=>100*StatPolicy.Ratio(x.Pvp.Kills,x.Pvp.Shots),x=>StatPolicy.Eligible(x.Pvp,settings.MinShots,settings.MinKills),Pvp))},OpenStats,a=>Allowed(a)&&settings.PvpEnabled);
 private void Bots(IPlayer p)=>Page(p,"Bottraining",_=>new (string,Action<IPlayer>)[]{("Meine Werte",a=>ShowCounters(a,true)),("Top Präzision",a=>Ranking(a,"Trainingspräzision",x=>100*StatPolicy.Ratio(x.Bots.Hits,x.Bots.Shots),x=>StatPolicy.Eligible(x.Bots,settings.MinBotShots,settings.MinBotKills),Bots)),("Wertung erklären",a=>a.SendChat($"[LPLK] Bottraining separat. Mindestwerte: {settings.MinBotShots} Schüsse / {settings.MinBotKills} Kills. Nur Botgegner: {settings.OnlyBotEnemies}. Pro Schuss höchstens ein Treffer und ein Kopftreffer."))},OpenStats,a=>Allowed(a)&&settings.BotTrainingEnabled);
 private void ShowCounters(IPlayer p,bool bots)
 {
  data.Players.TryGetValue(p.SteamID,out var row);var c=(bots?row?.Bots:row?.Pvp)??new();
  p.SendChat($"[LPLK] {(bots?"Training":"PvP")} · Schüsse {c.Shots}, Kills {c.Kills}, Headshots {c.Headshots}, Tode {c.Deaths}.");
  p.SendChat(bots?$"[LPLK] Treffer {c.Hits} ({100*StatPolicy.Ratio(c.Hits,c.Shots):0.0} %), Kopftreffer {c.HeadHits}.":$"[LPLK] HS-Quote {100*StatPolicy.Ratio(c.Headshots,c.Kills):0.0} %, Kills/Schuss {100*StatPolicy.Ratio(c.Kills,c.Shots):0.0} %, Gesamt {StatPolicy.Rating(c,settings):0.0}/100 (ab {settings.MinShots}/{settings.MinKills}).");
 }
 private void Ranking(IPlayer p,string title,Func<PlayerStats,double> score,Func<PlayerStats,bool> eligible,Action<IPlayer> back)=>Page(p,title,_=>data.Players.Where(x=>eligible(x.Value)).OrderByDescending(x=>score(x.Value)).ThenBy(x=>x.Key).Take(settings.TopCount).Select((x,i)=>($"#{i+1} {StatPolicy.Html(x.Value.Name)} · {score(x.Value):0.##}",(Action<IPlayer>)(a=>a.SendChat($"[LPLK] #{i+1} {x.Value.Name}: {score(x.Value):0.##}")))).ToArray(),back,a=>Allowed(a)&&(back==Pvp?settings.PvpEnabled:back==Bots?settings.BotTrainingEnabled:settings.WeaponTracking));
 private void Weapons(IPlayer p)=>Page(p,"Waffen",a=>new (string,Action<IPlayer>)[]{("Waffengruppen",WeaponGroups),("Granaten",b=>WeaponList(b,StatPolicy.Grenades,Weapons)),("Gesamtrangliste",b=>Ranking(b,"Waffenkills",x=>x.Weapons.Values.Sum(),x=>x.Weapons.Values.Sum()>0,Weapons)),($"StatTrak: {(data.Players.GetValueOrDefault(a.SteamID)?.Hud??settings.DefaultHud)}",b=>{var row=Get(b);row.Hud=!(row.Hud??settings.DefaultHud);Flush();Weapons(b);})},OpenStats,a=>Allowed(a)&&settings.WeaponTracking);
 private void WeaponGroups(IPlayer p)=>Page(p,"Waffengruppen",_=>WeaponCatalog.Groups.Append("Messer & Zeus").Select(g=>(g,(Action<IPlayer>)(a=>WeaponList(a,g=="Messer & Zeus"?["knife","taser"]:WeaponCatalog.All.Where(w=>w.Group==g).Select(w=>w.Id[7..]),WeaponGroups)))).ToArray(),Weapons,a=>Allowed(a)&&settings.WeaponTracking);
 private void WeaponList(IPlayer p,IEnumerable<string> weapons,Action<IPlayer> back)=>Page(p,"Einzelwerte",a=>weapons.Select(w=>($"{w}: {data.Players.GetValueOrDefault(a.SteamID)?.Weapons.GetValueOrDefault(w)??0}",(Action<IPlayer>)(b=>Ranking(b,w,x=>x.Weapons.GetValueOrDefault(w),x=>x.Weapons.GetValueOrDefault(w)>0,c=>WeaponList(c,weapons,back))))).ToArray(),back,a=>Allowed(a)&&settings.WeaponTracking);
 private void AdminPage(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> entries,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,"Statistik · "+title,()=>module.Active,module.CanAdmin,entries,back);
 private void OpenAdmin(IPlayer p)=>AdminPage(p,"Verwaltung",_=>new (string,Action<IPlayer>)[]{($"Modul aktiv: {settings.Enabled}",a=>{Save(settings with{Enabled=!settings.Enabled});OpenAdmin(a);}),("Zählung",AdminCounting),("Waffen",AdminWeapons),("Schadenübersicht",AdminDamage)});
 private void AdminCounting(IPlayer p)=>AdminPage(p,"Zählung",_=>new (string,Action<IPlayer>)[]{($"PvP: {settings.PvpEnabled}",a=>{Save(settings with{PvpEnabled=!settings.PvpEnabled});AdminCounting(a);}),($"Bottraining: {settings.BotTrainingEnabled}",a=>{Save(settings with{BotTrainingEnabled=!settings.BotTrainingEnabled});AdminCounting(a);}),($"Nur Bots: {settings.OnlyBotEnemies}",a=>{Save(settings with{OnlyBotEnemies=!settings.OnlyBotEnemies});AdminCounting(a);}),($"Teamkills zählen: {settings.CountTeamKills}",a=>{Save(settings with{CountTeamKills=!settings.CountTeamKills});AdminCounting(a);})},OpenAdmin);
 private void AdminWeapons(IPlayer p)=>AdminPage(p,"Waffen",_=>new (string,Action<IPlayer>)[]{($"Waffenstatistik: {settings.WeaponTracking}",a=>{Save(settings with{WeaponTracking=!settings.WeaponTracking});AdminWeapons(a);}),($"Inspect-HUD: {settings.InspectHud}",a=>{Save(settings with{InspectHud=!settings.InspectHud});AdminWeapons(a);}),($"Warmup-Kills: {settings.CountWeaponWarmup}",a=>{Save(settings with{CountWeaponWarmup=!settings.CountWeaponWarmup});AdminWeapons(a);}),($"Granatenkill-Hinweis: {settings.AnnounceGrenadeKill}",a=>{Save(settings with{AnnounceGrenadeKill=!settings.AnnounceGrenadeKill});AdminWeapons(a);})},OpenAdmin);
 private void AdminDamage(IPlayer p)=>AdminPage(p,"Schaden",_=>new (string,Action<IPlayer>)[]{($"Rundenübersicht: {settings.DamageSummary}",a=>{Save(settings with{DamageSummary=!settings.DamageSummary});AdminDamage(a);}),($"Todesübersicht: {settings.DamageOnDeath}",a=>{Save(settings with{DamageOnDeath=!settings.DamageOnDeath});AdminDamage(a);}),($"Live-Konsole: {settings.DamageConsole}",a=>{Save(settings with{DamageConsole=!settings.DamageConsole});AdminDamage(a);}),($"Live-HUD: {settings.DamageCenter}",a=>{Save(settings with{DamageCenter=!settings.DamageCenter});AdminDamage(a);})},OpenAdmin);
 [Command("stats")] public void Stats(ICommandContext c){if(c.Sender is {} p)OpenStats(p);else c.Reply("Statistik: !stats / !menu → Statistik & Demos.");}
 [Command("hs")] public void Hs(ICommandContext c){if(c.Sender is {} p)Pvp(p);}
 [Command("top")] public void Top(ICommandContext c){if(c.Sender is {} p)Pvp(p);}
 [Command("weaponmenu")] public void WeaponMenu(ICommandContext c){if(c.Sender is {} p)Weapons(p);}
 [Command("weaponkills")] public void WeaponKills(ICommandContext c)=>WeaponMenu(c);
 [Command("weaponstats")] public void WeaponStats(ICommandContext c)=>WeaponMenu(c);
 [Command("weapontop")] public void WeaponTop(ICommandContext c){if(c.Sender is {} p&&Allowed(p)&&settings.WeaponTracking)Ranking(p,"Waffenkills",x=>x.Weapons.Values.Sum(),x=>x.Weapons.Values.Sum()>0,Weapons);}
 [Command("nademenu")] public void NadeMenu(ICommandContext c){if(c.Sender is {} p)WeaponList(p,StatPolicy.Grenades,Weapons);}
 [Command("nadekills")] public void NadeKills(ICommandContext c)=>NadeMenu(c);
 [Command("nadestats")] public void NadeStats(ICommandContext c)=>NadeMenu(c);
 [Command("nadetop")] public void NadeTop(ICommandContext c){if(c.Sender is {} p&&Allowed(p)&&settings.WeaponTracking)Ranking(p,"Granatenkills",x=>StatPolicy.Grenades.Sum(w=>x.Weapons.GetValueOrDefault(w)),x=>StatPolicy.Grenades.Any(w=>x.Weapons.GetValueOrDefault(w)>0),Weapons);}
 [Command("stattrak")] public void StatTrak(ICommandContext c)=>WeaponMenu(c);
}
