using CS2ServerPortal;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class GitHubChecks
{
    const string FixtureToken = "github_" + "pat_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_TEST_NOT_REAL";
    const string ClassicToken = "gh" + "p_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_TEST_NOT_REAL";
    const string UserJson = """{"id":42,"login":"example"}""";
    const string RepositoryJson = """{"id":1,"full_name":"example/lplk","private":true,"visibility":"private","archived":false,"disabled":false,"permissions":{"push":true,"admin":true,"pull":true}}""";
    static int checks;
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("GitHub: " + message); }

    public static async Task RunAsync()
    {
        Check(GitHubCredentialStore.ParseRepository(" https://github.com/example/lplk.git/ ").Url == "https://github.com/example/lplk", "Canonical repository URL");
        foreach (var invalid in new[] { "http://github.com/example/lplk", "https://evil.test/example/lplk", "https://github.com.evil.test/example/lplk", "https://github.com@evil.test/example/lplk", "https://token@github.com/example/lplk", "https://github.com:443/example/lplk", "https://github.com/example/lplk?token=x", "https://github.com/example/lplk#x", "https://github.com/example/lplk/tree/main", "https://github.com/example/../lplk", "https://github.com/example/%2e%2e", "https://github.com/example/.", "https://github.com/example/..", "https://github.com/example/.git", "https://github.com/example\\lplk", "https://github.com/example/lplk\nX: y" })
        {
            bool rejected = false;
            try { GitHubCredentialStore.ParseRepository(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Unsafe target rejected");
        }
        foreach (var invalid in new[] { "", "MyGitHubPassword", "ghp_short", "github_pat_short", FixtureToken + "\r\nHeader: value", new string('x', 2000) })
        {
            using var handler = new Handler(); bool rejected = false;
            try { using var client = new GitHubClient(new(invalid), handler); } catch (ArgumentException) { rejected = true; }
            Check(rejected && handler.Requests.Count == 0, "Invalid credential rejected before request");
        }
        var connection = new GitHubConnection(FixtureToken, 42, "example");
        Check(!connection.ToString().Contains(FixtureToken), "Credential record does not print secret");
        foreach (var secret in new[] { FixtureToken, ClassicToken })
        {
            using var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, "[" + RepositoryJson + "]")) { ExpectedToken = secret };
            using var client = new GitHubClient(new(secret), handler);
            var report = await client.CheckAsync();
            Check(report.AccountId == 42 && report.Repositories.Count == 1 && report.ListingComplete && !report.TokenWriteVerified, "Account inventory without false write claim");
            Check(handler.Requests.Count == 2 && handler.Requests[0] == "GET https://api.github.com/user" && handler.Requests[1].StartsWith("GET https://api.github.com/user/repos?visibility=all&affiliation=owner,collaborator,organization_member"), "Account-wide GET endpoints");
            Check(handler.AuthValid && !handler.Requests.Any(r => r.Contains(secret)), "Token only in authorization header");
            Check(!JsonSerializer.Serialize(report).Contains(secret), "Report excludes secret");
            Check(report.TokenKind == (secret == ClassicToken ? "Classic" : "Fine-grained"), "Both PAT formats accepted");
            Check(report.ReportedScopes.SequenceEqual(new[] { "repo", "workflow" }), "Only known scopes emitted, no arbitrary header data");
        }
        foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Found, HttpStatusCode.TemporaryRedirect, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            using var handler = new Handler((code, FixtureToken)); using var client = new GitHubClient(connection, handler); bool rejected = false;
            try { await client.CheckAsync(); } catch (InvalidOperationException ex) { rejected = !SafeError.Message(ex).Contains(FixtureToken); }
            Check(rejected && handler.Requests.Count == 1, "Error body suppressed and redirects not followed");
        }
        foreach (var body in new[]
        {
            "{}", "{", FixtureToken, new string('x', 2200000), "[null]", "[{}]",
            "[" + RepositoryJson.Replace("\"visibility\":\"private\"", "\"visibility\":\"public\"") + "]",
            "[" + RepositoryJson.Replace("\"push\":true", "\"push\":\"true\"") + "]",
            "[" + RepositoryJson.Replace("example/lplk", "example/../secret") + "]",
            "[" + RepositoryJson.Replace("\"id\":1", "\"id\":0") + "]",
            "[" + RepositoryJson.Replace("\"id\":1", "\"id\":\"wrong\"") + "]"
        })
        {
            using var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, body)); using var client = new GitHubClient(connection, handler); bool rejected = false;
            try { await client.CheckAsync(); } catch (InvalidOperationException ex) { rejected = !SafeError.Message(ex).Contains(FixtureToken); }
            Check(rejected, "Malformed repository inventory fails safely");
        }
        string publicRepo = RepositoryJson.Replace("\"private\":true", "\"private\":false").Replace("\"visibility\":\"private\"", "\"visibility\":\"public\"").Replace("example/lplk", "example/public").Replace("\"id\":1", "\"id\":2");
        string archivedRepo = RepositoryJson.Replace("\"archived\":false", "\"archived\":true").Replace("\"push\":true", "\"push\":false").Replace("\"admin\":true", "\"admin\":false").Replace("example/lplk", "org/archive").Replace("\"id\":1", "\"id\":3");
        using (var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, "[" + string.Join(',', RepositoryJson, publicRepo, archivedRepo) + "]")))
        using (var client = new GitHubClient(connection, handler))
        {
            var report = await client.CheckAsync();
            Check(report.Repositories.Count == 3 && report.Repositories.Any(r => r.Visibility == "public"), "Private and public repositories accepted together");
            Check(report.Repositories.Any(r => r.Archived && r.AccountCanPush == false && r.AccountCanRead == true), "Archived and read-only repositories remain visible");
            Check(report.Repositories.Any(r => r.FullName.StartsWith("org/")), "Organization repositories retained");
        }
        foreach (var body in new[] { "[]", "{}", "{\"id\":42,\"login\":\"bad\\nvalue\"}", new string('x', 70000), UserJson.Replace("42", "43") })
        {
            using var handler = new Handler((HttpStatusCode.OK, body)); using var client = new GitHubClient(connection, handler); bool rejected = false;
            try { await client.CheckAsync(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && handler.Requests.Count == 1, "Invalid or different account stopped before repository read");
        }
        string pageOne = "[" + string.Join(',', Enumerable.Range(1, 100).Select(i => RepositoryJson.Replace("\"id\":1", "\"id\":" + i).Replace("example/lplk", "example/repo" + i))) + "]";
        using (var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, pageOne), (HttpStatusCode.OK, "[]")))
        using (var client = new GitHubClient(connection, handler))
        {
            var report = await client.CheckAsync();
            Check(report.Repositories.Count == 100 && report.ListingComplete && handler.Requests[^1].EndsWith("page=2"), "Pagination continues past exactly one full page");
            Check(handler.Requests.All(r => r.StartsWith("GET https://api.github.com/")), "Malicious Link header not followed");
        }
        using (var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, pageOne)))
        using (var client = new GitHubClient(connection, handler))
            Check(!(await client.CheckAsync(maxPages: 1)).ListingComplete, "Page bound is visibly incomplete");
        using (var handler = new Handler((HttpStatusCode.OK, UserJson), (HttpStatusCode.OK, "[]")))
        using (var client = new GitHubClient(connection, handler))
            Check((await client.CheckAsync()).Repositories.Count == 0, "Empty account is valid");
        using (var handler = new Handler())
        using (var client = new GitHubClient(connection, handler))
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); bool cancelled = false;
            try { await client.CheckAsync(cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && handler.Requests.Count == 0, "Cancellation stops before request");
        }

        string workspace = Path.Combine(Path.GetTempPath(), "lplk-github-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(workspace, "private", "github-connection.json");
        try
        {
            var store = new GitHubCredentialStore(workspace); Check(store.Read() == null, "Missing store is unconfigured");
            store.Save(connection); string saved = File.ReadAllText(path);
            Check(!saved.Contains(FixtureToken) && !saved.Contains("example"), "Account and token encrypted together");
            Check(store.Read() == connection, "DPAPI roundtrip");
            var replacement = connection with { Token = ClassicToken }; store.Save(replacement);
            Check(store.Read() == replacement && Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "Atomic replacement without stale files");
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var encrypted = Convert.FromBase64String(json.RootElement.GetProperty("protectedConnection").GetString()!); encrypted[^1] ^= 1;
                File.WriteAllText(path, JsonSerializer.Serialize(new { version = 2, protectedConnection = Convert.ToBase64String(encrypted) }));
                bool rejected = false; try { store.Read(); } catch (CryptographicException) { rejected = true; }
                Check(rejected, "Tampered encrypted account rejected");
            }
            byte[] legacyPlain = JsonSerializer.SerializeToUtf8Bytes(new { repositoryUrl = "https://github.com/example/lplk", token = FixtureToken });
            var legacy = ProtectedData.Protect(legacyPlain, Encoding.UTF8.GetBytes("LPLK/GitHub/private-repository/v1"), DataProtectionScope.CurrentUser);
            CryptographicOperations.ZeroMemory(legacyPlain);
            string legacyJson = JsonSerializer.Serialize(new { version = 1, protectedConnection = Convert.ToBase64String(legacy) }); File.WriteAllText(path, legacyJson);
            Check(store.Read() == new GitHubConnection(FixtureToken), "Legacy repo-bound token retained without binding");
            Check(File.ReadAllText(path) == legacyJson, "Read-only migration does not rewrite credentials");
            store.Save(connection); using var upgraded = JsonDocument.Parse(File.ReadAllText(path));
            Check(upgraded.RootElement.GetProperty("version").GetInt32() == 2, "Save upgrades encrypted schema");
        }
        finally
        {
            // Only synthetic files created above; never touch the real credential store.
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(Path.GetDirectoryName(path)!)) Directory.Delete(Path.GetDirectoryName(path)!, false);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, false);
        }
        Console.WriteLine($"GitHub account checks: {checks} PASS (synthetic data, no network writes).");
    }

    sealed class Handler(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        readonly Queue<(HttpStatusCode Status, string Body)> queue = new(replies);
        public List<string> Requests { get; } = [];
        public bool AuthValid { get; private set; } = true;
        public string ExpectedToken { get; init; } = FixtureToken;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request.Method + " " + request.RequestUri);
            AuthValid &= request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == ExpectedToken;
            var reply = queue.Dequeue(); var response = new HttpResponseMessage(reply.Status) { Content = new StringContent(reply.Body) };
            response.Headers.Add("X-OAuth-Scopes", "repo, workflow, " + FixtureToken); response.Headers.Add("Link", "<https://evil.test/page2>; rel=\"next\"");
            if ((int)reply.Status is >= 300 and < 400) response.Headers.Location = new Uri("https://evil.test/");
            return Task.FromResult(response);
        }
    }
}
