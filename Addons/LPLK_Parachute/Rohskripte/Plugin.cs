using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Parachute;

[PluginMetadata(Id = "LPLK_Parachute", Name = "LPLK_Parachute", Version = "0.1.13", Author = "LPLK / schwarper", Description = "Fallschirm mit Benutzen-Aktion; Mechanik aus Parachute v5", MinimumAPIVersion = "1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private WatchedSettings<ParachuteSettings> config = null!;
    private ParachuteSettings settings => config.Current;
    private long nextConfigCheck;
    private bool statusWriteFailed;
    private ModuleSupport module = null!;
    private IConVar<bool>? enabledConvar;
    private sealed record Flight(CHandle<CCSPlayerPawn> Pawn, float OriginalGravity, int Owner);
    private readonly Dictionary<uint, Flight> flights = [];
    private int botDeployments, humanDeployments, takeoverDeployments;
    public override void ConfigureSharedInterface(IInterfaceManager manager) => module.ConfigureSharedInterface(manager);
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Parachute",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module = new(Core, "LPLK_Parachute", "Fallschirm", "lplk.parachute.admin", OpenAdmin);
        config = new(module.SettingsPath,ParachuteSettings.Validate);
        enabledConvar = Core.ConVar.Find<bool>("sv_parachute") ?? Core.ConVar.Create("sv_parachute", "LPLK Fallschirm aktiv", true);
        Core.GameEvent.HookPre<EventPlayerDeath>(e => { if(e.UserIdPlayer is {} p) Release(p.PlayerID); return HookResult.Continue; });
        Core.GameEvent.HookPre<EventPlayerSpawn>(e => { if(e.UserIdPlayer is {} p) Release(p.PlayerID); return HookResult.Continue; });
        module.Active = true;
        Core.Logger.LogInformation("LPLK_Parachute 0.1.13 bereit; Parachute-v5-Mechanik: +use, FallSpeed={Speed}, Decrease={Step}, Linear={Linear}; ursprüngliche Schwerkraft wird zurückgegeben.", settings.FallSpeed, settings.BrakePerTick, settings.Linear);
        WriteStatus();
    }

    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore() { if (module is not null) module.Active = false; ReleaseAll();  LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnMapUnload>] public void MapUnload(IOnMapUnloadEvent e) => ReleaseAll();
    [EventListener<EventDelegates.OnClientDisconnected>] public void Disconnect(IOnClientDisconnectedEvent e) => Release(e.PlayerId);
    private void ReleaseAll() { foreach(uint id in flights.Keys.ToArray()) ReleasePawn(id); takeovers.Clear(); }
    private void Release(int slot) { foreach(var id in flights.Where(x=>x.Value.Owner==slot).Select(x=>x.Key).ToArray())ReleasePawn(id); }
    private void ReleasePawn(uint id)
    {
        if(flights.Remove(id, out var flight) && flight.Pawn.Value is { IsValid: true } pawn)
            pawn.ActualGravityScale = FallPolicy.ReleaseGravity(pawn.ActualGravityScale, flight.OriginalGravity);
    }

    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(module?.Active==true&&Environment.TickCount64>=nextConfigCheck)
        {
            nextConfigCheck=Environment.TickCount64+2000;
            if(config.Reload())ReleaseAll();
            WriteStatus();
        }
        if (module?.Active != true || !settings.Enabled || enabledConvar?.Value != true) { ReleaseAll(); return; }
        HashSet<uint> active = [], seen = [];
        HashSet<(int,ulong,uint)> controlled = [];
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (ControlledPawn(player) is not {} pawn) continue;
            uint id=Core.EntitySystem.GetRefEHandle(pawn).Raw;
            if(!seen.Add(id))continue;
            bool bot=player.IsFakeClient;
            var buttons=(GameButtonFlags)(pawn.MovementServices?.Buttons.ButtonStates[0]??0UL);
            if(!bot&&player.Controller.ControllingBot&&settings.TakeoverRecovery)
            {
                var key=(player.PlayerID,player.SessionId,id);controlled.Add(key);
                CheckTakeover(key,pawn,buttons);
            }
            bool allowed=bot||settings.UsePermission.Length==0||Core.Permission.PlayerHasPermission(player.SteamID,settings.UsePermission);
            if(!ControlPolicy.Deploy(bot,settings.BotsAutoDeploy,(buttons&GameButtonFlags.E)!=0,allowed,
                pawn.LifeState==0&&pawn.Health>0,pawn.GroundEntity.IsValid,pawn.MoveType==MoveType_t.MOVETYPE_WALK,
                pawn.AbsVelocity.Z,settings.DisableWhenCarryingHostage&&pawn.HostageServices?.CarriedHostageProp.Value is not null))continue;
            active.Add(id);
            // Ownership follows the actual controlled pawn, including its entity serial.
            // A takeover may change controller without creating a new pawn/gravity baseline.
            if(flights.TryGetValue(id,out var existing))flights[id]=existing with{Owner=player.PlayerID};
            if(!flights.ContainsKey(id))
            {
                flights[id]=new(new CHandle<CCSPlayerPawn>{Raw=id},pawn.ActualGravityScale,player.PlayerID);
                pawn.ActualGravityScale=FallPolicy.DeployedGravity;
                if(bot)botDeployments++;else if(player.Controller.ControllingBot)takeoverDeployments++;else humanDeployments++;
            }
            pawn.AbsVelocity.Z = FallPolicy.Brake(pawn.AbsVelocity.Z, settings);
        }
        foreach(uint id in flights.Keys.Where(id=>!active.Contains(id)).ToArray()) ReleasePawn(id);
        foreach(var key in takeovers.Keys.Where(k=>!controlled.Contains(k)).ToArray())takeovers.Remove(key);
        TickBotProbe();
    }

    [Command("lplk_parachute", permission: "lplk.parachute.admin")]
    public void Command(ICommandContext ctx)
    {
        if(!ctx.IsSentByPlayer&&ctx.Args.Length==1&&ctx.Args[0]=="probe"){ctx.Reply("LPLK_PARACHUTE_PROBE "+System.Text.Json.JsonSerializer.Serialize(ProbeSnapshot()));return;}
        if(!ctx.IsSentByPlayer&&ctx.Args.Length==1&&ctx.Args[0]=="botfall-test"){ctx.Reply("LPLK_PARACHUTE_TEST "+StartBotProbe());return;}
        if (ctx.IsSentByPlayer && ctx.Sender is { } player) OpenAdmin(player);
        else ctx.ReplyLplk($"LPLK_Parachute 0.1.13: enabled={settings.Enabled && enabledConvar?.Value == true}; fallSpeed={settings.FallSpeed}; decrease={settings.BrakePerTick}; linear={settings.Linear}; gravity=0.1/restored; reloadError={config.Error??"none"}");
    }
    private void WriteStatus()
    {
        try { LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.13",settings.Enabled,settings.FallSpeed,settings.BrakePerTick,settings.Linear,settings.DisableWhenCarryingHostage,settings.BotsAutoDeploy,settings.TakeoverRecovery,useRestricted=settings.UsePermission.Length>0,activeFlights=flights.Count,botDeployments,humanDeployments,takeoverDeployments,takeoverChecks,takeoverRecoveries,takeoverBlocked,recoveryError,botProbeResult,reloadError=config.Error}); statusWriteFailed=false; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        { if(!statusWriteFailed)Core.Logger.LogWarning("LPLK_Parachute: Statusdatei nicht schreibbar ({Error}); Spielregeln bleiben aktiv.",e.GetType().Name);statusWriteFailed=true; }
    }
    private void Apply(Func<ParachuteSettings,ParachuteSettings> edit,IPlayer player)
    {
        ParachuteSettings? before=null;config.Change(s=>{before=s;return edit(s);});ReleaseAll();WriteStatus();
        Core.Logger.LogInformation("LPLK_Parachute: Admin {Actor}; vorher={Before}; nachher={After}",player.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
        player.SendLplkChat("[LPLK] Fallschirm-Einstellung gespeichert.");
    }
}
