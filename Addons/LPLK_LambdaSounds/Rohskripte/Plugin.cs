using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_LambdaSounds;

[PluginMetadata(Id = "LPLK_LambdaSounds", Name = "LPLK_LambdaSounds", Version = "0.1.0",
    Author = "LPLK", Description = "Precaches the Dust II Lambda weapon sound bank", MinimumAPIVersion = "1.4.14")]
public sealed class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private const string Version = "0.1.0";
    private const string Workshop = "3773100753";
    private const string WorkshopPath = "/730/" + Workshop + "/" + Workshop;

    // The model pack also ships soundevents_addon.vsndevts. Register
    // Lambda's uniquely named library without changing either pack's mount order.
    private static readonly string[] Resources =
    [
        "soundevents/soundevents_lambda.vsndevts",
        "models/weapons/weapon_1911_u/sounds/wpn_shotgun_fire_head_01.vsnd",
        "models/weapons/weapon_1911_u/sounds/wpn_shotgun_fire_head_02.vsnd",
        "models/weapons/weapon_1911_u/sounds/wpn_shotgun_fire_head_03.vsnd",
        "sounds/ar2/npc_ar2_fire1.vsnd",
        "sounds/combine_smg/wpn_combine_smg_lfe_01.vsnd",
        "sounds/combine_smg/wpn_combine_smg_lfe_02.vsnd",
        "sounds/combine_smg/wpn_combine_smg_lfe_03.vsnd",
        "sounds/combine_smg/wpn_combine_smg_lfe_04.vsnd",
        "sounds/crossbow/bolt_fire.vsnd",
        "sounds/grenade_launcher/grenade_launcher.vsnd",
        "sounds/guns/lever/distant.vsnd",
        "sounds/guns/lever/shot.vsnd",
        "sounds/rpg/rocketfire1.vsnd"
    ];

    private int manifestResources;
    private string? lastManifestWorkshopId;

    public override void Load(bool hotReload)
    {
        Core.Event.OnPrecacheResource += OnPrecache;
        Core.Event.OnMapUnload += OnMapUnload;
    }

    public override void Unload()
    {
        Core.Event.OnPrecacheResource -= OnPrecache;
        Core.Event.OnMapUnload -= OnMapUnload;
    }

    private void OnMapUnload(IOnMapUnloadEvent _)
    {
        manifestResources = 0;
        lastManifestWorkshopId = null;
    }

    private bool TargetMounted()
    {
        if (!Core.GameFileSystem.FileExists(Resources[0] + "_c", "GAME"))
            return false;

        // The map ID reported by Swiftly can still describe the previous map
        // while the engine builds the incoming map's resource manifest.
        string paths = Core.GameFileSystem.GetSearchPath("GAME", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 128)
            .Replace('\\', '/');
        return paths.Contains(WorkshopPath + ".vpk", StringComparison.Ordinal)
            || paths.Contains(WorkshopPath + "_dir.vpk", StringComparison.Ordinal);
    }

    private void OnPrecache(IOnPrecacheResourceEvent manifest)
    {
        if (!TargetMounted())
            return;
        if (Resources.Any(path => !Core.GameFileSystem.FileExists(path + "_c", "GAME")))
        {
            Core.Logger.LogWarning("Workshop {Workshop}: Lambda sound resources are incomplete.", Workshop);
            return;
        }

        foreach (string path in Resources)
            manifest.AddItem(path);
        manifestResources = Resources.Length;
        lastManifestWorkshopId = Workshop;
        Core.Logger.LogInformation("Workshop {Workshop}: added {Count} Lambda sound resources to the manifest.",
            Workshop, manifestResources);
    }

    [Command("lplk_lambda_sounds")]
    public void Status(ICommandContext context)
    {
        if (context.IsSentByPlayer || context.Sender is not null || context.Args.Length != 0)
            return;
        context.Reply("LPLK_LAMBDA_SOUNDS " + JsonSerializer.Serialize(new
        {
            version = Version, workshopId = Core.Engine.WorkshopId, targetMounted = TargetMounted(),
            manifestResources, lastManifestWorkshopId
        }));
    }
}
