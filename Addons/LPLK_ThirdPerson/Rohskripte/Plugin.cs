using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using Vec=System.Numerics.Vector3;

namespace LPLK_ThirdPerson;

[PluginMetadata(Id="LPLK_ThirdPerson",Name="LPLK_ThirdPerson",Version="0.1.4",Author="LPLK; reference criskkky",Description="ThirdPerson-Kamera mit zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private CameraSettings settings=new();
    private CameraController cameras=null!;
    private NativeCameraWorld world=null!;
    private WatchedSettings<CameraSettings> config=null!;
    private bool statusWriteFailed;
    private long nextStatus,lastTick;
    private bool ready;
    private int errors,blocked;
    private string? reloadError;
    private readonly Dictionary<CameraOwner,int> knifeWarnings=[];
    private bool Enabled=>module.Active&&ready&&settings.Enabled&&Core.ConVar.Find<bool>("thirdperson_enabled")?.Value==true;
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_ThirdPerson","Kamera","lplk.thirdperson.admin",OpenAdmin);
        config=new(module.SettingsPath,CameraSettings.Validate);settings=config.Current;
        Core.ConVar.CreateOrFind<bool>("thirdperson_enabled","Enable/Disable ThirdPerson",true,SwiftlyS2.Shared.Convars.ConvarFlags.NONE);
        world=new(Core,()=>settings,()=>cameras?.Leases??Array.Empty<CameraLease>());cameras=new(world);module.Active=true;ready=hotReload;
        Core.GameHooks.Entities.TakeDamage.Pre+=Damage;
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerTeam>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{if(ready)cameras.Clear(Error);knifeWarnings.Clear();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventWeaponFire>(KnifeWarning);
        ReconcileCommand();WriteStatus();Core.Logger.LogInformation("LPLK_ThirdPerson 0.1.4 geladen: permission={Permission}; damage={Damage}",settings.UseTpPermission,settings.DamageMode);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.ThirdPerson.v1",new()
        { ["allowed"]=(Func<IPlayer,string,bool>)((p,cmd)=>cmd=="sw_tp"&&PlayerAllowed(p)) });
    }
    private bool PlayerAllowed(IPlayer p)=>module.Active&&ready&&p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&cameras.Allowed(world.Player(p.PlayerID),settings,Enabled);
    [Command("thirdperson")][CommandAlias("tp")]
    public void Toggle(ICommandContext ctx)=>ToggleCurrent(ctx);
    private void ToggleCurrent(ICommandContext ctx,Func<bool>? stillCurrent=null)
    {
        if(!module.Active)return;
        config.Reload();SyncSettings();reloadError=config.Error;
        if(stillCurrent is not null&&!stillCurrent())return;
        if(ctx.Sender is not {} p){ctx.Reply("LPLK_ThirdPerson: !tp / !thirdperson benötigt einen lebenden Spieler.");return;}
        if(!module.Active||!ready||!p.IsValid||!p.IsAuthorized||p.IsFakeClient)return;
        try{p.SendChat("[LPLK] "+cameras.Toggle(p.PlayerID,settings,Enabled));WriteStatus();}
        catch(Exception e){Error(e);p.SendChat("[LPLK] Kamera konnte nicht sicher umgeschaltet werden.");}
    }
    public override void Unload()
    {
        module.Active=false;RemoveCustomCommand();Core.GameHooks.Entities.TakeDamage.Pre-=Damage;
        if(ready)cameras.Clear(Error);else cameras.Forget();ready=false;knifeWarnings.Clear();
    }
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e){cameras.Forget();knifeWarnings.Clear();ready=true;lastTick=0;}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){ready=false;cameras.Forget();knifeWarnings.Clear();lastTick=0;}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e)=>Release(e.PlayerId);
    private void Release(int slot)
    {
        if(ready)try{cameras.Release(slot);}catch(Exception e){Error(e);}
        foreach(var key in knifeWarnings.Keys.Where(k=>k.Slot==slot).ToArray())knifeWarnings.Remove(key);
    }
    private HookResult KnifeWarning(EventWeaponFire e)
    {
        if(!ready||!module.Active||!settings.EnableKnifeWarnings||e.UserIdPlayer is not {} p||!(e.Weapon.Contains("knife",StringComparison.OrdinalIgnoreCase)||e.Weapon.Contains("bayonet",StringComparison.OrdinalIgnoreCase)))return HookResult.Continue;
        if(world.Player(p.PlayerID) is {} snapshot&&cameras.Owns(snapshot))
        {
            int count=knifeWarnings.GetValueOrDefault(snapshot.Owner);if(count<3){knifeWarnings[snapshot.Owner]=count+1;p.SendChat("[LPLK] Messerangriffe können in ThirdPerson durch die Kameraposition abweichen. Mit !tp zur Egoansicht wechseln.");}
        }
        return HookResult.Continue;
    }
    private void Damage(ref TakeDamageEntityPreContext ctx)
    {
        if(!module.Active||!ready||cameras.Count==0||ctx.Params.Entity.DesignerName!="player")return;
        try
        {
            var attacker=ctx.Params.Info.Attacker.Value;if(attacker is not {IsValid:true}||attacker.DesignerName!="player")return;
            var p=Core.PlayerManager.GetPlayerFromPawn(attacker.As<CCSPlayerPawn>());
            if(p is null||world.Player(p.PlayerID) is not {} a||!cameras.Owns(a))return;
            var target=ctx.Params.Entity.As<CCSPlayerPawn>();if(target.AbsOrigin is not {} origin)return;
            if(CameraMath.BlockDamage(settings.DamageMode,a.Origin,new Vec(origin.X,origin.Y,origin.Z),a.Yaw)){ctx.SetHookResult(HookResult.Stop);blocked++;}
        }
        catch(Exception e){Error(e);ctx.SetHookResult(HookResult.Stop);}
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active)return;
        long now=Environment.TickCount64;float delta=lastTick==0?1f/64:Math.Clamp((now-lastTick)/1000f,0,.1f);lastTick=now;
        if(now>=nextStatus)
        {
            nextStatus=now+2000;
            config.Reload();SyncSettings();reloadError=config.Error;
            WriteStatus();
        }
        if(ready)cameras.Tick(settings,Enabled,delta,Error);
    }
    private void Error(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_ThirdPerson: Kameraaktion fehlgeschlagen");}
    private void SyncSettings(){settings=config.Current;ReconcileCommand();}
    private void WriteStatus()
    {
        try
        {
            AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.4",settings,ready,effective=Enabled,activeCameras=cameras.Count,blocked,errors,reloadError,commandError,command=activeCommandName.Length==0?null:"sw_"+activeCommandName});
            statusWriteFailed=false;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_ThirdPerson: Status nicht schreibbar.");statusWriteFailed=true;}
    }
}
