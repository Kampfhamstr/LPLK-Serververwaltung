using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private string? bonusRound;
    private long bonusPeriod;
    private int bonusRoundNumber;
    private bool bonusRoundEnded,cashCollectionMapBlocked;
    private readonly Dictionary<string,(string Session,int Cash)> deathCash=[];
    private readonly List<string> collectionReports=[];
    private void BeginBonusRound()
    {
        if(!Active||!ready||Core.EntitySystem.GetGameRules() is not {} game||game.WarmupPeriod)return;
        string id=boot+":"+titleMapEpoch+":"+game.TotalRoundsPlayed;if(bonusRound==id)return;
        bonusRound=id;bonusPeriod=bank!.Period;bonusRoundNumber=game.TotalRoundsPlayed+1;bonusRoundEnded=false;deathCash.Clear();
        var c=settings.Lotto.CashToSuperpot;
        foreach(string report in collectionReports)
            foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&p.Controller.TeamNum is 2 or 3&&(!c.DebugMessageToAdminsOnly||module.CanAdmin(p))))if(c.SendDebugMessageNextRound)p.SendChat("[LPLK Lotto] "+report);
        collectionReports.Clear();
        if(c.BlockIfRoundStartCashAboveBankLimit)foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&p.Controller.TeamNum is 2 or 3&&!p.Controller.ControllingBot))
            try{if(Cash(p)>settings.MapCashBlockLimit)cashCollectionMapBlocked=true;}catch(BankRejected){cashCollectionMapBlocked=true;}
    }
    private void ApplyRoundLotteryBonuses()
    {
        if(!Active||lotRound is null||!settings.LottoAbilityBonus.Enabled||LottoDenied() is not null)return;
        var game=Core.EntitySystem.GetGameRules();if(game is null||game.WarmupPeriod&&!settings.LottoAbilityBonus.AllowDuringWarmup)return;
        foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))BettingWork(()=>
        {
            if(!LottoAvailable(p)||p.Controller.ControllingBot||bank!.Blocked(Id(p)))return;
            bank.Open(Id(p),p.Name);var bonuses=bank.RollLotteryBonuses(lotRound,Id(p),BankPolicy.Pistol(settings,game.TotalRoundsPlayed+1),settings.LottoAbilityBonus);
            for(int n=0;n<bonuses.Length;n++)
            {
                var bonus=bonuses[n];if(bonus.Status!="ready")continue;long paid=0;
                if(bonus.Kind=="CashBonus")
                {
                    if(p.PlayerPawn is not {IsValid:true} pawn)continue;
                    int before=Cash(p);uint handle=Core.EntitySystem.GetRefEHandle(pawn).Raw;byte team=p.Controller.TeamNum;
                    var intent=bank.PrepareLotteryBonusCash(lotRound,Id(p),n,p.SessionId.ToString(),before);
                    if(intent is not null){string round=lotRound;ExecuteBetCash(p,intent,handle,team,()=>LottoAvailable(p)&&lotRound==round&&settings.LottoAbilityBonus.Enabled);paid=intent.After-intent.Before;}
                }
                else paid=bank.ApplyLotteryBonus(lotRound,Id(p),n);
                string text=bonus.Kind switch{"CashBonus"=>$"+{paid:N0} $ Cash","BankBonus"=>$"+{paid:N0} $ Bank","FreeTicket"=>$"+{paid} Gratislose für diese Ziehung",_=>$"Nächster gegnerischer Kill in dieser Runde: +{paid:N0} $ Bank"};
                if(settings.LottoAbilityBonus.AnnounceToPlayer&&!bank.Muted(Id(p)))p.SendChat("[LPLK Lotto] Bonus: "+text);
                if(settings.LottoAbilityBonus.ShowCenterMessage)p.SendCenter("Lotto-Bonus: "+text);
                if(settings.LottoAbilityBonus.AnnouncePublic)LottoBroadcast(Ledger.SafeName(p.Name)+": "+text);
            }
        });
    }
    private void BonusDeath(IPlayer? victim,IPlayer? attacker)
    {
        if(!Active||!ready||bonusRound is null||bonusRoundEnded||bonusPeriod!=bank!.Period||Core.EntitySystem.GetGameRules()?.WarmupPeriod!=false)return;
        var c=settings.Lotto.CashToSuperpot;
        if(victim is not null&&Human(victim)&&victim.Controller.TeamNum is 2 or 3&&!victim.Controller.ControllingBot&&settings.Lotto.Enabled&&c.Enabled&&c.TrackCashOnPlayerDeath&&BonusPolicy.CollectionReason(c,bonusRoundNumber,settings.MaxRounds)!="")
        {
            try{int cash=Cash(victim);int amount=BonusPolicy.CollectAmount(c,cash,0);if(amount>0)deathCash[Id(victim)]=(victim.SessionId.ToString(),amount);}catch(BankRejected){/* An uncertain native read is never turned into a cash snapshot. */}
        }
        if(victim is null||attacker is null||!Human(victim)||!Human(attacker)||attacker.Controller.ControllingBot||victim.Controller.ControllingBot||lotRound is null)return;
        bool opposing=attacker.Controller.TeamNum is 2 or 3&&victim.Controller.TeamNum is 2 or 3&&attacker.Controller.TeamNum!=victim.Controller.TeamNum;
        long paid=bank.ClaimLotteryKill(lotRound,Id(attacker),Id(victim),opposing);if(paid>0)attacker.SendChat($"[LPLK Lotto] Killbonus: +{paid:N0} $ Bank.");
    }
    private void EndBonusRound(int winner)
    {
        if(bonusRound is null||bonusRoundEnded)return;bonusRoundEnded=true;
        try
        {
            if(!Active||!ready||bonusPeriod!=bank!.Period||!settings.Lotto.Enabled||!settings.Lotto.CashToSuperpot.Enabled)return;
            var game=Core.EntitySystem.GetGameRules();var c=settings.Lotto.CashToSuperpot;
            if(!RobberyPolicy.ValidRound(settings.BankRobbery,game is not null,game?.WarmupPeriod??true,LottoPlayers(),winner))return;
            string reason=BonusPolicy.CollectionReason(c,bonusRoundNumber,settings.MaxRounds);if(reason=="")return;
            if(c.BlockIfRoundStartCashAboveBankLimit&&cashCollectionMapBlocked){collectionReports.Add("Rest-Cash übersprungen: erhöhtes oder nicht sicher lesbares Startgeld auf dieser Map.");return;}
            long total=0;int count=0,offline=0;var processed=new HashSet<string>();
            var online=Core.PlayerManager.GetAllValidPlayers().Where(Human).ToArray();
            foreach(var p in online.Where(p=>p.Controller.TeamNum is 2 or 3&&!p.Controller.ControllingBot))
            {
                string account=Id(p);processed.Add(account);if(bank.Blocked(account))continue;
                try
                {
                    int before=Cash(p);var snap=deathCash.GetValueOrDefault(account);int amount=BonusPolicy.CollectAmount(c,before,snap.Session==p.SessionId.ToString()?snap.Cash:0);if(amount<=0)continue;
                    if(p.PlayerPawn is not {IsValid:true} pawn)continue;uint handle=Core.EntitySystem.GetRefEHandle(pawn).Raw;byte team=p.Controller.TeamNum;
                    bank.Open(account,p.Name);string id="collect:"+bonusRound+":"+account;
                    var intent=bank.PrepareCashCollection(id,bonusPeriod,account,p.SessionId.ToString(),before,amount,c.SetPlayerCashToZero);
                    if(intent is not null)ExecuteBetCash(p,intent,handle,team,()=>ready&&bonusPeriod==bank.Period&&settings.Lotto.CashToSuperpot.Enabled);
                    total+=amount;count++;if(c.NotifyAffectedPlayers&&!bank.Muted(account))p.SendChat($"[LPLK Lotto] {amount:N0} $ Rest-Cash wegen {reason} in den Superpot eingezahlt.");
                }
                catch(BankRejected e){p.SendChat("[LPLK Lotto] Rest-Cash nicht bestätigt: "+e.Message);}
            }
            if(c.CollectDisconnectedDeathSnapshots)foreach(var item in deathCash.Where(x=>!processed.Contains(x.Key)&&!online.Any(p=>Id(p)==x.Key)))
            {
                if(bank.Blocked(item.Key))continue;int amount=BonusPolicy.CollectAmount(c,0,item.Value.Cash,true);if(amount<=0)continue;
                bank.Open(item.Key,bank.Accounts(int.MaxValue).FirstOrDefault(a=>a.Id==item.Key)?.Name??"Spieler");bank.PrepareCashCollection("collect:"+bonusRound+":"+item.Key,bonusPeriod,item.Key,item.Value.Session,0,amount,false);total+=amount;count++;offline++;
            }
            string report=$"Runde {bonusRoundNumber}: {total:N0} $ Rest-Cash von {count} Spielern, davon {offline} nur Todessnapshot, wegen {reason}.";
            collectionReports.Add(report);if(c.AnnounceCollection&&total>0)LottoBroadcast(report);
        }
        finally{deathCash.Clear();}
    }
    private void LottoRules(IPlayer p)=>Page(p,"Lotto · Regeln",_=>
    {
        var rows=new List<(string,Action<IPlayer>)>{("Gewinne",a=>{if(!LottoAvailable(a))throw new BankRejected("Lotto nicht verfügbar.");a.SendChat($"[LPLK Lotto] Los {settings.Lotto.TicketPrice:N0} $ Bank; {settings.Lotto.RoundPrizePercent}% Gewinn, {settings.Lotto.SuperpotPercent}% Superpot, {settings.Lotto.HouseCutPercent}% Hausanteil. Jedes Los gleiche Chance. Gratislose sind finanziert; bei Abbruch werden nur selbst bezahlte Lose erstattet.");})};
        if(settings.LottoAbilityBonus.Enabled||settings.Lotto.PermanentTicketUpgradeDiscountEnabled||settings.Lotto.CashToSuperpot.Enabled||bank!.UpgradeDiscount(Id(p))>0)rows.Add(("Boni",LottoBonusInfo));return rows;
    },OpenLotto);
    private void LottoBonusInfo(IPlayer p)
    {
        if(!LottoAvailable(p))throw new BankRejected("Lotto nicht verfügbar.");
        var s=settings.LottoAbilityBonus;
        if(s.Enabled)p.SendChat($"[LPLK Lotto] Rundenstart-Bonus: {s.ChancePerTicketBasisPoints/100m:0.##}% je vorhandenem Los, höchstens {s.MaxBonusesPerPlayerPerRound}. Cash, Bank, Gratislose oder nächster gegnerischer Kill; kein Cashbonus in Pistolenrunden, keine Heilung/Rüstung.");
        int discount=bank!.UpgradeDiscount(Id(p));if(settings.Lotto.PermanentTicketUpgradeDiscountEnabled||discount>0)p.SendChat($"[LPLK Lotto] Einmaliger Ausbaurabatt: {discount}%. Gilt für den nächsten Tresor-, Schutz-, Spion- oder Räuberausbau; bis Bankreset.");
        if(settings.Lotto.CashToSuperpot.Enabled)p.SendChat($"[LPLK Lotto] Rest-Cash-Sammlung gemäß Regeln am Ende von Runde {settings.MaxRounds/2} / {settings.MaxRounds}; Status im Adminmenü. Todessnapshots werden getrennt von live abgezogenem Cash verbucht.");
    }
    private void LottoBonusMenu(IPlayer p)=>Page(p,"Lotto · Boni & Rest-Cash",_=>[
        ($"Lottofähigkeiten: {(settings.LottoAbilityBonus.Enabled?"AN":"AUS")}",a=>{Save(settings with{LottoAbilityBonus=settings.LottoAbilityBonus with{Enabled=!settings.LottoAbilityBonus.Enabled}});LottoBonusMenu(a);}),
        ($"Dauerlos-Rabatte: {(settings.Lotto.PermanentTicketUpgradeDiscountEnabled?"AN":"AUS")}",a=>{Save(settings with{Lotto=settings.Lotto with{PermanentTicketUpgradeDiscountEnabled=!settings.Lotto.PermanentTicketUpgradeDiscountEnabled}});LottoBonusMenu(a);}),
        ($"Rest-Cash-Sammlung: {(settings.Lotto.CashToSuperpot.Enabled?"AN":"AUS")}",a=>{Save(settings with{Lotto=settings.Lotto with{CashToSuperpot=settings.Lotto.CashToSuperpot with{Enabled=!settings.Lotto.CashToSuperpot.Enabled}}});LottoBonusMenu(a);}),
        ("Details",a=>a.SendChat($"[LPLK Lotto] Bonuschance {settings.LottoAbilityBonus.ChancePerTicketBasisPoints/100m:0.##}%, Rabattchance {settings.Lotto.PermanentTicketUpgradeDiscountChanceBasisPoints/100m:0.##}% je Dauerlos. Rest-Cash-Mapsperre {cashCollectionMapBlocked}, offene Cashvorgänge {bank?.PendingCount}. Zahlenregeln unter LottoAbilityBonus und Lotto in settings.json."))],LottoAdmin,true);
}

