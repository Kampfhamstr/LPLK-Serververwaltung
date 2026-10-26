namespace LPLK_Economy;

// Volatile until round end by explicit server-owner policy. Only managed values
// are retained; no player/entity objects, open transaction or file writes.
public sealed partial class Ledger
{
    private sealed record DeferredKill(string Key,long Period,EconomySettings Rules,Action Apply,string? BountyTarget=null);
    private readonly List<DeferredKill> deferredKills=[];
    private readonly HashSet<string> deferredKillKeys=[];
    private readonly HashSet<string> deferredBountyOrders=[];
    public int QueuedKillCount {get {lock(gate)return deferredKills.Count;}}
    private bool QueueKill(string key,Action apply,string? bountyTarget=null)
    {
        if(!deferredKillKeys.Add(key))return false;
        deferredKills.Add(new(key,Period,titleRules,apply,bountyTarget));return true;
    }
    public long DeferKillReward(string eventId,string attacker,string attackerName,string victim,string victimName,int victimCash,bool sameTeam)
    {
        lock(gate)
        {
            if(!Key(eventId,70)||!BankPolicy.Steam(attacker)||!BankPolicy.Steam(victim)||attacker==victim||victimCash<0||victimCash>titleRules.MaxPlausibleCash)throw new BankRejected("Ungültiger Kill-Schnappschuss.");
            var rules=titleRules.KillReward;if(!titleRules.Enabled||!rules.Enabled||sameTeam&&rules.IgnoreTeamKills)return 0;
            long amount=KillRewardPolicy.Amount(victimCash,rules);
            if(amount==0||!QueueKill("reward:"+eventId,()=>{Open(attacker,attackerName);AwardKillReward(eventId,attacker,victim,victimName,victimCash,sameTeam);}))return 0;
            return amount;
        }
    }
    public long DeferBounty(string eventId,string attacker,string attackerName,string victim,bool sameTeam)
    {
        lock(gate)
        {
            if(!Key(eventId,70)||!BankPolicy.Steam(attacker)||!BankPolicy.Steam(victim)||attacker==victim||!titleRules.Enabled||!titleRules.Bounty.Enabled||sameTeam&&titleRules.Bounty.IgnoreTeamKills)return 0;
            var orders=Bounties().Where(o=>o.Target==victim&&!deferredBountyOrders.Contains(o.Id)).ToArray();
            if(orders.Length==0)return 0;
            var ids=orders.Select(o=>o.Id).ToArray();var pending=orders.Where(o=>o.Status=="pending").Select(o=>o.Id).ToArray();
            if(!QueueKill("bounty:"+eventId,()=>{Open(attacker,attackerName);ClaimBounty(eventId,attacker,victim,sameTeam,ids,pending);},victim))return 0;
            deferredBountyOrders.UnionWith(ids);return orders.Where(o=>o.Status=="active").Sum(o=>o.Amount);
        }
    }
    public long DeferLotteryKill(string round,string account,string victim,bool opposingTeams)
    {
        lock(gate)
        {
            if(!titleRules.Enabled||!titleRules.LottoAbilityBonus.Enabled||!opposingTeams||account==victim||!BankPolicy.Steam(victim)||!BankPolicy.Steam(account)||GetLotteryRound(round) is not {Status:"open"} r||r.Period!=Period||!Exists(account)||Blocked(account))return 0;
            int amount=BonusState("kill:"+round+":"+account,0);if(amount<=0)return 0;
            return QueueKill("lottery:"+round+":"+account,()=>ClaimLotteryKill(round,account,victim,opposingTeams))?amount:0;
        }
    }
    // A disconnect after a queued death must not refund its bounty first.
    private bool DeferBountyRefund(string? target,string reason)
    {
        lock(gate)
        {
            if(!deferredKills.Any(k=>k.BountyTarget is not null&&(target is null||k.BountyTarget==target)))return false;
            QueueKill("refund:"+(target??"all")+":"+deferredKills.Count,()=>RefundBountiesNow(target,reason));return true;
        }
    }
    public int FlushKillEvents()
    {
        lock(gate)
        {
            if(deferredKills.Count==0)return 0;
            int count=deferredKills.Count;var current=titleRules;
            try
            {
                Atomic(()=>{foreach(var item in deferredKills){if(item.Period!=Period)continue;titleRules=item.Rules;item.Apply();}return 0;});
                deferredKills.Clear();deferredKillKeys.Clear();deferredBountyOrders.Clear();return count;
            }
            finally{titleRules=current;}
        }
    }
}
