using Microsoft.Extensions.Logging;
using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Admin;

[PluginMetadata(Id = "LPLK_Admin", Name = "LPLK_Admin", Version = "0.11.7", Author = "LPLK", Description = "Einfache Spieler- und Adminmenüs", MinimumAPIVersion = "1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private IInterfaceManager? interfaces;
    private bool active;
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload) { ChatTheme.Initialize("LPLK_Admin",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name)); LoadSettings(); active = true; Core.MenusAPI.MenuOpened += ModuleMenuOpened; ReconcileAdminCommands(); WriteSettingsStatus(); Core.Logger.LogInformation("LPLK_Admin 0.11.7: !menu, !admin und !adminmenu bereit."); }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore() { active = false; Core.MenusAPI.MenuOpened -= ModuleMenuOpened; moduleReturns.Clear(); menuCursors.Clear(); RemoveAdminCommands(); InvalidateMenus(); interfaces = null;  LPLK.Common.DeferredJson.Drain(); }
    public override void UseSharedInterface(IInterfaceManager manager) => interfaces = manager;

    private IReadOnlyDictionary<string, Delegate>? Api(string id)
    {
        if (!active || interfaces is null || !Loaded(id)) return null;
        return interfaces.TryGetSharedInterface<IReadOnlyDictionary<string, Delegate>>("LPLK.Admin." + id + ".v1", out var result) ? result : null;
    }

    [Command("admin")]
    [CommandAlias("adminmenu")]
    public void Admin(ICommandContext ctx)
    {
        if (!ctx.IsSentByPlayer || ctx.Sender is not { } player)
        {
            ReloadSettings();WriteSettingsStatus();
            ctx.ReplyLplk("LPLK_Admin 0.11.7; Gründe="+settings.Reasons.Length+"; Dauern="+settings.Durations.Length+"; reloadError="+(settingsError??"none")+"; Backend="+(Backend() is not null)+"; Module: " + string.Join(", ", Core.PluginManager.GetAllPlugins().Where(id => Api(id) is not null)));
            return;
        }
        OpenAdministration(player);
    }
}



