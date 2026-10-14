using System.Text.Json;

namespace CS2ServerPortal;

// Fixed read-only reference and explicit non-secret fields. Never export entire server configs.
internal static class ReferenceGameplay
{
    internal static async Task ReadChatGuardAsync(PanelClient client,string workspace)
    {
        const string root="/game/csgo/addons/swiftlys2/data/serververwaltung_antiadvertisement/";
        using var config=JsonDocument.Parse(await client.ReadFileAsync("11110003",root+"config.json"));
        using var global=JsonDocument.Parse(await client.ReadFileAsync("11110003",root+"global_protection.json"));
        string[] fields=["enabled","check_player_names","mask_advertising_player_names","censor_advertising_system_messages","suppress_advertising_disconnect_messages","suppress_advertising_server_messages","hard_actions_only_for_player_role","elevated_role_warning_cooldown_seconds","censored_player_name","broadcast_success","broadcast_offender_name","log_blocked_text","repeat_window_minutes","gag_at_violation","gag_minutes","kick_at_violation","kick_reason","blocked_domains","allowed_domains","marketing_keywords"];
        string[] ruleFields=["version","domains","allowed_domains","blocked_phrases","marketing_keywords"];
        var report=new{sourceServer="11110003",observedAt=DateTimeOffset.UtcNow,
            config=config.RootElement.EnumerateObject().Where(p=>fields.Contains(p.Name)).ToDictionary(p=>p.Name,p=>p.Value.Clone()),
            rules=global.RootElement.EnumerateObject().Where(p=>ruleFields.Contains(p.Name)).ToDictionary(p=>p.Name,p=>p.Value.Clone()),
            blockedIdentitiesCopied=false};
        string path=Path.Combine(workspace,"state","reference-chatguard-rules.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(report,Program.JsonOptions));File.Move(path+".tmp",path,true);
    }
    internal static async Task SaveAsync(PanelClient client, string workspace)
    {
        const string root = "/game/csgo/addons/swiftlys2/configs/plugins/";
        const string server = "11110003";
        var result = new Dictionary<string, object> { ["sourceServer"] = server, ["observedAt"] = DateTimeOffset.UtcNow };
        var allowed = new HashSet<string>(["fallspeed", "linear", "model", "decrease", "adminflag", "disablewhencarryinghostage"]);
        string toml = await client.ReadFileAsync(server, root + "Parachute/config.toml");
        result["parachute"] = toml.Split('\n').Select(l => l.Trim()).Where(l => l.Contains('='))
            .Select(l => l.Split('=', 2)).Where(p => allowed.Contains(p[0].Trim().Replace("_", "").ToLowerInvariant())).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (((Dictionary<string, string>)result["parachute"]).Count == 0)
            throw new InvalidOperationException("Fallschirm-Konfigurationsformat unbekannt; Migration abgebrochen.");
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var (key, path, section, fields) in new[] {
            ("antiCamp", "SLAYER_AntiCamp/config.jsonc", "AntiCamp", new[] { "PluginEnabled", "CampTime", "WarningSeconds", "SlapORSlayPunishment", "SlapDamage", "MinHealthReserve", "FirstPunishDelay", "PunishFrequency", "CampCheckRadius", "PunishBeacon", "PunishAnyway", "AllowTCampHostageMap", "AllowTCampBombPlanted", "AllowCTCampBombMap", "AllowCtCampBombDropped", "DisableChatMessages" }),
            ("bombWires", "k4.bombwires/config.json", "K4BombWires", new[] { "MenuTimeout", "RandomizeWireOrder" }),
            ("whitelist", "Whitelist/config.jsonc", "Whitelist", new[] { "Mode", "Enabled", "AddCommand", "RemoveCommand", "PermissionForCommands", "KickMessage" }),
            ("mapVote", "headshotonly/config.jsonc", "HeadshotOnly", new[] { "MapVote" }) })
        {
            using var document = JsonDocument.Parse(await client.ReadFileAsync(server, root + path), options);
            var node = document.RootElement.GetProperty(section);
            result[key] = node.EnumerateObject().Where(p => fields.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        using (var addons = JsonDocument.Parse(await client.ReadFileAsync(server, root + "AddonsManager/config.jsonc"), options))
            result["assetWorkshopIds"] = addons.RootElement.GetProperty("Main").GetProperty("Addons").EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                .Where(v => v is not null && v.All(char.IsAsciiDigit) && ulong.TryParse(v, out _)).ToArray();
        string output = Path.Combine(workspace, "state", "reference-gameplay-rules.json");
        File.WriteAllText(output + ".tmp", JsonSerializer.Serialize(result, Program.JsonOptions));
        File.Move(output + ".tmp", output, true);
    }
}
