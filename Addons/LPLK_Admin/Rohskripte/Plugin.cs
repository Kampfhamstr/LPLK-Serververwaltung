using Microsoft.Extensions.Logging;
using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Admin;

[PluginMetadata(Id = "LPLK_Admin", Name = "LPLK_Admin", Version = "0.3.7", Author = "LPLK", Description = "Gruppierte Spieler- und Adminmenüs", MinimumAPIVersion = "1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private IInterfaceManager? interfaces;
    private bool active;
    public override void Load(bool hotReload) { LoadSettings(); active = true; Core.Logger.LogInformation("LPLK_Admin 0.3.7: !menu, !admin und !adminmenu bereit."); }
    public override void Unload() { active = false; generation++; interfaces = null; }
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
            ctx.Reply("LPLK_Admin 0.3.7; Backend="+(Backend() is not null)+"; Module: " + string.Join(", ", Core.PluginManager.GetAllPlugins().Where(id => Api(id) is not null)));
            return;
        }
        OpenAdministration(player);
    }
}


