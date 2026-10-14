using CS2ServerPortal;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class GitHubImportChecks
{
    const string Token = "gh" + "p_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_TEST_NOT_REAL";
    static int count;
    static void Check(bool ok, string why) { count++; if (!ok) throw new Exception("GitHub import: " + why); }
    public static async Task RunAsync()
    {
        foreach (string path in new[] { "../secret", "/root", "a//b", "a\\b", "a:stream", "a/../b", "private/key.json", "state/data.json", "Website/config/app.php", "Website/storage/a", "a.sqlite3", ".github/workflows/run.yml", ".git/config", ".env", "a.dll" })
        { bool denied = false; try { GitHubImport.ValidatePath(path); } catch (InvalidOperationException) { denied = true; } Check(denied, "unsafe selection rejected"); }
        Check(GitHubImport.BlobHash(Encoding.UTF8.GetBytes("hello\n")) == "ce013625030ba8dba906f756967f9e9ca394464a", "Git blob hash interoperable");
        string root = Path.Combine(Path.GetTempPath(), "lplk-github-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "state/github-import-source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "README.md"), "# Test\nÄnderungen\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, ".gitignore"), "private/\n", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(source, "icon.png"), [137, 80, 78, 71]);
            var entries = Directory.GetFiles(source).Select(f => new GitHubImportFile(Path.GetFileName(f), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), new FileInfo(f).Length)).ToArray();
            var manifest = new GitHubImportManifest(1, "example", 42, "import-test", true, "state/github-import-source", "Initial import", entries);
            string path = Path.Combine(root, "manifest.json"); File.WriteAllText(path, JsonSerializer.Serialize(manifest, CS2ServerPortal.Program.JsonOptions));
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            using var handler = new Fake(); using var import = new GitHubImport(root, new(Token, 42, "example"), handler);
            var result = await import.RunAsync(path, hash);
            Check(result.Verified && result.Private && result.Files == 3, "private upload verified file by file");
            Check(handler.Mutations == 5 && handler.PrivateCreated && handler.NoForce && handler.AuthValid, "scoped writes and header auth");
            Check(!JsonSerializer.Serialize(result).Contains(Token), "result has no token");
            var again = await import.RunAsync(path, hash);
            Check(again.Commit == result.Commit && handler.Mutations == 5, "retry of complete import only verifies");

            foreach (bool visibilityChanged in new[] { false, true })
            {
                File.Delete(Path.Combine(root, "state/github-import-receipt.json"));
                using var moved = new Fake { ConcurrentAfterCommit = !visibilityChanged, PublicAfterCommit = visibilityChanged };
                using var guarded = new GitHubImport(root, new(Token, 42, "example"), moved);
                bool denied = false; try { await guarded.RunAsync(path, hash); } catch (InvalidOperationException) { denied = true; }
                Check(denied && !moved.NoForce && moved.Mutations == 4, "concurrent branch or visibility change prevents publication");
            }

            foreach (bool publicTarget in new[] { false, true })
            {
                File.Delete(Path.Combine(root, "state/github-import-receipt.json"));
                using var other = new Fake { Exists = true, Public = publicTarget }; using var client = new GitHubImport(root, new(Token, 42, "example"), other);
                bool refused = false; try { await client.RunAsync(path, hash); } catch (InvalidOperationException) { refused = true; }
                Check(refused && other.Mutations == 0, "existing or public target never overwritten");
            }
            using (var other = new Fake { Error = true })
            using (var client = new GitHubImport(root, new(Token, 42, "example"), other))
            {
                bool hidden = false; try { await client.RunAsync(path, hash); } catch (InvalidOperationException e) { hidden = !e.Message.Contains(Token); }
                Check(hidden && other.Mutations == 0, "response bodies and redirects suppressed");
            }
            foreach (var invalid in new[] { manifest with { Private = false }, manifest with { Files = [entries[0], entries[0]] }, manifest with { Root = "private" }, manifest with { Files = [new("../secret", entries[0].Sha256, 3)] } })
            {
                bool denied = false; try { GitHubImport.LoadPayload(root, invalid); } catch (InvalidOperationException) { denied = true; }
                Check(denied, "unsafe manifest rejected locally");
            }
            File.AppendAllText(Path.Combine(source, "README.md"), "changed");
            using (var other = new Fake())
            using (var client = new GitHubImport(root, new(Token, 42, "example"), other))
            {
                bool denied = false; try { await client.RunAsync(path, hash); } catch (InvalidOperationException) { denied = true; }
                Check(denied && other.Requests == 0, "file change detected before any request");
            }
            File.WriteAllText(Path.Combine(source, "README.md"), Token);
            var secret = manifest with { Files = [new("README.md", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, "README.md")))), Token.Length), entries.Single(e => e.Path == ".gitignore")] };
            bool rejected = false; try { GitHubImport.LoadPayload(root, secret); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "token-like source blocked");
            Console.WriteLine("GitHub import checks passed: " + count);
        }
        finally
        {
            string full = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("lplk-github-import-")
                && !Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)) Directory.Delete(full, true);
        }
    }

    sealed class Fake : HttpMessageHandler
    {
        public bool Exists, Public, Error, PrivateCreated, NoForce, ConcurrentAfterCommit, PublicAfterCommit, AuthValid = true;
        public int Requests, Mutations;
        string head = new('a', 40);
        readonly string initialTree = new('b', 40), finalTree = new('d', 40), commit = new('c', 40);
        readonly List<object> entries = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; AuthValid &= request.RequestUri!.Host == "api.github.com" && request.Headers.Authorization?.Parameter == Token;
            string path = request.RequestUri.AbsolutePath; string method = request.Method.Method;
            if (Error) return Response(HttpStatusCode.Found, Token);
            if (method != "GET") Mutations++;
            object repo = new { id = 99, full_name = "example/import-test", @private = !Public, visibility = Public ? "public" : "private", archived = false, owner = new { id = 42 }, default_branch = "main" };
            if (path == "/user") return Ok(new { id = 42, login = "example" });
            if (path == "/repos/example/import-test") return Exists ? Ok(repo) : Response(HttpStatusCode.NotFound, "{}");
            if (path == "/user/repos")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); PrivateCreated = body.RootElement.GetProperty("private").GetBoolean() && body.RootElement.GetProperty("auto_init").GetBoolean(); Exists = true; return Ok(repo);
            }
            if (path.EndsWith("/git/ref/heads/main")) return Ok(new { @object = new { sha = head } });
            if (path.Contains("/git/commits/")) return Ok(new { tree = new { sha = path.EndsWith(commit) ? finalTree : initialTree }, parents = Array.Empty<object>() });
            if (path.EndsWith("/git/trees/" + initialTree)) return Ok(new { truncated = false, tree = new[] { new { path = "README.md", mode = "100644", type = "blob", sha = new string('e', 40) } } });
            if (path.EndsWith("/git/trees/" + finalTree)) return Ok(new { truncated = false, tree = entries });
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var data = json.RootElement;
            if (path.EndsWith("/git/blobs")) return Ok(new { sha = GitHubImport.BlobHash(Convert.FromBase64String(data.GetProperty("content").GetString()!)) });
            if (path.EndsWith("/git/trees"))
            {
                foreach (var e in data.GetProperty("tree").EnumerateArray()) entries.Add(new { path = e.GetProperty("path").GetString(), mode = "100644", type = "blob", sha = e.TryGetProperty("sha", out var s) ? s.GetString() : GitHubImport.BlobHash(Encoding.UTF8.GetBytes(e.GetProperty("content").GetString()!)) });
                return Ok(new { sha = finalTree });
            }
            if (path.EndsWith("/git/commits")) { if (ConcurrentAfterCommit) head = new('f', 40); if (PublicAfterCommit) Public = true; return Ok(new { sha = commit }); }
            if (path.EndsWith("/git/refs/heads/main")) { NoForce = !data.GetProperty("force").GetBoolean(); head = data.GetProperty("sha").GetString()!; return Ok(new { @object = new { sha = head } }); }
            throw new Exception("Unexpected synthetic endpoint.");
        }
        static HttpResponseMessage Ok(object data) => Response(HttpStatusCode.OK, JsonSerializer.Serialize(data));
        static HttpResponseMessage Response(HttpStatusCode status, string data) => new(status) { Content = new StringContent(data) };
    }
}
