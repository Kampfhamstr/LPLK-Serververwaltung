using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record GitHubConnection(string Token, long? AccountId = null, string? Login = null)
{
    public override string ToString() => "GitHubConnection { credentials hidden }";
}

internal sealed record GitHubRepository(string Owner, string Name)
{
    public string FullName => Owner + "/" + Name;
    public string Url => "https://github.com/" + FullName;
}

internal sealed class GitHubCredentialStore(string workspace)
{
    private sealed record Envelope(int Version, string ProtectedConnection);
    private sealed record LegacyConnection(string RepositoryUrl, string Token);
    private string StorePath => Path.Combine(workspace, "private", "github-connection.json");
    // Retain the original entropy so existing v1 credentials remain readable.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LPLK/GitHub/private-repository/v1");

    public static GitHubRepository ParseRepository(string text, bool stripGitSuffix = true)
    {
        // Match the original text, before URI normalization can conceal traversal or encoded paths.
        var match = Regex.Match(text.Trim(), @"\Ahttps://github\.com/([A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)/([A-Za-z0-9_.-]{1,100})/?\z", RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups[1].Value.Contains("--"))
            throw new ArgumentException("Bitte die Repository-Adresse als https://github.com/NAME/REPOSITORY eingeben, ohne Token oder Unterseite.");
        string name = match.Groups[2].Value;
        if (stripGitSuffix && name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0 || name is "." or "..") throw new ArgumentException("Der Repository-Name ist ungültig.");
        return new(match.Groups[1].Value, name);
    }

    public static void ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || !Regex.IsMatch(token, @"\A(?:github_pat_|ghp_)[A-Za-z0-9_]{30,1000}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Bitte einen GitHub Personal Access Token eintragen: klassisch (ghp_…) oder Fine-grained (github_pat_…). Ein GitHub-Passwort ist hier nicht geeignet.");
    }

    public static string TokenKind(string token) { ValidateToken(token); return token.StartsWith("ghp_", StringComparison.Ordinal) ? "Classic" : "Fine-grained"; }

    static void ValidateAccount(GitHubConnection connection)
    {
        ValidateToken(connection.Token);
        if (connection.AccountId == null && connection.Login == null) return;
        if (connection.AccountId is not > 0 || connection.Login == null || !Regex.IsMatch(connection.Login, @"\A[A-Za-z0-9-]{1,39}\z"))
            throw new InvalidOperationException("Die gespeicherte GitHub-Kontozuordnung ist ungültig.");
    }

    public GitHubConnection? Read()
    {
        if (!File.Exists(StorePath)) return null;
        if (new FileInfo(StorePath).Length > 32768) throw new InvalidOperationException("Die gespeicherte GitHub-Verbindung ist ungültig.");
        var saved = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(StorePath), Program.JsonOptions);
        if (saved?.Version is not (1 or 2)) throw new InvalidOperationException("Die gespeicherte GitHub-Verbindung ist ungültig.");
        var plain = ProtectedData.Unprotect(Convert.FromBase64String(saved.ProtectedConnection), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            GitHubConnection connection;
            if (saved.Version == 1)
            {
                var old = JsonSerializer.Deserialize<LegacyConnection>(plain, Program.JsonOptions)
                    ?? throw new InvalidOperationException("Die gespeicherte GitHub-Verbindung ist unvollständig.");
                connection = new(old.Token);
            }
            else connection = JsonSerializer.Deserialize<GitHubConnection>(plain, Program.JsonOptions)
                ?? throw new InvalidOperationException("Die gespeicherte GitHub-Verbindung ist unvollständig.");
            ValidateAccount(connection);
            return connection;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(GitHubConnection connection)
    {
        ValidateAccount(connection);
        var plain = JsonSerializer.SerializeToUtf8Bytes(connection, Program.JsonOptions);
        byte[] cipher;
        try { cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        string temp = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Envelope(2, Convert.ToBase64String(cipher)), Program.JsonOptions));
            File.Move(temp, StorePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
