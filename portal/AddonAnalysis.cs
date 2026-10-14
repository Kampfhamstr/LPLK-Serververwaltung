using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CS2ServerPortal;

internal sealed record AddonEvidence(string Server, string Path, string Kind, string Sha256, string[] Findings);
internal sealed record AddonAnalysisReport(DateTimeOffset CheckedAt, List<AddonEvidence> Files, List<InventoryGap> Gaps);

internal static class AddonAnalysis
{
    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 };
    private static bool SecretName(string name) => Regex.IsMatch(name, "password|secret|token|credential|connectionstring|apikey|privatekey", RegexOptions.IgnoreCase);
    internal static string[] JsonShape(string body)
    {
        using var json = JsonDocument.Parse(body, Jsonc);
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        void Visit(JsonElement value, string path, int depth)
        {
            if (depth > 8 || fields.Count > 2000) return;
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    // No values, user identifiers or secret-bearing branches are exported.
                    if (SecretName(property.Name) || !Regex.IsMatch(property.Name, "^[A-Za-z_][A-Za-z0-9_.-]{0,79}$")) continue;
                    string child = path.Length == 0 ? property.Name : path + "." + property.Name;
                    string kind = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : property.Value.ValueKind.ToString();
                    fields.Add(child + " : " + kind);
                    Visit(property.Value, child, depth + 1);
                }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray().Take(10)) Visit(item, path + "[]", depth + 1);
        }
        Visit(json.RootElement, "", 0);
        return fields.ToArray();
    }

    internal static string[] SourceFacts(string body)
    {
        var results = new SortedSet<string>(StringComparer.Ordinal);
        void Extract(string label, string pattern)
        {
            foreach (Match match in Regex.Matches(body, pattern))
            {
                int line = body.AsSpan(0, match.Index).Count('\n') + 1;
                results.Add($"{label} L{line}: {match.Groups[1].Value}");
            }
        }
        Extract("command literal (static candidate)", "(?:\\[Command|RegisterCommand)\\s*\\(\\s*\"([A-Za-z0-9_!./-]{1,100})\"");
        Extract("permission literal (static candidate)", "(?:permission\\s*:|PlayerHasPermission\\s*\\([^,]+,)\\s*\"([A-Za-z0-9_.*-]{1,100})\"");
        Extract("type", @"\b(?:class|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)");
        Extract("method", @"\b(?:public|private|internal|protected)\s+(?:(?:static|async|override|virtual|sealed)\s+)*(?:[\w<>?\[\],.]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(");
        Extract("event type", @"(?:EventListener|GameEventHandler)<([A-Za-z_][A-Za-z0-9_]*)>");
        return results.ToArray();
    }

    internal static bool Eligible(AddonFile file)
    {
        if (!file.IsFile || file.IsSymlink || file.Size > 1024 * 1024 || file.Size < 0) return false;
        string path = file.Path;
        if (Regex.IsMatch(path, @"/(translations|gamedata|obj|examples|data|logs)/", RegexOptions.IgnoreCase)) return false;
        string name = Path.GetFileName(path);
        if (SecretName(name) || Regex.IsMatch(name, @"^(database|permissions|admins|groups|bans|users|players|stats)(\.|$)", RegexOptions.IgnoreCase)) return false;
        return path.EndsWith(".cs") || path.EndsWith(".csproj") || path.EndsWith(".deps.json") ||
            ((path.Contains("/configs/") || path.Contains("/config/")) && (path.EndsWith(".json") || path.EndsWith(".jsonc")) && !name.Contains(".example."));
    }

    public static async Task<AddonAnalysisReport> CollectAsync(PanelClient client, AddonInventoryReport inventory)
    {
        var report = new AddonAnalysisReport(DateTimeOffset.UtcNow, [], []);
        foreach (var server in inventory.Servers)
        {
            var files = server.Files.Where(Eligible).ToArray();
            if (files.Length > 1500) report.Gaps.Add(new(server.Server.Identifier, "Analysis limited to first 1500 eligible files."));
            foreach (var file in files.Take(1500))
            {
                try
                {
                    string body = await client.ReadFileAsync(server.Server.Identifier, file.Path);
                    string kind;
                    string[] findings;
                    if (file.Path.EndsWith(".cs")) { kind = "source-static-candidates"; findings = SourceFacts(body); }
                    else if (file.Path.EndsWith(".csproj"))
                    {
                        kind = "project-references";
                        var xml = XDocument.Parse(body);
                        findings = xml.Descendants().Where(e => e.Name.LocalName == "PackageReference")
                            .Select(e => (string?)e.Attribute("Include") + "/" + ((string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value))
                            .Where(s => Regex.IsMatch(s, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.+*-]+$")).ToArray();
                    }
                    else if (file.Path.EndsWith(".deps.json"))
                    {
                        kind = "declared-dependencies";
                        using var json = JsonDocument.Parse(body, Jsonc);
                        findings = json.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name)
                            .Where(s => Regex.IsMatch(s, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.+-]+$")).ToArray();
                    }
                    else { kind = "configuration-shape-no-values"; findings = JsonShape(body); }
                    report.Files.Add(new(server.Server.Identifier, file.Path, kind, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))), findings));
                }
                catch (Exception ex) when (ex is PanelException or HttpRequestException or OperationCanceledException or JsonException or System.Xml.XmlException or KeyNotFoundException)
                { report.Gaps.Add(new(server.Server.Identifier + ":" + file.Path, "Could not analyze: " + SafeError.Message(ex))); }
                await Task.Delay(200);
            }
            Console.Error.WriteLine($"Analysis {server.Server.Identifier}: {files.Length} eligible files.");
        }
        return report;
    }
}
