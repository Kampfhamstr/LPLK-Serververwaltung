using System.Text.Json;
namespace LPLK_Economy;
public sealed record TitleProgress
{
    public string Since {get;init;}=DateTimeOffset.UtcNow.ToString("O");
    public Dictionary<string,long> Values {get;init;}=[];
    public Dictionary<string,long> Reached {get;init;}=[];
    public string Active {get;init;}="";
}
public sealed record RoundPayment(string Account,long Amount,long Superpot,int BasisPoints);
public sealed partial class Ledger
{
    private const string TitleOwner="LPLK_Economy.Titles";
    private EconomySettings titleRules=new();
    private void TitleStateInside(string key,object value)=>Exec("INSERT INTO module_state VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,TitleOwner,key,JsonSerializer.Serialize(value));
    public TitleProgress TitlesProgress(string account)
    {
        lock(gate){string? json=Text("SELECT json FROM module_state WHERE period=$p0 AND owner=$p1 AND key=$p2",Period,TitleOwner,"player:"+account);return json is null?new():JsonSerializer.Deserialize<TitleProgress>(json)??throw new InvalidDataException("Titelstand beschädigt.");}
    }
    private void TitleAddInside(string account,params (string Metric,long Delta)[] changes)
    {
        if(!titleRules.TitleSystem.Enabled||!BankPolicy.Steam(account))return;
        var p=TitlesProgress(account);long reached=Number("SELECT coalesce(max(rowid),0) FROM transactions");
        foreach(var (metric,delta) in changes){if(delta<0)throw new InvalidDataException("Negativer Titelfortschritt.");if(delta==0)continue;p.Values[metric]=checked(p.Values.GetValueOrDefault(metric)+delta);p.Reached[metric]=reached;}
        TitleStateInside("player:"+account,p);
        fault?.Invoke("after-title-progress");
    }
    private void TitleWealthInside(string account,bool pioneer=true)
    {
        if(!titleRules.TitleSystem.Enabled||!BankPolicy.Steam(account))return;
        var p=TitlesProgress(account);long value=Balance(account),seq=Number("SELECT coalesce(max(rowid),0) FROM transactions"),previousHigh=p.Values.GetValueOrDefault("HighestBank");
        if(!p.Values.ContainsKey("CurrentBank")||p.Values["CurrentBank"]!=value){p.Values["CurrentBank"]=value;p.Reached["CurrentBank"]=seq;}
        if(value>p.Values.GetValueOrDefault("HighestBank")){p.Values["HighestBank"]=value;p.Reached["HighestBank"]=seq;}
        TitleStateInside("player:"+account,p);
        foreach(var t in TitleCatalog.All.Where(t=>pioneer&&t.Metric=="Pioneer"&&value>=t.Required&&previousHigh<t.Required))
            Exec("INSERT OR IGNORE INTO module_state VALUES($p0,$p1,$p2,1,$p3)",Period,TitleOwner,"pioneer:"+t.Id,JsonSerializer.Serialize(account));
    }
    public void ObserveTitles(string account)=>Atomic(()=>{if(!BankPolicy.Steam(account)||!Exists(account))throw new BankRejected("Konto fehlt.");TitleWealthInside(account,false);return 0;});
    public Dictionary<string,string> PioneerOwners()
    {
        lock(gate){var result=new Dictionary<string,string>();using var c=Command("SELECT key,json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'pioneer:*'",Period,TitleOwner);using var r=c.ExecuteReader();while(r.Read())result[r.GetString(0)[8..]]=JsonSerializer.Deserialize<string>(r.GetString(1))!;return result;}
    }
    public EconomyTitle[] OwnedTitles(string account)
    {
        lock(gate)
        {
            if(!titleRules.TitleSystem.Enabled)return [];
            var own=TitlesProgress(account);var peers=new Dictionary<string,TitleProgress>();
            using(var c=Command("SELECT key,json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'player:*'",Period,TitleOwner))using(var r=c.ExecuteReader())while(r.Read())peers[r.GetString(0)[7..]]=JsonSerializer.Deserialize<TitleProgress>(r.GetString(1))!;
            var pioneers=PioneerOwners();
            bool Leader(string metric)
            {
                long score=metric=="CurrentBank"?Balance(account):own.Values.GetValueOrDefault(metric);if(score<=0)return false;
                long reached=own.Reached.GetValueOrDefault(metric);
                return !peers.Any(x=>x.Key!=account&&(x.Value.Values.GetValueOrDefault(metric)>score||x.Value.Values.GetValueOrDefault(metric)==score&&(x.Value.Reached.GetValueOrDefault(metric)<reached||x.Value.Reached.GetValueOrDefault(metric)==reached&&string.CompareOrdinal(x.Key,account)<0)));
            }
            return TitleCatalog.All.Where(t=>t.Metric=="Pioneer"?pioneers.GetValueOrDefault(t.Id)==account:t.Metric.StartsWith("Ruler:")?Leader(t.Metric[6..]):t.Metric=="RobberSkill"?Security(account,robberyRules,DateTimeOffset.UtcNow.ToUnixTimeSeconds()).RobberSkill>=t.Required:own.Values.GetValueOrDefault(t.Metric)>=t.Required).ToArray();
        }
    }
    public void SelectTitle(string id,long period,string account,string title)=>Atomic(()=>
    {
        if(!titleRules.TitleSystem.Enabled||period!=Period||!Exists(account)||!BankPolicy.Steam(account))throw new BankRejected("Titelansicht nicht verfügbar oder veraltet.");
        var request=new Booking(id,period,TitleOwner,"Titelwahl "+account+" "+title,[]);
        if(Text("SELECT id FROM transactions WHERE id=$p0",id)is not null){BookInside(request,false);return 0;}
        if(title!=""&&!OwnedTitles(account).Any(t=>t.Id==title))throw new BankRejected("Diesen Titel besitzt du aktuell nicht.");
        BookInside(request,false);TitleStateInside("player:"+account,TitlesProgress(account) with{Active=title});return 0;
    });
    public string ActiveTitle(string account)=>OwnedTitles(account).FirstOrDefault(t=>t.Id==TitlesProgress(account).Active)?.Text??"";
    public int RulerSlots(string account)=>titleRules.TitleSystem.Enabled?OwnedTitles(account).Count(t=>t.Category=="Herrscher")*titleRules.TitleSystem.PermanentTicketsPerRulerTitle:0;
    public int PermanentLotteryLimit(string account,LotterySettings s,long bought)=>Math.Min(s.MaxPermanentTicketsTotal<0?int.MaxValue:s.MaxPermanentTicketsTotal,checked(s.MaxPermanentTicketsPerPlayer+(s.PermanentUnlocksEnabled?s.PermanentUnlockTicketThresholds.Count(v=>bought>=v):0)+RulerSlots(account)));
    public int TitleInterestRate(string account)
    {
        if(!titleRules.TitleSystem.Enabled||!titleRules.InterestSystem.Enabled)return 0;
        var owned=OwnedTitles(account);int rulers=owned.Count(t=>t.Category=="Herrscher");return TitlePolicy.InterestRate(titleRules.InterestSystem,owned.Length-rulers,rulers);
    }
    // Freeze all calculations before any account changes. One durable receipt per
    // round prevents repeat interest/tax, including a replay after process failure.
    public RoundPayment[] ApplyTitleRound(string id,long period,string[] online,bool tax,int? activePlayers=null)=>Atomic(()=>
    {
        if(!Key(id,80)||period!=Period||online.Length>64||online.Any(a=>!BankPolicy.Steam(a))||online.Distinct().Count()!=online.Length)throw new BankRejected("Ungültige Wirtschafts-Runde.");
        var prior=State(TitleOwner,"round:"+id);if(prior.Json is not null){var saved=JsonSerializer.Deserialize<SavedTitleRound>(prior.Json)!;if(saved.Tax!=tax||!saved.Players.SequenceEqual(online.Order()))throw new BankRejected("Abweichende Wirtschafts-Runde.");return [];}
        var s=titleRules;var i=s.InterestSystem;var x=s.LuxuryTax;
        int count=activePlayers??online.Length;if(count<online.Length||count>64)throw new BankRejected("Ungültige Teilnehmerzahl.");
        if(!s.Enabled||!(tax?x.Enabled&&x.CollectOnRoundStart:s.TitleSystem.Enabled&&i.Enabled&&i.PayOnValidRoundEnd)||count<(tax?x.MinActivePlayersForLuxuryTax:i.MinActivePlayersForInterest))return [];
        var payments=online.Where(a=>Exists(a)&&!Blocked(a)).Order().Select(a=>
        {
            long balance=Balance(a);int rate=tax?TitlePolicy.TaxRate(x,balance):TitleInterestRate(a);
            long amount=RobberyPolicy.Percent(balance,rate,10000);
            if(!tax){if(balance<i.MinBankForInterest)amount=0;if(i.MaxInterestPerPlayer>=0)amount=Math.Min(amount,i.MaxInterestPerPlayer);amount=Math.Min(amount,Math.Max(0,Limit-balance));}
            return new RoundPayment(a,amount,tax?RobberyPolicy.Percent(amount,x.SuperpotShareBasisPoints,10000):0,rate);
        }).Where(p=>p.Amount>0).ToArray();
        foreach(var p in payments)BookInside(new("title-round:"+id+":"+p.Account,period,TitleOwner,tax?"Luxussteuer":"Titelzinsen",tax?[new(p.Account,-p.Amount),new("system:superpot",p.Superpot),new("system:tax",p.Amount-p.Superpot)]:[new(p.Account,p.Amount),new("system:interest",-p.Amount)]),false);
        BookInside(new("title-round:"+id,period,TitleOwner,tax?"Steuerrunde abgeschlossen":"Zinsrunde abgeschlossen",[],[new("round:"+id,0,JsonSerializer.Serialize(new SavedTitleRound(tax,online.Order().ToArray(),payments)))]),false);
        fault?.Invoke("after-title-round");return payments;
    });
    private sealed record SavedTitleRound(bool Tax,string[] Players,RoundPayment[] Payments);
    private void VerifyTitles()
    {
        using var c=Command("SELECT key,json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'player:*'",Period,TitleOwner);using var r=c.ExecuteReader();
        while(r.Read())
        {
            var p=JsonSerializer.Deserialize<TitleProgress>(r.GetString(1));
            if(!BankPolicy.Steam(r.GetString(0)[7..])||p is null||p.Values is null||p.Reached is null||p.Values.Count>100||p.Reached.Count>100||p.Values.Any(x=>!Key(x.Key,64)||x.Value<0)||p.Reached.Any(x=>!Key(x.Key,64)||x.Value<0)||p.Active is null||p.Active!=""&&!TitleCatalog.All.Any(t=>t.Id==p.Active)||!DateTimeOffset.TryParse(p.Since,out _))throw new InvalidDataException("Titelstand beschädigt; Daten bleiben erhalten.");
        }
    }
}
