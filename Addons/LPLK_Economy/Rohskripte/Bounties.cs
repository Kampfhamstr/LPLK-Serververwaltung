using System.Text.Json;
namespace LPLK_Economy;

public sealed record BountyOrder(string Id,long Period,string Sponsor,string Target,string SponsorName,string TargetName,long Amount,string Source,bool Paid,string Status,string Session="",string RefundReason="");
public sealed record BountyClaim(string Id,string Account,long Amount,long Remaining,string Mode,string Kind,string TargetName);
public sealed record BountyCash(string Kind,string Reference,long Amount);
public sealed record BountyPayment(string Claim,string Account,string Session,int Before,int Cash,long Bank,long Discard);
public sealed partial class Ledger
{
    private const string BountyOwner="LPLK_Economy.Bounty";
    private T? BountyState<T>(string key) where T:class=>State(BountyOwner,key).Json is {} json?JsonSerializer.Deserialize<T>(json)??throw new InvalidDataException("Kopfgeldzustand beschädigt."):null;
    private void BountyStateInside(string key,object value)=>Exec("INSERT INTO module_state VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,BountyOwner,key,JsonSerializer.Serialize(value));
    private T[] BountyStates<T>(string prefix) where T:class
    {
        var rows=new List<T>();using var c=Command("SELECT json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB $p2 ORDER BY rowid",Period,BountyOwner,prefix+"*");using var r=c.ExecuteReader();while(r.Read())rows.Add(JsonSerializer.Deserialize<T>(r.GetString(0))??throw new InvalidDataException("Kopfgeldzustand beschädigt."));return rows.ToArray();
    }
    public BountyOrder? GetBounty(string id){lock(gate)return BountyState<BountyOrder>("order:"+id);}
    public BountyOrder[] Bounties(bool openOnly=true){lock(gate)return BountyStates<BountyOrder>("order:").Where(o=>!openOnly||o.Status is "active" or "pending").ToArray();}
    public BountyClaim[] BountyClaims(){lock(gate)return BountyStates<BountyClaim>("claim:").Where(c=>c.Remaining>0).ToArray();}
    public long BountyTotal(string target){lock(gate)return Bounties().Where(o=>o.Target==target&&o.Status=="active").Sum(o=>o.Amount);}
    public BountyOrder PlaceBounty(string id,long period,string sponsor,string target,long amount,string source,BountySettings rules,bool percent=false,string session="",int before=0)=>Atomic(()=>
    {
        if(!Key(id,72)||period!=Period||!BankPolicy.Steam(sponsor)||!BankPolicy.Steam(target)||!Exists(sponsor)||!Exists(target)||amount<rules.MinBounty||amount>BountyPolicy.Maximum(rules,int.MaxValue,percent)||source is not ("Bank" or "Cash")||rules.BlockSelfBounty&&sponsor==target||percent&&(source!="Bank"||!rules.AllowBankPercentBounty))throw new BankRejected("Ungültiger oder veralteter Kopfgeldauftrag.");
        if(GetBounty(id) is {} old){if(old.Period!=period||old.Sponsor!=sponsor||old.Target!=target||old.Amount!=amount||old.Source!=source||old.Paid!=rules.TakeMoneyImmediately||old.Session!=session||source=="Cash"&&old.Paid&&Intent(id)?.Before!=before)throw new BankRejected("Abweichende Kopfgeld-Wiederholung.");return old;}
        if(!titleRules.Enabled||!titleRules.BankEnabled||!rules.Enabled||source=="Bank"&&!rules.AllowBankFunding||source=="Cash"&&!rules.AllowCashFunding||Blocked(sponsor))throw new BankRejected("Kopfgeldquelle nicht freigegeben oder Cashprüfung offen.");
        if(!rules.AllowStacking&&Bounties().Any(o=>o.Target==target))throw new BankRejected("Auf dieses Ziel besteht bereits ein Kopfgeld.");
        if(source=="Bank"&&Balance(sponsor)<amount||source=="Cash"&&(before<amount||before>titleRules.MaxPlayerCash||session.Length is <1 or >128))throw new BankRejected("Guthaben reicht nicht oder Cash ist unklar.");
        var order=new BountyOrder(id,period,sponsor,target,Text("SELECT name FROM accounts WHERE id=$p0",sponsor)!,Text("SELECT name FROM accounts WHERE id=$p0",target)!,amount,source,rules.TakeMoneyImmediately,source=="Cash"&&rules.TakeMoneyImmediately?"pending":"active",session);
        if(order.Status=="pending")
        {
            BookInside(new(id+":prepare",period,BountyOwner,"Cash-Kopfgeld vorgemerkt",[]),false);
            BountyStateInside("cash:"+id,new BountyCash("stake",id,amount));
            Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,sponsor,session,period,before,checked(before-(int)amount),amount);
        }
        else
        {
            BookInside(new(id+":fund",period,BountyOwner,"Kopfgeld finanziert",[new(order.Paid?sponsor:"system:bountygrant",-amount),new("system:bountypool",amount)]),false);
            TitleAddInside(sponsor,("BountiesSet",1),("TotalBountySet",amount));
        }
        BountyStateInside("order:"+id,order);fault?.Invoke("after-bounty-funded");return order;
    });
    private void NewBountyClaimInside(string id,string account,long amount,string mode,string kind,string targetName)
    {
        if(BountyState<BountyClaim>("claim:"+id)is not null)throw new InvalidDataException("Doppelter Kopfgeldanspruch.");
        BountyStateInside("claim:"+id,new BountyClaim(id,account,amount,amount,mode,kind,targetName));
    }
    public long ClaimBounty(string eventId,string attacker,string victim,bool sameTeam)=>Atomic(()=>
    {
        if(!Key(eventId,70)||!BankPolicy.Steam(attacker)||!BankPolicy.Steam(victim)||attacker==victim||!Exists(attacker)||!titleRules.Enabled||!titleRules.Bounty.Enabled||titleRules.Bounty.IgnoreTeamKills&&sameTeam)return 0L;
        string key="kill:"+eventId;
        if(State(BountyOwner,key).Json is {} previous){var pair=JsonSerializer.Deserialize<string[]>(previous)!;if(!pair.SequenceEqual(new[]{attacker,victim}))throw new BankRejected("Abweichendes Kopfgeldereignis.");return 0L;}
        var orders=Bounties().Where(o=>o.Target==victim&&o.Status=="active").ToArray();long total=orders.Sum(o=>o.Amount);
        BookInside(new("bounty-kill:"+eventId,Period,BountyOwner,"Kopfgeldanspruch nach Kill",[]),true);
        BountyStateInside(key,new[]{attacker,victim});
        foreach(var o in orders)BountyStateInside("order:"+o.Id,o with{Status="claimed"});
        if(total>0){NewBountyClaimInside("kill:"+eventId,attacker,total,titleRules.Bounty.RewardMode,"reward",orders[0].TargetName);TitleAddInside(attacker,("BountiesClaimed",1),("TotalBountyClaimed",total));}
        foreach(var o in Bounties().Where(o=>o.Target==victim&&o.Status=="pending"))BountyStateInside("order:"+o.Id,o with{RefundReason="Ziel vor Cashbestätigung gestorben"});
        fault?.Invoke("after-bounty-claimed");return total;
    });
    private void RefundBountyInside(BountyOrder o,string reason)
    {
        if(o.Status=="pending"){BountyStateInside("order:"+o.Id,o with{RefundReason=reason});return;}
        if(o.Status!="active")return;
        BookInside(new("bounty-refund:"+o.Id,Period,BountyOwner,"Kopfgelderstattung: "+reason,[]),true);
        NewBountyClaimInside("refund:"+o.Id,o.Sponsor,o.Amount,o.Source=="Cash"?"CashThenBankOverflow":"Bank","refund",o.TargetName);
        BountyStateInside("order:"+o.Id,o with{Status="refunded",RefundReason=reason});
    }
    public void RefundBounties(string? target,string reason)=>Atomic(()=>
    {
        if(reason.Length is <1 or >100||target is not null&&!BankPolicy.Steam(target))throw new BankRejected("Ungültige Erstattung.");
        foreach(var o in Bounties().Where(o=>target is null||o.Target==target))RefundBountyInside(o,reason);
        fault?.Invoke("after-bounty-refund");return 0;
    });
    public void BountySurvivors(string round,long period,string[] died)=>Atomic(()=>
    {
        if(!Key(round,90)||period!=Period||died.Any(id=>!BankPolicy.Steam(id)))throw new BankRejected("Ungültige Kopfgeldrunde.");
        if(!titleRules.TitleSystem.Enabled||!titleRules.Bounty.Enabled||State(BountyOwner,"round:"+round).Json is not null)return 0;
        BookInside(new("bounty-survived:"+round,Period,BountyOwner,"Kopfgeldrunde überlebt",[]),true);
        foreach(var target in Bounties().Where(o=>o.Status=="active"&&!died.Contains(o.Target)).Select(o=>o.Target).Distinct())TitleAddInside(target,("BountySurvivedRounds",1));
        BountyStateInside("round:"+round,true);return 0;
    });
    public CashIntent? PayBountyClaim(string id,string claimId,string account,string session,int before,int toCash,long toBank,long discard=0)=>Atomic(()=>
    {
        var request=new BountyPayment(claimId,account,session,before,toCash,toBank,discard);
        if(BountyState<BountyPayment>("pay:"+id)is {} old){if(old!=request)throw new BankRejected("Abweichende Kopfgeldauszahlung.");return Intent(id);}
        var c=BountyState<BountyClaim>("claim:"+claimId)??throw new BankRejected("Kopfgeldanspruch fehlt.");
        decimal total=(decimal)toCash+toBank+discard;
        if(!Key(id,90)||c.Account!=account||Blocked(account)||!Exists(account)||before<0||before>titleRules.MaxPlayerCash||toCash<0||before+toCash>titleRules.MaxPlayerCash||toBank<0||discard<0||total<=0||total>c.Remaining||toCash>0&&session.Length is <1 or >128||c.Mode=="Bank"&&toCash>0||c.Mode!="Cash"&&discard>0||discard>0&&before+toCash!=titleRules.MaxPlayerCash)throw new BankRejected("Kopfgeldauszahlung nicht freigegeben.");
        var posts=new List<Posting>{new("system:bountypool",-(long)total)};
        if(toBank>0)posts.Add(new(account,toBank));if(toCash>0)posts.Add(new("system:escrow",toCash));if(discard>0)posts.Add(new("system:bountycap",discard));
        BookInside(new(id+":pay",Period,BountyOwner,c.Kind=="refund"?"Kopfgeld erstattet":"Kopfgeld ausgezahlt",posts.ToArray()),false);
        BountyStateInside("claim:"+claimId,c with{Remaining=c.Remaining-(long)total});BountyStateInside("pay:"+id,request);
        if(toCash>0){BountyStateInside("cash:"+id,new BountyCash("payout",claimId,toCash));Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,Period,before,before+toCash,-toCash);}
        fault?.Invoke("after-bounty-paid");return toCash>0?Intent(id):null;
    });
    private BountyCash? BountyCashSpec(string id)=>BountyState<BountyCash>("cash:"+id);
    private Receipt ResolveBountyCash(CashIntent i,BountyCash spec,bool applied,string actor)
    {
        Posting[] posts=spec.Kind=="stake"?(applied?[new("system:cash",-spec.Amount),new("system:bountypool",spec.Amount)]:[]):applied?[new("system:escrow",-spec.Amount),new("system:cash",spec.Amount)]:[new("system:escrow",-spec.Amount),new("system:bountypool",spec.Amount)];
        var result=BookInside(new(i.Id+":resolve",i.Period,BountyOwner,$"Kopfgeld-Cash {(applied?"bestätigt":"storniert")}: {actor}",posts),true);
        if(spec.Kind=="stake")
        {
            var o=GetBounty(spec.Reference)??throw new InvalidDataException("Kopfgeldauftrag fehlt.");o=o with{Status=applied?"active":"cancelled"};BountyStateInside("order:"+o.Id,o);
            if(applied){TitleAddInside(o.Sponsor,("BountiesSet",1),("TotalBountySet",o.Amount));if(o.RefundReason!="")RefundBountyInside(o,o.RefundReason);}
        }
        else if(!applied){var c=BountyState<BountyClaim>("claim:"+spec.Reference)??throw new InvalidDataException("Kopfgeldanspruch fehlt.");BountyStateInside("claim:"+spec.Reference,c with{Remaining=checked(c.Remaining+spec.Amount)});}
        Exec("UPDATE cash SET status=$p1 WHERE id=$p0",i.Id,applied?"committed":"cancelled");fault?.Invoke("after-bounty-cash");return result;
    }
    private void CarryBountiesInside(long nextPeriod)
    {
        if(BountyClaims().Length>0)throw new BankRejected("Vor Bankreset offene Kopfgeldauszahlungen/Erstattungen abschließen.");
        foreach(var o in Bounties())
        {
            if(o.Status!="active")throw new BankRejected("Unbestätigtes Cash-Kopfgeld vor Bankreset prüfen.");
            Exec("INSERT INTO module_state VALUES($p0,$p1,$p2,1,$p3)",nextPeriod,BountyOwner,"order:"+o.Id,JsonSerializer.Serialize(o));
        }
    }
    private void VerifyBounties()
    {
        var orders=Bounties(false);var claims=BountyStates<BountyClaim>("claim:");
        foreach(var o in orders)
        {
            if(!Key(o.Id,72)||o.Period<1||o.Period>Period||!BankPolicy.Steam(o.Sponsor)||!BankPolicy.Steam(o.Target)||!Exists(o.Sponsor)||!Exists(o.Target)||o.Amount is <1 or >int.MaxValue||o.Source is not ("Bank" or "Cash")||o.Status is not ("active" or "pending" or "claimed" or "refunded" or "cancelled"))throw new InvalidDataException("Kopfgeldauftrag beschädigt.");
            if(o.Status=="pending"&&(Intent(o.Id)is not {Status:"pending"} intent||intent.Account!=o.Sponsor||intent.Session!=o.Session||intent.Delta!=o.Amount||BountyCashSpec(o.Id)is not {Kind:"stake"}))throw new InvalidDataException("Cash-Kopfgeld ohne eindeutigen Vorgang.");
        }
        foreach(var c in claims)if(!Key(c.Id,90)||!BankPolicy.Steam(c.Account)||!Exists(c.Account)||c.Amount<1||c.Remaining<0||c.Remaining>c.Amount||c.Mode is not ("Bank" or "Cash" or "CashThenBankOverflow")||c.Kind is not ("reward" or "refund"))throw new InvalidDataException("Kopfgeldanspruch beschädigt.");
        if(Balance("system:bountypool")!=orders.Where(o=>o.Status=="active").Sum(o=>o.Amount)+claims.Sum(c=>c.Remaining))throw new InvalidDataException("Kopfgeldpool stimmt nicht mit Aufträgen und Ansprüchen überein.");
        foreach(var i in Pending())if(BountyCashSpec(i.Id)is {} c)
        {
            if(c.Amount<=0||c.Kind is not ("stake" or "payout")||i.Delta!=(c.Kind=="stake"?c.Amount:-c.Amount)||c.Kind=="payout"&&BountyState<BountyClaim>("claim:"+c.Reference)?.Account!=i.Account)throw new InvalidDataException("Kopfgeld-Cashzuordnung beschädigt.");
        }
    }
}
