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
    private bool CanOpenAdmin(IPlayer p) => Connected(p) && (CanManage(p) || Modules(p).Length > 0);

    private void Menu(IPlayer p, string caption, Func<IPlayer, IEnumerable<(string Label, Action<IPlayer> Action)>> entries,
        Action<IPlayer>? back, Func<IPlayer, bool> allowed, int page = 0, Action<IPlayer, int>? changePage = null, bool scroll = false)
    {
        if (!allowed(p)) { p.SendChat("[LPLK] Dieser Bereich ist nicht mehr freigegeben."); return; }
        var available = entries(p).ToArray();
        var slice = scroll ? new MenuPage<(string Label, Action<IPlayer> Action)>(available, 0, 1, 0) : MenuLayout.Page(available, page, back is not null);
        string title = caption + (slice.Count > 1 ? $" · {slice.Index + 1}/{slice.Count}" : "");
        var builder = Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false)
            .Design.SetMenuTitle(MenuColors.Heading(MenuLayout.Label(title))).Design.SetMaxVisibleItems(5);
        int serial = generation;
        var numbers = new MenuNumbers();
        void Add(string label, Action<IPlayer> action, bool navigation = false)
        {
            var option = new ButtonMenuOption(navigation ? MenuColors.Back(MenuLayout.Label(label)) : numbers.Next(MenuLayout.Label(label))) { CloseAfterClick = false };
            GameThreadMenu.Bind(Core, option, p, () => active && serial == generation, a =>
            {
                if (allowed(a)) Guard(() => action(a), a.SendChat);
                else a.SendChat("[LPLK] Berechtigung inzwischen entzogen.");
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
        Core.MenusAPI.OpenMenuForPlayer(p, builder.Build());
    }

    [Command("menu")]
    public void PlayerMenuCommand(ICommandContext context)
    {
        if (context.Sender is { } player && Connected(player)) OpenPlayerMenu(player);
        else context.Reply("LPLK_Admin 0.5.0; Spielermenü: !menu; Administration: !admin / !adminmenu.");
    }

    private bool Loaded(string id) => active && Core.PluginManager.GetAllPlugins().Contains(id) && Core.PluginManager.GetPluginStatus(id)==SwiftlyS2.Shared.Plugins.PluginStatus.Loaded;
    private bool Available(string id, string command) => Loaded(id) && Core.Command.IsCommandRegistered(command);
    private bool PlayerFeatureAllowed(IPlayer p,string id,string command)=>Available(id,command)&&interfaces is not null&&
        interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player."+id[5..]+".v1",out var api)&&
        api.TryGetValue("allowed",out var allowed)&&allowed is Func<IPlayer,string,bool> check&&check(p,command);
    private void PlayerCommand(IPlayer p, string id, string command)
    {
        if (!Connected(p) || !PlayerFeatureAllowed(p,id,command)) { p.SendChat("[LPLK] Funktion derzeit nicht verfügbar."); return; }
        // Execute as this player; never promote a public menu action to server-console authority.
        p.ExecuteCommand(command);
    }
    private void OpenPlayerMenu(IPlayer p) => Menu(p, "LPLK · Spielermenü", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (MapEntries(a).Count>0) entries.Add(("Maps", PlayerMaps));
        if (PlayerFeatureAllowed(a,"LPLK_Sounds","sound")) entries.Add(("Sounds",b=>PlayerCommand(b,"LPLK_Sounds","sound")));
        if (PlayerWeaponsAllowed(a)) entries.Add(("Waffen",b=>PlayerCommand(b,"LPLK_Weapons","sw_guns")));
        if (PlayerCameraAllowed(a)) entries.Add(("Kamera",b=>PlayerCommand(b,"LPLK_ThirdPerson","sw_tp")));
        if (EconomyEntries(a).Count>0) entries.Add(("Wirtschaft",PlayerEconomy));
        entries.Add(("Info", PlayerInformation));
        if (CanOpenAdmin(a)) entries.Add(("Adminmenü", OpenAdministration));
        return entries;
    }, null, Connected, scroll: true);

    private IReadOnlyList<(string,Action<IPlayer>)> EconomyEntries(IPlayer p)=>new (string Id,string Label,string Command)[]
    {
        ("LPLK_Economy","Konto","sw_bank"),("LPLK_Economy","Wetten","sw_bet"),("LPLK_Economy","Lotterie","sw_lotto"),("LPLK_Economy","Bankraub","sw_bankraub"),("LPLK_Economy","Titel","sw_titel"),("LPLK_Economy","Kopfgeld","sw_kopfgeld")
    }.Where(x=>PlayerFeatureAllowed(p,x.Id,x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,x.Id,x.Command)))).ToArray();
    private void PlayerEconomy(IPlayer p)=>Menu(p,"LPLK · Bank & Wirtschaft",EconomyEntries,OpenPlayerMenu,Connected);
    private bool PlayerWeaponsAllowed(IPlayer p) => PlayerFeatureAllowed(p,"LPLK_Weapons","sw_guns");
    private bool PlayerCameraAllowed(IPlayer p) => PlayerFeatureAllowed(p,"LPLK_ThirdPerson","sw_tp");
    private void PlayerExtras(IPlayer p) => Menu(p,"LPLK · Waffen, Kamera & Sounds",a=>
    {
        var entries=new List<(string,Action<IPlayer>)>();
        if(PlayerWeaponsAllowed(a))entries.Add(("Waffenauswahl",b=>{if(PlayerWeaponsAllowed(b))PlayerCommand(b,"LPLK_Weapons","sw_guns");else b.SendChat("[LPLK] Waffenauswahl derzeit nicht freigegeben.");}));
        if(SoundEntries(a).Count>0)entries.Add(("Sounds",PlayerSounds));
        if(PlayerCameraAllowed(a))entries.Add(("Kamera",b=>PlayerCommand(b,"LPLK_ThirdPerson","sw_tp")));
        return entries;
    },OpenPlayerMenu,Connected);

    private IReadOnlyList<(string,Action<IPlayer>)> MapEntries(IPlayer p)=>new (string Label,string Command)[]
    {
        ("Nominieren","sw_nominate"),("RTV","sw_rtv"),("Abstimmung","sw_mapchooser")
    }.Where(x=>PlayerFeatureAllowed(p,"LPLK_Maps",x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,"LPLK_Maps",x.Command)))).ToArray();
    private void PlayerMaps(IPlayer p) => Menu(p, "LPLK · Maps & Abstimmungen",MapEntries,OpenPlayerMenu,Connected);

    private IReadOnlyList<(string,Action<IPlayer>)> SoundEntries(IPlayer p)=>new (string Label,string Command)[]
    {
        ("Abspielen","sound"),("Senden","soundsend")
    }.Where(x=>PlayerFeatureAllowed(p,"LPLK_Sounds",x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,"LPLK_Sounds",x.Command)))).ToArray();
    private void PlayerSounds(IPlayer p) => Menu(p, "LPLK · Sounds",SoundEntries,PlayerExtras,Connected);

    private void PlayerInformation(IPlayer p)=>Menu(p,"LPLK · Statistik & Hilfe",a=>
    {
        var entries=new List<(string,Action<IPlayer>)>();
        if(PlayerFeatureAllowed(a,"LPLK_Statistics","sw_stats"))entries.Add(("Statistik",b=>PlayerCommand(b,"LPLK_Statistics","sw_stats")));
        if(PlayerFeatureAllowed(a,"LPLK_Demos","sw_demo"))entries.Add(("Demo sichern",b=>PlayerCommand(b,"LPLK_Demos","sw_demo")));
        entries.Add(("Hilfe",PlayerHelp));return entries;
    },OpenPlayerMenu,Connected);
    private void PlayerHelp(IPlayer p) => Menu(p, "LPLK · Hilfe & Bedienung", _ =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Menübedienung", a => a.SendChat("[LPLK] Navigation und Auswahl: Tasten in der Menü-Fußzeile. !menu öffnet den Einstieg erneut."))
        };
        if (Loaded("LPLK_Parachute")) entries.Add(("Fallschirm", a => a.SendChat("[LPLK] Fallschirm: beim Fallen E halten, sofern auf diesem Server aktiviert.")));
        if (Loaded("LPLK_BombWires")) entries.Add(("Bombendrähte", a => a.SendChat("[LPLK] Das Drahtmenü erscheint beim Legen/Entschärfen. Hier erfolgt die Auswahl mit deiner Sprungtaste (+jump).")));
        return entries;
    }, PlayerInformation, Connected);

    private bool HasPlayerActions(IPlayer p) => Core.PlayerManager.GetAllPlayers().Any(t => t.IsValid && !t.IsFakeClient && t.IsAuthorized && TargetModuleActions(p,t).Any()) || AdminActionPolicy.Permissions.Keys.Any(action => action is not ("unban" or "broadcast" or "round_restart" or "server_command" or "cvar_set") && Can(p, action));
    private void OpenAdministration(IPlayer p) => Menu(p, "LPLK · Adminmenü", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (HasPlayerActions(a)) entries.Add(("Spieler", b => PlayerList(b, 0)));
        if (ModuleAllowed(a, "LPLK_Maps")) entries.Add(("Maps", b => OpenModule(b, "LPLK_Maps", OpenAdministration)));
        if (ModuleAllowed(a, "LPLK_Sounds")) entries.Add(("Sounds", b => OpenModule(b, "LPLK_Sounds", OpenAdministration)));
        entries.Add(("Erweitert", b => AdvancedMenu(b, 0)));
        return entries;
    }, OpenPlayerMenu, CanOpenAdmin);

    // One flat, paged list for infrequent administration; no functional-category subtrees.
    private void AdvancedMenu(IPlayer p, int page) => Menu(p, "LPLK · Erweitert", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        Action<IPlayer> back = b => AdvancedMenu(b, page);
        if (Can(a, "round_restart")) entries.Add(("Rundenneustart", b => Confirm(b, "Rundenneustart in 5 Sekunden", null, "round_restart", null, new() { ["countdown_seconds"] = 5 }, back)));
        if (Can(a, "broadcast")) entries.Add(("Ansage", b => Input(b, "Servernachricht", (c, text) => Confirm(c, "Ansage: " + text, null, "broadcast", null, new() { ["message"] = text, ["type"] = "chat" }, back), back)));
        if (Can(a, "unban")) entries.Add(("Entbannen", b => Input(b, "SteamID64 entsperren", (c, id) =>
        {
            if (!AdminActionPolicy.SteamId(id)) throw new ArgumentException("Ungültige SteamID64.");
            Confirm(c, "Entbannen", null, "unban", id, [], back);
        }, back, 17)));
        entries.AddRange(Modules(a).Where(id => id is not ("LPLK_Maps" or "LPLK_Sounds"))
            .Select(id => (Id: id, Title: ((Func<string>)Api(id)!["title"])()))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x.Title, (Action<IPlayer>)(b => OpenModule(b, x.Id, back)))));
        entries.Add(("Rechte", b => RightsMenu(b, back)));
        return entries;
    }, OpenAdministration, CanOpenAdmin, page, AdvancedMenu);
    private void OpenModule(IPlayer p, string id, Action<IPlayer> back)
    {
        var api = Api(id);
        if (api is null || !ModuleAllowed(p, id)) { p.SendChat("[LPLK] Modul nicht verfügbar oder Berechtigung entzogen."); return; }
        var previous = Core.MenusAPI.GetCurrentMenu(p);
        ((Action<IPlayer>)api["open"])(p);
        var opened = Core.MenusAPI.GetCurrentMenu(p);
        if (opened is null || ReferenceEquals(previous, opened)) return;
        var option = new ButtonMenuOption(MenuColors.Back("Adminmenü")) { CloseAfterClick = false };
        GameThreadMenu.Bind(Core, option, p, () => active, a => { if (CanOpenAdmin(a)) back(a); });
        opened.AddOption(option);
    }

    private void RightsMenu(IPlayer p, Action<IPlayer> back) => Menu(p, "LPLK · Rollen & Rechte", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Anzeigen", b => b.SendChat("[LPLK] " + string.Join(", ", Core.Permission.GetPlayerPermissions(b.SteamID)))),
            ("Webpanel", b => b.SendChat("[LPLK] Rollenquelle: https://verwaltung.example.invalid – Spieler/Rollen, danach für diesen Server synchronisieren."))
        };
        if (Has(a, "admin.root")) entries.Add(("Neuladen", b => { if (Has(b, "admin.root")) { LoadSettings(); b.SendChat("[LPLK] Konfiguration geladen."); } }));
        return entries;
    }, back, CanOpenAdmin);
}


