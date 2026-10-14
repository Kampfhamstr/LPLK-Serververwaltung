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

[PluginMetadata(Id="LPLK_Fluch",Name="LPLK_Fluch",Version="0.1.1",Author="LPLK; reference Kampfhamster",Description="Hühnerfluch mit zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private FluchSettings settings=new();
    private NativeCurseWorld world=null!;
    private CurseController curses=null!;
    private bool ready;
    private long nextStatus;
    private DateTime stamp;
    private string? reloadError;
    private int errors;
    private bool Enabled=>module.Active&&ready&&settings.Enabled;
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_Fluch","Hühnerfluch","lplk.fluch.admin",OpenAdmin,p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.AdminPermission));
        settings=module.ReadSettings<FluchSettings>(FluchSettings.Validate);stamp=File.GetLastWriteTimeUtc(module.SettingsPath);
        world=new(Core,module.CanAdmin);curses=new(world,Error);module.Active=true;ready=hotReload;
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerTeam>(e=>{if(e.UserIdPlayer is {} p)Release(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{if(ready)curses.Clear();return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_Fluch 0.1.1 geladen; Hühner={Count}; Dauer={Duration}; Schaden={Damage}",settings.ChickenCount,settings.OrbitDurationSeconds,settings.TotalDamage);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Target.LPLK_Fluch.v1",new(){["actions"]=(Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>>)TargetActions});
    }
    public override void Unload(){module.Active=false;if(ready)curses.Clear();else curses.Forget();ready=false;}
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
            try
            {
                var changed=File.GetLastWriteTimeUtc(module.SettingsPath);
                if(changed!=stamp){var next=JsonSerializer.Deserialize<FluchSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();FluchSettings.Validate(next);settings=next;stamp=changed;reloadError=null;}
            }
            catch(Exception e){if(reloadError is null)Core.Logger.LogWarning(e,"LPLK_Fluch: letzter gültiger Stand bleibt aktiv");reloadError="Ungültige Änderung; letzter gültiger Stand aktiv.";}
            WriteStatus();
        }
        if(ready)curses.Tick(Enabled,now/1000d);
    }
    private void Error(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_Fluch: Aktion fehlgeschlagen");}
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.1",settings,ready,effective=Enabled,activeCurses=curses.Count,chickens=curses.ChickenCount,explosions=curses.Explosions,errors,reloadError});
}
