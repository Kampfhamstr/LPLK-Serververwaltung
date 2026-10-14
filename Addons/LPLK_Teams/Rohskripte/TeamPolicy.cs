namespace LPLK_Teams;

public sealed record TeamSettings
{
    public bool Enabled { get; init; } // Actual Inner Circle configuration: disabled.
    public bool AutoDetectMaxRounds { get; init; } = true;
    public int FallbackMaxRounds { get; init; } = 20;
    public double BlockAtMaxRoundsPercent { get; init; } = .5;
    public bool BlockOnlyIfTeamIsLeading { get; init; } = true;
    public bool BlockBothTeamsIfBothReachedLimit { get; init; }
    public bool AllowSpectator { get; init; } = true;
    public bool ForceBackIfSwitchStillHappened { get; init; } = true;
    public bool AllowHalftimeTeamSwitchException { get; init; } = true;
    public int HalftimeSwitchGraceSeconds { get; init; } = 30;
    public bool TreatWinScoreAsMatchEnd { get; init; } = true;
    public bool BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd { get; init; } = true;
    public int MatchEndGuardWindowSeconds { get; init; } = 15;
    public bool UseRoundEndFallback { get; init; } = true;
    public bool AnnounceWhenTeamBecomesProtected { get; init; } = true;
    public bool ShowBlockedMessageInChat { get; init; }
    public bool ShowBlockedMessageInCenter { get; init; } = true;
    public bool ShowForceToLosingTeamMessage { get; init; } = true;
    public bool AllowChatStatusCommand { get; init; } = true;
    public static void Validate(TeamSettings s)
    {
        if(s.FallbackMaxRounds is <2 or >1000 || !double.IsFinite(s.BlockAtMaxRoundsPercent) ||
           s.BlockAtMaxRoundsPercent is <=0 or >1 || s.HalftimeSwitchGraceSeconds is <1 or >120 ||
           s.MatchEndGuardWindowSeconds is <1 or >120)throw new InvalidDataException("Ungültige Teamregeln.");
    }
}

public readonly record struct Score(int Ct,int T)
{
    public bool Valid=>Ct>=0&&T>=0&&Ct<=10000&&T<=10000;
    public int Total=>Ct+T;
    public int Leader=>Ct>T?3:T>Ct?2:0;
    public Score Add(int team)=>team==3?this with{Ct=Ct+1}:team==2?this with{T=T+1}:this;
}

public static class TeamPolicy
{
    public static int MaxRounds(TeamSettings s,int? value)=>s.AutoDetectMaxRounds&&value is >=2 and <=1000?value.Value:s.FallbackMaxRounds;
    public static int Threshold(TeamSettings s,int rounds)=>Math.Max(1,(int)Math.Ceiling(rounds*s.BlockAtMaxRoundsPercent));
    // Bit values use the engine team numbers. Both-team protection cannot be represented by a single Team enum.
    public static int Protected(TeamSettings s,Score score,int rounds)
    {
        if(!score.Valid)return 0;
        int threshold=Threshold(s,rounds);
        bool ct=score.Ct>=threshold,t=score.T>=threshold;
        if(s.BlockOnlyIfTeamIsLeading)return ct&&score.Ct>score.T?1<<3:t&&score.T>score.Ct?1<<2:0;
        if(ct&&t&&s.BlockBothTeamsIfBothReachedLimit)return (1<<2)|(1<<3);
        return ct&&(!t||score.Ct>score.T)?1<<3:t&&(!ct||score.T>score.Ct)?1<<2:0;
    }
    public static bool IsProtected(int mask,int team)=>team is 2 or 3&&(mask&(1<<team))!=0;
    public static bool Block(TeamSettings s,int current,int requested,int mask,bool halftime,int guardWinner,bool selected)
    {
        if(!s.Enabled || current==requested)return false;
        if(requested==1)return !s.AllowSpectator;
        if(requested==0)return mask!=0&&!halftime || selected&&guardWinner is 2 or 3;
        if(requested is not (2 or 3))return false;
        if(selected&&s.BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd&&requested==guardWinner)return true;
        return !halftime&&IsProtected(mask,requested);
    }
    public static bool ScoreEndsMatch(TeamSettings s,Score score,int rounds,bool overtime,bool canClinch)
        =>score.Valid&&score.Leader!=0&&!overtime&&(score.Total>=rounds || s.TreatWinScoreAsMatchEnd&&canClinch&&Math.Max(score.Ct,score.T)>=rounds/2+1);
    public static int? ParseRequest(string commandLine)
    {
        var parts=commandLine.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0 || !new[]{"jointeam","teammenu"}.Contains(parts[0],StringComparer.OrdinalIgnoreCase))return null;
        if(parts.Length<2)return null; // Opening the team menu is harmless.
        return parts[1].Trim('"').ToLowerInvariant() switch
        {
            "0" or "auto"=>0,"1" or "spec" or "spectator"=>1,
            "2" or "t" or "terrorist" or "terrorists"=>2,
            "3" or "ct" or "counterterrorist" or "counterterrorists"=>3,_=>null
        };
    }
    public static int FallbackTeam(int old,int protectedMask)=>old is >=1 and <=3&&!IsProtected(protectedMask,old)?old:1;
}

// Monotonic deadlines. A window is opened by a transition, never by polling the same score.
public sealed class MatchWindows
{
    private bool halfSeen,endSeen;
    private double halfUntil,endUntil;
    private int winner;
    public void Reset(){halfSeen=endSeen=false;halfUntil=endUntil=0;winner=0;}
    public void Half(double now,int seconds){if(halfSeen)return;halfSeen=true;halfUntil=now+seconds;}
    public void End(double now,int seconds,int team){if(endSeen||team is not (2 or 3))return;endSeen=true;winner=team;endUntil=now+seconds;}
    public bool HalfActive(double now)=>now<halfUntil;
    public int Winner(double now)=>now<endUntil?winner:0;
}

// Fallback uses a round-start baseline, never adds a winner to an already updated end score.
public sealed class ScoreTracker
{
    private Score? baseline,last;
    private bool roundEnded,continuity;
    public string Source {get;private set;}="unknown";
    public void Reset(bool complete){baseline=last=complete?new Score(0,0):null;roundEnded=false;continuity=complete;Source="reset";}
    public void RoundStart(Score? live){if(live is {Valid:true})last=live;baseline=last;roundEnded=false;}
    public void RoundEnd(Score? live,int winner)
    {
        if(roundEnded)return;roundEnded=true;
        Score? projected=baseline?.Add(winner);
        if(live is {Valid:true} score && (projected is null || score.Total>=projected.Value.Total)){last=score;Source="MatchData";continuity=true;}
        else if(projected is {Valid:true}){last=projected;Source="RoundEndFallback";}
    }
    public Score? Read(Score? live,bool fallback)
    {
        if(live is {Valid:true} score)
        {
            // Only keep the projected winner while the authoritative score is exactly one round behind.
            if(roundEnded && last is {} pending && baseline is {} start && score==start && pending.Total==start.Total+1)
            { Source=fallback?"RoundEndFallback":"MatchData";return fallback?last:score; }
            last=score;Source="MatchData";continuity=true;return score;
        }
        if(fallback&&continuity&&last is not null){Source="RoundEndFallback";return last;}
        Source="unknown";return null;
    }
}

public readonly record struct ConnectionKey(int Slot,ulong Steam,ulong Session);
public readonly record struct TeamIntent(ConnectionKey Connection,int OldTeam,int RequestedTeam,double Until)
{
    public bool Matches(ConnectionKey connection,int oldTeam,int newTeam,double now)
        =>Connection==connection&&OldTeam==oldTeam&&now<Until&&newTeam is 2 or 3&&(RequestedTeam==0||RequestedTeam==newTeam);
}
public sealed class TeamPermits
{
    private readonly Dictionary<int,(ConnectionKey Key,int Team,double Until)> items=[];
    public void Grant(ConnectionKey key,int team,double now)=>items[key.Slot]=(key,team,now+1);
    public void Remove(int slot)=>items.Remove(slot);
    public void Clear()=>items.Clear();
    public bool Consume(ConnectionKey key,int team,double now)
        =>items.Remove(key.Slot,out var item)&&item.Key==key&&item.Team==team&&now<item.Until;
}
public readonly record struct PendingMove(ConnectionKey Connection,int ExpectedTeam,int TargetTeam,int Epoch,long Revision,long EventSequence);
public static class MovePolicy
{
    public static bool StillCurrent(PendingMove pending,ConnectionKey current,int team,int epoch,long revision,long sequence,bool active)
        =>active&&pending.Connection==current&&pending.ExpectedTeam==team&&pending.Epoch==epoch&&pending.Revision==revision&&pending.EventSequence==sequence;
}
