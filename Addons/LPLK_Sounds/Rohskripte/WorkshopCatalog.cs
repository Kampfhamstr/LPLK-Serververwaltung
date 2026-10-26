using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LPLK_Sounds;

public sealed class WorkshopCatalogDocument
{
    public const int MaxBytes = 4 * 1024 * 1024;
    // Schema 1 hashes indented JSON. Pin its newline instead of using the host OS.
    private static readonly JsonSerializerOptions HashJson = new(SoundsConfig.Json) { NewLine = "\n" };
    private static readonly JsonSerializerOptions LegacyWindowsHashJson = new(SoundsConfig.Json) { NewLine = "\r\n" };
    public int Schema { get; set; } = 1;
    public string WorkshopId { get; set; } = "";
    public string Revision { get; set; } = "";
    public AssetCatalog? Catalog { get; set; }

    public static string RelativePath(string id)
    {
        if (!ulong.TryParse(id, out ulong n) || n == 0 || n.ToString() != id)
            throw new InvalidDataException("Ungueltige Workshop-ID fuer den Katalog.");
        return $"scripts/deatheffects/catalogs/{id}.txt";
    }
    public static string Hash(AssetCatalog catalog) => Hash(catalog, HashJson);
    private static string Hash(AssetCatalog catalog, JsonSerializerOptions options) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog, options)))).ToLowerInvariant();
    public static string Create(string id, AssetCatalog catalog)
    {
        _ = RelativePath(id); catalog.Validate();
        return JsonSerializer.Serialize(new WorkshopCatalogDocument { WorkshopId = id, Revision = Hash(catalog), Catalog = catalog }, SoundsConfig.Json);
    }
    public static WorkshopCatalogDocument Parse(string text, string expectedId)
    {
        _ = RelativePath(expectedId);
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Addon-Katalog groesser als 4 MB.");
        text = text.TrimStart('\uFEFF');
        using var json = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        AssetCatalog.CheckDuplicateKeys(json.RootElement);
        var document = JsonSerializer.Deserialize<WorkshopCatalogDocument>(text, SoundsConfig.Json)
            ?? throw new InvalidDataException("Leerer Addon-Katalog.");
        if (document.Schema != 1 || document.WorkshopId != expectedId || document.Catalog is null)
            throw new InvalidDataException("Addon-Katalog: Schema, Workshop-ID oder Inhalt ungueltig.");
        document.Catalog.Validate();
        // Already published Windows catalogs used CRLF. Validate that exact payload too,
        // so a plugin update repairs Linux imports without republishing the Workshop item.
        if (document.Revision != Hash(document.Catalog) && document.Revision != Hash(document.Catalog, LegacyWindowsHashJson))
            throw new InvalidDataException("Addon-Katalog: Inhaltspruefsumme stimmt nicht.");
        return document;
    }
    public static IEnumerable<string> Resources(AssetCatalog catalog) => catalog.Cards.Values.Select(c => c.Particle)
        .Concat(catalog.Sounds.Values.SelectMany(s => s.Resources)).Distinct(StringComparer.Ordinal);

    public static AssetCatalog Merge(IEnumerable<WorkshopCatalogDocument> documents)
    {
        var result = new AssetCatalog();
        foreach (var document in documents)
        {
            foreach (var (id, card) in document.Catalog!.Cards)
                if (!result.Cards.TryAdd(id, card)) throw new InvalidDataException("Karten-ID in mehreren Addon-Katalogen: " + id);
            foreach (var (id, sound) in document.Catalog.Sounds)
                if (!result.Sounds.TryAdd(id, sound)) throw new InvalidDataException("Sound-ID in mehreren Addon-Katalogen: " + id);
            if (document.Catalog.DefaultCard is string cardDefault)
            {
                if (result.DefaultCard is not null && result.DefaultCard != cardDefault) throw new InvalidDataException("Mehrere Standardkarten im Addon-Katalog.");
                result.DefaultCard = cardDefault;
            }
            if (document.Catalog.DefaultSound is string soundDefault)
            {
                if (result.DefaultSound is not null && result.DefaultSound != soundDefault) throw new InvalidDataException("Mehrere Standardsounds im Addon-Katalog.");
                result.DefaultSound = soundDefault;
            }
        }
        result.Validate(); return result;
    }

    // Plain text in game/ is packed with the addon; content/ holds its source copy.
    public static string WriteToAddon(string id, AssetCatalog catalog, string contentRoot, string gameRoot)
    {
        SoundResources.ValidateExport(catalog, contentRoot, gameRoot);
        string relative = RelativePath(id);
        foreach (string resource in Resources(catalog))
            if (!File.Exists(Path.Combine(gameRoot, resource + "_c"))) throw new FileNotFoundException("Katalogressource fehlt: " + resource);
        string text = Create(id, catalog);
        foreach (string root in new[] { contentRoot, gameRoot })
        {
            string target = Path.Combine(root, relative);
            if (!File.Exists(target) || DataFiles.ReadLimited(target) != text) DataFiles.WriteAtomic(target, text);
            if (DataFiles.ReadLimited(target) != text) throw new IOException("Addon-Katalog konnte nicht geprueft werden: " + target);
        }
        return relative;
    }
}

public sealed class WorkshopCatalogSync
{
    private readonly string[] ids;
    private readonly string cachePath;
    private Dictionary<string, WorkshopCatalogDocument> cached = new(StringComparer.Ordinal);
    public AssetCatalog Current { get; private set; }
    public string Source { get; private set; } = "Lokaler Katalog (Rueckfall)";
    public string Status { get; private set; } = "Addon-Pruefung beim naechsten Mapstart";
    public string Revision => WorkshopCatalogDocument.Hash(Current)[..12];

    public WorkshopCatalogSync(IEnumerable<string> workshopIds, string cachePath, AssetCatalog fallback)
    {
        ids = workshopIds.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string id in ids) _ = WorkshopCatalogDocument.RelativePath(id);
        this.cachePath = cachePath; Current = fallback;
        if (!File.Exists(cachePath)) return;
        try
        {
            string text = DataFiles.ReadLimited(cachePath);
            using var json = JsonDocument.Parse(text);
            AssetCatalog.CheckDuplicateKeys(json.RootElement);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Katalogcache ist kein Array.");
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                string id = entry.GetProperty("WorkshopId").GetString() ?? "";
                if (!ids.Contains(id, StringComparer.Ordinal)) continue;
                if (!cached.TryAdd(id, WorkshopCatalogDocument.Parse(entry.GetRawText(), id))) throw new InvalidDataException("Doppelte Workshop-ID im Cache.");
            }
            if (cached.Count > 0) { Current = WorkshopCatalogDocument.Merge(cached.Values); Source = "Gesicherter Addon-Katalog"; }
        }
        catch (Exception ex) when (Expected(ex))
        { cached.Clear(); Current = fallback; Status = "Cache ungueltig; lokaler Rueckfall: " + ex.Message; }
    }

    public bool Refresh(Func<string, string?> readMounted, Func<string, bool> compiledResourceExists)
    {
        try
        {
            var next = new Dictionary<string, WorkshopCatalogDocument>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string id in ids)
            {
                string path = WorkshopCatalogDocument.RelativePath(id);
                string? text = readMounted(path);
                if (text is null)
                {
                    missing.Add(id);
                    if (cached.TryGetValue(id, out var old)) next.Add(id, old);
                    continue;
                }
                var document = WorkshopCatalogDocument.Parse(text, id);
                var absent = WorkshopCatalogDocument.Resources(document.Catalog!).Where(p => !compiledResourceExists(p + "_c")).ToArray();
                if (absent.Length > 0) throw new InvalidDataException($"Addon {id}: {absent.Length} Katalogressourcen fehlen, erste: {absent[0]}");
                next.Add(id, document);
            }
            if (next.Count == 0)
            {
                Status = ids.Length == 0 ? "Keine Workshop-ID konfiguriert" : "Katalogdatei fehlt im eingebundenen Addon; Re-Upload und Download erforderlich";
                return false;
            }
            AssetCatalog merged = WorkshopCatalogDocument.Merge(next.Values);
            bool changed = WorkshopCatalogDocument.Hash(Current) != WorkshopCatalogDocument.Hash(merged);
            string snapshot = JsonSerializer.Serialize(next.Values, SoundsConfig.Json);
            if (Encoding.UTF8.GetByteCount(snapshot) > WorkshopCatalogDocument.MaxBytes) throw new InvalidDataException("Gesamter Addon-Katalog groesser als 4 MB.");
            Current = merged; cached = next;
            Source = missing.Count == 0 ? "Workshop-Addon " + string.Join(", ", next.Keys) : "Addon-Katalog mit gesicherter Rueckfallkopie";
            Status = missing.Count == 0 ? "Beim Mapstart aus GAME gelesen" : "Katalogdatei fehlt fuer " + string.Join(", ", missing) + "; vorhandener Rueckfall verwendet";
            try
            {
                if (!File.Exists(cachePath) || DataFiles.ReadLimited(cachePath) != snapshot) DataFiles.WriteAtomic(cachePath, snapshot);
            }
            catch (Exception ex) when (Expected(ex)) { Status += "; Sicherung fehlgeschlagen: " + ex.Message; }
            return changed;
        }
        catch (Exception ex) when (Expected(ex))
        { Status = "Addon-Katalog nicht uebernommen; bisheriger Katalog bleibt: " + ex.Message; return false; }
    }
    private static bool Expected(Exception ex) => ex is IOException or InvalidDataException or JsonException
        or UnauthorizedAccessException or ArgumentException or InvalidOperationException or KeyNotFoundException;
}
