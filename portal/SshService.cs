using Renci.SshNet;
using Renci.SshNet.Common;

namespace CS2ServerPortal;

internal sealed class SshHostKeyException() : Exception("Die SSH-Serverkennung stimmt nicht mit der gespeicherten Kennung überein. Verbindung vor der Anmeldung abgebrochen. Bitte den Serverwechsel oder die Neuinstallation zuerst prüfen.");
internal sealed record SshHostKey(string Algorithm, string Sha256);
internal sealed record SshCommandResult(string Section, int? ExitCode, string Output, string Error);
internal sealed record SshDiagnosticReport(DateTimeOffset CheckedAt, string Host, int Port, string Username, List<SshCommandResult> Results);

internal static partial class SshService
{
    private const string ConnectionCommand = "printf 'Benutzer: '; id -un; printf 'UID: '; id -u; printf 'Hostname: '; hostname";
    private static readonly (string Section, string Command)[] Diagnostics =
    [
        ("System", "uname -sr; if [ -r /etc/os-release ]; then cat /etc/os-release; fi; uptime"),
        ("Arbeitsspeicher und Datenträger", "free -m; df -h -x tmpfs -x devtmpfs"),
        ("Dienste", "systemctl show ssh.service wings.service docker.service nginx.service mariadb.service redis-server.service --property=Id,LoadState,ActiveState,SubState --no-pager; systemctl --failed --no-legend --no-pager"),
        ("Container", "if command -v docker >/dev/null 2>&1; then docker ps --format '{{.Names}} | {{.Status}} | {{.Image}}'; else printf 'Docker ist nicht installiert.\\n'; fi")
    ];

    public static bool HostKeyMatches(string expected, string actual) =>
        !string.IsNullOrWhiteSpace(expected) && string.Equals(expected, actual, StringComparison.Ordinal);

    public static SshHostKey InspectHostKey(string host, int port)
    {
        host = SshCredentialStore.ValidateHost(host);
        if (port is < 1 or > 65535) throw new ArgumentException("Ungültiger SSH-Port.");
        using var authentication = new NoneAuthenticationMethod("root");
        var info = new ConnectionInfo(host, port, "root", authentication) { Timeout = TimeSpan.FromSeconds(20) };
        using var client = new SshClient(info);
        SshHostKey? observed = null;
        client.HostKeyReceived += (_, e) =>
        {
            observed = new(e.HostKeyName, "SHA256:" + e.FingerPrintSHA256);
            e.CanTrust = false; // Inspect the server key without sending credentials.
        };
        try { client.Connect(); }
        catch (SshConnectionException) when (observed != null) { }
        return observed ?? throw new InvalidOperationException("Der Server hat keine SSH-Serverkennung geliefert.");
    }

    private static SshClient Connect(SshConnection connection)
    {
        SshCredentialStore.Validate(connection);
        var info = new PasswordConnectionInfo(connection.Host, connection.Port, connection.Username, connection.Password)
        { Timeout = TimeSpan.FromSeconds(20) };
        var client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(15) };
        bool hostKeyRejected = false;
        client.HostKeyReceived += (_, e) =>
        {
            e.CanTrust = HostKeyMatches(connection.HostKeySha256, "SHA256:" + e.FingerPrintSHA256);
            hostKeyRejected |= !e.CanTrust;
        };
        try { client.Connect(); return client; }
        catch
        {
            client.Dispose();
            if (hostKeyRejected) throw new SshHostKeyException();
            throw;
        }
    }

    public static SshCommandResult TestConnection(SshConnection connection)
    {
        using var client = Connect(connection);
        var result = Run(client, connection, "SSH-Verbindung", ConnectionCommand);
        if (result.ExitCode != 0) throw new InvalidOperationException("Die SSH-Anmeldung war möglich, aber die Benutzerabfrage ist fehlgeschlagen.");
        return result;
    }

    public static SshDiagnosticReport Diagnose(SshConnection connection)
    {
        using var client = Connect(connection);
        var results = new List<SshCommandResult> { Run(client, connection, "SSH-Verbindung", ConnectionCommand) };
        foreach (var item in Diagnostics) results.Add(Run(client, connection, item.Section, item.Command));
        return new(DateTimeOffset.UtcNow, connection.Host, connection.Port, connection.Username, results);
    }

    public static SshCommandResult DiagnosePanelAccess(SshConnection connection)
    {
        using var source = typeof(SshService).Assembly.GetManifestResourceStream("CS2ServerPortal.Diagnostics.PanelAccess.php")
            ?? throw new InvalidOperationException("Die Panel-Zugriffsdiagnose fehlt im Programm.");
        using var reader = new StreamReader(source);
        string script = reader.ReadToEnd().Replace("\r\n", "\n");
        // Known installation directories only; no credential or user input is interpolated.
        string command = "panel_path=''; for candidate in /var/www/pterodactyl /var/www/panel /srv/pterodactyl; do if [ -f \"$candidate/artisan\" ] && [ -f \"$candidate/bootstrap/app.php\" ]; then panel_path=\"$candidate\"; break; fi; done; " +
            "if [ -z \"$panel_path\" ]; then printf 'Panelinstallation an den bekannten Pfaden nicht gefunden.\\n'; exit 2; fi; " +
            "cd \"$panel_path\" || exit 2; php -d display_errors=0 -d log_errors=0 <<'CS2_PANEL_ACCESS_DIAGNOSTIC'\n" + script + "\nCS2_PANEL_ACCESS_DIAGNOSTIC\n";
        using var client = Connect(connection);
        return Run(client, connection, "Panel-Zugriffsdiagnose (nur lesend)", command);
    }

    private static SshCommandResult Run(SshClient client, SshConnection connection, string section, string command, int timeoutSeconds = 20, int outputLimit = 20000)
    {
        // Fixed commands only. No command, path, or shell fragment comes from input.
        using var remote = client.CreateCommand("LC_ALL=C PATH=/usr/sbin:/usr/bin:/sbin:/bin; export LC_ALL PATH; " + command);
        remote.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var output = remote.Execute();
        return new(section, remote.ExitStatus, CleanOutput(output, connection.Password,outputLimit), CleanOutput(remote.Error, connection.Password));
    }

    internal static string CleanOutput(string value, string password, int limit = 20000)
    {
        if(limit is <1 or >32000)throw new ArgumentOutOfRangeException(nameof(limit));
        if (password.Length > 0) value = value.Replace(password, "[ausgeblendet]", StringComparison.Ordinal);
        value = new string(value.Where(c => c is '\r' or '\n' or '\t' || !char.IsControl(c)).ToArray());
        return value.Length <= limit ? value : value[..limit] + "\n[Ausgabe gekürzt]";
    }
}
