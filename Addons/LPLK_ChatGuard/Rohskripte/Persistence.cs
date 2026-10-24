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

public sealed partial class Plugin
{
    private void CleanupOldStates()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int window = Math.Clamp(_config.RepeatWindowMinutes, 1, 1440);
        foreach (ulong key in _violations.Where(x => now - x.Value.LastAt > TimeSpan.FromMinutes(window * 2)).Select(x => x.Key).ToArray())
            _violations.Remove(key);
        foreach (ulong key in _gagUntil.Where(x => x.Value <= now).Select(x => x.Key).ToArray())
            _gagUntil.Remove(key);
        foreach (ulong key in _lastSuccessBroadcast.Where(x => now - x.Value > TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray())
            _lastSuccessBroadcast.Remove(key);
        foreach (ulong key in _lastElevatedRoleWarning.Where(x => now - x.Value > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray())
            _lastElevatedRoleWarning.Remove(key);
    }

    private void EnsureDefaultConfig()
    {
        if (File.Exists(_configPath)) return;
        string seed=Path.Combine(Core.PluginPath,"resources","settings.seed.json");
        var initial=File.Exists(seed)?JsonSerializer.Deserialize<AntiAdConfig>(File.ReadAllText(seed),_json)??throw new InvalidDataException("Leere Startkonfiguration."):AntiAdConfig.CreateDefault();
        ChatGuardPolicy.Validate(initial);SaveConfig(initial);
    }

    private void ReloadConfigIfChanged()
    {
        if (DateTimeOffset.UtcNow - _lastConfigCheck < TimeSpan.FromSeconds(3)) return;
        _lastConfigCheck = DateTimeOffset.UtcNow;
        if(_settingsStore.Reload()){_config=_settingsStore.Current;_nameChecked.Clear();}
        _configError=_settingsStore.Error;
    }

    private void LoadConfig()
    {
        _settingsStore=new(_configPath,_json);_config=_settingsStore.Current;_configError=null;_nameChecked.Clear();
    }

    private void WriteEvent(string type, ulong steamId, string playerName, string match, string action, string? text = null)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                type,
                steam_id64 = steamId.ToString(CultureInfo.InvariantCulture),
                player = SanitizeForLog(playerName),
                match = SanitizeForLog(match),
                action,
                text = _config.LogBlockedText ? SanitizeForLog(text ?? string.Empty) : null
            }, _json);
            lock (_logSync) File.AppendAllText(_eventsLog, line + Environment.NewLine);
            QueueGlobalEvent(type, steamId, playerName, match, action, text);
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "AntiAdvertisement Ereignis konnte nicht protokolliert werden.");
        }
    }

}
