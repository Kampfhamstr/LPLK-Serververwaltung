using System.Text.Json;
namespace LPLK_Economy;

public sealed record BettingSettings
{
    public bool Enabled { get; init; }=true;
    public string OpenMode { get; init; }="PlayersPerTeam";
    public int MinHumanPlayersPerTeam { get; init; }=2;
    public int MinBet { get; init; }=100;
    public int MaxBet { get; init; }=5000;
    public bool AllowBetsWholeRound { get; init; }
    public int BetOnlyFirstSeconds { get; init; }=60;
    public bool AllowBetsDuringWarmup { get; init; }
    public bool AllowBetDuringFreezeTime { get; init; }=true;
    public int BetCloseSecondsAfterRoundStart { get; init; }=60;
    public bool RefundOnDrawOrRestart { get; init; }=true;
    public bool RefundIfNoWinningBets { get; init; }=true;
    public int HouseCutPercent { get; init; }
    public bool AnnouncePlacedBets { get; init; }
    public bool AnnounceBettingOnRoundStart { get; init; }
    public bool ShowCenterConfirmation { get; init; }=true;
    public int[] AmountButtons { get; init; }=[100,500,1000,2500,5000];
    public static void Validate(BettingSettings s)
    {
        if(s.OpenMode is not ("PlayersPerTeam" or "Time")||s.MinHumanPlayersPerTeam is <1 or >64||s.MinBet is <1 or >16000||s.MaxBet<s.MinBet||s.MaxBet>16000||s.BetOnlyFirstSeconds is <0 or >3600||s.BetCloseSecondsAfterRoundStart is <0 or >3600||s.HouseCutPercent is <0 or >100||s.AmountButtons is null||s.AmountButtons.Length is <1 or >20||s.AmountButtons.Any(x=>x is <1 or >16000))throw new InvalidDataException("Ungültige Wettregeln.");
    }
}
public readonly record struct BetContext(bool Known,bool Active,bool Warmup,bool Freeze,double Seconds,int T,int CT);
public static class BettingPolicy
{
    public static string? Denied(BettingSettings s,BetContext c)
    {
        if(!s.Enabled)return "Wetten sind ausgeschaltet.";
        if(!c.Known||!c.Active)return "Keine offene Wettrunde.";
        if(c.Warmup&&!s.AllowBetsDuringWarmup)return "Wetten im Warmup gesperrt.";
        if(s.OpenMode=="PlayersPerTeam")return c.T>=s.MinHumanPlayersPerTeam&&c.CT>=s.MinHumanPlayersPerTeam?null:$"Benötigt: {s.MinHumanPlayersPerTeam} lebende menschliche Spieler je Team (T {c.T}, CT {c.CT}).";
        if(c.Seconds<0||!double.IsFinite(c.Seconds))return "Rundenzeit unbekannt.";
        return s.AllowBetsWholeRound||s.BetOnlyFirstSeconds>0&&c.Seconds<=s.BetOnlyFirstSeconds||s.BetOnlyFirstSeconds==0&&(c.Freeze&&s.AllowBetDuringFreezeTime||c.Seconds<=s.BetCloseSecondsAfterRoundStart)?null:"Wettzeit abgelaufen.";
    }
    public static int Side(string value)=>value.Trim().ToLowerInvariant() switch{"t" or "terror" or "terrorist" or "terrorists" or "tt" or "terroristen"=>2,"ct" or "counter" or "counterterrorist" or "counterterrorists"=>3,_=>throw new BankRejected("Team t oder ct wählen.")};
    public static int Amount(string text,int cash,BettingSettings s)=>checked((int)BankPolicy.Amount(text,cash,s.MinBet,text.Equals("all",StringComparison.OrdinalIgnoreCase)?cash:Math.Min(cash,s.MaxBet)));
}
public sealed record BetTicket(string Round,string Account,int Side,long Amount,string Status,long Claim,long Payout);
public sealed record BetRound(string Id,long Period,string Status,int Winner,BettingSettings Rules);

public sealed partial class Ledger
{
    private void UpgradeBettingSchema()=>Atomic(()=>
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS bet_rounds(id TEXT PRIMARY KEY,period INTEGER NOT NULL,status TEXT NOT NULL CHECK(status IN ('open','closing','settled')),winner INTEGER NOT NULL,rules TEXT NOT NULL) STRICT;
            CREATE TABLE IF NOT EXISTS bet_tickets(round TEXT NOT NULL REFERENCES bet_rounds(id),account TEXT NOT NULL REFERENCES accounts(id),side INTEGER NOT NULL CHECK(side IN (2,3)),amount INTEGER NOT NULL CHECK(amount>0 AND amount<=16000),status TEXT NOT NULL CHECK(status IN ('pending','accepted','cancelled','settled')),claim INTEGER NOT NULL CHECK(claim>=0),payout INTEGER NOT NULL CHECK(payout>=0),PRIMARY KEY(round,account)) STRICT;
            CREATE TABLE IF NOT EXISTS bet_cash(id TEXT PRIMARY KEY REFERENCES cash(id),round TEXT NOT NULL REFERENCES bet_rounds(id),kind TEXT NOT NULL CHECK(kind IN ('stake','payout'))) STRICT;
            PRAGMA user_version=2;
            """);return 0;
    });
    public BetRound? BettingRound(string id)
    {
        lock(gate){using var c=Command("SELECT period,status,winner,rules FROM bet_rounds WHERE id=$p0",id);using var r=c.ExecuteReader();return r.Read()?new(id,r.GetInt64(0),r.GetString(1),r.GetInt32(2),JsonSerializer.Deserialize<BettingSettings>(r.GetString(3))!):null;}
    }
    public BetTicket[] BetTickets(string? round=null)
    {
        lock(gate){var result=new List<BetTicket>();using var c=round is null?Command("SELECT round,account,side,amount,status,claim,payout FROM bet_tickets WHERE claim>0 ORDER BY round,account LIMIT 128"):Command("SELECT round,account,side,amount,status,claim,payout FROM bet_tickets WHERE round=$p0 ORDER BY account",round);using var r=c.ExecuteReader();while(r.Read())result.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt64(3),r.GetString(4),r.GetInt64(5),r.GetInt64(6)));return result.ToArray();}
    }
    public int OpenBetRounds { get{lock(gate)return (int)Number("SELECT count(*) FROM bet_rounds WHERE status<>'settled'");} }
    public long BetClaims { get{lock(gate)return Number("SELECT coalesce(sum(claim),0) FROM bet_tickets");} }
    public long PlayerBetClaims(string account){lock(gate)return Number("SELECT coalesce(sum(claim),0) FROM bet_tickets WHERE account=$p0",account);}
    public BetTicket[] BankPayableClaims()
    {
        lock(gate){var result=new List<BetTicket>();using var c=Command("SELECT t.round,t.account,t.side,t.amount,t.status,t.claim,t.payout FROM bet_tickets t JOIN accounts a ON a.id=t.account WHERE t.claim>0 AND a.balance<$p0 AND NOT EXISTS(SELECT 1 FROM cash c WHERE c.account=t.account AND c.status='pending') ORDER BY t.round,t.account LIMIT 128",Limit);using var r=c.ExecuteReader();while(r.Read())result.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt64(3),r.GetString(4),r.GetInt64(5),r.GetInt64(6)));return result.ToArray();}
    }
    public void StartBetRound(string id,BettingSettings rules)=>Atomic(()=>
    {
        BettingSettings.Validate(rules);if(!Key(id,48))throw new BankRejected("Ungültige Wettrunde.");
        if(BettingRound(id) is {} old){if(old.Period!=Period||JsonSerializer.Serialize(old.Rules)!=JsonSerializer.Serialize(rules))throw new BankRejected("Abweichende Wettrunde.");return 0;}
        if(Number("SELECT count(*) FROM bet_rounds WHERE status='open'")>0)throw new BankRejected("Vorherige Wettrunde ist noch offen.");
        Exec("INSERT INTO bet_rounds VALUES($p0,$p1,'open',0,$p2)",id,Period,JsonSerializer.Serialize(rules));return 0;
    });
    public CashIntent PrepareBetStake(string id,string round,string account,string session,int side,int before,int amount,bool allIn=false)=>Atomic(()=>
    {
        var r=BettingRound(round)??throw new BankRejected("Wettrunde fehlt.");
        if(Intent(id) is {} prior){if((State(TitleOwner,"bet-all:"+id).Json=="true")!=allIn||prior.Account!=account||prior.Before!=before||prior.After!=before-amount||prior.Session!=session||Text("SELECT round FROM bet_cash WHERE id=$p0",id)!=round||Text("SELECT kind FROM bet_cash WHERE id=$p0",id)!="stake"||Number("SELECT side FROM bet_tickets WHERE round=$p0 AND account=$p1",round,account)!=side)throw new BankRejected("Abweichender Wetteinsatz.");return prior;}
        if(r.Status!="open"||r.Period!=Period||!Key(id,110)||!BankPolicy.Steam(account)||!Exists(account)||Blocked(account)||session.Length is <1 or >128||side is not (2 or 3)||before is <1 or >16000||amount<r.Rules.MinBet||amount>before)throw new BankRejected("Wetteinsatz nicht freigegeben.");
        if(BetTickets(round).Any(x=>x.Account==account&&x.Status!="cancelled"))throw new BankRejected("Bereits eine feste Wette in dieser Runde.");
        if(BetTickets(round).Length>=64)throw new BankRejected("Wettrunde voll.");
        BookInside(new(id+":prepare",Period,"betting","Wetteinsatz vorgemerkt",[]),false);
        Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,Period,before,before-amount,amount);
        Exec("INSERT INTO bet_cash VALUES($p0,$p1,'stake')",id,round);
        TitleStateInside("bet-all:"+id,allIn);
        Exec("INSERT INTO bet_tickets VALUES($p0,$p1,$p2,$p3,'pending',0,0) ON CONFLICT(round,account) DO UPDATE SET side=excluded.side,amount=excluded.amount,status='pending',claim=0,payout=0",round,account,side,amount);
        return Intent(id)!;
    });
    private Receipt ResolveBetCash(CashIntent i,bool applied,string actor,string round,string kind)
    {
        Posting[] postings=kind=="stake"?(applied?[new("system:cash",-i.Delta),new("system:betpool",i.Delta)]:[]):applied?[new("system:escrow",i.Delta),new("system:cash",-i.Delta)]:[new("system:escrow",i.Delta),new("system:betpool",-i.Delta)];
        var result=BookInside(new(i.Id+":resolve",i.Period,"betting",$"Wettcash {(applied?"bestätigt":"storniert")}: {actor}",postings),true);
        Exec("UPDATE cash SET status=$p1 WHERE id=$p0",i.Id,applied?"committed":"cancelled");
        if(kind=="stake")Exec("UPDATE bet_tickets SET status=$p2 WHERE round=$p0 AND account=$p1",round,i.Account,applied?"accepted":"cancelled");
        else if(!applied)Exec("UPDATE bet_tickets SET claim=claim+$p2 WHERE round=$p0 AND account=$p1",round,i.Account,-i.Delta);
        if(kind=="stake"&&applied)TitleAddInside(i.Account,("BetsPlaced",1),("TotalBetStake",i.Delta),("AllInBets",State(TitleOwner,"bet-all:"+i.Id).Json=="true"?1:0));
        FinalizeBetRound(round);return result;
    }
    public string CashPurpose(string id){lock(gate)return Text("SELECT kind FROM bet_cash WHERE id=$p0",id) switch{"stake"=>"Wetteinsatz","payout"=>"Wettauszahlung",_=>KillCashSpec(id) is not null?"Kill-Belohnung":BountyCashSpec(id) is {} bounty?(bounty.Kind=="stake"?"Kopfgeldeinsatz":"Kopfgeldauszahlung"):BonusCashSpec(id) is {} bonus?(bonus.Kind=="bonus"?"Lotto-Cashbonus":"Rest-Cash-Sammlung"):"Bankcash"};}
    public void CloseBetRound(string id,int winner)=>Atomic(()=>
    {
        var r=BettingRound(id)??throw new BankRejected("Wettrunde fehlt.");
        if(winner is not (0 or 2 or 3 or 9))throw new BankRejected("Ungültiges Rundenresultat.");
        if(r.Status!="open")return 0; // First result wins; callbacks and restarts never settle again.
        Exec("UPDATE bet_rounds SET status='closing',winner=$p1 WHERE id=$p0",id,winner);
        FinalizeBetRound(id);return 0;
    });
    private void FinalizeBetRound(string id)
    {
        var r=BettingRound(id)!;if(r.Status!="closing")return;
        var tickets=BetTickets(id);if(tickets.Any(x=>x.Status=="pending"))return;
        var accepted=tickets.Where(x=>x.Status=="accepted").ToArray();long total=accepted.Sum(x=>x.Amount),winnerPool=accepted.Where(x=>x.Side==r.Winner).Sum(x=>x.Amount),loserPool=total-winnerPool;
        bool refund=r.Winner==9||r.Winner==0&&r.Rules.RefundOnDrawOrRestart||r.Winner is 2 or 3&&(winnerPool==0&&r.Rules.RefundIfNoWinningBets||loserPool==0);
        long distributable=loserPool-(long)decimal.Floor(loserPool*r.Rules.HouseCutPercent/100m),allocated=0;
        foreach(var t in accepted)
        {
            long payout=refund?t.Amount:r.Winner is 2 or 3&&t.Side==r.Winner&&winnerPool>0?t.Amount+(long)decimal.Floor(distributable*t.Amount/(decimal)winnerPool):0;
            allocated+=payout;Exec("UPDATE bet_tickets SET status='settled',claim=$p2,payout=$p2 WHERE round=$p0 AND account=$p1",id,t.Account,payout);
            if(!refund&&r.Winner is 2 or 3)TitleAddInside(t.Account,(t.Side==r.Winner?"BetsWon":"BetsLost",1),("TotalBetPayout",payout),("TotalBetProfit",Math.Max(0,payout-t.Amount)));
        }
        long fee=total-allocated;
        if(accepted.Length>0)BookInside(new("bets:"+id,r.Period,"betting",refund?"Wettrunde erstattet":"Wettrunde abgerechnet",fee>0?[new("system:betpool",-fee),new("system:betfees",fee)]:[]),true);
        Exec("UPDATE bet_rounds SET status='settled' WHERE id=$p0",id);
    }
    public void RecoverBetRounds()
    {
        string[] ids;lock(gate){var list=new List<string>();using var c=Command("SELECT id FROM bet_rounds WHERE status='open'");using var r=c.ExecuteReader();while(r.Read())list.Add(r.GetString(0));ids=list.ToArray();}
        foreach(var id in ids)CloseBetRound(id,0);
    }
    public CashIntent? PayBetClaim(string id,string round,string account,string session,int before,int toCash,long toBank)=>Atomic(()=>
    {
        if(Intent(id) is {} old){if(old.Account!=account||old.Session!=session||old.Before!=before||old.After!=before+toCash||Text("SELECT round FROM bet_cash WHERE id=$p0",id)!=round||Text("SELECT kind FROM bet_cash WHERE id=$p0",id)!="payout"||Number("SELECT coalesce(sum(delta),0) FROM entries WHERE tx=$p0 AND account=$p1",id+":pay",account)!=toBank)throw new BankRejected("Abweichende Wettauszahlung.");return old;}
        if(Text("SELECT id FROM transactions WHERE id=$p0",id+":pay") is not null)throw new BankRejected("Auszahlung bereits verbucht.");
        var r=BettingRound(round)??throw new BankRejected("Wettrunde fehlt.");var t=BetTickets(round).SingleOrDefault(x=>x.Account==account)??throw new BankRejected("Wette fehlt.");
        if(!Key(id,110)||r.Period!=Period||r.Status!="settled"||Blocked(account)||before is <0 or >16000||toCash is <0 or >16000||before+toCash>16000||toBank<0||toBank>t.Claim||toCash+toBank<=0||toCash+toBank>t.Claim||toCash>0&&session.Length is <1 or >128)throw new BankRejected("Auszahlung nicht freigegeben.");
        var posts=new List<Posting>{new("system:betpool",-(toCash+toBank))};if(toBank>0)posts.Add(new(account,toBank));if(toCash>0)posts.Add(new("system:escrow",toCash));
        BookInside(new(id+":pay",Period,"betting","Wettgewinn / Erstattung",posts.ToArray()),false);
        Exec("UPDATE bet_tickets SET claim=claim-$p2 WHERE round=$p0 AND account=$p1",round,account,toCash+toBank);
        if(toCash==0)return null;
        Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,Period,before,before+toCash,-toCash);
        Exec("INSERT INTO bet_cash VALUES($p0,$p1,'payout')",id,round);return Intent(id);
    });
    private void VerifyBetting()
    {
        long expected=Number("SELECT coalesce(sum(CASE WHEN status='accepted' THEN amount ELSE claim END),0) FROM bet_tickets");
        if(Balance("system:betpool")!=expected)throw new InvalidDataException("Wettpool stimmt nicht mit Einsätzen/Auszahlungen überein.");
        if(Number("SELECT count(*) FROM bet_tickets t JOIN bet_rounds r ON r.id=t.round WHERE (t.status IN ('accepted','pending') OR t.claim>0) AND r.period<>$p0",Period)>0)throw new InvalidDataException("Offene Wette aus fremder Bankperiode.");
        if(Number("SELECT count(*) FROM bet_tickets t WHERE t.status='pending' AND (SELECT count(*) FROM bet_cash b JOIN cash c ON b.id=c.id WHERE b.round=t.round AND c.account=t.account AND b.kind='stake' AND c.status='pending')<>1")>0)throw new InvalidDataException("Unklare Wette ohne Cash-Vorgang.");
    }
}

