using System.Text.Json;
using LPLK_Sounds;

public static class SoundResourcesTests
{
    public static void Run(Action<bool, string> check)
    {
        AssetCatalog Catalog(string library) => new() { Sounds = new() {
            ["gong"] = new() { Title = "Gong", Event = "deatheffects.gong", Resources = [library, "sounds/deatheffects/gong/b12345678.vsnd"] }
        }};
        void Reject(Action action, string title)
        {
            try { action(); } catch (InvalidDataException) { check(true, title); return; }
            throw new Exception("Accepted invalid input: " + title);
        }
        var legacy = Catalog(SoundResources.LegacyLibrary);
        string legacyDocument = WorkshopCatalogDocument.Create("3797047889", legacy);
        check(SoundResources.UsesLegacy(WorkshopCatalogDocument.Parse(legacyDocument, "3797047889").Catalog!),
            "Old addon catalogs and their checksums remain readable during migration");
        var current = Catalog(SoundResources.Library);
        current.Validate();
        check(!SoundResources.UsesLegacy(current), "Private library is accepted without legacy status");
        check(WorkshopCatalogDocument.Parse(WorkshopCatalogDocument.Create("3797047889", current), "3797047889").Catalog!.Sounds["gong"].Resources[0] == SoundResources.Library,
            "Private library survives signed catalog export and parsing");
        Reject(() => Catalog("soundevents/another_map.vsndevts").Validate(), "Foreign map sound library is rejected");
        Reject(() => Catalog("soundevents/deatheffects/../../soundevents_addon.vsndevts").Validate(), "Sound library traversal is rejected");
        var both = Catalog(SoundResources.Library);
        both.Sounds["gong"].Resources = [SoundResources.Library, SoundResources.LegacyLibrary, "sounds/deatheffects/gong/b12345678.vsnd"];
        Reject(both.Validate, "A sound cannot request both conflicting and private libraries");
        string root = Path.Combine(Path.GetTempPath(), "dc-sound-library-tests-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(root, "content"), game = Path.Combine(root, "game");
        try
        {
            Directory.CreateDirectory(Path.Combine(content, "soundevents"));
            Directory.CreateDirectory(Path.Combine(game, "soundevents"));
            string source = Path.Combine(content, SoundResources.LegacyLibrary);
            string compiled = Path.Combine(game, SoundResources.LegacyLibrary + "_c");
            Reject(() => SoundResources.ValidateExport(legacy, content, game), "New exports refuse an old catalog instead of silently mutating its checksum");
            File.WriteAllText(source, "existing source");
            Reject(() => WorkshopCatalogDocument.WriteToAddon("3797047889", current, content, game), "Exporter rejects a leftover generic source library");
            check(File.ReadAllText(source) == "existing source", "Export guard never deletes existing addon sources");
            File.Move(source, Path.Combine(root, "source-backup"));
            File.WriteAllText(compiled, "existing compiled");
            Reject(() => WorkshopCatalogDocument.WriteToAddon("3797047889", current, content, game), "Exporter rejects a leftover generic compiled library");
            check(File.ReadAllText(compiled) == "existing compiled", "Export guard never deletes compiled addon resources");
            File.Move(compiled, Path.Combine(root, "compiled-backup"));
            SoundResources.ValidateExport(current, content, game);
            check(true, "Clean private-library addon passes the export guard");
            string cache = Path.Combine(root, "cache.json");
            var sync = new WorkshopCatalogSync(["3797047889"], cache, legacy);
            var requested = new List<string>();
            bool changed = sync.Refresh(_ => WorkshopCatalogDocument.Create("3797047889", current), p => { requested.Add(p); return true; });
            check(changed && sync.Current.Sounds["gong"].Event == legacy.Sounds["gong"].Event,
                "Mounted private-library catalog replaces legacy catalog without changing event identities");
            check(requested.Contains(SoundResources.Library + "_c") && !requested.Contains(SoundResources.LegacyLibrary + "_c"),
                "Updated catalog availability checks request only the private library");
        }
        finally
        {
            string resolved = Path.GetFullPath(root), temp = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("dc-sound-library-tests-")) throw new Exception("Unsafe cleanup");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
