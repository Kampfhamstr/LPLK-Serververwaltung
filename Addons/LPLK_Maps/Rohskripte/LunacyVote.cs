namespace LPLK_Maps;

// One follow-up ballot for the selected map. Only the original, still eligible
// voters count; a missing answer is an abstention, never a yes.
public sealed class LunacyVote
{
    public const string MapId = "3070560242";
    public const int Seconds = 30;
    public const float Gravity = 400;
    private readonly HashSet<ulong> electorate = [];
    private readonly Dictionary<ulong, bool> ballots = [];
    public bool Active { get; private set; }
    public bool Accepted { get; private set; }
    public long Deadline { get; private set; }
    public int Yes { get; private set; }
    public int No { get; private set; }
    public int Serial { get; private set; }

    public bool Start(string mapId, IEnumerable<ulong> players, long now)
    {
        if (Active || mapId != MapId) return false;
        Reset();
        electorate.UnionWith(players.Where(id => id > 0));
        if (electorate.Count == 0) return false;
        Active = true; Deadline = now + Seconds * 1000L;
        return true;
    }
    public bool MayVote(ulong steam) => Active && electorate.Contains(steam) && !ballots.ContainsKey(steam);
    public bool Cast(ulong steam, bool yes, long now, int serial)
    {
        if (serial != Serial || now >= Deadline || !MayVote(steam)) return false;
        ballots.Add(steam, yes); return true;
    }
    public bool Finish(long now, IEnumerable<ulong> eligible, bool force = false)
    {
        if (!Active || !force && now < Deadline) return false;
        var online = eligible.ToHashSet();
        Yes = ballots.Count(b => b.Value && online.Contains(b.Key));
        No = ballots.Count(b => !b.Value && online.Contains(b.Key));
        Accepted = Yes > No; Active = false; return true;
    }
    public void Reset()
    {
        Active = Accepted = false; Deadline = 0; Yes = No = 0;
        electorate.Clear(); ballots.Clear(); Serial++;
    }
}
