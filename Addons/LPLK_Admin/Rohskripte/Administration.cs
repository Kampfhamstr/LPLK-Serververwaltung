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
    private string SettingsPath=>Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"settings.json");
    private bool Has(IPlayer p,string permission)=>active&&p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&Core.Permission.PlayerHasPermission(p.SteamID,permission);
    private bool Can(IPlayer p,string action)=>active&&p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&AdminActionPolicy.Allowed(action,s=>Has(p,s));
    private bool CanManage(IPlayer p)=>Has(p,"admin.menu")||Has(p,"admin.map")||AdminActionPolicy.Permissions.Keys.Any(a=>Can(p,a));
    private Func<ulong,string,string,string>? Backend()=>active&&interfaces is not null&&interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",out var api)&&api.TryGetValue("execute",out var call)?call as Func<ulong,string,string,string>:null;
    private IPlayer? Find(ulong id)=>Core.PlayerManager.GetAllPlayers().FirstOrDefault(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID==id);
    private void Run(IPlayer? actor,string action,string? target,Dictionary<string,object>? payload,Action<string> reply)
    {
        if(actor is not null&&!Can(actor,action))throw new InvalidOperationException("Keine Berechtigung.");
        var execute=Backend()??throw new InvalidOperationException("SerververwaltungBridge 1.6.2 ist nicht bereit.");
        RequireLocalSanctions(action);
        using var result=JsonDocument.Parse(execute(actor?.SteamID??0,action,JsonSerializer.Serialize(new {target,payload=payload??[]})));
        bool success=result.RootElement.GetProperty("success").GetBoolean();string message=result.RootElement.GetProperty("message").GetString()??"Keine Rückmeldung.";
        if(!success)throw new InvalidOperationException(message);
        reply("[LPLK] "+message);
    }
    private void Guard(Action action,Action<string> reply)
    {
        try{if(active)action();}catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Admin: Aktion abgewiesen/fehlgeschlagen");reply("[LPLK] "+e.Message);}
    }
    private static bool GlobalAction(string action)=>action is "ban" or "unban" or "mute" or "unmute" or "gag" or "ungag" or "silence" or "unsilence";
    private void RequireLocalSanctions(string action)
    {
        if(GlobalAction(action)&&(interfaces is null||!interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",out var api)||!api.TryGetValue("localSanctions",out var capability)||capability is not Func<bool> ready||!ready()))throw new InvalidOperationException("Lokale Sperrverwaltung nicht bereit; Aktion nicht ausgeführt.");
    }
    private void Input(IPlayer p, string title, Action<IPlayer, string> accept, Action<IPlayer> back, int length = 150)
    {
        if (!CanManage(p)) return;
        int serial = generation;
        var input = new InputMenuOption(new MenuNumbers().Next(MenuLayout.Label(title)), maxLength: length,
            validator: v => !string.IsNullOrWhiteSpace(v) && v.Length <= length, hintMessage: "Text in den Chat eingeben.");
        GameThreadMenu.BindInput(Core, input, p, () => active && serial == generation, (a, value) =>
        { ReloadSettings();if(serial!=generation){a.SendLplkChat("[LPLK] Einstellungen geändert; Menü neu öffnen.");return;} if (CanManage(a)) Guard(() => accept(a, AdminActionPolicy.Text(value, length)), a.SendLplkChat); });
        var cancel = new ButtonMenuOption(MenuColors.Back("Zurück")) { CloseAfterClick = false };
        GameThreadMenu.Bind(Core, cancel, p, () => active && serial == generation, back);
        OpenOwnedMenu(p, Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false)
            .Design.SetMenuTitle(MenuColors.Heading(MenuLayout.Label(title))).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(cancel).Build());
    }

    private void PlayerList(IPlayer p, int page, bool extra = false, int advancedPage = 0) => Menu(p, "LPLK · Spieler auswählen", _ =>
        Core.PlayerManager.GetAllPlayers().Where(t => t.IsValid && !t.IsFakeClient && t.IsAuthorized)
            .OrderBy(t => t.Name).Select(t =>
            {
                ulong id = t.SteamID; string session = t.SessionId.ToString();
                return (t.Name, (Action<IPlayer>)(a => PlayerActions(a, id, session, page, extra, advancedPage)));
            }), PlayerManagement, CanOpenAdmin, page,
        (a, next) => PlayerList(a, next, extra, advancedPage), scroll: true);

    private IPlayer Target(string id, string? session)
    {
        var t = Find(ulong.Parse(id));
        if (t is null || (session is not null && session != t.SessionId.ToString()))
            throw new InvalidOperationException("Spieler hat den Server verlassen oder sich neu verbunden. Bitte neu auswählen.");
        return t;
    }

    private void PlayerActions(IPlayer p, ulong steamId, string session, int playerPage, bool extra = false, int advancedPage = 0, int actionPage = 0)
    {
        string id = steamId.ToString(); var target = Target(id, session);
        Action<IPlayer> here = a => PlayerActions(a, steamId, session, playerPage, extra, advancedPage, actionPage);
        Menu(p, "LPLK · " + target.Name, a =>
        {
            Target(id, session);
            var entries = new List<(string, Action<IPlayer>)>();
            void Add(string label, string action, Dictionary<string, object>? payload = null)
            {
                if (!Can(a, action)) return;
                entries.Add((label, b => PlayerAction(b, label, action, id, session, payload, here)));
            }
                Add("Kicken", "kick"); Add("Bannen", "ban");
                if (new[] { "mute", "gag", "silence", "unmute", "ungag", "unsilence" }.Any(action => Can(a, action)))
                    entries.Add(("Chat", b => PlayerChoices(b, id, session, "chat", here)));
                if (Can(a, "team")) entries.Add(("Team", b => PlayerChoices(b, id, session, "team", here)));
                Add("Töten", "slay"); Add("Respawn", "respawn");
                Add("Hinteleportieren", "goto_player"); Add("Herholen", "bring_player");
                Add("Einfrieren", "freeze", new() { ["frozen"] = true, ["seconds"] = 0 });
                Add("Freigeben", "freeze", new() { ["frozen"] = false, ["seconds"] = 0 });
                Add("Heilen", "health", new() { ["value"] = 100 }); Add("Rüsten", "armor", new() { ["value"] = 100 });
                Add("Noclip an", "noclip", new() { ["enabled"] = true }); Add("Noclip aus", "noclip", new() { ["enabled"] = false });
                if (Can(a, "private_message")) entries.Add(("Nachricht", b => Input(b, "Nachricht", (c, text) =>
                    Confirm(c, "Nachricht: " + text, session, "private_message", id, new() { ["message"] = text, ["type"] = "chat" }, here), here)));
            entries.AddRange(TargetModuleActions(a,target));
            return entries;
        }, a => PlayerList(a, playerPage, extra, advancedPage), CanOpenAdmin, actionPage,
            (a, next) => PlayerActions(a, steamId, session, playerPage, extra, advancedPage, next), scroll: true);
    }

    private void PlayerAction(IPlayer p, string label, string action, string id, string session, Dictionary<string, object>? payload, Action<IPlayer> back)
    {
        if (!Can(p, action)) throw new InvalidOperationException("Keine Berechtigung.");
        Target(id, session);
        if (action is "kick" or "ban" or "mute" or "gag" or "silence") Reasons(p, label, session, action, id, back);
        else Confirm(p, label, session, action, id, payload ?? [], back);
    }

    private void PlayerChoices(IPlayer p, string id, string session, string choice, Action<IPlayer> back, int page = 0)
    {
        var target = Target(id, session);
        Action<IPlayer> here = a => PlayerChoices(a, id, session, choice, back, page);
        Menu(p, "LPLK · " + target.Name + (choice == "team" ? " · Team" : " · Chat"), a =>
        {
            Target(id, session);
            var entries = new List<(string, Action<IPlayer>)>();
            void Add(string label, string action, Dictionary<string, object>? payload = null)
            {
                if (Can(a, action)) entries.Add((label, b => PlayerAction(b, label, action, id, session, payload, here)));
            }
            if (choice == "team")
            {
                Add("T", "team", new() { ["team"] = "t" }); Add("CT", "team", new() { ["team"] = "ct" });
                Add("Zuschauer", "team", new() { ["team"] = "spectator" });
            }
            else
            {
                Add("Stummschalten", "mute"); Add("Sprache freigeben", "unmute");
                Add("Schreibsperre", "gag"); Add("Text freigeben", "ungag");
                Add("Beides sperren", "silence"); Add("Beides freigeben", "unsilence");
            }
            return entries;
        }, back, CanManage, page, (a, next) => PlayerChoices(a, id, session, choice, back, next), scroll: true);
    }

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
        string targetName = target is not null && session is not null ? Target(target, session).Name : target ?? "Server";
        Menu(p, "LPLK · " + (action is "ban" or "unban" or "mute" or "unmute" or "gag" or "ungag" or "silence" or "unsilence"?"Lokal: ":"") + label + " → " + targetName, _ => new (string, Action<IPlayer>)[]
        {
            ("Bestätigen", a =>
            {
                if (target is not null && session is not null) Target(target, session);
                Run(a, action, target, payload, a.SendLplkChat);
                // Kick/ban can remove the target. Return to a valid player list after execution.
                if (target is not null && session is not null) PlayerList(a, 0); else OpenAdministration(a);
            })
        }, back, a => Can(a, action));
    }


}

public sealed record AdminSettings
{
    public bool LegacyPlayerMenus { get; init; }
    public Dictionary<string,string> LegacyGameMaps { get; init; } = [];
    public Dictionary<string,uint> LegacyWorkshopMaps { get; init; } = [];
    public Dictionary<string,string[]> Commands { get; init; } = [];
    public int[] Durations { get; init; } = [5, 30, 60, 120, 1440, 10080, 0];
    public string[] Reasons { get; init; } = ["Regelverstoß", "Beleidigung", "Spam", "AFK", "Cheating"];
}
