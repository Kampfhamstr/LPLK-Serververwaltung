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
    [ClientChatHookHandler]
    public HookResult OnClientChat(int playerId, string text, bool teamonly) => BridgePresent ? HookResult.Continue : FilterChat(playerId,text,teamonly);

    private HookResult FilterChat(int playerId, string text, bool teamonly)
    {
        if(playerId<0)return HookResult.Continue;
        var player = Core.PlayerManager.GetPlayer(playerId);
        if (!_active || player is null || !player.IsValid || !player.IsAuthorized || player.IsFakeClient || player.SteamID == 0)
            return HookResult.Continue;

        ulong steamId = player.SteamID;
        bool elevatedRoleProtected = IsElevatedRoleProtected(steamId, out string gameRole);

        // Bereits gebannte SteamIDs werden vollkommen still behandelt. Dadurch
        // wiederholt dieses Plugin weder den Namen noch den Werbetext eines Accounts,
        // den die Serververwaltung ohnehin disconnecten wird.
        if (IsAlreadyBannedByBridge(steamId))
            return HookResult.Stop;

        LoadGlobalProtection();
        if (!elevatedRoleProtected && IsGloballyAdvertisingBlocked(steamId))
        {
            _advertisingDisconnects.Add(steamId);
            return HookResult.Stop;
        }

        if (!_config.Enabled)
            return HookResult.Continue;

        if (elevatedRoleProtected)
        {
            // Ein alter, automatisch gesetzter lokaler Werbe-Gag darf nach einer
            // Rollenaufwertung nicht weiterwirken. Manuelle Bridge-Sanktionen bleiben
            // davon unberuehrt und werden weiterhin von der Bridge durchgesetzt.
            _gagUntil.Remove(steamId);
            _violations.Remove(steamId);

            if (!LooksLikeAdvertisement(text, isPlayerName: false, out string elevatedMatch))
                return HookResult.Continue;

            _advertisingDisconnects.Add(steamId);
            WarnElevatedRole(player, gameRole, text, elevatedMatch, isPlayerName: false);
            return HookResult.Stop;
        }

        // v1.0.7: Ein bekannter Werbename darf auch im Zeitfenster vor dem
        // naechsten einsekündigen Namensscan keinerlei Text senden. Der Scan
        // neutralisiert und entfernt den Account weiterhin wie bisher.
        if (_config.CheckPlayerNames
            && LooksLikeAdvertisement(player.Name ?? string.Empty, isPlayerName: true, out _))
        {
            _advertisingDisconnects.Add(steamId);
            return HookResult.Stop;
        }

        if (_gagUntil.TryGetValue(steamId, out var gagUntil))
        {
            if (gagUntil > DateTimeOffset.UtcNow)
                return HookResult.Stop;
            _gagUntil.Remove(steamId);
        }

        if (!LooksLikeAdvertisement(text, isPlayerName: false, out string match))
            return HookResult.Continue;

        HandleChatAdvertisement(player, text, match);
        return HookResult.Stop;
    }

    private void ScanPlayerNames()
    {
        try
        {
            ReloadConfigIfChanged();
            LoadGlobalProtection();
            LoadGameRoles();
            if (!_config.Enabled || !_config.CheckPlayerNames)
                return;

            var active = new HashSet<ulong>();
            foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            {
                if (player.IsFakeClient || player.SteamID == 0)
                    continue;

                ulong steamId = player.SteamID;
                active.Add(steamId);

                string currentName = player.Name ?? string.Empty;

                if (IsElevatedRoleProtected(steamId, out string gameRole))
                {
                    _gagUntil.Remove(steamId);
                    _violations.Remove(steamId);

                    if (_nameChecked.TryGetValue(steamId, out string? elevatedCheckedName)
                        && string.Equals(elevatedCheckedName, currentName, StringComparison.Ordinal))
                        continue;

                    _nameChecked[steamId] = currentName;
                    if (LooksLikeAdvertisement(currentName, isPlayerName: true, out string elevatedMatch))
                        WarnElevatedRole(player, gameRole, currentName, elevatedMatch, isPlayerName: true);
                    continue;
                }

                // Auf einem anderen Server bereits als Werbe-Identitaet erkannt:
                // lokal still neutralisieren und entfernen, ohne die Werbung erneut zu zeigen.
                if (IsGloballyAdvertisingBlocked(steamId))
                {
                    _nameChecked[steamId] = currentName;
                    // Eine vom zentralen Werbeschutz bestaetigte SteamID wird fuer
                    // die laufende Verbindung vorgemerkt. So kann ihr Name auch nach
                    // einem Namenswechsel nicht mehr in der Leave-Zeile auftauchen.
                    _advertisingDisconnects.Add(steamId);
                    MaskAdvertisingPlayerName(player);
                    ScheduleKick(player, () =>
                    {
                        try
                        {
                            var target = Core.PlayerManager.GetPlayerFromSteamId(steamId, true);
                            target?.Kick(_config.KickReason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                        }
                        catch (Exception ex) { Core.Logger.LogDebug(ex, "Global erkannte Werbe-ID konnte nicht entfernt werden: {SteamId}", steamId); }
                    });
                    continue;
                }

                if (_nameChecked.TryGetValue(steamId, out string? checkedName)
                    && string.Equals(checkedName, currentName, StringComparison.Ordinal))
                    continue;

                // Bereits gebannte Accounts sind Sache der Bridge und werden absichtlich
                // ohne Erfolgsmeldung behandelt. Die Bridge neutralisiert den sichtbaren
                // Namen vor dem Disconnect.
                if (IsAlreadyBannedByBridge(steamId))
                {
                    _nameChecked[steamId] = currentName;
                    continue;
                }

                if (!LooksLikeAdvertisement(currentName, isPlayerName: true, out string match))
                {
                    _nameChecked[steamId] = currentName;
                    continue;
                }

                _nameChecked[steamId] = currentName;
                _advertisingDisconnects.Add(steamId);
                WriteEvent("name_block", steamId, currentName, match, "mask_and_kick");

                // Wichtig: Der Original-Werbename darf nicht noch einmal durch eine
                // von CS2 erzeugte Leave-/Kick-Meldung verbreitet werden. Deshalb wird
                // der sichtbare Name zuerst neutralisiert und der Kick erst im naechsten
                // World-Update ausgefuehrt.
                MaskAdvertisingPlayerName(player);
                BroadcastSuccess("Werbe-Bot automatisch blockiert.", player);
                ScheduleKick(player, () =>
                {
                    try
                    {
                        var target = Core.PlayerManager.GetPlayerFromSteamId(steamId, true);
                        target?.Kick(_config.KickReason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.LogDebug(ex, "Werbe-Bot Kick nach Namenszensur fehlgeschlagen: {SteamId}", steamId);
                    }
                });
            }

            // Spieler, die den Server verlassen haben, duerfen bei einem spaeteren Join
            // erneut mit ihrem dann aktuellen Namen geprueft werden.
            foreach (ulong key in _nameChecked.Keys.Where(x => !active.Contains(x)).ToArray()) _nameChecked.Remove(key);
            foreach (ulong key in _advertisingDisconnects.Where(x => !active.Contains(x)).ToArray()) _advertisingDisconnects.Remove(key);
            CleanupOldStates();
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning(ex, "AntiAdvertisement Namenspruefung fehlgeschlagen.");
        }
    }

    private string GetCensoredPlayerName()
    {
        string replacement = SanitizeForLog(_config.CensoredPlayerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(replacement)) replacement = "Werbe-Bot";
        if (replacement.Length > 24) replacement = replacement[..24];
        return replacement;
    }

    private void MaskAdvertisingPlayerName(IPlayer player)
    {
        if (!_config.MaskAdvertisingPlayerNames)
            return;

        string replacement = GetCensoredPlayerName();

        try
        {
            player.Name = replacement;
            player.Controller.PlayerName = replacement;
            player.Controller.PlayerNameUpdated();
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Werbename konnte vor dem Kick nicht neutralisiert werden.");
        }
    }

    private void WarnElevatedRole(
        IPlayer player,
        string role,
        string originalText,
        string match,
        bool isPlayerName)
    {
        ulong steamId = player.SteamID;
        // Hoehergestellte Rollen bleiben bei Warn-only: kein Gag, Kick oder Ban.
        // Die bestaetigte Werbung wird dennoch fuer eine stille Leave-Zeile vorgemerkt.
        _advertisingDisconnects.Add(steamId);
        string normalizedRole = NormalizeGameRole(role);
        string action = isPlayerName
            ? $"warn_only_name_{normalizedRole}"
            : $"warn_only_chat_{normalizedRole}";

        // Diese Ereignisart bleibt bewusst lokal. Sie darf weder eine globale
        // Werbeidentitaet noch eine Moderationskarte mit Ban-Aktion erzeugen.
        WriteEvent("elevated_role_warning", steamId, player.Name, match, action, originalText);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int cooldownSeconds = Math.Clamp(_config.ElevatedRoleWarningCooldownSeconds, 1, 300);
        if (_lastElevatedRoleWarning.TryGetValue(steamId, out DateTimeOffset lastWarning)
            && now - lastWarning < TimeSpan.FromSeconds(cooldownSeconds))
            return;

        _lastElevatedRoleWarning[steamId] = now;
        string roleLabel = FormatGameRole(normalizedRole);
        if (isPlayerName)
        {
            player.SendLplkChat($"[Werbeschutz] Hinweis fuer {roleLabel}: Dein Name enthaelt ein Werbemuster. Bitte aendere ihn. Es wurde keine automatische Sanktion gesetzt.");
            return;
        }

        player.SendLplkChat($"[Werbeschutz] Hinweis fuer {roleLabel}: Der Werbetext wurde unterdrueckt. Kein Strike, Gag, Kick oder automatischer Ban.");
    }

    private void HandleChatAdvertisement(IPlayer player, string originalText, string match)
    {
        ulong steamId = player.SteamID;
        // Der Werbetext ist bereits bestaetigt und wird nicht ausgespielt. Die
        // SteamID bleibt bis zum Disconnect markiert, damit die Leave-Meldung nicht
        // den (gegebenenfalls noch werblichen) Namen dieses Accounts verbreitet.
        _advertisingDisconnects.Add(steamId);
        var now = DateTimeOffset.UtcNow;
        _violations.TryGetValue(steamId,out var previous);
        var state=ChatGuardPolicy.RegisterViolation(previous,now,_config.RepeatWindowMinutes);
        _violations[steamId] = state;
        string action=ChatGuardPolicy.ViolationAction(state.Count,_config);

        if (action=="kick")
        {
            WriteEvent("chat_block", steamId, player.Name, match, "kick", originalText);
            BroadcastSuccess("Werbung blockiert; wiederholter Werbe-Spam wurde entfernt.", player);
            player.Kick(_config.KickReason, ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
            return;
        }

        if (action=="gag")
        {
            int gagMinutes = Math.Clamp(_config.GagMinutes, 1, 1440);
            _gagUntil[steamId] = now.AddMinutes(gagMinutes);
            WriteEvent("chat_block", steamId, player.Name, match, $"gag_{gagMinutes}m", originalText);
            player.SendLplkChat($"[Werbeschutz] Werbung ist nicht erlaubt. Dein Textchat wurde fuer {gagMinutes} Minuten gesperrt.");
            BroadcastSuccess("Werbung automatisch blockiert und Wiederholung gesperrt.", player);
            return;
        }

        WriteEvent("chat_block", steamId, player.Name, match, "warn", originalText);
        player.SendLplkChat("[Werbeschutz] Werbung und Spam sind auf diesem Server nicht erlaubt.");
        BroadcastSuccess("Werbung automatisch blockiert.", player);
    }

    private void BroadcastSuccess(string message, IPlayer? offender = null)
    {
        if (!_config.BroadcastSuccess)
            return;

        if (offender is not null && offender.SteamID != 0)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_lastSuccessBroadcast.TryGetValue(offender.SteamID, out DateTimeOffset last)
                && now - last < TimeSpan.FromSeconds(5))
                return;
            _lastSuccessBroadcast[offender.SteamID] = now;
        }

        string suffix = string.Empty;
        if (_config.BroadcastOffenderName && offender is not null)
        {
            string safe = SanitizeForChat(offender.Name);
            if (!string.IsNullOrWhiteSpace(safe)) suffix = $" ({safe})";
        }

        foreach (var target in Core.PlayerManager.GetAllValidPlayers())
        {
            if (target.IsFakeClient || target.SteamID == 0)
                continue;
            target.SendLplkChat($"[Werbeschutz] ✓ {message}{suffix}");
        }
    }

}
