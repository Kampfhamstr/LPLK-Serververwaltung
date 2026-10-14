using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LPLK.Common;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace LPLK_ChatGuard;

[PluginMetadata(Id="LPLK_ChatGuard",Name="LPLK_ChatGuard",Version="0.1.1",Author="LPLK",Description="Chat-, Namen- und Werbeschutz mit gemeinsamer Administration",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<ulong, ViolationState> _violations = new();
    private readonly Dictionary<ulong, DateTimeOffset> _gagUntil = new();
    private readonly Dictionary<ulong, string> _nameChecked = new();
    private readonly object _logSync = new();
    // Jede bestaetigte Werbeerkennung dieser Verbindung merkt die SteamID vor.
    // Beim spaeteren player_disconnect wird ausschliesslich die oeffentliche
    // Leave-Meldung unterdrueckt; die Serverkonsole bleibt unveraendert.
    private readonly HashSet<ulong> _advertisingDisconnects = new();
    private readonly Dictionary<ulong, DateTimeOffset> _lastSuccessBroadcast = new();
    private Guid _playerConnectPreHookGuid;
    private Guid _playerDisconnectPreHookGuid;
    private Guid _serverMessagePreHookGuid;
    private Guid _gameMessagePreHookGuid;
    private Guid _playerHintMessagePreHookGuid;
    private readonly List<Guid> _serverNetMessageHookGuids = [];
    private readonly object _serverAdvertisingLogSync = new();
    private string _lastServerAdvertisingText = string.Empty;
    private DateTimeOffset _lastServerAdvertisingAt = DateTimeOffset.MinValue;

    private AntiAdConfig _config = new();
    private string _root = string.Empty;
    private string _configPath = string.Empty;
    private string _eventsLog = string.Empty;
    private string _bridgeBansPath = string.Empty;
    private string _bridgeGlobalBansPath = string.Empty;
    // v1.0.4: serveruebergreifender Werbeschutz via Bridge/Fast Live.
    private string _globalProtectionPath = string.Empty;
    private string _bridgeOutboxDir = string.Empty;
    private DateTime _globalProtectionLastWriteUtc = DateTime.MinValue;
    private readonly HashSet<ulong> _globalBlockedSteamIds = new();
    private readonly HashSet<string> _globalBlockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _globalAllowedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _globalBlockedPhrases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _globalMarketingKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, string> _gameRolesBySteamId = new();
    private readonly Dictionary<ulong, DateTimeOffset> _lastElevatedRoleWarning = new();
    private string _permissionsPath = string.Empty;
    private DateTime _permissionsLastWriteUtc = DateTime.MinValue;
    private DateTimeOffset _lastConfigCheck = DateTimeOffset.MinValue;
    private DateTime _configLastWriteUtc = DateTime.MinValue;

    public override void Load(bool hotReload)
    {
        _root = Core.PluginDataDirectory;
        _configPath = Path.Combine(_root, "config.json");
        _eventsLog = Path.Combine(_root, "events.log");

        string bridgeRoot = Path.Combine(Core.GameFilesDirectory, "addons", "swiftlys2", "data", "serververwaltung_bridge");
        _bridgeBansPath = Path.Combine(bridgeRoot, "bans.json");
        _bridgeGlobalBansPath = Path.Combine(bridgeRoot, "global_bans.json");
        _permissionsPath = Path.Combine(Core.GameFilesDirectory, "addons", "swiftlys2", "configs", "permissions.jsonc");
        _globalProtectionPath = Path.Combine(_root, "global_protection.json");
        _bridgeOutboxDir = Path.Combine(_root, "bridge_outbox");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_bridgeOutboxDir);
        _module=new(Core,"LPLK_ChatGuard","Werbeschutz","lplk.chatguard.admin",OpenAdmin);
        EnsureDefaultConfig();
        EnsureRuleSeed();
        LoadConfig();
        LoadGlobalProtection(force: true);
        LoadGameRoles(force: true);

        _playerConnectPreHookGuid = Core.GameEvent.HookPre<EventPlayerConnect>(OnPlayerConnectPre);
        _playerDisconnectPreHookGuid = Core.GameEvent.HookPre<EventPlayerDisconnect>(OnPlayerDisconnectPre);
        _serverMessagePreHookGuid = Core.GameEvent.HookPre<EventServerMessage>(OnServerMessagePre);
        _gameMessagePreHookGuid = Core.GameEvent.HookPre<EventGameMessage>(OnGameMessagePre);
        _playerHintMessagePreHookGuid = Core.GameEvent.HookPre<EventPlayerHintmessage>(OnPlayerHintMessagePre);
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageSayText>(OnServerSayText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageSayText2>(OnServerSayText2));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageSayTextChannel>(OnServerSayTextChannel));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageTextMsg>(OnServerTextMsg));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageColoredText>(OnServerColoredText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageHudText>(OnServerHudText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageHudMsg>(OnServerHudMsg));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CUserMessageShowMenu>(OnServerShowMenu));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CSVCMsg_Print>(OnServerPrint));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_HintText>(OnCsHintText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_HudText>(OnCsHudText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_HudMsg>(OnCsHudMsg));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_ShowMenu>(OnCsShowMenu));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_KeyHintText>(OnCsKeyHintText));
        _serverNetMessageHookGuids.Add(Core.NetMessage.HookServerMessage<CCSUsrMsg_RadioText>(OnCsRadioText));
        _active=true;_module.Active=true;WriteStatus();
        Core.Logger.LogInformation("LPLK_ChatGuard 0.1.1 geladen. Config: {Path}", _configPath);
    }

    public override void Unload()
    {
        _active=false;if(_module is not null)_module.Active=false;
        if (_playerConnectPreHookGuid != Guid.Empty) Core.GameEvent.Unhook(_playerConnectPreHookGuid);
        if (_playerDisconnectPreHookGuid != Guid.Empty) Core.GameEvent.Unhook(_playerDisconnectPreHookGuid);
        if (_serverMessagePreHookGuid != Guid.Empty) Core.GameEvent.Unhook(_serverMessagePreHookGuid);
        if (_gameMessagePreHookGuid != Guid.Empty) Core.GameEvent.Unhook(_gameMessagePreHookGuid);
        if (_playerHintMessagePreHookGuid != Guid.Empty) Core.GameEvent.Unhook(_playerHintMessagePreHookGuid);
        foreach (Guid hookGuid in _serverNetMessageHookGuids)
            if (hookGuid != Guid.Empty) Core.NetMessage.Unhook(hookGuid);
        _serverNetMessageHookGuids.Clear();
        _playerConnectPreHookGuid = Guid.Empty;
        _playerDisconnectPreHookGuid = Guid.Empty;
        _serverMessagePreHookGuid = Guid.Empty;
        _gameMessagePreHookGuid = Guid.Empty;
        _playerHintMessagePreHookGuid = Guid.Empty;
        _violations.Clear();
        _gagUntil.Clear();
        _nameChecked.Clear();
        _advertisingDisconnects.Clear();
        _lastSuccessBroadcast.Clear();
        _globalBlockedSteamIds.Clear();
        _globalBlockedDomains.Clear();
        _globalAllowedDomains.Clear();
        _globalBlockedPhrases.Clear();
        _globalMarketingKeywords.Clear();
        _gameRolesBySteamId.Clear();
        _lastElevatedRoleWarning.Clear();
        _permissionsLastWriteUtc = DateTime.MinValue;
        _lastServerAdvertisingText = string.Empty;
        _lastServerAdvertisingAt = DateTimeOffset.MinValue;
    }

}
