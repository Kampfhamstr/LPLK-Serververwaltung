using System.Text.RegularExpressions;

namespace LPLK_Maps;

public sealed record MapEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; } = 64;
    public int? ConfiguredMaxPlayers { get; init; }
    public long WorkshopUpdated { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsMap { get; init; } = true;
    public bool Removed { get; init; }
    public string SourceFolder { get; init; } = "";
    public int? TSpawns { get; init; }
    public int? CtSpawns { get; init; }
    public int? BombSites { get; init; }
    public int? BombVolumes { get; init; }
    public string? ObjectiveEvidence { get; init; }
    public int MeasurementVersion { get; init; }
    public int? Hostages { get; init; }
    public DateTimeOffset? MeasuredAt { get; init; }
    public bool InPool { get; init; } = true;
    public bool RemoteMapVerified { get; init; }
    public long DownloadBytes { get; init; }
    public string? ProbeError { get; init; }
    public DateTimeOffset? LastProbeAt { get; init; }
    public MapLimitProposal? LimitProposal { get; init; }
    public string? ApprovedLimitSignature { get; init; }
    public DateTimeOffset? LimitsApprovedAt { get; init; }
    public bool Workshop => ulong.TryParse(Id, out ulong number) && number > 0;
}

public sealed record MapLimitProposal(int MinPlayers,int MaxPlayers,int Reserve,string Signature);

public sealed record MapSettings
{
    public bool Enabled { get; init; } = true;
    public bool MeasurementOnly { get; init; } = true;
    public int RtvPercent { get; init; } = 60;
    public int VoteSeconds { get; init; } = 60;
    public int MapsToShow { get; init; } = 10;
    public int CooldownMaps { get; init; } = 3;
    public int RtvDelaySeconds { get; init; } = 0;
    public int AutoVoteMinutes { get; init; } = 0;
    public int ChangeDelaySeconds { get; init; } = 3;
    public bool RtvEnabled { get; init; } = true;
    public bool RtvInWarmup { get; init; }
    public int RtvMinPlayers { get; init; } = 1;
    public int RtvMinRounds { get; init; }
    public bool RtvAtRoundEnd { get; init; }
    public bool AllowSpectatorsToVote { get; init; }
    public bool AllowCurrentMap { get; init; }
    public bool EndOfMapVote { get; init; } = true;
    public int TriggerRoundsBeforeEnd { get; init; } = 2;
    public bool ForceDifferentMapAtMatchEnd { get; init; } = true;
    public bool FallbackRandomMap { get; init; } = true;
    public int MatchEndDelaySeconds { get; init; } = 3;
    public int VerifySeconds { get; init; } = 8;
    public int MaxAttempts { get; init; } = 3;
    public bool SetNextLevel { get; init; } = true;
    public bool RequireVpkFile { get; init; } = true;
    public bool RescanOnMapLoad { get; init; } = true;
    public bool RescanBeforeVote { get; init; } = true;
    public string WorkshopPath { get; init; } = "";
    public string CollectionId { get; init; } = "";
    public string[] WorkshopIds { get; init; } = [];
    public int PoolRefreshMinutes { get; init; } = 15;
    public bool AutoMeasureEmptyServer { get; init; } = true;
    public int EmptySecondsBeforeProbe { get; init; } = 120;
    public int MinimumFreeDiskGiB { get; init; } = 10;
    public int MaxWorkshopGiB { get; init; } = 25;
    public string[] ExcludedWorkshopIds { get; init; } = ["3797047889"];
    public static void Validate(MapSettings s)
    {
        if(s.RtvPercent is < 1 or > 100 || s.VoteSeconds is < 5 or > 120 || s.CooldownMaps is < 0 or > 20 ||
            s.RtvDelaySeconds is < 0 or > 3600 || s.AutoVoteMinutes is < 0 or > 240 || s.ChangeDelaySeconds is < 0 or > 60 ||
            s.MapsToShow is < 1 or > 30 || s.RtvMinPlayers is < 1 or > 128 || s.RtvMinRounds is < 0 or > 100 ||
            s.TriggerRoundsBeforeEnd is < 1 or > 30 || s.MatchEndDelaySeconds is < 0 or > 60 || s.VerifySeconds is < 2 or > 60 ||
            s.MaxAttempts is < 1 or > 10 || s.WorkshopPath is null || s.WorkshopPath.Contains('\0') ||
            s.CollectionId is null || (s.CollectionId.Length>0 && WorkshopId(s.CollectionId) is null) ||
            s.ExcludedWorkshopIds is null || s.ExcludedWorkshopIds.Any(id=>WorkshopId(id) is null) ||
            s.WorkshopIds is null || s.WorkshopIds.Length>2000 || s.WorkshopIds.Any(id=>WorkshopId(id) is null) ||
            s.PoolRefreshMinutes is < 1 or > 1440 || s.EmptySecondsBeforeProbe is < 15 or > 3600 ||
            s.MinimumFreeDiskGiB is < 5 or > 1000 || s.MaxWorkshopGiB is < 1 or > 1000)
            throw new InvalidDataException("Ungültige Map-Einstellungen.");
    }
    private static string? WorkshopId(string? value)=>MapPolicy.WorkshopId(value);
}

public static class MapPolicy
{
    public static MapEntry CaptureMeasurement(MapEntry map,int t,int ct,int bombSites,int hostages)
    {
        var limits=MeasuredLimits(t,ct,bombSites,hostages,map.ConfiguredMaxPlayers??128);
        string signature=$"v2:{map.Id}:{map.WorkshopUpdated}:{t}:{ct}:{bombSites}:{hostages}:{map.ConfiguredMaxPlayers}:{limits.Min}:{limits.Max}:{limits.Reserve}";
        return map with{MeasurementVersion=2,TSpawns=t,CtSpawns=ct,BombSites=bombSites,Hostages=hostages,MeasuredAt=DateTimeOffset.UtcNow,ProbeError=null,LastProbeAt=DateTimeOffset.UtcNow,
            LimitProposal=new(limits.Min,limits.Max,limits.Reserve,signature)};
    }
    public static int BombSiteCount(int volumes,int designations,int? goalSites)
    {
        if(volumes==0)return 0;
        if(volumes<0||designations<1||designations>volumes||goalSites is <0 or >2)throw new InvalidDataException("Ungültige Bombenplatz-Messung.");
        if(designations>=2)return designations;
        if(goalSites==2&&volumes>=2)return 2;
        if(volumes==1||goalSites==1)return 1;
        throw new InvalidDataException("Mehrere Bombenvolumen ohne eindeutige A/B-Zuordnung; erneute Prüfung nötig.");
    }
    public static bool LimitsApproved(MapEntry map)=>Measured(map)&&map.LimitProposal is {} p&&map.ApprovedLimitSignature==p.Signature&&map.MinPlayers==p.MinPlayers&&map.MaxPlayers==p.MaxPlayers;
    public static MapEntry ApproveLimits(MapEntry map,string displayedSignature)
    {
        if(!Measured(map)||map.LimitProposal is not {} p||p.Signature!=displayedSignature)throw new InvalidDataException("Messwerte inzwischen geändert; Übersicht erneut öffnen.");
        return map with{MinPlayers=p.MinPlayers,MaxPlayers=p.MaxPlayers,ApprovedLimitSignature=p.Signature,LimitsApprovedAt=DateTimeOffset.UtcNow};
    }
    public static (int Min,int Max,int Reserve) MeasuredLimits(int t,int ct,int bombSites,int hostages,int configuredMax=128)
    {
        if(t<1||ct<1||bombSites<0||hostages<0)throw new InvalidDataException("Keine gültige Spawn-/Zielmessung.");
        int capacity=2*Math.Min(t,ct);
        int reserve=capacity>2?2:0; // A genuine 1v1 map has no spare spawn to reserve.
        int min=hostages>0||bombSites>=2?6:1;
        int max=Math.Min(configuredMax,Math.Min(128,capacity-reserve));
        if(max<min)throw new InvalidDataException("Zu wenige nutzbare Spawnplätze für die Mindestspielerzahl.");
        return(min,max,reserve);
    }
    public static void Validate(IReadOnlyList<MapEntry> maps)
    {
        if(maps.Count==0 || maps.Count>2000 || maps.Select(m=>m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=maps.Count)
            throw new InvalidDataException("Mapliste leer, zu groß oder doppelte IDs.");
        foreach(var m in maps)
            if(!Regex.IsMatch(m.Id,@"^(?:[1-9][0-9]{0,19}|[a-zA-Z][a-zA-Z0-9_]{1,99})$") || m.Name.Length is < 1 or > 120 ||
                m.MinPlayers<0 || m.MaxPlayers<m.MinPlayers || m.MaxPlayers>128 || m.SourceFolder is null ||
                m.ConfiguredMaxPlayers is < 1 or > 128 ||
                (m.SourceFolder.Length>0 && !SafeMapToken(m.SourceFolder)))
                throw new InvalidDataException("Ungültiger Map-Eintrag.");
    }
    public static bool SafeMapToken(string value)=>Regex.IsMatch(value,@"^[a-zA-Z0-9_][a-zA-Z0-9_/.-]{0,159}$") && !value.Contains("..") && !value.StartsWith('/');
    public static string? WorkshopId(string? value)=>value is not null && value.Length is > 0 and <=20 && value.All(char.IsAsciiDigit) && ulong.TryParse(value,out ulong id) && id>0 ? id.ToString() : null;
    public static bool Confirmed(MapEntry m,string map,string workshop)=>m.Workshop ? workshop==m.Id : Matches(m,map,workshop);
    public static bool Measured(MapEntry m)=>m.MeasurementVersion==2 && m.MeasuredAt is not null && m.TSpawns>0 && m.CtSpawns>0 && m.BombSites>=0 && m.Hostages>=0 && m.ProbeError is null;
    public static string VoteLabel(MapEntry m,bool nominated)
    {
        string text=Escape($"{m.Name} [{m.MinPlayers}–{m.MaxPlayers} Spieler]"+(nominated?" · nominiert":""));
        return nominated?"<font color='#55DD77'>"+text+"</font>":text;
    }
    public static string Escape(string value)=>value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&#39;");
    public static bool Matches(MapEntry m,string map,string workshop) => m.Workshop
        ? (!string.IsNullOrEmpty(workshop) ? workshop==m.Id : string.Equals(m.Name,Path.GetFileName(map),StringComparison.OrdinalIgnoreCase))
        : string.Equals(m.Id,Path.GetFileName(map),StringComparison.OrdinalIgnoreCase);
    public static List<MapEntry> Eligible(IEnumerable<MapEntry> maps,string current,string workshop,int humans,IReadOnlyList<string> history,int cooldown,bool relax=false,bool allowCurrent=false) =>
        maps.Where(m=>m.Enabled && !m.Removed && m.InPool && m.IsMap && (allowCurrent || !Matches(m,current,workshop)) && (humans==0 || (humans>=m.MinPlayers && humans<=m.MaxPlayers)) &&
            (relax || !history.TakeLast(cooldown).Contains(m.Id,StringComparer.OrdinalIgnoreCase))).ToList();
    public static bool CanVote(int team,bool spectators)=>spectators || team is 2 or 3;
    public static bool RtvAllowed(MapSettings s,bool warmup,int rounds,int voters)=>s.Enabled && s.RtvEnabled && (!warmup || s.RtvInWarmup) && rounds>=s.RtvMinRounds && voters>=s.RtvMinPlayers;
    public static bool NearMatchEnd(int totalScore,int maxScore,int maxRounds,int winLimit,int before)=>
        (maxRounds>0 && maxRounds-totalScore<=before) || (winLimit>0 && winLimit-maxScore<=before) ||
        (winLimit==0 && maxRounds>0 && maxRounds/2+1-maxScore<=before);
    public static int RequiredRtv(int humans,int percent) => Math.Max(1,(int)Math.Ceiling(humans*percent/100.0));
    public static List<MapEntry> Leaders(IReadOnlyList<MapEntry> candidates,IEnumerable<string> votes,IReadOnlySet<string> nominees)
    {
        var counts=votes.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
        int max=candidates.Count==0?0:candidates.Max(m=>counts.GetValueOrDefault(m.Id));
        var leaders=candidates.Where(m=>counts.GetValueOrDefault(m.Id)==max).ToList();
        var nominated=leaders.Where(m=>nominees.Contains(m.Id)).ToList();
        return nominated.Count>0?nominated:leaders;
    }
}
