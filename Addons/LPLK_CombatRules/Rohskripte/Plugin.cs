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

namespace LPLK_CombatRules;

[PluginMetadata(Id="LPLK_CombatRules",Name="LPLK_CombatRules",Version="0.1.10",Author="LPLK; reference Michael Zauche",Description="Headshot-Regeln, Goldene Regel und Schadensregeln",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private CombatSettings settings=new();
    private WatchedSettings<CombatSettings> config=null!;
    private bool statusWriteFailed;
    private long nextStatus;
    private string? reloadError;
    private int headshotsBlocked,goldenBlocked,modified,errors;
    private readonly Dictionary<int,long> alerts=[];
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_CombatRules",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_CombatRules","Kampfregeln","lplk.combat.admin",OpenAdmin);
        config=new(module.SettingsPath,CombatSettings.Validate);settings=config.Current;module.Active=true;
        Core.GameHooks.Entities.TakeDamage.Pre+=Damage;
        Core.GameEvent.HookPost<EventRoundStart>(_=>{Announce();return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_CombatRules 0.1.10 geladen: headshot={Headshot}; golden={Golden}",settings.HeadshotOnly,settings.GoldenRule);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){module.Active=false;Core.GameHooks.Entities.TakeDamage.Pre-=Damage;alerts.Clear(); LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnected(IOnClientDisconnectedEvent e)=>alerts.Remove(e.PlayerId);
    private (int Ct,int T,bool? Warmup,bool? Ffa) Match()
    {
        var rules=Core.EntitySystem.GetGameRules();
        if(rules is null)return(-1,-1,null,null);
        var match=Core.Game.MatchData;
        return(match.CTScoreTotal,match.TerroristScoreTotal,rules.WarmupPeriod,Core.ConVar.Find<bool>("mp_teammates_are_enemies")?.Value);
    }
    private void Damage(ref TakeDamageEntityPreContext ctx)
    {
        if(!module.Active||!settings.Enabled||ctx.Params.Entity.DesignerName!="player")return;
        try
        {
            var info=ctx.Params.Info;var ability=info.Ability.Value;
            if(ability is not {IsValid:true}||!ability.DesignerName.StartsWith("weapon_",StringComparison.Ordinal))return;
            string id=Core.Helpers.GetClassnameByDefinitionIndex(ability.As<CBasePlayerWeapon>().AttributeManager.Item.ItemDefinitionIndex)??"weapon_unknown";
            if(settings.GoldenRule&&settings.GoldenWeapons.Contains(id)&&info.Attacker.Value is {IsValid:true,DesignerName:"player"} attacker)
            {
                var p=Core.PlayerManager.GetPlayerFromPawn(attacker.As<CBasePlayerPawn>());
                if(p is {IsValid:true}&&p.Controller.IsValid&&p.PlayerPawn is {IsValid:true} pawn)
                {
                    var m=Match();
                    // Preserve the original rule: scope is read at damage time;
                    // zoom itself is allowed. MatchData follows the current sides.
                    if(CombatPolicy.BlockGolden(settings,id,p.Controller.TeamNum,m.Ct,m.T,pawn.IsScoped,m.Warmup,m.Ffa))
                    {
                        ctx.SetHookResult(HookResult.Stop);goldenBlocked++;Alert(p);return;
                    }
                }
            }
            if(CombatPolicy.BlockHeadshot(settings,id,info.ActualHitGroup==HitGroup_t.HITGROUP_HEAD))
            {ctx.SetHookResult(HookResult.Stop);headshotsBlocked++;return;}
            if(settings.Rules.TryGetValue(id,out var rule)&&!string.IsNullOrEmpty(rule.Damage))
            {
                float next=DamageFormula.Apply(info.Damage,rule.Damage);
                if(float.IsFinite(next)){info.Damage=next;modified++;}
                // DamageCustom is an engine damage-type field, not damage amount.
            }
        }
        catch(Exception e){if(errors++==0)Core.Logger.LogError(e,"LPLK_CombatRules: Treffer konnte nicht ausgewertet werden");}
    }
    private void Alert(IPlayer p)
    {
        if(!settings.ShowBlockedShotAlert||p.IsFakeClient)return;
        long now=Environment.TickCount64;if(alerts.TryGetValue(p.PlayerID,out var last)&&now-last<1200)return;
        alerts[p.PlayerID]=now;p.SendAlert("[LPLK] Goldene Regel: führendes Team mit dieser Waffe nur No-Scope.");
    }
    private void Announce()
    {
        if(!module.Active||!settings.Enabled||!settings.GoldenRule||!settings.AnnounceAtRoundStart)return;
        try
        {
            var m=Match();int team=CombatPolicy.Leader(m.Ct,m.T,settings.MinimumLeadRounds);
            if(team==0||settings.GoldenWeapons.Length==0||!CombatPolicy.BlockGolden(settings,settings.GoldenWeapons[0],team,m.Ct,m.T,true,m.Warmup,m.Ffa))return;
            string weapons=string.Join(" / ",settings.GoldenWeapons.Select(id=>WeaponCatalog.Find(id)!.Name));
            foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(p=>!p.IsFakeClient))p.SendLplkChat($"[LPLK] Goldene Regel: {(team==3?"CT":"T")} führt {m.Ct}:{m.T} (CT:T) – {weapons} nur No-Scope.");
        }
        catch(Exception e){if(errors++==0)Core.Logger.LogWarning(e,"LPLK_CombatRules: Spielstand nicht verfügbar");}
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active||Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+2000;
        RefreshSettings();
        WriteStatus();
    }
    private void RefreshSettings(){if(config is null)return;config.Reload();settings=config.Current;reloadError=config.Error;}
    private void WriteStatus()
    {
        try { LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.10",settings.Enabled,settings.HeadshotOnly,settings.FirearmsOnly,settings.GoldenRule,settings.GoldenWeapons,settings.MinimumLeadRounds,settings.EnabledInWarmup,specialRules=settings.Rules.Count,headshotsBlocked,goldenBlocked,modified,errors,reloadError}); statusWriteFailed=false; }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning("LPLK_CombatRules: Status nicht schreibbar ({Error}); Spielregeln bleiben aktiv.",e.GetType().Name);statusWriteFailed=true;}
    }
    private void Save(Func<CombatSettings,CombatSettings> edit)
    {
        RefreshSettings();if(!module.Active)throw new InvalidOperationException("Modul entladen.");
        settings=config.Change(current=>edit(JsonSerializer.Deserialize<CombatSettings>(JsonSerializer.Serialize(current))!));reloadError=null;WriteStatus();
    }
    [Command("lplk_combatrules")]
    public void Status(ICommandContext c)
    {
        if(c.Sender is {} p){OpenAdmin(p);return;}
        RefreshSettings();WriteStatus();c.ReplyLplk($"LPLK_CombatRules 0.1.10: enabled={settings.Enabled}; headshot={settings.HeadshotOnly}; golden={settings.GoldenRule}; lead={settings.MinimumLeadRounds}; rules={settings.Rules.Count}; errors={errors}; reloadError={reloadError??"none"}");
    }
}
