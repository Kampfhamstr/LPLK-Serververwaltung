using System.Security.Cryptography;
using System.Text.Json;

namespace LPLK_Economy;

public static class RobberyPolicy
{
    public static void Validate(RobberySettings s)
    {
        foreach(var p in typeof(RobberySettings).GetProperties())
        {
            if(p.GetValue(s) is int i && (i<0||i>10_000_000)||p.GetValue(s) is long l&&(l<0||l>9_000_000_000_000_000))throw new InvalidDataException("Ungültige Bankraubregel: "+p.Name);
        }
        if(s.MaxRobberSkillLevel>100||s.MaxSpySkillLevel>100||s.MaxActiveOrdersPerSponsor is <1 or >10||s.MaxActiveOrdersPerTarget is <1 or >10||s.HighRollerProtectionAbsoluteMinutes is <1 or >1440||s.HighRollerProtectionBypassCostMultiplier is <1 or >100||s.HighRollerProtectionBypassSuperpotShareBasisPoints>10000||s.SpyLogMaxGlobalEvents is <1 or >1000||s.SpyLogMaxEntriesPerPlayer is <1 or >1000||s.SpyMaxTargetsShown is <1 or >64||s.VaultDurationDays is <1 or >3650||s.SecurityDurationDays is <1 or >3650||s.ProtectionHoursAfterSuccess>87600||s.ProtectionHoursAfterFail>87600||s.SameTargetCooldownHours>87600||s.MinActivePlayersForEconomyRoundEnd is <1 or >64)throw new InvalidDataException("Ungültige Bankraubgrenzen.");
        if(new[]{s.MinLootPercent,s.NoVaultMaxLootPercent,s.OldSteelVaultMaxLootPercent,s.ModernSteelVaultMaxLootPercent,s.TitanVaultMaxLootPercent,s.BaseSuccessChancePercent,s.MinSuccessChancePercent,s.SecuritySuccessReductionPercentPoints,s.SuccessChanceBonusPerRobberSkillLevel,s.PassiveSpyReportChancePerLevelPercent,s.RobinHoodMaxBonusPercentPoints,s.RobinHoodPoorAttackerBonusPercentPoints,s.RobinHoodVeryPoorAttackerBonusPercentPoints}.Any(v=>v>100))throw new InvalidDataException("Bankraub-Prozentwert außerhalb 0–100.");
    }
    public static bool ValidRound(RobberySettings s,bool known,bool warmup,int players,int winner)=>known&&!warmup&&(winner is 2 or 3||winner>=0&&s.ProcessEconomyOnRoundEndWithoutWinner&&players>=s.MinActivePlayersForEconomyRoundEnd);
    public static int Robin(RobberySettings s,long attacker,long target)=>!s.RobinHoodBonusEnabled||target<s.RobinHoodTargetBankThreshold?0:Math.Min(s.RobinHoodMaxBonusPercentPoints,attacker<=s.RobinHoodVeryPoorAttackerBankThreshold?s.RobinHoodVeryPoorAttackerBonusPercentPoints:attacker<=s.RobinHoodPoorAttackerBankThreshold?s.RobinHoodPoorAttackerBonusPercentPoints:0);
    public static int Chance(RobberySettings s,RobberySecurity target,int skill,long attackerBank,long targetBank,bool bypass)=>Math.Clamp(s.BaseSuccessChancePercent+Math.Clamp(skill,0,s.MaxRobberSkillLevel)*s.SuccessChanceBonusPerRobberSkillLevel+Robin(s,attackerBank,targetBank)-(bypass?0:target.Protections.Count)*s.SecuritySuccessReductionPercentPoints,s.MinSuccessChancePercent,100);
    public static int MaxLoot(RobberySettings s,RobberySecurity target,long balance,bool bypass)=> (bypass&&s.DisableVaultAboveTargetBank&&balance>=s.HighRollerProtectionBypassTargetBankThreshold?0:target.Vault) switch{1=>s.OldSteelVaultMaxLootPercent,2=>s.ModernSteelVaultMaxLootPercent,3=>s.TitanVaultMaxLootPercent,_=>s.NoVaultMaxLootPercent};
    public static long BypassCost(RobberySettings s,int count)
    {
        long cap=s.HighRollerProtectionBypassMaxCost>0?s.HighRollerProtectionBypassMaxCost:9_000_000_000_000_000,cost=Math.Min(s.HighRollerProtectionBypassBaseCost,cap);
        for(int i=0;i<count&&cost<cap&&s.HighRollerProtectionBypassCostMultiplier>1;i++)cost=cost>cap/s.HighRollerProtectionBypassCostMultiplier?cap:Math.Min(cap,cost*s.HighRollerProtectionBypassCostMultiplier);
        return cost;
    }
    public static long Percent(long value,int percent,int divisor=100)=>checked((long)((decimal)value*percent/divisor));
    public static int VaultPrice(RobberySettings s,int level)=>level switch{0=>0,1=>s.OldSteelVaultPrice,2=>s.ModernSteelVaultPrice,3=>s.TitanVaultPrice,_=>throw new BankRejected("Ungültige Tresorstufe.")};
}

public sealed record RobberySecurity
{
    public int Vault {get;init;}
    public long VaultUntil {get;init;}
    public Dictionary<string,long> Protections {get;init;}=[];
    public int RobberSkill {get;init;}
    public int SpySkill {get;init;}
    public long ProtectedAt {get;init;}
    public long ProtectedUntil {get;init;}
    public int Bypasses {get;init;}
}
public sealed record RobberyOrder
{
    public string Id {get;init;}="";
    public long Period {get;init;}
    public string Sponsor {get;init;}="";
    public string Target {get;init;}="";
    public string SponsorName {get;init;}="";
    public string TargetName {get;init;}="";
    public long Cost {get;init;}
    public long BypassCost {get;init;}
    public bool Bypassed {get;init;}
    public long ReferenceBank {get;init;}
    public int Skill {get;init;}
    public string Status {get;init;}="queued";
    public long Created {get;init;}
    public long Executed {get;init;}
    public long TargetBank {get;init;}
    public bool Success {get;init;}
    public int Chance {get;init;}
    public int Loot {get;init;}
    public long Stolen {get;init;}
    public long Claim {get;init;}
    public string[] Revealed {get;init;}=[];
    public string[] Observers {get;init;}=[];
    public RobberySettings Rules {get;init;}=new();
}
public sealed record RobberyQuote(long Cost,long BypassCost,bool Bypassed,int Chance,int MaxLoot);

public sealed partial class Ledger
{
    private RobberySettings robberyRules=new();
    private const string RobOwner="LPLK_Economy.Robbery";
    private void ResetRobberyBypassForBalance(string account)
    {
        if(!robberyRules.HighRollerProtectionBypassResetWhenTargetBelowThreshold||!BankPolicy.Steam(account)||Balance(account)>=robberyRules.HighRollerProtectionBypassTargetBankThreshold)return;
        var state=State(RobOwner,"security:"+account);if(state.Json is null)return;
        var sec=JsonSerializer.Deserialize<RobberySecurity>(state.Json)!;if(sec.Bypasses==0)return;
        Exec("UPDATE module_state SET revision=revision+1,json=$p3 WHERE period=$p0 AND owner=$p1 AND key=$p2",Period,RobOwner,"security:"+account,JsonSerializer.Serialize(sec with{Bypasses=0}));
    }
    private T RobState<T>(string key) where T:new()=>State(RobOwner,key).Json is {} json?JsonSerializer.Deserialize<T>(json)??throw new InvalidDataException("Ungültiger Bankraubstand."):new();
    private StateChange RobChange(string key,object value)=>new(key,State(RobOwner,key).Revision,JsonSerializer.Serialize(value,value.GetType()));
    public RobberySecurity Security(string account,RobberySettings s,long now)
    {
        lock(gate)
        {
            var raw=RobState<RobberySecurity>("security:"+account);
            bool Valid(long until)=>until==long.MaxValue||until>now;
            return raw with{Vault=Valid(raw.VaultUntil)?raw.Vault:0,Protections=raw.Protections.Where(p=>Valid(p.Value)).ToDictionary(),RobberSkill=Math.Min(raw.RobberSkill,s.MaxRobberSkillLevel),SpySkill=Math.Min(raw.SpySkill,s.MaxSpySkillLevel),Bypasses=s.HighRollerProtectionBypassResetWhenTargetBelowThreshold&&Balance(account)<s.HighRollerProtectionBypassTargetBankThreshold?0:raw.Bypasses};
        }
    }
    public RobberyOrder? GetRobbery(string id){lock(gate)return State(RobOwner,"order:"+id).Json is {} json?JsonSerializer.Deserialize<RobberyOrder>(json):null;}
    public RobberyOrder[] RobberyOrders(bool pendingOnly=false,int limit=500)
    {
        lock(gate)
        {
            var rows=new List<RobberyOrder>();using var c=Command("SELECT json FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'order:*'"+(pendingOnly?" AND (json_extract(json,'$.Status')<>'done' OR json_extract(json,'$.Claim')>0)":"")+" ORDER BY rowid DESC LIMIT $p2",Period,RobOwner,Math.Clamp(limit,1,10000));
            using var r=c.ExecuteReader();while(r.Read())rows.Add(JsonSerializer.Deserialize<RobberyOrder>(r.GetString(0))!);return rows.ToArray();
        }
    }
    public long RobberyClaims {get{lock(gate)return Number("SELECT coalesce(sum(json_extract(json,'$.Claim')),0) FROM module_state WHERE period=$p0 AND owner=$p1 AND key GLOB 'order:*'",Period,RobOwner);}}
    private void RobAccount(string id){if(!BankPolicy.Steam(id)||!Exists(id)||Blocked(id))throw new BankRejected("Bankkonto nicht verfügbar oder Cash-Prüfung offen.");}
    private void RobEnabled(RobberySettings s){RobberyPolicy.Validate(s);if(!s.Enabled)throw new BankRejected("Bankraub ist ausgeschaltet.");}
    public RobberyQuote QuoteRobbery(string sponsor,string target,RobberySettings s,long now)
    {
        lock(gate)
        {
            RobEnabled(s);RobAccount(sponsor);RobAccount(target);if(sponsor==target)throw new BankRejected("Eigenes Konto ist kein Raubziel.");
            if(Balance(target)<s.MinTargetBank)throw new BankRejected($"Ziel benötigt mindestens {s.MinTargetBank:N0} $ Bank.");
            var pending=RobberyOrders(true,10000).Where(o=>o.Status!="done").ToArray();
            if(pending.Count(o=>o.Sponsor==sponsor)>=s.MaxActiveOrdersPerSponsor||pending.Count(o=>o.Target==target)>=s.MaxActiveOrdersPerTarget)throw new BankRejected("Bereits genügend offene Aufträge für Auftraggeber oder Ziel.");
            long cooldown=RobState<long>("pair:"+sponsor+":"+target);if(cooldown>now)throw new BankRejected("Erneuter Angriff auf dieses Ziel noch gesperrt.");
            var sec=Security(target,s,now);bool bypass=sec.ProtectedUntil>now;long extra=0;
            if(bypass)
            {
                if(!s.HighRollerProtectionBypassEnabled||s.HighRollerProtectionBypassTargetBankThreshold<=0||Balance(target)<s.HighRollerProtectionBypassTargetBankThreshold)throw new BankRejected("Ziel besitzt noch Raubschutz.");
                if(now<sec.ProtectedAt+s.HighRollerProtectionAbsoluteMinutes*60L)throw new BankRejected("Absoluter Raubschutz ist noch aktiv.");
                extra=RobberyPolicy.BypassCost(s,sec.Bypasses);
            }
            long cost=checked(s.RobberyCost+extra);if(Balance(sponsor)<cost)throw new BankRejected($"Benötigt: {cost:N0} $ Bank.");
            return new(cost,extra,bypass,RobberyPolicy.Chance(s,sec,Security(sponsor,s,now).RobberSkill,Balance(sponsor),Balance(target),bypass),RobberyPolicy.MaxLoot(s,sec,Balance(target),bypass));
        }
    }
    public RobberyOrder OrderRobbery(string id,long period,string sponsor,string target,RobberySettings s,long now,long expectedCost)
    {
        if(!Key(id,90)||period!=Period)throw new BankRejected("Auftragsansicht veraltet.");
        return Atomic(()=>
        {
            if(GetRobbery(id) is {} old){if(old.Sponsor!=sponsor||old.Target!=target||old.Cost!=expectedCost)throw new BankRejected("Abweichende Auftragswiederholung.");return old;}
            var quote=QuoteRobbery(sponsor,target,s,now);if(quote.Cost!=expectedCost)throw new BankRejected("Preis geändert; Auftrag erneut bestätigen.");
            long pot=RobberyPolicy.Percent(quote.BypassCost,s.HighRollerProtectionBypassSuperpotShareBasisPoints,10000);
            var order=new RobberyOrder{Id=id,Period=period,Sponsor=sponsor,Target=target,SponsorName=SafeName(Text("SELECT name FROM accounts WHERE id=$p0",sponsor)??sponsor),TargetName=SafeName(Text("SELECT name FROM accounts WHERE id=$p0",target)??target),Cost=quote.Cost,BypassCost=quote.BypassCost,Bypassed=quote.Bypassed,ReferenceBank=Balance(target),Skill=Security(sponsor,s,now).RobberSkill,Created=now,Rules=s};
            var changes=new List<StateChange>{RobChange("order:"+id,order)};
            if(quote.Bypassed){var sec=Security(target,s,now);changes.Add(RobChange("security:"+target,sec with{Bypasses=checked(sec.Bypasses+1)}));}
            BookInside(new("rob-order:"+id,period,RobOwner,"Bankraubauftrag",[new(sponsor,-quote.Cost),new("system:superpot",pot),new("system:robfees",quote.Cost-pot)],changes.ToArray()),false);
            TitleAddInside(sponsor,("RobberyOrders",1));
            return order;
        });
    }
    public long UpgradePrice(string account,string kind,int level,RobberySettings s,long now)=>DiscountedUpgrade(account,UpgradeBasePrice(account,kind,level,s,now));
    private long UpgradeBasePrice(string account,string kind,int level,RobberySettings s,long now)
    {
        var sec=Security(account,s,now);
        return kind switch
        {
            "vault" when level>sec.Vault&&level<=3=>Math.Max(0,RobberyPolicy.VaultPrice(s,level)-RobberyPolicy.VaultPrice(s,sec.Vault)),
            "robber" when level==sec.RobberSkill+1&&level<=s.MaxRobberSkillLevel=>s.RobberSkillUpgradeCost,
            "spy" when level==sec.SpySkill+1&&level<=s.MaxSpySkillLevel=>s.SpySkillUpgradeCost,
            "alarm" or "dogs" or "tower" or "guard" when !sec.Protections.ContainsKey(kind)||!s.UpgradesValidUntilBankReset=>kind switch{"alarm"=>s.AlarmPrice,"dogs"=>s.DogsPrice,"tower"=>s.TowerPrice,_=>s.GuardPrice},
            _=>throw new BankRejected("Ausbau bereits aktiv oder ungültige Stufe.")
        };
    }
    public void BuyRobberyUpgrade(string id,long period,string account,string kind,int level,long price,RobberySettings s,long now)
    {
        Atomic(()=>
        {
            RobEnabled(s);RobAccount(account);if(period!=Period)throw new BankRejected("Bankperiode gewechselt.");
            string reason=$"Ausbau {account} {kind} {level} {price}";
            if(Text("SELECT reason FROM transactions WHERE id=$p0",id) is {} old){if(old!=reason)throw new BankRejected("Abweichende Ausbauwiederholung.");return 0;}
            if(UpgradePrice(account,kind,level,s,now)!=price)throw new BankRejected("Ausbaupreis geändert.");
            long basePrice=UpgradeBasePrice(account,kind,level,s,now);
            var sec=Security(account,s,now);
            if(kind=="vault")sec=sec with{Vault=level,VaultUntil=s.UpgradesValidUntilBankReset?long.MaxValue:now+s.VaultDurationDays*86400L};
            else if(kind=="robber")sec=sec with{RobberSkill=level};else if(kind=="spy")sec=sec with{SpySkill=level};
            else sec.Protections[kind]=s.UpgradesValidUntilBankReset?long.MaxValue:now+s.SecurityDurationDays*86400L;
            BookInside(new(id,period,RobOwner,reason,[new(account,-price),new("system:robfees",price)],[RobChange("security:"+account,sec)]),false);
            ConsumeDiscountInside(account,basePrice);
            if(kind=="vault"||kind is "alarm" or "dogs" or "tower" or "guard")TitleAddInside(account,("SecurityBuys",1),(kind=="vault"?"VaultUpgrades":"ProtectionsBought",1));
            return 0;
        });
    }
    public void PaySpy(string id,long period,string account,long expectedCost,RobberySettings s)
    {
        RobEnabled(s);RobAccount(account);if(expectedCost!=s.SpyCost)throw new BankRejected("Spionpreis geändert.");
        Atomic(()=>{var receipt=BookInside(new(id,period,RobOwner,"Spionageauftrag",[new(account,-s.SpyCost),new("system:robfees",s.SpyCost)]),false);if(!receipt.Duplicate)TitleAddInside(account,("SpyOrders",1));return 0;});
    }
    public void MarkRobberyRound(RobberySettings rules,string[] onlineSpies)
    {
        if(!rules.Enabled)return;RobberyPolicy.Validate(rules);
        foreach(var o in RobberyOrders(true,10000).Where(o=>o.Status=="queued"))Atomic(()=>
        {
            if(GetRobbery(o.Id)!.Status!="queued")return 0;
            var next=o with{Status="eligible",Rules=rules,Observers=onlineSpies.Where(BankPolicy.Steam).Distinct().Take(64).ToArray()};
            BookInside(new("rob-eligible:"+o.Id,Period,RobOwner,"Gültiges Rundenende",[],[RobChange("order:"+o.Id,next)]),false);return 0;
        });
    }
    public void ResumeRobbery(long now,Func<int,int>? random=null)
    {
        random??=RandomNumberGenerator.GetInt32;
        int Roll(int max){int n=random(max);if(n<0||n>=max)throw new InvalidDataException("Ungültiger Zufallswert.");return n;}
        foreach(var entry in RobberyOrders(true,10000).Where(o=>o.Status is "eligible" or "rolled"||o.Status=="done"&&o.Claim>0&&Balance(o.Sponsor)<Limit).Where(o=>!Blocked(o.Sponsor)&&(o.Status=="done"||!Blocked(o.Target))).Reverse().Take(128))
        {
            var o=GetRobbery(entry.Id)!;
            if(o.Status=="eligible")Atomic(()=>
            {
                o=GetRobbery(o.Id)!;if(o.Status!="eligible"||Blocked(o.Sponsor)||Blocked(o.Target))return 0;
                var s=o.Rules;var sec=Security(o.Target,s,now);long target=Balance(o.Target);
                int chance=RobberyPolicy.Chance(s,sec,o.Skill,Balance(o.Sponsor),target,o.Bypassed);int max=RobberyPolicy.MaxLoot(s,sec,target,o.Bypassed),min=Math.Min(s.MinLootPercent,max);
                bool success=Math.Min(o.ReferenceBank,target)>0&&Roll(100)<chance;int loot=success?min+Roll(max-min+1):0;
                long stolen=RobberyPolicy.Percent(Math.Min(o.ReferenceBank,target),loot);success=success&&stolen>0;
                string[] revealed=s.SpyLogEnabled?o.Observers.Where(id=>{int level=Security(id,s,now).SpySkill;return level>0&&Roll(100)<Math.Min(100,level*s.PassiveSpyReportChancePerLevelPercent);}).ToArray():[];
                var next=o with{Status="rolled",Executed=now,Success=success,Chance=chance,Loot=loot,Stolen=stolen,TargetBank=target,Revealed=revealed};
                BookInside(new("rob-roll:"+o.Id,Period,RobOwner,"Bankraubergebnis festgelegt",[],[RobChange("order:"+o.Id,next)]),false);return 0;
            });
            // Outcome is committed separately before money moves. A retry never rolls again.
            o=GetRobbery(entry.Id)!;
            if(o.Status=="rolled")Atomic(()=>
            {
                o=GetRobbery(o.Id)!;if(o.Status!="rolled"||Blocked(o.Sponsor)||Blocked(o.Target))return 0;
                long stolen=Math.Min(o.Stolen,Balance(o.Target)),paid=Math.Min(stolen,Math.Max(0,Limit-Balance(o.Sponsor))),claim=stolen-paid;bool success=o.Success&&stolen>0;
                var s=o.Rules;var sec=Security(o.Target,s,now);var next=o with{Status="done",Stolen=stolen,Claim=claim,Success=success};
                var changes=new[]{RobChange("order:"+o.Id,next),RobChange("security:"+o.Target,sec with{ProtectedAt=now,ProtectedUntil=now+(success?s.ProtectionHoursAfterSuccess:s.ProtectionHoursAfterFail)*3600L}),RobChange("pair:"+o.Sponsor+":"+o.Target,now+s.SameTargetCooldownHours*3600L)};
                BookInside(new("rob-settle:"+o.Id,Period,RobOwner,"Bankraub ausgeführt",[new(o.Target,-stolen),new(o.Sponsor,paid),new("system:robclaims",claim)],changes),false);
                TitleAddInside(o.Sponsor,(success?"SuccessfulRobberies":"FailedRobberyOrders",1),("TotalStolen",stolen));
                TitleAddInside(o.Target,(success?"RobberyVictimCount":"DefendedRobberies",1),("TotalRobbedFrom",stolen));return 0;
            });
            o=GetRobbery(entry.Id)!;
            if(o.Status=="done"&&o.Claim>0&&!Blocked(o.Sponsor)&&Balance(o.Sponsor)<Limit)Atomic(()=>
            {
                o=GetRobbery(o.Id)!;long paid=Math.Min(o.Claim,Math.Max(0,Limit-Balance(o.Sponsor)));if(paid<=0)return 0;
                BookInside(new("rob-claim:"+o.Id+":"+o.Claim,Period,RobOwner,"Bankraub-Beute nachgezahlt",[new(o.Sponsor,paid),new("system:robclaims",-paid)],[RobChange("order:"+o.Id,o with{Claim=o.Claim-paid})]),false);return 0;
            });
        }
    }
    public string[] SpyLog(string account,RobberySettings s,long now,int limit)
    {
        int level=Security(account,s,now).SpySkill;if(level==0)throw new BankRejected("Spion-Log ab Spionstufe 1 verfügbar.");if(!s.SpyLogEnabled)throw new BankRejected("Spion-Log ausgeschaltet.");
        var events=RobberyOrders(false,10000).Where(o=>o.Status=="done"&&o.Rules.SpyLogEnabled).OrderByDescending(o=>o.Executed).Take(Math.Max(s.SpyLogMaxGlobalEvents,s.SpyLogMaxEntriesPerPlayer)).ToArray();
        var revealed=events.Where(o=>o.Revealed.Contains(account)).Take(s.SpyLogMaxEntriesPerPlayer).Select(o=>o.Id).ToHashSet();
        return events.Where(o=>s.SpyLogRecordUnknownEvents||revealed.Contains(o.Id)).Take(Math.Clamp(limit,1,100)).Select(o=>
        {
            string time=DateTimeOffset.FromUnixTimeSeconds(o.Executed).ToLocalTime().ToString("dd.MM. HH:mm");if(!revealed.Contains(o.Id))return time+" · Unbekanntes Ereignis";
            return time+" · Ziel: "+o.TargetName+(level>=2?" · Räuber: "+o.SponsorName:"")+(level>=3?o.Success?" · Erfolg":" · Fehlschlag":"")+(level>=4?$" · Beute {o.Stolen:N0} $ ({o.Loot}%)":"")+(level>=5?$" · Chance {o.Chance}% · Zielbank {o.TargetBank:N0} $ · Referenz {o.ReferenceBank:N0} $":"");
        }).ToArray();
    }
}
