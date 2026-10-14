using System.Globalization;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;

public sealed partial class Plugin
{
    private readonly Dictionary<ulong,long> bountyLives=[];
    private string? bountyRound;
    private long bountyRoundPeriod;
    private bool bountyRoundEnded=true;
    private bool BountyAvailable(IPlayer p)=>CanUse(p)&&settings.BankEnabled&&settings.Bounty.Enabled;
    private void RequireBounty(IPlayer p){Require(p);if(!BountyAvailable(p))throw new BankRejected("Kopfgeld ausgeschaltet.");}
    private bool BountyCandidate(IPlayer actor,IPlayer target)=>Human(target)&&target.Controller.TeamNum is 2 or 3&&(!settings.Bounty.BlockSelfBounty||actor.SteamID!=target.SteamID)&&(!settings.Bounty.OnlyEnemyTeam||actor.Controller.TeamNum is 2 or 3&&actor.Controller.TeamNum!=target.Controller.TeamNum)&&(!settings.Bounty.OnlyAliveTargets||target.IsAlive)&&!target.Controller.ControllingBot&&(settings.Bounty.AllowStacking||!bank!.Bounties().Any(o=>o.Target==Id(target)));
    private IPlayer[] BountyTargetsFor(IPlayer p)=>Core.PlayerManager.GetAllValidPlayers().Where(x=>BountyCandidate(p,x)).GroupBy(x=>x.SteamID).Select(g=>g.First()).ToArray();
    private IPlayer BountyTarget(IPlayer actor,string id,string session)
    {
        RequireBounty(actor);return BountyTargetsFor(actor).FirstOrDefault(p=>Id(p)==id&&p.SessionId.ToString()==session)??throw new BankRejected("Kopfgeldziel nicht mehr verfügbar; Ziel erneut wählen.");
    }
    private IPlayer ResolveBountyTarget(IPlayer p,string query)
    {
        query=query.Trim().Trim('"');var candidates=BountyTargetsFor(p);var exact=candidates.Where(x=>Id(x)==query||x.Name.Equals(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        var found=exact.Length>0?exact:candidates.Where(x=>x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        return query.Length>0&&found.Length==1?found[0]:throw new BankRejected("Kopfgeldziel nicht eindeutig online gefunden; SteamID64 oder genaueren Namen verwenden.");
    }
    private CashContext BountyPlacementContext(IPlayer p,string? source=null)
    {
        var c=Context(p);if(!c.Known||c.Warmup&&!settings.Bounty.AllowDuringWarmup)throw new BankRejected("Kopfgeld in dieser Phase nicht freigegeben.");
        if(source is not null&&!BountyPolicy.Funding(settings.Bounty,source,c.Warmup))throw new BankRejected("Kopfgeldquelle gesperrt; im Warmup gegebenenfalls Bank verwenden.");
        if(source=="Cash"&&(!c.Alive||c.ControllingBot||settings.BlockCashAfterDeath&&c.Died))throw new BankRejected("Cash nur mit eigener lebender Spielfigur; nach Tod gesperrt.");return c;
    }
    private long BountyCashAvailable(IPlayer p)
    {
        try{BountyPlacementContext(p,"Cash");int cash=Cash(p);return cash<=settings.MaxPlayerCash?cash:0;}catch(BankRejected){return 0;}
    }
    private string AutoBountySource(IPlayer p,string amount)
    {
        var c=BountyPlacementContext(p);bool all=amount=="all";long n=all?0:BankPolicy.Amount(amount,0,1,int.MaxValue);
        return BountyPolicy.Choose(settings.Bounty,bank!.Balance(Id(p)),BountyCashAvailable(p),n,all,c.Warmup);
    }
    private void PlacePlayerBounty(IPlayer p,string target,string targetSession,string amount,string? source,string id,long period,bool percent=false,long? expectedBalance=null)
    {
        RequireBounty(p);var other=BountyTarget(p,target,targetSession);if(bank!.Period!=period)throw new BankRejected("Bankperiode gewechselt; Kopfgeld neu öffnen.");
        source??=AutoBountySource(p,amount);var context=BountyPlacementContext(p,source);
        if(expectedBalance.HasValue&&bank.Balance(Id(p))!=expectedBalance.Value)throw new BankRejected("Bankguthaben für Prozentwahl verändert; Betrag erneut bestätigen.");
        int before=source=="Cash"?Cash(p):0;if(source=="Cash"&&before>settings.MaxPlayerCash)throw new BankRejected("Cash über freigegebenem Limit.");
        long maximum=BountyPolicy.Maximum(settings.Bounty,source=="Bank"?bank.Balance(Id(p)):before,percent),value=BankPolicy.Amount(amount,maximum,settings.Bounty.MinBounty,maximum);
        string actorSession=source=="Cash"?p.SessionId.ToString():"";uint pawn=0;byte team=0;
        if(source=="Cash"&&settings.Bounty.TakeMoneyImmediately)
        {
            if(p.PlayerPawn is not {IsValid:true} live)throw new BankRejected("Eigene Spielfigur fehlt.");
            long now=Environment.TickCount64;if(cashThrottle.TryGetValue(p.SteamID,out var prior)&&now-prior<750)throw new BankRejected("Bitte kurz warten.");cashThrottle[p.SteamID]=now;
            pawn=Core.EntitySystem.GetRefEHandle(live).Raw;team=p.Controller.TeamNum;
        }
        var order=bank.PlaceBounty(id,period,Id(p),target,value,source,settings.Bounty,percent,actorSession,before);
        if(order.Status=="pending")ExecuteBetCash(p,bank.Intent(id)!,pawn,team,()=>BountyAvailable(p)&&Core.PlayerManager.GetAllValidPlayers().Any(t=>Human(t)&&Id(t)==target&&t.SessionId.ToString()==targetSession&&t.Controller.TeamNum is 2 or 3&&!t.Controller.ControllingBot&&(!settings.Bounty.OnlyAliveTargets||t.IsAlive)&&(!settings.Bounty.OnlyEnemyTeam||t.Controller.TeamNum!=p.Controller.TeamNum))&&(BountyPlacementContext(p,source) with{Seconds=context.Seconds})==context);
        p.SendChat($"[LPLK Kopfgeld] {value:N0} $ aus {source} auf {Ledger.SafeName(other.Name)} gesetzt; insgesamt {bank.BountyTotal(target):N0} $.");
        if(settings.Bounty.AnnounceBountyPlaced)BountyBroadcast($"{Ledger.SafeName(p.Name)} setzt {value:N0} $ auf {Ledger.SafeName(other.Name)}; gesamt {bank.BountyTotal(target):N0} $.");
        if(settings.BountyMarker.ShowCenterMessageOnBountyPlaced)BountyHud(target,Ledger.SafeName(other.Name),bank.BountyTotal(target));
    }
    [Command("kopfgeld")][CommandAlias("bounty")][CommandAlias("kopfgeld",registerRaw:true)][CommandAlias("bounty",registerRaw:true)]
    public void BountyCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){WriteStatus();ctx.Reply($"LPLK_Economy Kopfgeld: enabled={Active&&settings.BankEnabled&&settings.Bounty.Enabled}; active={bank?.Bounties().Count(o=>o.Status=="active")}; claims={bank?.BountyClaims().Sum(c=>c.Remaining)}; !kopfgeld Spieler|SteamID64 Betrag|all / Betrag / Spieler.");return;}
        Guard(p,()=>
        {
            RequireBounty(p);if(ctx.Args.Length==0){BountyMenu(p);return;}
            if(ctx.Args.Length==1)
            {
                string? amount=null;try{var parsed=LegacyAmount(ctx.Args[0]);if(parsed=="all"||long.TryParse(parsed,out long n)&&n>0)amount=parsed;}catch(BankRejected){}
                // SteamID64 is a target, despite also being a positive number.
                if(amount is not null&&!BankPolicy.Steam(ctx.Args[0])){BountyTargets(p,amount);return;}
                var target=ResolveBountyTarget(p,ctx.Args[0]);BountySources(p,Id(target),target.SessionId.ToString());return;
            }
            var other=ResolveBountyTarget(p,string.Join(' ',ctx.Args[..^1]));PlacePlayerBounty(p,Id(other),other.SessionId.ToString(),LegacyAmount(ctx.Args[^1]),null,NewId(),bank!.Period);
        });
    }
    private void BountyMenu(IPlayer p)
    {
        RequireBounty(p);Page(p,"Kopfgeld",_=>[("Kopfgeld aussetzen",a=>BountyTargets(a,null)),("Aktive Kopfgelder",BountyList),("Aufträge",BountyOwn),("Regeln",BountyInfo)],a=>a.ExecuteCommand("sw_menu"));
    }
    private void BountyTargets(IPlayer p,string? fixedAmount)
    {
        RequireBounty(p);BountyPlacementContext(p);long period=bank!.Period;
        Page(p,fixedAmount is null?"Kopfgeld · Ziel wählen":"Kopfgeld · "+fixedAmount+" $",a=>BountyTargetsFor(a).Select(t=>
        {
            string id=Id(t),session=t.SessionId.ToString(),name=Ledger.SafeName(t.Name);
            return (name+" · "+id[^4..],(Action<IPlayer>)(b=>{if(bank!.Period!=period)throw new BankRejected("Ansicht aus vorheriger Bankperiode.");if(fixedAmount is null)BountySources(b,id,session);else BountyConfirmation(b,id,session,fixedAmount,null);}));
        }).ToArray(),BountyMenu);
    }
    private void BountySources(IPlayer p,string target,string session)
    {
        var other=BountyTarget(p,target,session);var context=BountyPlacementContext(p);
        Page(p,"Kopfgeld · "+Ledger.SafeName(other.Name),_=>
        {
            var rows=new List<(string,Action<IPlayer>)>();if(BountyPolicy.Funding(settings.Bounty,"Bank",context.Warmup))rows.Add(("Aus Bankguthaben",a=>BountyAmounts(a,target,session,"Bank")));
            if(BountyPolicy.Funding(settings.Bounty,"Cash",context.Warmup))rows.Add(("Bargeld",a=>BountyAmounts(a,target,session,"Cash")));return rows;
        },a=>BountyTargets(a,null));
    }
    private void BountyAmounts(IPlayer p,string target,string session,string source)
    {
        BountyTarget(p,target,session);BountyPlacementContext(p,source);
        Page(p,"Kopfgeld · "+source,_=>
        {
            var rows=settings.BountyAmountButtons.Where(v=>v>=settings.Bounty.MinBounty&&v<=BountyPolicy.Maximum(settings.Bounty,int.MaxValue)).Select(v=>(v.ToString("N0")+" $",(Action<IPlayer>)(a=>BountyConfirmation(a,target,session,v.ToString(CultureInfo.InvariantCulture),source)))).ToList();
            rows.Add(("Alles Mögliche",a=>BountyConfirmation(a,target,session,"all",source)));if(source=="Bank"&&settings.Bounty.AllowBankPercentBounty)rows.Add(("Bankanteil",a=>BountyPercent(a,target,session)));return rows;
        },a=>BountySources(a,target,session));
    }
    private void BountyPercent(IPlayer p,string target,string session)
    {
        BountyTarget(p,target,session);if(!settings.Bounty.AllowBankPercentBounty)throw new BankRejected("Prozentwahl ausgeschaltet.");
        Page(p,"Kopfgeld · Bankanteil",_=>settings.Bounty.BankPercentButtons.Distinct().Select(percent=>(percent+" %",(Action<IPlayer>)(a=>{RequireBounty(a);long balance=bank!.Balance(Id(a)),amount=BountyPolicy.Percent(balance,percent);BountyConfirmation(a,target,session,amount.ToString(CultureInfo.InvariantCulture),"Bank",true,balance);}))).ToArray(),a=>BountyAmounts(a,target,session,"Bank"));
    }
    private void BountyConfirmation(IPlayer p,string target,string session,string amount,string? source,bool percent=false,long? balance=null)
    {
        var other=BountyTarget(p,target,session);source??=AutoBountySource(p,amount);BountyPlacementContext(p,source);
        long max=BountyPolicy.Maximum(settings.Bounty,source=="Bank"?bank!.Balance(Id(p)):BountyCashAvailable(p),percent);long value=BankPolicy.Amount(amount,max,settings.Bounty.MinBounty,max);
        string id=NewId();long period=bank!.Period;Confirm(p,$"{value:N0} $ ({source}) · {Ledger.SafeName(other.Name)}",a=>PlacePlayerBounty(a,target,session,value.ToString(CultureInfo.InvariantCulture),source,id,period,percent,balance),BountyMenu);
    }
    private void BountyList(IPlayer p)
    {
        RequireBounty(p);Page(p,"Aktive Kopfgelder",_=>bank!.Bounties().Where(o=>o.Status=="active").GroupBy(o=>o.Target).OrderByDescending(g=>g.Sum(o=>o.Amount)).Select(g=>(g.First().TargetName+" · "+g.Sum(o=>o.Amount).ToString("N0")+" $",(Action<IPlayer>)(a=>a.SendChat($"[LPLK Kopfgeld] {g.First().TargetName}: {g.Sum(o=>o.Amount):N0} $ aus {g.Count()} Aufträgen.")))).ToArray(),BountyMenu);
    }
    private void BountyOwn(IPlayer p)
    {
        RequireBounty(p);Page(p,"Kopfgeld · Eigene Vorgänge",a=>bank!.Bounties().Where(o=>o.Sponsor==Id(a)).Select(o=>($"{o.TargetName} · {o.Amount:N0} $ · {(o.Status=="pending"?"Cashprüfung":"aktiv")}",(Action<IPlayer>)(b=>b.SendChat($"[LPLK Kopfgeld] Auftrag {o.Id}: {o.Source}, {o.Amount:N0} $, Ziel {o.TargetName}; {o.Status}."))))
            .Concat(bank.BountyClaims().Where(c=>c.Account==Id(a)).Select(c=>($"{(c.Kind=="refund"?"Erstattung":"Gewinn")} · {c.Remaining:N0} $ offen",(Action<IPlayer>)(b=>b.SendChat("[LPLK Kopfgeld] Anspruch bleibt gespeichert, bis Cashprüfung abgeschlossen bzw. Platz im Konto vorhanden ist."))))).ToArray(),BountyMenu);
    }
    private void BountyInfo(IPlayer p)
    {
        RequireBounty(p);p.SendChat($"[LPLK Kopfgeld] Ab {settings.Bounty.MinBounty:N0} $. !kopfgeld / !bounty, auch Spieler Betrag, Betrag zur Zielwahl, k/m/b und all/alles/max. Direkte Befehle bevorzugen {(settings.Bounty.PreferBankForDirectBountyCommands?"Bank":"Cash")}.");
        p.SendChat($"[LPLK Kopfgeld] Auszahlung: {settings.Bounty.RewardMode}; Teamkills {(settings.Bounty.IgnoreTeamKills?"zählen nicht":"zählen")}; Rundenübernahme {(settings.Bounty.PersistAcrossRounds?"AN":"AUS")}; Erstattung bei Ziel-Disconnect {(settings.Bounty.RefundIfTargetDisconnects?"AN":"AUS")}. Ein Bankreset setzt Titel zurück, bereits finanzierte Kopfgelder bleiben wie bisher bestehen.");
    }
    private void BountyDeath(IPlayer? victim,IPlayer? attacker)
    {
        if(!ready||!Active||!settings.Bounty.Enabled||victim is null||attacker is null||!Human(victim)||!Human(attacker)||victim.SteamID==attacker.SteamID||attacker.Controller.ControllingBot||victim.Controller.ControllingBot||attacker.Controller.TeamNum is not (2 or 3)||victim.Controller.TeamNum is not (2 or 3))return;
        bank!.Open(Id(attacker),attacker.Name);
        string eventId=$"{boot}:{titleMapEpoch}:{Id(victim)}:{bountyLives.GetValueOrDefault(victim.SteamID)}";
        long total=bank.ClaimBounty(eventId,Id(attacker),Id(victim),attacker.Controller.TeamNum==victim.Controller.TeamNum);
        if(total>0){attacker.SendChat($"[LPLK Kopfgeld] {total:N0} $ Anspruch für {Ledger.SafeName(victim.Name)} gesichert.");if(settings.Bounty.AnnounceBountyClaimed)BountyBroadcast($"{Ledger.SafeName(attacker.Name)} kassiert {total:N0} $ Kopfgeld auf {Ledger.SafeName(victim.Name)}.");}
        DeliverBountyClaims();
    }
    private void BountyDisconnected(IPlayer? p)
    {
        if(p is null||!BankPolicy.Steam(Id(p))||!Active||!settings.Bounty.Enabled||!settings.Bounty.RefundIfTargetDisconnects)return;
        // Do not resolve a late disconnect for a replacement session.
        if(Core.PlayerManager.GetAllValidPlayers().Any(x=>Human(x)&&x.SteamID==p.SteamID&&x.SessionId!=p.SessionId))return;
        bank!.RefundBounties(Id(p),"Ziel hat Server verlassen");
    }
    private void BeginBountyRound()
    {
        bountyRound=ready&&Core.EntitySystem.GetGameRules()is {} rules?$"{boot}:{titleMapEpoch}:{rules.TotalRoundsPlayed+1}":null;bountyRoundPeriod=bank!.Period;bountyRoundEnded=false;
        if(!Active||!settings.Bounty.Enabled||!settings.BountyMarker.Enabled||!settings.BountyMarker.AnnounceBountiesOnRoundStart)return;
        var top=bank.Bounties().Where(o=>o.Status=="active").GroupBy(o=>o.Target).OrderByDescending(g=>g.Sum(o=>o.Amount)).ThenBy(g=>g.First().TargetName).Take(settings.BountyMarker.MaxBountiesAnnounced).ToArray();
        foreach(var g in top)BountyBroadcast($"Aktiv: {g.First().TargetName} · {g.Sum(o=>o.Amount):N0} $.");
        if(settings.BountyMarker.ShowCenterMessageOnRoundStart&&top.FirstOrDefault()is {} first)BountyHud(first.Key,first.First().TargetName,first.Sum(o=>o.Amount));
    }
    private void EndBountyRound(int winner)
    {
        if(bountyRoundEnded)return;bountyRoundEnded=true;
        var rules=ready?Core.EntitySystem.GetGameRules():null;
        if(!Active||rules is null||rules.WarmupPeriod||bountyRound is null||bank!.Period!=bountyRoundPeriod||winner is not (2 or 3)&&(!settings.Lotto.ProcessEconomyOnRoundEndWithoutWinner||LottoPlayers()<settings.Lotto.MinActivePlayersForEconomyRoundEnd))return;
        bank.BountySurvivors(bountyRound,bountyRoundPeriod,dead.Select(id=>id.ToString(CultureInfo.InvariantCulture)).ToArray());
        if(!settings.Bounty.PersistAcrossRounds)bank.RefundBounties(null,"Rundenende");DeliverBountyClaims();
    }
    private void DeliverBountyClaims()
    {
        if(bank is null||failure is not null||!ready)return;
        foreach(var claim in bank.BountyClaims())
        {
            if(bank.Blocked(claim.Account))continue;
            var p=ready?Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>Human(x)&&Id(x)==claim.Account):null;
            int before=0,toCash=0;long discard=0;string session="bank";uint pawn=0;byte team=0;
            if(p is not null&&claim.Mode!="Bank")
            {
                if(p.Controller.ControllingBot||p.Controller.TeamNum is not (2 or 3)||p.PlayerPawn is not {IsValid:true} live)continue;
                try{before=Cash(p);if(before>settings.MaxPlayerCash)continue;}catch(BankRejected){continue;}
                toCash=(int)Math.Min(claim.Remaining,settings.MaxPlayerCash-before);session=p.SessionId.ToString();pawn=Core.EntitySystem.GetRefEHandle(live).Raw;team=p.Controller.TeamNum;
                if(claim.Mode=="Cash")discard=claim.Remaining-toCash;
            }
            long toBank=Math.Min(claim.Remaining-toCash-discard,Math.Max(0,bank.Limit-bank.Balance(claim.Account)));
            if(toCash+toBank+discard==0)continue;
            var intent=bank.PayBountyClaim(NewId(),claim.Id,claim.Account,session,before,toCash,toBank,discard);
            if(intent is not null){try{ExecuteBetCash(p!,intent,pawn,team,()=>ready);}catch(BankRejected e){p!.SendChat("[LPLK Kopfgeld] "+e.Message);continue;}}
            p?.SendChat($"[LPLK Kopfgeld] {(claim.Kind=="refund"?"Erstattet":"Ausgezahlt")}: {toCash:N0} $ Cash, {toBank:N0} $ Bank."+(discard>0?$" {discard:N0} $ gemäß Cash-only-Regel verfallen.":""));
        }
    }
    private void BountyBroadcast(string message){foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))if(!bank!.Muted(Id(p)))p.SendChat("[LPLK Kopfgeld] "+message);}
    private void BountyHud(string target,string name,long amount)
    {
        if(!Active||!settings.Bounty.Enabled||!settings.BountyMarker.Enabled)return;
        var other=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>Human(x)&&Id(x)==target);if(settings.BountyMarker.OnlyEnemyTeamCanSeeHud&&(other is null||other.Controller.TeamNum is not (2 or 3)))return;
        foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(x=>Human(x)&&x.Controller.TeamNum is 2 or 3&&(!settings.BountyMarker.OnlyEnemyTeamCanSeeHud||x.Controller.TeamNum!=other!.Controller.TeamNum)))if(!bank!.Muted(Id(p)))p.SendCenter($"Kopfgeld: {Ledger.SafeName(name)} · {amount:N0} $");
    }
    private void BountyAdmin(IPlayer p)=>Page(p,"Kopfgeld · Verwaltung",_=>[("Finanzierung",BountyFundingAdmin),("Zielregeln",BountyRulesAdmin),("Hinweise",BountyMarkerAdmin),("Status",a=>a.SendChat($"[LPLK Kopfgeld] Aktive/offene Aufträge {bank?.Bounties().Length}; Zahlungsansprüche {bank?.BountyClaims().Sum(c=>c.Remaining):N0} $. Mindestbetrag {settings.Bounty.MinBounty:N0} $, große Beträge {(settings.Bounty.LargeBountyAmountsEnabled?"AN":"AUS")}. Erweiterte Werte: settings.json > Bounty, BountyMarker, BountyAmountButtons."))],EconomySettingsMenu,true);
    private void BountyFundingAdmin(IPlayer p)=>Page(p,"Kopfgeld · Finanzierung",_=>[($"Kopfgeld: {(settings.Bounty.Enabled?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{Enabled=!settings.Bounty.Enabled}});BountyFundingAdmin(a);}),($"Bank: {(settings.Bounty.AllowBankFunding?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{AllowBankFunding=!settings.Bounty.AllowBankFunding}});BountyFundingAdmin(a);}),($"Cash: {(settings.Bounty.AllowCashFunding?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{AllowCashFunding=!settings.Bounty.AllowCashFunding}});BountyFundingAdmin(a);}),($"Warmup: {(settings.Bounty.AllowDuringWarmup?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{AllowDuringWarmup=!settings.Bounty.AllowDuringWarmup}});BountyFundingAdmin(a);})],BountyAdmin,true);
    private void BountyRulesAdmin(IPlayer p)=>Page(p,"Kopfgeld · Ziele & Runden",_=>[($"Gegnerziele: {(settings.Bounty.OnlyEnemyTeam?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{OnlyEnemyTeam=!settings.Bounty.OnlyEnemyTeam}});BountyRulesAdmin(a);}),($"Addieren: {(settings.Bounty.AllowStacking?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{AllowStacking=!settings.Bounty.AllowStacking}});BountyRulesAdmin(a);}),($"Rundenbindung: {(settings.Bounty.PersistAcrossRounds?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{PersistAcrossRounds=!settings.Bounty.PersistAcrossRounds}});BountyRulesAdmin(a);}),($"Disconnect-Erstattung: {(settings.Bounty.RefundIfTargetDisconnects?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{RefundIfTargetDisconnects=!settings.Bounty.RefundIfTargetDisconnects}});BountyRulesAdmin(a);})],BountyAdmin,true);
    private void BountyMarkerAdmin(IPlayer p)=>Page(p,"Kopfgeld · Hinweise",_=>[($"Chat/HUD: {(settings.BountyMarker.Enabled?"AN":"AUS")}",a=>{Save(settings with{BountyMarker=settings.BountyMarker with{Enabled=!settings.BountyMarker.Enabled}});BountyMarkerAdmin(a);}),($"Rundenhinweise: {(settings.BountyMarker.AnnounceBountiesOnRoundStart?"AN":"AUS")}",a=>{Save(settings with{BountyMarker=settings.BountyMarker with{AnnounceBountiesOnRoundStart=!settings.BountyMarker.AnnounceBountiesOnRoundStart}});BountyMarkerAdmin(a);}),($"Gegner-HUD: {(settings.BountyMarker.OnlyEnemyTeamCanSeeHud?"AN":"AUS")}",a=>{Save(settings with{BountyMarker=settings.BountyMarker with{OnlyEnemyTeamCanSeeHud=!settings.BountyMarker.OnlyEnemyTeamCanSeeHud}});BountyMarkerAdmin(a);}),($"Auszahlung melden: {(settings.Bounty.AnnounceBountyClaimed?"AN":"AUS")}",a=>{Save(settings with{Bounty=settings.Bounty with{AnnounceBountyClaimed=!settings.Bounty.AnnounceBountyClaimed}});BountyMarkerAdmin(a);})],BountyAdmin,true);
}
