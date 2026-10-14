using System.Net.Http.Json;
using System.Text.Json;

namespace CS2ServerPortal;

internal sealed partial class PanelClient
{
    internal const string TestServerId = "11110004";
    internal const string TestServerUuid = "11110004-0000-4000-8000-000000000004";

    public async Task TestServerPowerAsync(string signal)
    {
        if (signal is not ("start" or "stop")) throw new ArgumentException("Nur Testserver-Start oder -Stopp erlaubt.");
        using var current = JsonDocument.Parse(await GetAsync($"api/client/servers/{TestServerId}"));
        var a = current.RootElement.GetProperty("attributes");
        var (ip, port, _) = ParseAllocation(a);
        if (a.GetProperty("uuid").GetString() != TestServerUuid || ip != "203.0.113.10" || port != 27020)
            throw new InvalidOperationException("Die Testserver-Identität stimmt nicht überein.");
        using var response = await http.PostAsJsonAsync($"api/client/servers/{TestServerId}/power", new { signal });
        if (!response.IsSuccessStatusCode) throw new PanelException("Testserver-Power-API: HTTP " + (int)response.StatusCode);
    }
}
