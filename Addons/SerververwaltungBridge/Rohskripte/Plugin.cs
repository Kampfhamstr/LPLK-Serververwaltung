using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Net.Http;
using SharpCompress.Readers;
using SharpCompress.Common;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameHooks;

namespace SerververwaltungBridge;

[PluginMetadata(
    Id = "lplk.serververwaltung.bridge",
    Version = "1.6.4",
    Name = "Serververwaltung Bridge",
    Author = "LPLK",
    Description = "Lokale Bridge zwischen LPLK Serververwaltung und SwiftlyS2")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<ulong, BanEntry> _bans = new();
    // v1.5.3: serverübergreifende Bans werden vom Webpanel per Fast Live synchronisiert
    // und lokal gecacht, damit sie auch bei vorübergehend nicht erreichbarem Panel gelten.
    private readonly Dictionary<ulong, GlobalBanEntry> _globalBans = new();
    private readonly object _globalBanSync = new();
    private readonly Dictionary<ulong, CommEntry> _mutes = new();
    private readonly Dictionary<ulong, CommEntry> _gags = new();
    private readonly Dictionary<ulong, NicknameEntry> _nicknames = new();
    private readonly HashSet<ulong> _frozen = new();
    private readonly HashSet<ulong> _noclip = new();
    private readonly HashSet<ulong> _godmode = new();
    private readonly Dictionary<ulong, float> _playerSpeed = new();
    private readonly Dictionary<ulong, float> _playerGravity = new();
    private float _globalSpeed = 1.0f;
    private string _weaponMode = "normal";
    private HashSet<ushort>? _allowedWeaponDefinitionIndices;
    private readonly Dictionary<string, int> _weaponTeamLimits = new(StringComparer.Ordinal);
    private bool _weaponTeamLimitsIncludeBots;
    private string _activePreset = "";
    private string _presetDuration = "manual";
    private int _rulesRemainingRounds;
    private bool _rulesResetOnMapChange;
    private CancellationTokenSource? _rulesResetTimer;
    private Guid _roundEndHookGuid;
    private Guid _playerSpawnHookGuid;
    private readonly Dictionary<ulong, CancellationTokenSource> _freezeTimers = new();
    private Timer? _tickTimer;
    private int _tickQueued;
    private volatile bool _unloading;

    // Robuster Mapwechselpfad nach dem Muster der bewährten HeadshotOnly/BotOnly-Logik.
    private readonly List<CancellationTokenSource> _mapCountdownTimers = new();
    private CancellationTokenSource? _mapStabilizationTimer;
    private CancellationTokenSource? _mapSwitchWatchdogTimer;
    private long _mapLifecycleToken;
    private bool _mapLoaded;
    private bool _mapStable;
    private bool _changingMap;
    private bool _mapSwitchInFlight;
    private bool _mapSwitchPending;
    private bool _mapSwitchDispatchQueued;
    private bool _mapSwitchIssued;
    private MapSwitchTarget? _scheduledMapTarget;
    private MapSwitchTarget? _issuedMapTarget;

    // Exakt nach dem Prinzip von HeadshotOnly:
    // installiert ist nur, was als numerischer Workshop-Ordner tatsächlich im
    // Steam-Workshop-Content-Pfad liegt und eine VPK besitzt.
    private List<InstalledWorkshopMap> _installedWorkshopMaps = new();
    private string _resolvedWorkshopPath = string.Empty;

    // GlobalVars ist während Plugin.Load() bei SwiftlyS2 noch nicht garantiert verfügbar.
    // Deshalb führen wir einen sicheren, gecachten Mapzustand und initialisieren ihn
    // über OnMapLoad bzw. einen verzögerten Fallback wie HeadshotOnly.
    private string _currentMapName = string.Empty;
    private string _currentWorkshopId = string.Empty;
    private CancellationTokenSource? _deferredMapInitTimer;

    private string _root = string.Empty;
    private string _commands = string.Empty;
    private string _results = string.Empty;
    private string _status = string.Empty;
    private string _bansFile = string.Empty;
    private string _globalBansFile = string.Empty;
    private string _globalCommsFile = string.Empty;
    // v1.6.0: AntiAdvertisement-Outbox, zentraler Snapshot und eine eigene
    // Vorfilterung vor dem Chatarchiv. Dadurch ist die Hook-Reihenfolge der
    // beiden Plugins fuer den Werbeschutz nicht mehr relevant.
    private string _antiAdOutboxDir = string.Empty;
    private string _antiAdGlobalFile = string.Empty;
    private string _antiAdConfigFile = string.Empty;
    private DateTime _antiAdConfigLastWriteUtc = DateTime.MinValue;
    private DateTimeOffset _lastAntiAdConfigCheck = DateTimeOffset.MinValue;
    private readonly object _antiAdProtectionSync = new();
    private bool _antiAdProtectionEnabled = true;
    private bool _antiAdLogBlockedText = true;
    private readonly HashSet<ulong> _antiAdBlockedSteamIds = new();
    private readonly HashSet<string> _antiAdGlobalBlockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _antiAdGlobalAllowedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _antiAdGlobalBlockedPhrases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _antiAdGlobalMarketingKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _antiAdLocalBlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2commends.com", "jaegerservice.xyz", "sinlyxe.cc", "clash.gg"
    };
    private readonly HashSet<string> _antiAdAllowedDomains = new(StringComparer.OrdinalIgnoreCase) { "community.example.invalid" };
    private readonly HashSet<string> _antiAdMarketingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "free commends", "free commend", "free cs2", "cs2 commends", "commends",
        "commend boost", "cheap skins", "free skins", "free cases", "deposit bonus",
        "bonus code", "promo code", "discord.gg"
    };
    private readonly object _globalCommSync = new();
    private readonly Dictionary<ulong, CommEntry> _globalMutes = new();
    private readonly Dictionary<ulong, CommEntry> _globalGags = new();
    private string _mutesFile = string.Empty;
    private string _gagsFile = string.Empty;
    private string _nicknamesFile = string.Empty;
    private string _adminActionsLog = string.Empty;
    private string _permissionsFile = string.Empty;
    private readonly HashSet<ulong> _adminNotifySteamIds = new();
    private readonly Dictionary<ulong, string> _gameRolesBySteamId = new();
    private DateTime _permissionsLastWriteUtc = DateTime.MinValue;
    private bool _processingCommands;
    private string _pluginBackupsRoot = string.Empty;
    private List<StatusPlugin> _pluginInventory = new();
    private DateTimeOffset _pluginInventoryRefreshedAt = DateTimeOffset.MinValue;

    // v1.4.8: Fast Live. Ein einziger ausgehender HTTPS-Austausch transportiert
    // Status + Chat + Resultate zum Webpanel und nimmt neue Befehle entgegen.
    private FastLiveConfig? _fastLiveConfig;
    private HttpClient? _fastLiveClient;
    private string _fastLiveConfigPath = string.Empty;
    private DateTime _fastLiveConfigLastWriteUtc = DateTime.MinValue;
    private long _fastLiveConfigLength = -1;
    private DateTimeOffset _lastFastLiveConfigCheck = DateTimeOffset.MinValue;
    private int _fastLiveInFlight;
    private DateTimeOffset _lastFastLiveAttempt = DateTimeOffset.MinValue;
    private readonly ConcurrentQueue<BridgeCommand> _fastLiveCommands = new();
    private readonly object _fastLiveResultSync = new();
    private readonly List<FastLiveResult> _pendingFastLiveResults = new();
    private readonly HashSet<string> _fastLiveSeenCommandIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _fastLiveSeenCommandOrder = new();

    // Die letzten Chatnachrichten bleiben zusätzlich im status.json als Ringpuffer.
    // Das Webpanel übernimmt sie idempotent in sein dauerhaftes SQLite-Archiv.
    private readonly Queue<StatusChatMessage> _chatMessages = new();
    private long _chatSequence;

    public override void Load(bool hotReload)
    {
        _unloading = false;
        _root = Path.Combine(Core.CSGODirectory, "addons", "swiftlys2", "data", "serververwaltung_bridge");
        _commands = Path.Combine(_root, "commands");
        _results = Path.Combine(_root, "results");
        _status = Path.Combine(_root, "status.json");
        _bansFile = Path.Combine(_root, "bans.json");
        _globalBansFile = Path.Combine(_root, "global_bans.json");
        _globalCommsFile = Path.Combine(_root, "global_communication_sanctions.json");
        string antiAdRoot = Path.Combine(Core.CSGODirectory, "addons", "swiftlys2", "data", "serververwaltung_antiadvertisement");
        _antiAdOutboxDir = Path.Combine(antiAdRoot, "bridge_outbox");
        _antiAdGlobalFile = Path.Combine(antiAdRoot, "global_protection.json");
        _antiAdConfigFile = Path.Combine(antiAdRoot, "config.json");
        _mutesFile = Path.Combine(_root, "mutes.json");
        _gagsFile = Path.Combine(_root, "gags.json");
        _nicknamesFile = Path.Combine(_root, "nickname_overrides.json");
        _adminActionsLog = Path.Combine(_root, "admin_actions.log");
        _permissionsFile = Path.Combine(Core.CSGODirectory, "addons", "swiftlys2", "configs", "permissions.jsonc");
        _pluginBackupsRoot = Path.Combine(_root, "plugin_backups");
        _fastLiveConfigPath = Path.Combine(_root, "fastlive.json");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_pluginBackupsRoot);
        Directory.CreateDirectory(_commands);
        Directory.CreateDirectory(_results);
        Directory.CreateDirectory(_antiAdOutboxDir);
        LoadCachedGlobalAntiAdvertisementSnapshot();
        ReloadAntiAdvertisementPolicy(force: true);
        LoadFastLiveConfig();
        LoadPersistentData();
        RefreshAdminNotifyRecipients(force: true);

        ScanInstalledWorkshopMaps();
        RefreshPluginInventory(force: true);

        // Wie HeadshotOnly: GlobalVars im Load niemals ungefangen voraussetzen.
        string activeMap = TryGetCurrentMapName();
        if (!string.IsNullOrWhiteSpace(activeMap))
        {
            _currentMapName = activeMap;
            _currentWorkshopId = TryGetCurrentWorkshopId();
            BeginMapLoad(activeMap, hotReload ? "HotReload" : "Load-Fallback");
        }
        else
        {
            _mapLoaded = false;
            _mapStable = false;
            _changingMap = true;
            ScheduleDeferredMapInitialization(hotReload ? "HotReload" : "Load", 1);
            Core.Logger.LogInformation(
                "GlobalVars beim Plugin-Load noch nicht verfügbar. Mapzustand wird verzögert bzw. über OnMapLoad initialisiert.");
        }

        _roundEndHookGuid = Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
        _playerSpawnHookGuid = Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        Core.GameHooks.Items.CanAcquire.Pre += OnCanAcquireItemPre;
        // RepeatBySeconds stopped firing after startup/map transitions on the
        // affected SwiftlyS2 runtime. Keep the cadence independent, but always
        // marshal the actual bridge work back to the game thread.
        _tickTimer = new Timer(_ => QueueTickOnGameThread(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        TryWriteStatus();
        Core.Logger.LogInformation("SerververwaltungBridge v1.6.4 geladen. Datenpfad: {Path}", _root);
    }

    public override void Unload()
    {
        _unloading = true;
        _chatGuardInterfaces=null;
        _tickTimer?.Dispose();
        _tickTimer = null;
        Core.GameHooks.Items.CanAcquire.Pre -= OnCanAcquireItemPre;
        if (_roundEndHookGuid != Guid.Empty) Core.GameEvent.Unhook(_roundEndHookGuid);
        if (_playerSpawnHookGuid != Guid.Empty) Core.GameEvent.Unhook(_playerSpawnHookGuid);
        try { _rulesResetTimer?.Cancel(); } catch { }
        _rulesResetTimer = null;
        try { _fastLiveClient?.Dispose(); } catch { }
        _fastLiveClient = null;
        foreach (var timer in _freezeTimers.Values)
        {
            try { timer.Cancel(); } catch { }
        }
        _freezeTimers.Clear();
        CancelDeferredMapInitialization();
        _mapLifecycleToken++;
        CancelAllMapTimers();
        ResetMapSwitchState(clearIssuedTarget: true);
        _mapLoaded = false;
        _mapStable = false;
        _changingMap = false;
        SavePersistentData();
        TryWriteStatus();
    }

    [EventListener<EventDelegates.OnMapLoad>]
    public void OnMapLoad(IOnMapLoadEvent @event)
    {
        CancelDeferredMapInitialization();

        if (_rulesResetOnMapChange)
        {
            ResetLiveRules(resetPlayers: true);
            _rulesResetOnMapChange = false;
        }

        _currentMapName = NormalizeMapName(@event.MapName);
        _currentWorkshopId = TryGetCurrentWorkshopId();

        ScanInstalledWorkshopMaps();
        BeginMapLoad(@event.MapName, "OnMapLoad");
    }

    [EventListener<EventDelegates.OnMapUnload>]
    public void OnMapUnload(IOnMapUnloadEvent @event)
    {
        _mapLifecycleToken++;
        CancelAllMapTimers();

        Core.Logger.LogInformation(
            "MapUnload erkannt: Map={Map}, Ziel={Target}, Issued={Issued}, InFlight={InFlight}.",
            NormalizeMapName(@event.MapName),
            _issuedMapTarget?.DisplayName ?? _scheduledMapTarget?.DisplayName ?? "<keins>",
            _mapSwitchIssued,
            _mapSwitchInFlight);

        _mapLoaded = false;
        _mapStable = false;
        _changingMap = true;
        _mapSwitchPending = false;
        _mapSwitchDispatchQueued = false;

        // Bei einem von uns ausgelösten Wechsel bleibt der Zielzustand bis zum stabilen
        // OnMapLoad erhalten. Dadurch kann eine noch alte Engine.WorkshopId nichts überschreiben.
        _mapSwitchInFlight = _issuedMapTarget is not null;
    }

    private void OnCanAcquireItemPre(ref CanAcquireItemPreContext ctx)
    {
        if (_allowedWeaponDefinitionIndices is null && _weaponTeamLimits.Count == 0) return;

        ushort definitionIndex = ctx.Params.EconItemView.ItemDefinitionIndex;
        // C4 und missionskritische Gegenstände nie blockieren.
        if (definitionIndex == 49) return;

        if (_allowedWeaponDefinitionIndices is not null && !_allowedWeaponDefinitionIndices.Contains(definitionIndex))
        {
            ctx.SetReturn(AcquireResult.NotAllowedByProhibition);
            ctx.SetHookResult(HookResult.Stop);
            return;
        }

        if (_weaponTeamLimits.Count == 0) return;
        string? weaponName = WeaponNameFromDefinitionIndex(definitionIndex);
        if (weaponName is null || !_weaponTeamLimits.TryGetValue(weaponName, out int limit) || limit <= 0) return;

        IPlayer acquiringPlayer = ctx.Params.Player;
        if (acquiringPlayer.IsFakeClient && !_weaponTeamLimitsIncludeBots) return;

        int team = (int)acquiringPlayer.Controller.Team;
        if (team is not (2 or 3)) return;

        int holders = CountTeamWeaponHolders(team, weaponName, acquiringPlayer.PlayerID, _weaponTeamLimitsIncludeBots);
        if (holders < limit) return;

        ctx.SetReturn(AcquireResult.NotAllowedByProhibition);
        ctx.SetHookResult(HookResult.Stop);
        if (!acquiringPlayer.IsFakeClient)
            acquiringPlayer.SendChat($"[Serververwaltung] Teamlimit erreicht: {WeaponDisplayName(weaponName)} max. {limit} pro Team.");
    }

    private HookResult OnRoundEnd(EventRoundEnd @event)
    {
        if (_rulesRemainingRounds > 0)
        {
            _rulesRemainingRounds--;
            if (_rulesRemainingRounds <= 0)
                ResetLiveRules(resetPlayers: true);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        var player = @event.UserIdPlayer;
        if (player is null || player.SteamID == 0) return HookResult.Continue;
        ulong steamId = player.SteamID;
        Core.Scheduler.NextWorldUpdate(() => ReapplyPlayerOverrides(steamId));
        return HookResult.Continue;
    }

    private void ReapplyPlayerOverrides(ulong steamId)
    {
        IPlayer? player = Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(p => p.SteamID == steamId);
        if (player is null) return;
        if (_godmode.Contains(steamId)) ApplyGodmode(player, true);
        ApplyPlayerSpeed(player, _playerSpeed.TryGetValue(steamId, out float speed) ? speed : _globalSpeed);
        ApplyPlayerGravity(player, _playerGravity.TryGetValue(steamId, out float gravity) ? gravity : 1.0f);
        if (_noclip.Contains(steamId)) ApplyNoclip(player, true);
        if (_frozen.Contains(steamId)) ApplyFreeze(player, true);
    }

    [ClientChatHookHandler]
    public HookResult OnClientChat(int playerId, string text, bool teamonly)
    {
        var player = Core.PlayerManager.GetPlayer(playerId);
        if (player is null || player.IsFakeClient || player.SteamID == 0)
            return HookResult.Continue;

        // v1.5.7: Ein bereits lokal oder global gebannter Account darf im kurzen
        // Zeitfenster bis zum Disconnect keinerlei Chat mehr senden. Wichtig:
        // bewusst OHNE Chat-/Erfolgsmeldung, damit Name/Werbetext eines
        // gebannten Spam-Accounts nicht erneut öffentlich verbreitet wird.
        if (IsBanActive(player.SteamID))
            return HookResult.Stop;

        // v1.6.0: Die Bridge prueft Werbung selbst, bevor CaptureChatMessage()
        // aufgerufen wird. Lief die Bridge bisher vor AntiAdvertisement, konnte
        // ein spaeter gestoppter Text trotzdem im Webpanel-Archiv erscheinen.
        // Bekannte Werbe-IDs, Werbenamen und Werbetexte werden nun zuerst still
        // verworfen und ausschliesslich als Werbeschutz-Ereignis vorgemerkt.
        // New module owns both filtering and sanctions. Call exactly once before
        // archiving; its independent hook defers to us when this Bridge is present.
        var chatGuard=FilterWithChatGuard(playerId,text,teamonly);
        if(chatGuard is { } decision && decision!=HookResult.Continue)return decision;
        if(chatGuard is null)ReloadAntiAdvertisementPolicy();
        if (chatGuard is null && TryMatchAntiAdvertisement(player, text, out string advertisingMatch, out string advertisingAction))
        {
            QueueBridgeAntiAdvertisementEvent(player, text, advertisingMatch, advertisingAction);
            return HookResult.Stop;
        }

        bool globallyGagged;
        lock (_globalCommSync) globallyGagged = IsCommActive(_globalGags, player.SteamID);
        if (IsCommActive(_gags, player.SteamID) || globallyGagged)
        {
            player.SendChat("[Serververwaltung] Dein Textchat ist derzeit gesperrt.");
            return HookResult.Stop;
        }

        CaptureChatMessage(player, text, teamonly);
        return HookResult.Continue;
    }

    private void Tick()
    {
        try
        {
            EnforcePersistentState();
            ProcessCommands();
            TryReloadFastLiveConfigIfChanged();
            ProcessFastLiveCommands();
            TryWriteStatus();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Fehler im SerververwaltungBridge-Tick.");
        }
    }

    private void QueueTickOnGameThread()
    {
        if (_unloading || Interlocked.CompareExchange(ref _tickQueued, 1, 0) != 0)
            return;

        try
        {
            Core.Scheduler.NextWorldUpdate(() =>
            {
                try
                {
                    if (!_unloading) Tick();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickQueued, 0);
                }
            });
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _tickQueued, 0);
            if (!_unloading)
                Core.Logger.LogError(ex, "Bridge-Tick konnte nicht auf den Game-Thread eingeplant werden.");
        }
    }

    private void ProcessCommands()
    {
        if (_processingCommands)
            return;

        _processingCommands = true;
        try
        {
            string[] files;
            try
            {
                var now = DateTime.UtcNow;
                files = Directory.GetFiles(_commands, "*.json")
                    // Kurzes Settling-Fenster: Das Webpanel bzw. die Pterodactyl-
                    // API darf den kleinen JSON-Befehl vollständig publizieren,
                    // bevor die Bridge ihn liest. Nahezu gleichzeitige Befehle
                    // liegen danach gemeinsam vor und werden lexikographisch
                    // (Zeitstempel bis Mikrosekunden) abgearbeitet.
                    .Where(file =>
                        now - File.GetLastWriteTimeUtc(file)
                        >= TimeSpan.FromMilliseconds(750))
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Take(50)
                    .ToArray();
            }
            catch
            {
                return;
            }

            foreach (var file in files)
            {
                BridgeCommand? command = null;
                try
                {
                    command = JsonSerializer.Deserialize<BridgeCommand>(File.ReadAllText(file), _json);
                    if (command is null || string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Action))
                        throw new InvalidDataException("Ungültiger Bridge-Befehl.");

                    Execute(command);

                    try
                    {
                        RecordAdminAction(command, success: true, error: null, notifyInGame: true);
                    }
                    catch (Exception auditEx)
                    {
                        Core.Logger.LogWarning(auditEx, "Admin-Aktionsprotokollierung für {CommandId} fehlgeschlagen.", command.Id);
                    }

                    WriteResult(command.Id, true, "OK");
                }
                catch (Exception ex)
                {
                    if (command is not null)
                    {
                        try
                        {
                            RecordAdminAction(command, success: false, error: ex.Message, notifyInGame: false);
                        }
                        catch (Exception auditEx)
                        {
                            Core.Logger.LogWarning(auditEx, "Fehlerprotokollierung für Admin-Aktion {CommandId} fehlgeschlagen.", command.Id);
                        }
                    }

                    WriteResult(command?.Id ?? Path.GetFileNameWithoutExtension(file), false, ex.Message);
                    Core.Logger.LogWarning(ex, "Bridge-Befehl {File} fehlgeschlagen.", Path.GetFileName(file));
                }
                finally
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        finally
        {
            _processingCommands = false;
        }
    }

    private void Execute(BridgeCommand command)
    {
        var p = command.Payload ?? new Dictionary<string, JsonElement>();

        // WICHTIG:
        // Serverweite Aktionen wie broadcast, map_change und round_restart haben absichtlich
        // keine target_steamid. Eine SteamID64 wird deshalb ausschließlich in den
        // Spieleraktionen validiert.
        switch (command.Action)
        {
            case "broadcast":
                {
                    var message = GetString(p, "message", "");
                    var type = GetString(p, "type", "chat");
                    var seconds = Clamp(GetInt(p, "countdown_seconds", 0), 0, 60);
                    if (seconds > 0) ScheduleCountdown(seconds, message, type, null, null);
                    else Broadcast(type, message);
                    break;
                }
            case "private_message":
                {
                    var target = ParseTarget(command);
                    RequirePlayer(target).SendMessage(ToMessageType(GetString(p, "type", "chat")), GetString(p, "message", ""));
                    break;
                }
            case "kick":
                {
                    var target = ParseTarget(command);
                    NotifyDisconnectingTargetBeforeAction(command, target);
                    RequirePlayer(target).Kick(GetString(p, "reason", "Von der Serververwaltung entfernt."), ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                    break;
                }
            case "ban":
                {
                    var target = ParseTarget(command);
                    NotifyDisconnectingTargetBeforeAction(command, target);
                    Ban(target, Clamp(GetInt(p, "minutes", 60), 0, 525600), GetString(p, "reason", "Serverban"));
                    break;
                }
            case "unban":
                {
                    var target = ParseTarget(command);
                    _bans.Remove(target);
                    SaveBans();
                    break;
                }
            case "freeze":
                {
                    var target = ParseTarget(command);
                    SetFrozen(target, GetBool(p, "frozen", true), Clamp(GetInt(p, "seconds", 0), 0, 3600));
                    break;
                }
            case "respawn":
                {
                    var target = ParseTarget(command);
                    RequirePlayer(target).Respawn();
                    break;
                }
            case "health":
                {
                    var target = ParseTarget(command);
                    SetHealth(target, Clamp(GetInt(p, "value", 100), 1, 500));
                    break;
                }
            case "armor":
                {
                    var target = ParseTarget(command);
                    SetArmor(target, Clamp(GetInt(p, "value", 100), 0, 500));
                    break;
                }
            case "team":
                {
                    var target = ParseTarget(command);
                    SetTeam(target, GetString(p, "team", "spectator"));
                    break;
                }
            case "give_weapon":
                {
                    var target = ParseTarget(command);
                    GiveWeapon(target, GetString(p, "weapon", ""));
                    break;
                }
            case "give_weapon_group":
                {
                    GiveWeaponGroup(
                        GetString(p, "weapon", ""),
                        GetString(p, "scope", "all"),
                        GetBool(p, "notify", true),
                        GetString(p, "notify_type", "chat"),
                        GetString(p, "notify_message", "Serververwaltung: {weapon} wurde ausgegeben."));
                    break;
                }
            case "remove_weapons":
                {
                    var target = ParseTarget(command);
                    RemoveWeapons(target);
                    break;
                }
            case "nickname_set":
                {
                    var target = ParseTarget(command);
                    SetNickname(
                        target,
                        GetString(p, "nickname", ""),
                        GetString(p, "steam_name", ""));
                    break;
                }
            case "nickname_restore":
                {
                    var target = ParseTarget(command);
                    RestoreNickname(
                        target,
                        GetString(p, "steam_name", ""));
                    break;
                }
            case "mute":
                {
                    var target = ParseTarget(command);
                    SetMute(target, true, Clamp(GetInt(p, "minutes", 60), 0, 525600), GetString(p, "reason", "Voicechat gesperrt"));
                    break;
                }
            case "unmute":
                {
                    var target = ParseTarget(command);
                    SetMute(target, false, 0, "");
                    break;
                }
            case "gag":
                {
                    var target = ParseTarget(command);
                    SetGag(target, true, Clamp(GetInt(p, "minutes", 60), 0, 525600), GetString(p, "reason", "Textchat gesperrt"));
                    break;
                }
            case "ungag":
                {
                    var target = ParseTarget(command);
                    SetGag(target, false, 0, "");
                    break;
                }
            case "silence":
                {
                    var target = ParseTarget(command);
                    var minutes = Clamp(GetInt(p, "minutes", 60), 0, 525600);
                    var reason = GetString(p, "reason", "Kommunikation gesperrt");
                    SetMute(target, true, minutes, reason);
                    SetGag(target, true, minutes, reason);
                    break;
                }
            case "unsilence":
                {
                    var target = ParseTarget(command);
                    SetMute(target, false, 0, "");
                    SetGag(target, false, 0, "");
                    break;
                }
            case "slay":
                {
                    Slay(ParseTarget(command));
                    break;
                }
            case "noclip":
                {
                    SetNoclip(ParseTarget(command), GetBool(p, "enabled", true));
                    break;
                }
            case "goto_player":
                {
                    var target = ParseTarget(command);
                    var admin = ParseSteamId(
                        FirstNonEmpty(
                            GetString(p, "admin_steamid", ""),
                            GetString(p, "other_steamid", "")));

                    if (admin == target)
                        throw new InvalidOperationException("Admin/Founder und Zielspieler müssen verschieden sein.");

                    // Goto im Webpanel: der ausgewählte Admin/Founder wird ZUM Zielspieler teleportiert.
                    TeleportToPlayer(admin, target);
                    break;
                }
            case "bring_player":
                {
                    var target = ParseTarget(command);
                    var admin = ParseSteamId(
                        FirstNonEmpty(
                            GetString(p, "admin_steamid", ""),
                            GetString(p, "other_steamid", "")));

                    if (admin == target)
                        throw new InvalidOperationException("Admin/Founder und Zielspieler müssen verschieden sein.");

                    // Bring im Webpanel: der Zielspieler wird ZUM ausgewählten Admin/Founder geholt.
                    BringPlayer(admin, target);
                    break;
                }
            case "teleport_player":
                {
                    var source = ParseSteamId(GetString(p, "source_steamid", command.TargetSteamid ?? ""));
                    var destination = ParseSteamId(GetString(p, "destination_steamid", ""));
                    if (source == destination)
                        throw new InvalidOperationException("Teleport-Quelle und -Ziel müssen verschieden sein.");
                    TeleportToPlayer(source, destination);
                    break;
                }
            case "separate_players":
                {
                    SeparateOverlappingPlayers();
                    break;
                }
            case "server_command":
                {
                    ExecuteServerCommand(GetString(p, "command", ""));
                    break;
                }
            case "whitelist_remove_sync":
                {
                    ExecuteWhitelistRemoveSync(
                        GetString(p, "remove_command", "uwl"),
                        GetStringArray(p, "steam_ids"),
                        GetString(p, "list_path", ""),
                        Clamp(GetInt(p, "settle_milliseconds", 1400), 500, 5000));
                    break;
                }
            case "cvar_set":
                {
                    SetCvar(GetString(p, "name", ""), GetString(p, "value", ""));
                    break;
                }
            case "godmode":
                SetGodmode(ParseTarget(command), GetBool(p, "enabled", true));
                break;
            case "player_speed":
                SetPlayerSpeed(ParseTarget(command), Clamp(GetFloat(p, "value", 1.0f), 0.25f, 3.0f));
                break;
            case "player_gravity":
                SetPlayerGravity(ParseTarget(command), Clamp(GetFloat(p, "value", 1.0f), 0.1f, 3.0f));
                break;
            case "bot_player_kick":
                {
                    var bot = RequireBot(GetInt(p, "user_id", 0), GetInt(p, "player_id", -1));
                    bot.Kick("Von der Serververwaltung entfernt.", ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                    break;
                }
            case "bot_player_slay":
                {
                    SlayPlayer(RequireBot(GetInt(p, "user_id", 0), GetInt(p, "player_id", -1)));
                    break;
                }
            case "bot_player_team":
                {
                    SetTeam(RequireBot(GetInt(p, "user_id", 0), GetInt(p, "player_id", -1)), GetString(p, "team", "spectator"));
                    break;
                }
            case "bot_kick":
                ExecuteServerCommand("bot_kick");
                break;
            case "bot_add":
                ExecuteServerCommand(string.Equals(GetString(p, "team", "ct"), "t", StringComparison.OrdinalIgnoreCase) ? "bot_add_t" : "bot_add_ct");
                break;
            case "bot_quota":
                SetCvar("bot_quota", Clamp(GetInt(p, "value", 0), 0, 30).ToString(CultureInfo.InvariantCulture));
                break;
            case "bot_stop":
                SetCvar("bot_stop", GetBool(p, "enabled", true) ? "1" : "0");
                break;
            case "bot_difficulty":
                SetCvar("bot_difficulty", Clamp(GetInt(p, "value", 2), 0, 5).ToString(CultureInfo.InvariantCulture));
                break;
            case "server_rules":
                ApplyServerRules(p);
                break;
            case "headshot_only":
                SetCvar("mp_damage_headshot_only", GetBool(p, "enabled", false) ? "1" : "0");
                break;
            case "weapon_mode":
                SetWeaponTeamLimits(GetIntDictionary(p, "team_limits"), GetBool(p, "team_limits_include_bots", false));
                SetWeaponMode(GetString(p, "mode", "normal"), GetStringArray(p, "custom_allowed"), GetBool(p, "apply_now", false));
                break;
            case "scope_action":
                ApplyScopeAction(GetString(p, "scope", "all"), GetString(p, "operation", ""), GetInt(p, "value", 100));
                break;
            case "round_control":
                ApplyRoundControl(GetString(p, "operation", ""));
                break;
            case "warmup_control":
                ApplyWarmupControl(GetString(p, "operation", ""));
                break;
            case "rules_reset":
                ResetLiveRules(GetBool(p, "reset_players", false));
                break;
            case "preset_apply":
                ApplyPreset(p);
                break;
            case "map_change":
                ScheduleMapChange(
                    GetString(p, "map", ""),
                    GetString(p, "map_name", ""),
                    GetString(p, "source_folder", ""),
                    Clamp(GetInt(p, "countdown_seconds", 5), 0, 60),
                    GetString(p, "message", "Mapwechsel zu {map} in {seconds} Sekunden."),
                    GetString(p, "type", "center"));
                break;
            case "round_restart":
                ScheduleCountdown(
                    Clamp(GetInt(p, "countdown_seconds", 5), 0, 60),
                    GetString(p, "message", "Rundenneustart in {seconds} Sekunden."),
                    GetString(p, "type", "center"),
                    null,
                    () => Core.Engine.ExecuteCommand("mp_restartgame 0"));
                break;
            case "workshop_rescan":
                ScanInstalledWorkshopMaps();
                break;
            case "plugin_rescan":
                RefreshPluginInventory(force: true);
                break;
            case "plugin_update":
                UpdatePluginFromGitHub(
                    GetString(p, "plugin_id", ""),
                    GetString(p, "download_url", ""),
                    GetString(p, "asset_name", ""),
                    GetString(p, "expected_sha256", ""),
                    GetString(p, "expected_version", ""),
                    GetString(p, "install_subpath", ""));
                break;
            case "plugin_rollback":
                RollbackPlugin(
                    GetString(p, "plugin_id", ""),
                    GetString(p, "backup_id", ""));
                break;
            default:
                throw new InvalidOperationException("Unbekannte Aktion: " + command.Action);
        }
    }

    private void RecordAdminAction(
        BridgeCommand command,
        bool success,
        string? error,
        bool notifyInGame)
    {
        var timestamp = DateTimeOffset.Now.ToString(
            "dd.MM.yy HH:mm:ss",
            CultureInfo.InvariantCulture);

        var actor = SanitizeAdminLogValue(
            string.IsNullOrWhiteSpace(command.RequestedBy)
                ? "Webpanel/System"
                : command.RequestedBy!);

        var role = FormatAdminRole(command.RequestedByRole);
        var summary = BuildAdminActionSummary(command);
        var status = success ? "OK" : "FEHLER";

        var line = string.Join(
            " | ",
            timestamp,
            status,
            actor,
            role,
            SanitizeAdminLogValue(summary),
            "ID=" + SanitizeAdminLogValue(command.Id));

        if (!success && !string.IsNullOrWhiteSpace(error))
            line += " | Fehler=" + SanitizeAdminLogValue(error!);

        Directory.CreateDirectory(_root);
        File.AppendAllText(
            _adminActionsLog,
            line + Environment.NewLine,
            Encoding.UTF8);

        Core.Logger.LogInformation("ADMIN-AKTION | {AdminActionLine}", line);

        if (!success || !notifyInGame)
            return;

        RefreshAdminNotifyRecipients();

        var chatMessage = BuildColoredAdminChatMessage(command, timestamp, actor, role);
        var scope = NormalizeNotifyScope(command.NotifyScope);
        var alwaysTarget = command.NotifyTarget;
        var targetPreNotified =
            alwaysTarget && IsDisconnectingTargetAction(command.Action);

        ulong targetSteamId = 0;
        _ = ulong.TryParse(command.TargetSteamid, out targetSteamId);

        int delivered = 0;
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.IsFakeClient || player.SteamID == 0)
                continue;

            bool inGeneralScope = scope == "all"
                || _adminNotifySteamIds.Contains(player.SteamID);

            bool isTarget = alwaysTarget
                && targetSteamId != 0
                && player.SteamID == targetSteamId
                && !targetPreNotified;

            if (!inGeneralScope && !isTarget)
                continue;

            player.SendChat(chatMessage);
            delivered++;
        }

        Core.Logger.LogInformation(
            "Admin-Aktionsmeldung gesendet: Empfänger={Delivered}, Scope={Scope}, ZielImmer={AlwaysTarget}.",
            delivered,
            scope,
            alwaysTarget);
    }

    private void RefreshAdminNotifyRecipients(bool force = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_permissionsFile) || !File.Exists(_permissionsFile))
            {
                _adminNotifySteamIds.Clear();
                _gameRolesBySteamId.Clear();
                _permissionsLastWriteUtc = DateTime.MinValue;
                return;
            }

            var writeUtc = File.GetLastWriteTimeUtc(_permissionsFile);
            if (!force && writeUtc == _permissionsLastWriteUtc)
                return;

            var nextAdmins = new HashSet<ulong>();
            var nextRoles = new Dictionary<ulong, string>();

            using var document = JsonDocument.Parse(
                File.ReadAllText(_permissionsFile),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            var root = document.RootElement;
            if (root.TryGetProperty("Permissions", out var permissions)
                && permissions.ValueKind == JsonValueKind.Object
                && permissions.TryGetProperty("Players", out var players)
                && players.ValueKind == JsonValueKind.Object)
            {
                foreach (var playerProperty in players.EnumerateObject())
                {
                    if (!ulong.TryParse(playerProperty.Name, out var steamId) || steamId == 0)
                        continue;

                    if (playerProperty.Value.ValueKind != JsonValueKind.Array)
                        continue;

                    var groups = playerProperty.Value
                        .EnumerateArray()
                        .Where(group => group.ValueKind == JsonValueKind.String)
                        .Select(group => (group.GetString() ?? string.Empty).Trim())
                        .Where(group => !string.IsNullOrWhiteSpace(group))
                        .ToArray();

                    var gameRole = ResolveHighestGameRole(groups);
                    nextRoles[steamId] = gameRole;

                    if (string.Equals(gameRole, "founder", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(gameRole, "admin", StringComparison.OrdinalIgnoreCase))
                    {
                        nextAdmins.Add(steamId);
                    }
                }
            }

            _adminNotifySteamIds.Clear();
            foreach (var steamId in nextAdmins)
                _adminNotifySteamIds.Add(steamId);

            _gameRolesBySteamId.Clear();
            foreach (var pair in nextRoles)
                _gameRolesBySteamId[pair.Key] = pair.Value;

            _permissionsLastWriteUtc = writeUtc;

            Core.Logger.LogInformation(
                "Admin-Aktionsrollen aktualisiert: {Admins} Admin/Founder, {Roles} Spielerrollen aus permissions.jsonc.",
                _adminNotifySteamIds.Count,
                _gameRolesBySteamId.Count);
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning(
                ex,
                "Admin/Founder für Admin-Aktionsbenachrichtigungen konnten nicht aus permissions.jsonc gelesen werden.");
        }
    }

    private void NotifyDisconnectingTargetBeforeAction(
        BridgeCommand command,
        ulong targetSteamId)
    {
        if (!command.NotifyTarget || targetSteamId == 0)
            return;

        var player = Core.PlayerManager.GetAllValidPlayers()
            .FirstOrDefault(candidate =>
                !candidate.IsFakeClient
                && candidate.SteamID == targetSteamId);

        if (player is null)
            return;

        RefreshAdminNotifyRecipients();

        var timestamp = DateTimeOffset.Now.ToString(
            "dd.MM.yy HH:mm:ss",
            CultureInfo.InvariantCulture);

        var actor = SanitizeAdminLogValue(
            string.IsNullOrWhiteSpace(command.RequestedBy)
                ? "Webpanel/System"
                : command.RequestedBy!);

        var role = FormatAdminRole(command.RequestedByRole);
        player.SendChat(BuildColoredAdminChatMessage(command, timestamp, actor, role));
    }

    private string BuildColoredAdminChatMessage(
        BridgeCommand command,
        string timestamp,
        string actor,
        string actorRoleLabel)
    {
        RefreshAdminNotifyRecipients();

        var actorColor = AdminRoleChatColor(command.RequestedByRole);
        var action = BuildColoredAdminActionSummary(command);

        return
            $"[white][ADMIN] {timestamp} | " +
            $"{actorColor}{Limit(actor, 48)} ({actorRoleLabel})" +
            $"[white] | {action}[default]";
    }

    private string BuildColoredAdminActionSummary(BridgeCommand command)
    {
        var p = command.Payload ?? new Dictionary<string, JsonElement>();
        var target = DescribeColoredAdminTarget(command.TargetSteamid);

        return command.Action switch
        {
            "kick" => $"Kick: {target}[white] | Grund: {GetString(p, "reason", "-")}",
            "ban" => $"Ban: {target}[white] | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unban" => $"Unban: {target}[white]",
            "mute" => $"Mute: {target}[white] | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unmute" => $"Unmute: {target}[white]",
            "gag" => $"Gag: {target}[white] | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "ungag" => $"Ungag: {target}[white]",
            "silence" => $"Silence: {target}[white] | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unsilence" => $"Unsilence: {target}[white]",
            "slay" => $"Slay: {target}[white]",
            "bot_player_kick" => $"Bot kicken: {DescribeBotTarget(p)}[white]",
            "bot_player_slay" => $"Bot töten: {DescribeBotTarget(p)}[white]",
            "bot_player_team" => $"Bot-Team: {DescribeBotTarget(p)}[white] -> {GetString(p, "team", "-")}",
            "freeze" => $"{(GetBool(p, "frozen", true) ? "Freeze" : "Unfreeze")}: {target}[white]",
            "respawn" => $"Respawn: {target}[white]",
            "health" => $"Health: {target}[white] -> {GetInt(p, "value", 100)} HP",
            "armor" => $"Armor: {target}[white] -> {GetInt(p, "value", 100)}",
            "team" => $"Team: {target}[white] -> {GetString(p, "team", "-")}",
            "noclip" => $"Noclip {(GetBool(p, "enabled", true) ? "AN" : "AUS")}: {target}[white]",
            "goto_player" => $"Goto: {DescribeColoredSteamIdFromPayload(p, "admin_steamid")}[white] -> {target}[white]",
            "bring_player" => $"Bring: {target}[white] -> {DescribeColoredSteamIdFromPayload(p, "admin_steamid")}[white]",
            "teleport_player" => $"Teleport: {DescribeColoredSteamIdFromPayload(p, "source_steamid")}[white] -> {DescribeColoredSteamIdFromPayload(p, "destination_steamid")}[white]",
            "separate_players" => "Überlappende Spieler trennen",
            "give_weapon" => $"Waffe geben: {target}[white] -> {GetString(p, "weapon", "-")}",
            "give_weapon_group" => $"Gruppenwaffe: {GetString(p, "weapon", "-")} | Bereich: {GetString(p, "scope", "all")}",
            "remove_weapons" => $"Waffen entfernen: {target}[white]",
            "nickname_set" => $"Nickname setzen: {target}[white] -> {GetString(p, "nickname", "-")}",
            "nickname_restore" => $"Nickname zurücksetzen: {target}[white]",
            "private_message" => $"Private Nachricht an {target}[white]",
            "broadcast" => $"Servernachricht: {Limit(GetString(p, "message", ""), 120)}",
            "map_change" => $"Mapwechsel: {FirstNonEmpty(GetString(p, "map_name", ""), GetString(p, "map", ""))}",
            "round_restart" => "Runde neu starten",
            "server_command" => $"Serverbefehl: {Limit(GetString(p, "command", ""), 140)}",
            "whitelist_remove_sync" => $"Whitelist entfernen + Datei abgleichen: {GetStringArray(p, "steam_ids").Count} SteamID(s)",
            "cvar_set" => $"ConVar: {GetString(p, "name", "-")} = {GetString(p, "value", "-")}",
            "workshop_rescan" => "Workshop-Maps neu einlesen",
            "plugin_rescan" => "Plugin-Inventar neu einlesen",
            "plugin_update" => $"Plugin aktualisieren: {GetString(p, "plugin_id", "-")}",
            "plugin_rollback" => $"Plugin-Rollback: {GetString(p, "plugin_id", "-")}",
            _ => command.Action
        };
    }

    private string DescribeBotTarget(Dictionary<string, JsonElement> payload)
    {
        var userId = GetInt(payload, "user_id", 0);
        var bot = userId > 0 ? Core.PlayerManager.GetBots().FirstOrDefault(player => player.UserID == userId) : null;
        var name = bot is null ? "BOT" : SanitizeAdminLogValue(bot.Name);
        return $"{Limit(string.IsNullOrWhiteSpace(name) ? "BOT" : name, 48)} (UserID {userId})";
    }

    private string DescribeColoredAdminTarget(string? steamIdValue)
    {
        if (!ulong.TryParse(steamIdValue, out var steamId) || steamId == 0)
            return "[grey]-";

        var role = ResolveGameRole(steamId);
        var roleLabel = FormatGameRole(role);
        var color = GameRoleChatColor(role);

        string display = steamId.ToString();
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.SteamID != steamId)
                continue;

            var name = SanitizeAdminLogValue(player.Name);
            if (!string.IsNullOrWhiteSpace(name))
                display = name;
            break;
        }

        return $"{color}{Limit(display, 48)} ({roleLabel})";
    }

    private string DescribeColoredSteamIdFromPayload(
        Dictionary<string, JsonElement> payload,
        string key)
    {
        return DescribeColoredAdminTarget(GetString(payload, key, ""));
    }

    private string ResolveGameRole(ulong steamId)
    {
        RefreshAdminNotifyRecipients();
        return _gameRolesBySteamId.TryGetValue(steamId, out var role)
            ? role
            : "spieler";
    }

    private static string ResolveHighestGameRole(IEnumerable<string> groups)
    {
        var set = new HashSet<string>(
            groups.Select(group => group.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (set.Contains("founder")) return "founder";
        if (set.Contains("admin")) return "admin";
        if (set.Contains("member")) return "member";
        if (set.Contains("vip")) return "vip";
        if (set.Contains("spieler")) return "spieler";
        return "spieler";
    }

    private static string GameRoleChatColor(string? role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "founder" => "[darkred]",
            "admin" => "[red]",
            "member" => "[blue]",
            "vip" => "[green]",
            "spieler" => "[yellow]",
            _ => "[yellow]"
        };
    }

    private static string FormatGameRole(string? role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "founder" => "Founder",
            "admin" => "Admin",
            "member" => "Member",
            "vip" => "VIP",
            "spieler" => "Spieler",
            _ => "Spieler"
        };
    }

    private static string NormalizeNotifyScope(string? scope)
    {
        return string.Equals((scope ?? "").Trim(),"all",StringComparison.OrdinalIgnoreCase)
            ? "all"
            : "admins";
    }

    private static bool IsDisconnectingTargetAction(string? action)
    {
        return string.Equals(action,"kick",StringComparison.OrdinalIgnoreCase)
            || string.Equals(action,"ban",StringComparison.OrdinalIgnoreCase);
    }

    private string BuildAdminActionSummary(BridgeCommand command)
    {
        var p = command.Payload ?? new Dictionary<string, JsonElement>();
        var target = DescribeAdminTarget(command.TargetSteamid);

        return command.Action switch
        {
            "kick" => $"Kick: {target} | Grund: {GetString(p, "reason", "-")}",
            "ban" => $"Ban: {target} | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unban" => $"Unban: {target}",
            "mute" => $"Mute: {target} | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unmute" => $"Unmute: {target}",
            "gag" => $"Gag: {target} | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "ungag" => $"Ungag: {target}",
            "silence" => $"Silence: {target} | {GetInt(p, "minutes", 0)} Min. | Grund: {GetString(p, "reason", "-")}",
            "unsilence" => $"Unsilence: {target}",
            "slay" => $"Slay: {target}",
            "bot_player_kick" => $"Bot kicken: {DescribeBotTarget(p)}",
            "bot_player_slay" => $"Bot töten: {DescribeBotTarget(p)}",
            "bot_player_team" => $"Bot-Team: {DescribeBotTarget(p)} -> {GetString(p, "team", "-")}",
            "freeze" => $"{(GetBool(p, "frozen", true) ? "Freeze" : "Unfreeze")}: {target}",
            "respawn" => $"Respawn: {target}",
            "health" => $"Health: {target} -> {GetInt(p, "value", 100)} HP",
            "armor" => $"Armor: {target} -> {GetInt(p, "value", 100)}",
            "team" => $"Team: {target} -> {GetString(p, "team", "-")}",
            "noclip" => $"Noclip {(GetBool(p, "enabled", true) ? "AN" : "AUS")}: {target}",
            "goto_player" => $"Goto: {DescribeSteamIdFromPayload(p, "admin_steamid")} -> {target}",
            "bring_player" => $"Bring: {target} -> {DescribeSteamIdFromPayload(p, "admin_steamid")}",
            "teleport_player" => $"Teleport: {DescribeSteamIdFromPayload(p, "source_steamid")} -> {DescribeSteamIdFromPayload(p, "destination_steamid")}",
            "separate_players" => "Überlappende Spieler trennen",
            "give_weapon" => $"Waffe geben: {target} -> {GetString(p, "weapon", "-")}",
            "give_weapon_group" => $"Gruppenwaffe: {GetString(p, "weapon", "-")} | Bereich: {GetString(p, "scope", "all")}",
            "remove_weapons" => $"Waffen entfernen: {target}",
            "nickname_set" => $"Nickname setzen: {target} -> {GetString(p, "nickname", "-")}",
            "nickname_restore" => $"Nickname zurücksetzen: {target}",
            "private_message" => $"Private Nachricht an {target}",
            "broadcast" => $"Servernachricht: {Limit(GetString(p, "message", ""), 120)}",
            "map_change" => $"Mapwechsel: {FirstNonEmpty(GetString(p, "map_name", ""), GetString(p, "map", ""))}",
            "round_restart" => "Runde neu starten",
            "server_command" => $"Serverbefehl: {Limit(GetString(p, "command", ""), 140)}",
            "whitelist_remove_sync" => $"Whitelist entfernen + Datei abgleichen: {GetStringArray(p, "steam_ids").Count} SteamID(s)",
            "cvar_set" => $"ConVar: {GetString(p, "name", "-")} = {GetString(p, "value", "-")}",
            "workshop_rescan" => "Workshop-Maps neu einlesen",
            "plugin_rescan" => "Plugin-Inventar neu einlesen",
            "plugin_update" => $"Plugin aktualisieren: {GetString(p, "plugin_id", "-")}",
            "plugin_rollback" => $"Plugin-Rollback: {GetString(p, "plugin_id", "-")}",
            _ => command.Action
        };
    }

    private string DescribeAdminTarget(string? steamIdValue)
    {
        if (!ulong.TryParse(steamIdValue, out var steamId) || steamId == 0)
            return "-";

        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.SteamID != steamId)
                continue;

            var name = SanitizeAdminLogValue(player.Name);
            return string.IsNullOrWhiteSpace(name)
                ? steamId.ToString()
                : $"{name} ({steamId})";
        }

        return steamId.ToString();
    }

    private string DescribeSteamIdFromPayload(
        Dictionary<string, JsonElement> payload,
        string key)
    {
        var value = GetString(payload, key, "");
        return DescribeAdminTarget(value);
    }

    private static string AdminRoleChatColor(string? role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "founder" => "[darkred]",
            "admin" => "[red]",
            "member" => "[blue]",
            _ => "[grey]"
        };
    }

    private static string FormatAdminRole(string? role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "founder" => "Founder",
            "admin" => "Admin",
            "member" => "Member",
            "system" => "System",
            "" => "System",
            _ => SanitizeAdminLogValue(role ?? "System")
        };
    }

    private static string SanitizeAdminLogValue(string value)
    {
        value = (value ?? "")
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal)
            .Trim();

        return Limit(value, 500);
    }

    private bool IsBanActive(ulong steamId)
    {
        var now = DateTimeOffset.UtcNow;

        if (_bans.TryGetValue(steamId, out var localBan)
            && (localBan.ExpiresAt is null || localBan.ExpiresAt > now))
            return true;

        lock (_globalBanSync)
        {
            if (_globalBans.TryGetValue(steamId, out var globalBan)
                && (globalBan.Permanent || globalBan.ExpiresAt is null || globalBan.ExpiresAt > now))
                return true;
        }

        return false;
    }

    private void EnforcePersistentState()
    {
        var now = DateTimeOffset.UtcNow;

        bool globalBansChanged;
        lock (_globalBanSync) globalBansChanged = RemoveExpired(_globalBans, now);
        bool bansChanged = RemoveExpired(_bans, now);
        bool mutesChanged = RemoveExpired(_mutes, now);
        bool gagsChanged = RemoveExpired(_gags, now);
        bool globalCommsChanged;
        lock (_globalCommSync)
        {
            globalCommsChanged = RemoveExpired(_globalMutes, now) | RemoveExpired(_globalGags, now);
        }
        if (globalBansChanged) SaveGlobalBans();
        if (globalCommsChanged) SaveGlobalComms();
        if (bansChanged) SaveBans();
        if (mutesChanged) SaveMutes();
        if (gagsChanged) SaveGags();

        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.IsFakeClient || player.SteamID == 0) continue;
            var sid = player.SteamID;

            GlobalBanEntry? globalBan;
            lock (_globalBanSync) _globalBans.TryGetValue(sid, out globalBan);
            if (globalBan is not null)
            {
                string duration = globalBan.Permanent || globalBan.ExpiresAt is null
                    ? "Permanent"
                    : globalBan.ExpiresAt.Value.ToUniversalTime().ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
                string reason = Limit($"Global gebannt | Grund: {globalBan.Reason} | Bis: {duration} | Ban-ID #{globalBan.Id}", 240);
                KickBannedPlayerSilently(player, reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKBANADDED);
                continue;
            }

            if (_bans.TryGetValue(sid, out var ban))
            {
                var reason = ban.ExpiresAt is null
                    ? $"Permanent gebannt: {ban.Reason}"
                    : $"Gebannt bis {ban.ExpiresAt:yyyy-MM-dd HH:mm} UTC: {ban.Reason}";
                KickBannedPlayerSilently(player, reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                continue;
            }

            bool globalMute;
            lock (_globalCommSync) globalMute = IsCommActive(_globalMutes, sid);
            ApplyVoiceMute(player, IsCommActive(_mutes, sid) || globalMute);

            if (_nicknames.TryGetValue(sid, out var nickname)
                && !string.IsNullOrWhiteSpace(nickname.Override))
            {
                if (!string.Equals(player.Name, nickname.Override, StringComparison.Ordinal))
                {
                    // Ein Server-Override darf niemals den gespeicherten Steam-Profilnamen ersetzen.
                    AddNicknameHistory(nickname, player.Name);
                    ApplyPlayerName(player, nickname.Override);
                }
            }

            if (_frozen.Contains(sid)) ApplyFreeze(player, true);
            if (_noclip.Contains(sid)) ApplyNoclip(player, true);
        }
    }

    private void KickBannedPlayerSilently(IPlayer player, string reason, ENetworkDisconnectionReason disconnectReason)
    {
        // Chat ist durch OnClientChat bereits vollständig gesperrt. Zusätzlich wird
        // der sichtbare Name vor dem Disconnect neutralisiert, damit eine von CS2
        // erzeugte Leave-/Kick-Zeile keinen Werbe-/Spam-Namen wiederholt.
        // Die Engine-Systemzeile selbst wird hier nicht nachgebaut oder öffentlich
        // bestätigt; es gibt keinerlei Bridge-Erfolgsmeldung.
        try
        {
            ApplyPlayerName(player, "Gesperrter Spieler");
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Name eines gebannten Accounts konnte vor Disconnect nicht neutralisiert werden.");
        }

        ulong sid = player.SteamID;
        Core.Scheduler.NextWorldUpdate(() =>
        {
            try
            {
                var target = Core.PlayerManager.GetPlayerFromSteamId(sid, true);
                target?.Kick(reason, disconnectReason);
            }
            catch (Exception ex)
            {
                Core.Logger.LogDebug(ex, "Disconnect eines gebannten Accounts fehlgeschlagen: {SteamId}", sid);
            }
        });
    }

    private static bool RemoveExpired<TEntry>(Dictionary<ulong, TEntry> entries, DateTimeOffset now)
        where TEntry : IExpiringEntry
    {
        var expired = entries
            .Where(x => x.Value.ExpiresAt is not null && x.Value.ExpiresAt <= now)
            .Select(x => x.Key)
            .ToArray();

        foreach (var id in expired) entries.Remove(id);
        return expired.Length > 0;
    }

    private static bool IsCommActive(Dictionary<ulong, CommEntry> entries, ulong steamId)
    {
        if (!entries.TryGetValue(steamId, out var entry))
            return false;

        return entry.ExpiresAt is null || entry.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private static bool IsCommEntryActive(CommEntry? entry)
        => entry is not null && (entry.ExpiresAt is null || entry.ExpiresAt > DateTimeOffset.UtcNow);

    private void Ban(ulong steamId, int minutes, string reason)
    {
        _bans[steamId] = new BanEntry
        {
            Reason = Limit(reason, 160),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes)
        };
        SaveBans();
        var player = Core.PlayerManager.GetPlayerFromSteamId(steamId, true);
        if (player is not null)
            KickBannedPlayerSilently(player, reason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
    }

    private void SetFrozen(ulong steamId, bool frozen, int seconds = 0)
    {
        if (_freezeTimers.Remove(steamId, out var existingTimer))
        {
            try { existingTimer.Cancel(); } catch { }
        }

        if (frozen) _frozen.Add(steamId); else _frozen.Remove(steamId);
        var player = RequirePlayer(steamId);
        ApplyFreeze(player, frozen);

        if (frozen && seconds > 0)
        {
            var timer = Core.Scheduler.DelayBySeconds(seconds, () =>
            {
                _freezeTimers.Remove(steamId);
                _frozen.Remove(steamId);
                var online = Core.PlayerManager.GetPlayerFromSteamId(steamId, true);
                if (online is not null) ApplyFreeze(online, false);
            });
            _freezeTimers[steamId] = timer;
        }
    }

    private static void ApplyFreeze(IPlayer player, bool frozen)
    {
        var pawn = player.PlayerPawn;
        if (pawn is null) return;
        pawn.MoveType = (MoveType_t)(frozen ? 0 : 2); // Source 2: NONE=0, WALK=2
        pawn.MoveTypeUpdated();
        pawn.ActualMoveType = (MoveType_t)(frozen ? 0 : 2);
    }

    private void SetMute(ulong steamId, bool muted, int minutes, string reason)
    {
        if (muted)
        {
            _mutes[steamId] = new CommEntry
            {
                Reason = Limit(reason.Trim(), 160),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes)
            };
        }
        else
        {
            _mutes.Remove(steamId);
        }

        SaveMutes();
        var player = Core.PlayerManager.GetPlayerFromSteamId(steamId, true);
        if (player is not null) ApplyVoiceMute(player, muted);
    }

    private void SetGag(ulong steamId, bool gagged, int minutes, string reason)
    {
        if (gagged)
        {
            _gags[steamId] = new CommEntry
            {
                Reason = Limit(reason.Trim(), 160),
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes)
            };
        }
        else
        {
            _gags.Remove(steamId);
        }

        SaveGags();
    }

    private static void ApplyVoiceMute(IPlayer player, bool muted)
    {
        var flags = player.VoiceFlags;
        var next = muted
            ? flags | VoiceFlagValue.Muted
            : flags & ~VoiceFlagValue.Muted;

        if (next != flags)
            player.VoiceFlags = next;
    }

    private void Slay(ulong steamId)
    {
        SlayPlayer(RequirePlayer(steamId));
    }

    private static void SlayPlayer(IPlayer player)
    {
        var pawn = player.PlayerPawn
            ?? throw new InvalidOperationException("Spieler-Pawn ist nicht verfügbar.");
        pawn.CommitSuicide(false, true);
    }

    private void SetNoclip(ulong steamId, bool enabled)
    {
        if (enabled) _noclip.Add(steamId); else _noclip.Remove(steamId);
        ApplyNoclip(RequirePlayer(steamId), enabled);
    }

    private static void ApplyNoclip(IPlayer player, bool enabled)
    {
        ref bool noClip = ref player.Controller.NoClipEnabled;
        if (noClip == enabled) return;
        noClip = enabled;
        player.Controller.NoClipEnabledUpdated();
    }

    private void TeleportToPlayer(ulong moverSteamId, ulong destinationSteamId)
    {
        var mover = RequirePlayer(moverSteamId);
        var destination = RequirePlayer(destinationSteamId);
        var destinationPawn = destination.PlayerPawn
            ?? throw new InvalidOperationException("Ziel-Pawn ist nicht verfügbar.");
        var origin = destinationPawn.AbsOrigin
            ?? throw new InvalidOperationException("Zielposition ist nicht verfügbar.");
        var angle = destinationPawn.EyeAngles;

        var safeOrigin = FindSafeNearbyTeleportPosition(
            moverSteamId,
            destinationSteamId,
            origin,
            angle);

        mover.Teleport(safeOrigin, angle, Vector.Zero);
    }

    private void BringPlayer(ulong anchorSteamId, ulong otherSteamId)
    {
        var anchor = RequirePlayer(anchorSteamId);
        var other = RequirePlayer(otherSteamId);
        var anchorPawn = anchor.PlayerPawn
            ?? throw new InvalidOperationException("Anker-Pawn ist nicht verfügbar.");
        var origin = anchorPawn.AbsOrigin
            ?? throw new InvalidOperationException("Ankerposition ist nicht verfügbar.");
        var angle = anchorPawn.EyeAngles;

        var safeOrigin = FindSafeNearbyTeleportPosition(
            otherSteamId,
            anchorSteamId,
            origin,
            angle);

        other.Teleport(safeOrigin, angle, Vector.Zero);
    }

    private Vector FindSafeNearbyTeleportPosition(
        ulong moverSteamId,
        ulong anchorSteamId,
        Vector anchorOrigin,
        QAngle anchorAngle)
    {
        // CS2-Spielerhülle konservativ etwas größer als die reine Pawn-Breite.
        // Dadurch vermeiden wir auch direkte Berührungen nach dem Teleport.
        var bounds = new BBox_t
        {
            Mins = new Vector(-18.0f, -18.0f, 0.0f),
            Maxs = new Vector(18.0f, 18.0f, 72.0f)
        };

        anchorAngle.ToDirectionVectors(out var forward, out var right, out _);

        forward.Z = 0.0f;
        right.Z = 0.0f;

        if (forward.Length2DSquared() < 0.001f)
            forward = new Vector(1.0f, 0.0f, 0.0f);
        else
            forward = forward.Normalized2D();

        if (right.Length2DSquared() < 0.001f)
            right = new Vector(0.0f, 1.0f, 0.0f);
        else
            right = right.Normalized2D();

        var diagonalRightForward = (right + forward).Normalized2D();
        var diagonalRightBack = (right - forward).Normalized2D();

        // Bevorzugt direkt rechts/links neben dem Ziel. Falls dort Wand,
        // Kiste oder Spieler steht, werden weitere Plätze rundherum geprüft.
        var directions = new[]
        {
            right,
            -right,
            forward,
            -forward,
            diagonalRightForward,
            -diagonalRightForward,
            diagonalRightBack,
            -diagonalRightBack
        };

        // 56 Einheiten reichen für zwei Spielerhüllen nebeneinander.
        // Größere Ringe dienen als Fallback in engen Bereichen.
        var distances = new[] { 56.0f, 72.0f, 88.0f };

        foreach (var distance in distances)
        {
            foreach (var direction in directions)
            {
                var candidate = anchorOrigin + direction * distance;

                if (!IsTeleportPositionFree(
                        candidate,
                        bounds,
                        moverSteamId,
                        anchorSteamId))
                    continue;

                Core.Logger.LogInformation(
                    "Kollisionsfreier Teleportplatz gefunden: Mover={Mover}, Anchor={Anchor}, Abstand={Distance}.",
                    moverSteamId,
                    anchorSteamId,
                    distance);

                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Keine kollisionsfreie Position neben dem Zielspieler gefunden. Teleport wurde abgebrochen.");
    }

    private bool IsTeleportPositionFree(
        Vector candidate,
        BBox_t bounds,
        ulong moverSteamId,
        ulong anchorSteamId)
    {
        // 1) Welt-/Entity-Kollision mit einer spielergroßen Bounding Box.
        // Start == Ende prüft, ob die gewünschte Zielhülle bereits in Solid steckt.
        var trace = Core.Trace.TracePlayerBBox(candidate, candidate, bounds);
        if (trace.StartInSolid || trace.DidHit)
            return false;

        // 2) Zusätzliche explizite Spielerabstandsprüfung.
        // Diese bleibt absichtlich zusätzlich zum Trace aktiv, damit auch bei
        // mapspezifischen Collision-Layern niemand ineinander teleportiert wird.
        const float minPlayerDistance2D = 44.0f;
        const float maxRelevantVerticalDifference = 80.0f;
        var minDistanceSquared = minPlayerDistance2D * minPlayerDistance2D;

        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (!player.IsAlive || player.SteamID == 0)
                continue;

            // Eigene alte Position darf die neue Position nicht blockieren.
            if (player.SteamID == moverSteamId)
                continue;

            var pawn = player.PlayerPawn;
            var origin = pawn?.AbsOrigin;
            if (origin is null)
                continue;

            var dz = MathF.Abs(candidate.Z - origin.Value.Z);
            if (dz >= maxRelevantVerticalDifference)
                continue;

            var dx = candidate.X - origin.Value.X;
            var dy = candidate.Y - origin.Value.Y;
            var distanceSquared = dx * dx + dy * dy;

            if (distanceSquared < minDistanceSquared)
                return false;
        }

        return true;
    }

    private void SeparateOverlappingPlayers()
    {
        const float overlapDistance2D = 34.0f;
        const float overlapVertical = 70.0f;
        const int maxPasses = 8;

        int separated = 0;
        int failedPairs = 0;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            var players = Core.PlayerManager.GetAllValidPlayers()
                .Where(player =>
                    player.SteamID != 0 &&
                    !player.IsFakeClient &&
                    player.IsAlive &&
                    player.PlayerPawn?.AbsOrigin is not null)
                .ToList();

            bool movedThisPass = false;

            for (int i = 0; i < players.Count; i++)
            {
                var first = players[i];
                var firstPawn = first.PlayerPawn;
                var firstOriginNullable = firstPawn?.AbsOrigin;
                if (firstPawn is null || firstOriginNullable is null)
                    continue;

                var firstOrigin = firstOriginNullable.Value;

                for (int j = i + 1; j < players.Count; j++)
                {
                    var second = players[j];
                    var secondPawn = second.PlayerPawn;
                    var secondOriginNullable = secondPawn?.AbsOrigin;
                    if (secondPawn is null || secondOriginNullable is null)
                        continue;

                    var secondOrigin = secondOriginNullable.Value;

                    if (!PlayersOverlap(
                            firstOrigin,
                            secondOrigin,
                            overlapDistance2D,
                            overlapVertical))
                        continue;

                    if (TrySeparatePlayer(
                            mover: second,
                            anchor: first,
                            anchorOrigin: firstOrigin,
                            anchorAngle: firstPawn.EyeAngles))
                    {
                        separated++;
                        movedThisPass = true;
                        break;
                    }

                    if (TrySeparatePlayer(
                            mover: first,
                            anchor: second,
                            anchorOrigin: secondOrigin,
                            anchorAngle: secondPawn.EyeAngles))
                    {
                        separated++;
                        movedThisPass = true;
                        break;
                    }

                    failedPairs++;
                    Core.Logger.LogWarning(
                        "Überlappende Spieler konnten nicht getrennt werden: {First} / {Second}.",
                        first.SteamID,
                        second.SteamID);
                }

                if (movedThisPass)
                    break;
            }

            if (!movedThisPass)
                break;
        }

        Core.Logger.LogInformation(
            "Spieler-Trennung abgeschlossen: {Separated} Verschiebung(en), {FailedPairs} nicht lösbare Paar(e).",
            separated,
            failedPairs);
    }

    private bool TrySeparatePlayer(
        IPlayer mover,
        IPlayer anchor,
        Vector anchorOrigin,
        QAngle anchorAngle)
    {
        try
        {
            var safeOrigin = FindSafeNearbyTeleportPosition(
                mover.SteamID,
                anchor.SteamID,
                anchorOrigin,
                anchorAngle);

            mover.Teleport(safeOrigin, anchorAngle, Vector.Zero);

            Core.Logger.LogInformation(
                "Überlappung getrennt: Spieler {Mover} wurde neben {Anchor} versetzt.",
                mover.SteamID,
                anchor.SteamID);

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool PlayersOverlap(
        Vector first,
        Vector second,
        float maxDistance2D,
        float maxVerticalDifference)
    {
        var dz = MathF.Abs(first.Z - second.Z);
        if (dz >= maxVerticalDifference)
            return false;

        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var distanceSquared = dx * dx + dy * dy;

        return distanceSquared < maxDistance2D * maxDistance2D;
    }

    private void ExecuteServerCommand(string command)
    {
        command = Limit(command.Trim(), 240);
        if (command.Length == 0)
            throw new InvalidOperationException("Serverbefehl darf nicht leer sein.");
        if (command.Any(ch => ch is '\r' or '\n' or '\0'))
            throw new InvalidOperationException("Serverbefehl enthält unzulässige Steuerzeichen.");

        Core.Logger.LogWarning("Webpanel führt Serverbefehl aus: {Command}", command);
        Core.Engine.ExecuteCommandWithBuffer(command, output =>
        {
            if (!string.IsNullOrWhiteSpace(output))
                Core.Logger.LogInformation("Serverbefehl-Ausgabe: {Output}", Limit(output.Trim(), 1000));
        });
    }

    private void ExecuteWhitelistRemoveSync(
        string removeCommand,
        List<string> steamIds,
        string listPath,
        int settleMilliseconds)
    {
        removeCommand = removeCommand.Trim();
        if (!Regex.IsMatch(removeCommand, @"^[A-Za-z0-9_.!-]{1,40}$"))
            throw new InvalidOperationException("Ungültiger Whitelist-Remove-Befehl.");

        var normalizedIds = steamIds
            .Select(value => value.Trim())
            .Where(value => Regex.IsMatch(value, @"^7656119\d{10}$"))
            .Distinct(StringComparer.Ordinal)
            .Take(500)
            .ToList();

        if (normalizedIds.Count == 0)
            throw new InvalidOperationException("Keine gültige SteamID64 zum Entfernen übergeben.");

        var resolvedListPath = ResolveWhitelistListPath(listPath);

        foreach (var steamId in normalizedIds)
            ExecuteServerCommand($"{removeCommand} {steamId}");

        Core.Logger.LogInformation(
            "Whitelist-Sync geplant: {Count} SteamID(s), Datei={Path}, Wartezeit={Delay}ms.",
            normalizedIds.Count,
            resolvedListPath,
            settleMilliseconds);

        Core.Scheduler.DelayBySeconds(settleMilliseconds / 1000f, () =>
        {
            try
            {
                ReconcileWhitelistFile(resolvedListPath, normalizedIds);
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "Whitelist-Dateiabgleich nach !uwl fehlgeschlagen.");
            }
        });
    }

    private string ResolveWhitelistListPath(string listPath)
    {
        var normalized = (listPath ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        const string gamePrefix = "game/csgo/";
        if (normalized.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[gamePrefix.Length..];

        if (!normalized.StartsWith("addons/swiftlys2/plugins/", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith("/whitelist.txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ungültiger Whitelist-Dateipfad.");

        var root = Path.GetFullPath(Path.Combine(Core.CSGODirectory, "addons", "swiftlys2", "plugins"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(
            Core.CSGODirectory,
            normalized.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("Whitelist-Dateipfad verlässt das Plugin-Verzeichnis.");

        return fullPath;
    }

    private void ReconcileWhitelistFile(string path, List<string> removeIds)
    {
        var remove = new HashSet<string>(removeIds, StringComparer.Ordinal);
        var existing = new List<string>();

        if (File.Exists(path))
        {
            foreach (var token in Regex.Split(File.ReadAllText(path), @"[\s,;]+"))
            {
                var value = token.Trim();
                if (!Regex.IsMatch(value, @"^7656119\d{10}$"))
                    continue;
                if (remove.Contains(value))
                    continue;
                if (!existing.Contains(value, StringComparer.Ordinal))
                    existing.Add(value);
            }
        }

        var content = existing.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, existing) + Environment.NewLine;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".serververwaltung.tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, true);

        Core.Logger.LogInformation(
            "Whitelist-Datei nach !uwl neu geschrieben: {Path}; {Count} Einträge verbleiben.",
            path,
            existing.Count);
    }

    private void SetGodmode(ulong steamId, bool enabled)
    {
        if (enabled) _godmode.Add(steamId); else _godmode.Remove(steamId);
        ApplyGodmode(RequirePlayer(steamId), enabled);
    }

    private static void ApplyGodmode(IPlayer player, bool enabled)
    {
        var pawn = player.PlayerPawn;
        if (pawn is null) return;
        ref bool takesDamage = ref pawn.TakesDamage;
        takesDamage = !enabled;
        pawn.TakesDamageUpdated();
    }

    private void SetPlayerSpeed(ulong steamId, float value)
    {
        if (MathF.Abs(value - 1.0f) < 0.001f) _playerSpeed.Remove(steamId);
        else _playerSpeed[steamId] = value;
        ApplyPlayerSpeed(RequirePlayer(steamId), value);
    }

    private static void ApplyPlayerSpeed(IPlayer player, float value)
    {
        var pawn = player.PlayerPawn;
        if (pawn is null) return;
        ref float modifier = ref pawn.VelocityModifier;
        modifier = value;
        pawn.VelocityModifierUpdated();
    }

    private void SetPlayerGravity(ulong steamId, float value)
    {
        if (MathF.Abs(value - 1.0f) < 0.001f) _playerGravity.Remove(steamId);
        else _playerGravity[steamId] = value;
        ApplyPlayerGravity(RequirePlayer(steamId), value);
    }

    private static void ApplyPlayerGravity(IPlayer player, float value)
    {
        var pawn = player.PlayerPawn;
        if (pawn is null) return;
        ref float scale = ref pawn.GravityScale;
        scale = value;
        pawn.GravityScaleUpdated();
    }

    private void ApplyServerRules(Dictionary<string, JsonElement> p)
    {
        if (p.ContainsKey("gravity")) SetCvar("sv_gravity", Clamp(GetInt(p, "gravity", 800), 100, 2000).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("global_speed"))
        {
            _globalSpeed = Clamp(GetFloat(p, "global_speed", 1.0f), 0.25f, 3.0f);
            foreach (var player in HumanPlayers())
            {
                if (!_playerSpeed.ContainsKey(player.SteamID)) ApplyPlayerSpeed(player, _globalSpeed);
            }
        }
        if (p.ContainsKey("friendly_fire")) SetCvar("mp_friendlyfire", GetBool(p, "friendly_fire", false) ? "1" : "0");
        if (p.ContainsKey("infinite_ammo")) SetCvar("sv_infinite_ammo", Clamp(GetInt(p, "infinite_ammo", 0), 0, 2).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("buy_anywhere")) SetCvar("mp_buy_anywhere", Clamp(GetInt(p, "buy_anywhere", 0), 0, 3).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("bunnyhop")) SetCvar("sv_enablebunnyhopping", GetBool(p, "bunnyhop", false) ? "1" : "0");
        if (p.ContainsKey("auto_bhop")) SetCvar("sv_autobunnyhopping", GetBool(p, "auto_bhop", false) ? "1" : "0");
        if (p.ContainsKey("airaccelerate")) SetCvar("sv_airaccelerate", Clamp(GetFloat(p, "airaccelerate", 12f), 1f, 2000f).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("fall_damage_scale")) SetCvar("sv_falldamage_scale", Clamp(GetFloat(p, "fall_damage_scale", 1f), 0f, 5f).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("damage_scale"))
        {
            string value = Clamp(GetFloat(p, "damage_scale", 1f), 0f, 10f).ToString(CultureInfo.InvariantCulture);
            foreach (string cvar in new[] { "mp_damage_scale_ct_head", "mp_damage_scale_ct_body", "mp_damage_scale_t_head", "mp_damage_scale_t_body" }) SetCvar(cvar, value);
        }
        if (p.ContainsKey("freeze_time")) SetCvar("mp_freezetime", Clamp(GetInt(p, "freeze_time", 15), 0, 60).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("round_time"))
        {
            string value = Clamp(GetFloat(p, "round_time", 1.92f), 0.5f, 60f).ToString(CultureInfo.InvariantCulture);
            SetCvar("mp_roundtime", value); SetCvar("mp_roundtime_defuse", value); SetCvar("mp_roundtime_hostage", value);
        }
        if (p.ContainsKey("buy_time")) SetCvar("mp_buytime", Clamp(GetInt(p, "buy_time", 20), 0, 9999).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("start_money")) SetCvar("mp_startmoney", Clamp(GetInt(p, "start_money", 800), 0, 65535).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("max_money")) SetCvar("mp_maxmoney", Clamp(GetInt(p, "max_money", 16000), 0, 65535).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("max_rounds")) SetCvar("mp_maxrounds", Clamp(GetInt(p, "max_rounds", 24), 0, 100).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("auto_team_balance")) SetCvar("mp_autoteambalance", GetBool(p, "auto_team_balance", true) ? "1" : "0");
        if (p.ContainsKey("limitteams")) SetCvar("mp_limitteams", Clamp(GetInt(p, "limitteams", 2), 0, 10).ToString(CultureInfo.InvariantCulture));
        if (p.ContainsKey("headshot_only")) SetCvar("mp_damage_headshot_only", GetBool(p, "headshot_only", false) ? "1" : "0");
        if (p.ContainsKey("weapon_mode")) SetWeaponMode(GetString(p, "weapon_mode", "normal"), [], false);
        if (p.ContainsKey("weapon_team_limits"))
            SetWeaponTeamLimits(GetIntDictionary(p, "weapon_team_limits"), GetBool(p, "weapon_team_limits_include_bots", false));
    }

    private IEnumerable<IPlayer> HumanPlayers(string scope = "all")
    {
        scope = (scope ?? "all").Trim().ToLowerInvariant();
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.IsFakeClient || player.SteamID == 0) continue;
            int team = (int)player.Controller.Team;
            if (team is not (2 or 3)) continue;
            if (scope == "t" && team != 2) continue;
            if (scope == "ct" && team != 3) continue;
            yield return player;
        }
    }

    private void ApplyScopeAction(string scope, string operation, int value)
    {
        foreach (var player in HumanPlayers(scope).ToList())
        {
            switch ((operation ?? "").Trim().ToLowerInvariant())
            {
                case "respawn": player.Respawn(); break;
                case "freeze": SetFrozen(player.SteamID, true); break;
                case "unfreeze": SetFrozen(player.SteamID, false); break;
                case "health": SetHealth(player.SteamID, Clamp(value, 1, 500)); break;
                case "armor": SetArmor(player.SteamID, Clamp(value, 0, 500)); break;
                case "godmode_on": SetGodmode(player.SteamID, true); break;
                case "godmode_off": SetGodmode(player.SteamID, false); break;
                default: throw new InvalidOperationException("Unbekannte Gruppenaktion.");
            }
        }
    }

    private void ApplyRoundControl(string operation)
    {
        switch ((operation ?? "").Trim().ToLowerInvariant())
        {
            case "swap_teams": ExecuteServerCommand("mp_swapteams"); break;
            case "scramble_teams": ExecuteServerCommand("mp_scrambleteams"); break;
            default: throw new InvalidOperationException("Unbekannte Team-/Rundenaktion.");
        }
    }

    private void ApplyWarmupControl(string operation)
    {
        switch ((operation ?? "").Trim().ToLowerInvariant())
        {
            case "start": ExecuteServerCommand("mp_warmup_start"); break;
            case "end": ExecuteServerCommand("mp_warmup_end"); break;
            default: throw new InvalidOperationException("Unbekannte Warmup-Aktion.");
        }
    }

    private void SetWeaponMode(string mode, List<string> customAllowed, bool applyNow)
    {
        mode = (mode ?? "normal").Trim().ToLowerInvariant();
        var allowedNames = ResolveWeaponMode(mode, customAllowed);
        _weaponMode = mode;
        _allowedWeaponDefinitionIndices = mode == "normal" ? null : new HashSet<ushort>(allowedNames.SelectMany(ResolveWeaponDefinitionIndices));

        // Zusätzlicher nativer CS2-Schutz: alle nicht erlaubten Definitionen verbieten.
        if (_allowedWeaponDefinitionIndices is null)
        {
            SetCvar("mp_items_prohibited", "");
        }
        else
        {
            var prohibited = WeaponDefinitionIds.Values.SelectMany(x => x).Distinct().Where(id => !_allowedWeaponDefinitionIndices.Contains(id) && id != 49).OrderBy(id => id);
            SetCvar("mp_items_prohibited", string.Join(',', prohibited));
        }

        if (applyNow && mode != "normal")
        {
            foreach (var player in HumanPlayers().ToList()) ApplyWeaponPresetNow(player, mode);
        }
    }

    private static HashSet<string> ResolveWeaponMode(string mode, List<string> customAllowed)
    {
        HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
        return mode switch
        {
            "normal" => new HashSet<string>(AllowedWeapons, StringComparer.Ordinal),
            "pistols_only" => Set("weapon_glock","weapon_hkp2000","weapon_usp_silencer","weapon_p250","weapon_fiveseven","weapon_tec9","weapon_cz75a","weapon_elite","weapon_deagle","weapon_revolver","weapon_knife"),
            "deagle_only" => Set("weapon_deagle","weapon_knife"),
            "awp_only" => Set("weapon_awp","weapon_knife"),
            "scout_only" => Set("weapon_ssg08","weapon_knife"),
            "rifles_only" => Set("weapon_galilar","weapon_famas","weapon_ak47","weapon_m4a1","weapon_m4a1_silencer","weapon_aug","weapon_sg556","weapon_knife"),
            "smg_only" => Set("weapon_mac10","weapon_mp9","weapon_mp7","weapon_mp5sd","weapon_ump45","weapon_p90","weapon_bizon","weapon_knife"),
            "shotguns_only" => Set("weapon_nova","weapon_xm1014","weapon_mag7","weapon_sawedoff","weapon_knife"),
            "snipers_only" => Set("weapon_ssg08","weapon_awp","weapon_scar20","weapon_g3sg1","weapon_knife"),
            "knife_only" => Set("weapon_knife"),
            "zeus_only" => Set("weapon_taser","weapon_knife"),
            "grenades_only" => Set("weapon_hegrenade","weapon_flashbang","weapon_smokegrenade","weapon_molotov","weapon_incgrenade","weapon_decoy","weapon_knife"),
            "custom" => new HashSet<string>(customAllowed.Where(w => AllowedWeapons.Contains(w) || w is "weapon_knife" or "weapon_taser"), StringComparer.Ordinal),
            _ => throw new InvalidOperationException("Unbekannter Waffenmodus.")
        };
    }

    private static IEnumerable<ushort> ResolveWeaponDefinitionIndices(string weapon)
        => WeaponDefinitionIds.TryGetValue(weapon, out var ids) ? ids : Array.Empty<ushort>();

    private void SetWeaponTeamLimits(Dictionary<string, int> limits, bool includeBots)
    {
        _weaponTeamLimits.Clear();
        foreach (var pair in limits)
        {
            if (!WeaponDefinitionIds.ContainsKey(pair.Key)) continue;
            int limit = Clamp(pair.Value, 0, 10);
            if (limit > 0) _weaponTeamLimits[pair.Key] = limit;
        }
        _weaponTeamLimitsIncludeBots = includeBots;
    }

    private static string? WeaponNameFromDefinitionIndex(ushort definitionIndex)
    {
        foreach (var pair in WeaponDefinitionIds)
            if (pair.Value.Contains(definitionIndex)) return pair.Key;
        return null;
    }

    private static string WeaponDisplayName(string weaponName)
        => weaponName.StartsWith("weapon_", StringComparison.Ordinal)
            ? weaponName[7..].Replace('_', ' ').ToUpperInvariant()
            : weaponName;

    private int CountTeamWeaponHolders(int team, string weaponName, int excludePlayerId, bool includeBots)
    {
        if (!WeaponDefinitionIds.TryGetValue(weaponName, out var definitionIds)) return 0;
        HashSet<ushort> acceptedDefinitionIds = new(definitionIds);
        int count = 0;
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.PlayerID == excludePlayerId) continue;
            if (!includeBots && player.IsFakeClient) continue;
            if ((int)player.Controller.Team != team) continue;
            var weaponServices = player.PlayerPawn?.WeaponServices;
            if (weaponServices is null) continue;
            bool hasWeapon = false;
            foreach (var weapon in weaponServices.MyValidWeapons)
            {
                if (acceptedDefinitionIds.Contains(weapon.AttributeManager.Item.ItemDefinitionIndex))
                {
                    hasWeapon = true;
                    break;
                }
            }
            if (hasWeapon) count++;
        }
        return count;
    }

    private static void ApplyWeaponPresetNow(IPlayer player, string mode)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.ItemServices is null) return;
        pawn.ItemServices.RemoveItems();
        int team = (int)player.Controller.Team;
        string? weapon = mode switch
        {
            "pistols_only" => team == 2 ? "weapon_glock" : "weapon_usp_silencer",
            "deagle_only" => "weapon_deagle",
            "awp_only" => "weapon_awp",
            "scout_only" => "weapon_ssg08",
            "rifles_only" => team == 2 ? "weapon_ak47" : "weapon_m4a1",
            "smg_only" => team == 2 ? "weapon_mac10" : "weapon_mp9",
            "shotguns_only" => "weapon_nova",
            "snipers_only" => "weapon_awp",
            "knife_only" => "weapon_knife",
            "zeus_only" => "weapon_taser",
            "grenades_only" => "weapon_hegrenade",
            _ => null
        };
        pawn.ItemServices.GiveItem("weapon_knife");
        if (!string.IsNullOrWhiteSpace(weapon) && weapon != "weapon_knife") pawn.ItemServices.GiveItem(weapon);
    }

    private void ApplyPreset(Dictionary<string, JsonElement> p)
    {
        string name = Limit(GetString(p, "name", GetString(p, "preset", "Preset")), 80);
        if (!p.TryGetValue("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Preset enthält keine Regeln.");

        var rules = rulesElement.EnumerateObject().ToDictionary(prop => prop.Name, prop => prop.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        if (GetBool(rules, "reset", false)) ResetLiveRules(resetPlayers: true);
        ApplyServerRules(rules);
        if (rules.TryGetValue("weapon_mode", out var weaponModeElement) && weaponModeElement.ValueKind == JsonValueKind.String)
            SetWeaponMode(weaponModeElement.GetString() ?? "normal", [], true);

        _activePreset = name;
        ConfigurePresetDuration(GetString(p, "duration", "manual"), GetInt(p, "rounds", 1), GetInt(p, "minutes", 10));
    }

    private void ConfigurePresetDuration(string duration, int rounds, int minutes)
    {
        try { _rulesResetTimer?.Cancel(); } catch { }
        _rulesResetTimer = null;
        _rulesRemainingRounds = 0;
        _rulesResetOnMapChange = false;
        _presetDuration = (duration ?? "manual").Trim().ToLowerInvariant();

        switch (_presetDuration)
        {
            case "manual": break;
            case "map": _rulesResetOnMapChange = true; break;
            case "rounds": _rulesRemainingRounds = Clamp(rounds, 1, 30); break;
            case "minutes":
                _rulesResetTimer = Core.Scheduler.DelayBySeconds(Clamp(minutes, 1, 240) * 60f, () => ResetLiveRules(resetPlayers: true));
                break;
            default: _presetDuration = "manual"; break;
        }
    }

    private void ResetLiveRules(bool resetPlayers)
    {
        try { _rulesResetTimer?.Cancel(); } catch { }
        _rulesResetTimer = null;
        _rulesRemainingRounds = 0;
        _rulesResetOnMapChange = false;
        _presetDuration = "manual";
        _activePreset = "";

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sv_gravity"]="800", ["mp_friendlyfire"]="0", ["sv_infinite_ammo"]="0", ["mp_buy_anywhere"]="0",
            ["sv_enablebunnyhopping"]="0", ["sv_autobunnyhopping"]="0", ["sv_airaccelerate"]="12", ["sv_falldamage_scale"]="1",
            ["mp_damage_scale_ct_head"]="1", ["mp_damage_scale_ct_body"]="1", ["mp_damage_scale_t_head"]="1", ["mp_damage_scale_t_body"]="1",
            ["mp_freezetime"]="15", ["mp_roundtime"]="1.92", ["mp_roundtime_defuse"]="1.92", ["mp_roundtime_hostage"]="1.92",
            ["mp_buytime"]="20", ["mp_startmoney"]="800", ["mp_maxmoney"]="16000", ["mp_maxrounds"]="24", ["mp_autoteambalance"]="1", ["mp_limitteams"]="2",
            ["mp_damage_headshot_only"]="0", ["mp_items_prohibited"]=""
        };
        foreach (var pair in defaults) SetCvar(pair.Key, pair.Value);
        _globalSpeed = 1.0f;
        _weaponMode = "normal";
        _allowedWeaponDefinitionIndices = null;
        _weaponTeamLimits.Clear();
        _weaponTeamLimitsIncludeBots = false;

        if (!resetPlayers) return;
        foreach (var player in HumanPlayers().ToList())
        {
            ulong sid = player.SteamID;
            _godmode.Remove(sid); _playerSpeed.Remove(sid); _playerGravity.Remove(sid); _noclip.Remove(sid); _frozen.Remove(sid);
            ApplyGodmode(player, false); ApplyPlayerSpeed(player, 1.0f); ApplyPlayerGravity(player, 1.0f); ApplyNoclip(player, false); ApplyFreeze(player, false);
        }
    }

    private LiveRuleStatus BuildRuleStatus()
    {
        float damageScale = ReadConVarFloat("mp_damage_scale_ct_body", 1f);
        return new LiveRuleStatus
        {
            Gravity = ReadConVarInt("sv_gravity", 800),
            GlobalSpeed = _globalSpeed,
            FriendlyFire = ReadConVarBool("mp_friendlyfire", false),
            InfiniteAmmo = ReadConVarInt("sv_infinite_ammo", 0),
            BuyAnywhere = ReadConVarInt("mp_buy_anywhere", 0),
            Bunnyhop = ReadConVarBool("sv_enablebunnyhopping", false),
            AutoBhop = ReadConVarBool("sv_autobunnyhopping", false),
            Airaccelerate = ReadConVarFloat("sv_airaccelerate", 12f),
            FallDamageScale = ReadConVarFloat("sv_falldamage_scale", 1f),
            DamageScale = damageScale,
            FreezeTime = ReadConVarInt("mp_freezetime", 15),
            RoundTime = ReadConVarFloat("mp_roundtime", 1.92f),
            BuyTime = ReadConVarInt("mp_buytime", 20),
            StartMoney = ReadConVarInt("mp_startmoney", 800),
            MaxMoney = ReadConVarInt("mp_maxmoney", 16000),
            MaxRounds = ReadConVarInt("mp_maxrounds", 24),
            AutoTeamBalance = ReadConVarBool("mp_autoteambalance", true),
            Limitteams = ReadConVarInt("mp_limitteams", 2),
            HeadshotOnly = ReadConVarBool("mp_damage_headshot_only", false),
            WeaponMode = _weaponMode,
            WeaponModeLabel = WeaponModeLabel(_weaponMode),
            WeaponTeamLimits = new Dictionary<string, int>(_weaponTeamLimits, StringComparer.Ordinal),
            WeaponTeamLimitsIncludeBots = _weaponTeamLimitsIncludeBots,
            BotQuota = ReadConVarInt("bot_quota", 0),
            BotDifficulty = ReadConVarInt("bot_difficulty", 2),
            BotStop = ReadConVarBool("bot_stop", false),
            ActivePreset = _activePreset,
            PresetDuration = _presetDuration,
            RulesRemainingRounds = _rulesRemainingRounds,
            SpecialPlayers = HumanPlayers().Where(p => _godmode.Contains(p.SteamID) || _noclip.Contains(p.SteamID) || _frozen.Contains(p.SteamID) || _playerSpeed.ContainsKey(p.SteamID) || _playerGravity.ContainsKey(p.SteamID)).Select(p => new LiveSpecialPlayerStatus
            {
                SteamId64 = p.SteamID.ToString(), Name = p.Name, Godmode = _godmode.Contains(p.SteamID), Noclip = _noclip.Contains(p.SteamID), Frozen = _frozen.Contains(p.SteamID),
                SpeedMultiplier = _playerSpeed.TryGetValue(p.SteamID, out var speed) ? speed : _globalSpeed,
                GravityScale = _playerGravity.TryGetValue(p.SteamID, out var gravity) ? gravity : 1f
            }).ToList()
        };
    }

    private string ReadConVar(string name, string fallback)
    {
        try { return Core.ConVar.FindAsString(name)?.ValueAsString ?? fallback; } catch { return fallback; }
    }
    private int ReadConVarInt(string name, int fallback) => int.TryParse(ReadConVar(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private float ReadConVarFloat(string name, float fallback) => float.TryParse(ReadConVar(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private bool ReadConVarBool(string name, bool fallback)
    {
        string value = ReadConVar(name, fallback ? "1" : "0").Trim();
        return value is "1" or "true" or "yes" or "on";
    }

    private static string WeaponModeLabel(string mode) => mode switch
    {
        "pistols_only"=>"Pistols Only", "deagle_only"=>"Deagle Only", "awp_only"=>"AWP Only", "scout_only"=>"Scout Only", "rifles_only"=>"Rifles Only",
        "smg_only"=>"SMG Only", "shotguns_only"=>"Shotguns Only", "snipers_only"=>"Snipers Only", "knife_only"=>"Knife Only", "zeus_only"=>"Zeus Only", "grenades_only"=>"Grenades Only", "custom"=>"Benutzerdefiniert", _=>"Normal"
    };

    private void SetCvar(string name, string value)
    {
        name = name.Trim();
        value = Limit(value.Trim(), 120);

        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_.-]{1,80}$"))
            throw new InvalidOperationException("Ungültiger ConVar-Name.");
        if (value.Any(ch => ch is '\r' or '\n' or '\0'))
            throw new InvalidOperationException("ConVar-Wert enthält unzulässige Steuerzeichen.");

        Core.Logger.LogWarning("Webpanel setzt ConVar: {Name} = {Value}", name, value);
        try
        {
            var convar = Core.ConVar.FindAsString(name);
            if (convar is not null)
            {
                convar.ValueAsString = value;
                return;
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "ConVar {Name} konnte nicht über Core.ConVar gesetzt werden, Console-Fallback wird verwendet.", name);
        }

        var escapedValue = EscapeCommandArgument(value);
        var command = $"{name} \"{escapedValue}\"";
        Core.Engine.ExecuteCommandWithBuffer(command, output =>
        {
            if (!string.IsNullOrWhiteSpace(output))
                Core.Logger.LogInformation("ConVar-Ausgabe: {Output}", Limit(output.Trim(), 1000));
        });
    }

    private void SetHealth(ulong steamId, int value)
    {
        var pawn = RequirePlayer(steamId).PlayerPawn ?? throw new InvalidOperationException("Spieler-Pawn ist nicht verfügbar.");
        pawn.Health = value;
        pawn.HealthUpdated();
    }

    private void SetArmor(ulong steamId, int value)
    {
        var pawn = RequirePlayer(steamId).PlayerPawn ?? throw new InvalidOperationException("Spieler-Pawn ist nicht verfügbar.");
        pawn.ArmorValue = value;
        pawn.ArmorValueUpdated();
    }

    private void SetTeam(ulong steamId, string team)
    {
        SetTeam(RequirePlayer(steamId), team);
    }

    private void SetTeam(IPlayer player, string team)
    {
        PermitTeamChange(player, (team ?? string.Empty).Trim().ToLowerInvariant() switch { "ct"=>3,"t"=>2,_=>1 });
        switch ((team ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "ct": player.SwitchTeam((Team)3); break;
            case "t": player.SwitchTeam((Team)2); break;
            default: player.ChangeTeam((Team)1); break;
        }
    }

    private void GiveWeapon(ulong steamId, string weapon)
    {
        if (!AllowedWeapons.Contains(weapon)) throw new InvalidOperationException("Waffe ist nicht freigegeben.");
        var pawn = RequirePlayer(steamId).PlayerPawn ?? throw new InvalidOperationException("Spieler-Pawn ist nicht verfügbar.");
        pawn.ItemServices?.GiveItem(weapon);
    }

    private void GiveWeaponGroup(string weapon, string scope, bool notify, string notifyType, string notifyMessage)
    {
        if (!AllowedWeapons.Contains(weapon))
            throw new InvalidOperationException("Waffe ist nicht freigegeben.");

        scope = scope.Trim().ToLowerInvariant();
        if (scope is not ("ct" or "t" or "all"))
            scope = "all";

        int affected = 0;
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
        {
            if (player.IsFakeClient || player.SteamID == 0)
                continue;

            int team = (int)player.Controller.Team;
            bool matches = scope switch
            {
                "ct" => team == 3,
                "t" => team == 2,
                _ => team is 2 or 3,
            };

            if (!matches)
                continue;

            var pawn = player.PlayerPawn;
            if (pawn?.ItemServices is null)
                continue;

            pawn.ItemServices.GiveItem(weapon);
            affected++;

            if (notify)
            {
                string message = Limit(
                    (notifyMessage ?? string.Empty)
                        .Replace("{weapon}", weapon.Replace("weapon_", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
                        .Replace("{scope}", scope.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase),
                    180);

                if (!string.IsNullOrWhiteSpace(message))
                    player.SendMessage(ToMessageType(notifyType), message);
            }
        }

        if (affected == 0)
            throw new InvalidOperationException("In der gewählten Zielgruppe ist aktuell kein menschlicher T/CT-Spieler verfügbar.");

        Core.Logger.LogInformation(
            "Webpanel-Gruppenwaffe ausgegeben: Weapon={Weapon}, Scope={Scope}, Players={Affected}.",
            weapon,
            scope,
            affected);
    }

    private void RemoveWeapons(ulong steamId)
    {
        var pawn = RequirePlayer(steamId).PlayerPawn ?? throw new InvalidOperationException("Spieler-Pawn ist nicht verfügbar.");
        pawn.ItemServices?.RemoveItems();
    }

    private void SetNickname(ulong steamId, string nickname, string preferredSteamName)
    {
        nickname = Limit(nickname.Trim(), 48);
        preferredSteamName = Limit((preferredSteamName ?? string.Empty).Trim(), 48);

        if (nickname.Length == 0)
            throw new InvalidOperationException("Nickname darf nicht leer sein.");

        var player = RequirePlayer(steamId);

        NicknameEntry entry;
        if (_nicknames.TryGetValue(steamId, out var existing))
        {
            entry = existing;
        }
        else
        {
            entry = new NicknameEntry();
            _nicknames[steamId] = entry;
        }

        NormalizeNicknameEntry(entry);

        if (string.IsNullOrWhiteSpace(entry.SteamName))
        {
            entry.SteamName = !string.IsNullOrWhiteSpace(preferredSteamName)
                ? preferredSteamName
                : player.Name;
        }
        else if (!string.IsNullOrWhiteSpace(preferredSteamName)
                 && !string.Equals(entry.SteamName, preferredSteamName, StringComparison.Ordinal))
        {
            AddNicknameHistory(entry, entry.SteamName);
            entry.SteamName = preferredSteamName;
        }

        entry.Original = entry.SteamName; // Legacy-Kompatibilität

        // Vor dem Wechsel den aktuell sichtbaren und den bisherigen Override sichern.
        AddNicknameHistory(entry, player.Name);
        AddNicknameHistory(entry, entry.Override);
        AddNicknameHistory(entry, nickname);

        entry.Override = nickname;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        SaveNicknames();
        ApplyPlayerName(player, nickname);
    }

    private void RestoreNickname(ulong steamId, string preferredSteamName)
    {
        preferredSteamName = Limit((preferredSteamName ?? string.Empty).Trim(), 48);
        var player = RequirePlayer(steamId);

        NicknameEntry entry;
        if (_nicknames.TryGetValue(steamId, out var existing))
        {
            entry = existing;
        }
        else
        {
            entry = new NicknameEntry();
            _nicknames[steamId] = entry;
        }

        NormalizeNicknameEntry(entry);

        AddNicknameHistory(entry, player.Name);
        AddNicknameHistory(entry, entry.Override);

        string steamName = FirstNonEmpty(
            preferredSteamName,
            entry.SteamName,
            entry.Original,
            player.Name);

        if (string.IsNullOrWhiteSpace(steamName))
            throw new InvalidOperationException("Kein Steam-Profilname zum Wiederherstellen verfügbar.");

        if (!string.IsNullOrWhiteSpace(entry.SteamName)
            && !string.Equals(entry.SteamName, steamName, StringComparison.Ordinal))
        {
            AddNicknameHistory(entry, entry.SteamName);
        }

        entry.SteamName = steamName;
        entry.Original = steamName;
        entry.Override = string.Empty;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        SaveNicknames();

        // Nur der Name auf diesem Gameserver wird gesetzt; das Steam-Konto wird nicht verändert.
        ApplyPlayerName(player, steamName);
    }

    private static void NormalizeNicknameEntry(NicknameEntry entry)
    {
        entry.History ??= new List<string>();

        if (string.IsNullOrWhiteSpace(entry.SteamName)
            && !string.IsNullOrWhiteSpace(entry.Original))
        {
            entry.SteamName = entry.Original;
        }

        if (string.IsNullOrWhiteSpace(entry.Original)
            && !string.IsNullOrWhiteSpace(entry.SteamName))
        {
            entry.Original = entry.SteamName;
        }
    }

    private static void AddNicknameHistory(NicknameEntry entry, string? name)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        entry.History ??= new List<string>();

        if (!entry.History.Any(x => string.Equals(x, name, StringComparison.Ordinal)))
            entry.History.Add(name);

        if (entry.History.Count > 50)
            entry.History.RemoveRange(0, entry.History.Count - 50);
    }

    private static void ApplyPlayerName(IPlayer player, string name)
    {
        player.Name = name;
        player.Controller.PlayerName = name;
        player.Controller.PlayerNameUpdated();
    }

    private void ScheduleDeferredMapInitialization(string source, int attempt)
    {
        CancelDeferredMapInitialization();

        _deferredMapInitTimer = Core.Scheduler.DelayBySeconds(1.0f, () =>
        {
            _deferredMapInitTimer = null;

            // OnMapLoad hat inzwischen bereits übernommen.
            if (_mapLoaded)
                return;

            string map = TryGetCurrentMapName();
            if (!string.IsNullOrWhiteSpace(map))
            {
                _currentMapName = map;
                _currentWorkshopId = TryGetCurrentWorkshopId();

                Core.Logger.LogInformation(
                    "Verzögerte Mapinitialisierung erfolgreich: Map={Map}, Versuch={Attempt}, Quelle={Source}.",
                    map,
                    attempt,
                    source);

                BeginMapLoad(map, $"{source}-Delayed#{attempt}");
                return;
            }

            if (attempt < 30)
            {
                ScheduleDeferredMapInitialization(source, attempt + 1);
                return;
            }

            Core.Logger.LogWarning(
                "Mapzustand konnte nach {Attempts} verzögerten Versuchen noch nicht initialisiert werden. Bridge bleibt geladen und wartet auf OnMapLoad.",
                attempt);
        });
    }

    private void CancelDeferredMapInitialization()
    {
        try { _deferredMapInitTimer?.Cancel(); } catch { }
        _deferredMapInitTimer = null;
    }

    private string TryGetCurrentMapName()
    {
        try
        {
            return NormalizeMapName(Core.Engine.GlobalVars.MapName.Value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private string TryGetCurrentWorkshopId()
    {
        try
        {
            return Core.Engine.WorkshopId.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ScanInstalledWorkshopMaps()
    {
        string root = ResolveWorkshopPath();
        _resolvedWorkshopPath = root;

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            _installedWorkshopMaps = new List<InstalledWorkshopMap>();
            Core.Logger.LogWarning(
                "Workshop-Pfad nicht gefunden. Letzter Kandidat={Path}.",
                root);
            return;
        }

        var installed = new List<InstalledWorkshopMap>();
        int numericFolders = 0;
        int missingPublishData = 0;
        int missingVpk = 0;

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string id = Path.GetFileName(directory);

                if (!ulong.TryParse(id, out _))
                    continue;

                numericFolders++;

                string publishData = Path.Combine(directory, "publish_data.txt");
                if (!File.Exists(publishData))
                    missingPublishData++;

                // Wie HeadshotOnly: nur tatsächlich installierte Workshopmaps mit
                // einer VPK direkt im Workshop-Ordner übernehmen.
                if (!Directory.EnumerateFiles(directory, "*.vpk", SearchOption.TopDirectoryOnly).Any())
                {
                    missingVpk++;
                    continue;
                }

                string title = File.Exists(publishData)
                    ? TryReadWorkshopValue(publishData, "title")
                    : string.Empty;

                string sourceFolder = File.Exists(publishData)
                    ? TryReadWorkshopValue(publishData, "source_folder")
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(title))
                    title = string.IsNullOrWhiteSpace(sourceFolder)
                        ? $"Workshop {id}"
                        : sourceFolder;

                installed.Add(new InstalledWorkshopMap
                {
                    Id = id,
                    Title = title,
                    SourceFolder = sourceFolder,
                    Directory = directory
                });
            }

            _installedWorkshopMaps = installed
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Core.Logger.LogInformation(
                "Workshop-Scan wie HeadshotOnly: {Installed} installierte Maps | Pfad={Path} | numerische Ordner={Folders} | ohne publish_data={MissingPublish} | ohne VPK={MissingVpk}.",
                _installedWorkshopMaps.Count,
                root,
                numericFolders,
                missingPublishData,
                missingVpk);
        }
        catch (Exception ex)
        {
            _installedWorkshopMaps = new List<InstalledWorkshopMap>();
            Core.Logger.LogError(ex, "Fehler beim Lesen des Workshop-Pfads {Path}.", root);
        }
    }

    private string ResolveWorkshopPath()
    {
        var candidates = new List<string>();

        static void AddCandidate(List<string> list, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string candidate = value.Trim().TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (!list.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                list.Add(candidate);
        }

        // Exakt dieselben Standardpfade wie HeadshotOnly.
        AddCandidate(candidates, "/home/container/game/bin/linuxsteamrt64/steamapps/workshop/content/730");
        AddCandidate(candidates, "/game/bin/linuxsteamrt64/steamapps/workshop/content/730");

        // Zusätzlich relativ zur laufenden Installation nach dem game-Ordner suchen.
        try
        {
            DirectoryInfo? cursor = new(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && cursor is not null; i++, cursor = cursor.Parent)
            {
                if (!cursor.Name.Equals("game", StringComparison.OrdinalIgnoreCase))
                    continue;

                AddCandidate(
                    candidates,
                    Path.Combine(
                        cursor.FullName,
                        "bin",
                        "linuxsteamrt64",
                        "steamapps",
                        "workshop",
                        "content",
                        "730"));
                break;
            }
        }
        catch
        {
            // Die festen Pterodactyl-Kandidaten reichen als Fallback.
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // nächsten Kandidaten testen
            }
        }

        return candidates.FirstOrDefault() ?? string.Empty;
    }

    private static string TryReadWorkshopValue(string publishDataPath, string key)
    {
        try
        {
            string text = File.ReadAllText(publishDataPath);
            string escapedKey = Regex.Escape(key);
            Match match = Regex.Match(
                text,
                $"\"{escapedKey}\"\\s+\"(?<value>[^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return match.Success
                ? match.Groups["value"].Value.Trim()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void BeginMapLoad(string? mapName, string source)
    {
        _mapLifecycleToken++;
        CancelAllMapTimers();

        _mapLoaded = true;
        _mapStable = false;
        _changingMap = true;
        _mapSwitchPending = false;
        _mapSwitchDispatchQueued = false;
        _mapSwitchIssued = false;
        _mapSwitchInFlight = false;

        string normalized = NormalizeMapName(mapName);
        if (!string.IsNullOrWhiteSpace(normalized))
            _currentMapName = normalized;

        string workshopAtLoad = TryGetCurrentWorkshopId();
        if (!string.IsNullOrWhiteSpace(workshopAtLoad))
            _currentWorkshopId = workshopAtLoad;

        long token = _mapLifecycleToken;

        Core.Logger.LogInformation(
            "MapLoad: Map={Map}, erwartetes Ziel={Target}, Stabilisierung=2s, Quelle={Source}, Lifecycle={Token}.",
            normalized,
            _issuedMapTarget?.DisplayName ?? "<keins>",
            source,
            token);

        CancellationTokenSource timer = Core.Scheduler.DelayBySeconds(2.0f, () =>
        {
            _mapStabilizationTimer = null;

            if (token != _mapLifecycleToken || !_mapLoaded)
                return;

            _mapStable = true;
            _changingMap = false;
            _mapSwitchPending = false;
            _mapSwitchDispatchQueued = false;
            _mapSwitchIssued = false;
            _mapSwitchInFlight = false;

            if (_issuedMapTarget is not null)
            {
                string actualMap = TryGetCurrentMapName();
                if (!string.IsNullOrWhiteSpace(actualMap))
                    _currentMapName = actualMap;

                string actualWorkshop = TryGetCurrentWorkshopId();
                if (!string.IsNullOrWhiteSpace(actualWorkshop))
                    _currentWorkshopId = actualWorkshop;

                Core.Logger.LogInformation(
                    "Mapwechsel abgeschlossen/stabil: Ziel={Target}, Workshop={Workshop}, aktuelle Map={Map}, EngineWorkshop={EngineWorkshop}.",
                    _issuedMapTarget.DisplayName,
                    _issuedMapTarget.WorkshopId ?? "<normal>",
                    string.IsNullOrWhiteSpace(_currentMapName) ? "<unbekannt>" : _currentMapName,
                    string.IsNullOrWhiteSpace(_currentWorkshopId) ? "<unbekannt>" : _currentWorkshopId);
            }

            _scheduledMapTarget = null;
            _issuedMapTarget = null;
        });

        _mapStabilizationTimer = timer;
        Core.Scheduler.StopOnMapChange(timer);
    }

    private void ScheduleMapChange(
        string map,
        string requestedMapName,
        string requestedSourceFolder,
        int seconds,
        string message,
        string type)
    {
        map = (map ?? string.Empty).Trim();

        if (!_mapLoaded || !_mapStable || _changingMap)
            throw new InvalidOperationException("Die aktuelle Map befindet sich noch im Lade-/Stabilisierungszustand.");

        if (_mapSwitchInFlight || _mapSwitchPending || _mapSwitchDispatchQueued || _mapSwitchIssued)
            throw new InvalidOperationException("Es läuft bereits ein Mapwechsel.");

        MapSwitchTarget target = ResolveMapSwitchTarget(map, requestedMapName, requestedSourceFolder);

        long token = _mapLifecycleToken;
        _scheduledMapTarget = target;
        _mapSwitchInFlight = true;
        _mapSwitchPending = true;
        _mapSwitchDispatchQueued = false;
        _mapSwitchIssued = false;

        Core.Logger.LogInformation(
            "Robuster Mapwechsel geplant: Ziel={Target}, Workshop={Workshop}, SourceFolder={SourceFolder}, Countdown={Seconds}s, Lifecycle={Token}.",
            target.DisplayName,
            target.WorkshopId ?? "<normal>",
            string.IsNullOrWhiteSpace(target.SourceFolder) ? "<none>" : target.SourceFolder,
            seconds,
            token);

        ScheduleMapCountdown(token, target, seconds, message, type);
    }

    private void ScheduleMapCountdown(
        long token,
        MapSwitchTarget target,
        int seconds,
        string template,
        string type)
    {
        CancelMapCountdownTimers();
        seconds = Clamp(seconds, 0, 60);

        if (seconds <= 0)
        {
            QueueMapSwitchDispatch(token, target);
            return;
        }

        foreach (int remaining in CountdownPoints(seconds))
        {
            float delay = seconds - remaining;

            void SendCountdown()
            {
                if (token != _mapLifecycleToken
                    || !_mapSwitchInFlight
                    || !_mapSwitchPending
                    || _scheduledMapTarget is null)
                {
                    return;
                }

                Broadcast(type, RenderCountdown(template, remaining, target.DisplayName));
            }

            if (delay <= 0)
            {
                SendCountdown();
            }
            else
            {
                CancellationTokenSource timer = Core.Scheduler.DelayBySeconds(delay, SendCountdown);
                _mapCountdownTimers.Add(timer);
                Core.Scheduler.StopOnMapChange(timer);
            }
        }

        CancellationTokenSource executeTimer = Core.Scheduler.DelayBySeconds(seconds, () =>
        {
            if (token != _mapLifecycleToken)
                return;

            QueueMapSwitchDispatch(token, target);
        });

        _mapCountdownTimers.Add(executeTimer);
        Core.Scheduler.StopOnMapChange(executeTimer);
    }

    private void QueueMapSwitchDispatch(long token, MapSwitchTarget target)
    {
        if (token != _mapLifecycleToken
            || !_mapLoaded
            || !_mapStable
            || _changingMap
            || !_mapSwitchInFlight
            || !_mapSwitchPending
            || _mapSwitchDispatchQueued
            || _mapSwitchIssued)
        {
            Core.Logger.LogDebug(
                "Mapwechsel-Dispatch verworfen: Token={Token}/{Current}, Loaded={Loaded}, Stable={Stable}, Changing={Changing}, InFlight={InFlight}, Pending={Pending}, Dispatch={Dispatch}, Issued={Issued}.",
                token,
                _mapLifecycleToken,
                _mapLoaded,
                _mapStable,
                _changingMap,
                _mapSwitchInFlight,
                _mapSwitchPending,
                _mapSwitchDispatchQueued,
                _mapSwitchIssued);
            return;
        }

        _mapSwitchDispatchQueued = true;

        Core.Logger.LogInformation(
            "Mapwechsel: NextWorldUpdate #1 eingeplant. Ziel={Target}, Lifecycle={Token}.",
            target.DisplayName,
            token);

        Core.Scheduler.NextWorldUpdate(() => BeginMapSwitchDispatch(token, target));
    }

    private void BeginMapSwitchDispatch(long token, MapSwitchTarget target)
    {
        try
        {
            if (!IsCurrentMapSwitchCallback(token))
                return;

            _mapSwitchDispatchQueued = false;
            _changingMap = true;

            // Wie bei unseren robusten Mapwechsel-Plugins: einen eventuell alten nextlevel
            // zunächst getrennt im ersten WorldUpdate-Schritt leeren.
            Core.Engine.ExecuteCommandWithBuffer("nextlevel \"\"", _ => { });

            Core.Logger.LogInformation(
                "Mapwechsel: Vorbereitung abgeschlossen, NextWorldUpdate #2 folgt. Ziel={Target}.",
                target.DisplayName);

            Core.Scheduler.NextWorldUpdate(() => CommitMapSwitch(token, target));
        }
        catch (Exception ex)
        {
            RecoverMapSwitchException(token, target, ex, "Vorbereitung");
        }
    }

    private void CommitMapSwitch(long token, MapSwitchTarget target)
    {
        try
        {
            if (token != _mapLifecycleToken
                || !_mapLoaded
                || !_mapStable
                || !_changingMap
                || !_mapSwitchInFlight
                || !_mapSwitchPending
                || _mapSwitchIssued)
            {
                Core.Logger.LogDebug(
                    "Mapwechsel-Commit verworfen: Token={Token}/{Current}, Loaded={Loaded}, Stable={Stable}, Changing={Changing}, InFlight={InFlight}, Pending={Pending}, Issued={Issued}.",
                    token,
                    _mapLifecycleToken,
                    _mapLoaded,
                    _mapStable,
                    _changingMap,
                    _mapSwitchInFlight,
                    _mapSwitchPending,
                    _mapSwitchIssued);
                return;
            }

            _mapSwitchIssued = true;
            _mapSwitchPending = false;
            _issuedMapTarget = target;

            ArmMapSwitchWatchdog(token, target);

            if (target.IsWorkshop)
            {
                // HeadshotOnly-Stil: source_folder ist geeigneter als ein reiner Anzeigename.
                // Nur wenn ein sicherer Token vorliegt, wird nextlevel gesetzt.
                if (IsSafeMapToken(target.SourceFolder))
                {
                    Core.Engine.ExecuteCommandWithBuffer(
                        $"nextlevel \"{EscapeCommandArgument(target.SourceFolder)}\"",
                        _ => { });
                }

                Core.Logger.LogInformation(
                    "Workshop-Wechsel wird gepuffert ausgeführt: Ziel={Target}, Workshop={Workshop}, nextlevel={NextLevel}.",
                    target.DisplayName,
                    target.WorkshopId,
                    IsSafeMapToken(target.SourceFolder) ? target.SourceFolder : "<nicht gesetzt>");

                // Absichtlich letzte Engine-Aktion im Commit.
                Core.Engine.ExecuteCommandWithBuffer(
                    $"host_workshop_map {target.WorkshopId}",
                    _ => { });
            }
            else
            {
                Core.Engine.ExecuteCommandWithBuffer(
                    $"nextlevel \"{EscapeCommandArgument(target.MapName)}\"",
                    _ => { });

                Core.Logger.LogInformation(
                    "Normaler Mapwechsel wird gepuffert ausgeführt: {Map}.",
                    target.MapName);

                // Absichtlich letzte Engine-Aktion im Commit.
                Core.Engine.ExecuteCommandWithBuffer(
                    $"changelevel \"{EscapeCommandArgument(target.MapName)}\"",
                    _ => { });
            }
        }
        catch (Exception ex)
        {
            RecoverMapSwitchException(token, target, ex, "Commit");
        }
    }

    private bool IsCurrentMapSwitchCallback(long token)
    {
        return token == _mapLifecycleToken
            && _mapLoaded
            && _mapStable
            && !_changingMap
            && _mapSwitchInFlight
            && _mapSwitchPending
            && _mapSwitchDispatchQueued
            && !_mapSwitchIssued;
    }

    private void ArmMapSwitchWatchdog(long token, MapSwitchTarget target)
    {
        CancelMapSwitchWatchdog();

        CancellationTokenSource watchdog = Core.Scheduler.DelayBySeconds(15.0f, () =>
        {
            _mapSwitchWatchdogTimer = null;

            // OnMapUnload erhöht den Lifecycle-Token und StopOnMapChange beendet den Timer.
            if (token != _mapLifecycleToken
                || !_mapLoaded
                || !_mapSwitchInFlight
                || !_mapSwitchIssued)
            {
                return;
            }

            Core.Logger.LogError(
                "Mapwechsel-Watchdog: Nach 15s kam kein OnMapUnload. Ziel={Target}, Workshop={Workshop}. Wechselzustand wird freigegeben.",
                target.DisplayName,
                target.WorkshopId ?? "<normal>");

            ResetMapSwitchState(clearIssuedTarget: true);
            _mapStable = true;
            _changingMap = false;
        });

        _mapSwitchWatchdogTimer = watchdog;
        Core.Scheduler.StopOnMapChange(watchdog);
    }

    private void RecoverMapSwitchException(
        long token,
        MapSwitchTarget target,
        Exception ex,
        string phase)
    {
        Core.Logger.LogError(
            ex,
            "Mapwechsel fehlgeschlagen in Phase {Phase}. Ziel={Target}, Workshop={Workshop}.",
            phase,
            target.DisplayName,
            target.WorkshopId ?? "<normal>");

        if (token != _mapLifecycleToken)
            return;

        ResetMapSwitchState(clearIssuedTarget: true);
        _mapStable = true;
        _changingMap = false;
    }

    private void ResetMapSwitchState(bool clearIssuedTarget)
    {
        CancelMapCountdownTimers();
        CancelMapSwitchWatchdog();

        _mapSwitchPending = false;
        _mapSwitchDispatchQueued = false;
        _mapSwitchIssued = false;
        _mapSwitchInFlight = false;
        _scheduledMapTarget = null;

        if (clearIssuedTarget)
            _issuedMapTarget = null;
    }

    private void CancelAllMapTimers()
    {
        CancelMapCountdownTimers();
        CancelMapSwitchWatchdog();

        try { _mapStabilizationTimer?.Cancel(); } catch { }
        _mapStabilizationTimer = null;
    }

    private void CancelMapCountdownTimers()
    {
        foreach (CancellationTokenSource timer in _mapCountdownTimers)
        {
            try { timer.Cancel(); } catch { }
        }
        _mapCountdownTimers.Clear();
    }

    private void CancelMapSwitchWatchdog()
    {
        try { _mapSwitchWatchdogTimer?.Cancel(); } catch { }
        _mapSwitchWatchdogTimer = null;
    }

    private MapSwitchTarget ResolveMapSwitchTarget(
        string map,
        string requestedMapName,
        string requestedSourceFolder)
    {
        if (!Regex.IsMatch(map, @"^(?:[A-Za-z0-9_\-.]{2,80}|[0-9]{6,20})$"))
            throw new InvalidOperationException("Ungültiger Mapname/Workshop-ID.");

        if (map.All(char.IsDigit)
            && ulong.TryParse(map, out ulong workshopId)
            && workshopId > 0)
        {
            InstalledWorkshopMap? installed = _installedWorkshopMaps
                .FirstOrDefault(x => x.Id.Equals(map, StringComparison.OrdinalIgnoreCase));

            // Falls die Map gerade erst installiert wurde, einmal frisch scannen.
            if (installed is null)
            {
                ScanInstalledWorkshopMaps();
                installed = _installedWorkshopMaps
                    .FirstOrDefault(x => x.Id.Equals(map, StringComparison.OrdinalIgnoreCase));
            }

            if (installed is null)
                throw new InvalidOperationException(
                    $"Workshop-ID {map} ist im installierten Workshop-Pfad nicht vorhanden oder besitzt keine VPK.");

            string displayName = FirstNonEmpty(
                requestedMapName?.Trim(),
                installed.Title,
                $"Workshop {map}");

            string sourceFolder = FirstNonEmpty(
                requestedSourceFolder?.Trim(),
                installed.SourceFolder,
                string.Empty);

            if (!string.IsNullOrWhiteSpace(sourceFolder) && !IsSafeMapToken(sourceFolder))
            {
                Core.Logger.LogWarning(
                    "Unsicherer source_folder aus Map-Metadaten verworfen: {SourceFolder}.",
                    sourceFolder);
                sourceFolder = string.Empty;
            }

            return new MapSwitchTarget
            {
                IsWorkshop = true,
                WorkshopId = workshopId.ToString(),
                MapName = string.Empty,
                DisplayName = displayName,
                SourceFolder = sourceFolder
            };
        }

        if (!Core.Engine.IsMapValid(map))
            throw new InvalidOperationException($"Die Map '{map}' wurde von der Engine nicht als gültig erkannt.");

        return new MapSwitchTarget
        {
            IsWorkshop = false,
            MapName = map,
            DisplayName = FirstNonEmpty(requestedMapName?.Trim(), map),
            SourceFolder = string.Empty
        };
    }

    private WorkshopMetadata ReadWorkshopMetadata(string workshopId)
    {
        try
        {
            string gameRoot = Directory.GetParent(Core.CSGODirectory)?.FullName ?? Core.CSGODirectory;
            string publishData = Path.Combine(
                gameRoot,
                "bin",
                "linuxsteamrt64",
                "steamapps",
                "workshop",
                "content",
                "730",
                workshopId,
                "publish_data.txt");

            if (!File.Exists(publishData))
                return new WorkshopMetadata();

            string title = string.Empty;
            string sourceFolder = string.Empty;

            foreach (string line in File.ReadLines(publishData))
            {
                MatchCollection quoted = Regex.Matches(line, "\"([^\"]*)\"");
                if (quoted.Count < 2)
                    continue;

                string key = quoted[0].Groups[1].Value.Trim();
                string value = quoted[1].Groups[1].Value.Trim();

                if (key.Equals("title", StringComparison.OrdinalIgnoreCase))
                    title = value;
                else if (key.Equals("source_folder", StringComparison.OrdinalIgnoreCase))
                    sourceFolder = value;
            }

            return new WorkshopMetadata
            {
                Title = title,
                SourceFolder = sourceFolder
            };
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(
                ex,
                "publish_data.txt für Workshop {WorkshopId} konnte nicht gelesen werden.",
                workshopId);
            return new WorkshopMetadata();
        }
    }

    private static bool IsSafeMapToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Regex.IsMatch(value, @"^[A-Za-z0-9_\-./]{1,160}$");

    private static string EscapeCommandArgument(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }

    private static string NormalizeMapName(string? value)
    {
        string name = (value ?? string.Empty).Trim().Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < name.Length)
            name = name[(slash + 1)..];

        if (name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name;
    }

    private void ScheduleCountdown(int seconds, string template, string type, string? map, Action? after)
    {
        seconds = Clamp(seconds, 0, 60);
        if (seconds == 0) { after?.Invoke(); return; }
        foreach (var remaining in CountdownPoints(seconds))
        {
            var delay = seconds - remaining;
            void Send() => Broadcast(type, RenderCountdown(template, remaining, map));
            if (delay <= 0) Send(); else Core.Scheduler.DelayBySeconds(delay, Send);
        }
        if (after is not null) Core.Scheduler.DelayBySeconds(seconds, after);
    }

    private static IEnumerable<int> CountdownPoints(int seconds)
    {
        if (seconds <= 10)
            return Enumerable.Range(1, seconds).Reverse();

        var points = new[] { seconds, 30, 10, 5, 4, 3, 2, 1 };
        return points.Where(x => x > 0 && x <= seconds).Distinct().OrderByDescending(x => x);
    }

    private static string RenderCountdown(string template, int seconds, string? map)
        => template.Replace("{seconds}", seconds.ToString(), StringComparison.OrdinalIgnoreCase)
                   .Replace("{map}", map ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private void Broadcast(string type, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Core.PlayerManager.SendMessage(ToMessageType(type), Limit(message, 500));
    }

    private static MessageType ToMessageType(string type) => type.ToLowerInvariant() switch
    {
        "center" => MessageType.Center,
        "alert" => MessageType.Alert,
        "notify" => MessageType.Notify,
        _ => MessageType.Chat,
    };

    private IPlayer RequirePlayer(ulong steamId)
        => Core.PlayerManager.GetPlayerFromSteamId(steamId, true) ?? throw new InvalidOperationException("Spieler ist nicht mehr online.");

    private IPlayer RequireBot(int userId, int playerId)
    {
        if (userId <= 0)
            throw new InvalidOperationException("Ungültige Bot-UserID.");

        var bot = Core.PlayerManager.GetBots().FirstOrDefault(player =>
            player.UserID == userId && (playerId < 0 || player.PlayerID == playerId));
        if (bot is null || !bot.IsFakeClient)
            throw new InvalidOperationException("Bot ist nicht mehr online oder seine Slot-ID hat sich geändert.");
        return bot;
    }

    private void RefreshPluginInventory(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _pluginInventoryRefreshedAt < TimeSpan.FromSeconds(10)) return;
        try
        {
            var metadata = Core.PluginManager.GetAllPluginMetadata();
            var statuses = Core.PluginManager.GetAllPluginStatuses();
            var paths = Core.PluginManager.GetPluginPaths();
            var list = new List<StatusPlugin>();
            foreach (string id in Core.PluginManager.GetAllPlugins().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                metadata.TryGetValue(id, out var meta); paths.TryGetValue(id, out var pluginPath);
                string statusText = statuses.TryGetValue(id, out var pluginStatus)
                    ? pluginStatus.ToString()
                    : "Indeterminate";
                list.Add(new StatusPlugin
                {
                    Id=id, Name=meta?.Name ?? id, Version=meta?.Version ?? "", Author=meta?.Author ?? "", Description=meta?.Description ?? "",
                    Website=meta?.Website ?? "", MinimumApiVersion=meta?.MinimumAPIVersion ?? "", Status=statusText, Path=pluginPath ?? "",
                    LatestBackupId=FindLatestPluginBackup(id), UpdateSupported=!string.Equals(id,"lplk.serververwaltung.bridge",StringComparison.OrdinalIgnoreCase)
                });
            }
            _pluginInventory=list; _pluginInventoryRefreshedAt=DateTimeOffset.UtcNow;
        }
        catch(Exception ex){Core.Logger.LogWarning(ex,"SwiftlyS2 Plugin-Inventar konnte nicht gelesen werden.");}
    }

    private void UpdatePluginFromGitHub(string pluginId,string downloadUrl,string assetName,string expectedSha256,string expectedVersion,string installSubpath)
    {
        pluginId=ValidatePluginId(pluginId);
        if(string.Equals(pluginId,"lplk.serververwaltung.bridge",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Die SerververwaltungBridge darf sich nicht selbst aktualisieren.");
        Uri uri=ValidateGitHubReleaseUrl(downloadUrl);
        string extension=Path.GetExtension(assetName).ToLowerInvariant();
        if(extension is not ".zip" and not ".rar") throw new InvalidOperationException("Nur ZIP- und RAR-Release-Assets werden unterstützt.");
        string pluginPath=ResolveManagedPluginPath(pluginId); string backupId=CreatePluginBackup(pluginId,pluginPath);
        string tempRoot=Path.Combine(_root,"plugin_update_tmp",Guid.NewGuid().ToString("N")); string archivePath=Path.Combine(tempRoot,"release"+extension); string extractRoot=Path.Combine(tempRoot,"extract"); Directory.CreateDirectory(extractRoot);
        bool unloaded=false;
        bool filesReplaced=false;
        try
        {
            DownloadReleaseArchive(uri,archivePath,expectedSha256); SafeExtractArchive(archivePath,extractRoot); string sourceRoot=ResolveInstallSource(extractRoot,installSubpath);
            if(!Directory.EnumerateFiles(sourceRoot,"*.dll",SearchOption.AllDirectories).Any()) throw new InvalidOperationException("Das Release-Verzeichnis enthält keine DLL und sieht nicht wie ein SwiftlyS2 Publish aus.");
            if(!Core.PluginManager.UnloadPlugin(pluginId,true)) throw new InvalidOperationException("Plugin konnte vor dem Update nicht kontrolliert entladen werden."); unloaded=true;
            ClearDirectory(pluginPath); CopyDirectory(sourceRoot,pluginPath); filesReplaced=true;
            if(!Core.PluginManager.LoadPlugin(pluginId,true)) throw new InvalidOperationException("Neue Plugin-Version konnte nicht geladen werden."); unloaded=false;
            if(!string.IsNullOrWhiteSpace(expectedVersion))
            {
                var loadedMeta=Core.PluginManager.GetPluginMetadata(pluginId); string loadedVersion=loadedMeta?.Version ?? "";
                if(!string.Equals(NormalizeVersion(loadedVersion),NormalizeVersion(expectedVersion),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Geladene Version '{loadedVersion}' entspricht nicht der erwarteten Version '{expectedVersion}'.");
            }
            RefreshPluginInventory(force:true); Core.Logger.LogInformation("Plugin {PluginId} erfolgreich aktualisiert. Backup={BackupId}, ErwarteteVersion={Version}.",pluginId,backupId,expectedVersion);
        }
        catch
        {
            if (unloaded || filesReplaced)
            {
                try{if(!unloaded)Core.PluginManager.UnloadPlugin(pluginId,true);}catch{}
                try{RestorePluginBackup(pluginId,backupId,pluginPath);if(!Core.PluginManager.LoadPlugin(pluginId,true))Core.Logger.LogError("Rollback von {PluginId} wurde kopiert, Plugin konnte danach aber nicht geladen werden.",pluginId);else Core.Logger.LogWarning("Update von {PluginId} fehlgeschlagen; Backup {BackupId} wurde automatisch wiederhergestellt.",pluginId,backupId);}catch(Exception rollbackEx){Core.Logger.LogCritical(rollbackEx,"Automatischer Rollback für Plugin {PluginId} ist fehlgeschlagen.",pluginId);}
            }
            RefreshPluginInventory(force:true); throw;
        }
        finally{try{if(Directory.Exists(tempRoot))Directory.Delete(tempRoot,true);}catch{}}
    }

    private void RollbackPlugin(string pluginId,string backupId)
    {
        pluginId=ValidatePluginId(pluginId); if(string.Equals(pluginId,"lplk.serververwaltung.bridge",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Die Bridge kann nicht über sich selbst zurückgerollt werden.");
        if(!Regex.IsMatch(backupId,@"^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$",RegexOptions.IgnoreCase))throw new InvalidOperationException("Ungültige Backup-ID.");
        string pluginPath=ResolveManagedPluginPath(pluginId); if(!Core.PluginManager.UnloadPlugin(pluginId,true))throw new InvalidOperationException("Plugin konnte vor dem Rollback nicht entladen werden.");
        RestorePluginBackup(pluginId,backupId,pluginPath); if(!Core.PluginManager.LoadPlugin(pluginId,true))throw new InvalidOperationException("Rollback-Dateien wurden kopiert, Plugin konnte danach aber nicht geladen werden.");
        RefreshPluginInventory(force:true); Core.Logger.LogInformation("Plugin {PluginId} auf Backup {BackupId} zurückgerollt.",pluginId,backupId);
    }

    private string CreatePluginBackup(string pluginId,string pluginPath)
    {
        string backupId=DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")+"-"+Guid.NewGuid().ToString("N")[..8]; string destination=Path.Combine(_pluginBackupsRoot,SafePluginDirectory(pluginId),backupId); Directory.CreateDirectory(destination); CopyDirectory(pluginPath,destination);
        var meta=Core.PluginManager.GetPluginMetadata(pluginId); AtomicWrite(Path.Combine(destination,"_serververwaltung_backup.json"),JsonSerializer.Serialize(new{plugin_id=pluginId,version=meta?.Version ?? "",created_at=DateTimeOffset.UtcNow},_json)); CleanupPluginBackups(pluginId,5); return backupId;
    }
    private void RestorePluginBackup(string pluginId,string backupId,string pluginPath)
    {
        string source=Path.Combine(_pluginBackupsRoot,SafePluginDirectory(pluginId),backupId); if(!Directory.Exists(source))throw new InvalidOperationException("Plugin-Backup wurde nicht gefunden."); ClearDirectory(pluginPath);
        foreach(string entry in Directory.EnumerateFileSystemEntries(source)){if(Path.GetFileName(entry).Equals("_serververwaltung_backup.json",StringComparison.OrdinalIgnoreCase))continue;string target=Path.Combine(pluginPath,Path.GetFileName(entry));if(Directory.Exists(entry))CopyDirectory(entry,target);else File.Copy(entry,target,true);}
    }
    private string? FindLatestPluginBackup(string pluginId)
    {try{string root=Path.Combine(_pluginBackupsRoot,SafePluginDirectory(pluginId));if(!Directory.Exists(root))return null;return Directory.GetDirectories(root).Select(Path.GetFileName).Where(x=>x is not null&&Regex.IsMatch(x,@"^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$",RegexOptions.IgnoreCase)).OrderByDescending(x=>x,StringComparer.Ordinal).FirstOrDefault();}catch{return null;}}
    private void CleanupPluginBackups(string pluginId,int keep)
    {try{string root=Path.Combine(_pluginBackupsRoot,SafePluginDirectory(pluginId));if(!Directory.Exists(root))return;foreach(string dir in Directory.GetDirectories(root).OrderByDescending(x=>Path.GetFileName(x),StringComparer.Ordinal).Skip(Math.Max(1,keep)))try{Directory.Delete(dir,true);}catch{}}catch{}}

    private string ResolveManagedPluginPath(string pluginId)
    {
        string? raw=Core.PluginManager.GetPluginPath(pluginId); if(string.IsNullOrWhiteSpace(raw))throw new InvalidOperationException("SwiftlyS2 meldet keinen Plugin-Pfad.");
        string root=Path.GetFullPath(Path.Combine(Core.CSGODirectory,"addons","swiftlys2","plugins")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string resolved=Path.GetFullPath(raw);
        string path=File.Exists(resolved) ? Path.GetDirectoryName(resolved)! : resolved;
        path=path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string prefix=path+Path.DirectorySeparatorChar; if(!prefix.StartsWith(root,StringComparison.Ordinal))throw new InvalidOperationException("Plugin-Pfad liegt außerhalb addons/swiftlys2/plugins."); if(!Directory.Exists(path))throw new InvalidOperationException("Plugin-Verzeichnis existiert nicht."); return path;
    }
    private static string ResolveInstallSource(string extractRoot,string installSubpath)
    {
        string root=Path.GetFullPath(extractRoot); if(!string.IsNullOrWhiteSpace(installSubpath)){string normalized=installSubpath.Replace('\\','/').Trim('/');if(normalized.Contains("..",StringComparison.Ordinal))throw new InvalidOperationException("Installations-Unterpfad darf kein '..' enthalten.");string selected=Path.GetFullPath(Path.Combine(root,normalized.Replace('/',Path.DirectorySeparatorChar)));string prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!selected.StartsWith(prefix,StringComparison.Ordinal)||!Directory.Exists(selected))throw new InvalidOperationException("Installations-Unterpfad wurde im Release nicht gefunden.");return selected;}var files=Directory.GetFiles(root);var dirs=Directory.GetDirectories(root);return files.Length==0&&dirs.Length==1?dirs[0]:root;
    }
    private static void DownloadReleaseArchive(Uri uri,string destination,string expectedSha256)
    {
        using var handler=new HttpClientHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5};using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(60)};client.DefaultRequestHeaders.UserAgent.ParseAdd("LPLK-SerververwaltungBridge/1.5.4");using var response=client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();const long maxBytes=200L*1024L*1024L;if(response.Content.Headers.ContentLength is long length&&length>maxBytes)throw new InvalidOperationException("Release-Asset ist größer als 200 MB.");using Stream input=response.Content.ReadAsStream();using FileStream output=File.Create(destination);using var sha=SHA256.Create();byte[] buffer=new byte[1024*128];long total=0;int read;while((read=input.Read(buffer,0,buffer.Length))>0){total+=read;if(total>maxBytes)throw new InvalidOperationException("Release-Asset überschreitet 200 MB.");output.Write(buffer,0,read);sha.TransformBlock(buffer,0,read,null,0);}sha.TransformFinalBlock(Array.Empty<byte>(),0,0);string actual=Convert.ToHexString(sha.Hash!).ToLowerInvariant();if(!string.IsNullOrWhiteSpace(expectedSha256)){string expected=expectedSha256.Trim().ToLowerInvariant();if(!Regex.IsMatch(expected,@"^[a-f0-9]{64}$"))throw new InvalidOperationException("Ungültiger erwarteter SHA-256-Wert.");if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual),Convert.FromHexString(expected)))throw new InvalidOperationException("SHA-256 des Release-Assets stimmt nicht mit GitHub überein.");}
    }
    private static void SafeExtractArchive(string archivePath,string destination)
    {
        string root=Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;

        using Stream archiveStream=File.OpenRead(archivePath);
        using var reader=ReaderFactory.OpenReader(archiveStream);

        int entryCount=0;
        long totalWritten=0;

        while(reader.MoveToNextEntry())
        {
            entryCount++;
            if(entryCount>5000)
                throw new InvalidOperationException("Release-Archiv enthält ungewöhnlich viele Einträge.");

            var entry=reader.Entry;
            string key=(entry.Key??"").Replace('\\','/');
            if(string.IsNullOrWhiteSpace(key)) continue;

            if(!string.IsNullOrWhiteSpace(entry.LinkTarget))
                throw new InvalidOperationException("Symlinks/Links in Plugin-Release-Archiven werden nicht akzeptiert.");

            if(key.StartsWith("/",StringComparison.Ordinal)
               || key.StartsWith("\\",StringComparison.Ordinal)
               || Regex.IsMatch(key,@"^[A-Za-z]:")
               || key.Split('/',StringSplitOptions.RemoveEmptyEntries).Any(part=>part==".."))
                throw new InvalidOperationException("Unsicherer Pfad im Release-Archiv erkannt.");

            string target=Path.GetFullPath(
                Path.Combine(destination,key.Replace('/',Path.DirectorySeparatorChar)));

            if(!target.StartsWith(root,StringComparison.Ordinal)
               && !string.Equals(
                   target.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),
                   StringComparison.Ordinal))
                throw new InvalidOperationException("Unsicherer Zielpfad im Release-Archiv erkannt.");

            if(entry.IsDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using Stream input=reader.OpenEntryStream();
            using FileStream output=new(target,FileMode.Create,FileAccess.Write,FileShare.None);

            byte[] buffer=new byte[128*1024];
            int read;
            while((read=input.Read(buffer,0,buffer.Length))>0)
            {
                totalWritten+=read;
                if(totalWritten>500L*1024L*1024L)
                    throw new InvalidOperationException("Entpackte Release-Daten überschreiten 500 MB.");
                output.Write(buffer,0,read);
            }
        }

        if(entryCount==0)
            throw new InvalidOperationException("Release-Archiv ist leer oder konnte nicht gelesen werden.");
    }

    private static Uri ValidateGitHubReleaseUrl(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||!string.Equals(uri.Host,"github.com",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Plugin-Updates dürfen nur von HTTPS Release-Assets auf github.com gestartet werden.");return uri;}
    private static string ValidatePluginId(string value){value=value.Trim();if(!Regex.IsMatch(value,@"^[A-Za-z0-9_.-]{1,100}$"))throw new InvalidOperationException("Ungültige Plugin-ID.");return value;}
    private static string SafePluginDirectory(string pluginId)=>new(pluginId.Select(ch=>char.IsLetterOrDigit(ch)||ch is '.' or '-' or '_'?ch:'_').Take(100).ToArray());
    private static string NormalizeVersion(string value){Match match=Regex.Match(value??"",@"\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?");return match.Success?match.Value:(value??"").Trim();}
    private static void CopyDirectory(string source,string destination){Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(destination,Path.GetFileName(file)),true);foreach(string directory in Directory.GetDirectories(source))CopyDirectory(directory,Path.Combine(destination,Path.GetFileName(directory)));}
    private static void ClearDirectory(string path){Directory.CreateDirectory(path);foreach(string file in Directory.GetFiles(path))File.Delete(file);foreach(string directory in Directory.GetDirectories(path))Directory.Delete(directory,true);}

    private void TryWriteStatus()
    {
        try
        {
            RefreshPluginInventory();
            var players = new List<StatusPlayer>();
            foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            {
                var isBot = player.IsFakeClient;
                if (!isBot && player.SteamID == 0) continue;

                var pawn = player.PlayerPawn;
                var teamNumber = (int)player.Controller.Team;
                var team = teamNumber switch { 3 => "CT", 2 => "T", 1 => "Spectator", _ => "Unassigned" };

                NicknameEntry? nick = null;
                CommEntry? mute = null;
                CommEntry? gag = null;
                CommEntry? globalMute = null;
                CommEntry? globalGag = null;
                var muted = false;
                var gagged = false;
                var frozen = false;
                var noclip = false;
                var godmode = false;
                var speedMultiplier = _globalSpeed;
                var gravityScale = 1.0f;

                if (!isBot)
                {
                    _nicknames.TryGetValue(player.SteamID, out nick);
                    _mutes.TryGetValue(player.SteamID, out mute);
                    _gags.TryGetValue(player.SteamID, out gag);
                    lock (_globalCommSync)
                    {
                        _globalMutes.TryGetValue(player.SteamID, out globalMute);
                        _globalGags.TryGetValue(player.SteamID, out globalGag);
                    }
                    var localMuted = IsCommActive(_mutes, player.SteamID);
                    var localGagged = IsCommActive(_gags, player.SteamID);
                    var globalMuted = IsCommEntryActive(globalMute);
                    var globalGagged = IsCommEntryActive(globalGag);
                    muted = localMuted || globalMuted;
                    gagged = localGagged || globalGagged;
                    frozen = _frozen.Contains(player.SteamID);
                    noclip = _noclip.Contains(player.SteamID);
                    godmode = _godmode.Contains(player.SteamID);
                    speedMultiplier = _playerSpeed.TryGetValue(player.SteamID, out float playerSpeed) ? playerSpeed : _globalSpeed;
                    gravityScale = _playerGravity.TryGetValue(player.SteamID, out float playerGravity) ? playerGravity : 1.0f;
                }

                var effectiveMute = IsCommEntryActive(globalMute) ? globalMute : mute;
                var effectiveGag = IsCommEntryActive(globalGag) ? globalGag : gag;

                players.Add(new StatusPlayer
                {
                    SteamId64 = isBot ? string.Empty : player.SteamID.ToString(),
                    Name = player.Name,
                    Team = team,
                    Alive = player.IsAlive,
                    Health = pawn?.Health ?? 0,
                    Armor = pawn?.ArmorValue ?? 0,
                    Kills = player.Controller.KillCount,
                    Score = player.Controller.Score,
                    ConnectedSeconds = player.ConnectedTime,
                    ConnectedHuman = FormatDuration(player.ConnectedTime),
                    IsBot = isBot,
                    Frozen = frozen,
                    Noclip = noclip,
                    Godmode = godmode,
                    SpeedMultiplier = speedMultiplier,
                    GravityScale = gravityScale,
                    VoiceMuted = muted,
                    Gagged = gagged,
                    Silenced = muted && gagged,
                    MuteReason = muted ? effectiveMute?.Reason : null,
                    MuteExpiresAt = muted ? effectiveMute?.ExpiresAt : null,
                    GagReason = gagged ? effectiveGag?.Reason : null,
                    GagExpiresAt = gagged ? effectiveGag?.ExpiresAt : null,
                    PlayerId = player.PlayerID,
                    UserId = player.UserID,
                    IpAddress = isBot ? string.Empty : player.IPAddress,
                    Ping = player.Controller.Ping,
                    NicknameOverride = isBot || string.IsNullOrWhiteSpace(nick?.Override) ? null : nick!.Override,
                    OriginalName = isBot ? null : FirstNonEmpty(nick?.SteamName, nick?.Original),
                    NicknameHistory = isBot ? new List<string>() : nick?.History?.ToList() ?? new List<string>(),
                });
            }
            string currentMap = TryGetCurrentMapName();
            if (!string.IsNullOrWhiteSpace(currentMap))
                _currentMapName = currentMap;

            string currentWorkshop = TryGetCurrentWorkshopId();
            if (!string.IsNullOrWhiteSpace(currentWorkshop))
                _currentWorkshopId = currentWorkshop;

            var ctScore = 0;
            var tScore = 0;
            var roundsPlayed = 0;
            var maxPlayers = 0;
            try
            {
                var match = Core.Game.MatchData;
                ctScore = match.CTScoreTotal;
                tScore = match.TerroristScoreTotal;
                roundsPlayed = match.ActualRoundsPlayed;
            }
            catch (Exception ex)
            {
                Core.Logger.LogDebug(ex, "Match-Score ist für diesen Status-Tick noch nicht verfügbar.");
            }
            try
            {
                // SwiftlyS2 derives MaxPlayers from GlobalVars. During early plugin
                // startup GlobalVars may legitimately be unavailable for a short
                // time; status generation and Fast Live must still continue.
                maxPlayers = Core.PlayerManager.MaxPlayers;
            }
            catch (InvalidOperationException ex)
            {
                Core.Logger.LogDebug(ex, "Maximale Spielerzahl ist für diesen Status-Tick noch nicht verfügbar.");
            }

            var status = new BridgeStatus
            {
                Version = "1.6.4",
                UpdatedAt = DateTimeOffset.UtcNow,
                Map = _currentMapName,
                CtScore = ctScore,
                TScore = tScore,
                RoundsPlayed = roundsPlayed,
                WorkshopId = _currentWorkshopId,
                MaxPlayers = maxPlayers,
                MapStable = _mapStable,
                MapChanging = _changingMap,
                MapSwitchInFlight = _mapSwitchInFlight,
                MapSwitchTarget = _issuedMapTarget?.DisplayName ?? _scheduledMapTarget?.DisplayName,
                MapSwitchTargetWorkshopId = _issuedMapTarget?.WorkshopId ?? _scheduledMapTarget?.WorkshopId,
                WorkshopPath = _resolvedWorkshopPath,
                InstalledWorkshopMaps = _installedWorkshopMaps,
                Plugins = _pluginInventory,
                Players = players,
                Rules = BuildRuleStatus(),
                ChatMessages = _chatMessages.ToList(),
                Bans = _bans.OrderBy(x => x.Value.ExpiresAt ?? DateTimeOffset.MaxValue).Select(x => new StatusBan
                {
                    SteamId64 = x.Key.ToString(),
                    Reason = x.Value.Reason,
                    CreatedAt = x.Value.CreatedAt,
                    ExpiresAt = x.Value.ExpiresAt
                }).ToList(),
                Mutes = _mutes.OrderBy(x => x.Value.ExpiresAt ?? DateTimeOffset.MaxValue).Select(x => new StatusComm
                {
                    SteamId64 = x.Key.ToString(),
                    Reason = x.Value.Reason,
                    CreatedAt = x.Value.CreatedAt,
                    ExpiresAt = x.Value.ExpiresAt
                }).ToList(),
                Gags = _gags.OrderBy(x => x.Value.ExpiresAt ?? DateTimeOffset.MaxValue).Select(x => new StatusComm
                {
                    SteamId64 = x.Key.ToString(),
                    Reason = x.Value.Reason,
                    CreatedAt = x.Value.CreatedAt,
                    ExpiresAt = x.Value.ExpiresAt
                }).ToList()
            };
            string serializedStatus = JsonSerializer.Serialize(status, _json);
            AtomicWrite(_status, serializedStatus);
            TryStartFastLiveExchange(status);
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Bridge-Status konnte nicht geschrieben werden.");
        }
    }

    private void CaptureChatMessage(IPlayer player, string text, bool teamOnly)
    {
        string message = (text ?? string.Empty).Trim();
        if (message.Length == 0) return;
        if (message.Length > 1000) message = message[..1000];
        int teamNumber = (int)player.Controller.Team;
        string team = teamNumber switch { 3 => "CT", 2 => "T", 1 => "Spectator", _ => "Unassigned" };
        _gameRolesBySteamId.TryGetValue(player.SteamID, out string? role);
        var now = DateTimeOffset.UtcNow;
        _chatMessages.Enqueue(new StatusChatMessage
        {
            Id = $"{now.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _chatSequence)}-{player.SteamID}",
            SteamId64 = player.SteamID.ToString(),
            PlayerName = player.Name,
            CommunityRole = string.IsNullOrWhiteSpace(role) ? "spieler" : role,
            Team = team,
            TeamOnly = teamOnly,
            Message = message,
            MapName = _currentMapName,
            SentAt = now,
        });
        while (_chatMessages.Count > 250) _chatMessages.Dequeue();
    }

    private void LoadFastLiveConfig()
    {
        try
        {
            string path = string.IsNullOrWhiteSpace(_fastLiveConfigPath)
                ? Path.Combine(_root, "fastlive.json")
                : _fastLiveConfigPath;
            _fastLiveConfigPath = path;

            if (!File.Exists(path))
            {
                DisableFastLiveRuntime();
                _fastLiveConfigLastWriteUtc = DateTime.MinValue;
                _fastLiveConfigLength = -1;
                Core.Logger.LogInformation(
                    "Fast Live wartet auf automatische Bereitstellung durch das Webpanel: {Path}",
                    path);
                return;
            }

            var info = new FileInfo(path);
            var cfg = JsonSerializer.Deserialize<FastLiveConfig>(File.ReadAllText(path), _json);
            _fastLiveConfigLastWriteUtc = info.LastWriteTimeUtc;
            _fastLiveConfigLength = info.Length;

            if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.PanelUrl) || cfg.ServerId <= 0 || string.IsNullOrWhiteSpace(cfg.Secret))
            {
                DisableFastLiveRuntime();
                Core.Logger.LogWarning("fastlive.json vorhanden, aber unvollständig oder deaktiviert.");
                return;
            }
            if (!Uri.TryCreate(cfg.PanelUrl.TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                DisableFastLiveRuntime();
                Core.Logger.LogWarning("Fast Live benötigt eine gültige HTTPS panel_url.");
                return;
            }

            cfg.PanelUrl = cfg.PanelUrl.TrimEnd('/');
            cfg.PushIntervalSeconds = Math.Clamp(cfg.PushIntervalSeconds, 1, 10);

            try { _fastLiveClient?.Dispose(); } catch { }
            _fastLiveConfig = cfg;
            _fastLiveClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            _fastLiveClient.DefaultRequestHeaders.UserAgent.ParseAdd("LPLK-SerververwaltungBridge/1.5.4");
            Core.Logger.LogInformation(
                "Fast Live aktiviert/aktualisiert: Server-ID {ServerId}, Intervall {Seconds}s. Kein Bridge-Neustart erforderlich.",
                cfg.ServerId,
                cfg.PushIntervalSeconds);
        }
        catch (Exception ex)
        {
            DisableFastLiveRuntime();
            Core.Logger.LogWarning(ex, "fastlive.json konnte nicht geladen werden. Fallback status.json bleibt aktiv.");
        }
    }

    private void TryReloadFastLiveConfigIfChanged()
    {
        if ((DateTimeOffset.UtcNow - _lastFastLiveConfigCheck).TotalSeconds < 2)
            return;
        _lastFastLiveConfigCheck = DateTimeOffset.UtcNow;

        try
        {
            string path = string.IsNullOrWhiteSpace(_fastLiveConfigPath)
                ? Path.Combine(_root, "fastlive.json")
                : _fastLiveConfigPath;

            if (!File.Exists(path))
            {
                if (_fastLiveConfig is not null || _fastLiveConfigLastWriteUtc != DateTime.MinValue || _fastLiveConfigLength >= 0)
                    LoadFastLiveConfig();
                return;
            }

            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc != _fastLiveConfigLastWriteUtc
                || info.Length != _fastLiveConfigLength)
            {
                LoadFastLiveConfig();
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Fast-Live-Konfigurationswächter konnte fastlive.json nicht prüfen.");
        }
    }

    private void DisableFastLiveRuntime()
    {
        _fastLiveConfig = null;
        try { _fastLiveClient?.Dispose(); } catch { }
        _fastLiveClient = null;
        Interlocked.Exchange(ref _fastLiveInFlight, 0);
    }

    private void ProcessFastLiveCommands()
    {
        int processed = 0;
        while (processed < 25 && _fastLiveCommands.TryDequeue(out BridgeCommand? command))
        {
            processed++;
            if (command is null || string.IsNullOrWhiteSpace(command.Id)) continue;
            try
            {
                Execute(command);
                try { RecordAdminAction(command, success: true, error: null, notifyInGame: true); } catch { }
                AddFastLiveResult(command.Id, true, "OK");
            }
            catch (Exception ex)
            {
                try { RecordAdminAction(command, success: false, error: ex.Message, notifyInGame: false); } catch { }
                AddFastLiveResult(command.Id, false, ex.Message);
                Core.Logger.LogWarning(ex, "Fast-Live-Befehl {CommandId} fehlgeschlagen.", command.Id);
            }
        }
    }

    private void AddFastLiveResult(string id, bool success, string message)
    {
        lock (_fastLiveResultSync)
        {
            _pendingFastLiveResults.RemoveAll(x => x.Id == id);
            _pendingFastLiveResults.Add(new FastLiveResult { Id = id, Success = success, Message = Limit(message, 500), ExecutedAt = DateTimeOffset.UtcNow });
            if (_pendingFastLiveResults.Count > 200) _pendingFastLiveResults.RemoveRange(0, _pendingFastLiveResults.Count - 200);
        }
    }

    private void TryStartFastLiveExchange(BridgeStatus status)
    {
        var cfg = _fastLiveConfig;
        var client = _fastLiveClient;
        if (cfg is null || client is null) return;
        if ((DateTimeOffset.UtcNow - _lastFastLiveAttempt).TotalSeconds < cfg.PushIntervalSeconds) return;
        if (Interlocked.CompareExchange(ref _fastLiveInFlight, 1, 0) != 0) return;
        _lastFastLiveAttempt = DateTimeOffset.UtcNow;

        List<FastLiveResult> resultSnapshot;
        lock (_fastLiveResultSync) resultSnapshot = _pendingFastLiveResults.ToList();
        AntiAdOutboxBatch antiAdBatch = ReadAntiAdvertisementOutbox(100);
        string body;
        try { body = JsonSerializer.Serialize(new FastLiveExchangeRequest { Status = status, Results = resultSnapshot, AntiAdvertisementEvents = antiAdBatch.Events }, _json); }
        catch { Interlocked.Exchange(ref _fastLiveInFlight, 0); return; }

        _ = Task.Run(async () =>
        {
            try
            {
                long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string ts = unix.ToString(CultureInfo.InvariantCulture);
                string signature;
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(cfg.Secret)))
                    signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ts + "\n" + body))).ToLowerInvariant();

                using var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg.PanelUrl}/bridge-api/v1/servers/{cfg.ServerId}/exchange");
                req.Headers.TryAddWithoutValidation("X-Bridge-Timestamp", ts);
                req.Headers.TryAddWithoutValidation("X-Bridge-Signature", signature);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(req).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Fast Live HTTP {(int)response.StatusCode}: {Limit(responseBody, 300)}");
                var data = JsonSerializer.Deserialize<FastLiveExchangeResponse>(responseBody, _json);
                if (data is null || !data.Ok) throw new InvalidDataException(data?.Error ?? "Ungültige Fast-Live-Antwort.");

                if (data.AckedResultIds is not null && data.AckedResultIds.Count > 0)
                {
                    lock (_fastLiveResultSync)
                    {
                        var ack = data.AckedResultIds.ToHashSet(StringComparer.Ordinal);
                        _pendingFastLiveResults.RemoveAll(x => ack.Contains(x.Id));
                    }
                }

                ApplyGlobalBanSnapshot(data.GlobalBans ?? []);
                ApplyGlobalCommunicationSnapshot(data.GlobalCommSanctions ?? []);
                ApplyGlobalAntiAdvertisementSnapshot(data.GlobalAntiAdvertisement);
                AckAntiAdvertisementOutbox(antiAdBatch.Files);

                foreach (var command in data.Commands ?? [])
                {
                    if (command is null || string.IsNullOrWhiteSpace(command.Id)) continue;
                    bool add;
                    lock (_fastLiveSeenCommandIds)
                    {
                        add = _fastLiveSeenCommandIds.Add(command.Id);
                        if (add)
                        {
                            _fastLiveSeenCommandOrder.Enqueue(command.Id);
                            while (_fastLiveSeenCommandOrder.Count > 1000)
                            {
                                string old = _fastLiveSeenCommandOrder.Dequeue();
                                _fastLiveSeenCommandIds.Remove(old);
                            }
                        }
                    }
                    if (add) _fastLiveCommands.Enqueue(command);
                }
            }
            catch (Exception ex)
            {
                Core.Logger.LogDebug(ex, "Fast-Live-Austausch fehlgeschlagen; lokaler status.json-Fallback bleibt aktiv.");
            }
            finally { Interlocked.Exchange(ref _fastLiveInFlight, 0); }
        });
    }

    private void ApplyGlobalBanSnapshot(List<GlobalBanPayload> payload)
    {
        var now = DateTimeOffset.UtcNow;
        var next = new Dictionary<ulong, GlobalBanEntry>();
        foreach (var item in payload)
        {
            if (item is null || !ulong.TryParse(item.SteamId64, out ulong steamId) || steamId == 0) continue;
            if (!item.Permanent && item.ExpiresAt is DateTimeOffset expires && expires <= now) continue;
            next[steamId] = new GlobalBanEntry
            {
                Id = item.Id,
                PlayerName = item.PlayerName ?? "",
                ReasonCode = item.ReasonCode ?? "",
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? "Serverregelverstoß" : item.Reason,
                CreatedAt = item.CreatedAt,
                ExpiresAt = item.ExpiresAt,
                Permanent = item.Permanent
            };
        }

        lock (_globalBanSync)
        {
            _globalBans.Clear();
            foreach (var item in next) _globalBans[item.Key] = item.Value;
        }
        SaveGlobalBans();
    }

    private void ApplyGlobalCommunicationSnapshot(List<GlobalCommPayload> payload)
    {
        var now = DateTimeOffset.UtcNow;
        var nextMutes = new Dictionary<ulong, CommEntry>();
        var nextGags = new Dictionary<ulong, CommEntry>();
        foreach (var item in payload)
        {
            if (item is null || !ulong.TryParse(item.SteamId64, out ulong steamId) || steamId == 0) continue;
            if (!item.Permanent && item.ExpiresAt is DateTimeOffset expires && expires <= now) continue;
            var entry = new CommEntry
            {
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? "Globale Kommunikationssperre" : item.Reason,
                CreatedAt = item.CreatedAt,
                ExpiresAt = item.Permanent ? null : item.ExpiresAt
            };
            switch ((item.SanctionType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "mute": nextMutes[steamId] = entry; break;
                case "gag": nextGags[steamId] = entry; break;
                case "silence": nextMutes[steamId] = entry; nextGags[steamId] = new CommEntry { Reason = entry.Reason, CreatedAt = entry.CreatedAt, ExpiresAt = entry.ExpiresAt }; break;
            }
        }
        lock (_globalCommSync)
        {
            _globalMutes.Clear(); foreach (var x in nextMutes) _globalMutes[x.Key] = x.Value;
            _globalGags.Clear(); foreach (var x in nextGags) _globalGags[x.Key] = x.Value;
        }
        SaveGlobalComms();
    }

    private AntiAdOutboxBatch ReadAntiAdvertisementOutbox(int limit)
    {
        var batch = new AntiAdOutboxBatch();
        if (string.IsNullOrWhiteSpace(_antiAdOutboxDir) || limit <= 0) return batch;

        try
        {
            Directory.CreateDirectory(_antiAdOutboxDir);
            foreach (string file in Directory.EnumerateFiles(_antiAdOutboxDir, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(File.GetLastWriteTimeUtc)
                         .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                         .Take(limit))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<AntiAdvertisementEventPayload>(File.ReadAllText(file), _json);
                    if (payload is null || string.IsNullOrWhiteSpace(payload.EventId))
                    {
                        Core.Logger.LogWarning("Ungültiger AntiAdvertisement-Outbox-Eintrag bleibt zur Prüfung erhalten: {File}", Path.GetFileName(file));
                        continue;
                    }

                    batch.Events.Add(payload);
                    batch.Files.Add(file);
                }
                catch (Exception ex)
                {
                    // Beschädigte oder noch nicht lesbare Einträge niemals bestätigen bzw. löschen.
                    Core.Logger.LogWarning(ex, "AntiAdvertisement-Outbox-Eintrag konnte nicht gelesen werden und bleibt erhalten: {File}", Path.GetFileName(file));
                }
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "AntiAdvertisement-Outbox konnte nicht gelesen werden.");
        }

        return batch;
    }

    private void ReloadAntiAdvertisementPolicy(bool force = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!force && now - _lastAntiAdConfigCheck < TimeSpan.FromSeconds(3)) return;
        _lastAntiAdConfigCheck = now;

        try
        {
            if (string.IsNullOrWhiteSpace(_antiAdConfigFile) || !File.Exists(_antiAdConfigFile)) return;
            DateTime stamp = File.GetLastWriteTimeUtc(_antiAdConfigFile);
            if (!force && stamp == _antiAdConfigLastWriteUtc) return;

            var config = JsonSerializer.Deserialize<AntiAdvertisementPolicyConfig>(File.ReadAllText(_antiAdConfigFile), _json);
            if (config is null) return;

            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in config.BlockedDomains ?? [])
            {
                string domain = NormalizeAntiAdvertisementDomain(raw);
                if (domain.Length > 0) blocked.Add(domain);
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in config.AllowedDomains ?? [])
            {
                string domain = NormalizeAntiAdvertisementDomain(raw);
                if (domain.Length > 0) allowed.Add(domain);
            }

            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in config.MarketingKeywords ?? [])
            {
                string keyword = NormalizeAntiAdvertisementText(raw);
                if (keyword.Length > 1) keywords.Add(keyword);
            }

            lock (_antiAdProtectionSync)
            {
                _antiAdProtectionEnabled = config.Enabled;
                _antiAdLogBlockedText = config.LogBlockedText;
                _antiAdLocalBlockedDomains.Clear();
                foreach (string domain in blocked) _antiAdLocalBlockedDomains.Add(domain);
                _antiAdAllowedDomains.Clear();
                foreach (string domain in allowed) _antiAdAllowedDomains.Add(domain);
                _antiAdMarketingKeywords.Clear();
                foreach (string keyword in keywords) _antiAdMarketingKeywords.Add(keyword);
            }
            _antiAdConfigLastWriteUtc = stamp;
        }
        catch (Exception ex)
        {
            // Bei einer kurzzeitig unlesbaren Konfiguration bleibt die letzte
            // gueltige Richtlinie aktiv, statt den Werbeschutz offen zu schalten.
            Core.Logger.LogDebug(ex, "AntiAdvertisement-Richtlinie konnte nicht neu geladen werden.");
        }
    }

    private void LoadCachedGlobalAntiAdvertisementSnapshot()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_antiAdGlobalFile) || !File.Exists(_antiAdGlobalFile)) return;
            var snapshot = JsonSerializer.Deserialize<GlobalAntiAdvertisementPayload>(File.ReadAllText(_antiAdGlobalFile), _json);
            if (snapshot is not null) UpdateAntiAdvertisementRuntimeSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Gecachter globaler AntiAdvertisement-Snapshot konnte nicht geladen werden.");
        }
    }

    private void UpdateAntiAdvertisementRuntimeSnapshot(GlobalAntiAdvertisementPayload snapshot)
    {
        var ids = new HashSet<ulong>();
        foreach (string raw in snapshot.BlockedSteamIds ?? [])
            if (ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0)
                ids.Add(id);

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in snapshot.Domains ?? [])
        {
            string domain = NormalizeAntiAdvertisementDomain(raw);
            if (domain.Length > 0) domains.Add(domain);
        }

        var allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in snapshot.AllowedDomains ?? [])
        {
            string domain = NormalizeAntiAdvertisementDomain(raw);
            if (domain.Length > 0) allowedDomains.Add(domain);
        }

        var blockedPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in snapshot.BlockedPhrases ?? [])
        {
            string phrase = NormalizeAntiAdvertisementText(raw);
            if (phrase.Length > 1) blockedPhrases.Add(phrase);
        }

        var marketingKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in snapshot.MarketingKeywords ?? [])
        {
            string keyword = NormalizeAntiAdvertisementText(raw);
            if (keyword.Length > 1) marketingKeywords.Add(keyword);
        }

        lock (_antiAdProtectionSync)
        {
            _antiAdBlockedSteamIds.Clear();
            foreach (ulong id in ids) _antiAdBlockedSteamIds.Add(id);
            _antiAdGlobalBlockedDomains.Clear();
            foreach (string domain in domains) _antiAdGlobalBlockedDomains.Add(domain);
            _antiAdGlobalAllowedDomains.Clear();
            foreach (string domain in allowedDomains) _antiAdGlobalAllowedDomains.Add(domain);
            _antiAdGlobalBlockedPhrases.Clear();
            foreach (string phrase in blockedPhrases) _antiAdGlobalBlockedPhrases.Add(phrase);
            _antiAdGlobalMarketingKeywords.Clear();
            foreach (string keyword in marketingKeywords) _antiAdGlobalMarketingKeywords.Add(keyword);
        }
    }

    private bool TryMatchAntiAdvertisement(IPlayer player, string text, out string match, out string action)
    {
        match = string.Empty;
        action = string.Empty;

        bool enabled;
        HashSet<string> blockedDomains;
        HashSet<string> allowedDomains;
        HashSet<string> blockedPhrases;
        HashSet<string> marketingKeywords;
        lock (_antiAdProtectionSync)
        {
            if (_antiAdBlockedSteamIds.Contains(player.SteamID))
            {
                match = "global-advertising-id";
                action = "bridge_global_id_prearchive_stop";
                return true;
            }

            enabled = _antiAdProtectionEnabled;
            blockedDomains = new HashSet<string>(_antiAdLocalBlockedDomains, StringComparer.OrdinalIgnoreCase);
            blockedDomains.UnionWith(_antiAdGlobalBlockedDomains);
            allowedDomains = new HashSet<string>(_antiAdAllowedDomains, StringComparer.OrdinalIgnoreCase);
            allowedDomains.UnionWith(_antiAdGlobalAllowedDomains);
            blockedPhrases = new HashSet<string>(_antiAdGlobalBlockedPhrases, StringComparer.OrdinalIgnoreCase);
            marketingKeywords = new HashSet<string>(_antiAdMarketingKeywords, StringComparer.OrdinalIgnoreCase);
            marketingKeywords.UnionWith(_antiAdGlobalMarketingKeywords);
        }

        if (!enabled) return false;

        string normalizedName = NormalizeAntiAdvertisementText(player.Name ?? string.Empty);
        string normalizedText = NormalizeAntiAdvertisementText(text ?? string.Empty);
        string compactName = Regex.Replace(normalizedName, @"\s+", string.Empty);
        string compactText = Regex.Replace(normalizedText, @"\s+", string.Empty);

        foreach (string phrase in blockedPhrases.OrderByDescending(value => value.Length))
        {
            if (normalizedName.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                match = "phrase:" + phrase;
                action = "bridge_name_phrase_prearchive_stop";
                return true;
            }
            if (normalizedText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                match = "phrase:" + phrase;
                action = "bridge_chat_phrase_prearchive_stop";
                return true;
            }
        }

        foreach (string domain in blockedDomains.OrderByDescending(value => value.Length))
        {
            if (compactName.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                match = domain;
                action = "bridge_name_prearchive_stop";
                return true;
            }
            if (compactText.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                match = domain;
                action = "bridge_chat_prearchive_stop";
                return true;
            }
        }

        bool nameHasMarketingKeyword = marketingKeywords.Any(keyword =>
            normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        bool textHasMarketingKeyword = marketingKeywords.Any(keyword =>
            normalizedText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        bool hasMarketingKeyword = nameHasMarketingKeyword || textHasMarketingKeyword;
        Match? domainMatch = Regex.Matches(compactName + " " + compactText,
                @"(?:https?://)?(?:www\.)?[a-z0-9][a-z0-9-]{2,}\.(?:com|xyz|cc|net|gg|io|ru|org|de|shop|site|online|pro)(?:/[^\s]*)?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .FirstOrDefault(candidate => !allowedDomains.Contains(NormalizeAntiAdvertisementDomain(candidate.Value)));
        bool discordInName = compactName.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase)
            || compactName.Contains("discord.com/invite/", StringComparison.OrdinalIgnoreCase);
        bool discordInText = compactText.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase)
            || compactText.Contains("discord.com/invite/", StringComparison.OrdinalIgnoreCase);
        bool discordAllowed = allowedDomains.Contains("discord.gg") || allowedDomains.Contains("discord.com");

        if (domainMatch is not null && domainMatch.Success && hasMarketingKeyword)
        {
            match = domainMatch.Value;
            action = "bridge_generic_ad_prearchive_stop";
            return true;
        }

        if (!discordAllowed && (discordInText || (discordInName && nameHasMarketingKeyword)))
        {
            match = "discord-invite";
            action = "bridge_invite_prearchive_stop";
            return true;
        }

        if (nameHasMarketingKeyword
            && (normalizedName.Contains("free cs2", StringComparison.OrdinalIgnoreCase)
                || normalizedName.Contains("commends", StringComparison.OrdinalIgnoreCase)
                || normalizedName.Contains("commend", StringComparison.OrdinalIgnoreCase)))
        {
            match = "name-marketing";
            action = "bridge_name_marketing_prearchive_stop";
            return true;
        }

        return false;
    }

    private void QueueBridgeAntiAdvertisementEvent(IPlayer player, string text, string match, string action)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_antiAdOutboxDir)) return;
            Directory.CreateDirectory(_antiAdOutboxDir);
            bool logBlockedText;
            lock (_antiAdProtectionSync) logBlockedText = _antiAdLogBlockedText;
            string eventId = Guid.NewGuid().ToString("N");
            var payload = new AntiAdvertisementEventPayload
            {
                EventId = eventId,
                EventType = "chat_block",
                SteamId64 = player.SteamID.ToString(CultureInfo.InvariantCulture),
                PlayerName = SanitizeAntiAdvertisementValue(player.Name, 500),
                MatchValue = SanitizeAntiAdvertisementValue(match, 500),
                Action = SanitizeAntiAdvertisementValue(action, 100),
                MessageExcerpt = logBlockedText ? SanitizeAntiAdvertisementValue(text, 500) : string.Empty,
                ObservedAt = DateTimeOffset.UtcNow
            };
            AtomicWrite(Path.Combine(_antiAdOutboxDir, eventId + ".json"), JsonSerializer.Serialize(payload, _json));
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Frueh gestopptes Werbeschutz-Ereignis konnte nicht vorgemerkt werden.");
        }
    }

    private static string NormalizeAntiAdvertisementText(string value)
    {
        string normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        normalized = normalized.Replace("[.]", ".", StringComparison.Ordinal)
            .Replace("(.)", ".", StringComparison.Ordinal)
            .Replace("{.}", ".", StringComparison.Ordinal)
            .Replace(" dot ", ".", StringComparison.Ordinal)
            .Replace("\u200b", string.Empty, StringComparison.Ordinal)
            .Replace("\u200c", string.Empty, StringComparison.Ordinal)
            .Replace("\u200d", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"\s*\.\s*", ".");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string NormalizeAntiAdvertisementDomain(string value)
    {
        string normalized = NormalizeAntiAdvertisementText(value)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        int slash = normalized.IndexOf('/');
        if (slash >= 0) normalized = normalized[..slash];
        return Regex.Replace(normalized, @"\s+", string.Empty);
    }

    private static string SanitizeAntiAdvertisementValue(string? value, int maxLength)
    {
        string sanitized = Regex.Replace((value ?? string.Empty).Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private void ApplyGlobalAntiAdvertisementSnapshot(GlobalAntiAdvertisementPayload? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(_antiAdGlobalFile)) return;

        var snapshot = new GlobalAntiAdvertisementPayload
        {
            Version = payload.Version > 0 ? payload.Version : 1,
            GeneratedAt = payload.GeneratedAt == default ? DateTimeOffset.UtcNow : payload.GeneratedAt,
            BlockedSteamIds = (payload.BlockedSteamIds ?? [])
                .Where(value => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Domains = (payload.Domains ?? [])
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AllowedDomains = (payload.AllowedDomains ?? [])
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BlockedPhrases = (payload.BlockedPhrases ?? [])
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .Where(value => value.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            MarketingKeywords = (payload.MarketingKeywords ?? [])
                .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
                .Where(value => value.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        UpdateAntiAdvertisementRuntimeSnapshot(snapshot);
        AtomicWrite(_antiAdGlobalFile, JsonSerializer.Serialize(snapshot, _json));
    }

    private void AckAntiAdvertisementOutbox(IEnumerable<string> files)
    {
        if (string.IsNullOrWhiteSpace(_antiAdOutboxDir)) return;
        string root = Path.GetFullPath(_antiAdOutboxDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (string file in files ?? [])
        {
            try
            {
                string fullPath = Path.GetFullPath(file);
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                // Bei einem Fehler bleibt der Eintrag für den nächsten erfolgreichen Austausch erhalten.
                Core.Logger.LogDebug(ex, "AntiAdvertisement-Outbox-Eintrag konnte nicht bestätigt werden: {File}", Path.GetFileName(file));
            }
        }
    }

    private void LoadPersistentData()
    {
        try
        {
            if (File.Exists(_globalBansFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, GlobalBanEntry>>(File.ReadAllText(_globalBansFile), _json);
                if (data is not null)
                {
                    lock (_globalBanSync) foreach (var x in data) _globalBans[x.Key] = x.Value;
                }
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "global_bans.json konnte nicht geladen werden."); }
        try
        {
            if (File.Exists(_globalCommsFile))
            {
                var data = JsonSerializer.Deserialize<GlobalCommCache>(File.ReadAllText(_globalCommsFile), _json);
                if (data is not null)
                {
                    lock (_globalCommSync)
                    {
                        foreach (var x in data.Mutes ?? new Dictionary<ulong, CommEntry>()) _globalMutes[x.Key] = x.Value;
                        foreach (var x in data.Gags ?? new Dictionary<ulong, CommEntry>()) _globalGags[x.Key] = x.Value;
                    }
                }
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "global_communication_sanctions.json konnte nicht geladen werden."); }
        try
        {
            if (File.Exists(_bansFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, BanEntry>>(File.ReadAllText(_bansFile), _json);
                if (data is not null) foreach (var x in data) _bans[x.Key] = x.Value;
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "bans.json konnte nicht geladen werden."); }
        try
        {
            if (File.Exists(_mutesFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, CommEntry>>(File.ReadAllText(_mutesFile), _json);
                if (data is not null) foreach (var x in data) _mutes[x.Key] = x.Value;
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "mutes.json konnte nicht geladen werden."); }
        try
        {
            if (File.Exists(_gagsFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, CommEntry>>(File.ReadAllText(_gagsFile), _json);
                if (data is not null) foreach (var x in data) _gags[x.Key] = x.Value;
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "gags.json konnte nicht geladen werden."); }
        try
        {
            if (File.Exists(_nicknamesFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<ulong, NicknameEntry>>(File.ReadAllText(_nicknamesFile), _json);
                if (data is not null)
                {
                    foreach (var x in data)
                    {
                        NormalizeNicknameEntry(x.Value);
                        _nicknames[x.Key] = x.Value;
                    }
                }
            }
        } catch (Exception ex) { Core.Logger.LogWarning(ex, "nickname_overrides.json konnte nicht geladen werden."); }
    }

    private void SavePersistentData() { SaveGlobalBans(); SaveGlobalComms(); SaveBans(); SaveMutes(); SaveGags(); SaveNicknames(); }
    private void SaveGlobalBans()
    {
        lock (_globalBanSync)
            AtomicWrite(_globalBansFile, JsonSerializer.Serialize(_globalBans, _json));
    }
    private void SaveGlobalComms()
    {
        lock (_globalCommSync)
        {
            var cache = new GlobalCommCache { Mutes = new Dictionary<ulong, CommEntry>(_globalMutes), Gags = new Dictionary<ulong, CommEntry>(_globalGags) };
            AtomicWrite(_globalCommsFile, JsonSerializer.Serialize(cache, _json));
        }
    }
    private void SaveBans() => AtomicWrite(_bansFile, JsonSerializer.Serialize(_bans, _json));
    private void SaveMutes() => AtomicWrite(_mutesFile, JsonSerializer.Serialize(_mutes, _json));
    private void SaveGags() => AtomicWrite(_gagsFile, JsonSerializer.Serialize(_gags, _json));
    private void SaveNicknames() => AtomicWrite(_nicknamesFile, JsonSerializer.Serialize(_nicknames, _json));

    private void WriteResult(string id, bool success, string message)
    {
        try
        {
            var result = new { id, success, message = Limit(message, 500), executed_at = DateTimeOffset.UtcNow };
            AtomicWrite(Path.Combine(_results, SafeFileName(id) + ".json"), JsonSerializer.Serialize(result, _json));
            CleanupOldResults();
        } catch { }
    }

    private void CleanupOldResults()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_results, "*.json"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-2)) File.Delete(file);
        } catch { }
    }

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content + Environment.NewLine);
        File.Move(tmp, path, true);
    }

    private static string SafeFileName(string value)
        => new(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').Take(80).ToArray());
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    private static float Clamp(float value, float min, float max) => MathF.Min(max, MathF.Max(min, value));
    private static ulong ParseTarget(BridgeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TargetSteamid))
            throw new InvalidOperationException($"Aktion '{command.Action}' benötigt eine SteamID64, aber target_steamid ist leer.");

        return ParseSteamId(command.TargetSteamid);
    }

    private static ulong ParseSteamId(string? value)
    {
        if (ulong.TryParse(value, out var id) && id >= 76561197960265728UL)
            return id;

        throw new InvalidOperationException($"Ungültige SteamID64: '{value ?? "<leer>"}'.");
    }
    private static string GetString(Dictionary<string, JsonElement> p, string key, string fallback) => p.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : fallback;
    private static List<string> GetStringArray(Dictionary<string, JsonElement> p, string key)
    {
        var result = new List<string>();
        if (!p.TryGetValue(key, out var e) || e.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in e.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is string value) result.Add(value);
        return result;
    }
    private static int GetInt(Dictionary<string, JsonElement> p, string key, int fallback) => p.TryGetValue(key, out var e) && e.TryGetInt32(out var v) ? v : fallback;
    private static Dictionary<string, int> GetIntDictionary(Dictionary<string, JsonElement> p, string key)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!p.TryGetValue(key, out var e)) return result;
        if (e.ValueKind == JsonValueKind.Array) return result; // [] bedeutet explizit: alle Limits löschen.
        if (e.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in e.EnumerateObject())
            if (property.Value.TryGetInt32(out int value)) result[property.Name] = value;
        return result;
    }
    private static bool GetBool(Dictionary<string, JsonElement> p, string key, bool fallback) => p.TryGetValue(key, out var e) && (e.ValueKind is JsonValueKind.True or JsonValueKind.False) ? e.GetBoolean() : fallback;
    private static float GetFloat(Dictionary<string, JsonElement> p, string key, float fallback) => p.TryGetValue(key, out var e) && e.TryGetSingle(out var v) ? v : p.TryGetValue(key, out e) && e.TryGetDouble(out var d) ? (float)d : fallback;
    private static string FormatDuration(uint seconds) => seconds >= 3600 ? $"{seconds / 3600}h {(seconds % 3600) / 60}m" : seconds >= 60 ? $"{seconds / 60}m {seconds % 60}s" : $"{seconds}s";

    private static readonly Dictionary<string, ushort[]> WeaponDefinitionIds = new(StringComparer.Ordinal)
    {
        ["weapon_deagle"]=[1], ["weapon_elite"]=[2], ["weapon_fiveseven"]=[3], ["weapon_glock"]=[4], ["weapon_ak47"]=[7], ["weapon_aug"]=[8], ["weapon_awp"]=[9], ["weapon_famas"]=[10], ["weapon_g3sg1"]=[11], ["weapon_galilar"]=[13], ["weapon_m249"]=[14], ["weapon_m4a1"]=[16], ["weapon_mac10"]=[17], ["weapon_p90"]=[19], ["weapon_mp5sd"]=[23], ["weapon_ump45"]=[24], ["weapon_xm1014"]=[25], ["weapon_bizon"]=[26], ["weapon_mag7"]=[27], ["weapon_negev"]=[28], ["weapon_sawedoff"]=[29], ["weapon_tec9"]=[30], ["weapon_taser"]=[31], ["weapon_hkp2000"]=[32], ["weapon_mp7"]=[33], ["weapon_mp9"]=[34], ["weapon_nova"]=[35], ["weapon_p250"]=[36], ["weapon_scar20"]=[38], ["weapon_sg556"]=[39], ["weapon_ssg08"]=[40], ["weapon_knife"]=[41,42,59,500,503,505,506,507,508,509,512,514,515,516,517,518,519,520,521,522,523,525,526], ["weapon_flashbang"]=[43], ["weapon_hegrenade"]=[44], ["weapon_smokegrenade"]=[45], ["weapon_molotov"]=[46], ["weapon_decoy"]=[47], ["weapon_incgrenade"]=[48], ["weapon_m4a1_silencer"]=[60], ["weapon_usp_silencer"]=[61], ["weapon_cz75a"]=[63], ["weapon_revolver"]=[64]
    };

    private static readonly HashSet<string> AllowedWeapons = new(StringComparer.Ordinal)
    {
        "weapon_glock","weapon_hkp2000","weapon_usp_silencer","weapon_p250","weapon_fiveseven","weapon_tec9","weapon_cz75a","weapon_elite","weapon_deagle","weapon_revolver",
        "weapon_mac10","weapon_mp9","weapon_mp7","weapon_mp5sd","weapon_ump45","weapon_p90","weapon_bizon",
        "weapon_galilar","weapon_famas","weapon_ak47","weapon_m4a1","weapon_m4a1_silencer","weapon_aug","weapon_sg556",
        "weapon_ssg08","weapon_awp","weapon_scar20","weapon_g3sg1",
        "weapon_nova","weapon_xm1014","weapon_mag7","weapon_sawedoff","weapon_m249","weapon_negev",
        "weapon_hegrenade","weapon_flashbang","weapon_smokegrenade","weapon_molotov","weapon_incgrenade","weapon_decoy",
        "weapon_knife","weapon_taser"
    };
}



public sealed class InstalledWorkshopMap
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string Directory { get; set; } = "";
}

public sealed class MapSwitchTarget
{
    public bool IsWorkshop { get; set; }
    public string? WorkshopId { get; set; }
    public string MapName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SourceFolder { get; set; } = "";
}

public sealed class WorkshopMetadata
{
    public string Title { get; set; } = "";
    public string SourceFolder { get; set; } = "";
}

public sealed class BridgeCommand
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public string? TargetSteamid { get; set; }
    public string? RequestedBy { get; set; }
    public string? RequestedByRole { get; set; }
    public string NotifyScope { get; set; } = "admins";
    public bool NotifyTarget { get; set; } = true;
    public Dictionary<string, JsonElement>? Payload { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
}

public interface IExpiringEntry
{
    DateTimeOffset? ExpiresAt { get; }
}

public sealed class BanEntry : IExpiringEntry
{
    public string Reason { get; set; } = "Serverban";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class GlobalBanEntry : IExpiringEntry
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Permanent { get; set; }
}

public sealed class CommEntry : IExpiringEntry
{
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class NicknameEntry
{
    public string Original { get; set; } = ""; // Legacy-Feld
    public string SteamName { get; set; } = "";
    public string Override { get; set; } = "";
    public List<string> History { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BridgeStatus
{
    public string Version { get; set; } = "1.5.6";
    public DateTimeOffset UpdatedAt { get; set; }
    public string Map { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public int CtScore { get; set; }
    public int TScore { get; set; }
    public int RoundsPlayed { get; set; }
    public int MaxPlayers { get; set; }
    public bool MapStable { get; set; }
    public bool MapChanging { get; set; }
    public bool MapSwitchInFlight { get; set; }
    public string? MapSwitchTarget { get; set; }
    public string? MapSwitchTargetWorkshopId { get; set; }
    public string WorkshopPath { get; set; } = "";
    public List<InstalledWorkshopMap> InstalledWorkshopMaps { get; set; } = [];
    public List<StatusPlugin> Plugins { get; set; } = [];
    public List<StatusPlayer> Players { get; set; } = [];
    public LiveRuleStatus Rules { get; set; } = new();
    public List<StatusChatMessage> ChatMessages { get; set; } = [];
    public List<StatusBan> Bans { get; set; } = [];
    public List<StatusComm> Mutes { get; set; } = [];
    public List<StatusComm> Gags { get; set; } = [];
}

public sealed class LiveRuleStatus
{
    public int Gravity { get; set; } = 800;
    public float GlobalSpeed { get; set; } = 1.0f;
    public bool FriendlyFire { get; set; }
    public int InfiniteAmmo { get; set; }
    public int BuyAnywhere { get; set; }
    public bool Bunnyhop { get; set; }
    public bool AutoBhop { get; set; }
    public float Airaccelerate { get; set; } = 12f;
    public float FallDamageScale { get; set; } = 1f;
    public float DamageScale { get; set; } = 1f;
    public int FreezeTime { get; set; } = 15;
    public float RoundTime { get; set; } = 1.92f;
    public int BuyTime { get; set; } = 20;
    public int StartMoney { get; set; } = 800;
    public int MaxMoney { get; set; } = 16000;
    public int MaxRounds { get; set; } = 24;
    public bool AutoTeamBalance { get; set; } = true;
    public int Limitteams { get; set; } = 2;
    public bool HeadshotOnly { get; set; }
    public string WeaponMode { get; set; } = "normal";
    public string WeaponModeLabel { get; set; } = "Normal";
    public Dictionary<string, int> WeaponTeamLimits { get; set; } = new(StringComparer.Ordinal);
    public bool WeaponTeamLimitsIncludeBots { get; set; }
    public int BotQuota { get; set; }
    public int BotDifficulty { get; set; } = 2;
    public bool BotStop { get; set; }
    public string ActivePreset { get; set; } = "";
    public string PresetDuration { get; set; } = "manual";
    public int RulesRemainingRounds { get; set; }
    public List<LiveSpecialPlayerStatus> SpecialPlayers { get; set; } = [];
}

public sealed class LiveSpecialPlayerStatus
{
    public string SteamId64 { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Godmode { get; set; }
    public bool Noclip { get; set; }
    public bool Frozen { get; set; }
    public float SpeedMultiplier { get; set; } = 1.0f;
    public float GravityScale { get; set; } = 1.0f;
    public List<string> Modes
    {
        get
        {
            var modes = new List<string>();
            if (Godmode) modes.Add("Godmode");
            if (Noclip) modes.Add("Noclip");
            if (Frozen) modes.Add("Freeze");
            if (MathF.Abs(SpeedMultiplier - 1.0f) > 0.001f) modes.Add($"Speed {SpeedMultiplier:0.##}x");
            if (MathF.Abs(GravityScale - 1.0f) > 0.001f) modes.Add($"Gravity {GravityScale:0.##}x");
            return modes;
        }
    }
}

public sealed class StatusPlugin
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Website { get; set; } = "";
    public string MinimumApiVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public string Path { get; set; } = "";
    public string? LatestBackupId { get; set; }
    public bool UpdateSupported { get; set; }
}

public sealed class StatusChatMessage
{
    public string Id { get; set; } = "";
    public string SteamId64 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string CommunityRole { get; set; } = "spieler";
    public string Team { get; set; } = "Unassigned";
    public bool TeamOnly { get; set; }
    public string Message { get; set; } = "";
    public string MapName { get; set; } = "";
    public DateTimeOffset SentAt { get; set; }
}

public sealed class FastLiveConfig
{
    public bool Enabled { get; set; }
    public string PanelUrl { get; set; } = "";
    public int ServerId { get; set; }
    public string Secret { get; set; } = "";
    public int PushIntervalSeconds { get; set; } = 2;
}

public sealed class FastLiveResult
{
    public string Id { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset ExecutedAt { get; set; }
}

public sealed class FastLiveExchangeRequest
{
    public BridgeStatus Status { get; set; } = new();
    public List<FastLiveResult> Results { get; set; } = [];
    public List<AntiAdvertisementEventPayload> AntiAdvertisementEvents { get; set; } = [];
}

public sealed class FastLiveExchangeResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<BridgeCommand> Commands { get; set; } = [];
    public List<string> AckedResultIds { get; set; } = [];
    public List<GlobalBanPayload> GlobalBans { get; set; } = [];
    public List<GlobalCommPayload> GlobalCommSanctions { get; set; } = [];
    public GlobalAntiAdvertisementPayload? GlobalAntiAdvertisement { get; set; }
}

public sealed class AntiAdOutboxBatch
{
    public List<AntiAdvertisementEventPayload> Events { get; set; } = [];
    public List<string> Files { get; set; } = [];
}

public sealed class AntiAdvertisementEventPayload
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string SteamId64 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string MatchValue { get; set; } = "";
    public string Action { get; set; } = "";
    public string MessageExcerpt { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class GlobalAntiAdvertisementPayload
{
    public int Version { get; set; } = 2;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<string> BlockedSteamIds { get; set; } = [];
    public List<string> Domains { get; set; } = [];
    public List<string> AllowedDomains { get; set; } = [];
    public List<string> BlockedPhrases { get; set; } = [];
    public List<string> MarketingKeywords { get; set; } = [];
}

public sealed class AntiAdvertisementPolicyConfig
{
    public bool Enabled { get; set; } = true;
    public bool LogBlockedText { get; set; } = true;
    public List<string> BlockedDomains { get; set; } = [];
    public List<string> AllowedDomains { get; set; } = [];
    public List<string> MarketingKeywords { get; set; } = [];
}

public sealed class GlobalCommPayload
{
    public int Id { get; set; }
    public string SteamId64 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string SanctionType { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Permanent { get; set; }
}

public sealed class GlobalCommCache
{
    public Dictionary<ulong, CommEntry> Mutes { get; set; } = new();
    public Dictionary<ulong, CommEntry> Gags { get; set; } = new();
}

public sealed class GlobalBanPayload
{
    public int Id { get; set; }
    public string SteamId64 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Permanent { get; set; }
}

public sealed class StatusBan
{
    public string SteamId64 { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class StatusComm
{
    public string SteamId64 { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class StatusPlayer
{
    public string SteamId64 { get; set; } = "";
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public bool Alive { get; set; }
    public int Health { get; set; }
    public int Armor { get; set; }
    public int Kills { get; set; }
    public int Score { get; set; }
    public uint ConnectedSeconds { get; set; }
    public string ConnectedHuman { get; set; } = "";
    public bool IsBot { get; set; }
    public bool Frozen { get; set; }
    public bool Noclip { get; set; }
    public bool Godmode { get; set; }
    public float SpeedMultiplier { get; set; } = 1.0f;
    public float GravityScale { get; set; } = 1.0f;
    public bool VoiceMuted { get; set; }
    public bool Gagged { get; set; }
    public bool Silenced { get; set; }
    public string? MuteReason { get; set; }
    public DateTimeOffset? MuteExpiresAt { get; set; }
    public string? GagReason { get; set; }
    public DateTimeOffset? GagExpiresAt { get; set; }
    public int PlayerId { get; set; }
    public int UserId { get; set; }
    public string IpAddress { get; set; } = "";
    public uint Ping { get; set; }
    public string? NicknameOverride { get; set; }
    public string? OriginalName { get; set; }
    public List<string> NicknameHistory { get; set; } = new();
}

