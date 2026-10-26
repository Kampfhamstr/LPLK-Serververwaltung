using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Surf;

public sealed partial class Plugin
{
    private readonly ModeScope _lifetime=new();
    private sealed record MenuLease(long Generation);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,MenuLease> _menuLeases=new();
    private readonly Dictionary<int,string> _menuSessions=[];
    private readonly HashSet<CancellationTokenSource> _delayed=[];
    private string _configSignature="";
    private string? _configError;
    private long _nextMaintenance;
    private void Next(Action callback) => Core.Scheduler.NextWorldUpdate(_lifetime.Bind(callback));
    private void Later(float seconds,Action callback)
    {
        CancellationTokenSource? timer=null;
        Action guarded=_lifetime.Bind(callback);
        timer=Core.Scheduler.DelayBySeconds(seconds,()=>{if(timer is not null)_delayed.Remove(timer);guarded();});
        _delayed.Add(timer);
    }
    private void CancelDelayed(){foreach(var timer in _delayed)timer.Cancel();_delayed.Clear();}
    private Action PlayerAction(IPlayer player,Action callback)
    {
        int slot=player.PlayerID;ulong steam=player.SteamID;string session=player.SessionId.ToString();
        return () => {
            var current=Core.PlayerManager.GetPlayer(slot);
            if(!_config.Enabled||current is null||!current.IsValid||current.IsFakeClient||current.SteamID!=steam||current.SessionId.ToString()!=session)return;
            callback();
        };
    }
    private void LoadConfig()
    {
        string path=LPLK.Common.RoundFileStore.ConfigPath(Core,ConfigFile);
        if(!File.Exists(path)){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,DefaultConfig);}
        _config=SurfConfiguration.Read(File.ReadAllText(path));
        _configSignature=JsonSerializer.Serialize(_config,Json);
    }
    private void RefreshConfiguration()
    {
        try
        {
            Config candidate=SurfConfiguration.Read(File.ReadAllText(LPLK.Common.RoundFileStore.ConfigPath(Core,ConfigFile)));
            string signature=JsonSerializer.Serialize(candidate,Json);
            if(candidate.Enabled!=_config.Enabled)throw new InvalidDataException("Enabled geändert: kontrollierter Serverneustart erforderlich.");
            if(signature!=_configSignature)
            {
                _config=candidate;_configSignature=signature;
                // Changed movement or timing rules must never complete a run started under old rules.
                _lifetime.Advance();CancelDelayed();_runs.Clear();_replayViewers.Clear();_voteActive=false;_mapChangeScheduled=false;
                _pendingWorkshopId="";_rtv.Clear();_votes.Clear();_nextMap=null;_endOfMapVoteStarted=false;
                ApplyServerSettings();ApplyTransparentPlayers();ScanWorkshopMaps();
            }
            _configError=null;
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            if(_configError!=e.Message)Core.Logger.LogWarning(e,"LPLK_Surf: Letzte gültige Konfiguration bleibt aktiv.");
            _configError=e.Message;
        }
    }
    [EventListener<EventDelegates.OnWorldUpdate>]
    public void Maintenance()
    {
        if(!_lifetime.Active)return;CompleteStorage();if(Environment.TickCount64<_nextMaintenance)return;
        _nextMaintenance=Environment.TickCount64+2000;RefreshConfiguration();WriteStatus();
    }
    private void WriteStatus()
    {
        try{LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new {plugin="LPLK_Surf",version="0.1.11",updated_at=DateTimeOffset.UtcNow,active=_lifetime.Active,enabled=_config.Enabled,map=_map,configuration_error=_configError,zones_error=_zonesError,ready=_zonesValid,workshop_maps=_workshopMaps.Count,native_triggers=_nativeTriggerMode,active_runs=_runs.Values.Count(r=>r.Running),replays=_replayViewers.Count,vote_active=_voteActive,delayed_actions=_delayed.Count,storage_error=storageError,storage_pending=!storageWork.IsCompleted,storage_schema="SwiftSurf-2.1.8-compatible"});}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Surf: Statusdatei konnte nicht geschrieben werden.");}
    }
    [Command("lplk_surf_status")]
    public void Status(ICommandContext context)
    {
        if(context.Sender is {} p&&!Admin(p))return;
        context.ReplyLplk($"LPLK_Surf 0.1.11; active={_lifetime.Active}; enabled={_config.Enabled}; map={_map}; maps={_workshopMaps.Count}; reloadError={_configError??"none"}");
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Admin.LPLK_Surf.v1",new()
        {
            ["title"]=(Func<string>)(()=>"Surf und KZ"),["allowed"]=(Func<IPlayer,bool>)Admin,
            ["open"]=(Action<IPlayer>)(p=>{if(Admin(p))OpenTrackedMenu(p,BuildAdminMenu(p));})
        });
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Surf.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,_)=>_lifetime.Active&&_config.Enabled&&p.IsValid&&!p.IsFakeClient),
            ["open"]=(Action<IPlayer,string>)((p,page)=>{if(_lifetime.Active&&_config.Enabled&&p.IsValid&&!p.IsFakeClient)OpenTrackedMenu(p,page=="maps"?BuildMapsMenu(p):BuildMainMenu(p));})
        });
    }
}

public static class SurfConfiguration
{
    public static Config Read(string text)
    {
        var options=new JsonSerializerOptions{ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true,PropertyNameCaseInsensitive=true};
        using var document=JsonDocument.Parse(text,new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        // Keep the old section name and all source bytes, including operator metadata.
        if(!document.RootElement.TryGetProperty("SwiftSurf",out var section))throw new InvalidDataException("SwiftSurf-Konfigurationsabschnitt fehlt.");
        Config c=section.Deserialize<Config>(options)??throw new InvalidDataException("Leere Surf-Konfiguration.");
        if(c.Timer is null||c.Server is null||c.MapVote is null||c.Rankings is null||c.AdminSteamIds is null)throw new InvalidDataException("Unvollständige Surf-Konfiguration.");
        if(c.Prefix is null||c.Prefix.Length>128||c.AdminPermission is null||c.AdminPermission.Any(char.IsWhiteSpace))throw new InvalidDataException("Ungültiger Präfix oder Berechtigungsname.");
        // Preserve the effective legacy clamp (the live profile stores 100, legacy used 25).
        c.Timer.HudIntervalMs=Math.Clamp(c.Timer.HudIntervalMs,15,25);
        c.MapVote.EndOfMapVoteMinutesBeforeEnd=Math.Clamp(c.MapVote.EndOfMapVoteMinutesBeforeEnd,1,60);
        bool Range(double v,double min,double max)=>double.IsFinite(v)&&v>=min&&v<=max;
        if(!Range(c.Timer.MaxRunMinutes,1,180)||!Range(c.Timer.StartSpeedCapMetersPerMinute,0,100000)||!Range(c.Server.AirAccelerate,0,10000)||!Range(c.Server.Accelerate,0,10000)||!Range(c.Server.MaxVelocity,1,100000)||!Range(c.Server.PlayerTransparencyAlpha,20,255)||!Range(c.Server.KzCheckpointSlots,1,20)||!Range(c.Rankings.TopCount,1,100)||!Range(c.MapVote.MapsToShow,1,30)||!Range(c.MapVote.VoteDurationSeconds,5,300)||!Range(c.MapVote.MapsInCooldown,0,100)||!Range(c.MapVote.RtvPercentage,1,100)||!Range(c.MapVote.RtvMinPlayers,1,64)||!Range(c.MapVote.ChangeDelaySeconds,0,60)||!Range(c.MapVote.MapChangeVerifySeconds,2,60)||!Range(c.MapVote.MapChangeMaxAttempts,1,5)||string.IsNullOrWhiteSpace(c.MapVote.WorkshopPath))throw new InvalidDataException("Ungültiger Surf-Timer-, Bewegungs- oder Mapvote-Wert.");
        return c;
    }
}
