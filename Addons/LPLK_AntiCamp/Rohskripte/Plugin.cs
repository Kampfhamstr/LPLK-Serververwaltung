using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_AntiCamp;

[PluginMetadata(Id="LPLK_AntiCamp",Name="LPLK_AntiCamp",Version="0.1.12",Author="LPLK",Description="AntiCamp mit echten Zeitabständen und zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private WatchedSettings<CampSettings> config=null!;
    private CampSettings settings=>config.Current;
    private bool statusWriteFailed;
    private sealed record Entry(ulong Session,int Pawn,int Team,CampClock Clock);
    private readonly Dictionary<int,Entry> states=[];
    private bool running,bombMap,hostageMap,planted,dropped;
    private double nextCheck,nextReload;
    private string? reloadError=>config.Error;
    private int warnings,punishments;
    private static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;

    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_AntiCamp",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_AntiCamp","AntiCamp","lplk.anticamp.admin",OpenAdmin);
        config=new(module.SettingsPath,CampSettings.Validate);module.Active=true;
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{Reset();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundStart>(_=>{Reset();Objectives();running=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundEnd>(_=>{Reset();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombPlanted>(_=>{planted=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombDropped>(_=>{dropped=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombPickup>(_=>{dropped=false;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)states.Remove(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)states.Remove(p.PlayerID);return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_AntiCamp 0.1.12 geladen: CampTime={Time}, Radius={Radius}, Warnung={Warning}s; beim Hotload erst ab nächster Runde aktiv.",settings.CampTime,settings.CampCheckRadius,settings.WarningSeconds);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){if(module is not null)module.Active=false;Reset(); LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnMapUnload>] public void MapUnload(IOnMapUnloadEvent e)=>Reset();
    [EventListener<EventDelegates.OnClientDisconnected>] public void Disconnect(IOnClientDisconnectedEvent e)=>states.Remove(e.PlayerId);
    private void Reset(){states.Clear();running=planted=dropped=false;bombMap=hostageMap=false;}
    private void Objectives()
    {
        bombMap=Core.EntitySystem.GetAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").Any(x=>x.IsValid);
        hostageMap=Core.EntitySystem.GetAllEntitiesByDesignerName<CHostage>("hostage_entity").Any(x=>x.IsValid);
    }
    [EventListener<EventDelegates.OnTick>] public void Tick()
    {
        if(module?.Active!=true)return;double now=Now;if(now<nextCheck)return;nextCheck=now+1;
        if(now>=nextReload){nextReload=now+2;Reload();WriteStatus();}
        if(!settings.PluginEnabled||!running){states.Clear();return;}
        var rules=Core.EntitySystem.GetGameRules();
        if(rules is null||rules.WarmupPeriod||rules.FreezePeriod){states.Clear();return;}
        var alive=Core.PlayerManager.GetAlive().Where(p=>p.IsValid&&p.IsAlive&&p.Controller.TeamNum is 2 or 3).ToArray();
        if(!alive.Any(p=>p.Controller.TeamNum==2)||!alive.Any(p=>p.Controller.TeamNum==3)){states.Clear();return;}
        HashSet<int> seen=[];
        foreach(var p in alive){
            int id=p.PlayerID;seen.Add(id);int team=p.Controller.TeamNum;
            if(CampPolicy.Exempt(team,bombMap,hostageMap,planted,dropped,settings)){states.Remove(id);continue;}
            var pawn=p.PlayerPawn;if(pawn is not {IsValid:true}||pawn.AbsOrigin is not {} pos){states.Remove(id);continue;}
            var position=new Vector3(pos.X,pos.Y,pos.Z);var angles=new Vector2(pawn.EyeAngles.Pitch,pawn.EyeAngles.Yaw);
            if(!float.IsFinite(position.X)||!float.IsFinite(position.Y)||!float.IsFinite(position.Z)||!float.IsFinite(angles.X)||!float.IsFinite(angles.Y)){states.Remove(id);continue;}
            if(!states.TryGetValue(id,out var entry)||entry.Session!=p.SessionId||entry.Pawn!=(int)pawn.Index||entry.Team!=team||entry.Clock.HasMoved(position,settings.CampCheckRadius)){
                states[id]=new(p.SessionId,(int)pawn.Index,team,new(position,angles));continue;
            }
            var decision=entry.Clock.Step(now,position,angles,settings);
            if(decision.Announce&&!settings.DisableChatMessages)Core.PlayerManager.SendLplkChat($"[LPLK] AntiCamp: {p.Name} campt – Position verlassen!");
            if(decision.Warn){warnings++;string text=$"ANTI-CAMP: Bewege dich! Strafe in {settings.WarningSeconds} Sekunden!";p.SendCenter(text);p.SendAlert(text);p.SendLplkChat("[LPLK] "+text);p.ExecuteCommand("play sounds/tools/sfm/beep.vsnd_c");}
            if(decision.Punish)Punish(p,pawn);
        }
        foreach(int id in states.Keys.Where(id=>!seen.Contains(id)).ToArray())states.Remove(id);
    }
    private void Punish(IPlayer p,CCSPlayerPawn pawn)
    {
        punishments++;
        // The beta26 HTML warning path caused a confirmed native failure on
        // this fleet. Keep the same plain-center workaround as the old patch.
        if(settings.PunishBeacon){p.SendCenter("CAMPER - BEWEGEN");p.ExecuteCommand("play sounds/tools/sfm/beep.vsnd_c");}
        if(settings.SlapORSlayPunishment==2){if(pawn.Health>settings.MinHealthReserve)pawn.CommitSuicide(false,true);return;}
        if(settings.SlapORSlayPunishment!=1)return;
        int damage=CampPolicy.Damage(pawn.Health,settings);
        if(pawn.Health<=settings.MinHealthReserve&&!settings.PunishAnyway)return;
        var velocity=pawn.AbsVelocity;velocity.X+=Random.Shared.Next(100,201)*(Random.Shared.Next(2)==0?-1:1);velocity.Y+=Random.Shared.Next(100,201)*(Random.Shared.Next(2)==0?-1:1);velocity.Z+=Random.Shared.Next(200,301);p.Teleport(velocity:velocity);
        if(damage>0)p.TakeDamage(damage,DamageTypes_t.DMG_GENERIC);
    }
    private void Reload(){if(config.Reload())states.Clear();}
    private void WriteStatus()
    {
        try { LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.12",settings,running,bombMap,hostageMap,tracked=states.Count,warnings,punishments,reloadError}); statusWriteFailed=false; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        { if(!statusWriteFailed)Core.Logger.LogWarning("LPLK_AntiCamp: Statusdatei nicht schreibbar ({Error}); Spielregeln bleiben aktiv.",e.GetType().Name);statusWriteFailed=true; }
    }
    [Command("lplk_anticamp",permission:"lplk.anticamp.admin")]public void Command(ICommandContext c){if(c.Sender is {} p)OpenAdmin(p);else c.ReplyLplk($"LPLK_AntiCamp 0.1.12: enabled={settings.PluginEnabled}; campTime={settings.CampTime}; warningSeconds={settings.WarningSeconds}; radius={settings.CampCheckRadius}; damage={settings.SlapDamage}; reserve={settings.MinHealthReserve}; tracked={states.Count}; reloadError={reloadError??"none"}");}
    private void Apply(Func<CampSettings,CampSettings> edit,IPlayer p)
    {
        CampSettings? before=null;config.Change(s=>{before=s;return edit(s);});states.Clear();WriteStatus();
        Core.Logger.LogInformation("LPLK_AntiCamp: Admin {Actor}; vorher={Before}; nachher={After}",p.SteamID,JsonSerializer.Serialize(before),JsonSerializer.Serialize(settings));
    }
}
