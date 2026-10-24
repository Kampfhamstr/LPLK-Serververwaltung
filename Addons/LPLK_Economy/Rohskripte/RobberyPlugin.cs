using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Economy;

public sealed partial class Plugin
{
    private bool robberyRoundEnded=true;
    private static long RobNow=>DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private bool RobberyAvailable(IPlayer p)=>CanUse(p)&&settings.BankEnabled&&settings.BankRobbery.Enabled;
    private void RequireRobbery(IPlayer p){Require(p);if(!RobberyAvailable(p))throw new BankRejected("Bankraub & Sicherheit ausgeschaltet.");}
    private void EndRobberyRound(int winner)
    {
        if(robberyRoundEnded)return;robberyRoundEnded=true;
        if(!Active||!settings.BankEnabled||!settings.BankRobbery.Enabled)return;
        var game=ready?Core.EntitySystem.GetGameRules():null;
        if(!RobberyPolicy.ValidRound(settings.BankRobbery,game is not null,game?.WarmupPeriod??true,LottoPlayers(),winner))return;
        bank!.MarkRobberyRound(settings.BankRobbery,Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&p.Controller.TeamNum is 2 or 3).Select(Id).ToArray());
        ResumeRobbery();
    }
    private void ResumeRobbery()
    {
        if(bank is null||failure is not null)return;
        var before=bank.RobberyOrders(true,10000).Where(o=>o.Status is "eligible" or "rolled").Select(o=>o.Id).ToHashSet();
        bank.ResumeRobbery(RobNow);
        foreach(string id in before)
        {
            var o=bank.GetRobbery(id)!;if(o.Status!="done")continue;
            string result=o.Success?$"Erfolg · Beute {o.Stolen:N0} $" : "Fehlschlag · Gebühr verbraucht";
            foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))
            {
                if(bank.Muted(Id(p)))continue;
                if(Id(p)==o.Sponsor||Id(p)==o.Target||o.Revealed.Contains(Id(p))||o.Rules.AnnouncePublicResult)
                    p.SendChat($"[LPLK Bankraub] {o.SponsorName} → {o.TargetName}: {result}.");
                if(Id(p)==o.Sponsor&&o.Claim>0)p.SendChat($"[LPLK Bankraub] {o.Claim:N0} $ Beute wartet auf freien Platz im Bankkonto.");
            }
        }
    }
    [Command("bankraub")][CommandAlias("robbery")][CommandAlias("bankraub",registerRaw:true)][CommandAlias("robbery",registerRaw:true)]
    public void RobberyCommand(ICommandContext ctx)
    {
        if(ctx.Sender is {} p)Guard(p,()=>RobberyMenu(p));
        else {WriteStatus();ctx.Reply($"LPLK_Economy Bankraub: enabled={Active&&settings.BankEnabled&&settings.BankRobbery.Enabled}; open={bank?.RobberyOrders(true,10000).Count(o=>o.Status!="done")}; claims={bank?.RobberyClaims}");}
    }
    [Command("spionlog")][CommandAlias("spylog")][CommandAlias("bankraublog")][CommandAlias("spionlog",registerRaw:true)][CommandAlias("spylog",registerRaw:true)][CommandAlias("bankraublog",registerRaw:true)]
    public void SpyLogCommand(ICommandContext ctx)
    {
        if(ctx.Sender is {} p)Guard(p,()=>{RequireRobbery(p);var lines=bank!.SpyLog(Id(p),settings.BankRobbery,RobNow,8);if(lines.Length==0)p.SendChat("[LPLK Spion] Noch keine Berichte.");foreach(var line in lines)p.SendChat("[LPLK Spion] "+line);});
        else ctx.Reply("Spion-Log benötigt einen Spieler mit Spionstufe.");
    }
    private void RobberyMenu(IPlayer p)
    {
        RequireRobbery(p);
        Page(p,"Bankraub",_=>[("Überfall",RobberyTargets),("Tresore",VaultMenu),("Schutzmaßnahmen",ProtectionMenu),("Fähigkeiten",RobberySkills),("Spion beauftragen",SpyTargets),("Spionberichte",SpyReports),("Status",RobberyInfo)],OpenBank);
    }
    private IPlayer[] RobberyTargetsFor(IPlayer p)=>Core.PlayerManager.GetAllValidPlayers().Where(x=>Human(x)&&x.SteamID!=p.SteamID&&x.Controller.TeamNum is 2 or 3).GroupBy(x=>x.SteamID).Select(g=>g.First()).ToArray();
    private void RobberyTargets(IPlayer p)
    {
        RequireRobbery(p);
        if(RobberyTargetsFor(p).Length==0)p.SendChat("[LPLK Bankraub] Gerade ist kein anderer menschlicher Spieler im T- oder CT-Team als Ziel verfügbar. Bots zählen nicht.");
        Page(p,"Bankraub · Ziel wählen",a=>RobberyTargetsFor(a).Select(target=>
        {
            string id=Id(target),session=target.SessionId.ToString();
            return (Ledger.SafeName(target.Name),(Action<IPlayer>)(b=>RobberyConfirmation(b,id,session)));
        }).ToArray(),RobberyMenu);
    }
    private IPlayer CurrentRobberyTarget(IPlayer actor,string target,string session)
    {
        RequireRobbery(actor);var p=RobberyTargetsFor(actor).FirstOrDefault(x=>Id(x)==target&&x.SessionId.ToString()==session);
        if(p is null||!CanUse(p))throw new BankRejected("Zielverbindung nicht mehr verfügbar; neu auswählen.");
        bank!.Open(Id(p),p.Name);return p;
    }
    private void RobberyConfirmation(IPlayer p,string target,string session)
    {
        var other=CurrentRobberyTarget(p,target,session);var quote=bank!.QuoteRobbery(Id(p),target,settings.BankRobbery,RobNow);string id=NewId();long period=bank.Period;
        p.SendChat($"[LPLK Bankraub] Auftrag kostet {quote.Cost:N0} $ Bank, davon Schutz-Bypass {quote.BypassCost:N0} $. Gebühr bleibt auch bei Fehlschlag verbraucht. Ausführung am nächsten gültigen Rundenende.");
        Confirm(p,$"{Ledger.SafeName(other.Name)} · {quote.Cost:N0} $",a=>
        {
            CurrentRobberyTarget(a,target,session);var o=bank.OrderRobbery(id,period,Id(a),target,settings.BankRobbery,RobNow,quote.Cost);
            a.SendChat($"[LPLK Bankraub] Auftrag gegen {o.TargetName} für {o.Cost:N0} $ gespeichert.");
        },RobberyTargets);
    }
    private void RobberyUpgrades(IPlayer p)=>RobberyMenu(p);
    private void VaultMenu(IPlayer p)
    {
        RequireRobbery(p);Page(p,"Bankraub · Tresore",a=>new[]{(1,"Alter Stahltresor"),(2,"Moderner Stahltresor"),(3,"Titan-Tresor")}.Select(v=>
        {
            bool owned=bank!.Security(Id(a),settings.BankRobbery,RobNow).Vault>=v.Item1;
            string label=owned?v.Item2+" · bereits aktiv":v.Item2+" · "+bank.UpgradePrice(Id(a),"vault",v.Item1,settings.BankRobbery,RobNow).ToString("N0")+" $";
            return (label,(Action<IPlayer>)(b=>{if(owned){RequireRobbery(b);RobberyInfo(b);}else UpgradeConfirm(b,"vault",v.Item1,v.Item2,VaultMenu);}));
        }).ToArray(),RobberyUpgrades);
    }
    private void ProtectionMenu(IPlayer p)
    {
        RequireRobbery(p);Page(p,"Bankraub · Schutz",a=>new[]{("alarm","Alarmanlage"),("dogs","Wachhunde"),("tower","Wachturm"),("guard","Wachpersonal")}.Select(v=>
        {
            bool owned=bank!.Security(Id(a),settings.BankRobbery,RobNow).Protections.ContainsKey(v.Item1)&&settings.BankRobbery.UpgradesValidUntilBankReset;
            string label=owned?v.Item2+" · bis Bankreset aktiv":v.Item2+" · "+bank.UpgradePrice(Id(a),v.Item1,0,settings.BankRobbery,RobNow).ToString("N0")+" $";
            return (label,(Action<IPlayer>)(b=>{if(owned){RequireRobbery(b);RobberyInfo(b);}else UpgradeConfirm(b,v.Item1,0,v.Item2,ProtectionMenu);}));
        }).ToArray(),RobberyUpgrades);
    }
    private void RobberySkills(IPlayer p)
    {
        RequireRobbery(p);Page(p,"Bankraub · Fähigkeiten",a=>
        {
            var sec=bank!.Security(Id(a),settings.BankRobbery,RobNow);var rows=new List<(string,Action<IPlayer>)>();
            if(sec.RobberSkill<settings.BankRobbery.MaxRobberSkillLevel)rows.Add(($"Räuber Stufe {sec.RobberSkill+1} · {bank.UpgradePrice(Id(a),"robber",sec.RobberSkill+1,settings.BankRobbery,RobNow):N0} $",b=>UpgradeConfirm(b,"robber",sec.RobberSkill+1,"Räuber",RobberySkills)));
            if(sec.SpySkill<settings.BankRobbery.MaxSpySkillLevel)rows.Add(($"Spion Stufe {sec.SpySkill+1} · {bank.UpgradePrice(Id(a),"spy",sec.SpySkill+1,settings.BankRobbery,RobNow):N0} $",b=>UpgradeConfirm(b,"spy",sec.SpySkill+1,"Spion",RobberySkills)));
            rows.Add(("Aktuelle Fähigkeiten",RobberyInfo));return rows;
        },RobberyUpgrades);
    }
    private void UpgradeConfirm(IPlayer p,string kind,int level,string name,Action<IPlayer> back)
    {
        RequireRobbery(p);long cost=bank!.UpgradePrice(Id(p),kind,level,settings.BankRobbery,RobNow),period=bank.Period;string id=NewId();
        Confirm(p,$"{name} · {cost:N0} $ Bank · Rabatt {bank.UpgradeDiscount(Id(p))}%",a=>{RequireRobbery(a);bank.BuyRobberyUpgrade(id,period,Id(a),kind,level,cost,settings.BankRobbery,RobNow);a.SendChat("[LPLK Bankraub] Ausbau gekauft.");},back);
    }
    private void RobberyInfo(IPlayer p)
    {
        RequireRobbery(p);var sec=bank!.Security(Id(p),settings.BankRobbery,RobNow);
        p.SendChat($"[LPLK Bankraub] Tresorstufe {sec.Vault}/3 · Schutz {sec.Protections.Count}/4 · Räuber {sec.RobberSkill}/{settings.BankRobbery.MaxRobberSkillLevel} · Spion {sec.SpySkill}/{settings.BankRobbery.MaxSpySkillLevel}.");
        if(sec.ProtectedUntil>RobNow)p.SendChat($"[LPLK Bankraub] Raubschutz bis {DateTimeOffset.FromUnixTimeSeconds(sec.ProtectedUntil).ToLocalTime():dd.MM. HH:mm}.");
        foreach(var o in bank.RobberyOrders(true,10000).Where(o=>o.Sponsor==Id(p)).Take(10))p.SendChat($"[LPLK Bankraub] {o.TargetName}: {o.Status switch{"queued"=>"Wartet auf gültiges Rundenende","done"=>"Auszahlung offen",_=>"Abrechnung gespeichert"}} · offener Beuteanspruch {o.Claim:N0} $.");
    }
    private void SpyMenu(IPlayer p)=>RobberyMenu(p);
    private void SpyReports(IPlayer p)
    {
        RequireRobbery(p);var lines=bank!.SpyLog(Id(p),settings.BankRobbery,RobNow,100);
        Page(p,"Spionberichte",_=>lines.Select(line=>(line,(Action<IPlayer>)(a=>{RequireRobbery(a);a.SendChat("[LPLK Spion] "+line);}))).ToArray(),RobberyMenu);
    }
    private void SpyTargets(IPlayer p)
    {
        RequireRobbery(p);if(settings.BankRobbery.SpyScanAllOnlineTargets){SpyConfirm(p,null,null);return;}
        Page(p,"Spion · Ziel wählen",a=>RobberyTargetsFor(a).Select(t=>
        {string id=Id(t),session=t.SessionId.ToString();return(Ledger.SafeName(t.Name),(Action<IPlayer>)(b=>SpyConfirm(b,id,session)));}).ToArray(),SpyMenu);
    }
    private void SpyConfirm(IPlayer p,string? target,string? session)
    {
        RequireRobbery(p);long cost=settings.BankRobbery.SpyCost,period=bank!.Period;string id=NewId();bool all=settings.BankRobbery.SpyScanAllOnlineTargets;
        Confirm(p,$"Spionage · {cost:N0} $",a=>
        {
            RequireRobbery(a);if(all!=settings.BankRobbery.SpyScanAllOnlineTargets)throw new BankRejected("Spionage-Modus geändert.");
            var targets=target is null?RobberyTargetsFor(a):[CurrentRobberyTarget(a,target,session!)];
            foreach(var t in targets)bank.Open(Id(t),t.Name);
            targets=targets.Where(t=>CanUse(t)&&!bank.Blocked(Id(t))&&(settings.BankRobbery.SpyShowTargetsBelowRobberyMinimum||bank.Balance(Id(t))>=settings.BankRobbery.MinTargetBank)).OrderByDescending(t=>bank.Balance(Id(t))).Take(settings.BankRobbery.SpyMaxTargetsShown).ToArray();
            if(targets.Length==0)throw new BankRejected("Keine auswertbaren Online-Ziele; keine Gebühr berechnet.");
            bank.PaySpy(id,period,Id(a),cost,settings.BankRobbery);
            foreach(var t in targets)
            {
                var sec=bank.Security(Id(t),settings.BankRobbery,RobNow);long balance=bank.Balance(Id(t));int chance=RobberyPolicy.Chance(settings.BankRobbery,sec,bank.Security(Id(a),settings.BankRobbery,RobNow).RobberSkill,bank.Balance(Id(a)),balance,false);
                a.SendChat($"[LPLK Spion] {Ledger.SafeName(t.Name)}: Bank {balance:N0} $ · Tresor {sec.Vault} · Schutz {sec.Protections.Count}/4 · Chance ohne Bypass {chance}% · Beute bis {RobberyPolicy.MaxLoot(settings.BankRobbery,sec,balance,false)}% · Raubschutz {(sec.ProtectedUntil>RobNow?"aktiv":"aus")}.");
            }
        },SpyMenu);
    }
    private void RobberyAdminPrices(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RobberyAdminPricesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Aufträge",a=>a.SendChat($"[LPLK Bankraub] Auftrag {settings.BankRobbery.RobberyCost:N0} $, Spionage {settings.BankRobbery.SpyCost:N0} $. Preise in Bank-Dollar.")),        ("Ausbauten",a=>a.SendChat($"[LPLK Bankraub] Tresore {settings.BankRobbery.OldSteelVaultPrice:N0}/{settings.BankRobbery.ModernSteelVaultPrice:N0}/{settings.BankRobbery.TitanVaultPrice:N0} $; Räuber-Stufe {settings.BankRobbery.RobberSkillUpgradeCost:N0} $, Spion-Stufe {settings.BankRobbery.SpySkillUpgradeCost:N0} $.")),        ($"Periodenbindung: {(settings.BankRobbery.UpgradesValidUntilBankReset?"AN":"AUS")}",a=>a.SendChat("[LPLK Bankraub] Gültigkeit bestehender Käufe bleibt erhalten; weitere Regeln in BankRobbery in settings.json."))]))(p);
    private void RobberyAdminProtection(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RobberyAdminProtectionRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Schutzzeiten",a=>a.SendChat($"[LPLK Bankraub] Erfolg {settings.BankRobbery.ProtectionHoursAfterSuccess}h, Fehlschlag {settings.BankRobbery.ProtectionHoursAfterFail}h, gleiches Ziel {settings.BankRobbery.SameTargetCooldownHours}h; absoluter Schutz {settings.BankRobbery.HighRollerProtectionAbsoluteMinutes}min.")),        ($"High-Roller-Bypass: {(settings.BankRobbery.HighRollerProtectionBypassEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{BankRobbery=current.BankRobbery with{HighRollerProtectionBypassEnabled=!current.BankRobbery.HighRollerProtectionBypassEnabled}});RobberyAdminProtection(a);}),        ("Bypasskosten",a=>a.SendChat($"[LPLK Bankraub] Ab {settings.BankRobbery.HighRollerProtectionBypassTargetBankThreshold:N0} $ Zielbank; Start {settings.BankRobbery.HighRollerProtectionBypassBaseCost:N0} $, Faktor {settings.BankRobbery.HighRollerProtectionBypassCostMultiplier}, Maximum {settings.BankRobbery.HighRollerProtectionBypassMaxCost:N0} $; {settings.BankRobbery.HighRollerProtectionBypassSuperpotShareBasisPoints/100m}% Superpot."))]))(p);
    private void RobberyAdminStatus(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RobberyAdminStatusRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Offene Vorgänge",a=>a.SendChat($"[LPLK Bankraub] Offene Aufträge {bank?.RobberyOrders(true,10000).Count(o=>o.Status!="done")}, Beuteansprüche {bank?.RobberyClaims:N0} $. Bezahlte Aufträge bleiben über Neustarts erhalten.")),        ($"Spion-Log: {(settings.BankRobbery.SpyLogEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{BankRobbery=current.BankRobbery with{SpyLogEnabled=!current.BankRobbery.SpyLogEnabled}});RobberyAdminStatus(a);}),        ("Erweiterungen",a=>a.SendChat("[LPLK Bankraub] Titel und Lotto-Ausbaurabatte sind in Economy enthalten. Kopfgeld folgt als weiterer interner Bereich."))]))(p);
}

