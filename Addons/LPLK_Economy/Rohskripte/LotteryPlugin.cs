using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;

public sealed partial class Plugin
{
    private string? lotRound;
    private int lotRoundNumber=-1;
    private bool LottoAvailable(IPlayer p)=>CanUse(p)&&settings.Lotto.Enabled;
    private int LottoPlayers()=>Core.PlayerManager.GetAllValidPlayers().Count(p=>Human(p)&&p.Controller.TeamNum is 2 or 3&&!p.Controller.ControllingBot);
    private string? LottoDenied()
    {
        var game=ready?Core.EntitySystem.GetGameRules():null;
        bool open=lotRound is not null&&bank!.GetLotteryRound(lotRound) is {Status:"open"} r&&r.Period==bank.Period;
        return LotteryPolicy.Denied(settings.Lotto,game is not null,open,game?.WarmupPeriod??true,game is null?0:LottoPlayers());
    }
    private void BeginLotteryRound()=>BettingWork(()=>
    {
        if(!ready||Core.EntitySystem.GetGameRules() is not {} game)return;
        if(lotRound is not null&&lotRoundNumber==game.TotalRoundsPlayed&&bank!.GetLotteryRound(lotRound)?.Status=="open")return;
        EndLotteryRound(-1);bank!.CheckReset(settings,ResetNow);
        if(!Active||!settings.Lotto.Enabled||game.WarmupPeriod&&!settings.Lotto.AllowBuyDuringWarmup)return;
        lotRound=Guid.NewGuid().ToString("N");lotRoundNumber=game.TotalRoundsPlayed;bank.StartLotteryRound(lotRound,settings.Lotto);
        if(!game.WarmupPeriod&&LottoDenied() is null)
        {
            foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))
            {
                if(!LottoAvailable(p)||bank.Blocked(Id(p)))continue;
                bank.Open(Id(p),p.Name);var before=bank.LotteryPlan(Id(p));int oldDiscount=bank.UpgradeDiscount(Id(p));
                var ticket=bank.BuyLottery("auto:"+lotRound+":"+Id(p),lotRound,Id(p),0,true);
                if(ticket is not null){LottoUnlockNotice(p,before.Bought);if(ticket.Count<before.Count)p.SendChat($"[LPLK Lotto] Bankguthaben reicht für {ticket.Count}/{before.Count} Dauerlose.");}
                if(settings.Lotto.PermanentTicketUpgradeDiscountAnnounce&&oldDiscount==0&&bank.UpgradeDiscount(Id(p))>0&&!bank.Muted(Id(p)))p.SendChat($"[LPLK Lotto] Einmaliger Ausbaurabatt gewonnen: {bank.UpgradeDiscount(Id(p))}% für Tresor, Schutz, Spion oder Räuber.");
            }
        }
        ApplyRoundLotteryBonuses();
        int count=bank.LotteryTickets(lotRound).Sum(t=>t.Count);
        if(settings.Lotto.AnnounceOnRoundStart&&count>0)LottoBroadcast($"{count} Lose für diese Runde; Superpot {bank.Superpot:N0} $.");
    });
    private void EndLotteryRound(int winner)
    {
        string? finished=lotRound;lotRound=null;if(finished is null)return;
        var rules=bank!.GetLotteryRound(finished)!.Rules;
        bool valid=winner>=0&&ready&&Core.EntitySystem.GetGameRules() is {} game&&LotteryPolicy.DrawAllowed(rules,game.WarmupPeriod,LottoPlayers(),winner);
        bank.CloseLotteryRound(finished,valid);bank.ResumeLottery();
        var result=bank.GetLotteryRound(finished)!;
        if(rules.AnnounceDrawResult&&bank.LotteryTickets(finished).Length>0)
        {
            if(result.Status=="drawn")LottoBroadcast($"Ziehung: {Ledger.SafeName(bank.Accounts(int.MaxValue).First(a=>a.Id==result.Winner).Name)} gewinnt {result.Prize:N0} $ Bank + {result.Jackpot:N0} $ Superpot. Superpot verbleibend: {bank.Superpot:N0} $.");
            else if(result.Status=="closing")LottoBroadcast("Ziehung gespeichert; Auszahlung wartet auf eine offene Cash-Prüfung.");
            else LottoBroadcast(result.Status=="refunded"?"Ungültige/unterbrochene Runde: bezahlte Lose erstattet. Offene Erstattungen bei Banklimit bleiben gespeichert.":"Ungültige Runde: Lose gemäß Rundeneinstellung ohne Erstattung beendet.");
        }
    }
    private void LottoBroadcast(string message){foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))if(!bank!.Muted(Id(p)))p.SendChat("[LPLK Lotto] "+message);}
    private void LottoUnlockNotice(IPlayer p,long before)
    {
        if(settings.Lotto.AnnouncePermanentUnlocks&&bank!.PermanentLotteryLimit(Id(p),settings.Lotto,bank.LotteryPlan(Id(p)).Bought)>bank.PermanentLotteryLimit(Id(p),settings.Lotto,before))p.SendChat($"[LPLK Lotto] Dauerlos-Slot freigeschaltet: jetzt bis {bank.PermanentLotteryLimit(Id(p),settings.Lotto,bank.LotteryPlan(Id(p)).Bought)} Dauerlose.");
    }
    private void BuyLotto(IPlayer p,string amount,string id,string round,long period)
    {
        Require(p,false);if(!LottoAvailable(p)||lotRound!=round||bank!.Period!=period)throw new BankRejected("Lottoansicht veraltet oder ausgeschaltet.");
        if(LottoDenied() is {} denied)throw new BankRejected(denied);
        var rules=bank.GetLotteryRound(round)!.Rules;int count=(int)BankPolicy.Amount(amount,rules.MaxSingleTicketsPerPlayerPerDraw,1,rules.MaxSingleTicketsPerPlayerPerDraw);
        long before=bank.LotteryPlan(Id(p)).Bought;var t=bank.BuyLottery(id,round,Id(p),count)!;LottoUnlockNotice(p,before);
        p.SendChat($"[LPLK Lotto] {t.Count} Lose für {t.Cost:N0} $ Bank gekauft.");
    }
    private void ChangeLottoPlan(IPlayer p,int count,string id,long period,long revision)
    {
        if(!Human(p)||bank is null||failure is not null)throw new BankRejected("Bank nicht verfügbar.");
        // Cancellation remains possible when lottery is switched off; it never refunds already bought tickets.
        if(count>0){Require(p,false);if(!LottoAvailable(p)||LottoDenied() is {} )throw new BankRejected(LottoDenied()??"Lotto nicht verfügbar.");}
        bank.Open(Id(p),p.Name);bank.SetLotteryPlan(id,period,Id(p),count,revision,settings.Lotto);
        p.SendChat(count==0?"[LPLK Lotto] Dauerlose gekündigt; bereits gekaufte Lose bleiben für diese Ziehung gültig.":$"[LPLK Lotto] {count} Dauerlose aktiv, bis zu {count*settings.Lotto.TicketPrice:N0} $ Bank je gültiger Runde; nur online und soweit Guthaben reicht.");
    }
    [Command("lotto")][CommandAlias("lotterie")][CommandAlias("los")][CommandAlias("lotto",registerRaw:true)][CommandAlias("lotterie",registerRaw:true)][CommandAlias("los",registerRaw:true)]
    public void LottoCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){ctx.Reply($"LPLK_Economy Lotto: enabled={settings.Lotto.Enabled}; openRounds={bank?.OpenLotteryRounds}; superpot={bank?.Superpot}; refunds={bank?.LotteryRefunds}; !lotto kaufen|dauerlos|kündigen|status.");return;}
        Guard(p,()=>
        {
            string action=ctx.Args.FirstOrDefault()?.Trim().ToLowerInvariant()??"menu";
            if(action is "kündigen" or "kuendigen" or "cancel" or "stop" or "aus") {var plan=bank?.LotteryPlan(Id(p))??throw new BankRejected("Bank nicht verfügbar.");ChangeLottoPlan(p,0,NewId(),bank!.Period,plan.Revision);return;}
            if(!LottoAvailable(p))throw new BankRejected("Lotto nicht verfügbar.");
            if(action is "status" or "info"){LottoInfo(p);return;}
            if(action is "kaufen" or "buy" or "los" or "lose" or "ticket" or "tickets"){BuyLotto(p,LegacyAmount(ctx.Args.ElementAtOrDefault(1)??"1"),NewId(),lotRound??"",bank!.Period);return;}
            if(action is "dauerlos" or "auto" or "dauerlose" or "abo" or "permanent"){var plan=bank!.LotteryPlan(Id(p));int free=Math.Max(0,bank!.PermanentLotteryLimit(Id(p),settings.Lotto,plan.Bought)-plan.Count);int count=(int)BankPolicy.Amount(LegacyAmount(ctx.Args.ElementAtOrDefault(1)??"1"),free,1,free);ChangeLottoPlan(p,plan.Count+count,NewId(),bank.Period,plan.Revision);return;}
            if(action!="menu")p.SendChat("[LPLK Lotto] Nutzung: !lotto kaufen Anzahl|all; dauerlos Anzahl|all; kündigen; status.");OpenLotto(p);
        });
    }
    private void OpenLotto(IPlayer p)
    {
        if(!LottoAvailable(p))throw new BankRejected("Lotto nicht verfügbar.");
        Page(p,"Lotto · Banklose",_=>[("Kaufen",LottoAmounts),("Dauerlose verwalten",LottoPermanent),("Losstatus",LottoInfo),("Regeln",LottoRules)],a=>a.ExecuteCommand("sw_menu"));
    }
    private void LottoInfo(IPlayer p)
    {
        if(!LottoAvailable(p))throw new BankRejected("Lotto nicht verfügbar.");var tickets=lotRound is null?[]:bank!.LotteryTickets(lotRound);var plan=bank!.LotteryPlan(Id(p));
        p.SendChat($"[LPLK Lotto] Pool {tickets.Sum(t=>t.Count)} Lose / {tickets.Sum(t=>t.Cost):N0} $; deine Lose {tickets.Where(t=>t.Account==Id(p)).Sum(t=>t.Count)}. Superpot {bank.Superpot:N0} $.");
        p.SendChat($"[LPLK Lotto] Dauerlose {plan.Count}/{bank!.PermanentLotteryLimit(Id(p),settings.Lotto,plan.Bought)}; Fortschritt {plan.Bought:N0} gekaufte Lose. "+(LottoDenied()??"Loskauf offen."));
    }
    private void LottoAmounts(IPlayer p)=>Page(p,"Lotto · Lose kaufen",_=>
    {
        string round=lotRound??"";long period=bank!.Period;var rules=bank.GetLotteryRound(round)?.Rules??settings.Lotto;
        return rules.TicketAmountButtons.Where(n=>n<=rules.MaxSingleTicketsPerPlayerPerDraw).Distinct().Select(n=>($"{n} Lose · {n*rules.TicketPrice:N0} $",(Action<IPlayer>)(a=>
        {string id=NewId();Confirm(a,$"{n} Lose für {n*rules.TicketPrice:N0} $ kaufen?",b=>BuyLotto(b,n.ToString(System.Globalization.CultureInfo.InvariantCulture),id,round,period),OpenLotto);}))).ToArray();
    },OpenLotto);
    private void LottoPermanent(IPlayer p)=>Page(p,"Lotto · Dauerlose",a=>
    {
        var plan=bank!.LotteryPlan(Id(a));int max=bank!.PermanentLotteryLimit(Id(a),settings.Lotto,plan.Bought);
        return [($"Dauerlose {plan.Count}/{max} · +1 aktivieren",b=>LottoPlanConfirm(b,plan.Count+1,plan.Revision)),("Aktivieren",b=>LottoPlanConfirm(b,max,plan.Revision)),("Kündigen",b=>LottoPlanConfirm(b,0,plan.Revision)),("Kosten",LottoInfo)];
    },OpenLotto);
    private void LottoPlanConfirm(IPlayer p,int count,long revision){string id=NewId();long period=bank!.Period;Confirm(p,count==0?"Dauerlose kündigen?":$"{count} Dauerlose · bis {count*settings.Lotto.TicketPrice:N0} $/Runde?",a=>ChangeLottoPlan(a,count,id,period,revision),OpenLotto);}
    private void LottoAdmin(IPlayer p)=>Page(p,"Economy · Lotto",_=>[($"Lotto: {(settings.Lotto.Enabled?"AN":"AUS")}",a=>{Save(settings with{Lotto=settings.Lotto with{Enabled=!settings.Lotto.Enabled}});LottoAdmin(a);}),
        ("Lose",LottoTicketSettings),
        ("Ziehung",LottoDrawSettings),
        ("Boni",LottoBonusMenu)],EconomySettingsMenu,true);
    private void LottoChoice(IPlayer p,string title,int[] values,Func<LotterySettings,int,LotterySettings> change,Action<IPlayer> back)=>Page(p,title,_=>values.Select(v=>(v.ToString("N0"),(Action<IPlayer>)(a=>{Save(settings with{Lotto=change(settings.Lotto,v)});a.SendChat("[LPLK Lotto] Gespeichert. Lospreis und Ziehungsregeln gelten ab der nächsten Runde.");back(a);}))).ToArray(),back,true);
    private void LottoTicketSettings(IPlayer p)=>Page(p,"Lotto · Lose & Dauerlose",_=>[
        ($"Lospreis: {settings.Lotto.TicketPrice:N0} $",a=>LottoChoice(a,"Lotto · Preis pro Los",[100,250,500,1000,2500],(s,v)=>s with{TicketPrice=v},LottoTicketSettings)),
        ($"Loslimit: {settings.Lotto.MaxSingleTicketsPerPlayerPerDraw}",a=>LottoChoice(a,"Lotto · Loslimit",[1,5,10,20,50],(s,v)=>s with{MaxSingleTicketsPerPlayerPerDraw=v},LottoTicketSettings)),
        ($"Basis-Dauerlose: {settings.Lotto.MaxPermanentTicketsPerPlayer}",a=>LottoChoice(a,"Lotto · Basis-Dauerlose",[0,1,3,5,10],(s,v)=>s with{MaxPermanentTicketsPerPlayer=v},LottoTicketSettings)),
        ($"Dauerloslimit: {settings.Lotto.MaxPermanentTicketsTotal}",a=>LottoChoice(a,"Lotto · Dauerlos-Gesamtlimit",[5,10,15,20],(s,v)=>s with{MaxPermanentTicketsTotal=v},LottoTicketSettings))],LottoAdmin,true);
    private void LottoDrawSettings(IPlayer p)=>Page(p,"Lotto · Ziehung & Superpot",_=>[
        ($"Mindestspieler: {settings.Lotto.MinActivePlayersForLotto}",a=>LottoChoice(a,"Lotto · Aktive Menschen",[2,3,4,6],(s,v)=>s with{MinActivePlayersForLotto=v},LottoDrawSettings)),
        ($"Jackpotchance: {settings.Lotto.JackpotChanceBasisPoints/100m:0.##} %",a=>LottoChoice(a,"Lotto · Chance in Basispunkten",[0,100,200,500,1000],(s,v)=>s with{JackpotChanceBasisPoints=v},LottoDrawSettings)),
        ($"Superpotreset: {(settings.Lotto.ResetSuperpotWithBankReset?"AN":"AUS")}",a=>{Save(settings with{Lotto=settings.Lotto with{ResetSuperpotWithBankReset=!settings.Lotto.ResetSuperpotWithBankReset}});LottoDrawSettings(a);}),
        ($"Rundenerstattung: {(settings.Lotto.RefundOnInvalidRoundEnd?"AN":"AUS")}",a=>{Save(settings with{Lotto=settings.Lotto with{RefundOnInvalidRoundEnd=!settings.Lotto.RefundOnInvalidRoundEnd}});LottoDrawSettings(a);})],LottoAdmin,true);
}







