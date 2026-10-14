using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed class PanelException(string message) : Exception(message);
internal sealed record ServerInfo(string Identifier, string Name, string Status, string? Cs2Version, string? Note,
    string? Ip = null, int? Port = null, string? IpAlias = null, string? Node = null);

internal sealed partial class PanelClient : IDisposable
{
    private readonly HttpClient http;
    public PanelClient(PanelConnection connection) : this(connection, new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

    internal PanelClient(PanelConnection connection, HttpMessageHandler handler)
    {
        PanelAccessErrors.ValidateClientKey(connection.ApiKey);
        http = new HttpClient(handler)
        { BaseAddress = new Uri(CredentialStore.ValidateUrl(connection.PanelUrl) + "/"), Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2-Workspace/1.0");
    }

    private async Task<string> GetAsync(string relative)
    {
        using var response = await http.GetAsync(relative, HttpCompletionOption.ResponseHeadersRead);
        using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var stream = await response.Content.ReadAsStreamAsync(readTimeout.Token);
        using var reader = new StreamReader(stream);
        int limit = response.IsSuccessStatusCode ? 4 * 1024 * 1024 : 64 * 1024;
        var buffer = new char[8192];
        var body = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - body.Length)), readTimeout.Token)) > 0)
        {
            body.Append(buffer, 0, count);
            if (body.Length > limit)
            {
                if (!response.IsSuccessStatusCode) throw new PanelException(PanelAccessErrors.Describe(response.StatusCode, "", response.Content.Headers.ContentType?.MediaType));
                throw new PanelException("Die Panelantwort ist unerwartet groß.");
            }
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new PanelException(PanelAccessErrors.Describe(response.StatusCode, body.ToString(), response.Content.Headers.ContentType?.MediaType));
        }
        return body.ToString();
    }

    public async Task<List<ServerInfo>> GetServersAsync(bool details)
    {
        var result = new List<ServerInfo>();
        for (int page = 1; page <= 20; page++)
        {
            using var json = JsonDocument.Parse(await GetAsync($"api/client?per_page=100&page={page}"));
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new PanelException("Die Adresse liefert keine gültige Pterodactyl-Client-API-Antwort.");
            foreach (var entry in data.EnumerateArray())
            {
                var attr = entry.GetProperty("attributes");
                var id = attr.GetProperty("identifier").GetString()!;
                if (!Regex.IsMatch(id, "^[a-zA-Z0-9-]{8,36}$")) throw new PanelException("Ungültige Serverkennung in der Panelantwort.");
                var name = attr.GetProperty("name").GetString() ?? id;
                string status = "Verbunden", note = "";
                string? version = null;
                if (details)
                {
                    try
                    {
                        using var resources = JsonDocument.Parse(await GetAsync($"api/client/servers/{id}/resources"));
                        status = resources.RootElement.GetProperty("attributes").GetProperty("current_state").GetString() ?? "Unbekannt";
                    }
                    catch (Exception ex) { note = SafeError.Message(ex); }
                    foreach (var path in new[] { "/game/csgo/steam.inf", "/csgo/steam.inf" })
                    {
                        try { version = ParseCs2Version(await GetAsync($"api/client/servers/{id}/files/contents?file={Uri.EscapeDataString(path)}")); }
                        catch (PanelException) { }
                        if (version != null) break;
                    }
                }
                var (ip, port, alias) = ParseAllocation(attr);
                result.Add(new(id, name, status, version, note.Length == 0 ? null : note, ip, port, alias,
                    attr.TryGetProperty("node", out var node) && node.ValueKind == JsonValueKind.String ? node.GetString() : null));
            }
            if (!json.RootElement.TryGetProperty("meta", out var meta) || !meta.TryGetProperty("pagination", out var pagination) ||
                !pagination.TryGetProperty("total_pages", out var pages) || page >= pages.GetInt32()) return result;
        }
        throw new PanelException("Das Panel hat mehr als 20 Ergebnisseiten. Bitte den Zugriff auf die benötigten Server begrenzen.");
    }

    internal static (string? Ip, int? Port, string? Alias) ParseAllocation(JsonElement attributes)
    {
        if (attributes.TryGetProperty("relationships", out var relationships) &&
            relationships.TryGetProperty("allocations", out var allocations) &&
            allocations.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var entry in data.EnumerateArray())
            {
                var a = entry.GetProperty("attributes");
                if (!a.TryGetProperty("is_default", out var primary) || primary.ValueKind != JsonValueKind.True) continue;
                string? Field(string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                int? port = a.TryGetProperty("port", out var p) && p.TryGetInt32(out int number) && number is > 0 and <= 65535 ? number : null;
                return (Field("ip"), port, Field("ip_alias"));
            }
        return (null, null, null);
    }

    public static string? ParseCs2Version(string text)
    {
        var server = Regex.Match(text, @"(?m)^\s*ServerVersion\s*=\s*(\d+)\s*$");
        if (server.Success) return server.Groups[1].Value;
        var patch = Regex.Match(text, @"(?m)^\s*PatchVersion\s*=\s*([\d.]+)\s*$");
        return patch.Success ? patch.Groups[1].Value.Replace(".", "") : null;
    }
    public void Dispose() => http.Dispose();
}
