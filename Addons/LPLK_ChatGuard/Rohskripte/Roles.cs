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
    private bool IsElevatedRoleProtected(ulong steamId, out string role)
    {
        LoadGameRoles();
        role = _gameRolesBySteamId.TryGetValue(steamId, out string? assignedRole)
            ? ChatGuardPolicy.NormalizeGameRole(assignedRole)
            : "spieler";

        return _config.HardActionsOnlyForPlayerRole && ChatGuardPolicy.GameRoleLevel(role) > ChatGuardPolicy.GameRoleLevel("spieler");
    }

    private void LoadGameRoles(bool force = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_permissionsPath) || !File.Exists(_permissionsPath))
            {
                _gameRolesBySteamId.Clear();
                _permissionsLastWriteUtc = DateTime.MinValue;
                return;
            }

            DateTime stamp = File.GetLastWriteTimeUtc(_permissionsPath);
            if (!force && stamp == _permissionsLastWriteUtc)
                return;

            var nextRoles = new Dictionary<ulong, string>();
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(_permissionsPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            JsonElement root = document.RootElement;
            if (root.TryGetProperty("Permissions", out JsonElement permissions)
                && permissions.ValueKind == JsonValueKind.Object
                && permissions.TryGetProperty("Players", out JsonElement players)
                && players.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty playerProperty in players.EnumerateObject())
                {
                    if (!ulong.TryParse(playerProperty.Name, out ulong steamId) || steamId == 0)
                        continue;
                    if (playerProperty.Value.ValueKind != JsonValueKind.Array)
                        continue;

                    string role = ChatGuardPolicy.ResolveHighestGameRole(
                        playerProperty.Value
                            .EnumerateArray()
                            .Where(group => group.ValueKind == JsonValueKind.String)
                            .Select(group => group.GetString() ?? string.Empty));
                    nextRoles[steamId] = role;
                }
            }

            _gameRolesBySteamId.Clear();
            foreach (var pair in nextRoles)
                _gameRolesBySteamId[pair.Key] = pair.Value;
            _permissionsLastWriteUtc = stamp;
            _nameChecked.Clear();

            Core.Logger.LogInformation(
                "AntiAdvertisement Rollen aktualisiert: {Count} Zuweisungen aus permissions.jsonc.",
                _gameRolesBySteamId.Count);
        }
        catch (Exception ex)
        {
            // Bei einer kurzzeitig unvollstaendigen Datei bleibt der letzte gueltige
            // Rollenstand aktiv. Unbekannte SteamIDs werden weiterhin als Spieler gewertet.
            Core.Logger.LogWarning(ex, "AntiAdvertisement Rollen konnten nicht aus permissions.jsonc geladen werden.");
        }
    }

}
