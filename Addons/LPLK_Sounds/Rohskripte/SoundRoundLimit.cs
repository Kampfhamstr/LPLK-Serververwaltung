namespace LPLK_Sounds;

public enum SoundStartResult { Started, Failed, LimitReached }

// Identities outlive connections/slots. Reset only when a new round/map begins.
public sealed class SoundRoundLimit
{
    public const int PerPlayer = 3;
    private readonly Lock sync = new();
    private readonly Dictionary<ulong, int> used = [];

    public int Remaining(ulong steamId)
    {
        lock (sync) return PerPlayer - used.GetValueOrDefault(steamId);
    }
    public SoundStartResult TryStart(ulong steamId, Func<bool> start, bool bypassLimit = false)
    {
        if (steamId == 0) throw new ArgumentException("A public sound needs a player identity.", nameof(steamId));
        lock (sync)
        {
            if (bypassLimit) return start() ? SoundStartResult.Started : SoundStartResult.Failed;
            int count = used.GetValueOrDefault(steamId);
            if (count >= PerPlayer) return SoundStartResult.LimitReached;
            // Reserve before invoking the emitter; failed attempts release the reservation.
            used[steamId] = count + 1;
            bool started = false;
            try
            {
                started = start();
                return started ? SoundStartResult.Started : SoundStartResult.Failed;
            }
            finally
            {
                if (!started && used.TryGetValue(steamId, out int current))
                {
                    if (current <= 1) used.Remove(steamId);
                    else used[steamId] = current - 1;
                }
            }
        }
    }
    public void Reset() { lock (sync) used.Clear(); }
}
