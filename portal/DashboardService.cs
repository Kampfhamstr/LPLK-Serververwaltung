namespace CS2ServerPortal;

internal static class DashboardService
{
    internal static async Task<DashboardState> RefreshAsync(string workspace, Action<string>? progress = null)
    {
        var store = new DashboardStore(workspace);
        var connection = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte unter Verbindung & Versionen die Panel-Verbindung speichern.");
        using var client = new PanelClient(connection);
        try
        {
            progress?.Invoke("Server, IP-Adressen und Ports lesen …");
            var servers = await client.GetServersAsync(true);
            var previous = store.Read();
            var report = new AddonRuntimeReport(DateTimeOffset.UtcNow, "Dashboard: lesende Panel- und Bridge-Abfrage", []);
            foreach (var server in servers)
            {
                if (server.Cs2Version == null && !previous.Servers.Any(s => s.Info.Identifier == server.Identifier && s.SnapshotAt != null)) continue;
                progress?.Invoke($"Pluginbestand lesen: {server.Name}");
                report.Servers.Add(await ReadRuntime(client, server.Identifier));
            }
            return store.Update(state => DashboardChanges.Apply(state, servers, report, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // A failed complete query never changes the baseline used for comparisons.
            try { store.Update(state => state.RefreshError = SafeError.Message(ex)); } catch (Exception) { /* Preserve the original error. */ }
            throw;
        }
    }

    internal static async Task<ServerAddonRuntime> ReadRuntime(PanelClient client, string server)
    {
        string? error = null;
        foreach (var addons in new[] { "/game/csgo/addons", "/csgo/addons" })
        {
            try
            {
                var root = await client.ListFilesAsync(server, addons);
                if (!root.Any(f => f.Name == "swiftlys2" && !f.IsFile && !f.IsSymlink)) continue;
                string path = addons + "/swiftlys2";
                foreach (string part in new[] { "data", "serververwaltung_bridge" })
                {
                    var entries = await client.ListFilesAsync(server, path);
                    if (!entries.Any(f => f.Name == part && !f.IsFile && !f.IsSymlink)) throw new PanelException("Bridge-Datenverzeichnis fehlt oder ist eine Verknüpfung.");
                    path += "/" + part;
                }
                var files = await client.ListFilesAsync(server, path);
                if (!files.Any(f => f.Name == "status.json" && f.IsFile && !f.IsSymlink && f.Size <= 4 * 1024 * 1024))
                    throw new PanelException("Bridge-Status fehlt, ist zu groß oder eine Verknüpfung.");
                return AddonRuntime.Parse(server, await client.ReadFileAsync(server, path + "/status.json"), DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is PanelException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            { error = SafeError.Message(ex); }
        }
        return new(server, null, false, [], error ?? "Kein SwiftlyS2-Bridge-Status gefunden.");
    }
}
