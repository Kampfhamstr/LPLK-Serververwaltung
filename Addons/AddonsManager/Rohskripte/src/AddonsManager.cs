using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;
using AddonsManager.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AddonsManager.Utils;
using AddonsManager.SteamWorkshop;
using AddonsManager.Commands;
using AddonsManager.Hooks;
using AddonsManager.Clients;

namespace AddonsManager;

[PluginMetadata(Id = "AddonsManager", Version = "2.0.3-lplk.6", Name = "Addons Manager", Author = "Swiftly Development Team; LPLK", Description = "Workshop mounts and game-data correction for CS2 2000930", MinimumAPIVersion = "1.4.13")]
public class AddonsManager(ISwiftlyCore core) : BasePlugin(core)
{
    public IServiceProvider? ServiceProvider;
    private CancellationTokenSource? progressTimer;

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void Load(bool hotReload)
    { ChatTheme.Initialize("AddonsManager",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        Core.Configuration
            .InitializeJsonWithModel<AddonsConfig>("config.jsonc", "Main")
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: false);
            });

        ServiceCollection services = new();
        services.AddSwiftly(Core)
                .AddSingleton<AddonsUtilities>()
                .AddSingleton<AddonsWorkshopManager>()
                .AddSingleton<AddonsCommands>()
                .AddSingleton<AddonsClients>()
                .AddSingleton<AddonsHooks>()
                .AddOptionsWithValidateOnStart<AddonsConfig>()
                .BindConfiguration("Main");

        ServiceProvider = services.BuildServiceProvider();

        _ = ServiceProvider.GetRequiredService<AddonsUtilities>();
        var workshopManager = ServiceProvider.GetRequiredService<AddonsWorkshopManager>();
        _ = ServiceProvider.GetRequiredService<AddonsCommands>();
        _ = ServiceProvider.GetRequiredService<AddonsClients>();
        _ = ServiceProvider.GetRequiredService<AddonsHooks>();

        progressTimer = Core.Scheduler.RepeatBySeconds(1.0f, workshopManager.PrintDownloadProgress);
        // A hot load may occur after the Steam activation event has already fired.
        if (hotReload) workshopManager.InitializeSteamCallbacks(apiAvailable: true);
    }

    public override void Unload()
    {
        progressTimer?.Cancel();
        progressTimer?.Dispose();
        (ServiceProvider as IDisposable)?.Dispose();
    }
}
