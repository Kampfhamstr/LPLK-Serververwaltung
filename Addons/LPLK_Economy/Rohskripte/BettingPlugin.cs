using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;

public sealed partial class Plugin
{
    private string? betRound;
    private double betStarted;
    private bool betActive;
    private bool BetAvailable(IPlayer p)=>CanUse(p)&&settings.Betting.Enabled;
    private void BettingWork(Action action)
    {
        if(bank is null||failure is not null)return;
        try{action();}catch(BankRejected e){Core.Logger.LogWarning("LPLK_Economy: {Reason}",e.Message);}catch(Exception e){Fail(e);}
    }
    private BetContext BettingContext()
    {
        if(!ready)return default;var rules=Core.EntitySystem.GetGameRules();if(rules is null)return default;
        var people=Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&p.IsAlive&&!p.Controller.ControllingBot).ToArray();
        bool current=betRound is not null&&bank!.BettingRound(betRound) is {Status:"open"} r&&r.Period==bank.Period;
        return new(true,betActive&&current,rules.WarmupPeriod,rules.FreezePeriod,Environment.TickCount64/1000d-betStarted,people.Count(p=>p.Controller.TeamNum==2),people.Count(p=>p.Controller.TeamNum==3));
    }
    private void BeginBettingRound()=>BettingWork(()=>
    {
        EndBettingRound(0,false);bank!.CheckReset(settings,ResetNow);
        if(!Active||!settings.Betting.Enabled||!ready||Core.EntitySystem.GetGameRules() is not {} rules||rules.WarmupPeriod&&!settings.Betting.AllowBetsDuringWarmup)return;
        betRound=Guid.NewGuid().ToString("N");bank.StartBetRound(betRound,settings.Betting);betStarted=Environment.TickCount64/1000d;betActive=true;
        if(settings.Betting.AnnounceBettingOnRoundStart)BetBroadcast("Neue Wettrunde: !bet / !wette.");
    });
    private void EndBettingRound(int winner,bool onlineCash)
    {
        betActive=false;
        string? finished=betRound;if(finished is not null){bank!.CloseBetRound(finished,winner);betRound=null;}
        DeliverBetClaims(onlineCash,finished);
    }
    private void BetBroadcast(string message)
    {
        foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))if(!bank!.Muted(Id(p)))p.SendChat("[LPLK Wetten] "+message);
    }
    private void PlaceWager(IPlayer p,int side,string amount,string id,string round,long period)
    {
        Require(p,false);
        if(!BetAvailable(p)||betRound!=round||bank!.Period!=period)throw new BankRejected("Wettansicht veraltet oder Bereich ausgeschaltet.");
        var rules=bank.BettingRound(round)!.Rules;
        // Current switches apply immediately; payout rules remain those of the accepted round.
        if(BettingPolicy.Denied(settings.Betting,BettingContext()) is {} denied)throw new BankRejected(denied);
        var c=Context(p);if(!c.Known||c.ControllingBot||p.PlayerPawn is not {IsValid:true})throw new BankRejected("Wette nur mit eigenem gültigem Spielerkonto, keine Bot-Übernahme.");
        if(bank.BetTickets(round).Any(x=>x.Account==Id(p)&&x.Status!="cancelled"))throw new BankRejected("Bereits eine feste Wette in dieser Runde.");
        int before=Cash(p);if(before>settings.MaxPlayerCash)throw new BankRejected("Cash über dem freigegebenen Limit.");
        int value=BettingPolicy.Amount(amount,before,rules);string session=p.SessionId.ToString();uint pawn=Core.EntitySystem.GetRefEHandle(p.PlayerPawn!).Raw;byte team=p.Controller.TeamNum;
        var intent=bank.PrepareBetStake(id,round,Id(p),session,side,before,value,amount.Equals("all",StringComparison.OrdinalIgnoreCase));
        ExecuteBetCash(p,intent,pawn,team,()=>betActive&&betRound==round&&BetAvailable(p)&&BettingPolicy.Denied(settings.Betting,BettingContext()) is null);
        p.SendChat($"[LPLK Wetten] {value:N0} $ Cash auf {(side==2?"T":"CT")} gesetzt. Diese Wette ist fest.");
        if(settings.Betting.ShowCenterConfirmation)p.SendCenter($"Wette: {value:N0} $ auf {(side==2?"T":"CT")}");
        if(settings.Betting.AnnouncePlacedBets)BetBroadcast($"{Ledger.SafeName(p.Name)} setzt {value:N0} $ auf {(side==2?"T":"CT")}.");
    }
    private void ExecuteBetCash(IPlayer p,CashIntent intent,uint pawn,byte team,Func<bool> allowed)
    {
        if(intent.Status!="pending")throw new BankRejected("Cash-Vorgang bereits verarbeitet.");
        try
        {
            if(!Human(p)||Id(p)!=intent.Account||p.SessionId.ToString()!=intent.Session||p.Controller.ControllingBot||p.Controller.TeamNum!=team||p.PlayerPawn is not {IsValid:true} live||Core.EntitySystem.GetRefEHandle(live).Raw!=pawn||Cash(p)!=intent.Before||!allowed())throw new BankRejected("Spielerzustand inzwischen geändert.");
            var money=p.Controller.InGameMoneyServices!;money.Account=intent.After;money.AccountUpdated();p.Controller.InGameMoneyServicesUpdated();
            if(Cash(p)!=intent.After)throw new BankRejected("Cash-Änderung nicht bestätigt.");
            bank!.ResolveCash(intent.Id,true,"native:"+intent.Session);
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Economy: Cash-Vorgang {Id} bleibt zur Prüfung offen",intent.Id);throw new BankRejected("Cash-Buchung unklar; Konto bis zur Adminprüfung gesperrt. Nicht erneut ausführen.");}
    }
    private void DeliverBetClaims(bool onlineCash,string? finished=null)
    {
        foreach(var t in onlineCash&&finished is not null?bank!.BetTickets(finished).Where(x=>x.Claim>0):bank!.BankPayableClaims())
        {
            if(bank.Blocked(t.Account))continue;
            IPlayer? p=onlineCash&&ready?Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>Human(x)&&Id(x)==t.Account&&!x.Controller.ControllingBot):null;
            int before=0,toCash=0;uint pawn=0;byte team=0;string session="bank";
            if(p is not null)
            {
                try
                {
                    if(p.PlayerPawn is not {IsValid:true} live)throw new BankRejected("Figur nicht bereit.");
                    before=Cash(p);if(before>settings.MaxPlayerCash)throw new BankRejected("Cashlimit überschritten.");
                    pawn=Core.EntitySystem.GetRefEHandle(live).Raw;team=p.Controller.TeamNum;session=p.SessionId.ToString();toCash=(int)Math.Min(t.Claim,Math.Max(0,settings.MaxPlayerCash-before));
                }
                catch(BankRejected){p=null;before=0;toCash=0;}
            }
            long toBank=Math.Min(t.Claim-toCash,Math.Max(0,bank.Limit-bank.Balance(t.Account)));
            if(toBank+toCash==0)continue; // Keep the claim until there is room; never clip winnings.
            var intent=bank.PayBetClaim(NewId(),t.Round,t.Account,session,before,toCash,toBank);
            if(intent is not null)
            {
                try{ExecuteBetCash(p!,intent,pawn,team,()=>ready);}catch(BankRejected e){p!.SendChat("[LPLK Wetten] "+e.Message);continue;}
            }
            p?.SendChat($"[LPLK Wetten] Auszahlung/Erstattung: {toCash:N0} $ Cash, {toBank:N0} $ Bank.");
        }
    }
    [Command("bet")][CommandAlias("bt")][CommandAlias("wette")][CommandAlias("wetten")][CommandAlias("bet",registerRaw:true)][CommandAlias("bt",registerRaw:true)][CommandAlias("wette",registerRaw:true)][CommandAlias("wetten",registerRaw:true)]
    public void BetCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){ctx.Reply($"LPLK_Economy Wetten: enabled={settings.Betting.Enabled}; openRounds={bank?.OpenBetRounds}; claims={bank?.BetClaims}; Runde={betActive}; !bet t|ct Betrag|all.");return;}
        Guard(p,()=>
        {
            if(!BetAvailable(p))throw new BankRejected("Wetten nicht verfügbar.");
            if(ctx.Args.Length==0){OpenBet(p);return;}
            if(ctx.Args[0].ToLowerInvariant() is "cancel" or "abbrechen")throw new BankRejected("Eine gesetzte Wette kann nicht geändert oder abgebrochen werden.");
            if(ctx.Args.Length<2)throw new BankRejected("Nutzung: !bet t|ct Betrag|all.");
            PlaceWager(p,BettingPolicy.Side(ctx.Args[0]),LegacyAmount(ctx.Args[1]),NewId(),betRound??"",bank!.Period);
        });
    }
    [Command("bets")][CommandAlias("pool")][CommandAlias("wettenstatus")][CommandAlias("wettinfo")][CommandAlias("bets",registerRaw:true)][CommandAlias("pool",registerRaw:true)][CommandAlias("wettenstatus",registerRaw:true)][CommandAlias("wettinfo",registerRaw:true)]
    public void BetsCommand(ICommandContext ctx){if(ctx.Sender is {} p)Guard(p,()=>ShowBetInfo(p));else ctx.Reply("Wettübersicht im Spiel: !bets.");}
    private void ShowBetInfo(IPlayer p)
    {
        if(!BetAvailable(p))throw new BankRejected("Wetten nicht verfügbar.");var tickets=betRound is null?[]:bank!.BetTickets(betRound).Where(x=>x.Status=="accepted").ToArray();
        p.SendChat($"[LPLK Wetten] T-Pool {tickets.Where(x=>x.Side==2).Sum(x=>x.Amount):N0} $ | CT-Pool {tickets.Where(x=>x.Side==3).Sum(x=>x.Amount):N0} $.");
        var own=tickets.SingleOrDefault(x=>x.Account==Id(p));if(own is not null)p.SendChat($"[LPLK Wetten] Deine feste Wette: {own.Amount:N0} $ auf {(own.Side==2?"T":"CT")}.");
        if(bank!.PlayerBetClaims(Id(p)) is >0 and var claim)p.SendChat($"[LPLK Wetten] Noch zur Auszahlung vorgemerkt: {claim:N0} $. Banklimit oder offene Cash-Prüfung kann die Gutschrift verzögern.");
        p.SendChat("[LPLK Wetten] "+(BettingPolicy.Denied(settings.Betting,BettingContext())??"Wetten sind offen."));
    }
    private void OpenBet(IPlayer p)
    {
        if(!BetAvailable(p))throw new BankRejected("Wetten nicht verfügbar.");
        Page(p,"Wetten · Cash-Einsatz",_=>[("Auf T wetten",a=>BetAmounts(a,2)),("Auf CT wetten",a=>BetAmounts(a,3)),("Pools & eigene Wette",ShowBetInfo),("Regeln & Auszahlung",a=>a.SendChat("[LPLK Wetten] Eine feste Cash-Wette je Runde. !bet t|ct Betrag|all; all setzt das gesamte Cash auch über dem normalen Höchsteinsatz. Gewinn anteilig aus Gegenwetten; ohne Gegenwette Erstattung. Cash bis Limit, Rest Bank; ungeklärte Vorgänge bleiben gesperrt."))],a=>a.ExecuteCommand("sw_menu"));
    }
    private void BetAmounts(IPlayer p,int side)=>Page(p,$"Wetten · {(side==2?"T":"CT")}",_=>settings.Betting.AmountButtons.Where(x=>x>=settings.Betting.MinBet&&x<=settings.Betting.MaxBet).Select(v=>(v.ToString("N0")+" $ Cash",(Action<IPlayer>)(a=>BetConfirmation(a,side,v.ToString(System.Globalization.CultureInfo.InvariantCulture))))).Append(("Gesamtes Cash (all)",a=>BetConfirmation(a,side,"all"))).ToArray(),OpenBet);
    private void BetConfirmation(IPlayer p,int side,string amount)
    {
        string round=betRound??"",id=NewId();long period=bank!.Period;
        Confirm(p,$"{amount} Cash auf {(side==2?"T":"CT")}?",a=>PlaceWager(a,side,amount,id,round,period),OpenBet);
    }
    private void EconomySettingsMenu(IPlayer p)=>Page(p,"Economy · Einstellungen",_=>[("Bankregeln",AdminSettings),("Wettregeln",BetAdmin),("Lottoregeln",LottoAdmin),("Bankraubregeln",RobberyAdmin),("Titel & Wirtschaft",TitlesAdmin),("Kopfgeldregeln",BountyAdmin),("Kill-Belohnungen",KillRewardAdmin),($"Economy gesamt: {(settings.Enabled?"AN":"AUS")}",a=>{Save(settings with{Enabled=!settings.Enabled});EconomySettingsMenu(a);})],OpenAdmin,true);
    private void BetAdmin(IPlayer p)=>Page(p,"Economy · Wetten",_=>[($"Wetten: {(settings.Betting.Enabled?"AN":"AUS")}",a=>{Save(settings with{Betting=settings.Betting with{Enabled=!settings.Betting.Enabled}});BetAdmin(a);}),
        ("Öffnung & Teilnehmer",BetOpening),("Einsatz & Abrechnung",BetLimits),("Status & Hinweise",a=>a.SendChat($"[LPLK Wetten] Offene Runden {bank?.OpenBetRounds}; ausstehende Gewinne {bank?.BetClaims:N0} $. Angenommene Wetten werden auch nach Ausschalten abgerechnet; Regeländerungen zur Auszahlung gelten ab nächster Runde."))],EconomySettingsMenu,true);
    private void BetOpening(IPlayer p)=>Page(p,"Wetten · Öffnung",_=>[("Modus: "+settings.Betting.OpenMode,a=>{Save(settings with{Betting=settings.Betting with{OpenMode=settings.Betting.OpenMode=="Time"?"PlayersPerTeam":"Time"}});BetOpening(a);}),
        ("Lebende Menschen je Team: "+settings.Betting.MinHumanPlayersPerTeam,a=>Page(a,"Wetten · Mindestteilnehmer",_=>new[]{1,2,3,4,5}.Select(v=>(v.ToString(),(Action<IPlayer>)(b=>{Save(settings with{Betting=settings.Betting with{MinHumanPlayersPerTeam=v}});BetOpening(b);}))).ToArray(),BetOpening,true)),
        ("Zeitfenster",a=>Page(a,"Wetten · Sekunden ab Rundenstart",_=>new[]{15,30,60,90,120}.Select(v=>(v+" Sekunden",(Action<IPlayer>)(b=>{Save(settings with{Betting=settings.Betting with{BetOnlyFirstSeconds=v}});BetOpening(b);}))).ToArray(),BetOpening,true))],BetAdmin,true);
    private void BetLimits(IPlayer p)=>Page(p,"Wetten · Abrechnung",_=>[("Einsatzgrenzen",a=>a.SendChat($"[LPLK Wetten] {settings.Betting.MinBet}–{settings.Betting.MaxBet} $; all darf das gesamte Cash setzen. Eine feste Wette. Werte in settings.json > Betting.")),
        ($"Erstattung ohne richtige Wette: {(settings.Betting.RefundIfNoWinningBets?"AN":"AUS")}",a=>{Save(settings with{Betting=settings.Betting with{RefundIfNoWinningBets=!settings.Betting.RefundIfNoWinningBets}});BetLimits(a);}),
        ($"Erstattung bei Abbruch: {(settings.Betting.RefundOnDrawOrRestart?"AN":"AUS")}",a=>{Save(settings with{Betting=settings.Betting with{RefundOnDrawOrRestart=!settings.Betting.RefundOnDrawOrRestart}});BetLimits(a);}),
        ("Hausanteil: "+settings.Betting.HouseCutPercent+" %",a=>a.SendChat("[LPLK Wetten] Hausanteil und Rundungsreste werden im Journal ausgewiesen. Konfiguration: Betting.HouseCutPercent; wirksam ab nächster Runde."))],BetAdmin,true);
}



