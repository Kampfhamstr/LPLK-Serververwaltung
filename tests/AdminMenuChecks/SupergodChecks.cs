using System.Reflection;
using LPLK.Common;
using LPLK_Admin;
using SwiftlyS2.Shared.Commands;

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
            check(world.Messages.Last().Contains("Du musst in T oder CT leben"),
                "Direct Supergod command requires a living player on T or CT");
            await world.Click(off);
            check(world.Messages.Last().Contains("Du musst in T oder CT leben") &&
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
    }
}
