using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Globalization;
using System.Text.Json;

namespace LPLK_Sounds;

public sealed record NumberedSound(int Number, string Id, string Title);
public sealed record SoundPage(int Index, int Count, IReadOnlyList<NumberedSound> Items);

// Keep the relative order of existing sounds, close gaps, then append new IDs.
public sealed class SoundNumberStore
{
    private readonly string path;
    private Dictionary<string, int> entries = new(StringComparer.Ordinal);
    public SoundNumberStore(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        string text = DataFiles.ReadLimited(path);
        using var json = JsonDocument.Parse(text);
        AssetCatalog.CheckDuplicateKeys(json.RootElement);
        entries = JsonSerializer.Deserialize<Dictionary<string, int>>(text, SoundsConfig.Json)
            ?? throw new InvalidDataException("Leere Soundnummern-Datei.");
        if (entries.Count > 10000 || entries.Keys.Any(id => !AssetCatalog.ValidId(id))
            || entries.Values.Any(n => n is < 1 or > 10000) || entries.Values.Distinct().Count() != entries.Count)
            throw new InvalidDataException("Ungueltige oder doppelte gespeicherte Soundnummern.");
    }

    public void Update(AssetCatalog catalog)
    {
        if (catalog.Sounds.Count > 10000) throw new InvalidDataException("Maximal 10000 Soundnummern.");
        if (catalog.Sounds.Keys.Any(id => !AssetCatalog.ValidId(id))) throw new InvalidDataException("Ungueltige Sound-ID.");
        var ordered = entries.Where(pair => catalog.Sounds.ContainsKey(pair.Key)).OrderBy(pair => pair.Value).Select(pair => pair.Key)
            .Concat(catalog.Sounds.Keys.Where(id => !entries.ContainsKey(id)).Order(StringComparer.Ordinal));
        var next = ordered.Select((id, index) => (id, number: index + 1)).ToDictionary(pair => pair.id, pair => pair.number, StringComparer.Ordinal);
        if (entries.Count == next.Count && entries.All(pair => next.GetValueOrDefault(pair.Key) == pair.Value)) return;
        if (File.Exists(path) && !File.Exists(path + ".before-renumbering.json"))
            File.Copy(path, path + ".before-renumbering.json");
        DataFiles.WriteAtomic(path, JsonSerializer.Serialize(next, SoundsConfig.Json));
        entries = next;
    }

    public NumberedSound[] List(AssetCatalog catalog) => entries.Where(pair => catalog.Sounds.ContainsKey(pair.Key))
        .OrderBy(pair => pair.Value).Select(pair => new NumberedSound(pair.Value, pair.Key, catalog.Sounds[pair.Key].Title)).ToArray();

    public string? Resolve(AssetCatalog catalog, int number) => entries
        .Where(pair => pair.Value == number && catalog.Sounds.ContainsKey(pair.Key)).Select(pair => pair.Key).FirstOrDefault();
}

public static class SoundMenuPolicy
{
    public const int PageSize = 10;
    public static SoundPage Page(IReadOnlyList<NumberedSound> sounds, int index)
    {
        int count = Math.Max(1, (sounds.Count + PageSize - 1) / PageSize);
        index = Math.Clamp(index, 0, count - 1);
        return new(index, count, sounds.Skip(index * PageSize).Take(PageSize).ToArray());
    }
    public static bool TryNumber(string text, out int number)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    public static bool TryPageToken(string text, out int number)
    {
        number = 0;
        return text.Length > 1 && (text[0] == 's' || text[0] == 'S') && TryNumber(text[1..], out number);
    }
}
