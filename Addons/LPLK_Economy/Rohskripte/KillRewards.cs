using System.Text.Json;
namespace LPLK_Economy;

public sealed record KillRewardSettings
{
    public bool Enabled {get;init;}=true;
    public int PercentOfVictimCash {get;init;}=10;
    public int MinReward {get;init;}=100;
    public int MaxReward {get;init;}=2000;
    public bool IgnoreTeamKills {get;init;}=true;
    public bool IgnoreBots {get;init;}=true;
    public string RewardMode {get;init;}="CashThenBankOverflow";
    public bool AnnounceReward {get;init;}=true;
}
public static class KillRewardPolicy
{
    public static void Validate(KillRewardSettings s)
    {
        if(s is null||s.PercentOfVictimCash<0||s.MinReward<0||s.MaxReward< -1||s.RewardMode is not ("Bank" or "Cash" or "CashThenBankOverflow"))throw new InvalidDataException("Ungültige Kill-Belohnung.");
        // BetBank's outer IsUsablePlayer check excludes bots even when IgnoreBots=false.
    }
    public static int Amount(int cash,KillRewardSettings s)
    {
        if(cash<=0||s.PercentOfVictimCash<=0)return 0;
        decimal amount=decimal.Floor(cash*(decimal)s.PercentOfVictimCash/100);
        if(amount<s.MinReward)return 0;
        if(s.MaxReward>=0)amount=Math.Min(amount,s.MaxReward);
        return (int)Math.Min(int.MaxValue,amount);
    }
}
public sealed record KillRewardClaim(string Id,string Account,string Victim,string VictimName,int VictimCash,int Amount,int Remaining,string Mode,bool Announce);
public sealed record KillRewardPayment(string Claim,string Account,string Session,int Before,int Cash,long Bank,long Discard);
public sealed partial class Ledger
{
    private const string KillRewardOwner="LPLK_Economy.KillReward";
    private T? KillState<T>(string key) where T:class=>State(KillRewardOwner,key).Json is {} json?JsonSerializer.Deserialize<T>(json)??throw new InvalidDataException("Kill-Belohnung beschädigt."):null;
    private void KillStateInside(string key,object value)=>Exec("INSERT INTO module_state VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,KillRewardOwner,key,JsonSerializer.Serialize(value));
    public KillRewardClaim[] KillRewardClaims(bool openOnly=true)
    {
        lock(gate){var result=new List<KillRewardClaim>();using var c=Command("SELECT json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'claim:*' ORDER BY rowid",Period,KillRewardOwner);using var r=c.ExecuteReader();while(r.Read()){var claim=JsonSerializer.Deserialize<KillRewardClaim>(r.GetString(0))??throw new InvalidDataException("Kill-Belohnung fehlt.");if(!openOnly||claim.Remaining>0)result.Add(claim);}return result.ToArray();}
    }
    public long AwardKillReward(string eventId,string attacker,string victim,string victimName,int victimCash,bool sameTeam)=>Atomic(()=>
    {
        if(!Key(eventId,70)||!BankPolicy.Steam(attacker)||!BankPolicy.Steam(victim)||attacker==victim||!Exists(attacker)||victimCash<0||victimCash>titleRules.MaxPlausibleCash)throw new BankRejected("Ungültiges Kill-Ereignis oder unbekanntes Opfer-Cash.");
        if(KillState<KillRewardClaim>("claim:"+eventId)is {} old){if(old.Account!=attacker||old.Victim!=victim)throw new BankRejected("Abweichende Kill-Wiederholung.");return 0L;}
        var s=titleRules.KillReward;if(!titleRules.Enabled||!s.Enabled||sameTeam&&s.IgnoreTeamKills)return 0L;
        int amount=KillRewardPolicy.Amount(victimCash,s);
        BookInside(new("killreward:"+eventId,Period,KillRewardOwner,"Kill-Belohnung gesichert",amount>0?[new("system:killgrant",-amount),new("system:killpool",amount)]:[]),true);
        KillStateInside("claim:"+eventId,new KillRewardClaim(eventId,attacker,victim,SafeName(victimName),victimCash,amount,amount,s.RewardMode,s.AnnounceReward));
        fault?.Invoke("after-kill-awarded");return (long)amount;
    });
    public CashIntent? PayKillReward(string id,string claimId,string account,string session,int before,int toCash,long toBank,long discard=0)=>Atomic(()=>
    {
        var request=new KillRewardPayment(claimId,account,session,before,toCash,toBank,discard);
        if(KillState<KillRewardPayment>("pay:"+id)is {} old){if(old!=request)throw new BankRejected("Abweichende Kill-Auszahlung.");return Intent(id);}
        var c=KillState<KillRewardClaim>("claim:"+claimId)??throw new BankRejected("Kill-Anspruch fehlt.");
        decimal total=(decimal)toCash+toBank+discard;
        if(!Key(id,90)||c.Account!=account||Blocked(account)||!Exists(account)||before<0||before>titleRules.MaxPlayerCash||toCash<0||(long)before+toCash>titleRules.MaxPlayerCash||toBank<0||discard<0||total<=0||total>c.Remaining||toCash>0&&session.Length is <1 or >128||c.Mode=="Bank"&&toCash>0||c.Mode!="Cash"&&discard>0||discard>0&&before+toCash!=titleRules.MaxPlayerCash)throw new BankRejected("Kill-Auszahlung nicht freigegeben.");
        var posts=new List<Posting>{new("system:killpool",-(long)total)};if(toBank>0)posts.Add(new(account,toBank));if(toCash>0)posts.Add(new("system:escrow",toCash));if(discard>0)posts.Add(new("system:killcap",discard));
        BookInside(new(id+":pay",Period,KillRewardOwner,"Kill-Belohnung ausgezahlt",posts.ToArray()),false);
        KillStateInside("claim:"+claimId,c with{Remaining=c.Remaining-(int)total});KillStateInside("pay:"+id,request);
        if(toCash>0)Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,Period,before,before+toCash,-toCash);
        fault?.Invoke("after-kill-paid");return toCash>0?Intent(id):null;
    });
    private KillRewardPayment? KillCashSpec(string id)=>KillState<KillRewardPayment>("pay:"+id);
    private Receipt ResolveKillCash(CashIntent i,KillRewardPayment spec,bool applied,string actor)
    {
        var result=BookInside(new(i.Id+":resolve",i.Period,KillRewardOwner,$"Kill-Cash {(applied?"bestätigt":"storniert")}: {actor}",[new("system:escrow",-spec.Cash),new(applied?"system:cash":"system:killpool",spec.Cash)]),true);
        if(!applied){var c=KillState<KillRewardClaim>("claim:"+spec.Claim)??throw new InvalidDataException("Kill-Anspruch fehlt.");KillStateInside("claim:"+spec.Claim,c with{Remaining=checked(c.Remaining+spec.Cash)});}
        Exec("UPDATE cash SET status=$p1 WHERE id=$p0",i.Id,applied?"committed":"cancelled");fault?.Invoke("after-kill-cash");return result;
    }
    private void VerifyKillRewards()
    {
        var claims=KillRewardClaims(false);
        foreach(var c in claims)if(!Key(c.Id,70)||!BankPolicy.Steam(c.Account)||!BankPolicy.Steam(c.Victim)||c.Account==c.Victim||!Exists(c.Account)||c.VictimCash<0||c.Amount<0||c.Remaining<0||c.Remaining>c.Amount||c.Mode is not ("Bank" or "Cash" or "CashThenBankOverflow"))throw new InvalidDataException("Kill-Anspruch beschädigt.");
        if(Balance("system:killpool")!=claims.Sum(c=>(long)c.Remaining))throw new InvalidDataException("Kill-Pool stimmt nicht mit Ansprüchen überein.");
        foreach(var i in Pending())if(KillCashSpec(i.Id)is {} s)if(s.Cash<=0||i.Account!=s.Account||i.Session!=s.Session||i.Before!=s.Before||i.After!=(long)s.Before+s.Cash||i.Delta!=-s.Cash||KillState<KillRewardClaim>("claim:"+s.Claim)?.Account!=i.Account)throw new InvalidDataException("Kill-Cashzuordnung beschädigt.");
    }
}
