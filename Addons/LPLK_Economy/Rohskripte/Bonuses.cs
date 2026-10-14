using System.Security.Cryptography;
using System.Text.Json;
namespace LPLK_Economy;
public sealed record LotteryBonus(string Kind,int Amount,string Status="ready");
public sealed record BonusCash(string Kind,long Amount,int MaxCash,string Round="",int Index=-1,string Account="",string Session="",int Before=-1,int After=-1);
public sealed partial class Ledger
{
    private const string BonusOwner="LPLK_Economy.Bonuses";
    private T BonusState<T>(string key,T fallback)=>State(BonusOwner,key).Json is {} json?JsonSerializer.Deserialize<T>(json)??throw new InvalidDataException("Lottobonus beschädigt."):fallback;
    private void BonusStateInside(string key,object value)=>Exec("INSERT INTO module_state VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,BonusOwner,key,JsonSerializer.Serialize(value));
    private static int BonusRoll(int max,Func<int,int>? random){int n=(random??RandomNumberGenerator.GetInt32)(max);return n>=0&&n<max?n:throw new BankRejected("Ungültiger Bonus-Zufallswert.");}
    public int UpgradeDiscount(string account){lock(gate)return BonusState("discount:"+account,0);}
    private void GrantDiscountInside(string account,int tickets,LotterySettings s,Func<int,int>? random)
    {
        if(!titleRules.TitleSystem.Enabled||!s.PermanentTicketUpgradeDiscountEnabled||UpgradeDiscount(account)>0||s.PermanentTicketUpgradeDiscountChanceBasisPoints==0)return;
        for(int n=0;n<tickets;n++)if(BonusRoll(10000,random)<s.PermanentTicketUpgradeDiscountChanceBasisPoints)
        {
            int percent=s.PermanentTicketUpgradeDiscountMinPercent+BonusRoll(s.PermanentTicketUpgradeDiscountMaxPercent-s.PermanentTicketUpgradeDiscountMinPercent+1,random);
            if(percent>0){BonusStateInside("discount:"+account,percent);TitleAddInside(account,("UpgradeDiscountsWon",1));}break;
        }
    }
    private long DiscountedUpgrade(string account,long cost)=>cost-RobberyPolicy.Percent(cost,UpgradeDiscount(account));
    private void ConsumeDiscountInside(string account,long baseCost)
    {
        if(baseCost-DiscountedUpgrade(account,baseCost)<=0)return;
        BonusStateInside("discount:"+account,0);TitleAddInside(account,("UpgradeDiscountsUsed",1));fault?.Invoke("after-discount-consumed");
    }
    public LotteryBonus[] LotteryBonuses(string round,string account){lock(gate)return BonusState<LotteryBonus[]>("roll:"+round+":"+account,[]);}
    public LotteryBonus[] RollLotteryBonuses(string round,string account,bool pistol,AbilitySettings s,Func<int,int>? random=null)=>Atomic(()=>
    {
        BonusPolicy.Validate(s);var r=GetLotteryRound(round)??throw new BankRejected("Lottorunde fehlt.");
        if(r.Period!=Period||r.Status!="open"||!BankPolicy.Steam(account)||!Exists(account))throw new BankRejected("Bonusrunde nicht verfügbar.");
        string key="roll:"+round+":"+account;if(State(BonusOwner,key).Json is not null)return LotteryBonuses(round,account);
        var result=new List<LotteryBonus>();
        if(titleRules.Enabled&&titleRules.Lotto.Enabled&&s.Enabled)
        {
            int tickets=LotteryTickets(round).Where(t=>t.Account==account&&!IsGift(t)).Sum(t=>t.Count);
            int count=s.MaxTicketsCountedPerPlayer<0?tickets:Math.Min(tickets,s.MaxTicketsCountedPerPlayer);
            var pool=s.Effects.Pool(pistol).Where(x=>x.Effect.Enabled&&x.Effect.Weight>0&&x.Effect.Amount>0).ToArray();int weight=pool.Sum(x=>x.Effect.Weight);
            int wins=0;for(int n=0;n<count;n++)if(BonusRoll(10000,random)<s.ChancePerTicketBasisPoints)wins++;
            for(int n=0;n<Math.Min(wins,s.MaxBonusesPerPlayerPerRound)&&weight>0;n++)
            {
                int value=BonusRoll(weight,random);var chosen=pool.First(x=>{if(value<x.Effect.Weight)return true;value-=x.Effect.Weight;return false;});result.Add(new(chosen.Kind,chosen.Effect.Amount));
            }
        }
        BonusStateInside(key,result.ToArray());return result.ToArray();
    });
    private LotteryBonus BonusAt(string round,string account,int index)
    {
        var r=GetLotteryRound(round);var all=LotteryBonuses(round,account);
        if(r?.Period!=Period||r.Status!="open"||index<0||index>=all.Length)throw new BankRejected("Lottobonus veraltet.");return all[index];
    }
    private void MarkBonusInside(string round,string account,int index,string status)
    {
        var all=LotteryBonuses(round,account);if(index<0||index>=all.Length)throw new InvalidDataException("Bonuszuordnung fehlt.");all[index]=all[index] with{Status=status};BonusStateInside("roll:"+round+":"+account,all);
    }
    private static string BonusId(string round,string account,int index)=>"bonus:"+round+":"+account+":"+index;
    private static bool IsGift(LotteryTicket ticket)=>ticket.Id.StartsWith("gift:",StringComparison.Ordinal);
    public long ApplyLotteryBonus(string round,string account,int index)=>Atomic(()=>
    {
        var bonus=BonusAt(round,account,index);if(bonus.Status!="ready")return 0L;if(Blocked(account))throw new BankRejected("Cashprüfung offen.");
        string id=BonusId(round,account,index);long paid=0;
        switch(bonus.Kind)
        {
            case "BankBonus":
                paid=Math.Min(bonus.Amount,Math.Max(0,Limit-Balance(account)));BookInside(new(id,Period,BonusOwner,"Lotto-Bankbonus",[new(account,paid),new("system:lotbonus",-paid)]),false);break;
            case "FreeTicket":
                long cost=checked(GetLotteryRound(round)!.Rules.TicketPrice*bonus.Amount);
                BookInside(new(id,Period,BonusOwner,"Lotto-Gratislose finanziert",[new("system:lotbonus",-cost),new("system:lotpool",cost)]),false);
                Exec("INSERT INTO lot_tickets VALUES($p0,$p1,$p2,$p3,0,$p4)","gift:"+id,round,account,bonus.Amount,cost);paid=bonus.Amount;break;
            case "NextKillBankBonus":BonusStateInside("kill:"+round+":"+account,bonus.Amount);paid=bonus.Amount;break;
            default:throw new BankRejected("Cashbonus benötigt bestätigte Cashänderung.");
        }
        MarkBonusInside(round,account,index,"applied");fault?.Invoke("after-lottery-bonus");return paid;
    });
    public long ClaimLotteryKill(string round,string account,string victim,bool opposingTeams)=>Atomic(()=>
    {
        if(!titleRules.Enabled||!titleRules.LottoAbilityBonus.Enabled||!opposingTeams||account==victim||!BankPolicy.Steam(victim)||!BankPolicy.Steam(account)||GetLotteryRound(round) is not {Status:"open"} r||r.Period!=Period||!Exists(account)||Blocked(account))return 0L;
        string key="kill:"+round+":"+account;int amount=BonusState(key,0);if(amount<=0)return 0L;
        long paid=Math.Min(amount,Math.Max(0,Limit-Balance(account)));BookInside(new("killbonus:"+round+":"+account,Period,BonusOwner,"Lotto-Killbonus",[new(account,paid),new("system:lotbonus",-paid)]),false);BonusStateInside(key,0);return paid;
    });
    public CashIntent? PrepareLotteryBonusCash(string round,string account,int index,string session,int before)=>Atomic(()=>
    {
        var bonus=BonusAt(round,account,index);string id=BonusId(round,account,index);
        if(Intent(id) is {} prior)return prior;
        if(bonus.Kind!="CashBonus"||bonus.Status!="ready")return null;
        if(before<0||before>titleRules.MaxPlayerCash)throw new BankRejected("Cashbonus über Cashlimit.");
        int after=Math.Min(titleRules.MaxPlayerCash,checked(before+bonus.Amount));
        if(after==before){MarkBonusInside(round,account,index,"applied");return null;}
        var intent=PrepareBonusCashInside(id,account,session,before,after,new("bonus",after-before,16000,round,index));MarkBonusInside(round,account,index,"cash-pending");return intent;
    });
    // Native cash is never replayed automatically. Uncertain writes retain the
    // same pending intent and use the existing audited administration resolution.
    private CashIntent PrepareBonusCashInside(string id,string account,string session,int before,int after,BonusCash spec)
    {
        if(!Key(id,110)||!BankPolicy.Steam(account)||!Exists(account)||session.Length is <1 or >128||before<0||after<0||before>spec.MaxCash||after>spec.MaxCash||before==after||spec.Amount<=0||Blocked(account))throw new BankRejected("Bonus-Cash nicht freigegeben.");
        long delta=(long)before-after;
        BookInside(new(id+":prepare",Period,BonusOwner,"Lotto-Cash vorgemerkt",delta<0?[new("system:lotbonus",delta),new("system:escrow",-delta)]:[]),false);
        BonusStateInside("cash:"+id,spec with{Account=account,Session=session,Before=before,After=after});Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,Period,before,after,delta);return new(id,account,session,Period,before,after,delta,"pending");
    }
    public CashIntent? PrepareCashCollection(string id,long period,string account,string session,int before,int amount,bool removeLive)=>Atomic(()=>
    {
        if(period!=Period||!Key(id,100)||!BankPolicy.Steam(account)||!Exists(account)||before<0||before>titleRules.MaxPlausibleCash||amount<1||amount>titleRules.MaxPlausibleCash)throw new BankRejected("Ungültige Rest-Cash-Sammlung.");
        int after=removeLive?before-Math.Min(before,amount):before;
        var spec=new BonusCash("collection",amount,titleRules.MaxPlausibleCash,Account:account,Session:session,Before:before,After:after);
        if(State(BonusOwner,"cash:"+id).Json is {} saved){if(JsonSerializer.Deserialize<BonusCash>(saved)!=spec)throw new BankRejected("Abweichende Cash-Sammlung.");return Intent(id);}
        if(after!=before)return PrepareBonusCashInside(id,account,session,before,after,spec);
        if(Blocked(account))throw new BankRejected("Cashprüfung offen; Snapshot nicht parallel buchen.");
        BookInside(new(id,Period,BonusOwner,"Rest-Cash aus Todessnapshot / ohne Liveabzug",[new("system:lotcashsnapshot",-amount),new("system:superpot",amount)]),false);BonusStateInside("cash:"+id,spec);fault?.Invoke("after-cash-collection");return null;
    });
    private BonusCash? BonusCashSpec(string id)=>BonusState<BonusCash?>("cash:"+id,null);
    private Receipt ResolveBonusCash(CashIntent i,BonusCash spec,bool applied,string actor)
    {
        Posting[] postings=spec.Kind=="bonus"?(applied?[new("system:escrow",i.Delta),new("system:cash",-i.Delta)]:[new("system:escrow",i.Delta),new("system:lotbonus",-i.Delta)]):applied?[new("system:cash",-i.Delta),new("system:lotcashsnapshot",-(spec.Amount-i.Delta)),new("system:superpot",spec.Amount)]:[];
        var result=BookInside(new(i.Id+":resolve",i.Period,BonusOwner,$"Lotto-Cash {(applied?"bestätigt":"storniert")}: {actor}",postings),true);
        if(spec.Kind=="bonus")MarkBonusInside(spec.Round,i.Account,spec.Index,applied?"applied":"cancelled");
        Exec("UPDATE cash SET status=$p1 WHERE id=$p0",i.Id,applied?"committed":"cancelled");fault?.Invoke("after-cash-collection");return result;
    }
    private void VerifyBonuses()
    {
        using var c=Command("SELECT key,json FROM module_state WHERE period=$p0 AND owner=$p1",Period,BonusOwner);using var r=c.ExecuteReader();
        while(r.Read())
        {
            string key=r.GetString(0),json=r.GetString(1);bool valid=true;
            if(key.StartsWith("discount:"))valid=JsonSerializer.Deserialize<int>(json) is >=0 and <=100;
            else if(key.StartsWith("roll:")){var all=JsonSerializer.Deserialize<LotteryBonus[]>(json);valid=all is not null&&all.Length<=10&&all.All(b=>b is not null&&b.Kind is "CashBonus" or "BankBonus" or "FreeTicket" or "NextKillBankBonus"&&b.Amount is >0 and <=1_000_000&&b.Status is "ready" or "applied" or "cash-pending" or "cancelled");}
            else if(key.StartsWith("cash:"))
            {
                var s=JsonSerializer.Deserialize<BonusCash>(json);valid=s is not null&&s.Kind is "bonus" or "collection"&&s.Amount>0&&s.MaxCash>0&&BankPolicy.Steam(s.Account)&&s.Session.Length is >0 and <=128&&s.Before>=0&&s.After>=0&&s.Before<=s.MaxCash&&s.After<=s.MaxCash;
                if(valid&&Intent(key[5..]) is {} i)valid=i.Account==s!.Account&&i.Session==s.Session&&i.Before==s.Before&&i.After==s.After&&(s.Kind=="bonus"?i.Delta<0&&s.Amount==-i.Delta:i.Delta>0&&s.Amount>=i.Delta);
            }
            if(!valid)throw new InvalidDataException("Lottobonus oder Cash-Sammlung beschädigt; Daten bleiben erhalten.");
        }
    }
}
