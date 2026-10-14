using LPLK.Common;
using System.Text.Json;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;
using Microsoft.Extensions.Logging;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private int generation;
    private AdminSettings settings=new();
    private string SettingsPath=>Path.Combine(Core.PluginDataDirectory,"settings.json");
    private void LoadSettings()
    {
        var next=File.Exists(SettingsPath)?JsonSerializer.Deserialize<AdminSettings>(File.ReadAllText(SettingsPath))??throw new InvalidDataException("Admin-Konfiguration leer."):settings;
        if(next.Durations is null||next.Reasons is null||next.Durations.Length is <1 or >20||next.Durations.Any(n=>n<0||n>525600)||next.Reasons.Length is <1 or >30||next.Reasons.Any(r=>string.IsNullOrWhiteSpace(r)||r.Length>150))throw new InvalidDataException("Ungültige Admin-Gründe/Dauern.");
        if(!File.Exists(SettingsPath))AtomicJson.Write(SettingsPath,next);settings=next;
    }
    private bool Has(IPlayer p,string permission)=>active&&p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&Core.Permission.PlayerHasPermission(p.SteamID,permission);
    private bool Can(IPlayer p,string action)=>active&&p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&AdminActionPolicy.Allowed(action,s=>Has(p,s));
    private bool CanManage(IPlayer p)=>Has(p,"admin.menu")||AdminActionPolicy.Permissions.Keys.Any(a=>Can(p,a));
    private Func<ulong,string,string,string>? Backend()=>active&&interfaces is not null&&interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",out var api)&&api.TryGetValue("execute",out var call)?call as Func<ulong,string,string,string>:null;
    private IPlayer? Find(ulong id)=>Core.PlayerManager.GetAllPlayers().FirstOrDefault(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID==id);
    private void Run(IPlayer? actor,string action,string? target,Dictionary<string,object>? payload,Action<string> reply)
    {
        if(actor is not null&&!Can(actor,action))throw new InvalidOperationException("Keine Berechtigung.");
        var execute=Backend()??throw new InvalidOperationException("SerververwaltungBridge 1.6.2 ist nicht bereit.");
        using var result=JsonDocument.Parse(execute(actor?.SteamID??0,action,JsonSerializer.Serialize(new {target,payload=payload??[]})));
        bool success=result.RootElement.GetProperty("success").GetBoolean();string message=result.RootElement.GetProperty("message").GetString()??"Keine Rückmeldung.";
        if(!success)throw new InvalidOperationException(message);
        reply("[LPLK] "+message);
    }
    private void Guard(Action action,Action<string> reply)
    {
        try{action();}catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Admin: Aktion abgewiesen/fehlgeschlagen");reply("[LPLK] "+e.Message);}
    }
    private void Input(IPlayer p, string title, Action<IPlayer, string> accept, Action<IPlayer> back, int length = 150)
    {
        if (!CanManage(p)) return;
        int serial = generation;
        var input = new InputMenuOption(new MenuNumbers().Next(MenuLayout.Label(title)), maxLength: length,
            validator: v => !string.IsNullOrWhiteSpace(v) && v.Length <= length, hintMessage: "Text in den Chat eingeben.");
        GameThreadMenu.BindInput(Core, input, p, () => active && serial == generation, (a, value) =>
        { if (CanManage(a)) Guard(() => accept(a, AdminActionPolicy.Text(value, length)), a.SendChat); });
        var cancel = new ButtonMenuOption(MenuColors.Back("Zurück")) { CloseAfterClick = false };
        GameThreadMenu.Bind(Core, cancel, p, () => active && serial == generation, back);
        Core.MenusAPI.OpenMenuForPlayer(p, Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false)
            .Design.SetMenuTitle(MenuColors.Heading(MenuLayout.Label(title))).Design.SetMaxVisibleItems(5).AddOption(input).AddOption(cancel).Build());
    }

    private void PlayerManagement(IPlayer p) => Menu(p, "LPLK · Spieler verwalten", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (HasPlayerActions(a)) entries.Add(("Spieler auswählen", b => PlayerList(b, 0)));
        if (Can(a, "unban")) entries.Add(("Offline-Ban entfernen", b => Input(b, "SteamID64 entsperren", (c, id) =>
        {
            if (!AdminActionPolicy.SteamId(id)) throw new ArgumentException("Ungültige SteamID64.");
            Confirm(c, "Ban entfernen", null, "unban", id, [], PlayerManagement);
        }, PlayerManagement, 17)));
        return entries;
    }, OpenAdministration, CanManage);

    private void PlayerList(IPlayer p, int page) => Menu(p, "LPLK · Spieler auswählen", _ =>
        Core.PlayerManager.GetAllPlayers().Where(t => t.IsValid && !t.IsFakeClient && t.IsAuthorized)
            .OrderBy(t => t.Controller.PlayerName).Select(t =>
            {
                ulong id = t.SteamID; string session = t.SessionId.ToString();
                return (t.Controller.PlayerName, (Action<IPlayer>)(a => PlayerActions(a, id, session, page)));
            }), PlayerManagement, CanManage, page, PlayerList);

    private IPlayer Target(string id, string? session)
    {
        var t = Find(ulong.Parse(id));
        if (t is null || (session is not null && session != t.SessionId.ToString()))
            throw new InvalidOperationException("Spieler hat den Server verlassen oder sich neu verbunden. Bitte neu auswählen.");
        return t;
    }

    private void PlayerActions(IPlayer p, ulong steamId, string session, int page)
    {
        string id = steamId.ToString(); var target = Target(id, session);
        Action<IPlayer> back = a => PlayerActions(a, steamId, session, page);
        Menu(p, "LPLK · " + target.Controller.PlayerName, a =>
        {
            var entries = new List<(string, Action<IPlayer>)>();
            foreach (var (group, title, actions) in new[]
            {
                ("sanctions", "Kick & Ban", new[] { "kick", "ban" }),
                ("communication", "Chat & Nachrichten", new[] { "mute", "gag", "silence", "private_message" }),
                ("movement", "Team & Bewegung", new[] { "team", "freeze", "noclip", "goto_player", "bring_player" }),
                ("health", "Leben & Ausrüstung", new[] { "slay", "respawn", "health", "armor" })
            }) if (actions.Any(action => Can(a, action))) entries.Add((title, b => PlayerGroup(b, id, session, group, back)));
            return entries;
        }, a => PlayerList(a, page), CanManage);
    }

    private void PlayerGroup(IPlayer p, string id, string session, string group, Action<IPlayer> back)
    {
        var target = Target(id, session);
        Action<IPlayer> here = a => PlayerGroup(a, id, session, group, back);
        Menu(p, "LPLK · " + target.Controller.PlayerName + " · " + GroupTitle(group), a =>
        {
            Target(id, session);
            var entries = new List<(string, Action<IPlayer>)>();
            void Action(string label, string action, Dictionary<string, object>? payload = null)
            {
                if (!Can(a, action)) return;
                entries.Add((label, b =>
                {
                    if (!Can(b, action)) throw new InvalidOperationException("Keine Berechtigung.");
                    Target(id, session);
                    if (action is "kick" or "ban" or "mute" or "gag" or "silence") Reasons(b, label, session, action, id, here);
                    else Confirm(b, label, session, action, id, payload ?? [], here);
                }));
            }
            void Sub(string label, string child, params string[] actions)
            {
                if (actions.Any(action => Can(a, action))) entries.Add((label, b => PlayerGroup(b, id, session, child, here)));
            }
            switch (group)
            {
                case "sanctions": Action("Kicken", "kick"); Action("Bannen", "ban"); break;
                case "communication":
                    Sub("Sprachchat", "voice", "mute"); Sub("Textchat", "text", "gag"); Sub("Sprache & Text gemeinsam", "silence", "silence");
                    if (Can(a, "private_message")) entries.Add(("Private Nachricht", b => Input(b, "Private Nachricht", (c, text) =>
                        Confirm(c, "Nachricht: " + text, session, "private_message", id, new() { ["message"] = text, ["type"] = "chat" }, here), here)));
                    break;
                case "voice": Action("Sprachchat sperren", "mute"); Action("Sprachchat freigeben", "unmute"); break;
                case "text": Action("Textchat sperren", "gag"); Action("Textchat freigeben", "ungag"); break;
                case "silence": Action("Beides sperren", "silence"); Action("Beides freigeben", "unsilence"); break;
                case "movement":
                    Sub("Team wechseln", "team", "team"); Sub("Einfrieren / Freigeben", "freeze", "freeze");
                    Sub("Teleportieren", "teleport", "goto_player", "bring_player"); Sub("Noclip", "noclip", "noclip"); break;
                case "team":
                    Action("Terroristen", "team", new() { ["team"] = "t" }); Action("Counter-Terroristen", "team", new() { ["team"] = "ct" });
                    Action("Zuschauer", "team", new() { ["team"] = "spectator" }); break;
                case "freeze":
                    Action("Einfrieren", "freeze", new() { ["frozen"] = true, ["seconds"] = 0 });
                    Action("Bewegung freigeben", "freeze", new() { ["frozen"] = false, ["seconds"] = 0 }); break;
                case "teleport": Action("Zum Spieler teleportieren", "goto_player"); Action("Spieler zu mir holen", "bring_player"); break;
                case "noclip": Action("Noclip an", "noclip", new() { ["enabled"] = true }); Action("Noclip aus", "noclip", new() { ["enabled"] = false }); break;
                case "health":
                    Action("Töten", "slay"); Action("Respawn", "respawn");
                    Action("100 Lebenspunkte", "health", new() { ["value"] = 100 }); Action("100 Rüstung", "armor", new() { ["value"] = 100 }); break;
            }
            return entries;
        }, back, CanManage);
    }

    private static string GroupTitle(string group) => group switch
    {
        "sanctions" => "Kick & Ban", "communication" => "Chat & Nachrichten", "voice" => "Sprachchat", "text" => "Textchat",
        "silence" => "Sprache & Text", "movement" => "Team & Bewegung", "team" => "Team wechseln", "freeze" => "Bewegung",
        "teleport" => "Teleportieren", "noclip" => "Noclip", "health" => "Leben & Ausrüstung", _ => "Spieler"
    };

    private void Reasons(IPlayer p, string label, string session, string action, string target, Action<IPlayer> back) =>
        Menu(p, "LPLK · Grund", _ => settings.Reasons.Select(reason => (reason, (Action<IPlayer>)(a =>
        {
            Target(target, session);
            if (action == "kick") Confirm(a, label + ": " + reason, session, action, target, new() { ["reason"] = reason },
                b => Reasons(b, label, session, action, target, back));
            else Durations(a, label, reason, session, action, target, back);
        }))), back, a => Can(a, action));

    private void Durations(IPlayer p, string label, string reason, string session, string action, string target, Action<IPlayer> back) =>
        Menu(p, "LPLK · Dauer", _ => settings.Durations.Select(minutes => (minutes == 0 ? "Dauerhaft" : minutes + " Minuten", (Action<IPlayer>)(a =>
            Confirm(a, label + ": " + reason + " / " + (minutes == 0 ? "dauerhaft" : minutes + " Min."), session, action, target,
                new() { ["reason"] = reason, ["minutes"] = minutes }, b => Durations(b, label, reason, session, action, target, back))))),
            a => Reasons(a, label, session, action, target, back), a => Can(a, action));

    private void Confirm(IPlayer p, string label, string? session, string action, string? target, Dictionary<string, object> payload, Action<IPlayer> back)
    {
        if (!Can(p, action)) return;
        string targetName = target is not null && session is not null ? Target(target, session).Controller.PlayerName : target ?? "Server";
        Menu(p, "LPLK · " + label + " → " + targetName, _ => new (string, Action<IPlayer>)[]
        {
            ("Bestätigen", a =>
            {
                if (target is not null && session is not null) Target(target, session);
                Run(a, action, target, payload, a.SendChat);
                // Kick/ban can remove the target. Return to a valid player list after execution.
                if (target is not null && session is not null) PlayerList(a, 0); else OpenAdministration(a);
            })
        }, back, a => Can(a, action));
    }

    private void ServerMenu(IPlayer p) => Menu(p, "LPLK · Server & Maps", a =>
    {
        var entries = new List<(string, Action<IPlayer>)>();
        if (Can(a, "round_restart")) entries.Add(("Runde neu starten (5 Sekunden)", b => Confirm(b, "Rundenneustart", null, "round_restart", null, new() { ["countdown_seconds"] = 5 }, ServerMenu)));
        if (Can(a, "broadcast")) entries.Add(("Nachricht an alle", b => Input(b, "Servernachricht", (c, text) => Confirm(c, "Ansage: " + text, null, "broadcast", null, new() { ["message"] = text, ["type"] = "chat" }, ServerMenu), ServerMenu)));
        entries.AddRange(ModuleEntries(a, "server", ServerMenu));
        return entries;
    }, OpenAdministration, CanOpenAdmin);
}

public sealed record AdminSettings
{
    public int[] Durations { get; init; } = [5, 30, 60, 120, 1440, 10080, 0];
    public string[] Reasons { get; init; } = ["Regelverstoß", "Beleidigung", "Spam", "AFK", "Cheating"];
}
