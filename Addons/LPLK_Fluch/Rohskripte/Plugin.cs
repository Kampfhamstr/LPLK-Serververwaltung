using SwiftlyS2.Shared.Players;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
namespace LPLK_Fluch;

[PluginMetadata(Id="LPLK_Fluch",Name="LPLK_Fluch",Version="0.1.10",Author="LPLK; reference Kampfhamster",Description="Hühnerfluch mit zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private FluchSettings settings=new();
    private NativeCurseWorld world=null!;
    private CurseController curses=null!;
    private bool ready;
    private long nextStatus;
    private WatchedSettings<FluchSettings> config=null!;
    private bool statusWriteFailed;
    private string? reloadError;
    private int errors;
    private bool Enabled=>module.Active&&ready&&settings.Enabled;
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Fluch",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Fluch","Hühnerfluch","lplk.fluch.admin",OpenAdmin,p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.AdminPermission));
        config=new(module.SettingsPath,FluchSettings.Validate);settings=config.Current;
        world=new(Core,module.CanAdmin);curses=new(world,Error);module.Active=true;ready=hotReload;
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerTeam>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{if(ready)curses.Clear();return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_Fluch 0.1.10 geladen; Hühner={Count}; Dauer={Duration}; Schaden={Damage}",settings.ChickenCount,settings.OrbitDurationSeconds,settings.TotalDamage);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Target.LPLK_Fluch.v1",new(){["actions"]=(Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>>)TargetActions});
    }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){module.Active=false;if(ready)curses.Clear();else curses.Forget();ready=false; LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e){curses.Forget();ready=true;}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){ready=false;curses.Forget();}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e)=>Release(e.PlayerId);
    private void Release(int slot){if(ready)curses.ReleasePlayer(slot);}
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
        if(ready)curses.Tick(Enabled,now/1000d);
    }
    private void Error(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_Fluch: Aktion fehlgeschlagen");}
    private void SyncSettings()
    {
        if(ReferenceEquals(settings,config.Current))return;
        settings=config.Current;if(!settings.Enabled&&ready)curses.Clear();
    }
    private void WriteStatus()
    {
        try
        {
            LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.10",settings,ready,effective=Enabled,activeCurses=curses.Count,chickens=curses.ChickenCount,explosions=curses.Explosions,errors,reloadError});
            statusWriteFailed=false;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Fluch: Status nicht schreibbar.");statusWriteFailed=true;}
    }
}
