using System.Text.RegularExpressions;
namespace LPLK_Maps;

public sealed partial record MapSettings
{
    public bool NominationEnabled {get;init;}=true;
    public bool CategorizedMenus {get;init;}
    public bool BalancedCategories {get;init;}
    public bool EndOfMapSameCategoryOnly {get;init;}
    public int EndVoteSecondsBeforeTimeEnd {get;init;}
    public int EndVoteSeconds {get;init;} // zero retains the existing shared vote settings
    public int EndVoteMapsToShow {get;init;}
    public int FailedVoteCooldownSeconds {get;init;}
    public bool ExtendEnabled {get;init;}
    public bool EndVoteAllowExtend {get;init;}
    public bool ExtendInWarmup {get;init;}
    public int ExtendMinPlayers {get;init;}=1;
    public int ExtendMinRounds {get;init;}
    public int ExtendPercent {get;init;}=60;
    public int ExtendLimit {get;init;}=3;
    public int ExtendMinutes {get;init;}=15;
    public int ExtendRounds {get;init;}=5;
    public bool ExtendInitializeRoundLimits {get;init;} // opt-in compatibility with MapChooser 0.0.10-beta
    public bool DirectVotemapEnabled {get;init;}
    public int DirectVotemapPercent {get;init;}=60;
    public int DirectVotemapMinPlayers {get;init;}=1;
    public bool DirectVotemapAtRoundEnd {get;init;}
    public bool CycleEnabled {get;init;}
    public bool CycleRandom {get;init;}
    public string ChangeMapPermission {get;init;}="admin.changemap";
    public string SetNextMapPermission {get;init;}="admin.changemap";
    public string MapsVotePermission {get;init;}="admin.mapsvote";
    public string CyclePermission {get;init;}="admin.changemap";
    private static void ValidateParity(MapSettings s)
    {
        if(s.EndVoteSecondsBeforeTimeEnd is <0 or >3600||s.EndVoteSeconds is <0 or >120||(s.EndVoteSeconds>0&&s.EndVoteSeconds<5)||s.EndVoteMapsToShow is <0 or >30||s.FailedVoteCooldownSeconds is <0 or >3600||
           s.ExtendMinPlayers is <0 or >128||s.ExtendMinRounds is <0 or >100||s.ExtendPercent is <1 or >100||s.ExtendLimit is <0 or >100||s.ExtendMinutes is <0 or >240||s.ExtendRounds is <0 or >100||
           s.DirectVotemapPercent is <1 or >100||s.DirectVotemapMinPlayers is <0 or >128||
           new[]{s.ChangeMapPermission,s.SetNextMapPermission,s.MapsVotePermission,s.CyclePermission}.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>128||x.Any(char.IsControl)))
            throw new InvalidDataException("Ungültige Mapwahl-/Verlängerungsregeln.");
    }
}

public enum MapCategory { Surf,Kz,Other }
public sealed record MapExtension(float Minutes,int Rounds,int Wins);
public static partial class MapPolicy
{
    public static MapCategory Category(MapEntry m)=>Category(m.SourceFolder+" "+m.Name+" "+(m.Workshop?"":m.Id));
    public static MapCategory Category(string evidence)=>Regex.IsMatch(evidence,@"(?:^|[^a-z0-9])surf[_\s-]",RegexOptions.IgnoreCase)?MapCategory.Surf:
        Regex.IsMatch(evidence,@"(?:^|[^a-z0-9])(?:kz|xc|bkz)[_\s-]",RegexOptions.IgnoreCase)?MapCategory.Kz:MapCategory.Other;
    public static string CategoryName(MapCategory c)=>c==MapCategory.Surf?"Surf":c==MapCategory.Kz?"KZ":"Sonstige";
    public static List<MapEntry> CandidateSelection(IReadOnlyList<MapEntry> randomized,IReadOnlySet<string> nominated,int limit,bool balanced)
    {
        var result=randomized.Where(m=>nominated.Contains(m.Id)).Take(limit).ToList();
        var remaining=randomized.Where(m=>result.All(x=>x.Id!=m.Id)).ToList();
        if(!balanced){result.AddRange(remaining.Take(limit-result.Count));return result;}
        var queues=Enum.GetValues<MapCategory>().Select(c=>new Queue<MapEntry>(remaining.Where(m=>Category(m)==c))).ToArray();
        while(result.Count<limit&&queues.Any(q=>q.Count>0))foreach(var q in queues)if(q.Count>0&&result.Count<limit)result.Add(q.Dequeue());
        return result;
    }
    public static MapExtension Extension(MapSettings s,int already,float minutes,int rounds,int wins)
    {
        if(already>=s.ExtendLimit||already<0||!float.IsFinite(minutes)||minutes<0||rounds<0||wins<0)throw new InvalidDataException("Verlängerung nicht verfügbar.");
        float t=minutes>0&&s.ExtendMinutes>0?minutes+s.ExtendMinutes:minutes;
        int r=rounds>0&&s.ExtendRounds>0?checked(rounds+s.ExtendRounds):rounds;
        int w=wins>0&&s.ExtendRounds>0?checked(wins+(s.ExtendRounds+1)/2):wins;
        if(s.ExtendInitializeRoundLimits&&s.ExtendRounds>0){if(rounds==0)r=checked(24+s.ExtendRounds);if(wins==0)w=checked(r/2+1+(s.ExtendRounds+1)/2);}
        if(t>1440||r>1000||w>1000||(t==minutes&&r==rounds&&w==wins))throw new InvalidDataException("Kein verlängerbares Limit vorhanden.");
        return new(t,r,w);
    }
    public static MapEntry? Resolve(IEnumerable<MapEntry> entries,string query)
    {
        query=query.Trim();var maps=entries.ToList();
        var exact=maps.Where(m=>m.Id.Equals(query,StringComparison.OrdinalIgnoreCase)||m.Name.Equals(query,StringComparison.OrdinalIgnoreCase)||m.SourceFolder.Length>0&&m.SourceFolder.Equals(query,StringComparison.OrdinalIgnoreCase)).ToList();
        if(exact.Count>0)return exact.Count==1?exact[0]:null;
        var partial=maps.Where(m=>query.Length>0&&(m.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||m.SourceFolder.Contains(query,StringComparison.OrdinalIgnoreCase))).Take(2).ToList();
        return partial.Count==1?partial[0]:null;
    }
    public static MapEntry? NextCycle(IReadOnlyList<MapEntry> ordered,IReadOnlySet<string> allowed,string current)
    {
        int start=ordered.ToList().FindIndex(m=>m.Id==current);
        for(int i=1;i<=ordered.Count;i++){var m=ordered[(start+i+ordered.Count)%ordered.Count];if(allowed.Contains(m.Id))return m;}
        return null;
    }
}
