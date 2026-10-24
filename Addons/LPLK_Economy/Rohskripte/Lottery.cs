using System.Security.Cryptography;
using System.Text.Json;
namespace LPLK_Economy;

public sealed record LotterySettings
{
    public bool PermanentTicketUpgradeDiscountEnabled {get;init;}=true;
    public int PermanentTicketUpgradeDiscountChanceBasisPoints {get;init;}=100;
    public int PermanentTicketUpgradeDiscountMinPercent {get;init;}=20;
    public int PermanentTicketUpgradeDiscountMaxPercent {get;init;}=30;
    public bool PermanentTicketUpgradeDiscountAnnounce {get;init;}=true;
    public CashCollectionSettings CashToSuperpot {get;init;}=new();
    public bool Enabled { get; init; }=true;
    public bool ResetSuperpotWithBankReset { get; init; }=true;
    public int MinActivePlayersForLotto { get; init; }=2;
    public long TicketPrice { get; init; }=500;
    public int MaxSingleTicketsPerPlayerPerDraw { get; init; }=10;
    public int MaxPermanentTicketsPerPlayer { get; init; }=5;
    public int MaxPermanentTicketsTotal { get; init; }=10;
    public bool PermanentUnlocksEnabled { get; init; }=true;
    public long[] PermanentUnlockTicketThresholds { get; init; }=[1000,10000,25000,50000,100000];
    public bool CountPermanentAutoTicketsForUnlocks { get; init; }=true;
    public bool ResetPermanentUnlocksWithBankReset { get; init; }=true;
    public bool AnnouncePermanentUnlocks { get; init; }=true;
    public int[] TicketAmountButtons { get; init; }=[1,2,5,10];
    public bool AllowBuyDuringWarmup { get; init; }
    public bool DrawOnValidRoundEnd { get; init; }=true;
    public int RoundPrizePercent { get; init; }=80;
    public int SuperpotPercent { get; init; }=10;
    public int HouseCutPercent { get; init; }=10;
    public bool JackpotEnabled { get; init; }=true;
    public int JackpotChanceBasisPoints { get; init; }=200;
    public long MinSuperpotForJackpot { get; init; }=5000;
    public bool AnnounceOnRoundStart { get; init; }=true;
    public bool AnnounceDrawResult { get; init; }=true;
    public bool RefundOnInvalidRoundEnd { get; init; }=true;
    public bool ProcessEconomyOnRoundEndWithoutWinner { get; init; }=true;
    public int MinActivePlayersForEconomyRoundEnd { get; init; }=2;
    public static void Validate(LotterySettings s)
    {
        BonusPolicy.Validate(s);
        if(s.MinActivePlayersForLotto is <0 or >64||s.MinActivePlayersForEconomyRoundEnd is <0 or >64||s.TicketPrice is <1 or >1_000_000_000||s.MaxSingleTicketsPerPlayerPerDraw is <1 or >1000||s.MaxPermanentTicketsPerPlayer is <0 or >1000||s.MaxPermanentTicketsTotal is < -1 or >1000||s.PermanentUnlockTicketThresholds is null||s.PermanentUnlockTicketThresholds.Length>100||s.PermanentUnlockTicketThresholds.Any(v=>v<1)||!s.PermanentUnlockTicketThresholds.SequenceEqual(s.PermanentUnlockTicketThresholds.Distinct().Order())||s.TicketAmountButtons is null||s.TicketAmountButtons.Length is <1 or >20||s.TicketAmountButtons.Any(v=>v is <1 or >1000)||s.RoundPrizePercent is <0 or >100||s.SuperpotPercent is <0 or >100||s.HouseCutPercent is <0 or >100||s.RoundPrizePercent+s.SuperpotPercent+s.HouseCutPercent!=100||s.JackpotChanceBasisPoints is <0 or >10000||s.MinSuperpotForJackpot is <0 or >9_000_000_000_000_000)throw new InvalidDataException("Ungültige Lotto-Regeln.");
    }
    public int PermanentLimit(long bought)=>Math.Min(MaxPermanentTicketsTotal<0?int.MaxValue:MaxPermanentTicketsTotal,MaxPermanentTicketsPerPlayer+(PermanentUnlocksEnabled?PermanentUnlockTicketThresholds.Count(v=>bought>=v):0));
}
public static class LotteryPolicy
{
    public static string? Denied(LotterySettings s,bool known,bool open,bool warmup,int players)=>!s.Enabled?"Lotto ist ausgeschaltet.":!known||!open?"Keine offene Lotto-Runde.":warmup&&!s.AllowBuyDuringWarmup?"Lotto im Warmup gesperrt.":players<s.MinActivePlayersForLotto?$"Lotto benötigt {s.MinActivePlayersForLotto} aktive Menschen (aktuell {players}).":null;
    public static bool DrawAllowed(LotterySettings s,bool warmup,int players,int winner)=>!warmup&&s.DrawOnValidRoundEnd&&players>=s.MinActivePlayersForLotto&&(winner is 2 or 3||s.ProcessEconomyOnRoundEndWithoutWinner&&players>=s.MinActivePlayersForEconomyRoundEnd);
}
public sealed record LotteryRound(string Id,long Period,string Status,LotterySettings Rules,string Winner,long Prize,long Jackpot,long Overflow,int Roll,int JackpotRoll);
public sealed record LotteryTicket(string Id,string Round,string Account,int Count,bool Automatic,long Cost);
public sealed record LotterySubscription(int Count,long Bought,long Revision);

public sealed partial class Ledger
{
    private void UpgradeLotterySchema()=>Atomic(()=>
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS lot_rounds(id TEXT PRIMARY KEY,period INTEGER NOT NULL,status TEXT NOT NULL CHECK(status IN ('open','closing','drawn','refunded','forfeited')),rules TEXT NOT NULL,winner TEXT NOT NULL,prize INTEGER NOT NULL,jackpot INTEGER NOT NULL,overflow INTEGER NOT NULL,roll INTEGER NOT NULL,jackpot_roll INTEGER NOT NULL) STRICT;
            CREATE TABLE IF NOT EXISTS lot_tickets(id TEXT PRIMARY KEY,round TEXT NOT NULL REFERENCES lot_rounds(id),account TEXT NOT NULL REFERENCES accounts(id),count INTEGER NOT NULL CHECK(count>0),automatic INTEGER NOT NULL CHECK(automatic IN (0,1)),cost INTEGER NOT NULL CHECK(cost>0)) STRICT;
            CREATE INDEX IF NOT EXISTS lot_tickets_round ON lot_tickets(round,account);
            CREATE TABLE IF NOT EXISTS lot_subscriptions(account TEXT PRIMARY KEY REFERENCES accounts(id),count INTEGER NOT NULL CHECK(count>=0),bought INTEGER NOT NULL CHECK(bought>=0),revision INTEGER NOT NULL CHECK(revision>=0)) STRICT;
            CREATE TABLE IF NOT EXISTS lot_refunds(round TEXT NOT NULL REFERENCES lot_rounds(id),account TEXT NOT NULL REFERENCES accounts(id),claim INTEGER NOT NULL CHECK(claim>=0),PRIMARY KEY(round,account)) STRICT;
            PRAGMA user_version=3;
            """);return 0;
    });
    public long Superpot=>Balance("system:superpot");
    public long LotteryRefunds { get{lock(gate)return Number("SELECT coalesce(sum(claim),0) FROM lot_refunds");} }
    public int OpenLotteryRounds { get{lock(gate)return (int)Number("SELECT count(*) FROM lot_rounds WHERE status IN ('open','closing')");} }
    public LotteryRound? GetLotteryRound(string id)
    {
        lock(gate){using var c=Command("SELECT period,status,rules,winner,prize,jackpot,overflow,roll,jackpot_roll FROM lot_rounds WHERE id=$p0",id);using var r=c.ExecuteReader();return r.Read()?new(id,r.GetInt64(0),r.GetString(1),JsonSerializer.Deserialize<LotterySettings>(r.GetString(2))!,r.GetString(3),r.GetInt64(4),r.GetInt64(5),r.GetInt64(6),r.GetInt32(7),r.GetInt32(8)):null;}
    }
    public LotteryTicket[] LotteryTickets(string round)
    {
        lock(gate){var list=new List<LotteryTicket>();using var c=Command("SELECT id,account,count,automatic,cost FROM lot_tickets WHERE round=$p0 ORDER BY id",round);using var r=c.ExecuteReader();while(r.Read())list.Add(new(r.GetString(0),round,r.GetString(1),r.GetInt32(2),r.GetInt32(3)==1,r.GetInt64(4)));return list.ToArray();}
    }
    public LotterySubscription LotteryPlan(string account)
    {
        lock(gate){using var c=Command("SELECT count,bought,revision FROM lot_subscriptions WHERE account=$p0",account);using var r=c.ExecuteReader();return r.Read()?new(r.GetInt32(0),r.GetInt64(1),r.GetInt64(2)):new(0,0,0);}
    }
    public Receipt SetLotteryPlan(string id,long period,string account,int count,long revision,LotterySettings rules)=>Atomic(()=>
    {
        LotterySettings.Validate(rules);var prior=LotteryPlan(account);
        var request=new Booking(id,period,"lottery",$"Dauerlose {account}: {count}, Revision {revision}",[]);
        if(Text("SELECT id FROM transactions WHERE id=$p0",TransactionId(id)) is not null)return BookInside(request,false);
        if(!BankPolicy.Steam(account)||!Exists(account)||count<0||count>PermanentLotteryLimit(account,rules,prior.Bought)||prior.Revision!=revision)throw new BankRejected("Dauerlosansicht veraltet oder Limit erreicht.");
        var result=BookInside(request,false);
        if(count>prior.Count)TitleAddInside(account,("PermanentLottoTicketsActivated",count-prior.Count));
        Exec("INSERT INTO lot_subscriptions VALUES($p0,$p1,0,1) ON CONFLICT(account) DO UPDATE SET count=excluded.count,revision=revision+1",account,count);return result;
    });
    public void StartLotteryRound(string id,LotterySettings rules)=>Atomic(()=>
    {
        LotterySettings.Validate(rules);if(!Key(id,48))throw new BankRejected("Ungültige Lotto-Runde.");
        if(GetLotteryRound(id) is {} old){if(old.Period!=Period||JsonSerializer.Serialize(old.Rules)!=JsonSerializer.Serialize(rules))throw new BankRejected("Abweichende Lotto-Runde.");return 0;}
        if(Number("SELECT count(*) FROM lot_rounds WHERE status='open'")>0)throw new BankRejected("Vorherige Lotto-Runde noch offen.");
        Exec("INSERT INTO lot_rounds VALUES($p0,$p1,'open',$p2,'',0,0,0,-1,-1)",id,Period,JsonSerializer.Serialize(rules));return 0;
    });
    public LotteryTicket? BuyLottery(string id,string round,string account,int count,bool automatic=false,Func<int,int>? random=null)=>Atomic(()=>
    {
        if(id.StartsWith("gift:",StringComparison.Ordinal))throw new BankRejected("Reservierter Losbezeichner.");
        var r=GetLotteryRound(round)??throw new BankRejected("Lotto-Runde fehlt.");
        var tickets=LotteryTickets(round);var old=tickets.FirstOrDefault(t=>t.Id==id);
        if(old is not null){if(r.Period!=Period||old.Account!=account||old.Automatic!=automatic||!automatic&&old.Count!=count)throw new BankRejected("Abweichender Loskauf.");return old;}
        if(r.Period!=Period||r.Status!="open"||!Key(id,110)||!BankPolicy.Steam(account)||!Exists(account)||Blocked(account))throw new BankRejected("Loskauf nicht freigegeben.");
        var plan=LotteryPlan(account);
        if(automatic)
        {
            if(tickets.Any(t=>t.Account==account&&t.Automatic))return null;
            count=(int)Math.Min(Math.Min(plan.Count,PermanentLotteryLimit(account,r.Rules,plan.Bought)),Balance(account)/r.Rules.TicketPrice);
            if(count==0)return null;
        }
        if(count<1||count>1000+100||!automatic&&tickets.Where(t=>t.Account==account&&!t.Automatic&&!IsGift(t)).Sum(t=>t.Count)+count>r.Rules.MaxSingleTicketsPerPlayerPerDraw||tickets.Select(t=>t.Account).Distinct().Count()>=64&&!tickets.Any(t=>t.Account==account))throw new BankRejected("Loslimit erreicht.");
        long cost=checked(count*r.Rules.TicketPrice);
        BookInside(new(id,Period,"lottery",automatic?"Lotto-Dauerlose gekauft":"Lotto-Lose gekauft",[new(account,-cost),new("system:lotpool",cost)]),false);
        Exec("INSERT INTO lot_tickets VALUES($p0,$p1,$p2,$p3,$p4,$p5)",id,round,account,count,automatic?1:0,cost);
        TitleAddInside(account,("LottoTicketsBought",count));
        long progress=r.Rules.PermanentUnlocksEnabled&&(!automatic||r.Rules.CountPermanentAutoTicketsForUnlocks)?count:0;
        Exec("INSERT INTO lot_subscriptions VALUES($p0,0,$p1,1) ON CONFLICT(account) DO UPDATE SET bought=bought+$p1,revision=revision+1",account,progress);
        if(automatic)GrantDiscountInside(account,count,r.Rules,random);
        return new LotteryTicket(id,round,account,count,automatic,cost);
    });
    public void CloseLotteryRound(string id,bool valid,Func<int,int>? random=null)
    {
        Atomic(()=>
        {
            var r=GetLotteryRound(id)??throw new BankRejected("Lotto-Runde fehlt.");if(r.Status!="open")return 0;
            int count=LotteryTickets(id).Sum(t=>t.Count),roll=-1,jackpotRoll=-1;
            if(valid&&count>0){roll=(random??RandomNumberGenerator.GetInt32)(count);jackpotRoll=(random??RandomNumberGenerator.GetInt32)(10000);if(roll<0||roll>=count||jackpotRoll<0||jackpotRoll>=10000)throw new BankRejected("Ungültige Ziehung.");}
            Exec("UPDATE lot_rounds SET status='closing',roll=$p1,jackpot_roll=$p2 WHERE id=$p0",id,roll,jackpotRoll);return 0;
        });
        FinalizeLotteryRound(id);
    }
    private void FinalizeLotteryRound(string id)=>Atomic(()=>
    {
        var r=GetLotteryRound(id)!;if(r.Status!="closing")return 0;
        var tickets=LotteryTickets(id);long pot=tickets.Sum(t=>t.Cost);
        if(r.Roll<0)
        {
            if(pot>0)
            {
                long gifts=tickets.Where(IsGift).Sum(t=>t.Cost);
                BookInside(new("lotend:"+id,Period,"lottery",r.Rules.RefundOnInvalidRoundEnd?"Lotto-Runde erstattet":"Lotto-Runde ohne Erstattung beendet",[new("system:lotpool",-pot),new("system:lotbonus",gifts),new(r.Rules.RefundOnInvalidRoundEnd?"system:lotrefunds":"system:lotfees",pot-gifts)]),true);
                if(r.Rules.RefundOnInvalidRoundEnd)foreach(var g in tickets.Where(t=>!IsGift(t)).GroupBy(t=>t.Account))Exec("INSERT INTO lot_refunds VALUES($p0,$p1,$p2)",id,g.Key,g.Sum(t=>t.Cost));
            }
            Exec("UPDATE lot_rounds SET status=$p1 WHERE id=$p0",id,r.Rules.RefundOnInvalidRoundEnd?"refunded":"forfeited");return 0;
        }
        int cursor=r.Roll;var winner=tickets.First(t=>{if(cursor<t.Count)return true;cursor-=t.Count;return false;}).Account;
        if(Blocked(winner))return 0; // Persist the chosen roll; never reroll while a cash intent is unresolved.
        long contribution=(long)decimal.Floor(pot*r.Rules.SuperpotPercent/100m),fee=(long)decimal.Floor(pot*r.Rules.HouseCutPercent/100m),prize=pot-contribution-fee;
        long paid=Math.Min(prize,Math.Max(0,Limit-Balance(winner))),overflow=prize-paid;
        long available=checked(Superpot+contribution+overflow),jackpot=0,jackpotOverflow=0;
        if(r.Rules.JackpotEnabled&&available>=r.Rules.MinSuperpotForJackpot&&r.JackpotRoll<r.Rules.JackpotChanceBasisPoints){jackpot=Math.Min(available,Math.Max(0,Limit-Balance(winner)-paid));jackpotOverflow=available-jackpot;}
        BookInside(new("lotend:"+id,Period,"lottery","Lotto-Ziehung / Bankgewinn",[new("system:lotpool",-pot),new("system:lotfees",fee),new("system:superpot",contribution+overflow-jackpot),new(winner,paid+jackpot)]),false);
        TitleAddInside(winner,("LottoWins",1),("TotalLottoWon",paid+jackpot),("LottoJackpotsWon",r.Rules.JackpotEnabled&&available>=r.Rules.MinSuperpotForJackpot&&r.JackpotRoll<r.Rules.JackpotChanceBasisPoints?1:0));
        Exec("UPDATE lot_rounds SET status='drawn',winner=$p1,prize=$p2,jackpot=$p3,overflow=$p4 WHERE id=$p0",id,winner,paid,jackpot,overflow+jackpotOverflow);return 0;
    });
    public void ResumeLottery(bool recoverOpen=false)
    {
        string[] ids;lock(gate){var list=new List<string>();using var c=Command(recoverOpen?"SELECT id FROM lot_rounds WHERE status IN ('open','closing')":"SELECT id FROM lot_rounds WHERE status='closing'");using var r=c.ExecuteReader();while(r.Read())list.Add(r.GetString(0));ids=list.ToArray();}
        foreach(var id in ids){if(GetLotteryRound(id)!.Status=="open")CloseLotteryRound(id,false);else FinalizeLotteryRound(id);}
        PayLotteryRefunds();
    }
    public void PayLotteryRefunds()=>Atomic(()=>
    {
        var claims=new List<(string Round,string Account,long Amount)>();
        using(var c=Command("SELECT r.round,r.account,r.claim FROM lot_refunds r JOIN accounts a ON a.id=r.account WHERE r.claim>0 AND a.balance<$p0 AND NOT EXISTS(SELECT 1 FROM cash c WHERE c.account=r.account AND c.status='pending') ORDER BY r.round,r.account LIMIT 128",Limit))using(var rows=c.ExecuteReader())while(rows.Read())claims.Add((rows.GetString(0),rows.GetString(1),rows.GetInt64(2)));
        foreach(var c in claims)
        {
            long amount=Math.Min(c.Amount,Math.Max(0,Limit-Balance(c.Account)));if(amount==0)continue;
            BookInside(new("lotrefund:"+Guid.NewGuid().ToString("N"),Period,"lottery","Lotto-Erstattung",[new("system:lotrefunds",-amount),new(c.Account,amount)]),false);
            Exec("UPDATE lot_refunds SET claim=claim-$p2 WHERE round=$p0 AND account=$p1",c.Round,c.Account,amount);
        }return 0;
    });
    private void ResetLotteryInside(string id,LotterySettings rules)
    {
        if(Number("SELECT count(*) FROM lot_tickets t JOIN lot_rounds r ON r.id=t.round WHERE r.status IN ('open','closing')")>0||LotteryRefunds>0)throw new BankRejected("Vor Bankreset offene Lotto-Lose und Erstattungen abschließen.");
        Exec("UPDATE lot_rounds SET status='refunded' WHERE status IN ('open','closing')");
        if(rules.ResetSuperpotWithBankReset&&Superpot>0)BookInside(new(id+":superpot",Period,"lottery","Superpot durch Bankreset zurückgesetzt",[new("system:superpot",-Superpot),new("system:reset",Superpot)]),false);
        if(rules.ResetPermanentUnlocksWithBankReset)Exec("UPDATE lot_subscriptions SET bought=0,count=min(count,$p0),revision=revision+1",rules.PermanentLimit(0));
        else Exec("UPDATE lot_subscriptions SET revision=revision+1");
    }
    private void VerifyLottery()
    {
        if(Balance("system:lotpool")!=Number("SELECT coalesce(sum(t.cost),0) FROM lot_tickets t JOIN lot_rounds r ON r.id=t.round WHERE r.status IN ('open','closing')")||Balance("system:lotrefunds")!=LotteryRefunds||Superpot<0)throw new InvalidDataException("Lotto-Pool oder Erstattungen widersprüchlich.");
        if(Number("SELECT count(*) FROM lot_rounds WHERE status IN ('open','closing') AND period<>$p0",Period)>0||Number("SELECT count(*) FROM lot_refunds f JOIN lot_rounds r ON f.round=r.id WHERE f.claim>0 AND r.period<>$p0",Period)>0)throw new InvalidDataException("Offenes Lotto aus fremder Bankperiode.");
    }
}
