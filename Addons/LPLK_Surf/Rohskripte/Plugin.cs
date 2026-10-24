using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Surf;

[PluginMetadata(Id = "LPLK_Surf", Version = "0.1.0", Name = "LPLK_Surf", Author = "Michael Zauche / OpenAI", Description = "Surf/KZ timer with courses, practice, records, replays and categorized map voting for SwiftlyS2")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private const string ConfigFile = "config.jsonc";
    private const string Section = "SwiftSurf";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private readonly List<Guid> _commands = [];
    private readonly Dictionary<ulong, RunState> _runs = [];
    private readonly Dictionary<ulong, PlayerPrefs> _prefs = [];
    private readonly HashSet<ulong> _rtv = [];
    private readonly Dictionary<ulong, string> _votes = [];
    private readonly Dictionary<ulong, string> _nominations = [];
    private readonly HashSet<string> _voteNominatedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> _menuHudSuppressed = [];
    private readonly Dictionary<ulong, long> _nextHudUpdateMs = [];
    private readonly List<Guid> _triggerHooks = [];
    private readonly HashSet<string> _detectedTriggerNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _triggerActivatorWarnings = [];
    private Config _config = new();
    private MapZones _zones = new();
    private string _map = "unknown";
    private string _zonesFile = "";
    private string _dbFile = "";
    private string _prefsFile = "";
    private bool _voteActive;
    private int _voteSerial;
    private List<WorkshopMap> _workshopMaps = [];
    private List<WorkshopMap> _voteCandidates = [];
    private WorkshopMap? _nextMap;
    private List<string> _history = [];
    private string _historyFile = "";
    private bool _nativeTriggerMode;
    private bool _nativeTouchLogged;
    private int _nativeLastStage = 1;
    private bool _endOfMapVoteStarted;
    private bool _endOfMapChangeScheduled;
    private bool _voteChangesAtMapEnd;
    private long _voteEndsAtMs;
    private long _nextEndOfMapCheckMs;
    private bool _mapChangeScheduled;
    private int _mapChangeAttempt;
    private string _mapChangeOriginMapName = string.Empty;
    private string _mapChangeOriginWorkshopId = string.Empty;
    private string _pendingWorkshopId = string.Empty;
    private string _currentWorkshopId = string.Empty;
    private bool _gameHooksRegistered;

    private const string DefaultConfig = """
{
  "SwiftSurf": {
    "Enabled": false,
    "AdminPermission": "swiftsurf.admin",
    "AdminSteamIds": [],
    "Prefix": "[gold][Surf]",
    "Timer": { "HudEnabledByDefault": true, "HudIntervalMs": 25, "RequireStagesInOrder": true, "MaxRunMinutes": 60, "TeleportToStartOnSpawn": false, "StartSpeedCapMetersPerMinute": 285 },
    "Server": {
      "ApplySurfConvars": true, "AirAccelerate": 150, "Accelerate": 6.5, "MaxVelocity": 3500, "EnableBhop": true,
      "RespawnOnDeath": true, "DisableFallDamage": true, "NoBlock": true, "DisableWeaponDamage": true,
      "TransparentPlayers": true, "PlayerTransparencyAlpha": 100,
      "KzCheckpointsEnabled": true, "KzCheckpointSlots": 20
    },
    "Rankings": { "TopCount": 10, "AnnouncePersonalBest": true, "AnnounceWorldRecord": true },
    "MapVote": {
      "Enabled": true, "WorkshopPath": "/home/container/game/bin/linuxsteamrt64/steamapps/workshop/content/730", "RequireVpkFile": true,
      "MapsToShow": 8, "VoteDurationSeconds": 45, "MapsInCooldown": 3, "RtvPercentage": 60, "RtvMinPlayers": 1,
      "RescanOnMapLoad": true, "ChangeDelaySeconds": 3, "MapChangeVerifySeconds": 8, "MapChangeMaxAttempts": 3, "SetNextLevelBeforeWorkshopChange": true,
      "EndOfMapVoteEnabled": true, "EndOfMapVoteMinutesBeforeEnd": 4, "EndOfMapSameCategoryOnly": true
    }
  }
}
""";

    public override void Load(bool hotReload)
    {
        LoadConfig();
        Directory.CreateDirectory(Core.PluginDataDirectory);
        InitializeSqliteProvider();
        Directory.CreateDirectory(Path.Combine(Core.PluginDataDirectory, "zones"));
        _dbFile = Path.Combine(Core.PluginDataDirectory, "records.db");
        _prefsFile = Path.Combine(Core.PluginDataDirectory, "player_preferences.json");
        _historyFile = Path.Combine(Core.PluginDataDirectory, "map_history.json");
        InitializeDatabase(); LoadPrefs(); LoadHistory(); _lifetime.Start();
        if(_config.Enabled){RegisterCommands(); RegisterTriggerHooks(); RegisterGameHooks();
        Next(() => { _map = CurrentMap(); LoadZones(); ScanWorkshopMaps(); _currentWorkshopId=CurrentWorkshopId(); ApplyServerSettings(); ApplyTransparentPlayers(); ScanNativeMapTriggers(); });}
        WriteStatus();
        Core.Logger.LogInformation("[LPLK_Surf] v0.1.0 geladen. Map={Map}, Zonen={Zones}, Workshopmaps={Maps}, HotReload={HotReload}", _map, _zones.Stages.Count + (_zones.Start is null ? 0 : 1) + (_zones.End is null ? 0 : 1), _workshopMaps.Count, hotReload);
    }

    public override void Unload() { _lifetime.Stop(); CancelDelayed(); UnregisterGameHooks(); UnregisterTriggerHooks(); UnregisterCommands(); _voteActive=false; _mapChangeScheduled=false; _runs.Clear(); _replayViewers.Clear(); SavePrefs(); SqliteConnection.ClearAllPools(); WriteStatus(); }

    [EventListener<EventDelegates.OnTick>]
    public void OnTick()
    {
        if (!_lifetime.Active || !_config.Enabled || !_zonesValid) return;
        CheckEndOfMapVoteAndChange();
        foreach (IPlayer p in Core.PlayerManager.GetAllValidPlayers())
        {
            if (!Usable(p) || p.IsFakeClient) continue;
            ProcessPlayer(p);
        }
        ProcessSpectatorHuds();
    }

    [EventListener<EventDelegates.OnMapLoad>]
    public void OnMapLoad(IOnMapLoadEvent e)
    {
        if(!_lifetime.Active||!_config.Enabled)return; _lifetime.Advance(); CancelDelayed();
        _map = NormalizeMap(e.MapName); _runs.Clear(); _replayViewers.Clear(); _spectatorHudTargets.Clear(); _rtv.Clear(); _votes.Clear(); _nominations.Clear(); _voteNominatedIds.Clear(); _voteActive = false; _nextMap = null;
        _endOfMapVoteStarted=false;_endOfMapChangeScheduled=false;_voteChangesAtMapEnd=false;_voteEndsAtMs=0;_nextEndOfMapCheckMs=0;
        if(!_mapChangeScheduled){_mapChangeAttempt=0;_mapChangeOriginMapName=string.Empty;_mapChangeOriginWorkshopId=string.Empty;}
        LoadZones(); if (_config.MapVote.RescanOnMapLoad) ScanWorkshopMaps(); ApplyServerSettings(); Later(2.0f,RecordCurrentMap); Later(2.0f,ApplyTransparentPlayers);
        if(_mapChangeScheduled&&!string.IsNullOrWhiteSpace(_pendingWorkshopId))
        {
            WorkshopMap? pending=_workshopMaps.FirstOrDefault(x=>x.Id.Equals(_pendingWorkshopId,StringComparison.OrdinalIgnoreCase));
            if(pending is not null)Later(3.0f,()=>VerifyWorkshopMapChange(pending));
        }
        _detectedTriggerNames.Clear(); _triggerActivatorWarnings.Clear(); _nativeTriggerMode = false; _nativeTouchLogged = false; _nativeLastStage = 1;
        // Map trigger entities are not guaranteed to exist on the first tick.
        Later(2.0f, ScanNativeMapTriggers);
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        if(!_lifetime.Active || !_config.Enabled)return HookResult.Continue;
        IPlayer? p = e.UserIdPlayer;
        if (p is not null)
        {
            Next(PlayerAction(p, () => ApplyPlayerRender(p)));
            if (Usable(p))
            {
                ResetRun(p.SteamID);
                if (_config.Timer.TeleportToStartOnSpawn && _zones.Start?.Teleport is not null)
                    Next(PlayerAction(p, () => { if(_zones.Start?.Teleport is {} point)Teleport(p,point); }));
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult OnPlayerDeath(EventPlayerDeath e)
    {
        if(!_lifetime.Active || !_config.Enabled)return HookResult.Continue;
        IPlayer? p = e.UserIdPlayer; if (p is not null) ResetRun(p.SteamID);
        if (_config.Server.RespawnOnDeath && p is not null && !p.IsFakeClient) Later(1, PlayerAction(p, () => { if(!p.IsAlive)p.Respawn(); }));
        return HookResult.Continue;
    }

    private void ProcessPlayer(IPlayer p)
    {
        if (ProcessReplayViewer(p)) return;
        Vector? pos = p.PlayerPawn?.AbsOrigin; if (pos is null) return;
        Vector vel = p.PlayerPawn!.AbsVelocity;
        double speed2d = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        RunState run = State(p.SteamID);
        CaptureReplayFrame(p, run);
        if (_nativeTriggerMode) goto DrawHud;
        bool inStart = _zones.Start?.Contains(pos.Value) == true;
        if (inStart)
        {
            if (!run.InStart && run.Running) ResetRun(p.SteamID);
            run.InStart = true;
            // Merely being inside a valid start zone arms the timer. A speed
            // limit made fast fly-through starts silently fail on surf maps.
            if (!run.Running) run.Armed = true;
        }
        else if (run.InStart)
        {
            run.InStart = false;
            if (run.Armed)
            {
                StartRun(p, run);
                // StartRun may have applied the one-shot cap, so use the
                // resulting velocity for the first HUD frame as well.
                vel = p.PlayerPawn!.AbsVelocity;
                speed2d = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            }
        }
        if (run.Running)
        {
            double elapsed = Elapsed(run);
            if (elapsed > _config.Timer.MaxRunMinutes * 60) { ResetRun(p.SteamID); Reply(p, "[red]Run wegen Zeitlimit abgebrochen."); return; }
            for (int i = 0; i < _zones.Stages.Count; i++)
            {
                if (run.TouchedStages.Contains(i + 1) || !_zones.Stages[i].Contains(pos.Value)) continue;
                if (_config.Timer.RequireStagesInOrder && i + 1 != run.TouchedStages.Count + 1)
                {
                    run.Ranked = false; run.InvalidReason = $"Stage {run.TouchedStages.Count + 1} übersprungen";
                    Reply(p, $"[red]{run.InvalidReason} – Timer läuft weiter, Run wird nicht gewertet.");
                }
                run.TouchedStages.Add(i + 1); run.StageTimes.Add(elapsed); ReplyStageSplit(p, run, i + 1, elapsed);
            }
            if (_zones.End?.Contains(pos.Value) == true && !run.InEnd)
            {
                run.InEnd = true;
                if (!_config.Timer.RequireStagesInOrder || run.TouchedStages.Count == _zones.Stages.Count) FinishRun(p, run);
            }
            else if (_zones.End?.Contains(pos.Value) != true) run.InEnd = false;
        }
        DrawHud:
        long nowMs = Environment.TickCount64;
        long nextHud = _nextHudUpdateMs.GetValueOrDefault(p.SteamID);
        if (nowMs >= nextHud && Pref(p).Hud)
        {
            _nextHudUpdateMs[p.SteamID] = nowMs + _config.Timer.HudIntervalMs;
            string timer = run.Running ? Format(Elapsed(run)) : run.Armed ? "BEREIT" : "--:--.---";
            string stage = run.Course != "main" ? run.Course.ToUpperInvariant() : run.UsingMapTriggers ? $"Stage {Math.Max(1, run.CurrentStage)}" : _zones.Stages.Count > 0 ? $"Stage {run.TouchedStages.Count}/{_zones.Stages.Count}" : "Linear";
            if (!_menuHudSuppressed.Contains(p.SteamID))
            {
                // Proven SwiftlyS2 speedmeter pattern: update every 25 ms and
                // keep each CenterHTML frame alive for 300 ms. The overlap
                // avoids gaps without accumulating long-lived frames.
                PlayerPrefs prefs = Pref(p);
                SplitComparisonMode hudMode = HudComparisonMode(prefs);
                string delta = LiveDelta(p, run);
                string runMode = run.Ranked ? "" : run.InvalidReason is not null ? " | UNGÜLTIG" : " | PRACTICE";
                EnsureHudReferences(p, run);
                string pbText=run.PbSeconds is null?"--:--.---":FormatHud(run.PbSeconds.Value);
                string wrText=run.WrSeconds is null?"--:--.---":FormatHud(run.WrSeconds.Value);
                string wrName=string.IsNullOrWhiteSpace(run.WrPlayerName)?"Maprekord":run.WrPlayerName!;
                string referenceHeader = BuildHudReferenceHeader(hudMode, pbText, wrName, wrText);
                // CenterHTML sanitizes CSS positioning and does not support tables. A
                // monospace <pre> line keeps the low-priority references in stable
                // left/right columns. Its small negative translation places it in
                // the panel's upper header area without moving the main timer.
                string hud = $"{referenceHeader}<span class='fontSize-m' color='#FFFFFF'>{timer}</span> {delta}<br><span class='fontSize-m' color='#66CCFF'>{ToMetersPerMinute(speed2d):0.0}</span>  <span class='fontSize-s' color='#AAAAAA'>{stage}{runMode}</span>";
                p.SendCenterHTML(hud, 300);
            }
        }
    }

    private void StartRun(IPlayer p, RunState r)
    {
        // Apply the anti-bhop protection exactly once, at the instant the run
        // starts. Afterwards the player can accelerate normally without a cap.
        if (r.Course == "main" && CurrentMapCategory() == MapCategory.Surf) CapStartVelocity(p);
        r.Running = true; r.Armed = false; r.StartTimestamp = Stopwatch.GetTimestamp(); r.TouchedStages.Clear(); r.StageTimes.Clear(); PrepareRun(p,r); Reply(p, r.Ranked ? "[green]Timer gestartet." : "[gray]Practice-Timer gestartet (nicht ranked).");
    }

    private void CapStartVelocity(IPlayer player)
    {
        double capMetersPerMinute = _config.Timer.StartSpeedCapMetersPerMinute;
        if (capMetersPerMinute <= 0) return;
        CCSPlayerPawn? pawn = player.PlayerPawn;
        if (pawn is null) return;
        Vector velocity = pawn.AbsVelocity;
        double speed2d = Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        double capUnitsPerSecond = capMetersPerMinute / (0.01905 * 60.0);
        if (speed2d <= capUnitsPerSecond || speed2d <= double.Epsilon) return;
        double scale = capUnitsPerSecond / speed2d;
        player.Teleport(null, null, new Vector((float)(velocity.X * scale), (float)(velocity.Y * scale), velocity.Z));
    }

    private void RegisterTriggerHooks()
    {
        UnregisterTriggerHooks();
        _triggerHooks.Add(Core.EntitySystem.HookEntityOutput("trigger_multiple", "OnStartTouch", OnNativeTriggerStartTouch));
        _triggerHooks.Add(Core.EntitySystem.HookEntityOutput("trigger_multiple", "OnEndTouch", OnNativeTriggerEndTouch));
    }

    private void UnregisterTriggerHooks()
    {
        foreach (Guid hook in _triggerHooks) try { Core.EntitySystem.UnhookEntityOutput(hook); } catch { }
        _triggerHooks.Clear();
    }

    private void ScanNativeMapTriggers()
    {
        try
        {
            _detectedTriggerNames.Clear();
            foreach (CBaseTrigger trigger in Core.EntitySystem.GetAllEntitiesByDesignerName<CBaseTrigger>("trigger_multiple"))
            {
                string name = NormalizeTriggerName(trigger.Identity?.Name);
                if (!string.IsNullOrWhiteSpace(name)) _detectedTriggerNames.Add(name);
            }
            _nativeTriggerMode = _detectedTriggerNames.Any(IsMainStartTrigger) && _detectedTriggerNames.Any(IsMainEndTrigger);
            _nativeLastStage = Math.Max(1, _detectedTriggerNames.Select(x => TryGetStageNumber(x, out int stage) ? stage : 0).DefaultIfEmpty(1).Max());
            Core.Logger.LogInformation("[LPLK_Surf] Map-Trigger: Modus={Mode}, gefunden={Count}, letzte Stage={LastStage}, Namen={Names}", _nativeTriggerMode ? "NATIV" : "MANUELLE BOXEN", _detectedTriggerNames.Count, _nativeLastStage, string.Join(", ", _detectedTriggerNames.OrderBy(x => x).Take(40)));
        }
        catch (Exception ex)
        {
            _nativeTriggerMode = false;
            Core.Logger.LogWarning(ex, "[LPLK_Surf] Native Trigger konnten nicht gelesen werden; manuelle Zonen bleiben aktiv.");
        }
    }

    private void OnNativeTriggerStartTouch(IOnEntityFireOutputHookEvent e)
    {
        if(!_lifetime.Active||!_config.Enabled||!_zonesValid)return;
        if (e.Activator is null || e.Caller is null) return;
        IPlayer? player = PlayerFromTriggerActivator(e.Activator);
        if (player is null || player.IsFakeClient) return;
        string name = NormalizeTriggerName(e.Caller.Identity?.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        LogFirstNativeTouch(player, name, "OnStartTouch");
        _detectedTriggerNames.Add(name);

        if (HandleBonusStartTouch(player, name)) return;

        if (IsActiveMainStartTrigger(name))
        {
            ResetRun(player.SteamID);
            RunState run = State(player.SteamID);
            run.Armed = true; run.InStart = true; run.UsingMapTriggers = true; run.CurrentStage = 1;
            return;
        }

        RunState state = State(player.SteamID);
        if (!state.Running || !state.UsingMapTriggers) return;
        if (state.Course != "main") return;
        if (TryGetStageNumber(name, out int stage) && stage > 1)
        {
            // A trigger can fire more than once while crossing its brush or
            // overlapping another brush. Repeated/older stages must not
            // invalidate or reset an otherwise valid run.
            if (stage <= state.CurrentStage || state.TouchedStages.Contains(stage)) return;
            if (_config.Timer.RequireStagesInOrder && stage != state.CurrentStage + 1)
            {
                state.Ranked = false;
                state.InvalidReason = $"Stage {state.CurrentStage + 1} übersprungen";
                state.CurrentStage = stage;
                RememberNativeStagePosition(player, stage);
                Reply(player, $"[red]{state.InvalidReason} – Timer läuft weiter, Run wird nicht gewertet.");
                return;
            }
            state.CurrentStage = stage; RememberNativeStagePosition(player, stage); state.TouchedStages.Add(stage); double split = Elapsed(state); state.StageTimes.Add(split); ReplyStageSplit(player, state, stage, split); return;
        }
        if (TryGetCheckpointNumber(name, out int checkpoint))
        {
            int key = 1000 + checkpoint; if (state.TouchedStages.Add(key)) { double split = Elapsed(state); state.StageTimes.Add(split); ReplyCheckpointSplit(player, state, checkpoint, split); } return;
        }
        if (IsActiveMainEndTrigger(name))
        {
            if (_config.Timer.RequireStagesInOrder && state.CurrentStage < _nativeLastStage)
            {
                ResetRun(player.SteamID);
                Reply(player, $"[red]Ziel vor Stage {_nativeLastStage} erreicht – Run ungültig.");
                return;
            }
            FinishRun(player, state);
        }
    }

    private void OnNativeTriggerEndTouch(IOnEntityFireOutputHookEvent e)
    {
        if(!_lifetime.Active||!_config.Enabled||!_zonesValid)return;
        if (e.Activator is null || e.Caller is null) return;
        IPlayer? player = PlayerFromTriggerActivator(e.Activator);
        if (player is null || player.IsFakeClient) return;
        string name = NormalizeTriggerName(e.Caller.Identity?.Name);
        LogFirstNativeTouch(player, name, "OnEndTouch");
        if (HandleBonusEndTouch(player, name)) return;
        if (!IsActiveMainStartTrigger(name)) return;
        RunState run = State(player.SteamID);
        if (!run.UsingMapTriggers) { run.UsingMapTriggers = true; run.Armed = true; run.CurrentStage = 1; }
        run.InStart = false;
        if (run.Armed && !run.Running) StartRun(player, run);
    }

    private IPlayer? PlayerFromTriggerActivator(CEntityInstance activator)
    {
        try
        {
            // Entity-output activators arrive as the generic CEntityInstance
            // wrapper. Recreate the pawn wrapper by entity index; a direct
            // interface cast silently fails even when the activator is a player.
            CCSPlayerPawn? pawn = Core.EntitySystem.GetEntityByIndex<CCSPlayerPawn>(activator.Index);
            if (pawn is null || !pawn.IsValidEntity) return null;
            IPlayer? player = Core.PlayerManager.GetPlayerFromPawn(pawn);
            return player is not null && Usable(player) ? player : null;
        }
        catch (Exception ex)
        {
            if (_triggerActivatorWarnings.Add(activator.Index))
                Core.Logger.LogWarning(ex, "[LPLK_Surf] Trigger-Activator Index={Index}, Entity={Entity} konnte keinem Spieler zugeordnet werden.", activator.Index, activator.DesignerName);
            return null;
        }
    }

    private void LogFirstNativeTouch(IPlayer player, string triggerName, string output)
    {
        if (_nativeTouchLogged) return;
        _nativeTouchLogged = true;
        Core.Logger.LogInformation("[LPLK_Surf] Nativer Triggerkontakt aktiv: Spieler={Player}, Trigger={Trigger}, Output={Output}", player.Name, triggerName, output);
    }

    private static string NormalizeTriggerName(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant();
    private static bool IsMainStartTrigger(string name) => name is "map_start" or "s1_start" or "stage1_start" or "timer_start" or "timer_startzone" or "surf_start" or "start_zone" or "start";
    private static bool IsMainEndTrigger(string name) => name is "map_end" or "s1_end" or "stage1_end" or "timer_end" or "timer_endzone" or "surf_end" or "end_zone" or "map_finish" or "end";
    private bool IsActiveMainStartTrigger(string name) => _detectedTriggerNames.Contains("map_start") ? name == "map_start" : IsMainStartTrigger(name);
    private bool IsActiveMainEndTrigger(string name) => _detectedTriggerNames.Contains("map_end") ? name == "map_end" : IsMainEndTrigger(name);
    private static bool TryGetStageNumber(string name, out int stage) { Match m = Regex.Match(name, @"^(?:s|stage)(\d+)_start$"); return int.TryParse(m.Success ? m.Groups[1].Value : string.Empty, out stage); }
    private static bool TryGetCheckpointNumber(string name, out int checkpoint) { Match m = Regex.Match(name, @"^(?:map_)?(?:cp|checkpoint)_?(\d+)(?:_start)?$"); return int.TryParse(m.Success ? m.Groups[1].Value : string.Empty, out checkpoint); }

    private void FinishRun(IPlayer p, RunState r)
    {
        double time = Elapsed(r); r.Running = false; r.Armed = false;
        if (!r.Ranked) { Reply(p, r.InvalidReason is null ? $"[gray]Practice-Ziel: {Format(time)} – nicht gespeichert." : $"[red]Ziel: {Format(time)} – nicht gespeichert ({r.InvalidReason})."); return; }
        RecordResult result = SaveCourseRecord(p, r.Course, time, r.StageTimes);
        string delta = result.OldPb is null ? "erste Zeit" : Signed(time - result.OldPb.Value);
        Reply(p, $"[green]Ziel! [default]{Format(time)} [gray]({delta}) – Rang #{result.Rank}/{result.Total}");
        if (result.IsWorldRecord) SaveWorldRecordReplay(p, r, time);
        if (result.IsWorldRecord && _config.Rankings.AnnounceWorldRecord) Broadcast($"[gold]NEUER WELTREKORD! [default]{p.Name} – {Format(time)} auf {_map} ({r.Course})");
        else if (result.IsPb && _config.Rankings.AnnouncePersonalBest) Broadcast($"[green]Neue Bestzeit: [default]{p.Name} – {Format(time)} ({r.Course}, #{result.Rank})");
    }

    private RecordResult SaveRecord(IPlayer p, double seconds, List<double> stages)
    {
        using SqliteConnection db = OpenDb();
        double? old = ScalarDouble(db, "SELECT best_seconds FROM records WHERE map=$m AND steam_id=$s", ("$m", _map), ("$s", p.SteamID.ToString()));
        bool pb = old is null || seconds < old;
        using (SqliteCommand cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO runs(map,steam_id,player_name,seconds,finished_at) VALUES($m,$s,$n,$t,$d)";
            cmd.Parameters.AddWithValue("$m", _map); cmd.Parameters.AddWithValue("$s", p.SteamID.ToString()); cmd.Parameters.AddWithValue("$n", p.Name); cmd.Parameters.AddWithValue("$t", seconds); cmd.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
        }
        if (pb)
        {
            using SqliteCommand cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO records(map,steam_id,player_name,best_seconds,finishes,stage_times,updated_at) VALUES($m,$s,$n,$t,1,$g,$d) ON CONFLICT(map,steam_id) DO UPDATE SET player_name=$n,best_seconds=$t,finishes=records.finishes+1,stage_times=$g,updated_at=$d";
            cmd.Parameters.AddWithValue("$m", _map); cmd.Parameters.AddWithValue("$s", p.SteamID.ToString()); cmd.Parameters.AddWithValue("$n", p.Name); cmd.Parameters.AddWithValue("$t", seconds); cmd.Parameters.AddWithValue("$g", JsonSerializer.Serialize(stages)); cmd.Parameters.AddWithValue("$d", DateTimeOffset.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
        }
        else using (SqliteCommand cmd = db.CreateCommand()) { cmd.CommandText = "UPDATE records SET finishes=finishes+1,player_name=$n WHERE map=$m AND steam_id=$s"; cmd.Parameters.AddWithValue("$n", p.Name); cmd.Parameters.AddWithValue("$m", _map); cmd.Parameters.AddWithValue("$s", p.SteamID.ToString()); cmd.ExecuteNonQuery(); }
        int rank = Convert.ToInt32(ScalarLong(db, "SELECT COUNT(*)+1 FROM records WHERE map=$m AND best_seconds<$t", ("$m", _map), ("$t", seconds)));
        int total = Convert.ToInt32(ScalarLong(db, "SELECT COUNT(*) FROM records WHERE map=$m", ("$m", _map)));
        return new(old, pb, rank == 1, rank, total);
    }

    private void RegisterCommands()
    {
        Add(["surf", "timer"], MainCommand, "LPLK-Surf-Hauptmenü"); Add(["hilfe", "surfhelp"], HelpCommand, "Alle SwiftSurf-Befehle erklären"); Add(["r", "surfrestart"], RestartCommand, "Zum Start"); Add(["stage", "s"], StageCommand, "Zur letzten Stage");
        Add(["top", "wr"], TopCommand, "Map-Rangliste"); Add(["rank", "pb"], RankCommand, "Eigene Bestzeit"); Add(["speed", "hud"], HudCommand, "Speedmeter umschalten");
        Add(["rtv"], RtvCommand, "Rock the Vote"); Add(["nominate"], NominateCommand, "Map nominieren"); Add(["revote"], VoteCommand, "Vote anzeigen/abstimmen"); Add(["nextmap"], NextMapCommand, "Nächste Map");
        Add(["surfadmin"], AdminCommand, "Surf-Zonen verwalten");
        Add(["practice"], PracticeCommand, "Practice-Modus umschalten"); Add(["cp"], CheckpointCommand, "Checkpoint setzen, optional Slot 1-20"); Add(["tp"], CheckpointTeleportCommand, "Zum Checkpoint, optional Slot 1-20"); Add(["cps", "checkpoints"], CheckpointsMenuCommand, "KZ-Checkpoint-Menü");
        Add(["bonus"], BonusCommand, "Bonus-Ranglisten"); Add(["profile", "stats"], ProfileCommand, "Spielerprofil"); Add(["replay", "ghost"], ReplayCommand, "WR-Replay ansehen"); Add(["specsurf"], SpectatorHudCommand, "HUD eines Spielers verfolgen");
    }

    private void Add(string[] names, ICommandService.CommandListener fn, string help) { foreach (string n in names) _commands.Add(Core.Command.RegisterCommand(n, c => { if(_lifetime.Active && _config.Enabled)fn(c); }, true, string.Empty, help)); }
    private void UnregisterCommands() { foreach (Guid id in _commands) try { Core.Command.UnregisterCommand(id); } catch { } _commands.Clear(); }
    private static IPlayer? Player(ICommandContext c) => c.Sender;
    private string Args(ICommandContext c) => string.Join(' ', c.Args).Trim();

    private void MainCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is not null) OpenTrackedMenu(p, BuildMainMenu(p)); }
    private void HelpCommand(ICommandContext c) { IPlayer? p=Player(c);if(p is not null)OpenTrackedMenu(p,BuildHelpMenu(p)); }
    private void RestartCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is not null) RestartPlayer(p); }
    private void StageCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is not null) StageCommandForPlayer(p); }
    private void HudCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is null) return; PlayerPrefs pref = Pref(p); pref.Hud = !pref.Hud; SavePrefs(); Reply(p, $"Speedmeter: {(pref.Hud ? "[green]AN" : "[red]AUS")}"); }
    private void RankCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is not null) OpenTrackedMenu(p, BuildPersonalMenu(p)); }
    private void TopCommand(ICommandContext c) { IPlayer? p = Player(c); if (p is not null) OpenTrackedMenu(p, BuildTopMenu()); }

    private void AdminCommand(ICommandContext c)
    {
        IPlayer? p = Player(c); if (p is null || !Admin(p)) { if(p is not null) Reply(p,"[red]Keine Berechtigung."); return; }
        string[] a = Args(c).Split(' ', StringSplitOptions.RemoveEmptyEntries); if (a.Length == 0) { OpenTrackedMenu(p, BuildAdminMenu(p)); return; }
        string op=a[0].ToLowerInvariant();
        if (op is "save") { SaveZones(); Reply(p,"[green]Zonen gespeichert."); return; }
        if (op is "reload") { LoadZones(); Reply(p,"[green]Zonen neu geladen."); return; }
        if (op is "rescan") { ScanWorkshopMaps(); Reply(p,$"[green]{_workshopMaps.Count} Workshopmaps gefunden."); return; }
        if (op is "tier" && a.Length >= 2 && int.TryParse(a[1],out int tier) && tier is >=1 and <=8) { _zones.Tier=tier;SaveZones();Reply(p,$"[green]Map-Tier auf {tier} gesetzt.");return; }
        if (op == "delete" && a.Length >= 3 && a[1].Equals("stage",StringComparison.OrdinalIgnoreCase) && int.TryParse(a[2],out int del) && del>0 && del<=_zones.Stages.Count) { _zones.Stages.RemoveAt(del-1); SaveZones(); Reply(p,"Stage gelöscht."); return; }
        if (op == "tp" && a.Length >= 2) { Zone? z = GetZone(a,1); if(z is null){Reply(p,"[red]Zone nicht gefunden.");return;} z.Teleport=Position(p); SaveZones(); Reply(p,"Teleportpunkt gesetzt."); return; }
        Zone? zone=GetOrCreateZone(a); if(zone is null){Reply(p,"[red]Syntax ungültig.");return;} string corner=a[^1].ToLowerInvariant(); if(corner is not ("p1" or "p2")){Reply(p,"[red]Letztes Argument muss p1 oder p2 sein.");return;} Vec v=Position(p); if(corner=="p1") zone.P1=v; else zone.P2=v; if(zone.Teleport is null) zone.Teleport=v; SaveZones(); Reply(p,$"[green]{op} {corner} gesetzt: {v}");
    }

    // ---------------------------------------------------------------------
    // Interactive menus
    // ---------------------------------------------------------------------

    private ValueTask QueueMenuAction(object? sender,IPlayer player, Action action)
    {
        if(sender is null||!_menuLeases.TryGetValue(sender,out var lease)||lease.Generation!=_lifetime.Generation)return ValueTask.CompletedTask;
        if(!_menuSessions.TryGetValue(player.PlayerID,out var session)||session!=player.SessionId.ToString())return ValueTask.CompletedTask;
        Next(PlayerAction(player, () =>
        {
            try { action(); }
            catch (Exception ex) { Core.Logger.LogError(ex, "[LPLK_Surf] Fehler in Menüaktion."); }
        }));
        return ValueTask.CompletedTask;
    }

    private static SubmenuMenuOption Submenu(string title, Func<IMenuAPI> build) =>
        new(title, () => Task.FromResult(build()));

    private static TextMenuOption MenuText(string text)
    {
        TextMenuOption option = new(text);
        if (text.Length > 34) option.TextStyle = MenuOptionTextStyle.ScrollLeftLoop;
        return option;
    }

    private ButtonMenuOption MenuButton(string text)
    {
        ButtonMenuOption option = new(text);
        _menuLeases.Add(option,new(_lifetime.Generation));
        if (text.Length > 34) option.TextStyle = MenuOptionTextStyle.ScrollLeftLoop;
        return option;
    }

    private IMenuBuilderAPI MenuBuilder(string title, int autoCloseSeconds = 60) => Core.MenusAPI.CreateBuilder()
        .Design.SetMenuTitle($"LPLK Surf | {title}")
        .Design.SetMenuTitleVisible(true)
        .Design.SetMenuFooterVisible(true)
        .Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
        .Design.SetMaxVisibleItems(5)
        .SetAutoCloseDelay(Math.Max(1, autoCloseSeconds))
        .SetPlayerFrozen(false);

    private static IMenuAPI Tagged(IMenuAPI menu) { menu.Tag = "SwiftSurf"; return menu; }

    private void OpenTrackedMenu(IPlayer player, IMenuAPI menu)
    {
        if(!_lifetime.Active||!player.IsValid||player.IsFakeClient)return;
        _menuSessions[player.PlayerID]=player.SessionId.ToString();
        _menuHudSuppressed.Add(player.SteamID);
        // Remove an already visible HUD frame immediately so it cannot overlap
        // the interactive menu.
        player.SendCenterHTML(string.Empty, 1);
        Core.MenusAPI.OpenMenuForPlayer(player, menu, OnSurfMenuClosed);
    }

    private void OnSurfMenuClosed(IPlayer player, IMenuAPI closed)
    {
        Next(() =>
        {
            try
            {
                IMenuAPI? current = Core.MenusAPI.GetCurrentMenu(player);
                if (current?.Tag is string tag && tag == "SwiftSurf") return;
            }
            catch { }
            _menuHudSuppressed.Remove(player.SteamID);
        });
    }

    private void CloseSurfMenu(IPlayer player){try{Core.MenusAPI.CloseActiveMenu(player);}catch{ }_menuHudSuppressed.Remove(player.SteamID);}

    private IMenuAPI BuildMainMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Hauptmenü");
        RunState run = State(player.SteamID);
        string state=run.Running?$"{Format(Elapsed(run))} | {run.Course}":run.Armed?"Bereit am Start":"Kein aktiver Lauf";
        b.AddOption(MenuText($"{_map} | {state}"));
        b.AddOption(Submenu("Lauf & Practice ›",()=>BuildRunMenu(player)));
        b.AddOption(Submenu("Rekorde & Profil ›",()=>BuildRecordsMenu(player)));
        if(_config.MapVote.Enabled)b.AddOption(Submenu(_voteActive?"Mapwahl: laufender Vote ›":"Mapwahl & RTV ›",()=>BuildMapsMenu(player)));
        b.AddOption(Submenu("Anzeige & Zuschauer ›",()=>BuildSettingsMenu(player)));
        b.AddOption(Submenu("Hilfe & Befehle ›",()=>BuildHelpMenu(player)));
        if(Admin(player))b.AddOption(Submenu("Administration ›",()=>BuildAdminMenu(player)));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildHelpMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("Hilfe & Befehle");
        b.AddOption(MenuText("Eintrag wählen für Erklärungen"));
        b.AddOption(Submenu("Lauf & Checkpoints ›",BuildRunHelpMenu));
        b.AddOption(Submenu("Rekorde & Anzeige ›",BuildRecordsHelpMenu));
        b.AddOption(Submenu("Mapwahl & RTV ›",BuildMapVoteHelpMenu));
        if(Admin(player))b.AddOption(Submenu("Adminbefehle ›",BuildAdminHelpMenu));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildRunHelpMenu()
    {
        IMenuBuilderAPI b=MenuBuilder("Hilfe | Lauf");
        b.AddOption(MenuText("!surf – Hauptmenü öffnen"));
        b.AddOption(MenuText("!r – Lauf abbrechen und zum Start"));
        b.AddOption(MenuText("!s – zur zuletzt erreichten Stage"));
        b.AddOption(MenuText("!practice – Practice ein/aus"));
        b.AddOption(MenuText("!cp [1-20] – KZ-Punkt speichern"));
        b.AddOption(MenuText("!tp [1-20] – KZ-Punkt laden"));
        b.AddOption(MenuText("!cp delete <Nr> – Punkt löschen"));
        b.AddOption(MenuText("!cps – Checkpoint-Menü öffnen"));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildRecordsHelpMenu()
    {
        IMenuBuilderAPI b=MenuBuilder("Hilfe | Rekorde");
        b.AddOption(MenuText("!top / !wr – Map-Rangliste"));
        b.AddOption(MenuText("!rank / !pb – Bestzeit und Rang"));
        b.AddOption(MenuText("!bonus [Nr] – Bonus-Rangliste"));
        b.AddOption(MenuText("!profile / !stats – Spielerprofil"));
        b.AddOption(MenuText("!replay [Kurs] – WR-Replay starten"));
        b.AddOption(MenuText("!specsurf <Name> – Spieler-HUD"));
        b.AddOption(MenuText("!specsurf off – Verfolgung beenden"));
        b.AddOption(MenuText("!speed / !hud – HUD ein/aus"));
        b.AddOption(MenuText("Anzeige-Menü – HUD-Vergleich wählen"));
        b.AddOption(MenuText("Anzeige-Menü – Chat-Vergleich wählen"));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildMapVoteHelpMenu()
    {
        IMenuBuilderAPI b=MenuBuilder("Hilfe | Mapwahl");
        b.AddOption(MenuText("!rtv – RTV-Stimme abgeben"));
        b.AddOption(MenuText("!nominate – Map nominieren"));
        b.AddOption(MenuText("!nominate <Map> – direkt nominieren"));
        b.AddOption(MenuText("!revote – laufenden Vote öffnen"));
        b.AddOption(MenuText("!revote <Nr> – direkt abstimmen"));
        b.AddOption(MenuText("!nextmap – nächste Map anzeigen"));
        b.AddOption(MenuText("Surf/KZ werden getrennt angezeigt"));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildAdminHelpMenu()
    {
        IMenuBuilderAPI b=MenuBuilder("Hilfe | Admin");
        b.AddOption(MenuText("!surfadmin – Adminmenü öffnen"));
        b.AddOption(MenuText("!surfadmin start p1/p2 – Startzone"));
        b.AddOption(MenuText("!surfadmin end p1/p2 – Zielzone"));
        b.AddOption(MenuText("!surfadmin stage <Nr> p1/p2"));
        b.AddOption(MenuText("!surfadmin tp start/end/stage <Nr>"));
        b.AddOption(MenuText("!surfadmin tier <1-8> – Map-Tier"));
        b.AddOption(MenuText("!surfadmin save – Zonen speichern"));
        b.AddOption(MenuText("!surfadmin reload – Zonen neu laden"));
        b.AddOption(MenuText("!surfadmin rescan – Maps neu lesen"));
        b.AddOption(MenuText("!surfadmin delete stage <Nr>"));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildRunMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("Lauf & Practice");RunState run=State(player.SteamID);PlayerPrefs prefs=Pref(player);
        b.AddOption(MenuText(run.Running?$"{run.Course}: {Format(Elapsed(run))}":prefs.Practice?"Practice aktiv":"Ranked bereit"));
        ButtonMenuOption restart=MenuButton("Neu starten (!r)");restart.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{CloseSurfMenu(a.Player);RestartPlayer(a.Player);});b.AddOption(restart);
        ButtonMenuOption stage=MenuButton("Zur letzten Stage (!s)");stage.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{CloseSurfMenu(a.Player);StageCommandForPlayer(a.Player);});b.AddOption(stage);
        ButtonMenuOption practice=MenuButton($"Practice: {(prefs.Practice?"AN":"AUS")}");practice.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{PlayerPrefs p=Pref(a.Player);p.Practice=!p.Practice;ResetRun(a.Player.SteamID);SavePrefs();OpenTrackedMenu(a.Player,BuildRunMenu(a.Player));});b.AddOption(practice);
        if(_config.Server.KzCheckpointsEnabled&&CurrentMapCategory()==MapCategory.Kz)
            b.AddOption(Submenu($"KZ-Checkpoints | Slot {ActiveCheckpointSlot(prefs)} ›",()=>BuildKzCheckpointMenu(player)));
        else
        {
            ButtonMenuOption cp=MenuButton(prefs.Checkpoint is null?"Checkpoint setzen (!cp)":"Checkpoint neu setzen (!cp)");cp.Enabled=prefs.Practice;cp.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{if(!Pref(a.Player).Practice)return;Pref(a.Player).Checkpoint=Position(a.Player);SavePrefs();Reply(a.Player,"[green]Checkpoint gespeichert.");});b.AddOption(cp);
            ButtonMenuOption tp=MenuButton("Zum Checkpoint (!tp)");tp.Enabled=prefs.Practice&&prefs.Checkpoint is not null;tp.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{PlayerPrefs p=Pref(a.Player);if(p.Checkpoint is null)return;CloseSurfMenu(a.Player);ResetRun(a.Player.SteamID);Teleport(a.Player,p.Checkpoint);});b.AddOption(tp);
        }
        b.AddOption(Submenu("WR-Replay ansehen ›",()=>BuildReplayMenu(player)));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildKzCheckpointMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("KZ-Checkpoints");PlayerPrefs prefs=Pref(player);Dictionary<int,SavedCheckpoint> points=KzCheckpoints(prefs);
        b.AddOption(MenuText("Nur fester Boden | immer Practice"));
        for(int slot=1;slot<=KzCheckpointSlotCount;slot++){int selected=slot;bool saved=points.ContainsKey(selected);b.AddOption(Submenu($"Slot {selected}: {(saved?"gespeichert":"leer")} ›",()=>BuildKzCheckpointSlotMenu(player,selected)));}
        return Tagged(b.Build());
    }

    private IMenuAPI BuildKzCheckpointSlotMenu(IPlayer player,int slot)
    {
        IMenuBuilderAPI b=MenuBuilder($"KZ-Checkpoint {slot}");PlayerPrefs prefs=Pref(player);bool saved=KzCheckpoints(prefs).ContainsKey(slot);
        b.AddOption(MenuText($"Slot {slot}: {(saved?"gespeichert":"leer")}"));
        ButtonMenuOption active=MenuButton($"Als aktiven Slot wählen{(ActiveCheckpointSlot(prefs)==slot?" ✓":"")}");active.Enabled=ActiveCheckpointSlot(prefs)!=slot;active.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{Pref(a.Player).ActiveCheckpointSlot=slot;SavePrefs();OpenTrackedMenu(a.Player,BuildKzCheckpointSlotMenu(a.Player,slot));});b.AddOption(active);
        ButtonMenuOption save=MenuButton("Hier auf Boden speichern");save.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{SaveKzCheckpoint(a.Player,slot);OpenTrackedMenu(a.Player,BuildKzCheckpointSlotMenu(a.Player,slot));});b.AddOption(save);
        ButtonMenuOption teleport=MenuButton("Zu diesem Punkt teleportieren");teleport.Enabled=saved;teleport.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{CloseSurfMenu(a.Player);TeleportToKzCheckpoint(a.Player,slot);});b.AddOption(teleport);
        ButtonMenuOption delete=MenuButton("Diesen Punkt löschen");delete.Enabled=saved;delete.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{DeleteKzCheckpoint(a.Player,slot);OpenTrackedMenu(a.Player,BuildKzCheckpointMenu(a.Player));});b.AddOption(delete);
        return Tagged(b.Build());
    }

    private IMenuAPI BuildRecordsMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("Rekorde & Profil");
        b.AddOption(Submenu("Meine Map-Bestzeit ›",()=>BuildPersonalMenu(player)));
        b.AddOption(Submenu("Map-Topliste ›",BuildTopMenu));
        b.AddOption(Submenu("Mein Spielerprofil ›",()=>BuildProfileMenu(player)));
        foreach(int bonus in DetectedBonuses())b.AddOption(Submenu($"Bonus {bonus} Topliste ›",()=>BuildCourseTopMenu($"bonus{bonus}")));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildMapsMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder(_voteActive?"Mapwahl | laufender Vote":"Mapwahl & RTV",_voteActive?RemainingVoteSeconds():60);
        b.AddOption(MenuText(_nextMap is null?"Nextmap: noch nicht gewählt":$"Nextmap: {_nextMap.Title}"));
        if(_voteActive)
        {
            WorkshopMap? selected = _votes.TryGetValue(player.SteamID, out string? selectedId) ? _voteCandidates.FirstOrDefault(x => x.Id == selectedId) : null;
            b.AddOption(MenuText(selected is null?"Kategorie wählen und abstimmen:":$"Deine Stimme: {selected.Title}"));
            AddMapCategoryLinks(b, category => BuildCategoryVoteMenu(player, category), _voteCandidates);
            if(_voteCandidates.Count==0)b.AddOption(MenuText("Keine Maps im laufenden Vote."));
        }
        else if(_endOfMapVoteStarted&&_nextMap is not null)
            b.AddOption(MenuText("Automatischer Wechsel am Mapende."));
        else
        {
            int humans=Math.Max(1,Core.PlayerManager.GetAllValidPlayers().Count(x=>!x.IsFakeClient));int needed=Math.Max(_config.MapVote.RtvMinPlayers,(int)Math.Ceiling(humans*_config.MapVote.RtvPercentage/100.0));
            ButtonMenuOption rtv=MenuButton(_rtv.Contains(player.SteamID)?$"RTV abgegeben ({_rtv.Count}/{needed})":$"RTV abgeben ({_rtv.Count}/{needed})");rtv.Enabled=!_rtv.Contains(player.SteamID);rtv.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{HandleRtv(a.Player);OpenTrackedMenu(a.Player,_voteActive?BuildVoteMenu(a.Player):BuildMapsMenu(a.Player));});b.AddOption(rtv);
            b.AddOption(Submenu("Map nominieren ›",()=>BuildNominationMenu(player)));
        }
        return Tagged(b.Build());
    }

    private IMenuAPI BuildSettingsMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("Anzeige & Zuschauer");PlayerPrefs prefs=Pref(player);
        ButtonMenuOption hud=MenuButton($"Timer-HUD: {(prefs.Hud?"AN":"AUS")}");hud.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{PlayerPrefs p=Pref(a.Player);p.Hud=!p.Hud;SavePrefs();OpenTrackedMenu(a.Player,BuildSettingsMenu(a.Player));});b.AddOption(hud);
        b.AddOption(Submenu($"HUD-Vergleich: {SplitModeLabel(HudComparisonMode(prefs))} ›",()=>BuildSplitComparisonMenu(player,true)));
        b.AddOption(Submenu($"Chat-Vergleich: {SplitModeLabel(ChatComparisonMode(prefs))} ›",()=>BuildSplitComparisonMenu(player,false)));
        b.AddOption(Submenu("Spieler-HUD verfolgen ›",()=>BuildSpectatorMenu(player)));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildSplitComparisonMenu(IPlayer player, bool forHud)
    {
        IMenuBuilderAPI b=MenuBuilder(forHud?"HUD-Vergleich":"Chat-Vergleich");
        PlayerPrefs prefs=Pref(player);
        SplitComparisonMode current=forHud?HudComparisonMode(prefs):ChatComparisonMode(prefs);
        b.AddOption(MenuText(forHud?"PB/WR im HUD":"PB/WR bei Stages & Checkpoints"));
        foreach(SplitComparisonMode mode in Enum.GetValues<SplitComparisonMode>())
        {
            SplitComparisonMode selected=mode;
            ButtonMenuOption button=MenuButton($"{SplitModeLabel(selected)}{(current==selected?" ✓":"")}");
            button.Enabled=current!=selected;
            button.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{PlayerPrefs p=Pref(a.Player);if(forHud)p.HudComparison=selected;else p.ChatComparison=selected;SavePrefs();OpenTrackedMenu(a.Player,BuildSplitComparisonMenu(a.Player,forHud));});
            b.AddOption(button);
        }
        return Tagged(b.Build());
    }

    private static string SplitModeLabel(SplitComparisonMode mode)=>mode switch
    {
        SplitComparisonMode.PersonalBest=>"Eigene PB",
        SplitComparisonMode.WorldRecord=>"Maprekord (WR)",
        SplitComparisonMode.None=>"Keine",
        _=>"PB + WR"
    };

    private IMenuAPI BuildPersonalMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Meine Bestzeit");
        using SqliteConnection db = OpenDb();
        double? pb = ScalarDouble(db, "SELECT best_seconds FROM course_records WHERE map=$m AND course='main' AND steam_id=$s", ("$m", _map), ("$s", player.SteamID.ToString()));
        if (pb is null) b.AddOption(MenuText("Noch keine gültige Zeit auf dieser Map."));
        else
        {
            long rank = ScalarLong(db, "SELECT COUNT(*)+1 FROM course_records WHERE map=$m AND course='main' AND best_seconds<$t", ("$m", _map), ("$t", pb.Value));
            long finishes = ScalarLong(db, "SELECT finishes FROM course_records WHERE map=$m AND course='main' AND steam_id=$s", ("$m", _map), ("$s", player.SteamID.ToString()));
            b.AddOption(MenuText($"Bestzeit: {Format(pb.Value)}")); b.AddOption(MenuText($"Rang: #{rank} | Zieleinläufe: {finishes}"));
        }
        return Tagged(b.Build());
    }

    private IMenuAPI BuildTopMenu()
    {
        IMenuBuilderAPI b = MenuBuilder("Map-Rangliste");
        using SqliteConnection db = OpenDb(); using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = "SELECT player_name,best_seconds FROM course_records WHERE map=$m AND course='main' ORDER BY best_seconds LIMIT $n"; cmd.Parameters.AddWithValue("$m", _map); cmd.Parameters.AddWithValue("$n", _config.Rankings.TopCount);
        using SqliteDataReader r = cmd.ExecuteReader(); int i = 0;
        while (r.Read()) b.AddOption(MenuText($"#{++i} {r.GetString(0)} – {Format(r.GetDouble(1))}"));
        if (i == 0) b.AddOption(MenuText("Noch keine Zeiten vorhanden."));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildNominationMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Map nominieren");
        List<WorkshopMap> maps=GetNominatableMaps();
        b.AddOption(MenuText("Nominiert für den nächsten Vote"));
        AddMapCategoryLinks(b, category => BuildCategoryNominationMenu(player, category),maps);
        if (maps.Count == 0) b.AddOption(MenuText("Keine nominierbare Map gefunden."));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildCategoryNominationMenu(IPlayer player, MapCategory category)
    {
        IMenuBuilderAPI b = MenuBuilder($"{CategoryLabel(category)} nominieren");
        List<WorkshopMap> maps = GetNominatableMaps().Where(x => x.Category == category).ToList();
        foreach (WorkshopMap map in maps)
        {
            WorkshopMap selected = map; ButtonMenuOption button = MenuButton(map.Title);
            button.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () =>
            {
                WorkshopMap? current=_workshopMaps.FirstOrDefault(x=>x.Id.Equals(selected.Id,StringComparison.OrdinalIgnoreCase));
                if(current is null||!CanNominateMap(current)){CloseSurfMenu(a.Player);Reply(a.Player,"[red]Diese Map ist nicht mehr nominierbar.");return;}
                _nominations[a.Player.SteamID]=current.Id;
                Core.Logger.LogInformation("[LPLK_Surf] Nominierung: Spieler={Player} SteamID={SteamId}, Map={Title} ({Id})",a.Player.Name,a.Player.SteamID,current.Title,current.Id);
                CloseSurfMenu(a.Player);Reply(a.Player,$"Nominiert: [green]{current.Title}[default]. Im Mapvote bitte noch abstimmen.");
            }); b.AddOption(button);
        }
        if (maps.Count == 0) b.AddOption(MenuText($"Keine {CategoryLabel(category)}-Maps gefunden."));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildVoteMenu(IPlayer player)
    {
        // !rtv, !revote and the Mapwahl entry inside !surf intentionally use
        // the exact same categorized hub.
        return BuildMapsMenu(player);
    }

    private IMenuAPI BuildCategoryVoteMenu(IPlayer player, MapCategory category)
    {
        IMenuBuilderAPI b = MenuBuilder($"Mapvote | {CategoryLabel(category)}", RemainingVoteSeconds());
        List<WorkshopMap> maps = _voteCandidates.Where(x => x.Category == category).ToList();
        foreach (WorkshopMap map in maps)
        {
            WorkshopMap selected = map;
            int votes = _votes.Values.Count(x => x == map.Id);
            bool ownVote = _votes.GetValueOrDefault(player.SteamID) == map.Id;
            ButtonMenuOption button = MenuButton($"{map.Title} [{votes}]{(ownVote?" ✓":"")}");
            button.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () =>
            {
                bool accepted=TryCastMapVote(a.Player,selected.Id);CloseSurfMenu(a.Player);
                if(!accepted)Core.Logger.LogWarning("[LPLK_Surf] Menü-Stimme abgelehnt: Spieler={Player}, angezeigte Map={Title} ({Id})",a.Player.Name,selected.Title,selected.Id);
            });
            b.AddOption(button);
        }
        if (maps.Count == 0) b.AddOption(MenuText($"Keine {CategoryLabel(category)}-Maps im Vote."));
        return Tagged(b.Build());
    }

    private void AddMapCategoryLinks(IMenuBuilderAPI builder, Func<MapCategory, IMenuAPI> build, IEnumerable<WorkshopMap>? source = null)
    {
        List<WorkshopMap> maps = (source ?? _workshopMaps).ToList();
        foreach (MapCategory category in Enum.GetValues<MapCategory>())
        {
            int count = maps.Count(x => x.Category == category);
            if (count > 0) builder.AddOption(Submenu($"{CategoryLabel(category)} ({count}) ›", () => build(category)));
        }
    }

    private IMenuAPI BuildRtvMenu(IPlayer player)
    {
        return BuildMapsMenu(player);
    }

    private IMenuAPI BuildAdminMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Administration");
        b.AddOption(MenuText($"Timer: {(_nativeTriggerMode?"Native Trigger":"Manuelle Zonen")} | Tier {_zones.Tier}"));
        b.AddOption(Submenu("Timer-Zonen ›", () => BuildZoneAdminMenu(player)));
        b.AddOption(Submenu("Mapvote & Mapwechsel ›", () => BuildMapAdminMenu(player)));
        b.AddOption(Submenu("Map-Tier festlegen ›",()=>BuildTierMenu(player)));
        ButtonMenuOption reload = MenuButton("Zonen von Datei neu laden"); reload.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return; LoadZones(); Reply(a.Player, "[green]Zonen neu geladen."); }); b.AddOption(reload);
        return Tagged(b.Build());
    }

    private IMenuAPI BuildTierMenu(IPlayer player)
    {
        IMenuBuilderAPI b=MenuBuilder("Map-Tier");b.AddOption(MenuText($"Aktuell: Tier {_zones.Tier}"));for(int tier=1;tier<=8;tier++){int selected=tier;ButtonMenuOption button=MenuButton($"Tier {selected}{(_zones.Tier==selected?" (aktiv)":"")}");button.Enabled=_zones.Tier!=selected;button.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{if(!Admin(a.Player))return;_zones.Tier=selected;SaveZones();OpenTrackedMenu(a.Player,BuildTierMenu(a.Player));});b.AddOption(button);}return Tagged(b.Build());
    }

    private IMenuAPI BuildZoneAdminMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Zonen bearbeiten");
        if(_nativeTriggerMode)b.AddOption(MenuText($"Native Trigger aktiv | letzte Stage {_nativeLastStage}"));
        else b.AddOption(MenuText($"Start {YesNo(_zones.Start)} | Ziel {YesNo(_zones.End)}"));
        AddZoneButtons(b, player, "Start", _zones.Start ??= new());
        AddZoneButtons(b, player, "Ziel", _zones.End ??= new());
        for (int i = 0; i < _zones.Stages.Count; i++) { int stageNo = i + 1; b.AddOption(Submenu($"Stage {stageNo} ›", () => BuildSingleZoneMenu(player, $"Stage {stageNo}", _zones.Stages[stageNo - 1], stageNo))); }
        ButtonMenuOption add = MenuButton($"Neue Stage {_zones.Stages.Count + 1}"); add.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return; _zones.Stages.Add(new Zone { Teleport = Position(a.Player) }); SaveZones(); OpenTrackedMenu(a.Player, BuildZoneAdminMenu(a.Player)); }); b.AddOption(add);
        return Tagged(b.Build());
    }

    private void AddZoneButtons(IMenuBuilderAPI b, IPlayer player, string title, Zone zone) => b.AddOption(Submenu($"{title} ›", () => BuildSingleZoneMenu(player, title, zone, null)));

    private IMenuAPI BuildSingleZoneMenu(IPlayer player, string title, Zone zone, int? stageNo)
    {
        IMenuBuilderAPI b = MenuBuilder(title);
        b.AddOption(MenuText($"P1: {(zone.P1 is null ? "fehlt" : "gesetzt")} | P2: {(zone.P2 is null ? "fehlt" : "gesetzt")}"));
        AddPositionButton(b, "Punkt 1 hier setzen", player, v => zone.P1 = v);
        AddPositionButton(b, "Punkt 2 hier setzen", player, v => zone.P2 = v);
        AddPositionButton(b, "Teleportpunkt hier setzen", player, v => zone.Teleport = v);
        if (stageNo is not null)
        {
            ButtonMenuOption del = MenuButton("Diese Stage löschen"); del.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return; int index = stageNo.Value - 1; if (index >= 0 && index < _zones.Stages.Count) _zones.Stages.RemoveAt(index); SaveZones(); OpenTrackedMenu(a.Player, BuildZoneAdminMenu(a.Player)); }); b.AddOption(del);
        }
        return Tagged(b.Build());
    }

    private void AddPositionButton(IMenuBuilderAPI b, string text, IPlayer owner, Action<Vec> set)
    {
        ButtonMenuOption button = MenuButton(text); button.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return; set(Position(a.Player)); SaveZones(); Reply(a.Player, $"[green]{text}."); }); b.AddOption(button);
    }

    private IMenuAPI BuildMapAdminMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Mapverwaltung");
        b.AddOption(MenuText($"Maps: Surf {_workshopMaps.Count(x => x.Category == MapCategory.Surf)} | KZ {_workshopMaps.Count(x => x.Category == MapCategory.Kz)} | Sonstige {_workshopMaps.Count(x => x.Category == MapCategory.Other)}"));
        ButtonMenuOption scan = MenuButton("Workshopmaps neu einlesen"); scan.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return; ScanWorkshopMaps(); Reply(a.Player, $"[green]{_workshopMaps.Count} Maps gefunden."); }); b.AddOption(scan);
        ButtonMenuOption vote = MenuButton("Mapvote jetzt starten"); vote.Enabled=!_voteActive;vote.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player))return;StartVote(); }); b.AddOption(vote);
        if(_voteActive)b.AddOption(Submenu("Laufenden Vote anzeigen ›",()=>BuildVoteMenu(player)));
        b.AddOption(Submenu("Map sofort wechseln ›", () => BuildMapChangeMenu(player)));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildMapChangeMenu(IPlayer player)
    {
        IMenuBuilderAPI b = MenuBuilder("Map wechseln");
        AddMapCategoryLinks(b, category => BuildCategoryMapChangeMenu(player, category));
        return Tagged(b.Build());
    }

    private IMenuAPI BuildCategoryMapChangeMenu(IPlayer player, MapCategory category)
    {
        IMenuBuilderAPI b = MenuBuilder($"Map wechseln: {CategoryLabel(category)}");
        foreach (WorkshopMap map in _workshopMaps.Where(x => x.Category == category)) { WorkshopMap selected = map; ButtonMenuOption button = MenuButton(map.Title); button.Click += (sender,a)=>QueueMenuAction(sender,a.Player, () => { if (!Admin(a.Player)) return;CloseSurfMenu(a.Player);ScheduleMapChange(selected,0); }); b.AddOption(button); }
        return Tagged(b.Build());
    }

    private void RestartPlayer(IPlayer p)
    {
        ResetRun(p.SteamID);
        if (_zones.Start?.Teleport is not null)
        {
            Teleport(p, _zones.Start.Teleport);
            return;
        }
        if (_nativeTriggerMode)
        {
            // Native brush triggers do not provide a safe teleport position:
            // their entity origin is frequently the map origin. Respawning is
            // the reliable way back to the mapper-defined surf start.
            p.Respawn();
            Reply(p, "[green]Zum Map-Start zurückgesetzt.");
            return;
        }
        Reply(p, "[red]Keine Startzone eingerichtet.");
    }

    private void StageCommandForPlayer(IPlayer p) { RunState r = State(p.SteamID); int nativeStage=r.CurrentStage; int i = Math.Max(0, r.TouchedStages.Count - 1); Zone? z = r.TouchedStages.Count == 0 ? _zones.Start : _zones.Stages.ElementAtOrDefault(i); ResetRun(p.SteamID); if (z?.Teleport is not null) { Teleport(p, z.Teleport);Pref(p).Practice=true;SavePrefs();Reply(p,"[gray]Stage-Teleport – Practice aktiviert."); } else if (_nativeTriggerMode && nativeStage>1 && _zones.NativeStageTeleports.TryGetValue(nativeStage,out Vec? position)) { Teleport(p,position);Pref(p).Practice=true;SavePrefs();Reply(p,$"[green]Zu Stage {nativeStage} teleportiert – Practice aktiviert."); } else if (_nativeTriggerMode) { p.Respawn(); Reply(p, "[gray]Noch kein sicherer Stage-Teleport gelernt; zum Start zurückgesetzt."); } }
    private static string YesNo(Zone? zone) => zone?.P1 is not null && zone.P2 is not null ? "JA" : "NEIN";

    private Zone? GetOrCreateZone(string[] a) { if(a[0].Equals("start",StringComparison.OrdinalIgnoreCase)) return _zones.Start ??= new(); if(a[0].Equals("end",StringComparison.OrdinalIgnoreCase)) return _zones.End ??= new(); if(a[0].Equals("stage",StringComparison.OrdinalIgnoreCase)&&a.Length>=3&&int.TryParse(a[1],out int n)&&n>0){while(_zones.Stages.Count<n)_zones.Stages.Add(new());return _zones.Stages[n-1];} return null; }
    private Zone? GetZone(string[] a,int offset) { if(a[offset].Equals("start",StringComparison.OrdinalIgnoreCase)) return _zones.Start; if(a[offset].Equals("end",StringComparison.OrdinalIgnoreCase)) return _zones.End; if(a[offset].Equals("stage",StringComparison.OrdinalIgnoreCase)&&a.Length>offset+1&&int.TryParse(a[offset+1],out int n)&&n>0) return _zones.Stages.ElementAtOrDefault(n-1); return null; }

    private void CheckEndOfMapVoteAndChange()
    {
        if(!_config.MapVote.Enabled||!_config.MapVote.EndOfMapVoteEnabled)return;
        long now=Environment.TickCount64;if(now<_nextEndOfMapCheckMs)return;_nextEndOfMapCheckMs=now+1000;
        float timeLimitMinutes;try{timeLimitMinutes=Core.ConVar.Find<float>("mp_timelimit")?.Value??0;}catch{return;}
        if(timeLimitMinutes<=0)return;
        CCSGameRules? gameRules;try{gameRules=Core.EntitySystem.GetGameRules();}catch{return;}
        if(gameRules is null||gameRules.WarmupPeriod)return;
        double elapsedSeconds=Math.Max(0,Core.Engine.GlobalVars.CurrentTime-gameRules.GameStartTime);
        double remainingSeconds=timeLimitMinutes*60.0-elapsedSeconds;

        if(_endOfMapVoteStarted&&_nextMap is not null&&!_voteActive&&!_endOfMapChangeScheduled&&remainingSeconds<=1.0)
        {
            _endOfMapChangeScheduled=true;WorkshopMap target=_nextMap;
            Broadcast($"[gold]Mapzeit beendet. Wechsel zu [green]{target.Title}[default].");
            ScheduleMapChange(target,0);return;
        }

        if(_endOfMapVoteStarted||_voteActive||_nextMap is not null||remainingSeconds<=0)return;
        double triggerSeconds=Math.Clamp(_config.MapVote.EndOfMapVoteMinutesBeforeEnd,1,60)*60.0;
        if(remainingSeconds>triggerSeconds)return;

        MapCategory currentCategory=CurrentMapCategory();
        MapCategory? restriction=_config.MapVote.EndOfMapSameCategoryOnly&&currentCategory is MapCategory.Surf or MapCategory.Kz?currentCategory:null;
        _endOfMapVoteStarted=true;
        if(!StartVote(restriction,changeAtMapEnd:true))
            Core.Logger.LogWarning("[LPLK_Surf] Automatischer End-of-Map-Vote konnte nicht gestartet werden. Kategorie={Category}, Restzeit={Seconds:0}s",restriction?.ToString()??"Alle",remainingSeconds);
    }

    private void RtvCommand(ICommandContext c) { IPlayer? p=Player(c); if(p is null)return;if(!_config.MapVote.Enabled){Reply(p,"[red]Mapvote ist deaktiviert.");return;}bool wasActive=_voteActive;if(!_voteActive&&_nextMap is null&&!_rtv.Contains(p.SteamID))HandleRtv(p);if(!wasActive&&_voteActive)return;OpenTrackedMenu(p,BuildRtvMenu(p)); }
    private void HandleRtv(IPlayer p) { if(!_config.MapVote.Enabled||(_endOfMapVoteStarted&&_nextMap is not null)||!_rtv.Add(p.SteamID))return; int players=Math.Max(1,Core.PlayerManager.GetAllValidPlayers().Count(x=>!x.IsFakeClient)); int need=Math.Max(_config.MapVote.RtvMinPlayers,(int)Math.Ceiling(players*_config.MapVote.RtvPercentage/100.0)); Broadcast($"{p.Name} möchte RTV ({_rtv.Count}/{need})."); if(_rtv.Count>=need) StartVote(); }
    private void NominateCommand(ICommandContext c)
    {
        IPlayer? p=Player(c);if(p is null)return;string q=Args(c);if(string.IsNullOrWhiteSpace(q)){OpenTrackedMenu(p,BuildNominationMenu(p));return;}
        List<WorkshopMap> maps=GetNominatableMaps();
        WorkshopMap? m=maps.FirstOrDefault(x=>x.Id.Equals(q,StringComparison.OrdinalIgnoreCase)||x.Title.Equals(q,StringComparison.OrdinalIgnoreCase)||x.SourceFolder.Equals(q,StringComparison.OrdinalIgnoreCase));
        m??=maps.FirstOrDefault(x=>x.Title.Contains(q,StringComparison.OrdinalIgnoreCase)||x.SourceFolder.Contains(q,StringComparison.OrdinalIgnoreCase));
        if(m is null){Reply(p,"[red]Map nicht gefunden, aktuell oder noch im Cooldown.");return;}
        _nominations[p.SteamID]=m.Id;Core.Logger.LogInformation("[LPLK_Surf] Nominierung: Spieler={Player} SteamID={SteamId}, Map={Title} ({Id})",p.Name,p.SteamID,m.Title,m.Id);Reply(p,$"Nominiert: [green]{m.Title}[default]. Im Mapvote bitte noch abstimmen.");
    }
    private void VoteCommand(ICommandContext c) { IPlayer? p=Player(c); if(p is null)return; if(!_voteActive){OpenTrackedMenu(p,BuildMapsMenu(p));return;} string arg=Args(c); if(int.TryParse(arg,out int n)&&n>0&&n<=_voteCandidates.Count){TryCastMapVote(p,_voteCandidates[n-1].Id);return;} OpenTrackedMenu(p,BuildVoteMenu(p)); }
    private void NextMapCommand(ICommandContext c) { IPlayer? p=Player(c); if(p is not null) Reply(p,_nextMap is null?"Noch keine nächste Map gewählt.":$"Nextmap: [green]{_nextMap.Title}"); }
    private bool StartVote(MapCategory? categoryRestriction=null,bool changeAtMapEnd=false)
    {
        if(_voteActive)return false;ScanWorkshopMaps();HashSet<string> cool=_history.TakeLast(_config.MapVote.MapsInCooldown).ToHashSet();
        List<WorkshopMap> eligible=_workshopMaps.Where(x=>!IsCurrentWorkshopMap(x)&&(categoryRestriction is null||x.Category==categoryRestriction)).ToList();
        List<WorkshopMap> pool=eligible.Where(x=>!cool.Contains(x.Id)).OrderBy(_=>Random.Shared.Next()).ToList();
        if(pool.Count<2)pool=eligible.OrderBy(_=>Random.Shared.Next()).ToList();
        _voteCandidates=BuildBalancedVoteCandidates(pool);if(_voteCandidates.Count<2){Broadcast($"[red]Nicht genug {(categoryRestriction is null?string.Empty:CategoryLabel(categoryRestriction.Value)+"-")}Maps für einen Vote.");return false;}
        _voteNominatedIds.Clear();foreach(string id in _nominations.Values)if(_voteCandidates.Any(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase)))_voteNominatedIds.Add(id);_nominations.Clear();
        int duration=Math.Max(1,_config.MapVote.VoteDurationSeconds);
        _voteChangesAtMapEnd=changeAtMapEnd;_voteActive=true;_voteEndsAtMs=Environment.TickCount64+duration*1000L;_votes.Clear();int serial=++_voteSerial;
        string scope=categoryRestriction is null?"Surf und KZ sind im Menü getrennt.":$"Es werden ausschließlich {CategoryLabel(categoryRestriction.Value)}-Maps vorgeschlagen.";
        Broadcast($"[gold]{(changeAtMapEnd?"Automatischer End-of-Map-Vote":"Mapvote")} gestartet ({duration}s)! {scope} Nutze !revote <Nummer>.");
        foreach(IPlayer p in Core.PlayerManager.GetAllValidPlayers().Where(x=>!x.IsFakeClient))
        {
            ShowVote(p);
            OpenTrackedMenu(p,BuildVoteMenu(p));
        }
        Later(duration,()=>{if(_voteActive&&serial==_voteSerial)FinishVote();});return true;
    }
    private int RemainingVoteSeconds()=>Math.Max(1,(int)Math.Ceiling((_voteEndsAtMs-Environment.TickCount64)/1000.0));
    private bool IsMapInCooldown(string id)=>_config.MapVote.MapsInCooldown>0&&_history.TakeLast(_config.MapVote.MapsInCooldown).Any(x=>x.Equals(id,StringComparison.OrdinalIgnoreCase));
    private bool CanNominateMap(WorkshopMap map)=>!IsCurrentWorkshopMap(map)&&!IsMapInCooldown(map.Id);
    private List<WorkshopMap> GetNominatableMaps()=>_workshopMaps.Where(CanNominateMap).ToList();
    private List<WorkshopMap> BuildBalancedVoteCandidates(List<WorkshopMap> pool)
    {
        int limit = Math.Max(2, _config.MapVote.MapsToShow);
        List<WorkshopMap> result = _nominations.Values.Distinct().Select(id => pool.FirstOrDefault(x => x.Id == id)).Where(x => x is not null).Cast<WorkshopMap>().DistinctBy(x => x.Id).Take(limit).ToList();
        Dictionary<MapCategory, Queue<WorkshopMap>> queues = Enum.GetValues<MapCategory>().ToDictionary(category => category, category => new Queue<WorkshopMap>(pool.Where(x => x.Category == category && result.All(y => y.Id != x.Id))));
        MapCategory[] order = [MapCategory.Surf, MapCategory.Kz, MapCategory.Other];
        while (result.Count < limit && queues.Values.Any(x => x.Count > 0))
            foreach (MapCategory category in order) if (result.Count < limit && queues[category].Count > 0) result.Add(queues[category].Dequeue());
        return result;
    }
    private void ShowVote(IPlayer p) { for(int i=0;i<_voteCandidates.Count;i++) Reply(p,$"[default]{i+1}. [gray][{CategoryLabel(_voteCandidates[i].Category)}] [green]{_voteCandidates[i].Title}"); }
    private bool TryCastMapVote(IPlayer player,string mapId)
    {
        if(!_voteActive){Reply(player,"[red]Der Mapvote ist bereits beendet.");return false;}
        WorkshopMap? map=_voteCandidates.FirstOrDefault(x=>x.Id.Equals(mapId,StringComparison.OrdinalIgnoreCase));
        if(map is null){Reply(player,"[red]Diese Map gehört nicht zum laufenden Vote.");return false;}
        _votes[player.SteamID]=map.Id;int count=_votes.Values.Count(x=>x.Equals(map.Id,StringComparison.OrdinalIgnoreCase));
        Core.Logger.LogInformation("[LPLK_Surf] Mapvote-Stimme: Spieler={Player} SteamID={SteamId}, Map={Title} ({Id}), Stimmen={Votes}",player.Name,player.SteamID,map.Title,map.Id,count);
        Reply(player,$"Stimme gespeichert: [green]{map.Title}[default] ({count}).");return true;
    }
    private void FinishVote()
    {
        bool changeAtMapEnd=_voteChangesAtMapEnd;_voteChangesAtMapEnd=false;_voteActive=false;_voteEndsAtMs=0;
        List<(WorkshopMap Map,int Votes)> results=_voteCandidates.Select(map=>(map,_votes.Values.Count(id=>id.Equals(map.Id,StringComparison.OrdinalIgnoreCase)))).ToList();
        int topVotes=results.Count==0?0:results.Max(x=>x.Votes);List<(WorkshopMap Map,int Votes)> tied=results.Where(x=>x.Votes==topVotes).ToList();
        List<(WorkshopMap Map,int Votes)> nominatedTied=tied.Where(x=>_voteNominatedIds.Contains(x.Map.Id)).ToList();if(nominatedTied.Count>0)tied=nominatedTied;
        _nextMap=tied.Count==0?null:tied[Random.Shared.Next(tied.Count)].Map;if(_nextMap is null)return;
        Core.Logger.LogInformation("[LPLK_Surf] Mapvote beendet: Gewinner={Title} ({Id}), Stimmen={Votes}, Kandidaten={Candidates}",_nextMap.Title,_nextMap.Id,topVotes,string.Join(", ",results.Select(x=>$"{x.Map.Title}({x.Map.Id})={x.Votes}")));
        if(changeAtMapEnd){Broadcast($"Vote gewonnen: [green]{_nextMap.Title}[default] mit {topVotes} Stimme(n). Wechsel erfolgt am Mapende.");return;}Broadcast($"Vote gewonnen: [green]{_nextMap.Title}[default] mit {topVotes} Stimme(n).");WorkshopMap target=_nextMap;ScheduleMapChange(target,_config.MapVote.ChangeDelaySeconds);
    }

    private void ScheduleMapChange(WorkshopMap map,int delaySeconds)
    {
        if(_mapChangeScheduled){Core.Logger.LogDebug("[LPLK_Surf] Mapwechsel bereits geplant. Ziel={Title} ({Id}), Versuch={Attempt}",map.Title,map.Id,_mapChangeAttempt);return;}
        _mapChangeScheduled=true;_mapChangeAttempt=0;_mapChangeOriginMapName=CurrentMap();_mapChangeOriginWorkshopId=CurrentWorkshopIdForComparison();
        int delay=Math.Max(0,delaySeconds);
        Broadcast($"[gold]Mapwechsel zu [green]{map.Title}[default] in {delay}s.");
        Core.Logger.LogInformation("[LPLK_Surf] Mapwechsel geplant: {Title} ({Id}) in {Delay}s. Ausgangsmap={OriginMap}, Workshop={OriginWorkshop}",map.Title,map.Id,delay,_mapChangeOriginMapName,_mapChangeOriginWorkshopId);
        Later(delay,()=>IssueWorkshopMapChange(map));
    }

    private void IssueWorkshopMapChange(WorkshopMap map)
    {
        try
        {
            if(!_mapChangeScheduled)return;
            if(!ulong.TryParse(map.Id,NumberStyles.None,CultureInfo.InvariantCulture,out _))
            {
                Core.Logger.LogError("[LPLK_Surf] Ungültige Workshop-ID: {Id}",map.Id);_mapChangeScheduled=false;return;
            }
            _mapChangeAttempt++;_pendingWorkshopId=map.Id;
            if(_config.MapVote.SetNextLevelBeforeWorkshopChange&&IsSafeMapToken(map.SourceFolder))
                ExecuteEngineCommand($"nextlevel {map.SourceFolder}","nextlevel");
            ExecuteEngineCommand($"host_workshop_map {map.Id}","host_workshop_map");
            Core.Logger.LogInformation("[LPLK_Surf] Mapwechsel-Befehl Versuch {Attempt}/{MaxAttempts}: host_workshop_map {Id} ({Title}), source_folder={SourceFolder}",_mapChangeAttempt,Math.Max(1,_config.MapVote.MapChangeMaxAttempts),map.Id,map.Title,string.IsNullOrWhiteSpace(map.SourceFolder)?"<none>":map.SourceFolder);
            Later(Math.Max(2,_config.MapVote.MapChangeVerifySeconds),()=>VerifyWorkshopMapChange(map));
        }
        catch(Exception ex)
        {
            Core.Logger.LogError(ex,"[LPLK_Surf] Workshop-Mapwechsel fehlgeschlagen: {Id}",map.Id);
            RetryWorkshopMapChangeOrFail(map,"Exception beim Ausführen des Mapwechsels");
        }
    }

    private void VerifyWorkshopMapChange(WorkshopMap map)
    {
        if(!_mapChangeScheduled)return;
        string currentWorkshopId=CurrentWorkshopId();string currentMapName=CurrentMap();
        bool targetLoaded=MapMatchesTarget(map,currentWorkshopId,currentMapName);
        bool leftOrigin=!string.IsNullOrWhiteSpace(_mapChangeOriginMapName)&&!string.IsNullOrWhiteSpace(currentMapName)&&!currentMapName.Equals(_mapChangeOriginMapName,StringComparison.OrdinalIgnoreCase);
        if(targetLoaded)
        {
            Core.Logger.LogInformation("[LPLK_Surf] Mapwechsel bestätigt. Ziel={Title} ({Id}), aktuelle Map={CurrentMap}, Workshop={CurrentWorkshop}",map.Title,map.Id,currentMapName,currentWorkshopId);
            _mapChangeScheduled=false;_mapChangeAttempt=0;_mapChangeOriginMapName=string.Empty;_mapChangeOriginWorkshopId=string.Empty;_pendingWorkshopId=string.Empty;return;
        }
        if(leftOrigin)Core.Logger.LogError("[LPLK_Surf] Unerwartete Map geladen. Erwartet={Title} ({Id}), tatsächlich Map={CurrentMap}, Workshop={CurrentWorkshop}. Gewähltes Ziel wird erneut angefordert.",map.Title,map.Id,currentMapName,currentWorkshopId);
        RetryWorkshopMapChangeOrFail(map,$"nach {_config.MapVote.MapChangeVerifySeconds}s noch auf {_mapChangeOriginMapName}/{_mapChangeOriginWorkshopId}");
    }

    private void RetryWorkshopMapChangeOrFail(WorkshopMap map,string reason)
    {
        int maxAttempts=Math.Max(1,_config.MapVote.MapChangeMaxAttempts);
        if(_mapChangeAttempt>=maxAttempts)
        {
            Core.Logger.LogError("[LPLK_Surf] Mapwechsel endgültig fehlgeschlagen nach {Attempts} Versuch(en): {Title} ({Id}). Grund: {Reason}",_mapChangeAttempt,map.Title,map.Id,reason);
            Broadcast($"[red]Mapwechsel zu {map.Title} fehlgeschlagen. Siehe Serverlog.");_mapChangeScheduled=false;_pendingWorkshopId=string.Empty;return;
        }
        Core.Logger.LogWarning("[LPLK_Surf] Mapwechsel noch nicht erfolgt ({Reason}). Wiederhole host_workshop_map, Versuch {Next}/{Max}.",reason,_mapChangeAttempt+1,maxAttempts);
        Later(1.0f,()=>IssueWorkshopMapChange(map));
    }

    private void ExecuteEngineCommand(string command,string label)
    {
        try
        {
            MethodInfo? buffered=Core.Engine.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(method=>
            {
                if(!method.Name.Equals("ExecuteCommandWithBuffer",StringComparison.Ordinal))return false;
                ParameterInfo[] parameters=method.GetParameters();return parameters.Length==2&&parameters[0].ParameterType==typeof(string);
            });
            if(buffered is not null)
            {
                Action<string> callback=output=>LogCommandOutput(label,output);buffered.Invoke(Core.Engine,[command,callback]);return;
            }
        }
        catch(Exception ex){Core.Logger.LogDebug(ex,"[LPLK_Surf] ExecuteCommandWithBuffer für {Command} nicht nutzbar.",label);}
        Core.Engine.ExecuteCommand(command);
    }

    private void LogCommandOutput(string command,string output){if(!string.IsNullOrWhiteSpace(output))Core.Logger.LogDebug("[LPLK_Surf] Engine-Ausgabe {Command}: {Output}",command,output.Trim());}
    private static bool IsSafeMapToken(string? value){if(string.IsNullOrWhiteSpace(value))return false;string token=value.Trim();return token.Length<=128&&!token.Contains("..",StringComparison.Ordinal)&&Regex.IsMatch(token,"^[A-Za-z0-9_./-]+$",RegexOptions.CultureInvariant);}
    private static bool MapMatchesTarget(WorkshopMap map,string workshopId,string mapName)
    {
        if(!string.IsNullOrWhiteSpace(workshopId)&&workshopId.Equals(map.Id,StringComparison.OrdinalIgnoreCase))return true;
        if(string.IsNullOrWhiteSpace(mapName))return false;
        return (!string.IsNullOrWhiteSpace(map.SourceFolder)&&NormalizeMap(map.SourceFolder).Equals(mapName,StringComparison.OrdinalIgnoreCase))||NormalizeMap(map.Title).Equals(mapName,StringComparison.OrdinalIgnoreCase);
    }

    private void ScanWorkshopMaps()
    {
        _workshopMaps=[]; string root=ResolveWorkshopPath(_config.MapVote.WorkshopPath); if(!Directory.Exists(root)){Core.Logger.LogWarning("[LPLK_Surf] Workshop-Pfad fehlt: {Path}",root);return;}
        foreach(string dir in Directory.EnumerateDirectories(root)) { string id=Path.GetFileName(dir); if(!ulong.TryParse(id,out _))continue; List<string> vpks=Directory.EnumerateFiles(dir,"*.vpk",SearchOption.AllDirectories).ToList(); if(_config.MapVote.RequireVpkFile&&vpks.Count==0)continue; string pub=Path.Combine(dir,"publish_data.txt");string title=File.Exists(pub)?TryReadWorkshopValue(pub,"title"):string.Empty;string sourceFolder=File.Exists(pub)?TryReadWorkshopValue(pub,"source_folder"):string.Empty;if(string.IsNullOrWhiteSpace(title))title=string.IsNullOrWhiteSpace(sourceFolder)?id:sourceFolder; _workshopMaps.Add(new(id,title,ClassifyMap(title,vpks),sourceFolder)); }
        _workshopMaps=_workshopMaps.OrderBy(x=>x.Category).ThenBy(x=>x.Title,StringComparer.OrdinalIgnoreCase).ToList(); Core.Logger.LogInformation("[LPLK_Surf] Workshop-Scan: {Count} Maps (Surf={Surf}, KZ={Kz}, Sonstige={Other}), Pfad={Path}",_workshopMaps.Count,_workshopMaps.Count(x=>x.Category==MapCategory.Surf),_workshopMaps.Count(x=>x.Category==MapCategory.Kz),_workshopMaps.Count(x=>x.Category==MapCategory.Other),root);
    }
    private static MapCategory ClassifyMap(string title, IEnumerable<string> vpkFiles)
    {
        string evidence = title + " " + string.Join(' ', vpkFiles.Select(Path.GetFileNameWithoutExtension));
        if (Regex.IsMatch(evidence, @"(?:^|[^a-z0-9])surf[_\s-]", RegexOptions.IgnoreCase)) return MapCategory.Surf;
        if (Regex.IsMatch(evidence, @"(?:^|[^a-z0-9])(?:kz|xc|bkz)[_\s-]", RegexOptions.IgnoreCase)) return MapCategory.Kz;
        return MapCategory.Other;
    }
    private MapCategory CurrentMapCategory()
    {
        string workshopId=CurrentWorkshopIdForComparison();
        WorkshopMap? installed=!string.IsNullOrWhiteSpace(workshopId)?_workshopMaps.FirstOrDefault(x=>x.Id==workshopId):null;
        return installed?.Category??ClassifyMap(_map,[_map]);
    }
    private bool IsCurrentWorkshopMap(WorkshopMap map)
    {
        string workshopId=CurrentWorkshopIdForComparison();
        if(!string.IsNullOrWhiteSpace(workshopId)&&map.Id==workshopId)return true;
        return NormalizeMap(map.Title)==_map;
    }
    private string CurrentWorkshopId(){try{return (Core.Engine.WorkshopId??string.Empty).Trim();}catch{return string.Empty;}}
    private string CurrentWorkshopIdForComparison(){string current=CurrentWorkshopId();return !string.IsNullOrWhiteSpace(current)?current:_currentWorkshopId;}
    private static string TryReadWorkshopValue(string publishDataPath,string key){try{string text=File.ReadAllText(publishDataPath);Match match=Regex.Match(text,$"\"{Regex.Escape(key)}\"\\s+\"(?<value>[^\"]+)\"",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);return match.Success?match.Groups["value"].Value.Trim():string.Empty;}catch{return string.Empty;}}
    private static string CategoryLabel(MapCategory category) => category switch { MapCategory.Surf => "Surf", MapCategory.Kz => "KZ", _ => "Sonstige" };
    private string ResolveWorkshopPath(string p) { string[] c=[p,"/home/container"+p,"/home/container/game/bin/linuxsteamrt64/steamapps/workshop/content/730","/game/bin/linuxsteamrt64/steamapps/workshop/content/730",Path.Combine(Core.GameFilesDirectory,"..","bin","linuxsteamrt64","steamapps","workshop","content","730")]; return c.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists)??p; }

    private bool _zonesValid=true;
    private string? _zonesError;
    private void LoadZones()
    {
        _zonesFile=Path.Combine(Core.PluginDataDirectory,"zones",Safe(_map)+".json");
        try
        {
            var candidate=File.Exists(_zonesFile)?JsonSerializer.Deserialize<MapZones>(File.ReadAllText(_zonesFile),Json)??throw new InvalidDataException("Leere Zonendatei."):new();
            candidate.Stages??=[];candidate.NativeStageTeleports??=[];candidate.Tier=Math.Clamp(candidate.Tier,1,8);
            if(candidate.Stages.Any(z=>z is null)||candidate.NativeStageTeleports.Values.Any(v=>v is null))throw new InvalidDataException("Ungültige Zonendatei.");
            _zones=candidate;_zonesValid=true;_zonesError=null;
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {_zones=new();_zonesValid=false;_zonesError=ex.Message;Core.Logger.LogError(ex,"LPLK_Surf: Zonen ungültig, Zeitmessung angehalten; Originaldatei bleibt erhalten.");}
    }
    private void SaveZones(){if(!_zonesValid)throw new InvalidDataException("Zonendatei zuerst reparieren und neu laden.");AtomicJson.Write(_zonesFile,_zones);}
    private void LoadPrefs()
    {
        if(!File.Exists(_prefsFile))return;
        var data=JsonSerializer.Deserialize<Dictionary<ulong,PlayerPrefs>>(File.ReadAllText(_prefsFile),Json)??throw new InvalidDataException("Leere Spielereinstellungen.");
        if(data.Values.Any(p=>p is null))throw new InvalidDataException("Ungültige Spielereinstellungen.");
        foreach(var item in data)_prefs.Add(item.Key,item.Value);
    }
    private void SavePrefs(){if(string.IsNullOrEmpty(_prefsFile))return;AtomicJson.Write(_prefsFile,_prefs);}
    private void LoadHistory(){try{if(File.Exists(_historyFile))_history=JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_historyFile),Json)??[];}catch(Exception ex){throw new InvalidDataException("Maphistorie ist ungültig; Originaldatei bleibt erhalten.",ex);}}
    private void RecordCurrentMap()
    {
        string liveWorkshopId=CurrentWorkshopId();
        WorkshopMap? map=!string.IsNullOrWhiteSpace(liveWorkshopId)?_workshopMaps.FirstOrDefault(x=>x.Id.Equals(liveWorkshopId,StringComparison.OrdinalIgnoreCase)):null;
        map??=_workshopMaps.FirstOrDefault(x=>(!string.IsNullOrWhiteSpace(x.SourceFolder)&&NormalizeMap(x.SourceFolder)==_map)||NormalizeMap(x.Title)==_map);
        if(map is null)return;
        _currentWorkshopId=map.Id;_history.RemoveAll(x=>x.Equals(map.Id,StringComparison.OrdinalIgnoreCase));_history.Add(map.Id);_history=_history.TakeLast(Math.Max(10,_config.MapVote.MapsInCooldown+5)).ToList();AtomicJson.Write(_historyFile,_history);
        if(_pendingWorkshopId.Equals(map.Id,StringComparison.OrdinalIgnoreCase))_pendingWorkshopId=string.Empty;
    }
    private void InitializeDatabase(){using SqliteConnection db=OpenDb();using SqliteCommand cmd=db.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS records(map TEXT NOT NULL,steam_id TEXT NOT NULL,player_name TEXT NOT NULL,best_seconds REAL NOT NULL,finishes INTEGER NOT NULL DEFAULT 1,stage_times TEXT NOT NULL DEFAULT '[]',updated_at TEXT NOT NULL,PRIMARY KEY(map,steam_id)); CREATE INDEX IF NOT EXISTS ix_records_map_time ON records(map,best_seconds); CREATE TABLE IF NOT EXISTS runs(id INTEGER PRIMARY KEY AUTOINCREMENT,map TEXT NOT NULL,steam_id TEXT NOT NULL,player_name TEXT NOT NULL,seconds REAL NOT NULL,finished_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS course_records(map TEXT NOT NULL,course TEXT NOT NULL,steam_id TEXT NOT NULL,player_name TEXT NOT NULL,best_seconds REAL NOT NULL,finishes INTEGER NOT NULL DEFAULT 1,stage_times TEXT NOT NULL DEFAULT '[]',updated_at TEXT NOT NULL,PRIMARY KEY(map,course,steam_id)); CREATE INDEX IF NOT EXISTS ix_course_records_map_course_time ON course_records(map,course,best_seconds); CREATE TABLE IF NOT EXISTS course_runs(id INTEGER PRIMARY KEY AUTOINCREMENT,map TEXT NOT NULL,course TEXT NOT NULL,steam_id TEXT NOT NULL,player_name TEXT NOT NULL,seconds REAL NOT NULL,finished_at TEXT NOT NULL); INSERT OR IGNORE INTO course_records(map,course,steam_id,player_name,best_seconds,finishes,stage_times,updated_at) SELECT map,'main',steam_id,player_name,best_seconds,finishes,stage_times,updated_at FROM records;";cmd.ExecuteNonQuery();}
    private SqliteConnection OpenDb(){var db=new SqliteConnection($"Data Source={_dbFile}");db.Open();return db;}
    private static void InitializeSqliteProvider() => SQLitePCL.Batteries_V2.Init();
    private static double? ScalarDouble(SqliteConnection db,string sql,params (string,object)[] p){using var c=db.CreateCommand();c.CommandText=sql;foreach(var x in p)c.Parameters.AddWithValue(x.Item1,x.Item2);object? v=c.ExecuteScalar();return v is null or DBNull?null:Convert.ToDouble(v,CultureInfo.InvariantCulture);}
    private static long ScalarLong(SqliteConnection db,string sql,params (string,object)[] p){using var c=db.CreateCommand();c.CommandText=sql;foreach(var x in p)c.Parameters.AddWithValue(x.Item1,x.Item2);return Convert.ToInt64(c.ExecuteScalar()??0);}
    private void ApplyServerSettings()
    {
        // Plugin.Load may run on SwiftlyS2's plugin-loader thread. Native engine
        // commands are main-thread-only, therefore every convar is deferred to
        // the next game tick. This is also safe when called from OnMapLoad.
        Next(() =>
        {
            if(!_lifetime.Active || !_config.Enabled)return;
            if(_config.Server.ApplySurfConvars)
            {
                Core.Engine.ExecuteCommand($"sv_airaccelerate {_config.Server.AirAccelerate.ToString(CultureInfo.InvariantCulture)}");
                Core.Engine.ExecuteCommand($"sv_accelerate {_config.Server.Accelerate.ToString(CultureInfo.InvariantCulture)}");
                Core.Engine.ExecuteCommand($"sv_maxvelocity {_config.Server.MaxVelocity}");
                Core.Engine.ExecuteCommand($"sv_enablebunnyhopping {(_config.Server.EnableBhop ? 1 : 0)}");
                Core.Engine.ExecuteCommand($"sv_autobunnyhopping {(_config.Server.EnableBhop ? 1 : 0)}");
                Core.Engine.ExecuteCommand($"mp_falldamage {(_config.Server.DisableFallDamage ? 0 : 1)}");
            }
            Core.Engine.ExecuteCommand($"mp_solid_teammates {(_config.Server.NoBlock?0:1)}");
        });
    }
    private void ApplyTransparentPlayers()
    {
        if (!_lifetime.Active || !_config.Enabled) return;
        foreach (IPlayer player in Core.PlayerManager.GetAllValidPlayers()) ApplyPlayerRender(player);
    }
    private void ApplyPlayerRender(IPlayer player)
    {
        try
        {
            CCSPlayerPawn? pawn = player.PlayerPawn;
            if (pawn is null) return;
            bool transparent = _config.Server.TransparentPlayers;
            int alpha = Math.Clamp(_config.Server.PlayerTransparencyAlpha, 20, 255);
            pawn.Render = new Color(255, 255, 255, transparent ? alpha : 255);
            pawn.RenderMode = transparent ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;
            pawn.RenderUpdated();
            pawn.RenderModeUpdated();
            // Debris is non-blocking against other player collision groups while
            // retaining normal world/map movement. Restore Player when disabled.
            pawn.Collision.CollisionGroup = (byte)(_config.Server.NoBlock ? CollisionGroup.Debris : CollisionGroup.Player);
            pawn.Collision.CollisionGroupUpdated();
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "[LPLK_Surf] Spieler-Transparenz konnte nicht gesetzt werden.");
        }
    }
    private void RegisterGameHooks(){if(_gameHooksRegistered)return;Core.GameHooks.Entities.TakeDamage.Pre+=OnTakeDamagePre;_gameHooksRegistered=true;}
    private void UnregisterGameHooks(){if(!_gameHooksRegistered)return;Core.GameHooks.Entities.TakeDamage.Pre-=OnTakeDamagePre;_gameHooksRegistered=false;}
    private void OnTakeDamagePre(ref TakeDamageEntityPreContext context)
    {
        if(!_lifetime.Active||!_config.Enabled||!_config.Server.DisableWeaponDamage||context.Params.Entity.DesignerName is not "player")return;
        CTakeDamageInfo info=context.Params.Info;CEntityInstance? attacker=info.Attacker.Value;if(attacker is null)return;
        if(attacker.DesignerName is "player" or "cs_player_controller")context.SetHookResult(HookResult.Stop);
    }
    private bool Admin(IPlayer p)=>_lifetime.Active && p.IsValid && !p.IsFakeClient && (_config.AdminSteamIds.Contains(p.SteamID)||Core.Permission.PlayerHasPermission(p.SteamID,"lplk.admin")||Core.Permission.PlayerHasPermission(p.SteamID,"lplk.surf.admin")||(!string.IsNullOrWhiteSpace(_config.AdminPermission)&&Core.Permission.PlayerHasPermissions(p.SteamID,[_config.AdminPermission])));
    private static bool Usable(IPlayer p)=>p.IsValid&&p.IsAlive&&p.PlayerPawn is not null;
    private RunState State(ulong id){if(!_runs.TryGetValue(id,out RunState? r))_runs[id]=r=new();return r;}
    private void ResetRun(ulong id)=>_runs[id]=new();
    private PlayerPrefs Pref(IPlayer p){if(!_prefs.TryGetValue(p.SteamID,out PlayerPrefs? x))_prefs[p.SteamID]=x=new(){Hud=_config.Timer.HudEnabledByDefault};x.KzCheckpoints??=[];x.ActiveCheckpointSlot=Math.Clamp(x.ActiveCheckpointSlot,1,KzCheckpointSlotCount);x.HudComparison??=x.SplitComparison;x.ChatComparison??=x.SplitComparison;return x;}
    private static SplitComparisonMode HudComparisonMode(PlayerPrefs prefs)=>prefs.HudComparison??prefs.SplitComparison;
    private static SplitComparisonMode ChatComparisonMode(PlayerPrefs prefs)=>prefs.ChatComparison??prefs.SplitComparison;
    private static double Elapsed(RunState r)=>(Stopwatch.GetTimestamp()-r.StartTimestamp)/(double)Stopwatch.Frequency;
    private static double ToMetersPerMinute(double sourceUnitsPerSecond) => sourceUnitsPerSecond * 0.01905 * 60.0;
    private static string Format(double s)=>TimeSpan.FromSeconds(s).ToString(s>=3600?@"h\:mm\:ss\.fff":@"m\:ss\.fff",CultureInfo.InvariantCulture);

    private static string BuildHudReferenceHeader(SplitComparisonMode mode, string pbText, string wrName, string wrText)
    {
        bool showPb = ShowsPersonalBest(mode);
        bool showWr = ShowsWorldRecord(mode);
        if (!showPb && !showWr) return string.Empty;

        string cleanName = new string((wrName ?? string.Empty).Where(c => !char.IsControl(c) && c is not '<' and not '>' and not '&').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Maprekord";
        if (cleanName.Length > 16) cleanName = cleanName[..15] + "…";

        // Keep a predictable width so the two compact reference blocks sit at
        // opposite sides of the centered CenterHTML panel. A single selected
        // reference remains in its normal left (PB) or right (WR) position.
        const int lineWidth = 58;
        string pbPlain = showPb ? $"PB {pbText}" : string.Empty;
        string wrPlain = showWr ? $"WR {cleanName} {wrText}" : string.Empty;
        int minimumGap = showPb && showWr ? 3 : 0;
        string gap = new(' ', Math.Max(minimumGap, lineWidth - pbPlain.Length - wrPlain.Length));
        string pbHtml = showPb ? $"<span color='#888888'>{pbPlain}</span>" : string.Empty;
        string wrHtml = showWr ? $"<span color='#888888'>WR {cleanName} </span><span color='#FFD966'>{wrText}</span>" : string.Empty;
        return $"<pre transform='translateY(-7px)'><span class='fontSize-xs'>{pbHtml}{gap}{wrHtml}</span></pre>";
    }
    private static string Signed(double d)=>$"{(d<0?"-":"+")}{Format(Math.Abs(d))}";
    private void Reply(IPlayer p,string s)=>p.SendChat($"{_config.Prefix} [default]{s}");
    private void Broadcast(string s)=>Core.PlayerManager.SendChat($"{_config.Prefix} [default]{s}");
    private Vec Position(IPlayer p){Vector v=p.PlayerPawn!.AbsOrigin!.Value;return new(v.X,v.Y,v.Z);}
    private static void Teleport(IPlayer p,Vec v)=>p.Teleport(new Vector(v.X,v.Y,v.Z),null,new Vector(0,0,0));
    private string CurrentMap(){try{return NormalizeMap(Core.Engine.GlobalVars.MapName);}catch{return "unknown";}}
    private static string NormalizeMap(string s)=>Path.GetFileNameWithoutExtension(s.Replace('\\','/')).ToLowerInvariant();
    private static string Safe(string s)=>string.Concat(s.Select(c=>char.IsLetterOrDigit(c)||c is '_' or '-'?c:'_'));
}

public sealed class Config { public bool Enabled{get;set;}=true; public string AdminPermission{get;set;}="swiftsurf.admin"; public ulong[] AdminSteamIds{get;set;}=[]; public string Prefix{get;set;}="[gold][Surf]"; public TimerConfig Timer{get;set;}=new(); public ServerConfig Server{get;set;}=new(); public RankingConfig Rankings{get;set;}=new(); public MapVoteConfig MapVote{get;set;}=new(); }
public sealed class TimerConfig { public bool HudEnabledByDefault{get;set;}=true; public int HudIntervalMs{get;set;}=25; public bool RequireStagesInOrder{get;set;}=true; public int MaxRunMinutes{get;set;}=60; public bool TeleportToStartOnSpawn{get;set;} public double StartSpeedCapMetersPerMinute{get;set;}=285; }
public sealed class ServerConfig { public bool ApplySurfConvars{get;set;}=true; public double AirAccelerate{get;set;}=150; public double Accelerate{get;set;}=6.5; public int MaxVelocity{get;set;}=3500; public bool EnableBhop{get;set;}=true; public bool RespawnOnDeath{get;set;}=true; public bool DisableFallDamage{get;set;}=true; public bool NoBlock{get;set;}=true; public bool DisableWeaponDamage{get;set;}=true; public bool TransparentPlayers{get;set;}=true; public int PlayerTransparencyAlpha{get;set;}=100; public bool KzCheckpointsEnabled{get;set;}=true; public int KzCheckpointSlots{get;set;}=20; }
public sealed class RankingConfig { public int TopCount{get;set;}=10; public bool AnnouncePersonalBest{get;set;}=true; public bool AnnounceWorldRecord{get;set;}=true; }
public sealed class MapVoteConfig { public bool Enabled{get;set;}=true; public string WorkshopPath{get;set;}=""; public bool RequireVpkFile{get;set;}=true; public int MapsToShow{get;set;}=8; public int VoteDurationSeconds{get;set;}=45; public int MapsInCooldown{get;set;}=3; public int RtvPercentage{get;set;}=60; public int RtvMinPlayers{get;set;}=1; public bool RescanOnMapLoad{get;set;}=true; public int ChangeDelaySeconds{get;set;}=3; public int MapChangeVerifySeconds{get;set;}=8; public int MapChangeMaxAttempts{get;set;}=3; public bool SetNextLevelBeforeWorkshopChange{get;set;}=true; public bool EndOfMapVoteEnabled{get;set;}=true; public int EndOfMapVoteMinutesBeforeEnd{get;set;}=4; public bool EndOfMapSameCategoryOnly{get;set;}=true; }
public sealed class MapZones { public Zone? Start{get;set;} public Zone? End{get;set;} public List<Zone> Stages{get;set;}=[]; public Dictionary<int,Vec> NativeStageTeleports{get;set;}=[]; public int Tier{get;set;}=1; }
public sealed class Zone { public Vec? P1{get;set;} public Vec? P2{get;set;} public Vec? Teleport{get;set;} public bool Contains(Vector v){if(P1 is null||P2 is null)return false;return v.X>=Math.Min(P1.X,P2.X)&&v.X<=Math.Max(P1.X,P2.X)&&v.Y>=Math.Min(P1.Y,P2.Y)&&v.Y<=Math.Max(P1.Y,P2.Y)&&v.Z>=Math.Min(P1.Z,P2.Z)&&v.Z<=Math.Max(P1.Z,P2.Z);} }
public sealed record Vec(float X,float Y,float Z);
public sealed class RunState { public bool Running; public bool Armed; public bool InStart; public bool InEnd; public bool UsingMapTriggers; public bool Ranked=true; public string Course="main"; public string? InvalidReason; public string? WrPlayerName; public bool ReferencesLoaded; public int CurrentStage=1; public long StartTimestamp; public long NextReplayCaptureMs; public double? PbSeconds; public double? WrSeconds; public HashSet<int> TouchedStages=[]; public List<double> StageTimes=[]; public List<double> PbStageTimes=[]; public List<double> WrStageTimes=[]; public List<ReplayFrame> ReplayFrames=[]; }
public sealed class PlayerPrefs { public bool Hud{get;set;}=true; public bool Practice{get;set;} public Vec? Checkpoint{get;set;} public Dictionary<string,Dictionary<int,SavedCheckpoint>>? KzCheckpoints{get;set;}=[]; public int ActiveCheckpointSlot{get;set;}=1; public SplitComparisonMode SplitComparison{get;set;}=SplitComparisonMode.Both; public SplitComparisonMode? HudComparison{get;set;} public SplitComparisonMode? ChatComparison{get;set;} }
public sealed record SavedCheckpoint(Vec Position,Vec Angles);
public enum SplitComparisonMode { Both, PersonalBest, WorldRecord, None }
public enum MapCategory { Surf, Kz, Other }
public sealed record WorkshopMap(string Id,string Title,MapCategory Category,string SourceFolder);
public sealed record RecordResult(double? OldPb,bool IsPb,bool IsWorldRecord,int Rank,int Total);
