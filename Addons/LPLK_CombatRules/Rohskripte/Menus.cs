using LPLK.Common;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;

namespace LPLK_CombatRules;

public sealed partial class Plugin
{
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,a=>{RefreshSettings();return module.CanAdmin(a);},a=>rows(a).Select(row=>(row.Item1,(Action<IPlayer>)(actor=>
    {
        var before=settings;
        try{row.Item2(actor);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {actor.SendLplkChat("[LPLK] Änderung nicht übernommen: "+e.Message);return;}
        if(!ReferenceEquals(before,settings))Core.Logger.LogInformation("LPLK_CombatRules: Admin {Actor}; vorher={Before}; nachher={After}",actor.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
    }))).ToArray(),back);

    private void OpenAdmin(IPlayer p)=>AdminHome(p,null);
    private void AdminHome(IPlayer p,string? section)=>MenuRows.Home(p,"LPLK · Kampfregeln",(a,t,r,b)=>Page(a,t,r,b),OpenAdminRows,section,("Allgemein",SettingsMenuRows),("Kopfschuss",HeadshotMenuRows),("Goldene Regel",GoldenMenuRows),("Goldene Regel",GoldenConditionsRows),("Goldene Meldung",GoldenMessagesRows));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
("Sonderregeln",SpecialRules),        ("Status",a=>a.SendLplkChat($"[LPLK] Blockierte Treffer: Kopf {headshotsBlocked}, Goldene Regel {goldenBlocked}; {settings.Rules.Count} Schadensregeln (settings.json / Rules); Fehler {errors}."))
    }))(p);
    private void SettingsMenu(IPlayer p) => AdminHome(p,"Allgemein");
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Modul aktiv: {settings.Enabled}",a=>{Save(current=>current with{Enabled=!current.Enabled});SettingsMenu(a);})    }))(p);
    private void HeadshotMenu(IPlayer p) => AdminHome(p,"Kopfschuss");
    private IReadOnlyList<(string,Action<IPlayer>)> HeadshotMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Nur Kopftreffer: {settings.HeadshotOnly}",a=>{Save(current=>current with{HeadshotOnly=!current.HeadshotOnly});HeadshotMenu(a);}),        ($"Schusswaffen: {settings.FirearmsOnly}",a=>{Save(current=>current with{FirearmsOnly=!current.FirearmsOnly});HeadshotMenu(a);})
    }))(p);
    private void GoldenMenu(IPlayer p) => AdminHome(p,"Goldene Regel");
    private IReadOnlyList<(string,Action<IPlayer>)> GoldenMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Goldene Regel: {settings.GoldenRule}",a=>{Save(current=>current with{GoldenRule=!current.GoldenRule});GoldenMenu(a);}),        ("Waffen",GoldenWeapons)    }))(p);
    private void GoldenWeapons(IPlayer p)=>Page(p,"Goldene Regel · Waffen",_=>WeaponCatalog.All.Where(w=>w.Group=="Scharfschützen").Select(w=>
        ($"{w.Name}: {settings.GoldenWeapons.Contains(w.Id)}",(Action<IPlayer>)(a=>
        {
            Save(current=>{var list=current.GoldenWeapons.ToHashSet();if(!list.Remove(w.Id))list.Add(w.Id);return current with{GoldenWeapons=list.Order().ToArray()};});GoldenWeapons(a);
        }))).ToArray(),GoldenMenu);
    private void GoldenConditions(IPlayer p) => AdminHome(p,"Goldene Regel");
    private IReadOnlyList<(string,Action<IPlayer>)> GoldenConditionsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Vorsprung: {settings.MinimumLeadRounds} Runde(n)",a=>Page(a,"Goldene Regel · Vorsprung",_=>new[]{1,2,3,5}.Select(n=>($"Ab {n} Runde(n)",(Action<IPlayer>)(b=>{Save(current=>current with{MinimumLeadRounds=n});GoldenConditions(b);}))).ToArray(),GoldenConditions)),        ($"Im Warmup: {settings.EnabledInWarmup}",a=>{Save(current=>current with{EnabledInWarmup=!current.EnabledInWarmup});GoldenConditions(a);}),        ("Erklärung",a=>a.SendLplkChat("[LPLK] Nur das führende Team: Schaden mit Scope wird blockiert. Zoom bleibt erlaubt. Gleichstand, fehlender Spielstand und FFA lösen keine Sperre aus."))
    }))(p);
    private void GoldenMessages(IPlayer p) => AdminHome(p,"Goldene Meldung");
    private IReadOnlyList<(string,Action<IPlayer>)> GoldenMessagesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Rundenansage: {settings.AnnounceAtRoundStart}",a=>{Save(current=>current with{AnnounceAtRoundStart=!current.AnnounceAtRoundStart});GoldenMessages(a);}),        ($"Treffer-Hinweis: {settings.ShowBlockedShotAlert}",a=>{Save(current=>current with{ShowBlockedShotAlert=!current.ShowBlockedShotAlert});GoldenMessages(a);})
    }))(p);
}
