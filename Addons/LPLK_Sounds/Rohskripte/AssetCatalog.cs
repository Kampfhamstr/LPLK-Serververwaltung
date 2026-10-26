using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LPLK_Sounds;

public sealed class CardAsset
{
    public string Title { get; set; } = "";
    public string Particle { get; set; } = "";
    public double DurationSeconds { get; set; } = 3;
}

public sealed class SoundAsset
{
    // Omit false so previously published Schema-1 catalog checksums stay valid.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AdminOnly { get; set; }
    public string Title { get; set; } = "";
    public string Event { get; set; } = "";
    public string[] Resources { get; set; } = [];
    public double DurationSeconds { get; set; } = 3;
    public float Volume { get; set; } = 0.7f;
}

public static class SoundAccess
{
    public static bool CanStart(AssetCatalog catalog, string id, bool admin) =>
        !catalog.Sounds.TryGetValue(id, out var sound) || !sound.AdminOnly || admin;
    public static bool CanPlayAutomatically(AssetCatalog catalog, string id) =>
        catalog.Sounds.TryGetValue(id, out var sound) && !sound.AdminOnly;
}

public static class SoundResources
{
    public const string Library = "soundevents/deatheffects/deatheffects.vsndevts";
    public const string LegacyLibrary = "soundevents/soundevents_addon.vsndevts";
    public static bool IsLibrary(string? path) => path is Library or LegacyLibrary;
    public static bool UsesLegacy(AssetCatalog catalog) => catalog.Sounds.Values.Any(s => s.Resources.Contains(LegacyLibrary, StringComparer.Ordinal));

    // Accept old catalogs on the server during rollout; never publish the conflicting path again.
    public static void ValidateExport(AssetCatalog catalog, string contentRoot, string gameRoot)
    {
        catalog.Validate();
        if (UsesLegacy(catalog)) throw new InvalidDataException("Alter Soundkatalog: zuerst auf " + Library + " migrieren.");
        if (File.Exists(Path.Combine(contentRoot, LegacyLibrary)) || File.Exists(Path.Combine(gameRoot, LegacyLibrary + "_c")))
            throw new InvalidDataException("Generische Sounddatei soundevents_addon noch im Addon. Vor dem Upload ausserhalb des Addons sichern und entfernen, damit Map-Sounds nicht verdeckt werden.");
    }
}

public sealed class AssetCatalog
{
    public int Schema { get; set; } = 1;
    public string? DefaultCard { get; set; }
    public string? DefaultSound { get; set; }
    public Dictionary<string, CardAsset> Cards { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, SoundAsset> Sounds { get; set; } = new(StringComparer.Ordinal);

    public static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"\A[a-z0-9_]{1,24}\z") && id != "off";
    private static bool ValidDuration(double n) => double.IsFinite(n) && n > 0 && n <= 60;
    public static AssetCatalog Read(string path)
        => Parse(DataFiles.ReadLimited(path));
    public static AssetCatalog Parse(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > 4 * 1024 * 1024) throw new InvalidDataException("Katalog groesser als 4 MB.");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        CheckDuplicateKeys(doc.RootElement);
        var catalog = JsonSerializer.Deserialize<AssetCatalog>(text, SoundsConfig.Json) ?? throw new InvalidDataException("Leerer Katalog.");
        catalog.Validate();
        return catalog;
    }
    internal static void CheckDuplicateKeys(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in node.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw new InvalidDataException("Doppelter Katalogschluessel: " + field.Name);
                CheckDuplicateKeys(field.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var value in node.EnumerateArray()) CheckDuplicateKeys(value);
    }
    public void Validate()
    {
        if (Schema != 1 || Cards is null || Sounds is null || Cards.Count > 256 || Sounds.Count > 256)
            throw new InvalidDataException("Katalogschema oder Anzahl ungueltig (maximal 256 Karten und 256 Sounds).");
        foreach (var (id, card) in Cards)
        {
            if (!ValidId(id) || card is null || !ValidDuration(card.DurationSeconds) || card.Title is null || card.Title.Length > 80 || card.Title.Any(char.IsControl))
                throw new InvalidDataException("Ungueltige Karte: " + id);
            if (card.Particle is null || !Regex.IsMatch(card.Particle, @"\Aparticles/deatheffects/[a-z0-9_]{1,24}/s[0-9a-f]{8}/hud\.vpcf\z"))
                throw new InvalidDataException("Ungueltiger HUD-Pfad: " + id);
        }
        foreach (var (id, sound) in Sounds)
        {
            if (!ValidId(id) || sound is null || !ValidDuration(sound.DurationSeconds) || !float.IsFinite(sound.Volume) || sound.Volume is < 0 or > 1 || sound.Title is null || sound.Title.Length > 80 || sound.Title.Any(char.IsControl))
                throw new InvalidDataException("Ungueltiger Sound: " + id);
            if (sound.Event is null || !Regex.IsMatch(sound.Event, @"\Adeatheffects\.[a-z0-9_.]{1,80}\z")) throw new InvalidDataException("Soundevent muss mit deatheffects. beginnen: " + id);
            if (sound.Resources is null || sound.Resources.Length is < 2 or > 32 || sound.Resources.Distinct(StringComparer.Ordinal).Count() != sound.Resources.Length ||
                sound.Resources.Count(SoundResources.IsLibrary) != 1 ||
                !sound.Resources.Any(p => p is not null && p.EndsWith(".vsnd", StringComparison.Ordinal)))
                throw new InvalidDataException("Sound benoetigt Eventdatei und mindestens eine VSND-Ressource: " + id);
            foreach (var path in sound.Resources)
                if (path is null || (!SoundResources.IsLibrary(path) && !Regex.IsMatch(path, @"\Asounds/deatheffects/(?:[a-z0-9_]+/)*[a-z0-9_]+\.vsnd\z")))
                    throw new InvalidDataException("Ungueltige Soundressource: " + id);
        }
        if(DefaultSound is not null && !Sounds.ContainsKey(DefaultSound))throw new InvalidDataException("Unbekannter Standardsound.");
    }
}

// SteamID validation retained for the listener preference format; no card assignments are loaded.
public static class SelectionPolicy
{
    public static bool ValidSteamId(string? id)=>id?.Length==17&&ulong.TryParse(id,out var n)&&n>=76561197960265728&&n<=76561202255233023;
}
public static class DataFiles
{
    public static string ReadLimited(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Datei ist groesser als 4 MB: " + Path.GetFileName(path));
        return File.ReadAllText(path);
    }
    public static void WriteAtomic(string path, string text)
    {
        if(LPLK.Common.RoundFileStore.Active){File.WriteAllText(path,text);return;}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = LPLK.Common.RamFile.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
                stream.Write(bytes); stream.Flush();
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

