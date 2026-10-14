using System.Text.Json;

namespace CS2ServerPortal;

internal sealed class UpdateReport
{
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? SwiftlyStable { get; set; }
    public string? SwiftlyReleaseUrl { get; set; }
    public string? SwiftlyApiStable { get; set; }
    public string? TemplateStable { get; set; }
    public string? Cs2RequiredServerVersion { get; set; }
    public string? DotnetLatestSdk { get; set; }
    public string? DotnetInstalledSdk { get; set; }
    public bool PanelConfigured { get; set; }
    public List<ServerInfo> Servers { get; set; } = [];
    public string SwiftlyInstalledVersionNote { get; set; } = "Die installierte SwiftlyS2-Version wird über die Panel-API noch nicht automatisch ermittelt. Vor einem Update in der Serverkonsole prüfen.";
    public List<string> Errors { get; set; } = [];
}

internal static class UpdateCheck
{
    public static async Task<UpdateReport> RunAsync(string workspace)
    {
        var report = new UpdateReport();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2-Workspace/1.0");
        async Task Source(string name, Func<Task> action) { try { await action(); } catch (Exception ex) { lock (report.Errors) report.Errors.Add(name + ": " + SafeError.Message(ex)); } }
        await Task.WhenAll(
            Source("SwiftlyS2", async () => {
                using var j = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/swiftly-solution/swiftlys2/releases/latest"));
                report.SwiftlyStable = j.RootElement.GetProperty("tag_name").GetString();
                report.SwiftlyReleaseUrl = j.RootElement.GetProperty("html_url").GetString();
            }),
            Source("SwiftlyS2 API", async () => report.SwiftlyApiStable = await LatestPackage(http, "swiftlys2.cs2")),
            Source("Pluginvorlage", async () => report.TemplateStable = await LatestPackage(http, "swiftlys2.cs2.plugintemplate")),
            Source("CS2", async () => {
                using var j = JsonDocument.Parse(await http.GetStringAsync("https://api.steampowered.com/ISteamApps/UpToDateCheck/v1/?appid=730&version=0"));
                var response = j.RootElement.GetProperty("response");
                if (!response.GetProperty("success").GetBoolean()) throw new InvalidOperationException("Steam hat keine Versionsauskunft geliefert.");
                report.Cs2RequiredServerVersion = response.GetProperty("required_version").ToString();
            }),
            Source(".NET", async () => {
                using var j = JsonDocument.Parse(await http.GetStringAsync("https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json"));
                report.DotnetLatestSdk = j.RootElement.GetProperty("latest-sdk").GetString();
            })
        );
        var toolchain = Path.Combine(workspace, "tools", "toolchain.json");
        if (File.Exists(toolchain))
        {
            using var j = JsonDocument.Parse(File.ReadAllText(toolchain));
            report.DotnetInstalledSdk = j.RootElement.GetProperty("dotnetVersion").GetString();
        }
        await Source("Pterodactyl", async () => {
            var connection = new CredentialStore(workspace).Read();
            report.PanelConfigured = connection != null;
            if (connection != null) { using var client = new PanelClient(connection); report.Servers = await client.GetServersAsync(true); }
        });
        var state = Path.Combine(workspace, "state");
        Directory.CreateDirectory(state);
        var current = Path.Combine(state, "latest-check.json");
        var baseline = Path.Combine(state, "baseline-check.json");
        string json = JsonSerializer.Serialize(report, Program.JsonOptions);
        File.WriteAllText(current, json);
        if (!File.Exists(baseline) && report.Errors.Count == 0) File.WriteAllText(baseline, json);
        return report;
    }

    private static async Task<string?> LatestPackage(HttpClient http, string id)
    {
        using var j = JsonDocument.Parse(await http.GetStringAsync($"https://api.nuget.org/v3-flatcontainer/{id}/index.json"));
        return j.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!)
            .Where(v => !v.Contains('-')).OrderBy(v => Version.Parse(v)).LastOrDefault();
    }
}
