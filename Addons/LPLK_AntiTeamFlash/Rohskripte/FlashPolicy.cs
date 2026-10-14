using System.Text.RegularExpressions;

namespace LPLK_AntiTeamFlash;

public sealed record FlashSettings
{
    public bool Enabled { get; init; } = true;
    public bool FlashOwner { get; init; } = true;
    public string AccessPermission { get; init; } = "";

    public static bool ValidPermission(string? value) => value is not null &&
        (value.Length == 0 || Regex.IsMatch(value, @"\A[A-Za-z0-9_.:-]{1,128}\z"));
    public static void Validate(FlashSettings settings)
    {
        if (!ValidPermission(settings.AccessPermission)) throw new InvalidDataException("Ungültiges Schutzrecht.");
    }
}

public static class FlashPolicy
{
    // Only actual T/CT teammates. A team number alone is insufficient in FFA.
    // Self-blinding remains an independent, explicit setting even in FFA.
    public static bool Suppress(FlashSettings settings, int victimTeam, int attackerTeam,
        bool samePlayer, bool hasPermission, bool? teammatesAreEnemies) =>
        settings.Enabled && victimTeam is 2 or 3 && attackerTeam == victimTeam &&
        (settings.AccessPermission.Length == 0 || hasPermission) &&
        (samePlayer ? !settings.FlashOwner : teammatesAreEnemies == false);
}

public readonly record struct FlashIdentity(int Slot, ulong Session, uint Pawn, int Team);

// An event hook cannot surgically subtract a friendly flash from an already
// blended enemy flash. In that case preserve the engine result, never clear it.
public sealed class FlashHistory
{
    private sealed record Window(FlashIdentity Identity, double LastTime, double Until);
    private readonly Dictionary<int, Window> windows = [];
    public int Count => windows.Count;
    public void Clear() => windows.Clear();
    public void Remove(int slot) => windows.Remove(slot);

    public bool ShouldClear(FlashIdentity identity, bool suppress, double now, double duration)
    {
        if (!double.IsFinite(now) || now < 0 || !double.IsFinite(duration) || duration < 0)
        {
            // Unknown timing may describe an active enemy flash: retain until
            // this life ends. Do not let a later friendly event erase it.
            windows[identity.Slot] = new(identity, 0, double.PositiveInfinity);
            return false;
        }
        windows.TryGetValue(identity.Slot, out var old);
        if (old?.Identity != identity) old = null;
        bool clockReversed = old is not null && now < old.LastTime;
        bool preserve = clockReversed || old is not null && old.Until > now;
        bool clear = suppress && !preserve;
        double until = clear ? 0 : Math.Max(old?.Until ?? 0, now + duration);
        windows[identity.Slot] = new(identity, now, until);
        return clear;
    }
}
