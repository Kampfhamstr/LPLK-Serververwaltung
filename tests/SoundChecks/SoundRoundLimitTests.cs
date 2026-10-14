using LPLK_Sounds;

public static class SoundRoundLimitTests
{
    public static void Run(Action<bool, string> check)
    {
        const ulong alice = 76561198000000001, bob = 76561198000000002;
        var limit = new SoundRoundLimit();
        check(limit.Remaining(alice) == 3 && limit.Remaining(bob) == 3, "Every player begins with three public sounds");
        for (int n = 1; n <= 3; n++)
            check(limit.TryStart(alice, () => true) == SoundStartResult.Started && limit.Remaining(alice) == 3 - n,
                $"Successful public sound {n} consumes exactly one use");
        bool fourthRan = false;
        check(limit.TryStart(alice, () => { fourthRan = true; return true; }) == SoundStartResult.LimitReached && !fourthRan,
            "Fourth public sound is rejected before invoking the emitter");
        check(limit.TryStart(bob, () => true) == SoundStartResult.Started && limit.Remaining(bob) == 2 && limit.Remaining(alice) == 0,
            "Another player retains an independent allowance");
        check(limit.Remaining(ulong.Parse(alice.ToString())) == 0, "Reconnection with the same Steam identity cannot replenish sounds");
        for (int n = 0; n < 5; n++)
            check(limit.TryStart(alice, () => true, bypassLimit: true) == SoundStartResult.Started,
                "Admin can start sounds even after the normal quota is exhausted");
        check(limit.TryStart(alice, () => false, bypassLimit: true) == SoundStartResult.Failed,
            "Admin bypass preserves emitter failures such as cooldown rejection");
        check(limit.TryStart(alice, () => true) == SoundStartResult.LimitReached,
            "Revoking admin permission restores the previous normal quota");
        check(limit.TryStart(bob, () => true, bypassLimit: true) == SoundStartResult.Started && limit.Remaining(bob) == 2,
            "Admin playback does not consume the normal allowance");
        limit.Reset();
        check(limit.Remaining(alice) == 3 && limit.Remaining(bob) == 3, "New round replenishes everyone");
        check(limit.TryStart(alice, () => false) == SoundStartResult.Failed && limit.Remaining(alice) == 3,
            "Unsuccessful emitter releases its reserved use");
        try { limit.TryStart(alice, () => throw new IOException("emit failed")); }
        catch (IOException) { }
        check(limit.Remaining(alice) == 3, "Exception during start releases reservation");
        int successful = 0;
        Parallel.For(0, 32, _ => {
            if (limit.TryStart(alice, () => true) == SoundStartResult.Started) Interlocked.Increment(ref successful);
        });
        check(successful == 3 && limit.Remaining(alice) == 0, "Concurrent attempts cannot exceed three starts");
        limit.Reset();
        int nested = 0;
        bool Reenter()
        {
            nested++;
            if (nested < 5) limit.TryStart(alice, Reenter);
            return true;
        }
        limit.TryStart(alice, Reenter);
        check(nested == 3 && limit.Remaining(alice) == 0, "Reserved uses also prevent reentrant overrun");
    }
}
