using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;

namespace LPLK_Economy;

public sealed partial class Plugin
{
    private long nextBankPresence;
    private string BankServerName()=>Ledger.SafeName(Core.ConVar.Find<string>("hostname")?.Value??bank!.ServerScope);
    private void PublishBankPlayers(ulong exclude=0)
    {
        if(bank?.Networked!=true)return;
        var players=ready&&Active&&settings.BankEnabled&&settings.BankRobbery.Enabled
            ?Core.PlayerManager.GetAllValidPlayers().Where(p=>CanUse(p)&&p.SteamID!=exclude&&p.Controller.TeamNum is 2 or 3)
                .Select(p=>new BankPresence(Id(p),p.SessionId.ToString(),p.Name,bank.ServerScope,BankServerName())).ToArray():[];
        bank.PublishBankPresence(players,RobNow);
    }
    [EventListener<EventDelegates.OnWorldUpdate>]
    public void BankPresenceTick()
    {
        if(!module.Active||bank?.Networked!=true||Environment.TickCount64<nextBankPresence)return;
        nextBankPresence=Environment.TickCount64+2000;
        BettingWork(()=>PublishBankPlayers());
        BettingWork(()=>
        {
            foreach(var player in Core.PlayerManager.GetAllValidPlayers().Where(Human))
                foreach(var notice in bank.BankNotices(Id(player)))
                {
                    if(!bank.Muted(Id(player)))player.SendLplkChat(notice.Message);
                    bank.AckBankNotice(Id(player),notice.Id,RobNow);
                }
        });
    }
    private BankPresence[] NetworkTargets(IPlayer actor)
    {
        RequireRobbery(actor);PublishBankPlayers();
        return bank!.BankPlayers(RobNow).Where(p=>p.Account!=Id(actor)&&CanRobTarget(Id(actor),p.Account)).ToArray();
    }
    private void NetworkRobberyTargets(IPlayer p)
    {
        if(NetworkTargets(p).Length==0)p.SendLplkChat("[LPLK Bankraub] Im Bankverbund ist gerade kein ausraubbares Ziel verfügbar. Mindestguthaben, Schutz, Sperren und Auftragskosten werden berücksichtigt.");
        Page(p,"Bankraub · Ziel wählen",actor=>NetworkTargets(actor).Select(target=>
            (target.Name+" · "+target.ServerName,(Action<IPlayer>)(a=>NetworkRobberyConfirm(a,target)))).ToArray(),RobberyMenu);
    }
    private void NetworkRobberyConfirm(IPlayer p,BankPresence selected)
    {
        RequireRobbery(p);PublishBankPlayers();var target=bank!.RequireBankTarget(selected.Account,selected.Server,selected.Session,RobNow);
        bank.Open(target.Account,target.Name);var quote=bank.QuoteRobbery(Id(p),target.Account,settings.BankRobbery,RobNow);string request=NewId();long period=bank.Period;
        p.SendLplkChat($"[LPLK Bankraub] Ziel: {target.Name} auf {target.ServerName}. Auftrag {quote.Cost:N0} $, davon Schutz-Bypass {quote.BypassCost:N0} $. Gebühr auch bei Fehlschlag verbraucht. Ausführung am nächsten gültigen Rundenende hier auf {BankServerName()}.");
        Confirm(p,$"{target.Name} · {quote.Cost:N0} $",actor=>
        {
            RequireRobbery(actor);PublishBankPlayers();
            var order=bank.OrderNetworkRobbery(request,period,Id(actor),target,settings.BankRobbery,RobNow,quote.Cost);
            actor.SendLplkChat($"[LPLK Bankraub] Auftrag gegen {order.TargetName} im Bankverbund für {order.Cost:N0} $ gespeichert.");
        },RobberyTargets);
    }
}
