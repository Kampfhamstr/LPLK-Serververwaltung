namespace AddonsManager.SteamWorkshop;

public readonly record struct InstalledRevision(ulong Size, uint Timestamp);
public sealed record TrackedDownload(bool Important, InstalledRevision? Before, long Started);
public readonly record struct DownloadCompletion(bool Changed, bool ReloadMap);

// Delays count from each failure. Offline waiting does not consume an attempt.
public sealed class DownloadRetries
{
    private static readonly long[] delays = [30_000, 60_000, 120_000];
    private readonly Dictionary<ulong, (int Attempts, long? Due)> entries = [];
    public void Reset(ulong id) => entries.Remove(id);
    public long? Due(ulong id) => entries.TryGetValue(id, out var entry) ? entry.Due : null;
    public int Attempts(ulong id) => entries.TryGetValue(id, out var entry) ? entry.Attempts : 0;
    public bool Schedule(ulong id, long now)
    {
        int count = Attempts(id);
        if (count == delays.Length) return false;
        entries[id] = (count, now + delays[count]);
        return true;
    }
    public ulong[] Ready(long now) => entries.Where(pair => pair.Value.Due is long due && due <= now).Select(pair => pair.Key).ToArray();
    public void Take(ulong id) => entries[id] = (Attempts(id) + 1, null);
}

// Tracks by Workshop ID because Steam callbacks need not arrive in request order.
public sealed class DownloadTracker
{
    private readonly Dictionary<ulong, TrackedDownload> pending = [];
    private readonly HashSet<ulong> awaitingMount = [];
    private bool importantBatchFailed;
    public IReadOnlyDictionary<ulong, TrackedDownload> Pending => pending;
    public bool Contains(ulong id) => pending.ContainsKey(id);
    public bool AwaitingMount(ulong id) => awaitingMount.Contains(id);

    public bool Start(ulong id, bool important, InstalledRevision? before, long now)
    {
        if (important && !pending.Values.Any(x => x.Important)) importantBatchFailed = false;
        if (pending.TryGetValue(id, out var existing))
        {
            if (important) pending[id] = existing with { Important = true };
            return false;
        }
        pending.Add(id, new(important, before, now));
        return true;
    }

    public DownloadCompletion? Complete(ulong id, bool success, InstalledRevision? after)
    {
        if (!pending.Remove(id, out var request)) return null;
        bool changed = success && (request.Before is null || after is null || request.Before != after);
        if (changed) awaitingMount.Add(id);
        if (request.Important && !success) importantBatchFailed = true;
        bool reload = request.Important && success && !importantBatchFailed && !pending.Values.Any(x => x.Important);
        return new(changed, reload);
    }

    public bool Mounted(ulong id) => awaitingMount.Remove(id);
}
