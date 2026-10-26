using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private bool Connected(IPlayer p) => active && p.IsValid && p.IsAuthorized && !p.IsFakeClient;
    private bool ModuleAllowed(IPlayer p, string id) => Connected(p) && Api(id) is { } api && ((Func<IPlayer, bool>)api["allowed"])(p);
    private string[] Modules(IPlayer p, string? group = null) => Core.PluginManager.GetAllPlugins()
        .Where(id => (group is null || MenuLayout.ModuleGroup(id) == group) && ModuleAllowed(p, id)).OrderBy(id => id).ToArray();
    private bool CanOpenAdmin(IPlayer p) => Connected(p) && (CanManage(p) || CanConfigure(p) || Modules(p).Length > 0);

    private void Menu(IPlayer p, string caption, Func<IPlayer, IEnumerable<(string Label, Action<IPlayer> Action)>> entries,
        Action<IPlayer>? back, Func<IPlayer, bool> allowed, int page = 0, Action<IPlayer, int>? changePage = null, bool scroll = true)
    {
        ReloadSettings();
        if (!allowed(p)) { p.SendLplkChat("[LPLK] Dieser Bereich ist nicht mehr freigegeben."); return; }
        var available = entries(p).ToArray();
        var slice = scroll ? new MenuPage<(string Label, Action<IPlayer> Action)>(available, 0, 1, 0) : MenuLayout.Page(available, page, back is not null);
        string title = caption + (slice.Count > 1 ? $" · {slice.Index + 1}/{slice.Count}" : "");
        var builder = Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false)
            .Design.SetMenuTitle(MenuColors.Heading(MenuLayout.Label(title))).Design.SetMaxVisibleItems(5)
            .Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
        int serial = generation;
        var numbers = new MenuNumbers();
        void Add(string label, Action<IPlayer> action, bool navigation = false)
        {
            var option = new ButtonMenuOption(navigation ? MenuColors.Back(MenuLayout.Label(label)) : numbers.Next(MenuLayout.Label(label))) { CloseAfterClick = false };
            GameThreadMenu.Bind(Core, option, p, () => active && serial == generation, a =>
            {
                ReloadSettings();if(serial!=generation){a.SendLplkChat("[LPLK] Einstellungen geändert; Menü neu öffnen.");return;}
                if (allowed(a)) Guard(() => action(a), a.SendLplkChat);
                else a.SendLplkChat("[LPLK] Berechtigung inzwischen entzogen.");
            });
            builder.AddOption(option);
        }
        foreach (var entry in slice.Items) Add(entry.Label, entry.Action);
        if (slice.Items.Length == 0) builder.AddOption(new TextMenuOption(MenuColors.Heading("Keine Einträge verfügbar.")));
        void Navigate(IPlayer a, int index)
        {
            if (changePage is not null) changePage(a, index);
            else Menu(a, caption, entries, back, allowed, index);
        }
        if (slice.Index > 0) Add("Vorherige Seite", a => Navigate(a, slice.Index - 1), true);
        if (slice.Index + 1 < slice.Count) Add("Nächste Seite", a => Navigate(a, slice.Index + 1), true);
        if (back is not null) Add("Zurück", back, true);
        OpenOwnedMenu(p, builder.Build());
    }

    [Command("menu")]
    [CommandAlias("menu",registerRaw:true)]
    public void PlayerMenuCommand(ICommandContext context)
    {
        if (context.Sender is { } player && Connected(player)) OpenPlayerMenu(player);
        else context.ReplyLplk("LPLK_Admin 0.11.5; Spielermenü: !menu; Administration: !admin / !adminmenu.");
    }

    private bool Loaded(string id) => active && Core.PluginManager.GetAllPlugins().Contains(id) && Core.PluginManager.GetPluginStatus(id)==SwiftlyS2.Shared.Plugins.PluginStatus.Loaded;
    private bool Available(string id, string command) => Loaded(id) && Core.Command.IsCommandRegistered(command);
    private bool PlayerFeatureAllowed(IPlayer p,string id,string command)=>Available(id,command)&&interfaces is not null&&
        interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player."+id[5..]+".v1",out var api)&&
        api.TryGetValue("allowed",out var allowed)&&allowed is Func<IPlayer,string,bool> check&&check(p,command);
    private void PlayerCommand(IPlayer p, string id, string command)
    {
        if (!Connected(p) || !PlayerFeatureAllowed(p,id,command)) { p.SendLplkChat("[LPLK] Funktion derzeit nicht verfügbar."); return; }
        // Execute as this player; never promote a public menu action to server-console authority.
        p.ExecuteCommand(command);
    }
    private void OpenPlayerMenu(IPlayer p) => Menu(p, "LPLK · Spielermenü", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (PublicMapsAvailable(a)) entries.Add(("Mapmenü", PlayerMaps));
        entries.AddRange(EconomyEntries(a));
        if(ModeApi(a,"Surf","main") is not null)entries.Add(("Surf / KZ",b=>OpenMode(b,"Surf","main")));
        if(ModeApi(a,"Zombie","main") is not null)entries.Add(("Zombie",b=>OpenMode(b,"Zombie","main")));
        if (PublicFeatureAllowed(a,"LPLK_Sounds","sound")) entries.Add(("Sounds",b=>PublicCommand(b,"LPLK_Sounds","sound")));
        if(PublicFeatureAllowed(a,"LPLK_Statistics","sw_stats"))entries.Add(("Statistik",b=>PublicCommand(b,"LPLK_Statistics","sw_stats")));
        if (PlayerWeaponsAllowed(a)) entries.Add(("Waffen",b=>PublicCommand(b,"LPLK_Weapons","sw_guns")));
        if (PlayerCameraAllowed(a)) entries.Add(("Kamera",b=>PublicCommand(b,"LPLK_ThirdPerson","sw_tp")));
        if(PublicFeatureAllowed(a,"LPLK_Demos","sw_demo"))entries.Add(("Demo sichern",b=>PublicCommand(b,"LPLK_Demos","sw_demo")));
        if(PublicFeatureAllowed(a,"LPLK_Economy","sw_titel"))entries.Add(("Titel",b=>PublicCommand(b,"LPLK_Economy","sw_titel")));
        entries.Add(("Hilfe", PlayerHelp));
        if (CanOpenAdmin(a)) entries.Add(("Adminmenü", OpenAdministration));
        return entries;
    }, null, Connected, scroll: true);

    private IReadOnlyList<(string,Action<IPlayer>)> EconomyEntries(IPlayer p)=>new (string Id,string Label,string Command)[]
    {
        ("LPLK_Economy","Kopfgeld","sw_kopfgeld"),("LPLK_Economy","Konto","sw_bank"),("LPLK_Economy","Wetten","sw_bet"),("LPLK_Economy","Bankraub","sw_bankraub"),("LPLK_Economy","Lotterie","sw_lotto")
    }.Where(x=>PublicFeatureAllowed(p,x.Id,x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PublicCommand(a,x.Id,x.Command)))).ToArray();
    private void PlayerEconomy(IPlayer p)=>OpenPlayerMenu(p);
    private bool PlayerWeaponsAllowed(IPlayer p) => PublicFeatureAllowed(p,"LPLK_Weapons","sw_guns");
    private bool PlayerCameraAllowed(IPlayer p) => PublicFeatureAllowed(p,"LPLK_ThirdPerson","sw_tp");
    private void PlayerExtras(IPlayer p)=>OpenPlayerMenu(p);

    private IReadOnlyList<(string,Action<IPlayer>)> MapEntries(IPlayer p)
    {
        if(!Loaded("LPLK_Maps"))return new (string Label,string Command)[]{("RTV","sw_rtv"),("Nominieren","sw_nominate"),("Abstimmung","sw_revote"),("Nächste Map","sw_nextmap")}.Where(x=>LegacyCommand(p,"LPLK_Maps",x.Command) is not null).Select(x=>(x.Label,(Action<IPlayer>)(a=>PublicCommand(a,"LPLK_Maps",x.Command)))).ToArray();
        var rows=new List<(string,Action<IPlayer>)>{("RTV",a=>PlayerMapCommand(a,"sw_rtv")),("Nominieren",a=>PlayerMapCommand(a,"sw_nominate"))};
        if(PublicFeatureAllowed(p,"LPLK_Maps","sw_mapchooser"))rows.Add(("Abstimmung",a=>PlayerMapCommand(a,"sw_mapchooser")));
        return rows;
    }
    private void PlayerMapCommand(IPlayer p,string command)
    {
        if(PublicFeatureAllowed(p,"LPLK_Maps",command)){PublicCommand(p,"LPLK_Maps",command);return;}
        string reason="Mapfunktion derzeit nicht verfügbar.";
        if(Loaded("LPLK_Maps")&&interfaces is not null&&interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player.Maps.v1",out var api)
            &&api.TryGetValue("unavailable",out var check)&&check is Func<IPlayer,string,string> why)reason=why(p,command);
        p.SendLplkChat("[LPLK] "+reason);
    }
    [Command("mapmenu")]
    [CommandAlias("mapmenu",registerRaw:true)]
    public void MapMenuCommand(ICommandContext context)
    {
        if(context.Sender is {} player&&Connected(player))PlayerMaps(player);
    }
    private void PlayerMaps(IPlayer p)
    {
        if(Loaded("LPLK_Surf")){OpenMode(p,"Surf","maps");return;}
        Menu(p, "Maps · RTV & Nominieren",MapEntries,OpenPlayerMenu,Connected);
    }

    private IReadOnlyList<(string,Action<IPlayer>)> SoundEntries(IPlayer p)=>new (string Label,string Command)[]
    {
        ("Abspielen","sound"),("Senden","soundsend")
    }.Where(x=>PlayerFeatureAllowed(p,"LPLK_Sounds",x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,"LPLK_Sounds",x.Command)))).ToArray();
    private void PlayerSounds(IPlayer p) => Menu(p, "LPLK · Sounds",SoundEntries,OpenPlayerMenu,Connected);

    private void PlayerInformation(IPlayer p)=>OpenPlayerMenu(p);
    private void PlayerHelp(IPlayer p) => Menu(p, "LPLK · Hilfe & Bedienung", _ =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Menübedienung", a => a.SendLplkChat("[LPLK] Navigation und Auswahl: Tasten in der Menü-Fußzeile. !menu öffnet den Einstieg erneut."))
        };
        if (Loaded("LPLK_Parachute")) entries.Add(("Fallschirm", a => a.SendLplkChat("[LPLK] Fallschirm: beim Fallen E halten, sofern auf diesem Server aktiviert.")));
        if (Loaded("LPLK_BombWires")) entries.Add(("Bombendrähte", a => a.SendLplkChat("[LPLK] Das Drahtmenü erscheint beim Legen/Entschärfen. Hier erfolgt die Auswahl mit deiner Sprungtaste (+jump).")));
        return entries;
    }, OpenPlayerMenu, Connected);

    private bool HasPlayerActions(IPlayer p) => Core.PlayerManager.GetAllPlayers().Any(t => t.IsValid && !t.IsFakeClient && t.IsAuthorized && TargetModuleActions(p,t).Any()) || AdminActionPolicy.Permissions.Keys.Any(action => action is not ("unban" or "broadcast" or "round_restart" or "server_command" or "cvar_set") && Can(p, action));
    private void OpenAdministration(IPlayer p) => Menu(p, "LPLK · Adminmenü", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (ServerEntries(a).Count > 0) entries.Add(("Serververwaltung", ServerManagement));
        if (PlayerManagementEntries(a).Count > 0) entries.Add(("Spielerverwaltung", PlayerManagement));
        if (ModuleAllowed(a, "LPLK_Maps")) entries.Add(("Maps", b => OpenModule(b, "LPLK_Maps", OpenAdministration)));
        else if(!Loaded("LPLK_Maps")&&LegacyMapAllowed(a)&&settings.LegacyGameMaps.Count>0)entries.Add(("Maps",LegacyAdminMaps));
        if (ModuleAllowed(a, "LPLK_Sounds")) entries.Add(("Sound", b => OpenModule(b, "LPLK_Sounds", OpenAdministration)));
        if (ModuleAllowed(a, "LPLK_Economy")) entries.Add(("Bank", b => OpenModule(b, "LPLK_Economy", OpenAdministration)));
        if (LotteryAdministration(a) is not null) entries.Add(("Lotto", OpenLotteryAdministration));
        return entries;
    }, OpenPlayerMenu, CanOpenAdmin, scroll: true);

    private IReadOnlyList<(string, Action<IPlayer>)> ServerEntries(IPlayer a)
    {
        var entries = new List<(string, Action<IPlayer>)>();
        Action<IPlayer> back = ServerManagement;
        if (Can(a, "round_restart")) entries.Add(("Rundenneustart", b => Confirm(b, "Rundenneustart in 5 Sekunden", null, "round_restart", null, new() { ["countdown_seconds"] = 5 }, back)));
        if (Can(a, "broadcast")) entries.Add(("Ansage", b => Input(b, "Servernachricht", (c, text) => Confirm(c, "Ansage: " + text, null, "broadcast", null, new() { ["message"] = text, ["type"] = "chat" }, back), back)));
        entries.AddRange(ModuleEntries(a, "rules", back));
        entries.AddRange(ModuleEntries(a, "effects", back));
        entries.AddRange(ModuleEntries(a, "server", back));
        entries.AddRange(ModuleEntries(a, "other", back));
        if (CanConfigure(a)) entries.Add(("Einstellungen", SettingsMenu));
        return entries;
    }
    private void ServerManagement(IPlayer p) => Menu(p, "Serververwaltung", ServerEntries, OpenAdministration, CanOpenAdmin, scroll: true);
    private IReadOnlyList<(string, Action<IPlayer>)> PlayerManagementEntries(IPlayer a)
    {
        var entries = new List<(string, Action<IPlayer>)>();
        Action<IPlayer> back = PlayerManagement;
        if (HasPlayerActions(a)) entries.Add(("Spieler", b => PlayerList(b, 0)));
        if (Can(a, "unban")) entries.Add(("Entbannen", b => Input(b, "SteamID64 entsperren", (c, id) =>
        {
            if (!AdminActionPolicy.SteamId(id)) throw new ArgumentException("Ungültige SteamID64.");
            Confirm(c, "Entbannen", null, "unban", id, [], back);
        }, back, 17)));
        entries.AddRange(ModuleEntries(a, "players", back));
        if (CanManage(a)) entries.Add(("Rechte", b => RightsMenu(b, back)));
        return entries;
    }
    private void PlayerManagement(IPlayer p) => Menu(p, "Spielerverwaltung", PlayerManagementEntries, OpenAdministration, CanOpenAdmin, scroll: true);
    private IEnumerable<(string, Action<IPlayer>)> ModuleEntries(IPlayer p, string group, Action<IPlayer> back) =>
        Modules(p, group).Select(id => (Id: id, Title: ((Func<string>)Api(id)!["title"])()))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x.Title, (Action<IPlayer>)(a => OpenModule(a, x.Id, back))));
    private Action<IPlayer>? LotteryAdministration(IPlayer p) =>
        ModuleAllowed(p, "LPLK_Economy") && interfaces is not null &&
        interfaces.TryGetSharedInterface<IReadOnlyDictionary<string, Delegate>>("LPLK.AdminSections.Economy.v1", out var sections) &&
        sections.TryGetValue("lotto", out var open) ? open as Action<IPlayer> : null;
    private void OpenLotteryAdministration(IPlayer p)
    {
        if (LotteryAdministration(p) is not { } open) { p.SendLplkChat("[LPLK] Lottoverwaltung nicht verfügbar oder Berechtigung entzogen."); return; }
        open(p);
    }
    private void OpenModule(IPlayer p, string id, Action<IPlayer> back)
    {
        var api = Api(id);
        if (api is null || !ModuleAllowed(p, id)) { p.SendLplkChat("[LPLK] Modul nicht verfügbar oder Berechtigung entzogen."); return; }
        var previous = Core.MenusAPI.GetCurrentMenu(p);
        ((Action<IPlayer>)api["open"])(p);
        var opened = Core.MenusAPI.GetCurrentMenu(p);
        if (opened is null || ReferenceEquals(previous, opened)) return;
        moduleReturns[p.PlayerID] = new(p.SteamID, p.SessionId.ToString(), id, opened.Configuration.Title, back);
        AddModuleReturn(p, opened, back);
        ApplyMenuVisuals(opened);
        RestoreMenuCursor(p,opened);
    }

    private void RightsMenu(IPlayer p, Action<IPlayer> back) => Menu(p, "LPLK · Rollen & Rechte", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Anzeigen", b => b.SendLplkChat("[LPLK] " + string.Join(", ", Core.Permission.GetPlayerPermissions(b.SteamID)))),
            ("Webpanel", b => b.SendLplkChat("[LPLK] Rollenquelle: https://verwaltung.lplk.de – Spieler/Rollen, danach für diesen Server synchronisieren."))
        };
        if (Has(a, "admin.root")) entries.Add(("Neuladen", b => { if (Has(b, "admin.root")) { LoadSettings(); b.SendLplkChat("[LPLK] Konfiguration geladen."); } }));
        return entries;
    }, back, CanOpenAdmin);
}


