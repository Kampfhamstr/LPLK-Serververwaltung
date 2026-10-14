using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Renci.SshNet;

namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult InnerCircleRecovery(SshConnection connection, string phase, string? package = null)
    {
        if (phase is not ("check" or "prepare" or "apply" or "start-check" or "rollback")) throw new ArgumentException("Recovery phase");
        if (phase == "prepare")
        {
            using var file = File.OpenRead(package ?? throw new ArgumentException("Package missing"));
            if (file.Length != 58966967 || Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != "d3b9a459f5a5dc984348b6033882f05c07ba62c90b4caa1cdcaa85ac46a865cc")
                throw new InvalidOperationException("Geprüftes SwiftlyS2-1.4.14-Paket erforderlich.");
        }
        using var client = Connect(connection);
        var result = Run(client, connection, "Inner Circle: Framework-Rückstellung / " + phase,
            PanelScript("InnerCircleRecovery.php").Replace("__LPLK_RECOVERY_PHASE__", phase), 60);
        if (phase != "prepare" || result.ExitCode != 0) return result;
        const string stage = "/var/lib/pterodactyl/volumes/11110003-0000-4000-8000-000000000003/.lplk-framework-recovery";
        using var document = JsonDocument.Parse(result.Output);
        if (document.RootElement.GetProperty("stage").GetString() != stage) throw new InvalidOperationException("Stage mismatch");
        var info = new PasswordConnectionInfo(connection.Host, connection.Port, connection.Username, connection.Password) { Timeout = TimeSpan.FromSeconds(20) };
        using var sftp = new SftpClient(info);
        sftp.HostKeyReceived += (_, e) => e.CanTrust = HostKeyMatches(connection.HostKeySha256, "SHA256:" + e.FingerPrintSHA256);
        sftp.Connect();
        if (sftp.Exists(stage + "/package.zip") || sftp.Exists(stage + "/package.zip.incoming")) throw new InvalidOperationException("Stage occupied");
        using var source = File.OpenRead(package!);
        sftp.UploadFile(source, stage + "/package.zip.incoming", false);
        sftp.RenameFile(stage + "/package.zip.incoming", stage + "/package.zip");
        return result;
    }
}

internal sealed partial class PanelClient
{
    public async Task InnerCircleRecoveryPowerAsync(string signal)
    {
        if (signal is not ("start" or "stop")) throw new ArgumentException("Power signal");
        using var current = JsonDocument.Parse(await GetAsync("api/client/servers/11110003"));
        var a = current.RootElement.GetProperty("attributes"); var (ip, port, _) = ParseAllocation(a);
        if (a.GetProperty("uuid").GetString() != "11110003-0000-4000-8000-000000000003" || ip != "203.0.113.10" || port != 27019)
            throw new InvalidOperationException("Produktionsidentität verändert.");
        using var response = await http.PostAsJsonAsync("api/client/servers/11110003/power", new { signal });
        if (!response.IsSuccessStatusCode) throw new PanelException("Frameworkwartung: Power-API HTTP " + (int)response.StatusCode);
    }
}
