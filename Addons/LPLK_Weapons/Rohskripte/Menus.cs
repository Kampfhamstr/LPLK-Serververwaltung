using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Microsoft.Extensions.Logging;

namespace LPLK_Weapons;

public sealed partial class Plugin
{
    private bool PlayerAllowed(IPlayer p){RefreshSettings();return module.Active&&p.IsValid&&p.IsAuthorized&&!p.IsFakeClient&&settings.Enabled&&settings.MenuEnabled&&
        (settings.MenuTeam==0||p.Controller is {IsValid:true}&&p.Controller.TeamNum==settings.MenuTeam)&&
        (settings.MenuPermission.Length==0||Core.Permission.PlayerHasPermission(p.SteamID,settings.MenuPermission));}
    private void AdminPage(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null,int page=0)=>
        CompactMenu.Open(Core,p,title,()=>module.Active,a=>{RefreshSettings();return module.CanAdmin(a);},a=>rows(a).Select(row=>(row.Item1,(Action<IPlayer>)(actor=>
        {
            var before=settings;try{row.Item2(actor);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException){actor.SendLplkChat("[LPLK] Änderung nicht übernommen: "+e.Message);return;}
            if(!ReferenceEquals(before,settings))Core.Logger.LogInformation("LPLK_Weapons: Admin {Actor}; vorher={Before}; nachher={After}",actor.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
        }))).ToArray(),back,page);
    private void OpenAdmin(IPlayer p) => AdminPage(p,"LPLK · Waffen & Auswahl",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Waffen",RestrictionsRows(actor)),("Auswahl",SelectionSettingsRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
("Sonderregeln",SpecialRules),        ("Status",a=>a.SendLplkChat($"[LPLK] Preset {settings.ActivePreset}; {settings.AllowedWeapons.Length} Freigaben; {settings.Rules.Count} Sonderregeln; Fehler {errors}. Mapquoten und Rechte: settings.json / Rules."))
    }))(p);
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Modul aktiv: {settings.Enabled}",a=>{Save(current=>current with{Enabled=!current.Enabled});SettingsMenu(a);})    }))(p);
    private void Restrictions(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RestrictionsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Beschränkung: {settings.RestrictionEnabled}",a=>{Save(current=>current with{RestrictionEnabled=!current.RestrictionEnabled});Restrictions(a);}),        ("Preset",Presets),("Einzelne Waffen",a=>AdminGroups(a,false)),        ("Weitere Regeln",a=>AdminPage(a,"Waffen · Weitere Regeln",_=>new (string,Action<IPlayer>)[]
        {
            ($"Unbekannte sperren: {settings.RestrictUnknownWeapons}",b=>{Save(current=>current with{RestrictUnknownWeapons=!current.RestrictUnknownWeapons});Restrictions(b);}),
            ($"Sperrhinweise: {settings.ShowRestrictionAlert}",b=>{Save(current=>current with{ShowRestrictionAlert=!current.ShowRestrictionAlert});Restrictions(b);})
        },Restrictions))
    }))(p);
    private void Presets(IPlayer p)=>AdminPage(p,"Waffen · Presets",_=>new[]{("AllWeapons","Alle Waffen"),("PistolsOnly","Nur Pistolen"),("DeagleOnly","Nur Deagle"),("RiflesOnly","Nur Gewehre"),("SnipersOnly","Nur Scharfschützen"),("AwpOnly","Nur AWP"),("NoSnipers","Keine Scharfschützen")}
        .Select(x=>(x.Item2,(Action<IPlayer>)(a=>{Save(current=>current with{AllowedWeapons=WeaponCatalog.Preset(x.Item1),ActivePreset=x.Item1});a.SendLplkChat("[LPLK] Preset gespeichert; Beschränkung bleibt "+(settings.RestrictionEnabled?"AN":"AUS")+".");Restrictions(a);}))).ToArray(),Restrictions);
    private void SelectionSettings(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SelectionSettingsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Kostenlose Auswahl: {settings.MenuEnabled}",a=>{Save(current=>current with{MenuEnabled=!current.MenuEnabled});SelectionSettings(a);}),        ("Angebotene Waffen",a=>AdminGroups(a,true)),        ("Berechtigung",a=>a.SendLplkChat("[LPLK] Menürecht: "+(settings.MenuPermission.Length==0?"alle Spieler":settings.MenuPermission)+". Lebender Spieler in T/CT erforderlich.")),        ("Ausprobieren",a=>Guns(a,null))
    }))(p);
    private void AdminGroups(IPlayer p,bool blacklist)=>AdminPage(p,blacklist?"Auswahl · Kategorien":"Freigaben · Kategorien",_=>WeaponCatalog.Groups.Select(group=>(group,(Action<IPlayer>)(a=>AdminWeapons(a,group,blacklist)))).ToArray(),blacklist?SelectionSettings:Restrictions);
    private void AdminWeapons(IPlayer p,string group,bool blacklist)=>AdminPage(p,group,_=>WeaponCatalog.All.Where(w=>w.Group==group).Select(w=>
    {
        bool on=blacklist?!settings.MenuBlacklist.Contains(w.Id):settings.AllowedWeapons.Contains(w.Id);
        return ($"{w.Name}: {on}",(Action<IPlayer>)(a=>
        {
            Save(current=>{var list=(blacklist?current.MenuBlacklist:current.AllowedWeapons).ToHashSet(StringComparer.Ordinal);if(!list.Remove(w.Id))list.Add(w.Id);return blacklist?current with{MenuBlacklist=list.Order().ToArray()}:current with{AllowedWeapons=list.Order().ToArray(),ActivePreset="Custom"};});
            AdminWeapons(a,group,blacklist);
        }));
    }).ToArray(),a=>AdminGroups(a,blacklist));
    [Command("guns")]
    public void GunsCommand(ICommandContext c){if(c.Sender is {} p)Guns(p,null);else c.ReplyLplk("Waffenauswahl nur im Spiel.");}
    [Command("primary")]
    public void PrimaryCommand(ICommandContext c){if(c.Sender is {} p)Guns(p,false);}
    [Command("secondary")]
    public void SecondaryCommand(ICommandContext c){if(c.Sender is {} p)Guns(p,true);}
    private void Guns(IPlayer p,bool? secondary)
    {
        if(!PlayerAllowed(p)){p.SendLplkChat("[LPLK] Waffenauswahl ausgeschaltet oder keine Berechtigung.");return;}
        if(secondary==true){PlayerWeapons(p,"Pistolen",true);return;}
        CompactMenu.Open(Core,p,"LPLK · Waffenauswahl",()=>module.Active,PlayerAllowed,_=>WeaponCatalog.Groups.Where(g=>secondary!=false||g!="Pistolen").Select(g=>(g,(Action<IPlayer>)(a=>PlayerWeapons(a,g,secondary)))).ToArray(),BackToMenu);
    }
    private void BackToMenu(IPlayer p){if(Core.Command.IsCommandRegistered("sw_menu"))p.ExecuteCommand("sw_menu");}
    private void PlayerWeapons(IPlayer p,string group,bool? secondary)
    {
        if(!lives.TryGetValue(p.PlayerID,out long life))lives[p.PlayerID]=life=++generation;
        CompactMenu.Open(Core,p,"Waffen · "+group,()=>module.Active,PlayerAllowed,_=>WeaponCatalog.All.Where(w=>w.Group==group&&WeaponPolicy.MenuAllows(settings,w.Id))
            .Select(w=>(w.Name,(Action<IPlayer>)(a=>
            {
                if(life!=lives.GetValueOrDefault(a.PlayerID,-1)){a.SendLplkChat("[LPLK] Neue Runde oder neues Leben: Waffenauswahl bitte erneut öffnen.");return;}
                Give(a,w);
            }))).ToArray(),a=>{if(secondary==true)BackToMenu(a);else Guns(a,secondary);});
    }
    private void Give(IPlayer p,WeaponEntry entry)
    {
        // Bind already validates the originating session and current menu; recheck
        // rights, life, team, configuration and quota immediately before mutation.
        if(!PlayerAllowed(p)||!WeaponPolicy.MenuAllows(settings,entry.Id)||!p.IsAlive||!p.Controller.IsValid||p.Controller.TeamNum is not (2 or 3)||p.PlayerPawn is not {IsValid:true} pawn||pawn.WeaponServices is not {} services||pawn.ItemServices is not {} items)
        {p.SendLplkChat("[LPLK] Auswahl momentan nicht möglich; lebender Spieler in T/CT erforderlich.");return;}
        var old=services.MyWeapons.Select(h=>h.Value).Where(w=>w is {IsValid:true}&&WeaponCatalog.Find(WeaponName(w))?.Secondary==entry.Secondary).ToArray();
        var existing=old.FirstOrDefault(w=>WeaponName(w!)==entry.Id);
        if(existing is not null){services.SelectWeapon(existing);p.SendLplkChat("[LPLK] Waffe bereits vorhanden.");return;}
        if(Denial(p,entry.Id,false) is {} reason){p.SendLplkChat("[LPLK] "+reason);return;}
        try
        {
            // Never delete the old inventory before successful, verified delivery.
            var weapon=items.GiveItem<CBasePlayerWeapon>(entry.Id);
            if(weapon is not {IsValid:true}||!services.MyWeapons.Any(h=>h.Value is {IsValid:true} w&&w.Index==weapon.Index))
            {p.SendLplkChat("[LPLK] Ausgabe vom Spiel abgelehnt; bisherige Waffen bleiben erhalten.");return;}
            foreach(var w in old)if(w is {IsValid:true}&&w.Index!=weapon.Index&&services.MyWeapons.Any(h=>h.Value is {IsValid:true} owned&&owned.Index==w.Index))services.RemoveWeapon(w);
            services.SelectWeapon(weapon);given++;p.SendLplkChat("[LPLK] Ausgewählt: "+entry.Name);
        }
        catch(Exception e){Error(e);p.SendLplkChat("[LPLK] Waffenausgabe fehlgeschlagen; siehe Serverprotokoll.");}
    }
}
