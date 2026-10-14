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

[PluginMetadata(Id="LPLK_AntiCamp",Name="LPLK_AntiCamp",Version="0.1.3",Author="LPLK",Description="AntiCamp mit echten Zeitabständen und zentraler Administration",MinimumAPIVersion="1.4.14")]
public sealed class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private CampSettings settings=new();
    private sealed record Entry(ulong Session,int Pawn,int Team,CampClock Clock);
    private readonly Dictionary<int,Entry> states=[];
    private bool running,bombMap,hostageMap,planted,dropped;
    private double nextCheck,nextReload;
    private DateTime configStamp;
    private string? reloadError;
    private int warnings,punishments;
    private static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;

    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_AntiCamp","AntiCamp","lplk.anticamp.admin",OpenAdmin);
        settings=module.ReadSettings<CampSettings>(CampSettings.Validate);configStamp=File.GetLastWriteTimeUtc(module.SettingsPath);module.Active=true;
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{Reset();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundStart>(_=>{Reset();Objectives();running=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundEnd>(_=>{Reset();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombPlanted>(_=>{planted=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombDropped>(_=>{dropped=true;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventBombPickup>(_=>{dropped=false;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p)states.Remove(p.PlayerID);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p)states.Remove(p.PlayerID);return HookResult.Continue;});
        WriteStatus();Core.Logger.LogInformation("LPLK_AntiCamp 0.1.3 geladen: CampTime={Time}, Radius={Radius}, Warnung={Warning}s; beim Hotload erst ab nächster Runde aktiv.",settings.CampTime,settings.CampCheckRadius,settings.WarningSeconds);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload(){if(module is not null)module.Active=false;Reset();}
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
        var rules=Core.EntitySystem.GetGameRules();
        if(!settings.PluginEnabled||!running||rules is null||rules.WarmupPeriod||rules.FreezePeriod){states.Clear();return;}
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
            if(decision.Announce&&!settings.DisableChatMessages)Core.PlayerManager.SendChat($"[LPLK] AntiCamp: {p.Name} campt – Position verlassen!");
            if(decision.Warn){warnings++;string text=$"ANTI-CAMP: Bewege dich! Strafe in {settings.WarningSeconds} Sekunden!";p.SendCenterHTML("<font color='orange'>"+text+"</font>",settings.WarningSeconds*1000);p.SendAlert(text);p.SendChat("[LPLK] "+text);p.ExecuteCommand("play sounds/tools/sfm/beep.vsnd_c");}
            if(decision.Punish)Punish(p,pawn);
        }
        foreach(int id in states.Keys.Where(id=>!seen.Contains(id)).ToArray())states.Remove(id);
    }
    private void Punish(IPlayer p,CCSPlayerPawn pawn)
    {
        punishments++;
        if(settings.PunishBeacon){p.SendCenterHTML("<font color='orange'>⚠ CAMPER – BEWEGEN ⚠</font>",1200);p.ExecuteCommand("play sounds/tools/sfm/beep.vsnd_c");}
        if(settings.SlapORSlayPunishment==2){if(pawn.Health>settings.MinHealthReserve)pawn.CommitSuicide(false,true);return;}
        if(settings.SlapORSlayPunishment!=1)return;
        int damage=CampPolicy.Damage(pawn.Health,settings);
        if(pawn.Health<=settings.MinHealthReserve&&!settings.PunishAnyway)return;
        var velocity=pawn.AbsVelocity;velocity.X+=Random.Shared.Next(100,201)*(Random.Shared.Next(2)==0?-1:1);velocity.Y+=Random.Shared.Next(100,201)*(Random.Shared.Next(2)==0?-1:1);velocity.Z+=Random.Shared.Next(200,301);p.Teleport(velocity:velocity);
        if(damage>0)p.TakeDamage(damage,DamageTypes_t.DMG_GENERIC);
    }
    private void Reload()
    {
        try{var stamp=File.GetLastWriteTimeUtc(module.SettingsPath);if(stamp==configStamp)return;var next=JsonSerializer.Deserialize<CampSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();CampSettings.Validate(next);settings=next;configStamp=stamp;reloadError=null;states.Clear();}
        catch(Exception e){if(reloadError is null)Core.Logger.LogWarning(e,"AntiCamp: ungültige Dateiänderung; bisherige Einstellungen bleiben aktiv.");reloadError="Ungültige Änderung; letzter gültiger Stand aktiv.";}
    }
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.3",settings,running,bombMap,hostageMap,tracked=states.Count,warnings,punishments,reloadError});
    [Command("lplk_anticamp",permission:"lplk.anticamp.admin")]public void Command(ICommandContext c){if(c.Sender is {} p)OpenAdmin(p);else c.Reply($"LPLK_AntiCamp 0.1.3: enabled={settings.PluginEnabled}; campTime={settings.CampTime}; warningSeconds={settings.WarningSeconds}; radius={settings.CampCheckRadius}; damage={settings.SlapDamage}; reserve={settings.MinHealthReserve}; tracked={states.Count}; reloadError={reloadError??"none"}");}
    private void Apply(CampSettings next,IPlayer p){CampSettings.Validate(next);module.SaveSettings(next);settings=next;configStamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;states.Clear();WriteStatus();OpenAdmin(p);}
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · AntiCamp",
        ($"Aktiv: {settings.PluginEnabled}",a=>Apply(settings with{PluginEnabled=!settings.PluginEnabled},a)),
        ($"Campzeit: {settings.CampTime}s (+5)",a=>Apply(settings with{CampTime=Math.Min(300,settings.CampTime+5)},a)),
        ("Campzeit −5s",a=>Apply(settings with{CampTime=Math.Max(Math.Max(5,settings.WarningSeconds),settings.CampTime-5)},a)),
        ($"Radius: {settings.CampCheckRadius} (+10)",a=>Apply(settings with{CampCheckRadius=Math.Min(1000,settings.CampCheckRadius+10)},a)),
        ("Radius −10",a=>Apply(settings with{CampCheckRadius=Math.Max(10,settings.CampCheckRadius-10)},a)),
        ("Strafen",PenaltyMenu),("Ausnahmen",ExceptionMenu));
    private void PenaltyMenu(IPlayer p)=>module.Menu(p,"AntiCamp · Warnung / Strafe",1,
        ($"Strafe: {new[]{"Markierung","Slap","Slay"}[settings.SlapORSlayPunishment]}",a=>Apply(settings with{SlapORSlayPunishment=(settings.SlapORSlayPunishment+1)%3},a)),
        ($"Warnung: {settings.WarningSeconds}s",a=>Apply(settings with{WarningSeconds=settings.WarningSeconds>=Math.Min(10,settings.CampTime)?0:settings.WarningSeconds+1},a)),
        ($"Schaden: {settings.SlapDamage}",a=>Apply(settings with{SlapDamage=settings.SlapDamage>=20?0:settings.SlapDamage+1},a)),
        ($"HP-Reserve: {settings.MinHealthReserve}",a=>Apply(settings with{MinHealthReserve=(int)CampPolicy.NextMenuValue(settings.MinHealthReserve,5,100,0)},a)),
        ($"Erste Verzögerung: {settings.FirstPunishDelay}s",a=>Apply(settings with{FirstPunishDelay=CampPolicy.NextMenuValue(settings.FirstPunishDelay,1,10,0)},a)),
        ($"Intervall: {settings.PunishFrequency}s",a=>Apply(settings with{PunishFrequency=CampPolicy.NextMenuValue(settings.PunishFrequency,1,10,1)},a)),
        ($"Warnsignal: {settings.PunishBeacon}",a=>Apply(settings with{PunishBeacon=!settings.PunishBeacon},a)),
        ($"Immer Slap: {settings.PunishAnyway}",a=>Apply(settings with{PunishAnyway=!settings.PunishAnyway},a)),
        ($"Chat aus: {settings.DisableChatMessages}",a=>Apply(settings with{DisableChatMessages=!settings.DisableChatMessages},a)),("Zurück",OpenAdmin));
    private void ExceptionMenu(IPlayer p)=>module.Menu(p,"AntiCamp · Ausnahmen",1,
        ($"T-Geiselmaps: {settings.AllowTCampHostageMap}",a=>Apply(settings with{AllowTCampHostageMap=!settings.AllowTCampHostageMap},a)),
        ($"T-Bombe gelegt: {settings.AllowTCampBombPlanted}",a=>Apply(settings with{AllowTCampBombPlanted=!settings.AllowTCampBombPlanted},a)),
        ($"CT-Bombenmaps: {settings.AllowCTCampBombMap}",a=>Apply(settings with{AllowCTCampBombMap=!settings.AllowCTCampBombMap},a)),
        ($"CT-Bombe gefallen: {settings.AllowCtCampBombDropped}",a=>Apply(settings with{AllowCtCampBombDropped=!settings.AllowCtCampBombDropped},a)),("Zurück",OpenAdmin));
}
