using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private static string NoticeText(string text)
    {
        // Names come from clients. Remove chat formatting and line breaks before adding our colors.
        return new string(ChatTheme.Plain(text).Where(c => !char.IsControl(c)).Take(72).ToArray()).Trim();
    }

    private void NotifyAdminAction(IPlayer? actor, string action, string? target = null)
    {
        ulong actorId = actor?.SteamID ?? 0;
        string actorName = NoticeText(actor?.Name ?? "Serverkonsole");
        string targetText = target is null ? "" : $" [white]→ [red]{NoticeText(target)}";
        string message = $"[red][LPLK Admin] {actorName}[white] · {NoticeText(action)}{targetText}[default]";

        foreach (var recipient in Core.PlayerManager.GetAllPlayers())
        {
            try
            {
                if (CanManage(recipient) && recipient.SteamID != actorId)
                    recipient.SendChat(message);
            }
            catch (Exception error)
            {
                Core.Logger.LogDebug(error, "Adminhinweis an einen Empfänger konnte nicht gesendet werden.");
            }
        }
    }

    // Keep private messages, command arguments and configuration values out of staff chat.
    private static string AdminActionLabel(string action, Dictionary<string, object>? payload) => action switch
    {
        "kick" => "Kick",
        "ban" => "Bann",
        "unban" => "Bann aufgehoben",
        "mute" => "Sprache gesperrt",
        "unmute" => "Sprache freigegeben",
        "gag" => "Text gesperrt",
        "ungag" => "Text freigegeben",
        "silence" => "Sprache und Text gesperrt",
        "unsilence" => "Sprache und Text freigegeben",
        "slay" => "Spieler getötet",
        "respawn" => "Respawn",
        "team" => "Team geändert",
        "freeze" => payload?.GetValueOrDefault("frozen") is false ? "Freigegeben" : "Eingefroren",
        "noclip" => payload?.GetValueOrDefault("enabled") is false ? "Noclip AUS" : "Noclip AN",
        "goto_player" => "Zum Spieler teleportiert",
        "bring_player" => "Spieler herteleportiert",
        "health" => "Gesundheit geändert",
        "armor" => "Rüstung geändert",
        "private_message" => "Privatnachricht gesendet",
        "broadcast" => "Ansage gesendet",
        "round_restart" => "Rundenneustart angefordert",
        "server_command" => "Serverbefehl ausgeführt",
        "cvar_set" => "Servervariable geändert",
        _ => "Adminaktion ausgeführt"
    };
}
