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
    private bool IsAlreadyBannedByBridge(ulong steamId)
    {
        return BanFileContainsActive(_bridgeBansPath, steamId, global: false)
            || BanFileContainsActive(_bridgeGlobalBansPath, steamId, global: true);
    }

    private static bool BanFileContainsActive(string path, ulong steamId, bool global)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty(steamId.ToString(CultureInfo.InvariantCulture), out JsonElement entry)) return false;

            if (global && entry.TryGetProperty("permanent", out JsonElement permanent)
                && permanent.ValueKind is JsonValueKind.True)
                return true;

            if (!entry.TryGetProperty("expires_at", out JsonElement expires)
                || expires.ValueKind == JsonValueKind.Null
                || string.IsNullOrWhiteSpace(expires.GetString()))
                return true;

            return DateTimeOffset.TryParse(expires.GetString(), CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var until)
                   && until > DateTimeOffset.UtcNow;
        }
        catch
        {
            // Ein Lesefehler darf niemals einen legitimen Spieler faelschlich blockieren.
            return false;
        }
    }

    private bool IsGloballyAdvertisingBlocked(ulong steamId)
    {
        lock (_globalBlockedSteamIds) return _globalBlockedSteamIds.Contains(steamId);
    }

    private void LoadGlobalProtection(bool force = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_globalProtectionPath) || !File.Exists(_globalProtectionPath)) return;
            DateTime stamp = File.GetLastWriteTimeUtc(_globalProtectionPath);
            if (!force && stamp == _globalProtectionLastWriteUtc) return;
            var data = JsonSerializer.Deserialize<GlobalProtectionSnapshot>(File.ReadAllText(_globalProtectionPath), _json);
            if (data is null || data.Version != 2) throw new InvalidDataException("Unbekannte Schutzregel-Version.");
            var ids = new HashSet<ulong>();
            foreach (string raw in data.BlockedSteamIds ?? []) if (ulong.TryParse(raw, out ulong id) && id != 0) ids.Add(id);
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in data.Domains ?? []) { string d = NormalizeDomain(raw); if (d.Length > 0) domains.Add(d); }
            var allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in data.AllowedDomains ?? []) { string d = NormalizeDomain(raw); if (d.Length > 0) allowedDomains.Add(d); }
            var blockedPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in data.BlockedPhrases ?? []) { string phrase = Normalize(raw); if (phrase.Length > 1) blockedPhrases.Add(phrase); }
            var marketingKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in data.MarketingKeywords ?? []) { string keyword = Normalize(raw); if (keyword.Length > 1) marketingKeywords.Add(keyword); }
            lock (_globalBlockedSteamIds) { _globalBlockedSteamIds.Clear(); foreach (ulong id in ids) _globalBlockedSteamIds.Add(id); }
            lock (_globalBlockedDomains) { _globalBlockedDomains.Clear(); foreach (string d in domains) _globalBlockedDomains.Add(d); }
            lock (_globalAllowedDomains) { _globalAllowedDomains.Clear(); foreach (string d in allowedDomains) _globalAllowedDomains.Add(d); }
            lock (_globalBlockedPhrases) { _globalBlockedPhrases.Clear(); foreach (string phrase in blockedPhrases) _globalBlockedPhrases.Add(phrase); }
            lock (_globalMarketingKeywords) { _globalMarketingKeywords.Clear(); foreach (string keyword in marketingKeywords) _globalMarketingKeywords.Add(keyword); }
            _globalProtectionLastWriteUtc = stamp;
            _nameChecked.Clear();
        }
        catch (Exception ex) { Core.Logger.LogDebug(ex, "Globaler Werbeschutz-Snapshot konnte nicht geladen werden."); }
    }

    private void QueueGlobalEvent(string type, ulong steamId, string playerName, string match, string action, string? text)
    {
        if (!_config.ExportBridgeEvents || string.IsNullOrWhiteSpace(_bridgeOutboxDir)) return;
        if (type is not ("chat_block" or "name_block" or "system_connect_censor" or "server_message_block")) return;
        try
        {
            Directory.CreateDirectory(_bridgeOutboxDir);
            string eventId = Guid.NewGuid().ToString("N");
            var payload = new GlobalAdvertisingEvent
            {
                EventId = eventId,
                EventType = type,
                SteamId64 = steamId == 0 ? "" : steamId.ToString(CultureInfo.InvariantCulture),
                PlayerName = SanitizeForLog(playerName),
                MatchValue = SanitizeForLog(match),
                Action = SanitizeForLog(action),
                MessageExcerpt = _config.LogBlockedText ? SanitizeForLog(text ?? string.Empty) : "",
                ObservedAt = DateTimeOffset.UtcNow
            };
            string tmp = Path.Combine(_bridgeOutboxDir, eventId + ".tmp");
            string path = Path.Combine(_bridgeOutboxDir, eventId + ".json");
            File.WriteAllText(tmp, JsonSerializer.Serialize(payload, _json));
            File.Move(tmp, path, true);
        }
        catch (Exception ex) { Core.Logger.LogDebug(ex, "Werbeschutz-Ereignis konnte nicht fuer die globale Synchronisation vorgemerkt werden."); }
    }

}
