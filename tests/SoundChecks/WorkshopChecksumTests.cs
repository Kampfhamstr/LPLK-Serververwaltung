using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LPLK_Sounds;

public static class WorkshopChecksumTests
{
    public static void Run(Action<bool, string> check)
    {
        const string id = "3797047889";
        const string windowsRevision = "6fc39e9681a18e4730385ae7a9e5490c622e0b1ff5c91e62adedc458d0bc8f6c";
        const string linuxRevision = "c1374cd402fbf0e67f308938b0bdb518e0ea223fa96f34248f4e00a8317878dd";
        string original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/workshop-catalog-windows034.txt"));
        var document = JsonSerializer.Deserialize<WorkshopCatalogDocument>(original, SoundsConfig.Json)!;
        var catalog = document.Catalog!;
        string LegacyHash(string newline) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(catalog, new JsonSerializerOptions(SoundsConfig.Json) { NewLine = newline })))).ToLowerInvariant();
        void Reject(string text, string label)
        {
            try { WorkshopCatalogDocument.Parse(text, id); }
            catch (InvalidDataException ex) when (ex.Message.Contains("Inhaltspruefsumme")) { check(true, label); return; }
            throw new Exception("Checksum accepted changed payload: " + label);
        }

        check(document.Revision == windowsRevision && LegacyHash("\r\n") == windowsRevision,
            "Real 3-card/64-sound Windows export reproduces old CRLF checksum");
        check(LegacyHash("\n") == linuxRevision && linuxRevision != windowsRevision,
            "Old Linux calculation rejects same Windows payload: regression reproduced");
        check(WorkshopCatalogDocument.Hash(catalog) == linuxRevision,
            "New canonical hash is fixed LF even on Windows");
        foreach (string revision in new[] { windowsRevision, linuxRevision })
        foreach (string newline in new[] { "\n", "\r\n" })
        {
            string text = original.Replace(windowsRevision, revision).ReplaceLineEndings(newline);
            var parsed = WorkshopCatalogDocument.Parse(text, id);
            check(parsed.Catalog!.Cards.Count == 3 && parsed.Catalog.Sounds.Count == 64,
                $"Legacy {revision[..8]} digest accepted with {(newline.Length == 1 ? "LF" : "CRLF")} envelope");
            check(WorkshopCatalogDocument.Hash(parsed.Catalog) == linuxRevision,
                "Imported catalog has same canonical revision regardless of legacy format");
            Reject(text.Replace("\"Title\": \"Stuhl\"", "\"Title\": \"Geändert\""), "Legacy compatibility still rejects a changed title");
        }
        Reject(original.Replace(windowsRevision, new string('0', 64)), "Arbitrary checksum rejected");
        var newDocument = WorkshopCatalogDocument.Parse(WorkshopCatalogDocument.Create(id, catalog), id);
        check(newDocument.Revision == linuxRevision, "New exporter writes portable revision");
        catalog.Sounds["fi_01_aerger"].Title = "Ärger \\r\\n \"Gong\"";
        var escaped = WorkshopCatalogDocument.Parse(WorkshopCatalogDocument.Create(id, catalog).ReplaceLineEndings("\r\n"), id);
        check(escaped.Catalog!.Sounds["fi_01_aerger"].Title == catalog.Sounds["fi_01_aerger"].Title,
            "Envelope whitespace handling preserves quotes, backslashes and Unicode content");

        string root = Path.Combine(Path.GetTempPath(), "dc-checksum-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cache = Path.Combine(root, "cache.json");
            var fallback = new AssetCatalog();
            var sync = new WorkshopCatalogSync([id], cache, fallback);
            check(sync.Refresh(_ => original, _ => true) && sync.Current.Cards.Count == 3 && sync.Current.Sounds.Count == 64,
                "Existing uploaded Windows catalog replaces fallback during map refresh");
            string snapshot = File.ReadAllText(cache);
            File.WriteAllText(cache, snapshot.ReplaceLineEndings("\n"));
            var restarted = new WorkshopCatalogSync([id], cache, fallback);
            check(restarted.Current.Sounds.Count == 64 && restarted.Source == "Gesicherter Addon-Katalog",
                "Legacy Windows digest survives Linux-written cache and plugin restart");
            check(!restarted.Refresh(_ => original.Replace(windowsRevision, linuxRevision), _ => true) && restarted.Revision == linuxRevision[..12],
                "Switch from legacy to canonical digest does not report changed catalog content");
            string goodCache = File.ReadAllText(cache);
            check(!restarted.Refresh(_ => original.Replace("\"Title\": \"Stuhl\"", "\"Title\": \"Geändert\""), _ => true)
                && restarted.Current.Sounds.Count == 64 && File.ReadAllText(cache) == goodCache,
                "Invalid replacement retains current catalog and persistent cache");
        }
        finally
        {
            string resolved = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("dc-checksum-tests-"))
                throw new Exception("Unsafe cleanup path");
            Directory.Delete(resolved, true);
        }
    }
}
