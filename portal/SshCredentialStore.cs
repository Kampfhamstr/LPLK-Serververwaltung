using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record SshConnection(string Host, int Port, string Username, string Password, string HostKeySha256);
internal sealed record StoredSshConnection(int Version, string ProtectedConnection);

internal sealed class SshCredentialStore(string workspace)
{
    private string StorePath => Path.Combine(workspace, "private", "ssh-connection.json");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2-Workspace/Debian/SSH/v1");

    public static string ValidateHost(string text)
    {
        var host = text.Trim();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        if (host.Length == 0 || host.Length > 253 || host.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('/') || host.Contains('@'))
            throw new ArgumentException("Bitte nur den Hostnamen oder die IP-Adresse eingeben, ohne ssh://, Benutzer oder Port.");
        return host;
    }

    public static void Validate(SshConnection connection, bool requireHostKey = true)
    {
        ValidateHost(connection.Host);
        if (connection.Port is < 1 or > 65535) throw new ArgumentException("Der SSH-Port muss zwischen 1 und 65535 liegen.");
        if (!Regex.IsMatch(connection.Username, @"^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}$"))
            throw new ArgumentException("Bitte einen gültigen SSH-Benutzernamen eingeben.");
        if (string.IsNullOrEmpty(connection.Password) || connection.Password.Length > 4096 || connection.Password.Contains('\0'))
            throw new ArgumentException("Bitte das SSH-Passwort eingeben. Bei unveränderter gespeicherter Verbindung darf das Feld leer bleiben.");
        if (requireHostKey && !Regex.IsMatch(connection.HostKeySha256, @"^SHA256:[a-zA-Z0-9+/]{43}$"))
            throw new ArgumentException("Die SSH-Serverkennung wurde noch nicht bestätigt.");
    }

    public SshConnection? Read()
    {
        if (!File.Exists(StorePath)) return null;
        var stored = JsonSerializer.Deserialize<StoredSshConnection>(File.ReadAllText(StorePath), Program.JsonOptions)
            ?? throw new InvalidOperationException("Die gespeicherte SSH-Verbindung ist unvollständig.");
        if (stored.Version != 1) throw new InvalidOperationException("Das Format der SSH-Verbindung wird nicht unterstützt.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedConnection), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var connection = JsonSerializer.Deserialize<SshConnection>(bytes, Program.JsonOptions)
                ?? throw new InvalidOperationException("Die gespeicherte SSH-Verbindung ist unvollständig.");
            Validate(connection);
            return connection;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(SshConnection connection)
    {
        Validate(connection);
        // Protect the endpoint and host key together with the password so a changed
        // plaintext hostname cannot redirect stored root credentials to another host.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(connection, Program.JsonOptions);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporary = StorePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredSshConnection(1, Convert.ToBase64String(encrypted)), Program.JsonOptions));
        File.Move(temporary, StorePath, true);
    }
}
