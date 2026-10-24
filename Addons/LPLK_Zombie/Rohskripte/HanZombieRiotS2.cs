using System.Text.Json;
using LPLK.Common;
using HanZombieRiotS2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Zombie;

[PluginMetadata(Id="LPLK_Zombie",Name="LPLK_Zombie",Version="0.1.0",Author="LPLK; gameplay derived from H-AN (GPL-3.0)",Description="Zombie waves with LPLK lifecycle, administration and validated profiles",MinimumAPIVersion="1.4.14")]
public sealed class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ServiceProvider? services;
    private ZombieProfile profiles=null!;
    private ZombieOptions settings=null!;
    private HanZriotGlobals globals=null!;
    private HanZriotEvents events=null!;
    private HanZriotCommands commands=null!;
    private ModuleSupport module=null!;
    private string configPath="",configSignature="",map="unknown";
    private string? error;
    private bool loaded;
    private long nextStatus;
    public override void Load(bool hotReload)
    {
        configPath=Core.Configuration.GetConfigPath("HanZriotCFG.jsonc");
        settings=new(ZombieProfile.ReadMain(File.ReadAllText(configPath)));
        configSignature=JsonSerializer.Serialize(settings.CurrentValue);
        profiles=new();profiles.Reload(Path.GetDirectoryName(configPath)!,"unknown",false);
        var collection=new ServiceCollection();collection.AddSwiftly(Core);
        collection.AddSingleton<IOptionsMonitor<HanZriotCFG>>(settings);
        collection.AddSingleton<IStageConfigProvider>(profiles);collection.AddSingleton<IZombieConfigProvider>(profiles);
        collection.AddSingleton<HanZriotGlobals>();collection.AddSingleton<HanZriotHelpers>();collection.AddSingleton<HanZriotService>();collection.AddSingleton<HanZriotHud>();collection.AddSingleton<HanZriotCommands>();collection.AddSingleton<HanZriotEvents>();collection.AddSingleton<HanZriotAPI>();
        services=collection.BuildServiceProvider();globals=services.GetRequiredService<HanZriotGlobals>();events=services.GetRequiredService<HanZriotEvents>();commands=services.GetRequiredService<HanZriotCommands>();
        module=new(Core,"LPLK_Zombie","Zombie und Wellen","lplk.zombie.admin",OpenAdmin,p=>commands.Allowed(p));module.Active=true;loaded=true;
        Core.Event.OnWorldUpdate+=Maintenance;
        if(settings.CurrentValue.Enabled)
        {
            globals.Scheduler.Scope.Start();Core.Event.OnMapLoad+=MapLoaded;events.HookEvents();commands.Command();
            globals.Scheduler.NextTick(()=>{string current;try{current=Core.Engine.GlobalVars.MapName;}catch{return;}LoadMap(current);});
        }
        WriteStatus();Core.Logger.LogInformation("LPLK_Zombie 0.1.0 loaded; Enabled={Enabled}",settings.CurrentValue.Enabled);
    }
    public override void Unload()
    {
        loaded=false;if(module is not null)module.Active=false;
        if(globals is not null){globals.Ready=false;globals.Scheduler.Dispose();}
        Core.Event.OnWorldUpdate-=Maintenance;Core.Event.OnMapLoad-=MapLoaded;
        events?.Dispose();commands?.Dispose();services?.Dispose();WriteStatus();
    }
    private void MapLoaded(IOnMapLoadEvent e)=>LoadMap(e.MapName);
    private void LoadMap(string name)
    {
        if(!loaded||!settings.CurrentValue.Enabled)return;
        globals.Ready=false;globals.Scheduler.Advance();map=name;
        try
        {
            // Reject an incomplete new map as a pair; it must not run with half the previous profile.
            profiles.Reload(Path.GetDirectoryName(configPath)!,name,globals.HightDiff);
            globals.CurrentMapIsHighDiff=globals.HightDiff;
            if(globals.HightDiff){globals.KillCount=0;globals.KillPercent=0;globals.HightDiff=false;}
            globals.RiotDay=Math.Clamp(globals.RiotDay,1,profiles.Days.Days.Count);
            globals.AllowHumanZombie=profiles.Days.Days[globals.RiotDay-1].BeforeZombie>0;
            globals.GameStart=false;globals.Ready=true;error=null;commands.UpDateServerCommand();
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {error=e.Message;Core.Logger.LogError(e,"LPLK_Zombie: Mapprofil ungültig; Wellen bleiben angehalten.");}
        WriteStatus();
    }
    private void Maintenance()
    {
        if(!loaded||Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+2000;
        try
        {
            var candidate=ZombieProfile.ReadMain(File.ReadAllText(configPath));string signature=JsonSerializer.Serialize(candidate);
            if(signature!=configSignature)throw new InvalidDataException("Zombie-Konfiguration geändert: kontrollierter Neustart erforderlich; aktive Welle bleibt unverändert.");
            if(error?.StartsWith("Zombie-Konfiguration geändert:")==true)error=null;
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {if(error!=e.Message)Core.Logger.LogWarning(e,"LPLK_Zombie: Letzte gültige Einstellungen bleiben aktiv.");error=e.Message;}
        WriteStatus();
    }
    private void WriteStatus()
    {
        if(globals is null||settings is null)return;
        try{AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{plugin="LPLK_Zombie",version="0.1.0",updated_at=DateTimeOffset.UtcNow,active=loaded,enabled=settings.CurrentValue.Enabled,ready=globals.Ready,map,high=globals.CurrentMapIsHighDiff,day=globals.RiotDay,waves=profiles.Days.Days.Count,zombie_types=profiles.Zombies.ZombieList.Count,killed=globals.ZombieKill,required=globals.NeedKillZombie,timers=globals.Scheduler.Pending,configuration_error=error});}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Zombie: Statusdatei konnte nicht geschrieben werden.");}
    }
    [Command("lplk_zombie_status")]
    public void Status(ICommandContext c){if(c.Sender is {} p&&!commands.Allowed(p))return;c.Reply($"LPLK_Zombie 0.1.0; enabled={settings.CurrentValue.Enabled}; ready={globals.Ready}; day={globals.RiotDay}; waves={profiles.Days.Days.Count}; error={error??"none"}");}
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IHanZriotAPI,HanZriotAPI>("zriot",services!.GetRequiredService<HanZriotAPI>());
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Zombie.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,_)=>loaded&&globals.Ready&&p.IsValid&&!p.IsFakeClient),
            ["open"]=(Action<IPlayer,string>)((p,_)=>OpenPlayer(p))
        });
    }
    private void OpenAdmin(IPlayer player)
    {
        if(!commands.Allowed(player))return;
        module.Menu(player,"Zombie und Wellen",
            ($"Welle: {globals.RiotDay}/{profiles.Days.Days.Count}",OpenAdmin),
            ("Nächste Welle",p=>{if(commands.Allowed(p)&&globals.Ready){commands.SetDay(Math.Min(globals.RiotDay+1,profiles.Days.Days.Count));OpenAdmin(p);}}),
            ("Erste Welle",p=>{if(commands.Allowed(p)&&globals.Ready){commands.SetDay(1);OpenAdmin(p);}}),
            ("Schwierigkeit für nächste Map erhöhen",p=>{if(commands.Allowed(p)&&globals.Ready){globals.KillCount=1000;globals.KillPercent=100;OpenAdmin(p);}}),
            ("Status",p=>p.SendChat($"[LPLK] Zombie: bereit={globals.Ready}; Welle {globals.RiotDay}; Fehler={error??"keine"}")));
    }
    private void OpenPlayer(IPlayer player)
    {
        if(!loaded||!globals.Ready||!player.IsValid||player.IsFakeClient)return;
        var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle("LPLK Zombie").Design.SetMaxVisibleItems(5);
        foreach(var action in new (string Label,Action<IPlayer> Click)[]{($"Welle {globals.RiotDay}/{profiles.Days.Days.Count}",_=>{}),("Wellen-HUD umschalten",p=>globals.PlayerHud[p.PlayerID]=!globals.PlayerHud[p.PlayerID]),("Schadens-HUD umschalten",p=>globals.PlayerDmgHud[p.PlayerID]=!globals.PlayerDmgHud[p.PlayerID])})
        {var button=new SwiftlyS2.Core.Menus.OptionsBase.ButtonMenuOption(action.Label){CloseAfterClick=false};GameThreadMenu.Bind(Core,button,player,()=>loaded&&globals.Ready,p=>{action.Click(p);OpenPlayer(p);});builder.AddOption(button);}
        Core.MenusAPI.OpenMenuForPlayer(player,builder.Build());
    }
}
