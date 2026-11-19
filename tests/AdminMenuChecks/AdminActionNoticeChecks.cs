using System.Text.Json;
using LPLK_Admin;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Permissions;
using SwiftlyS2.Shared.Players;

internal static class AdminActionNoticeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = new MenuWorld();
        const string privateReason = "Interner Prüfgrund";
        const string backendDetail = "Vertrauliche Backendantwort";
        var actor = new Recipient(1, "Ausführender Admin", ["admin.menu", "admin.kick"]);
        var observer = new Recipient(2, "Weiterer Admin", ["admin.menu"]);
        var player = new Recipient(3, "Normaler [green]Spieler\n", []);
        var bot = new Recipient(4, "Bot", ["admin.menu"], bot: true);
        var unauthorized = new Recipient(5, "Nicht angemeldeter Admin", ["admin.menu"], authorized: false);
        var disconnected = new Recipient(6, "Getrennter Admin", ["admin.menu"], valid: false);
        var otherPlayer = new Recipient(7, "Verbundener Spieler", []);
        Recipient[] recipients = [actor, observer, player, bot, unauthorized, disconnected, otherPlayer];
        List<IPlayer> online = recipients.Select(recipient => recipient.Player).ToList();

        var playerManager = Proxy.Make<IPlayerManagerService>((method, args) => method.Name switch
        {
            "GetAllPlayers" or "GetAllValidPlayers" => online,
            _ => method.Invoke(world.Core.PlayerManager, args)
        });
        var permission = Proxy.Make<IPermissionManager>((method, args) =>
            method.Name == "PlayerHasPermission"
                ? recipients.Any(recipient => recipient.Player.SteamID == (ulong)args![0]! &&
                    recipient.Permissions.Contains((string)args[1]!))
                : Proxy.Empty(method.ReturnType));
        var core = Proxy.Make<ISwiftlyCore>((method, args) => method.Name switch
        {
            "get_PlayerManager" => playerManager,
            "get_Permission" => permission,
            _ => method.Invoke(world.Core, args)
        });

        bool backendSuccess = true;
        int backendCalls = 0;
        string? backendPayload = null;
        world.Interfaces.AddSharedInterface<IReadOnlyDictionary<string, Delegate>, Dictionary<string, Delegate>>(
            "LPLK.Bridge.Admin.v1", new()
            {
                ["execute"] = (Func<ulong, string, string, string>)((_, _, payload) =>
                {
                    backendCalls++;
                    backendPayload = payload;
                    if (backendSuccess) online.RemoveAll(candidate => ReferenceEquals(candidate, player.Player));
                    return JsonSerializer.Serialize(new { success = backendSuccess, message = backendDetail });
                })
            });
        var replies = new List<string>();
        var context = Proxy.Make<ICommandContext>((method, args) => method.Name switch
        {
            "get_Sender" => actor.Player,
            "get_IsSentByPlayer" => true,
            "get_Args" => new[] { "#3", privateReason },
            "Reply" => AddMessage(replies, args),
            _ => Proxy.Empty(method.ReturnType)
        });

        var plugin = new Plugin(core);
        plugin.Load(true);
        plugin.UseSharedInterface(world.Interfaces);
        try
        {
            plugin.Kick(context);
            using var executed = JsonDocument.Parse(backendPayload ?? "{}");
            check(backendCalls == 1 &&
                executed.RootElement.GetProperty("payload").GetProperty("reason").GetString() == privateReason,
                "A real kick command reaches the backend with its action payload");
            check(observer.Messages.Count == 1, "Successful backend action notifies another authorized Admin once");
            var notice = observer.Messages.Single();
            check(notice.Contains("[red]") && notice.Contains("[white]"), "Admin action notice uses red and white");
            check(notice.Contains(actor.Player.Name), "Admin action notice names the actor");
            check(notice.Contains("Kick") && notice.Contains("Normaler Spieler") &&
                !notice.Contains("[green]") && !notice.Contains('\n'),
                "A kick identifies its now-disconnected target and sanitizes their display name");
            check(!notice.Contains(privateReason) && !notice.Contains(backendDetail),
                "Admin action notice does not disclose the reason or backend reply");
            check(actor.Messages.Count == 0 && replies.Count == 1,
                "The actor receives their regular command reply only");
            foreach (var excluded in new[] { player, bot, unauthorized, disconnected, otherPlayer })
                check(excluded.Messages.Count == 0, "Admin action notice excludes " + excluded.Player.Name);

            online.Add(player.Player);
            backendSuccess = false;
            plugin.Kick(context);
            check(backendCalls == 2 && replies.Count == 2 && observer.Messages.Count == 1,
                $"A backend rejection returns to the actor without notifying other admins: {backendCalls} calls, " +
                $"{replies.Count} replies, {observer.Messages.Count} notices, reply {replies.LastOrDefault()}");

            actor.Permissions.Remove("admin.kick");
            plugin.Kick(context);
            check(backendCalls == 2 && replies.Count == 3 && observer.Messages.Count == 1,
                "An unauthorized action reaches neither backend nor other admins");
        }
        finally
        {
            plugin.Unload();
        }
    }

    private static object? AddMessage(List<string> messages, object?[]? args)
    {
        messages.Add((string)args![0]!);
        return null;
    }

    private sealed class Recipient
    {
        public IPlayer Player { get; }
        public HashSet<string> Permissions { get; }
        public List<string> Messages { get; } = [];

        public Recipient(int slot, string name, IEnumerable<string> permissions, bool bot = false,
            bool authorized = true, bool valid = true)
        {
            Permissions = new(permissions, StringComparer.Ordinal);
            Player = Proxy.Make<IPlayer>((method, args) => method.Name switch
            {
                "get_SteamID" => 76561198000000000UL + (ulong)slot,
                "get_PlayerID" or "get_Slot" => slot,
                "get_SessionId" => (ulong)slot,
                "get_Name" => name,
                "get_IsValid" => valid,
                "get_IsAuthorized" => authorized,
                "get_IsFakeClient" => bot,
                "SendChat" => AddMessage(Messages, args),
                _ => Proxy.Empty(method.ReturnType)
            });
        }
    }
}
