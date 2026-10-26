using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using System.Collections.Immutable;

namespace LPLK_Sounds;

public sealed record SoundListeningPreference
{
    public bool Enabled { get; init; } = true;
    public int VolumePercent { get; init; } = 100;
    public bool HearAlive { get; init; } = true;
    public bool HearDead { get; init; } = true;
    public ImmutableHashSet<string> MutedSounds { get; init; } = [];
    public ImmutableHashSet<string> BlockedSoundSenders { get; init; } = [];
    public bool Allows(bool alive, string? soundId = null) => Enabled && VolumePercent > 0 &&
        (alive ? HearAlive : HearDead) && (soundId is null || !MutedSounds.Contains(soundId));
    public float Volume(float catalogVolume) => catalogVolume * VolumePercent / 100f;
    public void Validate()
    {
        if (VolumePercent is < 0 or > 100 || MutedSounds is null || MutedSounds.Count > 4096 ||
            MutedSounds.Any(id => !AssetCatalog.ValidId(id)) || BlockedSoundSenders is null || BlockedSoundSenders.Count > 10000 ||
            BlockedSoundSenders.Any(id => !SelectionPolicy.ValidSteamId(id)))
            throw new InvalidDataException("Ungueltige Lautstaerke oder Sound-Stummliste.");
    }
}

// Kept separate from loadouts: changing or resetting a card must never unmute a listener.
public sealed class SoundPreferenceStore
{
    private readonly string path;
    private Dictionary<string, SoundListeningPreference> entries = new();
    public SoundPreferenceStore(string path)
    {
        this.path = path;
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(DataFiles.ReadLimited(path), new JsonDocumentOptions {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            });
            AssetCatalog.CheckDuplicateKeys(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Leere Sound-Einstellungsdatei.");
            foreach (var item in document.RootElement.EnumerateObject())
            {
                // Read old boolean entries without rewriting the file on plugin load.
                var preference = item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? new SoundListeningPreference { Enabled = item.Value.GetBoolean() }
                    : item.Value.Deserialize<SoundListeningPreference>(SoundsConfig.Json)
                        ?? throw new InvalidDataException("Leere Spieler-Sound-Einstellung.");
                preference.Validate();
                entries.Add(item.Name, preference);
            }
        }
        if (entries.Count > 10000 || entries.Keys.Any(id => !SelectionPolicy.ValidSteamId(id)))
            throw new InvalidDataException("Ungueltige gespeicherte Sound-Einstellungen.");
    }
    public SoundListeningPreference Get(string steamId) => entries.GetValueOrDefault(steamId) ?? new();
    public bool HearsSounds(string steamId) => Get(steamId).Enabled;
    public void Set(string steamId, bool enabled) => Set(steamId, Get(steamId) with { Enabled = enabled });
    public void Set(string steamId, SoundListeningPreference preference)
    {
        if (!SelectionPolicy.ValidSteamId(steamId)) throw new InvalidDataException("Ungueltige SteamID64.");
        preference.Validate();
        if (entries.TryGetValue(steamId, out var previous) && previous == preference) return;
        var next = new Dictionary<string, SoundListeningPreference>(entries) { [steamId] = preference };
        if (next.Count > 10000) throw new InvalidDataException("Maximal 10000 gespeicherte Spieler.");
        DataFiles.WriteAtomic(path, JsonSerializer.Serialize(next, SoundsConfig.Json));
        entries = next;
    }
}

public sealed record SoundListener(int Slot, ulong SteamId, bool Authorized, bool Valid, bool Fake, bool Enabled, bool Alive = false, int VolumePercent = 100);
public sealed record SoundVolumeGroup(int VolumePercent, Dictionary<int, ulong> Recipients);
public sealed record SoundDelivery(uint Guid, ulong SteamId);

public static class SoundAudience
{
    public static bool IsPlayingAlive(bool alive, int team) => alive && team is 2 or 3;
    public static bool CanHear(SoundListener listener) =>
        listener.Slot is >= 0 and < 64 && listener.SteamId != 0 &&
        listener.Authorized && listener.Valid && !listener.Fake && listener.Enabled && listener.VolumePercent is > 0 and <= 100;

    public static SoundListener ForPlayback(SoundListener listener, bool adminOverride) => adminOverride
        ? listener with { Enabled = true, VolumePercent = 100 } : listener;
    public static bool Eligible(SoundListener listener, bool deadOnly, bool adminOverride = false) =>
        CanHear(ForPlayback(listener, adminOverride)) && (adminOverride || !deadOnly || !listener.Alive);
    public static bool RestrictToDead(bool sourceAlive, bool settingEnabled) => settingEnabled && !sourceAlive;
    public static Dictionary<int, ulong> Select(IEnumerable<SoundListener> listeners, bool deadOnly = false) =>
        listeners.Where(p => Eligible(p, deadOnly)).DistinctBy(p => p.Slot).ToDictionary(p => p.Slot, p => p.SteamId);
    public static SoundVolumeGroup[] Groups(IEnumerable<SoundListener> listeners, bool deadOnly = false, bool adminOverride = false) =>
        listeners.Where(p => Eligible(p, deadOnly, adminOverride)).Select(p => ForPlayback(p, adminOverride))
            .DistinctBy(p => p.Slot).GroupBy(p => p.VolumePercent)
            .Select(g => new SoundVolumeGroup(g.Key, g.ToDictionary(p => p.Slot, p => p.SteamId))).ToArray();

    public static bool SameConnection(int slot, ulong steamId, SoundListener current) =>
        current.Slot == slot && current.SteamId == steamId && current.Valid && !current.Fake && current.Authorized;
}

// Each broadcast retains the original identities, never just a reusable player slot.
public sealed class SoundBroadcast
{
    public string SoundId { get; }
    public bool DeadOnly { get; }
    public bool AdminOverride { get; }
    public long Started { get; }
    public double Duration { get; }
    private readonly Dictionary<int, SoundDelivery> recipients = new();
    private SoundBroadcast(string id, long started, double duration, bool deadOnly, bool adminOverride)
    { SoundId = id; Started = started; Duration = duration; AdminOverride = adminOverride; DeadOnly = deadOnly && !adminOverride; }
    public KeyValuePair<int, SoundDelivery>[] Recipients => recipients.ToArray();
    public bool Remove(int slot, out SoundDelivery delivery) => recipients.Remove(slot, out delivery!);
    public bool Expired(long now) => PlaybackPolicy.Expired(Started, now, Duration);
    public static SoundBroadcast Start(string id, long now, double duration, SoundVolumeGroup[] groups,
        Func<SoundVolumeGroup, uint> emit, Action<uint, int, ulong> stop, bool deadOnly = false, bool adminOverride = false)
    {
        var active = new SoundBroadcast(id, now, duration, deadOnly, adminOverride);
        try
        {
            foreach (var group in groups)
            {
                uint guid = emit(group);
                foreach (var (slot, steam) in group.Recipients) active.recipients.Add(slot, new(guid, steam));
            }
            return active;
        }
        catch
        {
            foreach (var (slot, delivery) in active.recipients) stop(delivery.Guid, slot, delivery.SteamId);
            throw;
        }
    }
}
