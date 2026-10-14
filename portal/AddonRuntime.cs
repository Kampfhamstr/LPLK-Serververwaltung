using System.Text.Json;

namespace CS2ServerPortal;

internal sealed record RuntimeAddon(string Id, string Name, string Version, string MinimumApiVersion, string Status, string Path);
internal sealed record ServerAddonRuntime(string Server, DateTimeOffset? SnapshotAt, bool FreshAtRead, List<RuntimeAddon> Plugins, string? Error);
internal sealed record AddonRuntimeReport(DateTimeOffset CheckedAt, string Evidence, List<ServerAddonRuntime> Servers);

internal static class AddonRuntime
{
    internal static ServerAddonRuntime Parse(string server, string body, DateTimeOffset now)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var updated = root.TryGetProperty("updated_at", out var date) && date.TryGetDateTimeOffset(out var value) ? value : (DateTimeOffset?)null;
        var plugins = new List<RuntimeAddon>();
        foreach (var p in root.GetProperty("plugins").EnumerateArray())
        {
            string Field(string name) => p.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
            plugins.Add(new(Field("id"), Field("name"), Field("version"), Field("minimum_api_version"), Field("status"), Field("path")));
        }
        return new(server, updated, updated != null && now - updated >= TimeSpan.Zero && now - updated <= TimeSpan.FromMinutes(2), plugins, null);
    }

    public static async Task<AddonRuntimeReport> CollectAsync(PanelClient client, AddonInventoryReport inventory)
    {
        var report = new AddonRuntimeReport(DateTimeOffset.UtcNow,
            "Read-only plugin list from existing Bridge status.json. Freshness is checked at read time. Self-reported runtime snapshot, not an in-game functional test. Player, chat and sanction data are not exported.", []);
        foreach (var server in inventory.Servers.Where(s => s.Server.Cs2Version != null))
        {
            string? data = server.Files.FirstOrDefault(f => !f.IsFile && !f.IsSymlink && f.Path.EndsWith("/addons/swiftlys2/data"))?.Path;
            try
            {
                if (data == null) throw new PanelException("Kein SwiftlyS2-Datenpfad im Inventar.");
                var dirs = await client.ListFilesAsync(server.Server.Identifier, data);
                if (!dirs.Any(f => f.Name == "serververwaltung_bridge" && !f.IsFile && !f.IsSymlink))
                    throw new PanelException("Kein reguläres Bridge-Datenverzeichnis.");
                string path = data + "/serververwaltung_bridge";
                var files = await client.ListFilesAsync(server.Server.Identifier, path);
                if (!files.Any(f => f.Name == "status.json" && f.IsFile && !f.IsSymlink && f.Size <= 4 * 1024 * 1024))
                    throw new PanelException("Kein lesbarer Bridge-Status.");
                report.Servers.Add(Parse(server.Server.Identifier, await client.ReadFileAsync(server.Server.Identifier, path + "/status.json"), DateTimeOffset.UtcNow));
            }
            catch (Exception ex) when (ex is PanelException or HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException)
            { report.Servers.Add(new(server.Server.Identifier, null, false, [], SafeError.Message(ex))); }
            await Task.Delay(200);
        }
        return report;
    }
}
