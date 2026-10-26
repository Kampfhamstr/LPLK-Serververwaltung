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

[PluginMetadata(Id="LPLK_Weapons",Name="LPLK_Weapons",Version="0.2.10",Author="LPLK",Description="Waffenfreigaben, Teamquoten und gruppierte Waffenauswahl",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private WeaponSettings settings=new();
    private WatchedSettings<WeaponSettings> config=null!;
    private bool statusWriteFailed;
    private long nextStatus;
    private string? reloadError;
    private int denied,damageBlocked,given,errors;
    private readonly Dictionary<int,long> alerts=[];
    private readonly Dictionary<int,long> lives=[];
    private long generation;
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Weapons",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Weapons","Waffen","lplk.weapons.admin",OpenAdmin);
        config=new(module.SettingsPath,WeaponSettings.Validate);settings=config.Current;module.Active=true;nativeReady=hotReload;
        Core.GameHooks.Items.CanAcquire.Pre+=Acquire;
        Core.GameEvent.HookPost<EventWeaponFire>(WeaponFire);Core.GameEvent.HookPost<EventItemEquip>(WeaponEquip);
        Core.GameHooks.Entities.TakeDamage.Pre+=Damage;
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)lives[p.PlayerID]=++generation;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)lives[p.PlayerID]=++generation;return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_Weapons 0.2.10 geladen: restrictions={Restrictions}; menu={Menu}",settings.RestrictionEnabled,settings.MenuEnabled);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Weapons.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,command)=>command=="sw_guns"&&PlayerAllowed(p))
        });
    }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){ResetAmmo();nativeReady=false;module.Active=false;Core.GameHooks.Items.CanAcquire.Pre-=Acquire;Core.GameHooks.Entities.TakeDamage.Pre-=Damage;alerts.Clear(); LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e){alerts.Remove(e.PlayerId);lives.Remove(e.PlayerId);reloadRequests.Remove(e.PlayerId);}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){ResetAmmo();nativeReady=false;lives.Clear();generation++;}
    private string WeaponName(CBasePlayerWeapon w)=>Core.Helpers.GetClassnameByDefinitionIndex(w.AttributeManager.Item.ItemDefinitionIndex)??"weapon_unknown";
    private string? Denial(IPlayer player,string id,bool pickup)
    {
        if(WeaponPolicy.Restricted(settings,id))return "Waffe im aktuellen Modus gesperrt";
        if(!settings.Rules.ContainsKey(id))return null;
        bool bypass=settings.Rules.TryGetValue(id,out var rule)&&!player.IsFakeClient&&(rule.BypassPermission.Length>0&&Core.Permission.PlayerHasPermission(player.SteamID,rule.BypassPermission)||rule.BypassPermissions.Length>0&&rule.BypassPermissions.All(flag=>Core.Permission.PlayerHasPermission(player.SteamID,flag)));
        // Ammo-only rules do not require walking every teammate's inventory.
        if(bypass&&rule!.BypassQuota||rule!.TeamQuota.Count==0&&rule.MapQuota.Count==0)
            return WeaponPolicy.Denial(settings,id,pickup,bypass,"",0,0);
        if(rule.BlockUsing&&!(pickup&&rule.AllowPickupWhenBlocked)&&!bypass)return "Waffe gesperrt";
        var team=Core.PlayerManager.GetAllValidPlayers().Where(p=>p.Controller.IsValid&&p.Controller.TeamNum==player.Controller.TeamNum).ToArray();
        int owned=0;
        foreach(var p in team)
        {
            if(p.PlayerPawn?.WeaponServices is not {} services)continue;
            foreach(var handle in services.MyWeapons)if(handle.Value is {IsValid:true} weapon&&WeaponName(weapon)==id){int slot=WeaponPolicy.GrenadeAmmoSlot(id);owned+=slot<0?1:Math.Max((int)services.Ammo[slot],1);}
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
        RefreshSettings();
        WriteStatus();
    }
    private static bool Equivalent<T>(T first,T second)=>System.Text.Json.Nodes.JsonNode.DeepEquals(JsonSerializer.SerializeToNode(first),JsonSerializer.SerializeToNode(second));
    private void RefreshSettings()
    {
        if(config is null)return;config.Reload();var next=config.Current;
        if(!Equivalent(settings,next))ResetAmmo();settings=next;reloadError=config.Error;
    }
    private void WriteStatus()
    {
        try { LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.2.10",settings.Enabled,settings.RestrictionEnabled,settings.ActivePreset,allowed=settings.AllowedWeapons.Length,settings.MenuEnabled,specialRules=settings.Rules.Count,denied,damageBlocked,given,errors,reloadError}); statusWriteFailed=false; }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning("LPLK_Weapons: Status nicht schreibbar ({Error}); Regeln bleiben aktiv.",e.GetType().Name);statusWriteFailed=true;}
    }
    private void Save(Func<WeaponSettings,WeaponSettings> edit)
    {
        RefreshSettings();if(!module.Active)throw new InvalidOperationException("Modul entladen.");
        var next=config.Change(current=>edit(JsonSerializer.Deserialize<WeaponSettings>(JsonSerializer.Serialize(current))!));
        if(!Equivalent(settings,next))ResetAmmo();settings=next;reloadError=null;WriteStatus();
    }
    [Command("lplk_weapons")]
    public void Status(ICommandContext c)
    {
        if(c.Sender is {} p){OpenAdmin(p);return;}
        RefreshSettings();if(c.Args is ["ammoprobe"]){AmmoProbe(c);return;}WriteStatus();c.ReplyLplk($"LPLK_Weapons 0.2.10: enabled={settings.Enabled}; restrictions={settings.RestrictionEnabled}; menu={settings.MenuEnabled}; preset={settings.ActivePreset}; rules={settings.Rules.Count}; errors={errors}; reloadError={reloadError??"none"}");
    }
}
