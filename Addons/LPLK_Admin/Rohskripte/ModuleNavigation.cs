using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private sealed record ModuleReturn(ulong SteamId, string Session, string Id, string? Title, Action<IPlayer> Back);
    private readonly Dictionary<int, ModuleReturn> moduleReturns = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMenuAPI, object> returnMenus = new();

    // Modules recreate their root after changing a setting or returning from a child.
    // Retain the category return for this player session without changing their actions.
    private void ModuleMenuOpened(object? sender, MenuManagerEventArgs args)
    {
        Core.Scheduler.NextWorldUpdate(() =>
        {
            var p = args.Player;
            if (!active || p is null || args.Menu is null || !Connected(p) || !ReferenceEquals(Core.MenusAPI.GetCurrentMenu(p), args.Menu)) return;
            if (moduleReturns.TryGetValue(p.PlayerID, out var route) &&
                route.SteamId == p.SteamID && route.Session == p.SessionId.ToString() &&
                route.Title == args.Menu.Configuration.Title && ModuleAllowed(p, route.Id))
                AddModuleReturn(p, args.Menu, route.Back);
        });
    }

    private void AddModuleReturn(IPlayer p, IMenuAPI menu, Action<IPlayer> back)
    {
        if (returnMenus.TryGetValue(menu, out _) ||
            menu.Options.Any(o => o.Text == MenuColors.Back("Zurück"))) return;
        returnMenus.Add(menu, new());
        var option = new ButtonMenuOption(MenuColors.Back("Zurück")) { CloseAfterClick = false };
        GameThreadMenu.Bind(Core, option, p, () => active, a => { if (CanOpenAdmin(a)) back(a); });
        menu.AddOption(option);
    }
}
