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

[PluginMetadata(Id = "LPLK_Parachute", Name = "LPLK_Parachute", Version = "0.1.6", Author = "LPLK / schwarper", Description = "Fallschirm mit Benutzen-Aktion; Mechanik aus Parachute v5", MinimumAPIVersion = "1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private WatchedSettings<ParachuteSettings> config = null!;
    private ParachuteSettings settings => config.Current;
    private long nextConfigCheck;
    private bool statusWriteFailed;
    private ModuleSupport module = null!;
    private IConVar<bool>? enabledConvar;
    private sealed record Flight(CHandle<CCSPlayerPawn> Pawn, float OriginalGravity);
    private readonly Dictionary<int, Flight> flights = [];
    public override void ConfigureSharedInterface(IInterfaceManager manager) => module.ConfigureSharedInterface(manager);
    public override void Load(bool hotReload)
    {
        module = new(Core, "LPLK_Parachute", "Fallschirm", "lplk.parachute.admin", OpenAdmin);
        config = new(module.SettingsPath,ParachuteSettings.Validate);
        enabledConvar = Core.ConVar.Find<bool>("sv_parachute") ?? Core.ConVar.Create("sv_parachute", "LPLK Fallschirm aktiv", true);
        Core.GameEvent.HookPre<EventPlayerDeath>(e => { if(e.UserIdPlayer is {} p) Release(p.PlayerID); return HookResult.Continue; });
        Core.GameEvent.HookPre<EventPlayerSpawn>(e => { if(e.UserIdPlayer is {} p) Release(p.PlayerID); return HookResult.Continue; });
        module.Active = true;
        Core.Logger.LogInformation("LPLK_Parachute 0.1.6 bereit; Parachute-v5-Mechanik: +use, FallSpeed={Speed}, Decrease={Step}, Linear={Linear}; ursprüngliche Schwerkraft wird zurückgegeben.", settings.FallSpeed, settings.BrakePerTick, settings.Linear);
        WriteStatus();
    }

    public override void Unload() { if (module is not null) module.Active = false; ReleaseAll(); }
    [EventListener<EventDelegates.OnMapUnload>] public void MapUnload(IOnMapUnloadEvent e) => ReleaseAll();
    [EventListener<EventDelegates.OnClientDisconnected>] public void Disconnect(IOnClientDisconnectedEvent e) => Release(e.PlayerId);
    private void ReleaseAll() { foreach(int id in flights.Keys.ToArray()) Release(id); }
    private void Release(int id)
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
        HashSet<int> active = [];
        foreach (var player in Core.PlayerManager.GetAllPlayers())
        {
            if (!player.IsValid || !player.IsAlive || (player.PressedButtons & GameButtonFlags.E) == 0 ||
                player.PlayerPawn is not { IsValid: true } pawn || pawn.GroundEntity.IsValid ||
                pawn.MoveType != MoveType_t.MOVETYPE_WALK || pawn.AbsVelocity.Z >= 0) continue;
            if (settings.UsePermission.Length > 0 && !Core.Permission.PlayerHasPermission(player.SteamID, settings.UsePermission)) continue;
            if (settings.DisableWhenCarryingHostage && pawn.HostageServices?.CarriedHostageProp.Value is not null) continue;
            int id=player.PlayerID; active.Add(id);
            // CHandle includes the entity serial: never restore a previous pawn's gravity onto a respawn.
            if(flights.TryGetValue(id,out var existing) && (existing.Pawn.Value is not { IsValid: true } previous || previous.Index!=pawn.Index)) Release(id);
            if(!flights.ContainsKey(id))
            {
                flights[id]=new(new CHandle<CCSPlayerPawn>{Value=pawn},pawn.ActualGravityScale);
                pawn.ActualGravityScale=FallPolicy.DeployedGravity;
            }
            pawn.AbsVelocity.Z = FallPolicy.Brake(pawn.AbsVelocity.Z, settings);
        }
        foreach(int id in flights.Keys.Where(id=>!active.Contains(id)).ToArray()) Release(id);
    }

    [Command("lplk_parachute", permission: "lplk.parachute.admin")]
    public void Command(ICommandContext ctx)
    {
        if (ctx.IsSentByPlayer && ctx.Sender is { } player) OpenAdmin(player);
        else ctx.Reply($"LPLK_Parachute 0.1.6: enabled={settings.Enabled && enabledConvar?.Value == true}; fallSpeed={settings.FallSpeed}; decrease={settings.BrakePerTick}; linear={settings.Linear}; gravity=0.1/restored; reloadError={config.Error??"none"}");
    }
    private void WriteStatus()
    {
        try { AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.6",settings.Enabled,settings.FallSpeed,settings.BrakePerTick,settings.Linear,settings.DisableWhenCarryingHostage,useRestricted=settings.UsePermission.Length>0,activeFlights=flights.Count,reloadError=config.Error}); statusWriteFailed=false; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        { if(!statusWriteFailed)Core.Logger.LogWarning("LPLK_Parachute: Statusdatei nicht schreibbar ({Error}); Spielregeln bleiben aktiv.",e.GetType().Name);statusWriteFailed=true; }
    }
    private void Apply(Func<ParachuteSettings,ParachuteSettings> edit,IPlayer player)
    {
        ParachuteSettings? before=null;config.Change(s=>{before=s;return edit(s);});ReleaseAll();WriteStatus();
        Core.Logger.LogInformation("LPLK_Parachute: Admin {Actor}; vorher={Before}; nachher={After}",player.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
        player.SendChat("[LPLK] Fallschirm-Einstellung gespeichert.");
    }
}
