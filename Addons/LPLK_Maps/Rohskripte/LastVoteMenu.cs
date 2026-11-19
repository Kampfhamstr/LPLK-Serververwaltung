using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Maps;

public sealed partial class Plugin
{
    private readonly LastVoteHistory lastVotes=new();
    private bool MayReadVote(IPlayer p)=>module.Active&&p.IsValid&&p.IsAuthorized&&!p.IsFakeClient;
    [Command("lv")]
    public void LastVoteCommand(ICommandContext context)
    {
        var report=lastVotes.Last;
        if(context.Sender is {} player)
        {
            if(!MayReadVote(player))return;
            if(report is null){player.SendLplkChat("[LPLK] "+(lastVotes.LoadError??"Noch keine abgeschlossene Map-Abstimmung protokolliert."));return;}
            OpenLastVote(player,report);return;
        }
        // Bounded console rows also permit a native read-only verification.
        if(report is not null&&context.Args is [var kind,var index]&&int.TryParse(index,out int n)&&n>=0)
        {
            object? row=kind switch{"map" when n<report.Result.counts.Length=>report.Result.counts[n],"player" when n<report.Result.ballots.Length=>report.Result.ballots[n],"change" when n<report.History.Length=>report.History[n],_=>null};
            context.Reply("LPLK_LASTVOTE_ROW "+JsonSerializer.Serialize(row));return;
        }
        context.Reply("LPLK_LASTVOTE "+JsonSerializer.Serialize(new{version="0.4.23",hasResult=report is not null,error=lastVotes.LoadError,at=report?.At,fromMap=report?.FromMap,winner=report?.Result.winnerName,
            eligible=report?.Result.eligibleVoters,counted=report is null?0:LastVoteHistory.Counted(report.Result),players=report?.Result.ballots.Length??0,maps=report?.Result.counts.Length??0,history=report?.History.Length??0,report?.HistoryTruncated,report?.Result.randomTieBreak,report?.Result.zeroVotes}));
    }
    private void VoteMenu(IPlayer p,string title,LastVoteReport r,Func<IPlayer,IReadOnlyList<(string Label,Action<IPlayer> Action)>> rows,Action<IPlayer>? back=null)
        =>CompactMenu.Open(Core,p,title,()=>module.Active,MayReadVote,rows,back);
    private static string EscapeVote(string value)=>MapPolicy.Escape(value);
    private void VoteSummary(IPlayer p,LastVoteReport r)
    {
        if(lastVotes.LoadError is {} error)p.SendLplkChat("[LPLK] "+error+" Anzeige kann unvollständig sein.");
        string winner=r.Result.outcome=="extended"?"Aktuelle Map verlängert":r.Result.winnerName??"Keine zulässige Map";
        p.SendLplkChat($"[LPLK] Letzte Mapwahl: {winner} · {LastVoteHistory.Time(r.At)} (Berlin).");
        p.SendLplkChat($"[LPLK] Auf {r.FromMap}: {LastVoteHistory.Counted(r.Result)} gezählte Stimmen"+(r.Result.eligibleVoters>=0?$" bei {r.Result.eligibleVoters} Stimmberechtigten.":"; direkte Mapwahl."));
        if(r.Result.zeroVotes)p.SendLplkChat("[LPLK] Keine gültige Stimme; Auswahl nach den bestehenden Nominierungs-/Zufallsregeln.");
        else if(r.Result.randomTieBreak)p.SendLplkChat("[LPLK] Gleichstand; zufällig gewählt aus: "+string.Join(", ",r.Result.finalTiePool.Select(id=>LastVoteHistory.MapName(r,id))));
        if(r.HistoryTruncated)p.SendLplkChat("[LPLK] Das alte Protokoll ist teilweise unvollständig. Fehlende Angaben werden nicht als bestätigte Stimmen ausgewiesen.");
    }
    private void OpenLastVote(IPlayer p,LastVoteReport r)
    {
        VoteSummary(p,r);
        VoteMenu(p,"Letzte Mapwahl",r,_=>[
            ("Ergebnis: "+EscapeVote(r.Result.winnerName??"keine Map"),a=>VoteSummary(a,r)),
            ("Maps: "+r.Result.counts.Length,a=>VoteMapList(a,r)),
            ("Spieler: "+r.Result.ballots.Length,a=>VotePlayerList(a,r,null)),
            ("Verlauf: "+r.History.Length,a=>VoteHistoryList(a,r)),
            (LastVoteHistory.Time(r.At),a=>VoteSummary(a,r))]);
    }
    private void VoteMapList(IPlayer p,LastVoteReport r)=>VoteMenu(p,"Letzte Mapwahl · Maps",r,_=>
        r.Result.counts.OrderByDescending(c=>LastVoteHistory.Counted(r.Result,c.Id)).Select(c=>(EscapeVote(c.Name)+" · "+LastVoteHistory.Counted(r.Result,c.Id)+" Stimmen"+(c.legal?"":" · ungültig"),(Action<IPlayer>)(a=>VotePlayerList(a,r,c.Id))))
        .Concat(r.Result.extensionVotes>0?[("Map verlängern · "+r.Result.extensionVotes,(Action<IPlayer>)(a=>VotePlayerList(a,r,"$extend")))]:[]).ToArray(),a=>OpenLastVote(a,r));
    private void VotePlayerList(IPlayer p,LastVoteReport r,string? map)=>VoteMenu(p,map is null?"Letzte Mapwahl · Spieler":EscapeVote(LastVoteHistory.MapName(r,map)),r,_=>
        r.Result.ballots.Where(b=>map is null||b.mapId==map).OrderBy(b=>b.actor.Name,StringComparer.CurrentCultureIgnoreCase)
        .Select(b=>(EscapeVote(b.actor.Name)+" → "+EscapeVote(LastVoteHistory.MapName(r,b.mapId))+(b.counted?"":b.excludedReason=="not_recorded"?" · unbelegt":" · nicht gezählt"),(Action<IPlayer>)(a=>
        {a.SendLplkChat($"[LPLK] {b.actor.Name}: {LastVoteHistory.MapName(r,b.mapId)} · "+(b.counted?"gezählt":LastVoteHistory.Exclusion(b.excludedReason)));}))).ToArray(),a=>{if(map is null)OpenLastVote(a,r);else VoteMapList(a,r);});
    private void VoteHistoryList(IPlayer p,LastVoteReport r)=>VoteMenu(p,"Letzte Mapwahl · Verlauf",r,_=>r.History.Select(h=>
        (EscapeVote(h.Actor.Name)+" → "+EscapeVote(h.MapName),(Action<IPlayer>)(a=>a.SendLplkChat($"[LPLK] {LastVoteHistory.Time(h.At)} · {h.Actor.Name}: "+(h.PreviousMapId is null?"Stimme für ":"Wechsel von "+LastVoteHistory.MapName(r,h.PreviousMapId)+" zu ")+h.MapName)))).ToArray(),a=>OpenLastVote(a,r));
}
