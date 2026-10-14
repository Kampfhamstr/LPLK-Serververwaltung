using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record GitHubImportFile(string Path, string Sha256, long Bytes);
internal sealed record GitHubImportManifest(int Version, string Owner, long AccountId, string Name, bool Private,
    string Root, string Message, GitHubImportFile[] Files);
internal sealed record GitHubImportState(long RepositoryId, string ManifestSha256, string Branch,
    string? InitialCommit = null, string? Commit = null, bool Verified = false);
internal sealed record GitHubImportResult(string Url, string Branch, string Commit, bool Private, int Files, long Bytes, bool Verified);

// Explicit first import only. Existing repositories without our durable creation receipt are refused.
// Credentials never leave the portal process except in the HTTPS Authorization header.
internal sealed class GitHubImport : IDisposable
{
    readonly HttpClient client;
    readonly GitHubConnection connection;
    readonly string workspace;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static readonly Regex Hash = new("\\A[0-9a-fA-F]{64}\\z");
    static readonly Regex GitHash = new("\\A[0-9a-f]{40}\\z");
    internal sealed record Payload(string Path, byte[] Data, string GitSha, string? Text);

    public GitHubImport(string workspace, GitHubConnection connection, HttpMessageHandler? handler = null)
    {
        this.workspace = Path.GetFullPath(workspace);
        this.connection = connection;
        GitHubCredentialStore.ValidateToken(connection.Token);
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LPLK-Serververwaltung/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
    }

    internal static string BlobHash(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0")); hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)
            || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidOperationException("Unsicherer Importpfad.");
        if (Regex.IsMatch(path, @"(^|/)(private|state|storage|backups?|logs?|\.git|\.github|bin/Debug|obj)(/|$)|(^|/)config/app\.php$|\.(sqlite3?|db|pem|pfx|key|zip|exe|dll|so)$", RegexOptions.IgnoreCase)
            || path.Split('/').Any(p => p.Equals(".env", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Betriebsdaten oder nicht freigegebene Dateien im Import.");
    }

    internal static void NoLinks(string path, string root)
    {
        string absolute = Path.GetFullPath(path), boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!absolute.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Import außerhalb des freigegebenen Ordners.");
        for (string? part = absolute; part != null && part.Length >= boundary.Length; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Verknüpfte Importdateien sind nicht zugelassen.");
    }

    internal static Payload[] LoadPayload(string workspace, GitHubImportManifest manifest)
    {
        if (manifest.Version != 1 || !manifest.Private || manifest.AccountId <= 0 || manifest.Root is not ("state/github-import-source" or "state/github-release-source")
            || manifest.Files is not { Length: > 0 and <= 2500 } || string.IsNullOrWhiteSpace(manifest.Message) || manifest.Message.Length > 300)
            throw new InvalidOperationException("Ungültiges oder nicht privates Importmanifest.");
        var repo = GitHubCredentialStore.ParseRepository("https://github.com/" + manifest.Owner + "/" + manifest.Name, false);
        if (repo.FullName != manifest.Owner + "/" + manifest.Name) throw new InvalidOperationException("Ungültiges Importziel.");
        string root = Path.GetFullPath(Path.Combine(workspace, manifest.Root)); NoLinks(root, workspace);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<Payload>(); long total = 0;
        foreach (var file in manifest.Files)
        {
            ValidatePath(file.Path);
            if (!names.Add(file.Path) || file.Bytes is < 0 or > 4 * 1024 * 1024 || !Hash.IsMatch(file.Sha256)) throw new InvalidOperationException("Ungültiger Dateieintrag.");
            string full = Path.GetFullPath(Path.Combine(root, file.Path)); NoLinks(full, root);
            if (new FileInfo(full).Length != file.Bytes || (total += file.Bytes) > 24 * 1024 * 1024) throw new InvalidOperationException("Importumfang hat sich geändert oder ist zu groß.");
            byte[] bytes = File.ReadAllBytes(full);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Importdatei wurde nach der Prüfung verändert.");
            string? text = null;
            if (!new[] { ".png", ".ico", ".jpg", ".jpeg", ".webp", ".woff", ".woff2", ".ttf" }.Contains(Path.GetExtension(full).ToLowerInvariant()))
            {
                try { text = StrictUtf8.GetString(bytes); } catch (DecoderFallbackException) { throw new InvalidOperationException("Nicht freigegebene Binärdatei im Import."); }
                if (text.Contains('\0') || Regex.IsMatch(text, @"(?:ghp_|github_pat_|ptlc_|ptla_)[A-Za-z0-9_]{25,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----|https://discord(?:app)?\.com/api/webhooks/\d+/[A-Za-z0-9_-]+"))
                    throw new InvalidOperationException("Mögliches Geheimnis im Import; Dateiauswahl erneut prüfen.");
            }
            result.Add(new(file.Path, bytes, BlobHash(bytes), text));
        }
        if (!names.Contains("README.md") || !names.Contains(".gitignore")) throw new InvalidOperationException("Startseite oder Ausschlussregeln fehlen.");
        return result.ToArray();
    }

    public async Task<GitHubImportResult> RunAsync(string manifestPath, string expectedSha256, CancellationToken ct = default)
    {
        if (!Hash.IsMatch(expectedSha256) || new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidOperationException("Geprüfter Manifest-Hash fehlt.");
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        string manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        if (!manifestHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Importmanifest wurde verändert.");
        var manifest = JsonSerializer.Deserialize<GitHubImportManifest>(manifestBytes, Program.JsonOptions) ?? throw new InvalidOperationException("Importmanifest fehlt.");
        var files = LoadPayload(workspace, manifest); // Complete local review before any network write.
        if (files.Any(f => f.Text?.Contains(connection.Token, StringComparison.Ordinal) == true)) throw new InvalidOperationException("Zugangsdaten im Import erkannt.");
        if (connection.AccountId != manifest.AccountId) throw new InvalidOperationException("Importkonto stimmt nicht mit gespeichertem Zugang überein.");
        using var user = await Send(HttpMethod.Get, "user", null, ct);
        if (user!.RootElement.GetProperty("id").GetInt64() != manifest.AccountId || String(user.RootElement, "login") != manifest.Owner)
            throw new InvalidOperationException("Importkonto stimmt nicht mit GitHub überein.");
        string api = "repos/" + manifest.Owner + "/" + manifest.Name;
        string receiptPath = Path.Combine(workspace, "state", "github-import-receipt.json");
        var state = File.Exists(receiptPath) ? JsonSerializer.Deserialize<GitHubImportState>(File.ReadAllText(receiptPath), Program.JsonOptions) : null;
        if (state != null && state.ManifestSha256 != manifestHash) throw new InvalidOperationException("Ein anderer Importnachweis besteht; zuerst dessen Ergebnis prüfen.");
        using var existing = await Send(HttpMethod.Get, api, null, ct, allow404: true);
        if (existing == null)
        {
            if (state != null) throw new InvalidOperationException("Bereits angelegtes Importziel nicht mehr erreichbar.");
            using var created = await Send(HttpMethod.Post, "user/repos", new { name = manifest.Name, description = "LPLK CS2-Addons, Windows-Verwaltungsprogramm, Website und Installationsanleitungen", @private = true, visibility = "private", auto_init = true, has_wiki = false }, ct);
            CheckRepo(created!.RootElement, manifest);
            string branch = String(created.RootElement, "default_branch"); ValidateBranch(branch);
            state = new(created.RootElement.GetProperty("id").GetInt64(), manifestHash, branch); Save();
        }
        else
        {
            CheckRepo(existing.RootElement, manifest);
            if (state == null || existing.RootElement.GetProperty("id").GetInt64() != state.RepositoryId) throw new InvalidOperationException("Repository existiert bereits; fremde oder ungeprüfte Inhalte bleiben unverändert.");
        }
        ValidateBranch(state!.Branch);
        string refPath = api + "/git/ref/heads/" + Uri.EscapeDataString(state.Branch);
        using var reference = await Send(HttpMethod.Get, refPath, null, ct);
        string head = Sha(reference!.RootElement.GetProperty("object"));
        if (state.Commit != null && head == state.Commit) return await Verify();
        if (state.InitialCommit != null && state.InitialCommit != head) throw new InvalidOperationException("Zielbranch wurde inzwischen verändert; kein Überschreiben.");
        using var initial = await Send(HttpMethod.Get, api + "/git/commits/" + head, null, ct);
        if (initial!.RootElement.GetProperty("parents").GetArrayLength() != 0) throw new InvalidOperationException("Initiales Repository hat bereits weitere Commits; kein Überschreiben.");
        using var initialTree = await Send(HttpMethod.Get, api + "/git/trees/" + Sha(initial!.RootElement.GetProperty("tree")) + "?recursive=1", null, ct);
        var initialEntries = initialTree!.RootElement.GetProperty("tree");
        if (initialTree.RootElement.GetProperty("truncated").GetBoolean() || initialEntries.GetArrayLength() != 1 || String(initialEntries[0], "path") != "README.md")
            throw new InvalidOperationException("Initiales Repository enthält unerwartete Dateien; kein Überschreiben.");
        state = state with { InitialCommit = head }; Save();
        if (state.Commit == null)
        {
            var tree = new List<object>();
            var blobs = new HashSet<string>();
            foreach (var file in files)
            {
                if (file.Text != null) tree.Add(new { path = file.Path, mode = "100644", type = "blob", content = file.Text });
                else
                {
                    if (blobs.Add(file.GitSha))
                    {
                        using var blob = await Send(HttpMethod.Post, api + "/git/blobs", new { content = Convert.ToBase64String(file.Data), encoding = "base64" }, ct);
                        if (Sha(blob!.RootElement) != file.GitSha) throw new InvalidOperationException("GitHub-Binärdatei stimmt nicht mit lokaler Prüfung überein.");
                        await Task.Delay(1100, ct); // Respect secondary write limits; never retry a mutation blindly.
                    }
                    tree.Add(new { path = file.Path, mode = "100644", type = "blob", sha = file.GitSha });
                }
            }
            using var uploadedTree = await Send(HttpMethod.Post, api + "/git/trees", new { tree }, ct);
            string treeSha = Sha(uploadedTree!.RootElement);
            await VerifyTree(treeSha);
            using var commit = await Send(HttpMethod.Post, api + "/git/commits", new { message = manifest.Message, tree = treeSha, parents = new[] { head } }, ct);
            state = state with { Commit = Sha(commit!.RootElement) }; Save();
        }
        using var latestRepo = await Send(HttpMethod.Get, api, null, ct); CheckRepo(latestRepo!.RootElement, manifest);
        using var latest = await Send(HttpMethod.Get, refPath, null, ct);
        if (Sha(latest!.RootElement.GetProperty("object")) != head) throw new InvalidOperationException("Zielbranch wurde inzwischen verändert; kein Überschreiben.");
        using var updated = await Send(HttpMethod.Patch, api + "/git/refs/heads/" + Uri.EscapeDataString(state.Branch), new { sha = state.Commit, force = false }, ct);
        return await Verify();

        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            string temp = receiptPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Program.JsonOptions)); File.Move(temp, receiptPath, true);
        }
        async Task VerifyTree(string sha)
        {
            using var remote = await Send(HttpMethod.Get, api + "/git/trees/" + sha + "?recursive=1", null, ct);
            if (remote!.RootElement.GetProperty("truncated").GetBoolean()) throw new InvalidOperationException("GitHub-Dateibaum unvollständig.");
            var entries = remote.RootElement.GetProperty("tree").EnumerateArray().Where(e => String(e, "type") != "tree").ToArray();
            if (entries.Length != files.Length) throw new InvalidOperationException("GitHub-Dateianzahl stimmt nicht überein.");
            var actual = entries.ToDictionary(e => String(e, "path"), StringComparer.Ordinal);
            foreach (var file in files)
                if (!actual.TryGetValue(file.Path, out var entry) || String(entry, "type") != "blob" || String(entry, "mode") != "100644" || Sha(entry) != file.GitSha)
                    throw new InvalidOperationException("GitHub-Datei weicht von der geprüften Auswahl ab.");
        }
        async Task<GitHubImportResult> Verify()
        {
            using var repository = await Send(HttpMethod.Get, api, null, ct); CheckRepo(repository!.RootElement, manifest);
            if (repository.RootElement.GetProperty("id").GetInt64() != state!.RepositoryId) throw new InvalidOperationException("Repository-ID geändert.");
            using var finalRef = await Send(HttpMethod.Get, refPath, null, ct);
            if (Sha(finalRef!.RootElement.GetProperty("object")) != state!.Commit) throw new InvalidOperationException("Importcommit nicht mehr Branchspitze.");
            using var commit = await Send(HttpMethod.Get, api + "/git/commits/" + state.Commit, null, ct);
            await VerifyTree(Sha(commit!.RootElement.GetProperty("tree")));
            state = state with { Verified = true }; Save();
            return new("https://github.com/" + manifest.Owner + "/" + manifest.Name, state.Branch, state.Commit!, true, files.Length, files.Sum(f => (long)f.Data.Length), true);
        }
    }

    static void CheckRepo(JsonElement repo, GitHubImportManifest manifest)
    {
        if (!repo.GetProperty("private").GetBoolean() || String(repo, "visibility") != "private" || String(repo, "full_name") != manifest.Owner + "/" + manifest.Name
            || repo.GetProperty("owner").GetProperty("id").GetInt64() != manifest.AccountId || repo.GetProperty("archived").GetBoolean())
            throw new InvalidOperationException("Repository ist nicht das freigegebene private Ziel.");
    }
    static void ValidateBranch(string branch) { if (!Regex.IsMatch(branch, "\\A[A-Za-z0-9_-]{1,100}\\z")) throw new InvalidOperationException("Unerwarteter Initialbranch."); }
    static string String(JsonElement e, string property) => e.GetProperty(property).GetString() ?? throw new InvalidOperationException("GitHub-Antwort unvollständig.");
    static string Sha(JsonElement e) { string sha = String(e, "sha"); return GitHash.IsMatch(sha) ? sha : throw new InvalidOperationException("Ungültige GitHub-Prüfsumme."); }
    async Task<JsonDocument?> Send(HttpMethod method, string path, object? body, CancellationToken ct, bool allow404 = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (allow404 && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("GitHub-Import abgebrochen (HTTP " + (int)response.StatusCode + "); keine Fehlerantwort oder Zugangsdaten ausgegeben.");
        const int limit = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("GitHub-Antwort zu groß.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) != 0) { if (buffer.Length + count > limit) throw new InvalidOperationException("GitHub-Antwort zu groß."); buffer.Write(chunk, 0, count); }
        try { return JsonDocument.Parse(buffer.ToArray()); } catch (JsonException) { throw new InvalidOperationException("GitHub-Antwort nicht prüfbar."); }
    }
    public void Dispose() => client.Dispose();
}
