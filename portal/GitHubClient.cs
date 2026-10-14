using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record GitHubRepositoryInfo(long Id, string FullName, string Url, string Visibility,
    bool Archived, bool Disabled, bool? AccountCanPush, bool? AccountCanAdmin, bool? AccountCanRead);
internal sealed record GitHubCheck(string Login, long AccountId, string TokenKind,
    IReadOnlyList<GitHubRepositoryInfo> Repositories, bool ListingComplete, bool TokenWriteVerified,
    IReadOnlyList<string> ReportedScopes, DateTimeOffset CheckedAt);

// Account-wide access. Connection checks and inventory remain read-only.
internal sealed class GitHubClient : IDisposable
{
    readonly HttpClient client;
    readonly GitHubConnection connection;
    readonly HashSet<string> scopes = new(StringComparer.Ordinal);
    static readonly HashSet<string> KnownScopes = new(StringComparer.Ordinal)
    { "repo", "repo:status", "repo_deployment", "public_repo", "repo:invite", "security_events", "workflow", "read:org", "write:org", "admin:org", "admin:repo_hook", "write:repo_hook", "read:repo_hook", "delete_repo", "user", "read:user", "user:email", "gist", "project", "read:project", "write:packages", "read:packages", "delete:packages", "codespace" };

    public GitHubClient(GitHubConnection connection, HttpMessageHandler? handler = null)
    {
        GitHubCredentialStore.ValidateToken(connection.Token);
        this.connection = connection;
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LPLK-Serververwaltung/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
    }

    public async Task<GitHubCheck> CheckAsync(CancellationToken cancellationToken = default, int maxPages = 100)
    {
        if (maxPages is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxPages));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var user = await ReadAsync("user", JsonValueKind.Object, timeout.Token);
        string login = RequiredString(user.RootElement, "login");
        long accountId = RequiredId(user.RootElement);
        if (!Regex.IsMatch(login, @"\A[A-Za-z0-9-]{1,39}\z")) throw InvalidResponse();
        if (connection.AccountId != null && connection.AccountId != accountId)
            throw new InvalidOperationException("Der gespeicherte Token gehört zu einem anderen GitHub-Konto. Den gewünschten Token erneut eingeben und speichern.");
        var repositories = new Dictionary<long, GitHubRepositoryInfo>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool complete = false;
        for (int page = 1; page <= maxPages; page++)
        {
            // Build each page locally; never follow URLs supplied in a Link header.
            using var list = await ReadAsync("user/repos?visibility=all&affiliation=owner,collaborator,organization_member&sort=full_name&direction=asc&per_page=100&page=" + page,
                JsonValueKind.Array, timeout.Token);
            int count = list.RootElement.GetArrayLength();
            if (count > 100) throw InvalidResponse();
            foreach (var entry in list.RootElement.EnumerateArray())
            {
                var repo = ParseRepository(entry);
                if (repositories.TryGetValue(repo.Id, out var seen))
                {
                    if (seen != repo) throw new InvalidOperationException("Die Repository-Liste hat sich während der Abfrage geändert. Bitte erneut laden.");
                    continue;
                }
                if (!names.Add(repo.FullName)) throw InvalidResponse();
                repositories.Add(repo.Id, repo);
            }
            if (count < 100) { complete = true; break; }
        }
        // Account roles and classic token scopes do not prove that a particular write will succeed.
        return new(login, accountId, GitHubCredentialStore.TokenKind(connection.Token),
            repositories.Values.OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase).ToArray(), complete,
            false, scopes.Order(StringComparer.Ordinal).ToArray(), DateTimeOffset.UtcNow);
    }

    static GitHubRepositoryInfo ParseRepository(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        string fullName = RequiredString(root, "full_name");
        GitHubRepository parsed;
        try { parsed = GitHubCredentialStore.ParseRepository("https://github.com/" + fullName, false); }
        catch (ArgumentException) { throw InvalidResponse(); }
        if (parsed.FullName != fullName) throw InvalidResponse();
        string visibility = RequiredString(root, "visibility");
        bool isPrivate = RequiredBool(root, "private");
        if (visibility is not ("public" or "private" or "internal") || (visibility == "public") == isPrivate) throw InvalidResponse();
        bool? push = null, admin = null, read = null;
        if (root.TryGetProperty("permissions", out var permissions) && permissions.ValueKind != JsonValueKind.Null)
        {
            if (permissions.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            push = OptionalBool(permissions, "push"); admin = OptionalBool(permissions, "admin"); read = OptionalBool(permissions, "pull");
        }
        return new(RequiredId(root), parsed.FullName, parsed.Url, visibility,
            RequiredBool(root, "archived"), RequiredBool(root, "disabled"), push, admin, read);
    }

    async Task<JsonDocument> ReadAsync(string path, JsonValueKind kind, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "GitHub hat den Token abgelehnt. Gültigkeit und Ablaufdatum prüfen.",
                HttpStatusCode.Forbidden => "GitHub verweigert den Zugriff. Tokenrechte, Organisationsfreigabe oder das API-Limit prüfen.",
                HttpStatusCode.NotFound => "Der angefragte GitHub-Bereich ist für diesen Token nicht erreichbar.",
                HttpStatusCode.TooManyRequests => "Das GitHub-API-Limit ist erreicht. Bitte später erneut prüfen.",
                _ when (int)response.StatusCode is >= 300 and < 400 => "GitHub hat eine Weiterleitung gemeldet. Die Verbindung wurde nicht weitergeleitet.",
                _ => "GitHub konnte die Verbindung nicht bestätigen (HTTP " + (int)response.StatusCode + ")."
            });
        if (response.Headers.TryGetValues("X-OAuth-Scopes", out var reported))
            foreach (string scope in reported.SelectMany(s => s.Split(',')).Select(s => s.Trim()))
                if (KnownScopes.Contains(scope)) scopes.Add(scope);
        int limit = kind == JsonValueKind.Array ? 2 * 1024 * 1024 : 64 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw InvalidResponse();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
        {
            if (buffer.Length + read > limit) throw InvalidResponse();
            buffer.Write(chunk, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind != kind) { document.Dispose(); throw InvalidResponse(); }
            return document;
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    static string RequiredString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw InvalidResponse();
    static bool RequiredBool(JsonElement element, string name) => OptionalBool(element, name) ?? throw InvalidResponse();
    static bool? OptionalBool(JsonElement element, string name) => !element.TryGetProperty(name, out var value) ? null
        : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw InvalidResponse();
    static long RequiredId(JsonElement element) => element.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long id) && id > 0 ? id : throw InvalidResponse();
    static InvalidOperationException InvalidResponse() => new("GitHub hat keine vollständig prüfbaren Kontodaten geliefert.");
    public void Dispose() => client.Dispose();
}
