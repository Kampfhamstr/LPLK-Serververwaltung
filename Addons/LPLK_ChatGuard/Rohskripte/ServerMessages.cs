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
    private HookResult OnServerMessagePre(EventServerMessage @event)
    {
        if (!ShouldBlockServerAdvertisement("event_server_message", @event.Text))
            return HookResult.Continue;

        @event.DontBroadcast = true;
        return HookResult.Handled;
    }

    private HookResult OnGameMessagePre(EventGameMessage @event)
    {
        if (!ShouldBlockServerAdvertisement("event_game_message", @event.Text))
            return HookResult.Continue;

        @event.DontBroadcast = true;
        return HookResult.Handled;
    }

    private HookResult OnPlayerHintMessagePre(EventPlayerHintmessage @event)
    {
        if (!ShouldBlockServerAdvertisement("event_player_hint", @event.HintMessage))
            return HookResult.Continue;

        @event.DontBroadcast = true;
        return HookResult.Handled;
    }

    private HookResult OnServerSayText(CUserMessageSayText message)
        => ShouldBlockServerAdvertisement("say_text", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerSayText2(CUserMessageSayText2 message)
        => ShouldBlockServerAdvertisement(
            "say_text_2",
            message.Messagename,
            message.Param1,
            message.Param2,
            message.Param3,
            message.Param4)
            ? HookResult.Stop
            : HookResult.Continue;

    private HookResult OnServerSayTextChannel(CUserMessageSayTextChannel message)
        => ShouldBlockServerAdvertisement("say_text_channel", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerTextMsg(CUserMessageTextMsg message)
        => ShouldBlockServerAdvertisement("text_msg", message.Param.ToArray()) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerColoredText(CUserMessageColoredText message)
        => ShouldBlockServerAdvertisement("colored_text", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerHudText(CUserMessageHudText message)
        => ShouldBlockServerAdvertisement("hud_text", message.Message) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerHudMsg(CUserMessageHudMsg message)
        => ShouldBlockServerAdvertisement("hud_msg", message.Message) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerShowMenu(CUserMessageShowMenu message)
        => ShouldBlockServerAdvertisement("show_menu", message.Menustring) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnServerPrint(CSVCMsg_Print message)
        => ShouldBlockServerAdvertisement("svc_print", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsHintText(CCSUsrMsg_HintText message)
        => ShouldBlockServerAdvertisement("cs_hint_text", message.Message) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsHudText(CCSUsrMsg_HudText message)
        => ShouldBlockServerAdvertisement("cs_hud_text", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsHudMsg(CCSUsrMsg_HudMsg message)
        => ShouldBlockServerAdvertisement("cs_hud_msg", message.Text) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsShowMenu(CCSUsrMsg_ShowMenu message)
        => ShouldBlockServerAdvertisement("cs_show_menu", message.MenuString) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsKeyHintText(CCSUsrMsg_KeyHintText message)
        => ShouldBlockServerAdvertisement("cs_key_hint_text", message.Messages.ToArray()) ? HookResult.Stop : HookResult.Continue;

    private HookResult OnCsRadioText(CCSUsrMsg_RadioText message)
        => ShouldBlockServerAdvertisement(
            "cs_radio_text",
            new[] { message.MsgName }.Concat(message.Params).ToArray())
            ? HookResult.Stop
            : HookResult.Continue;

    private bool ShouldBlockServerAdvertisement(string channel, params string?[] parts)
    {
        if (!_config.Enabled || !_config.SuppressAdvertisingServerMessages)
            return false;

        string text = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!LooksLikeAdvertisement(text, isPlayerName: false, out string match))
            return false;

        LogServerAdvertisementOnce(channel, text, match);
        return true;
    }

    private void LogServerAdvertisementOnce(string channel, string text, string match)
    {
        string sanitized = SanitizeForLog(text);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_serverAdvertisingLogSync)
        {
            if (string.Equals(_lastServerAdvertisingText, sanitized, StringComparison.Ordinal)
                && now - _lastServerAdvertisingAt < TimeSpan.FromSeconds(3))
                return;

            _lastServerAdvertisingText = sanitized;
            _lastServerAdvertisingAt = now;
        }

        WriteEvent("server_message_block", 0, "Console", match, "drop_" + channel, sanitized);
    }

    private HookResult OnPlayerConnectPre(EventPlayerConnect @event)
    {
        try
        {
            if (!_config.Enabled || !_config.CensorAdvertisingSystemMessages)
                return HookResult.Continue;

            string name = @event.Name ?? string.Empty;
            if (!LooksLikeAdvertisement(name, isPlayerName: true, out string match))
                return HookResult.Continue;

            ulong steamId = @event.XuID;
            if (steamId != 0 && IsElevatedRoleProtected(steamId, out string role))
            {
                // Der Werbename darf nicht als Connect-Werbung ausgespielt werden. Eine
                // hoehergestellte Rolle wird aber weder umbenannt noch automatisch bestraft.
                _advertisingDisconnects.Add(steamId);
                @event.DontBroadcast = true;
                WriteEvent("elevated_role_warning", steamId, name, match, $"connect_name_suppressed_{role}", name);
                return HookResult.Handled;
            }

            if (steamId != 0)
                _advertisingDisconnects.Add(steamId);

            string replacement = GetCensoredPlayerName();
            @event.Name = replacement;
            @event.DontBroadcast = true;
            WriteEvent("system_connect_censor", steamId, name, match, "name_replaced");
            return HookResult.Handled;
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Werbe-Systemmeldung beim Connect konnte nicht zensiert werden.");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnectPre(EventPlayerDisconnect @event)
    {
        ulong disconnected=@event.XuID;
        try
        {
            if (!_config.Enabled || !_config.SuppressAdvertisingDisconnectMessages)
                return HookResult.Continue;

            ulong steamId = @event.XuID;
            string name = @event.Name ?? string.Empty;
            bool trackedAdvertisingActivity = steamId != 0 && _advertisingDisconnects.Remove(steamId);
            bool advertisingName = LooksLikeAdvertisement(name, isPlayerName: true, out string match);

            if (steamId != 0 && advertisingName && IsElevatedRoleProtected(steamId, out string role))
            {
                @event.DontBroadcast = true;
                WriteEvent("elevated_role_warning", steamId, name, match, $"disconnect_name_suppressed_{role}", name);
                return HookResult.Handled;
            }

            if (!trackedAdvertisingActivity && !advertisingName)
                return HookResult.Continue;

            string disconnectMatch = advertisingName ? match : "previous-advertising-hit";
            string disconnectAction = advertisingName
                ? "advertising_name_silent"
                : "previous_advertising_hit_silent";

            // EventPlayerDisconnect.Name wird von CS2 fuer die oeffentliche Leave-Meldung
            // nicht auf allen Builds rechtzeitig uebernommen. Jede zuvor bestaetigte
            // Spielerwerbung wird deshalb verlaesslich komplett unterdrueckt; das interne
            // Ereignis bleibt erhalten.
            if (steamId != 0 && IsAlreadyBannedByBridge(steamId))
            {
                @event.DontBroadcast = true;
                WriteEvent("system_disconnect_suppress", steamId, name, disconnectMatch, "banned_silent");
                return HookResult.Handled;
            }

            @event.DontBroadcast = true;
            WriteEvent("system_disconnect_suppress", steamId, name, disconnectMatch, disconnectAction);
            return HookResult.Handled;
        }
        catch (Exception ex)
        {
            Core.Logger.LogDebug(ex, "Werbe-Systemmeldung beim Disconnect konnte nicht zensiert werden.");
        }
        finally
        {
            // This flag is connection-scoped, including when suppression is disabled.
            _advertisingDisconnects.Remove(disconnected);_nameChecked.Remove(disconnected);
        }

        return HookResult.Continue;
    }

}
