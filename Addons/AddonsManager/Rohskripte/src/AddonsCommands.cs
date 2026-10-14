using AddonsManager.Config;
using AddonsManager.SteamWorkshop;
using AddonsManager.Utils;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;

namespace AddonsManager.Commands;

public partial class AddonsCommands
{
    private ISwiftlyCore Core;
    private AddonsWorkshopManager WorkshopManager;

    public AddonsCommands(ISwiftlyCore core, AddonsWorkshopManager workshopManager)
    {
        Core = core;
        WorkshopManager = workshopManager;
        core.Registrator.Register(this);
    }

    [Command("searchpath")]
    public void ViewSearchPaths(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            context.Reply("[AddonsManager] This command can only be used from the server console.");
            return;
        }

        Core.GameFileSystem.PrintSearchPaths();
    }

    [Command("downloadaddon")]
    public void DownloadAddonCommand(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            context.Reply("[AddonsManager] This command can only be used from the server console.");
            return;
        }

        if (context.Args.Length < 1)
        {
            context.Reply("[AddonsManager] Usage: downloadaddon <workshop_id>");
            return;
        }

        WorkshopManager.DownloadAddon(context.Args[0], true, true);
    }

    [Command("addonupdates")]
    public void UpdateStatus(ICommandContext context)
    {
        context.Reply($"[AddonsManager 2.0.3-dc.2] Pruefung bei jedem Mapstart: {WorkshopManager.MapChecksEnabled}; letzte Pruefung: {WorkshopManager.LastMapCheck}.");
        foreach (string id in WorkshopManager.ConfiguredAddons)
            context.Reply($"[AddonsManager] {id}: {WorkshopManager.AddonStatus(id)}");
    }
}
