using System.Globalization;
using System.Text.Json;
namespace LPLK_Maps;

// The records retain the existing audit JSON property names. Live capture does
// not serialize, read files, or access engine entities from a worker.
public sealed record VoteActor(string Kind,string? SteamId,string Name);
public sealed record VoteChoice(string Id,string Name,bool nominated);
public sealed record VoteStartedData(string reason,VoteActor? actor,VoteChoice[] candidates);
public sealed record VoteCastData(VoteActor actor,string mapId,string mapName,string? previousMapId);
public sealed record VoteCount(string Id,string Name,bool legal,bool nominated,int votes);
public sealed record VoteFinalBallot(VoteActor actor,string mapId,bool counted,string? excludedReason);
public sealed record VoteResultData(string reason,string outcome,string? winnerId,string? winnerName,int eligibleVoters,
    VoteCount[] counts,int extensionVotes,string[] finalTiePool,bool randomTieBreak,bool zeroVotes,VoteFinalBallot[] ballots);
public sealed record DirectVoteBallot(string steamId,string mapId);
public sealed record DirectVoteResultData(string winnerId,string winnerName,int threshold,DirectVoteBallot[] votes,VoteFinalBallot[]? ballots=null,VoteCount[]? counts=null,int eligibleVoters=-1);
public sealed record VoteHistoryLine(DateTimeOffset At,VoteActor Actor,string MapId,string MapName,string? PreviousMapId);
public sealed record LastVoteReport(DateTimeOffset? StartedAt,DateTimeOffset At,string FromMap,string? VoteId,
    VoteResultData Result,VoteHistoryLine[] History,bool HistoryTruncated);

public sealed class LastVoteHistory
{
    public const int HistoryLimit=2048;
    private string? voteId,directMap;
    private DateTimeOffset? started;
    private readonly List<VoteHistoryLine> changes=[],direct=[];
    private bool truncated,directTruncated;
    public LastVoteReport? Last {get;private set;}
    public string? LoadError {get;private set;}
    public void Restore(string json)
    {
        var document=JsonSerializer.Deserialize<MapAuditDocument>(json)??throw new InvalidDataException("Leeres Mapprotokoll.");
        if(document.Schema!=1||document.Events is null||document.Events.Length>MapAuditJournal.Limit)throw new InvalidDataException("Ungültiges Mapprotokoll.");
        foreach(var e in document.Events)Observe(e);
    }
    public void MarkLoadError()=>LoadError="Vorherige Mapwahl konnte nicht gelesen werden.";
    private static T? Data<T>(object? data) where T:class=>data is T typed?typed:data is JsonElement json?json.Deserialize<T>():null;
    private static void Add(List<VoteHistoryLine> rows,VoteHistoryLine value,ref bool cut)
    {if(rows.Count==HistoryLimit){rows.RemoveAt(0);cut=true;}rows.Add(value);}
    public void Observe(MapAuditEvent e)
    {
        switch(e.Kind)
        {
            case "vote_started" when Data<VoteStartedData>(e.Data) is not null:
                voteId=e.VoteId;started=e.At;changes.Clear();truncated=false;break;
            case "ballot" when Data<VoteCastData>(e.Data) is {} ballot:
                if(voteId!=e.VoteId){voteId=e.VoteId;started=null;changes.Clear();truncated=true;}
                Add(changes,new(e.At,ballot.actor,ballot.mapId,ballot.mapName,ballot.previousMapId),ref truncated);break;
            case "vote_result" when Data<VoteResultData>(e.Data) is {} result:
                if(result.counts is null||result.ballots is null||result.finalTiePool is null)throw new InvalidDataException("Unvollständiges Abstimmungsergebnis.");
                Last=new(voteId==e.VoteId?started:null,e.At,e.Map,e.VoteId,result,
                    voteId==e.VoteId?changes.ToArray():[],voteId!=e.VoteId||truncated);LoadError=null;break;
            case "direct_ballot" when Data<VoteCastData>(e.Data) is {} ballot:
                if(directMap!=e.Map){directMap=e.Map;direct.Clear();directTruncated=false;}
                Add(direct,new(e.At,ballot.actor,ballot.mapId,ballot.mapName,ballot.previousMapId),ref directTruncated);break;
            case "direct_vote_result" when Data<DirectVoteResultData>(e.Data) is {} result:
                if(result.votes is null)throw new InvalidDataException("Unvollständige direkte Mapwahl.");
                var history=directMap==e.Map?direct.ToArray():[];
                var final=result.ballots??result.votes.Select(b=>new VoteFinalBallot(history.LastOrDefault(h=>h.Actor.SteamId==b.steamId)?.Actor??new("player",b.steamId,"Name nicht erfasst"),b.mapId,b.mapId==result.winnerId,b.mapId==result.winnerId?null:"not_recorded")).ToArray();
                var counts=result.counts??result.votes.GroupBy(b=>b.mapId).Select(g=>new VoteCount(g.Key,g.Key==result.winnerId?result.winnerName:history.LastOrDefault(h=>h.MapId==g.Key)?.MapName??g.Key,true,false,g.Count())).ToArray();
                Last=new(history.Length>0?history[0].At:null,e.At,e.Map,null,new("Direct","map_selected",result.winnerId,result.winnerName,result.eligibleVoters,counts,0,[result.winnerId],false,final.Length==0,final),history,directTruncated||result.ballots is null);
                direct.Clear();directTruncated=false;LoadError=null;break;
            case "map_change_confirmed":direct.Clear();directMap=null;directTruncated=false;break;
        }
    }
    private static readonly TimeZoneInfo Berlin=TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()?"W. Europe Standard Time":"Europe/Berlin");
    public static string Time(DateTimeOffset value)=>TimeZoneInfo.ConvertTime(value,Berlin).ToString("dd.MM.yyyy  HH:mm:ss",CultureInfo.InvariantCulture);
    public static string MapName(LastVoteReport report,string? id)=>id=="$extend"?"Map verlängern":report.Result.counts.FirstOrDefault(c=>c.Id==id)?.Name??report.History.LastOrDefault(h=>h.MapId==id)?.MapName??id??"unbekannt";
    public static int Counted(VoteResultData result,string? map=null)=>result.ballots.Count(b=>b.counted&&(map is null||b.mapId==map));
    public static string Exclusion(string? reason)=>reason switch{"not_online_or_eligible"=>"bei Abschluss nicht mehr online oder stimmberechtigt","map_no_longer_legal"=>"Map bei Abschluss nicht mehr zulässig","not_recorded"=>"Zählung im alten Protokoll nicht belegt",_=>"nicht gezählt"};
}
