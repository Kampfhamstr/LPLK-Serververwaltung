using System.Diagnostics;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_AntiTeamFlash;

[PluginMetadata(Id="LPLK_AntiTeamFlash",Name="LPLK_AntiTeamFlash",Version="0.1.0",Author="LPLK; reference St1nixx",Description="Teamflash-Schutz mit zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private ModuleSupport module = null!;
    private FlashSettings settings = new();
    private readonly FlashHistory history = new();
    private DateTime stamp;
    private double nextStatus;
    private bool ready;
    private string? reloadError;
    private int cleared, preserved, errors;

    public override void Load(bool hotReload)
    {
        module = new(Core,"LPLK_AntiTeamFlash","Teamflash-Schutz","lplk.antiteamflash.admin",OpenAdmin);
        settings = module.ReadSettings<FlashSettings>(FlashSettings.Validate);
        stamp = File.GetLastWriteTimeUtc(module.SettingsPath);
        module.Active = true;
        ready = !hotReload; // Existing flashes have unknown origin after a hotload.
        Core.GameEvent.HookPost<EventPlayerBlind>(OnBlind);
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{history.Clear();ready=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)history.Remove(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)history.Remove(p.PlayerID);return HookResult.Continue;});
        WriteStatus();
        Core.Logger.LogInformation("LPLK_AntiTeamFlash 0.1.0 geladen: enabled={Enabled}; flashOwner={Owner}; Schutzrecht={Permission}; hotloadWait={Wait}",settings.Enabled,settings.FlashOwner,settings.AccessPermission,!ready);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload(){if(module is not null)module.Active=false;ready=false;history.Clear();}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e)=>history.Remove(e.PlayerId);
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){history.Clear();ready=false;}
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e){history.Clear();ready=true;}

    private HookResult OnBlind(EventPlayerBlind e)
    {
        if(module?.Active!=true || !ready)return HookResult.Continue;
        try
        {
            var victim=e.UserIdPlayer;
            if(victim is not {IsValid:true,IsAlive:true} || victim.PlayerPawn is not {IsValid:true} pawn || !victim.Controller.IsValid)return HookResult.Continue;
            var attacker=e.Accessor.GetPlayerController("attacker");
            int victimTeam=victim.Controller.TeamNum;
            bool validAttacker=attacker is {IsValid:true};
            bool samePlayer=validAttacker && attacker!.Index==victim.Controller.Index;
            bool permission=settings.AccessPermission.Length==0 || Core.Permission.PlayerHasPermission(victim.SteamID,settings.AccessPermission);
            bool? ffa=Core.ConVar.Find<bool>("mp_teammates_are_enemies")?.Value;
            bool suppress=validAttacker && FlashPolicy.Suppress(settings,victimTeam,attacker!.TeamNum,samePlayer,permission,ffa);
            // Capture every valid victim event, including disabled/missing-attacker
            // cases; toggling protection must not clear an existing allowed flash.
            double duration=Math.Max(e.BlindDuration,pawn.FlashDuration);
            var identity=new FlashIdentity(victim.PlayerID,victim.SessionId,pawn.Index,victimTeam);
            bool clear=history.ShouldClear(identity,suppress,Core.Engine.GlobalVars.CurrentTime,duration);
            if(clear)
            {
                pawn.FlashDuration=0; pawn.FlashMaxAlpha=0;
                pawn.FlashDurationUpdated(); pawn.FlashMaxAlphaUpdated();
                cleared++;
            }
            else if(suppress)preserved++;
        }
        catch(Exception ex)
        {
            if(errors++==0)Core.Logger.LogError(ex,"LPLK_AntiTeamFlash: Ereignis konnte nicht sicher ausgewertet werden; Schutz bis zur nächsten Runde ausgesetzt.");
            ready=false;history.Clear();
        }
        return HookResult.Continue;
    }

    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(module?.Active!=true)return;
        double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        if(now<nextStatus)return;nextStatus=now+2;
        try
        {
            var modified=File.GetLastWriteTimeUtc(module.SettingsPath);
            if(modified!=stamp)
            {
                var next=JsonSerializer.Deserialize<FlashSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();
                FlashSettings.Validate(next);settings=next;stamp=modified;reloadError=null;
                // Keep flash history across configuration changes.
            }
        }
        catch(Exception ex)
        {
            if(reloadError is null)Core.Logger.LogWarning(ex,"LPLK_AntiTeamFlash: ungültige Dateiänderung; letzter gültiger Stand bleibt aktiv.");
            reloadError="Ungültige Änderung; letzter gültiger Stand aktiv.";
        }
        WriteStatus();
    }
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.0",settings,ready,cleared,preservedOverlaps=preserved,errors,tracked=history.Count,reloadError});
}
