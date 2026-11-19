using System.Collections;
using System.Reflection;
using LPLK.Common;
using LPLK_Admin;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

internal static class SupergodChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        // The fake player has no live native pawn. These tests cover the real
        // menu/command gates, leaving native movement, damage and weapons to integration.
        var world = new MenuWorld();
        var plugin = new Plugin(world.Core);
        plugin.Load(true);
        try
        {
            var toggle = typeof(Plugin).GetMethod(nameof(Plugin.SupergodCommand))!;
            check(toggle.GetCustomAttribute<Command>()?.Name == "supergod" &&
                toggle.GetCustomAttributes<CommandAlias>().Any(alias => alias.Alias == "sgm"),
                "The short Supergod alias uses the same guarded toggle handler");

            world.Permissions.Add("admin.menu");
            plugin.Admin(world.Context);
            await world.Click("Spielerverwaltung");
            check(!world.Labels.Any(label => label.Contains("Supergodmodus")),
                "Basic menu permission does not expose Supergod");
            plugin.SupergodCommand(world.Context);
            check(world.Messages.Last().Contains("Keine Berechtigung"),
                "Direct Supergod command also requires elevated rights");

            world.Permissions.Add("admin.cheats");
            plugin.Admin(world.Context);
            await world.Click("Spielerverwaltung");
            await world.Click("Supergodmodus");
            var off = world.Find("Supergodmodus: AUS");
            check(off.Text.Contains(MenuColors.Red), "Supergod starts off and is displayed in red");
            check(!world.Labels.Any(label => label.Contains("Waffen holen")),
                "Weapon picker is hidden when Supergod is off");

            var current = world.Current;
            plugin.SupergodWeaponsCommand(world.Context);
            check(ReferenceEquals(current, world.Current) && world.Messages.Last().Contains("Zuerst Supergodmodus"),
                "Direct weapon command also requires active Supergod");

            plugin.SupergodCommand(world.Context);
            check(world.Messages.Last().Contains("Du musst in T oder CT sein"),
                "Direct Supergod command requires a T or CT player");
            await world.Click(off);
            check(world.Messages.Last().Contains("Du musst in T oder CT sein") &&
                world.Find("Supergodmodus: AUS").Text.Contains(MenuColors.Red),
                "Invalid activation keeps the menu switch off");

            world.Permissions.Remove("admin.cheats");
            await world.Click(off);
            check(world.Messages.Last().Contains("Berechtigung inzwischen entzogen"),
                "A stale Supergod option cannot run after its permission is revoked");
        }
        finally
        {
            plugin.Unload();
        }
        await RespawnChecks(check);
    }

    private static async Task RespawnChecks(Action<bool, string> check)
    {
        const BindingFlags internalInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        int team = 2;
        int respawns = 0;
        bool alive = false;
        var world = new MenuWorld();
        var standardPlayer = world.Player;
        world.Player = Proxy.Make<IPlayer>((method, args) =>
        {
            if (method.Name == nameof(IPlayer.Respawn))
            {
                respawns++;
                return null;
            }
            if (method.Name == "get_IsAlive") return alive;
            return method.Invoke(standardPlayer, args);
        });
        world.Permissions.Add("admin.cheats");
        var plugin = new Plugin(world.Core);
        typeof(Plugin).GetField("supergodTeam", internalInstance)!
            .SetValue(plugin, (Func<IPlayer, int>)(_ => team));
        var requests = (IDictionary)typeof(Plugin).GetField("supergodRespawns", internalInstance)!.GetValue(plugin)!;
        var spawned = typeof(Plugin).GetMethod("SupergodPlayerSpawned", internalInstance)!;
        void Spawn() => spawned.Invoke(plugin, [world.Player]);
        void Toggle() => plugin.SupergodCommand(world.Context);

        plugin.Load(true);
        try
        {
            plugin.Admin(world.Context);
            await world.Click("Spielerverwaltung");
            await world.Click("Supergodmodus");
            var start = world.Find("Supergodmodus: AUS");
            await world.Click(start);
            check(respawns == 1 && requests.Count == 1 && world.Find("Start abbrechen") is not null,
                "Dead T player requests one respawn and can cancel the pending start");
            check(!world.Messages.Any(message => message.Contains("Supergodmodus AN")),
                "A respawn request does not declare Supergod active before confirmation");
            await world.Click(start);
            check(respawns == 1 && requests.Count == 1, "Stale activate button cannot start a duplicate respawn");

            Toggle();
            check(respawns == 1 && requests.Count == 0 && world.Messages.Last().Contains("bleibt AUS"),
                "!sgm cancels a pending start");
            Spawn();
            plugin.SupergodTick();
            check(requests.Count == 0 && !world.Messages.Any(message => message.Contains("Supergodmodus AN")),
                "Spawn after cancellation does not enable Supergod");

            team = 3;
            Toggle();
            check(respawns == 2 && requests.Count == 1, "A dead CT player can request respawn after cancelling");
            alive = true;
            plugin.SupergodTick();
            check(requests.Count == 1, "Alive flag alone does not complete a pending spawn");
            Spawn();
            Spawn();
            plugin.SupergodTick();
            plugin.SupergodWeaponsCommand(world.Context);
            check(requests.Count == 1 && respawns == 2 && world.Messages.Last().Contains("Zuerst Supergodmodus"),
                "Spawn events without a valid pawn cannot activate the mode or allow weapons");

            team = 2;
            plugin.SupergodTick();
            check(requests.Count == 0, "Team change cancels the pending start");

            alive = false;
            Toggle();
            world.Session++;
            Spawn();
            plugin.SupergodTick();
            check(respawns == 3 && requests.Count == 0, "A new connection cannot inherit a pending start");

            Toggle();
            world.Permissions.Remove("admin.cheats");
            Spawn();
            plugin.SupergodTick();
            check(respawns == 4 && requests.Count == 0, "Revoking rights cancels the pending start");
            check(!world.Messages.Any(message => message.Contains("Supergodmodus AN")),
                "None of the unconfirmed or invalidated spawns report successful activation");
        }
        finally
        {
            plugin.Unload();
        }
    }
}
