using LPLK.Common;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;

namespace LPLK_CombatRules;

public sealed partial class Plugin
{
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,module.CanAdmin,a=>rows(a).Select(row=>(row.Item1,(Action<IPlayer>)(actor=>
    {
        var before=settings;row.Item2(actor);
        if(!ReferenceEquals(before,settings))Core.Logger.LogInformation("LPLK_CombatRules: Admin {Actor}; vorher={Before}; nachher={After}",actor.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
    }))).ToArray(),back);
    private void OpenAdmin(IPlayer p)=>Page(p,"LPLK · Kampfregeln",_=>new (string,Action<IPlayer>)[]
    {
        ($"Modul aktiv: {settings.Enabled}",a=>{Save(settings with{Enabled=!settings.Enabled});OpenAdmin(a);}),
        ("Kopftreffer-Regeln",HeadshotMenu),("Goldene Regel",GoldenMenu),
        ("Status / Schadensregeln",a=>a.SendChat($"[LPLK] Blockierte Treffer: Kopf {headshotsBlocked}, Goldene Regel {goldenBlocked}; {settings.Rules.Count} Schadensregeln (settings.json / Rules); Fehler {errors}."))
    });
    private void HeadshotMenu(IPlayer p)=>Page(p,"Kampf · Kopftreffer",_=>new (string,Action<IPlayer>)[]
    {
        ($"Nur Kopftreffer: {settings.HeadshotOnly}",a=>{Save(settings with{HeadshotOnly=!settings.HeadshotOnly});HeadshotMenu(a);}),
        ($"Nur Schusswaffen prüfen: {settings.FirearmsOnly}",a=>{Save(settings with{FirearmsOnly=!settings.FirearmsOnly});HeadshotMenu(a);})
    },OpenAdmin);
    private void GoldenMenu(IPlayer p)=>Page(p,"Kampf · Goldene Regel",_=>new (string,Action<IPlayer>)[]
    {
        ($"Goldene Regel: {settings.GoldenRule}",a=>{Save(settings with{GoldenRule=!settings.GoldenRule});GoldenMenu(a);}),
        ("Betroffene Waffen",GoldenWeapons),
        ("Vorsprung & Warmup",GoldenConditions),("Hinweise",GoldenMessages)
    },OpenAdmin);
    private void GoldenWeapons(IPlayer p)=>Page(p,"Goldene Regel · Waffen",_=>WeaponCatalog.All.Where(w=>w.Group=="Scharfschützen").Select(w=>
        ($"{w.Name}: {settings.GoldenWeapons.Contains(w.Id)}",(Action<IPlayer>)(a=>
        {
            var list=settings.GoldenWeapons.ToHashSet();if(!list.Remove(w.Id))list.Add(w.Id);
            Save(settings with{GoldenWeapons=list.Order().ToArray()});GoldenWeapons(a);
        }))).ToArray(),GoldenMenu);
    private void GoldenConditions(IPlayer p)=>Page(p,"Goldene Regel · Bedingungen",_=>new (string,Action<IPlayer>)[]
    {
        ($"Vorsprung: {settings.MinimumLeadRounds} Runde(n)",a=>Page(a,"Goldene Regel · Vorsprung",_=>new[]{1,2,3,5}.Select(n=>($"Ab {n} Runde(n)",(Action<IPlayer>)(b=>{Save(settings with{MinimumLeadRounds=n});GoldenConditions(b);}))).ToArray(),GoldenConditions)),
        ($"Im Warmup: {settings.EnabledInWarmup}",a=>{Save(settings with{EnabledInWarmup=!settings.EnabledInWarmup});GoldenConditions(a);}),
        ("Regel erklären",a=>a.SendChat("[LPLK] Nur das führende Team: Schaden mit Scope wird blockiert. Zoom bleibt erlaubt. Gleichstand, fehlender Spielstand und FFA lösen keine Sperre aus."))
    },GoldenMenu);
    private void GoldenMessages(IPlayer p)=>Page(p,"Goldene Regel · Hinweise",_=>new (string,Action<IPlayer>)[]
    {
        ($"Rundenansage: {settings.AnnounceAtRoundStart}",a=>{Save(settings with{AnnounceAtRoundStart=!settings.AnnounceAtRoundStart});GoldenMessages(a);}),
        ($"Treffer-Hinweis: {settings.ShowBlockedShotAlert}",a=>{Save(settings with{ShowBlockedShotAlert=!settings.ShowBlockedShotAlert});GoldenMessages(a);})
    },GoldenMenu);
}
