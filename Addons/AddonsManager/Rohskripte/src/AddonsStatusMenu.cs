using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace AddonsManager.Commands;

public partial class AddonsCommands : IDisposable
{
    private sealed class View(int slot, ulong steam)
    {
        public int Slot = slot;
        public ulong Steam = steam;
        public IMenuAPI? Menu;
        public int Pending;
    }
    private readonly Dictionary<int, View> views = [];
    private readonly Dictionary<int, long> lastOpen = [];
    private volatile bool disposed;

    [Command("addons", registerRaw: true, helpText: "Workshop-Update-Status als Menue anzeigen")]
    public void OpenStatusMenu(ICommandContext context)
    {
        if (context.Sender is not { IsValid: true, IsAuthorized: true, IsFakeClient: false } player)
        { UpdateStatus(context); return; }
        long now = Environment.TickCount64;
        if (lastOpen.TryGetValue(player.Slot, out var last) && now - last < 500) return;
        lastOpen[player.Slot] = now;
        ShowStatus(player);
    }

    private void ShowStatus(IPlayer player)
    {
        var view = new View(player.Slot, player.SteamID);
        var builder = Core.MenusAPI.CreateBuilder().DisableSound().EnableExit().SetPlayerFrozen(false)
            .SetAutoCloseDelay(120).Design.SetMenuTitle("Workshop-Updates")
            .Design.SetMenuFooterVisible(true).Design.SetCommentVisible(true)
            .Design.SetDefaultComment("Statusanzeige; Steuerung aus core.jsonc");
        AddButton(builder, view, "Mapstart-Pruefung: " + (WorkshopManager.MapChecksEnabled ? "AN" : "AUS"),
            p => p.SendLplkChat("[AddonsManager] Letzte Pruefung: " + WorkshopManager.LastMapCheck));
        foreach (string id in WorkshopManager.ConfiguredAddons)
        {
            string itemId = id;
            AddButton(builder, view, itemId + ": " + WorkshopManager.AddonStatus(itemId),
                p => p.SendLplkChat($"[AddonsManager] {itemId}: {WorkshopManager.AddonStatus(itemId)}"));
        }
        AddButton(builder, view, "Anzeige aktualisieren", ShowStatus);
        AddButton(builder, view, "Steam-Verbindung und Wiederholungen", p => p.SendLplkChat("[AddonsManager] Bei voruebergehenden Steam-Fehlern bis zu 3 Wiederholungen nach 30/60/120 Sekunden. Ohne Steam-Anmeldung wird gewartet. Ein Klick auf eine Workshop-ID zeigt den aktuellen Stand."));
        AddButton(builder, view, "Hilfe", p => p.SendLplkChat("[AddonsManager] Pruefung bei jedem Mapstart. Nach Download und folgendem Mapstart liest DeathEffects 0.3.4 den Katalog aus dem Addon und laedt vor. Automatische Wiederholungen erzwingen keinen Mapwechsel."));
        AddButton(builder, view, "Schliessen", p => Close(p.Slot));
        Close(player.Slot);
        view.Menu = builder.Build();
        views[player.Slot] = view;
        try { Core.MenusAPI.OpenMenuForPlayer(player, view.Menu); }
        catch { Close(player.Slot); throw; }
    }

    private void AddButton(IMenuBuilderAPI builder, View view, string label, Action<IPlayer> action)
    {
        var button = new ButtonMenuOption(label) { CloseAfterClick = false };
        button.Click += (_, args) =>
        {
            if (disposed || Interlocked.Exchange(ref view.Pending, 1) != 0) return ValueTask.CompletedTask;
            try
            {
                Core.Scheduler.NextTick(() =>
                {
                    try
                    {
                        if (disposed || !views.TryGetValue(view.Slot, out var current) || !ReferenceEquals(current, view)) return;
                        var player = Core.PlayerManager.GetPlayer(view.Slot);
                        if (player is not { IsValid: true, IsFakeClient: false, IsAuthorized: true }
                            || player.SteamID != view.Steam || args.Player.Slot != view.Slot
                            || !ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player), view.Menu)) return;
                        action(player);
                    }
                    catch (Exception ex) { Core.Logger.LogWarning(ex, "Addon-Statusmenue fehlgeschlagen."); }
                    finally { Interlocked.Exchange(ref view.Pending, 0); }
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref view.Pending, 0);
                if (!disposed) Core.Logger.LogWarning(ex, "Addon-Statusmenue konnte nicht eingeplant werden.");
            }
            return ValueTask.CompletedTask;
        };
        builder.AddOption(button);
    }

    private void Close(int slot)
    {
        if (!views.Remove(slot, out var view) || view.Menu is null) return;
        try
        {
            var player = Core.PlayerManager.GetPlayer(slot);
            if (player is { IsValid: true } && player.SteamID == view.Steam && ReferenceEquals(Core.MenusAPI.GetCurrentMenu(player), view.Menu))
                Core.MenusAPI.CloseMenuForPlayer(player, view.Menu);
        }
        finally
        {
            foreach (var option in view.Menu.Options.ToArray()) (option as IDisposable)?.Dispose();
            (view.Menu as IDisposable)?.Dispose();
        }
    }

    [EventListener<EventDelegates.OnMapUnload>]
    public void ClearStatusMenus(IOnMapUnloadEvent ev) => ClearViews();

    [EventListener<EventDelegates.OnClientDisconnected>]
    public void ClearStatusMenu(IOnClientDisconnectedEvent ev)
    {
        lastOpen.Remove(ev.PlayerId);
        Close(ev.PlayerId);
    }

    private void ClearViews()
    {
        foreach (int slot in views.Keys.ToArray())
            try { Close(slot); } catch (Exception ex) { Core.Logger.LogWarning(ex, "Addon-Statusmenue konnte nicht geschlossen werden."); }
        lastOpen.Clear();
    }

    public void Dispose() { disposed = true; ClearViews(); }
}
