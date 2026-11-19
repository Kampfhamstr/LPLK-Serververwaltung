using System.Text.Json;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;

namespace LPLK_Maps;

public sealed partial class Plugin
{
    private MapAuditJournal? mapAudit;
    private readonly string auditRuntime=Guid.NewGuid().ToString("N");
    private long auditSequence;
    private string? auditVoteId;
    private readonly Dictionary<ulong,VoteActor> ballotActors=[];
    private static VoteActor Actor(IPlayer? player)=>player is null
        ?new("console",null,"Serverkonsole / externer Befehl")
        :new("player",player.SteamID.ToString(),player.Name);
    private void Audit(string kind,object? data=null,string? voteId=null)
    {
        var entry=new MapAuditEvent(auditRuntime+":"+(++auditSequence),DateTimeOffset.UtcNow,kind,CurrentMap,rounds,voteId??auditVoteId,data);
        mapAudit?.Capture(entry);
        try{lastVotes.Observe(entry);}catch(Exception e){lastVotes.MarkLoadError();Core.Logger.LogWarning(e,"Letzte Mapwahl konnte nicht aktualisiert werden.");}
    }
    private void StartAudit()
    {
        mapAudit=new(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"map-audit.json"));
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"map-audit.json");
        try{if(LPLK.Common.RamFile.Exists(path))lastVotes.Restore(LPLK.Common.RamFile.ReadAllText(path));}
        catch(Exception e){lastVotes.MarkLoadError();Core.Logger.LogWarning(e,"Letzte Mapwahl konnte beim Laden nicht gelesen werden.");}
        Audit("logger_started",new {version="0.4.20",runtime=auditRuntime});
    }
    private void AuditVoteStarted(IPlayer? actor)
    {
        auditVoteId=Guid.NewGuid().ToString("N");ballotActors.Clear();
        Audit("vote_started",new VoteStartedData(reason.ToString(),reason==Reason.Admin?Actor(actor):null,
            candidates.Select(m=>new VoteChoice(m.Id,m.Name,nominatedCandidates.Contains(m.Id))).ToArray()));
    }
    private void AuditBallot(IPlayer actor,MapEntry map,string? previous)
    {
        var who=Actor(actor);ballotActors[actor.SteamID]=who;
        Audit("ballot",new VoteCastData(who,map.Id,map.Name,previous));
    }
    private void AuditResult(HashSet<ulong> online,HashSet<string> legal,List<MapEntry> leaders,MapEntry? winner,bool extended)
    {
        Audit("vote_result",new VoteResultData(reason.ToString(),extended?"extended":winner is null?"no_legal_candidate":"map_selected",
            extended?ExtendChoice:winner?.Id,extended?CurrentMap:winner?.Name,online.Count,
            candidates.Select(m=>new VoteCount(m.Id,m.Name,legal.Contains(m.Id),nominatedCandidates.Contains(m.Id),ballots.Count(b=>online.Contains(b.Key)&&b.Value==m.Id))).ToArray(),
            ballots.Count(b=>online.Contains(b.Key)&&b.Value==ExtendChoice),leaders.Select(m=>m.Id).ToArray(),!extended&&leaders.Count>1,
            ballots.Count(b=>online.Contains(b.Key)&&(legal.Contains(b.Value)||b.Value==ExtendChoice))==0,
            ballots.Select(b=>new VoteFinalBallot(ballotActors.GetValueOrDefault(b.Key)??new("player",b.Key.ToString(),"Name nicht erfasst"),b.Value,
                online.Contains(b.Key)&&(legal.Contains(b.Value)||b.Value==ExtendChoice),!online.Contains(b.Key)?"not_online_or_eligible":!legal.Contains(b.Value)&&b.Value!=ExtendChoice?"map_no_longer_legal":null)).ToArray()));
        auditVoteId=null;ballotActors.Clear();
    }
    private void AuditDirectResult(MapEntry winner,int threshold,HashSet<ulong> online,Dictionary<string,MapEntry> legal)
    {
        var players=Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).GroupBy(p=>p.SteamID).ToDictionary(g=>g.Key,g=>g.First());
        var final=directVotes.Select(b=>new VoteFinalBallot(players.TryGetValue(b.Key,out var p)?Actor(p):new("player",b.Key.ToString(),"Name nicht erfasst"),b.Value,online.Contains(b.Key)&&legal.ContainsKey(b.Value),!online.Contains(b.Key)?"not_online_or_eligible":!legal.ContainsKey(b.Value)?"map_no_longer_legal":null)).ToArray();
        var counts=directVotes.Values.Distinct().Select(id=>new VoteCount(id,maps.FirstOrDefault(m=>m.Id==id)?.Name??id,legal.ContainsKey(id),false,final.Count(b=>b.mapId==id&&b.counted))).ToArray();
        Audit("direct_vote_result",new DirectVoteResultData(winner.Id,winner.Name,threshold,directVotes.Select(v=>new DirectVoteBallot(v.Key.ToString(),v.Value)).ToArray(),final,counts,online.Count));
    }
    private void AuditCancellation(string cause)
    {
        if(auditVoteId is not null)Audit("vote_cancelled",new {cause});
        auditVoteId=null;ballotActors.Clear();
    }
    [Command("lplk_map_audit")]
    public void MapAuditStatus(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!module.CanAdmin(p))return;
        ctx.ReplyLplk("LPLK_MAP_AUDIT "+JsonSerializer.Serialize(new {version="0.4.20",runtime=auditRuntime,
            pending=mapAudit?.Pending??0,persisted=mapAudit?.Persisted??0,dropped=mapAudit?.Dropped??0,error=mapAudit?.Error,
            activeVote=voteEnds>0,pendingMap=pending?.Id,selectedNextMap=nextMap?.Id,nominations=nominations.Count,
            file="data/LPLK_Maps/map-audit.json",retainedEvents=MapAuditJournal.Limit,flush="round_end+1s"}));
    }
}
