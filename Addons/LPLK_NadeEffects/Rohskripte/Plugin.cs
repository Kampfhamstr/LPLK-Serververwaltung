using SwiftlyS2.Shared.Players;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
namespace LPLK_NadeEffects;

[PluginMetadata(Id="LPLK_NadeEffects",Name="LPLK_NadeEffects",Version="0.1.3",Author="LPLK; reference Kampfhamster",Description="Gezielte Granatenhinweise und Entzug mit gemeinsamer Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private NadeSettings settings=new();
    private NativeNadeWorld world=null!;
    private NadeController effects=null!;
    private bool ready;
    private long nextStatus;
    private WatchedSettings<NadeSettings> config=null!;
    private bool statusWriteFailed;
    private int errors;
    private string? reloadError;
    private bool Enabled=>module.Active&&ready&&settings.Enabled;
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_NadeEffects","Granateneffekte","lplk.nadeeffects.admin",OpenAdmin,p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.AdminPermission));
        config=new(module.SettingsPath,NadeSettings.Validate);settings=config.Current;
        world=new(Core,module.CanAdmin,Notify);effects=new(world,Error);module.Active=true;ready=hotReload;
        Core.GameEvent.HookPost<EventItemEquip>(Equip);
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerTeam>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{if(ready)effects.CancelEffects();return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_NadeEffects 0.1.3 geladen; enabled={Enabled}; selected=0",settings.Enabled);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Target.LPLK_NadeEffects.v1",new(){["actions"]=(Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>>)TargetActions});
    }
    public override void Unload(){module.Active=false;if(ready)effects.Clear();else effects.ForgetMap();ready=false;}
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e){effects.ForgetMap();ready=true;}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){ready=false;effects.ForgetMap();}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e){if(ready)effects.Disconnect(e.PlayerId);}
    private void Release(int slot){if(ready)effects.ReleaseEffects(slot);}
    private HookResult Equip(EventItemEquip e)
    {
        if(!Enabled||e.UserIdPlayer is not {} p)return HookResult.Continue;
        try{effects.Equip(p.PlayerID,e.Item,settings,Environment.TickCount64/1000d);}catch(Exception ex){Error(ex);}
        return HookResult.Continue;
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active)return;long now=Environment.TickCount64;
        if(now>=nextStatus)
        {
            nextStatus=now+2000;
            config.Reload();SyncSettings();reloadError=config.Error;
            WriteStatus();
        }
        if(ready)try{effects.Tick(Enabled,now/1000d);}catch(Exception e){effects.CancelEffects();Error(e);}
    }
    private void Notify(string text,NadeSettings s)
    {
        foreach(var p in Core.PlayerManager.GetAllValidPlayers())if(p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&Core.Permission.PlayerHasPermission(p.SteamID,s.AdminMessagePermission))p.SendChat("[LPLK Admin] "+text);
    }
    private void Error(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_NadeEffects: Aktion fehlgeschlagen");}
    private void SyncSettings()
    {
        if(ReferenceEquals(settings,config.Current))return;
        settings=config.Current;if(ready)effects.CancelEffects();
    }
    private void WriteStatus()
    {
        try
        {
            AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.3",settings,ready,effective=Enabled,selected=effects.Selected,pending=effects.PendingCount,fades=effects.FadeCount,triggered=effects.Triggered,removed=effects.Removed,errors,reloadError});
            statusWriteFailed=false;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_NadeEffects: Status nicht schreibbar.");statusWriteFailed=true;}
    }
}
