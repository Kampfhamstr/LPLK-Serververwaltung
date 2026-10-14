using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CS2ServerPortal;

internal sealed record PanelConnection(string PanelUrl, string ApiKey);
internal sealed record StoredConnection(int Version, string PanelUrl, string ProtectedKey);

internal sealed class CredentialStore(string workspace)
{
    private string StorePath => Path.Combine(workspace, "private", "panel-connection.json");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2-Workspace/Pterodactyl/client/v1");

    public static string ValidateUrl(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
            throw new ArgumentException("Bitte die HTTPS-Basisadresse des Panels eingeben, z. B. https://panel.example.de (ohne Unterseite oder Anmeldedaten).");
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public PanelConnection? Read()
    {
        if (!File.Exists(StorePath)) return null;
        var saved = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(StorePath), Program.JsonOptions)
            ?? throw new InvalidOperationException("Gespeicherte Verbindung ist unvollständig.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(saved.ProtectedKey), Entropy, DataProtectionScope.CurrentUser);
        try { return new(ValidateUrl(saved.PanelUrl), Encoding.UTF8.GetString(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(PanelConnection connection)
    {
        var url = ValidateUrl(connection.PanelUrl);
        PanelAccessErrors.ValidateClientKey(connection.ApiKey);
        var bytes = Encoding.UTF8.GetBytes(connection.ApiKey);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new StoredConnection(1, url, Convert.ToBase64String(encrypted)), Program.JsonOptions));
        File.Move(temp, StorePath, true);
    }

    public static void SelfTest()
    {
        var testBytes = Encoding.UTF8.GetBytes("LOCAL-TEST-NOT-A-REAL-KEY");
        var cipher = ProtectedData.Protect(testBytes, Entropy, DataProtectionScope.CurrentUser);
        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        if (!plain.SequenceEqual(testBytes) || cipher.SequenceEqual(testBytes)) throw new Exception("DPAPI test failed.");
        foreach (var invalid in new[] { "http://example.org", "https://key@example.org", "https://example.org/?key=x", "https://example.org/account" })
        {
            bool rejected = false;
            try { ValidateUrl(invalid); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Unsafe panel URL accepted.");
        }
    }
}
