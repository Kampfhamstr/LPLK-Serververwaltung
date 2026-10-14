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
        Action<IPlayer>? back, Func<IPlayer, bool> allowed, int page = 0, Action<IPlayer, int>? changePage = null)
    {
        if (!allowed(p)) { p.SendChat("[LPLK] Dieser Bereich ist nicht mehr freigegeben."); return; }
        var slice = MenuLayout.Page(entries(p), page, back is not null);
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
        else context.Reply("LPLK_Admin 0.3.7; Spielermenü: !menu; Administration: !admin / !adminmenu.");
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
        if (MapEntries(a).Count>0) entries.Add(("Maps & Abstimmungen", PlayerMaps));
        if (SoundEntries(a).Count>0 || PlayerWeaponsAllowed(a) || PlayerCameraAllowed(a)) entries.Add(("Waffen, Kamera & Sounds", PlayerExtras));
        if (EconomyEntries(a).Count>0) entries.Add(("Bank & Wirtschaft",PlayerEconomy));
        entries.Add(("Statistik & Hilfe", PlayerInformation));
        if (CanOpenAdmin(a)) entries.Add(("Adminmenü", OpenAdministration));
        return entries;
    }, null, Connected);

    private IReadOnlyList<(string,Action<IPlayer>)> EconomyEntries(IPlayer p)=>new (string Id,string Label,string Command)[]
    {
        ("LPLK_Economy","Bankkonto & Überweisungen","sw_bank"),("LPLK_Economy","Wetten","sw_bet"),("LPLK_Economy","Lotterie","sw_lotto"),("LPLK_Economy","Bankraub","sw_bankraub"),("LPLK_Economy","Titel","sw_titel"),("LPLK_Economy","Kopfgeld","sw_kopfgeld")
    }.Where(x=>PlayerFeatureAllowed(p,x.Id,x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,x.Id,x.Command)))).ToArray();
    private void PlayerEconomy(IPlayer p)=>Menu(p,"LPLK · Bank & Wirtschaft",EconomyEntries,OpenPlayerMenu,Connected);
    private bool PlayerWeaponsAllowed(IPlayer p) => PlayerFeatureAllowed(p,"LPLK_Weapons","sw_guns");
    private bool PlayerCameraAllowed(IPlayer p) => PlayerFeatureAllowed(p,"LPLK_ThirdPerson","sw_tp");
    private void PlayerExtras(IPlayer p) => Menu(p,"LPLK · Waffen, Kamera & Sounds",a=>
    {
        var entries=new List<(string,Action<IPlayer>)>();
        if(PlayerWeaponsAllowed(a))entries.Add(("Waffenauswahl",b=>{if(PlayerWeaponsAllowed(b))PlayerCommand(b,"LPLK_Weapons","sw_guns");else b.SendChat("[LPLK] Waffenauswahl derzeit nicht freigegeben.");}));
        if(SoundEntries(a).Count>0)entries.Add(("Sounds",PlayerSounds));
        if(PlayerCameraAllowed(a))entries.Add(("ThirdPerson umschalten",b=>PlayerCommand(b,"LPLK_ThirdPerson","sw_tp")));
        return entries;
    },OpenPlayerMenu,Connected);

    private IReadOnlyList<(string,Action<IPlayer>)> MapEntries(IPlayer p)=>new (string Label,string Command)[]
    {
        ("Map nominieren","sw_nominate"),("RTV-Stimme abgeben","sw_rtv"),("Laufende Abstimmung öffnen","sw_mapchooser")
    }.Where(x=>PlayerFeatureAllowed(p,"LPLK_Maps",x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,"LPLK_Maps",x.Command)))).ToArray();
    private void PlayerMaps(IPlayer p) => Menu(p, "LPLK · Maps & Abstimmungen",MapEntries,OpenPlayerMenu,Connected);

    private IReadOnlyList<(string,Action<IPlayer>)> SoundEntries(IPlayer p)=>new (string Label,string Command)[]
    {
        ("Soundliste & Höreinstellungen","sound"),("Sound an Spieler senden","soundsend")
    }.Where(x=>PlayerFeatureAllowed(p,"LPLK_Sounds",x.Command)).Select(x=>(x.Label,(Action<IPlayer>)(a=>PlayerCommand(a,"LPLK_Sounds",x.Command)))).ToArray();
    private void PlayerSounds(IPlayer p) => Menu(p, "LPLK · Sounds",SoundEntries,PlayerExtras,Connected);

    private void PlayerInformation(IPlayer p)=>Menu(p,"LPLK · Statistik & Hilfe",a=>
    {
        var entries=new List<(string,Action<IPlayer>)>();
        if(PlayerFeatureAllowed(a,"LPLK_Statistics","sw_stats"))entries.Add(("Statistik & Ranglisten",b=>PlayerCommand(b,"LPLK_Statistics","sw_stats")));
        if(PlayerFeatureAllowed(a,"LPLK_Demos","sw_demo"))entries.Add(("Aktuelle Demo aufbewahren",b=>PlayerCommand(b,"LPLK_Demos","sw_demo")));
        entries.Add(("Hilfe & Bedienung",PlayerHelp));return entries;
    },OpenPlayerMenu,Connected);
    private void PlayerHelp(IPlayer p) => Menu(p, "LPLK · Hilfe & Bedienung", _ =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Menübedienung", a => a.SendChat("[LPLK] Navigation und Auswahl: Tasten in der Menü-Fußzeile. !menu öffnet den Einstieg erneut."))
        };
        if (Loaded("LPLK_Parachute")) entries.Add(("Fallschirm", a => a.SendChat("[LPLK] Fallschirm: beim Fallen E halten, sofern auf diesem Server aktiviert.")));
        if (Loaded("LPLK_BombWires")) entries.Add(("Bombendrähte", a => a.SendChat("[LPLK] Das Drahtmenü erscheint beim Legen/Entschärfen. Hier erfolgt die Auswahl mit Leertaste.")));
        return entries;
    }, PlayerInformation, Connected);

    private bool HasPlayerActions(IPlayer p) => AdminActionPolicy.Permissions.Keys.Any(action => action is not ("unban" or "broadcast" or "round_restart" or "server_command" or "cvar_set") && Can(p, action));
    private bool HasServerActions(IPlayer p) => Can(p, "round_restart") || Can(p, "broadcast") || Modules(p, "server").Length > 0;
    private bool HasGameplay(IPlayer p) => Modules(p).Any(id => MenuLayout.ModuleGroup(id) is "movement" or "combat" or "sounds" or "economy" or "other");
    private void OpenAdministration(IPlayer p) => Menu(p, "LPLK · Adminmenü", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (HasPlayerActions(a) || Can(a, "unban")) entries.Add(("Spieler verwalten", PlayerManagement));
        if (HasServerActions(a)) entries.Add(("Server & Maps", ServerMenu));
        if (HasGameplay(a)) entries.Add(("Spielregeln & Sounds", GameplayMenu));
        entries.Add(("Zugriff & Rechte", AccessMenu));
        return entries;
    }, OpenPlayerMenu, CanOpenAdmin);

    private void GameplayMenu(IPlayer p) => Menu(p, "LPLK · Spielregeln & Sounds", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        foreach (var (group, title) in new[] { ("movement", "Bewegung & Teams"), ("combat", "Kampf & Bomben"), ("sounds", "Sounds"), ("economy", "Bank & Wirtschaft"), ("other", "Weitere Module") })
            if (Modules(a, group).Length > 0) entries.Add((title, b => ModuleMenu(b, group, title, GameplayMenu)));
        return entries;
    }, OpenAdministration, CanOpenAdmin);

    private IEnumerable<(string, Action<IPlayer>)> ModuleEntries(IPlayer p, string group, Action<IPlayer> back) => Modules(p, group)
        .Select(id => (((Func<string>)Api(id)!["title"])(), (Action<IPlayer>)(a => OpenModule(a, id, back))));
    private void ModuleMenu(IPlayer p, string group, string title, Action<IPlayer> back) =>
        Menu(p, "LPLK · " + title, a => ModuleEntries(a, group, b => ModuleMenu(b, group, title, back)), back, CanOpenAdmin);
    private void OpenModule(IPlayer p, string id, Action<IPlayer> back)
    {
        var api = Api(id);
        if (api is null || !ModuleAllowed(p, id)) { p.SendChat("[LPLK] Modul nicht verfügbar oder Berechtigung entzogen."); return; }
        var previous = Core.MenusAPI.GetCurrentMenu(p);
        ((Action<IPlayer>)api["open"])(p);
        var opened = Core.MenusAPI.GetCurrentMenu(p);
        if (opened is null || ReferenceEquals(previous, opened)) return;
        var option = new ButtonMenuOption(MenuColors.Back("Zurück zum Adminmenü")) { CloseAfterClick = false };
        GameThreadMenu.Bind(Core, option, p, () => active, a => { if (CanOpenAdmin(a)) back(a); });
        opened.AddOption(option);
    }

    private void AccessMenu(IPlayer p) => Menu(p, "LPLK · Zugriff & Rechte", a =>
    {
        var entries = ModuleEntries(a, "access", AccessMenu).ToList();
        entries.Add(("Rollen & eigene Rechte", RightsMenu));
        return entries;
    }, OpenAdministration, CanOpenAdmin);
    private void RightsMenu(IPlayer p) => Menu(p, "LPLK · Rollen & Rechte", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>
        {
            ("Meine Rechte anzeigen", b => b.SendChat("[LPLK] " + string.Join(", ", Core.Permission.GetPlayerPermissions(b.SteamID)))),
            ("Rollen im Webpanel verwalten", b => b.SendChat("[LPLK] Rollenquelle: https://verwaltung.example.invalid – Spieler/Rollen, danach für diesen Server synchronisieren."))
        };
        if (Has(a, "admin.root")) entries.Add(("Admin-Konfiguration neu laden", b => { if (Has(b, "admin.root")) { LoadSettings(); b.SendChat("[LPLK] Konfiguration geladen."); } }));
        return entries;
    }, AccessMenu, CanOpenAdmin);
}


