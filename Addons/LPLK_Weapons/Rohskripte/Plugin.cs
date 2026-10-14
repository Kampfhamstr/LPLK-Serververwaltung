using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Weapons;

[PluginMetadata(Id="LPLK_Weapons",Name="LPLK_Weapons",Version="0.1.0",Author="LPLK",Description="Waffenfreigaben, Teamquoten und gruppierte Waffenauswahl",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private WeaponSettings settings=new();
    private DateTime stamp;
    private long nextStatus;
    private string? reloadError;
    private int denied,damageBlocked,given,errors;
    private readonly Dictionary<int,long> alerts=[];
    private readonly Dictionary<int,long> lives=[];
    private long generation;
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_Weapons","Waffen & Auswahl","lplk.weapons.admin",OpenAdmin);
        settings=module.ReadSettings<WeaponSettings>(WeaponSettings.Validate);stamp=File.GetLastWriteTimeUtc(module.SettingsPath);module.Active=true;
        Core.GameHooks.Items.CanAcquire.Pre+=Acquire;
        Core.GameHooks.Entities.TakeDamage.Pre+=Damage;
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)lives[p.PlayerID]=++generation;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)lives[p.PlayerID]=++generation;return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_Weapons 0.1.0 geladen: restrictions={Restrictions}; menu={Menu}",settings.RestrictionEnabled,settings.MenuEnabled);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Weapons.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,command)=>command=="sw_guns"&&PlayerAllowed(p))
        });
    }
    public override void Unload(){module.Active=false;Core.GameHooks.Items.CanAcquire.Pre-=Acquire;Core.GameHooks.Entities.TakeDamage.Pre-=Damage;alerts.Clear();}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e){alerts.Remove(e.PlayerId);lives.Remove(e.PlayerId);}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){lives.Clear();generation++;}
    private string WeaponName(CBasePlayerWeapon w)=>Core.Helpers.GetClassnameByDefinitionIndex(w.AttributeManager.Item.ItemDefinitionIndex)??"weapon_unknown";
    private string? Denial(IPlayer player,string id,bool pickup)
    {
        if(WeaponPolicy.Restricted(settings,id))return "Waffe im aktuellen Modus gesperrt";
        if(!settings.Rules.ContainsKey(id))return null;
        bool bypass=settings.Rules.TryGetValue(id,out var rule)&&rule.BypassPermission.Length>0&&!player.IsFakeClient&&Core.Permission.PlayerHasPermission(player.SteamID,rule.BypassPermission);
        var team=Core.PlayerManager.GetAllValidPlayers().Where(p=>p.Controller.IsValid&&p.Controller.TeamNum==player.Controller.TeamNum).ToArray();
        int owned=0;
        foreach(var p in team)
        {
            if(p.PlayerPawn?.WeaponServices is not {} services)continue;
            foreach(var handle in services.MyWeapons)if(handle.Value is {IsValid:true} weapon&&WeaponName(weapon)==id)owned++;
        }
        return WeaponPolicy.Denial(settings,id,pickup,bypass,Core.Engine.GlobalVars.MapName,team.Length,owned);
    }
    private void Alert(IPlayer p,string text)
    {
        if(!settings.ShowRestrictionAlert||p.IsFakeClient)return;
        long now=Environment.TickCount64;if(alerts.TryGetValue(p.PlayerID,out var last)&&now-last<1500)return;
        alerts[p.PlayerID]=now;p.SendAlert("[LPLK] "+text);
    }
    private void Acquire(ref CanAcquireItemPreContext ctx)
    {
        if(!module.Active||!settings.Enabled)return;
        try
        {
            var p=ctx.Params.Player;if(!p.IsValid||!p.Controller.IsValid)return;
            string id=Core.Helpers.GetClassnameByDefinitionIndex(ctx.Params.EconItemView.ItemDefinitionIndex)??"weapon_unknown";
            if(Denial(p,id,ctx.Params.AcquireMethod==AcquireMethod.PickUp) is not {} reason)return;
            denied++;ctx.SetReturn(AcquireResult.NotAllowedByProhibition);ctx.SetHookResult(HookResult.Stop);Alert(p,reason);
        }
        catch(Exception e){Error(e);}
    }
    private void Damage(ref TakeDamageEntityPreContext ctx)
    {
        if(!module.Active||!settings.Enabled||!settings.RestrictionEnabled||ctx.Params.Entity.DesignerName!="player")return;
        try
        {
            var ability=ctx.Params.Info.Ability.Value;
            if(ability is not {IsValid:true}||!ability.DesignerName.StartsWith("weapon_",StringComparison.Ordinal))return;
            if(WeaponPolicy.Restricted(settings,WeaponName(ability.As<CBasePlayerWeapon>()))){ctx.SetHookResult(HookResult.Stop);damageBlocked++;}
        }
        catch(Exception e){Error(e);}
    }
    private void Error(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_Weapons: Waffenaktion fehlgeschlagen");}
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active||Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+2000;
        try
        {
            var changed=File.GetLastWriteTimeUtc(module.SettingsPath);
            if(changed!=stamp){var next=JsonSerializer.Deserialize<WeaponSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();WeaponSettings.Validate(next);settings=next;stamp=changed;reloadError=null;}
        }
        catch(Exception e){if(reloadError is null)Core.Logger.LogWarning(e,"LPLK_Weapons: letzter gültiger Stand bleibt aktiv");reloadError="Ungültige Dateiänderung";}
        WriteStatus();
    }
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.0",settings.Enabled,settings.RestrictionEnabled,settings.ActivePreset,allowed=settings.AllowedWeapons.Length,settings.MenuEnabled,specialRules=settings.Rules.Count,denied,damageBlocked,given,errors,reloadError});
    private void Save(WeaponSettings next)
    {
        WeaponSettings.Validate(next);module.SaveSettings(next);settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;WriteStatus();
    }
    [Command("lplk_weapons")]
    public void Status(ICommandContext c)
    {
        if(c.Sender is {} p){OpenAdmin(p);return;}
        WriteStatus();c.Reply($"LPLK_Weapons 0.1.0: enabled={settings.Enabled}; restrictions={settings.RestrictionEnabled}; menu={settings.MenuEnabled}; preset={settings.ActivePreset}; rules={settings.Rules.Count}; errors={errors}; reloadError={reloadError??"none"}");
    }
}
