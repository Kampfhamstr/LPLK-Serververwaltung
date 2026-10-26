using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Economy;

[PluginMetadata(Id="LPLK_Economy",Name="LPLK_Economy",Version="0.12.12",Author="LPLK; BetBank reference Michael Zauche",Description="Gemeinsame Kontoführung, Bank und nachvollziehbare Buchungen",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private EconomySettings settings=new();
    private Ledger? bank;
    private bool ready,roundActive,mapBlocked;
    private double roundStarted;
    private long nextStatus,nextReset;
    private double roundEndMs;
    private readonly Dictionary<string,double> roundEndPhases=[];
    private void RoundPhase(string name,Action work){var timer=System.Diagnostics.Stopwatch.StartNew();BettingWork(work);roundEndPhases[name]=timer.Elapsed.TotalMilliseconds;}
    private EconomySettingsFile? config;
    private bool statusWriteFailed;
    private string? failure,reloadError,resetDelay;
    private int errors;
    private readonly HashSet<ulong> dead=[],blocked=[];
    private bool Active=>module.Active&&settings.Enabled&&bank is not null&&failure is null;
    private bool Human(IPlayer p)=>p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&BankPolicy.Steam(Id(p));
    private bool CanUse(IPlayer p)=>Active&&Human(p)&&!mapBlocked&&!blocked.Contains(p.SteamID);
    private static string Id(IPlayer p)=>p.SteamID.ToString(CultureInfo.InvariantCulture);
    private DateTime ResetNow=>settings.UseUtcForResetKey?DateTime.UtcNow:DateTime.Now;
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Economy",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Economy","Wirtschaft","lplk.economy.admin",OpenAdmin,p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.AdminPermission));
        config=new(module.SettingsPath);settings=config.Current;module.Active=true;
        try{bank=NetworkStorage.Open(LPLK.Common.RoundFileStore.DataPath(Core),settings);bank.RecoverBetRounds();bank.ResumeLottery(true);bank.ResumeRobbery(RobNow);DeliverBetClaims(false);try{bank.CheckReset(settings,ResetNow);}catch(BankRejected e){resetDelay=e.Message;}}
        catch(Exception e){Fail(e);}
        titlePioneers=bank?.PioneerOwners()??[];titleAnnouncementPeriod=bank?.Period??0;ready=hotReload;
        Core.GameEvent.HookPost<EventRoundStart>(_=>{roundSettlement.Boundary(()=>{settlementRound++;robberyRoundEnded=false;roundActive=false;dead.Clear();if(ready)CheckMapCash();BettingWork(()=>bank!.CheckReset(settings,ResetNow));BettingWork(BeginBonusRound);BettingWork(BeginBountyRound);BettingWork(BeginTitleRound);BeginBettingRound();BeginLotteryRound();});return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundFreezeEnd>(_=>{double began=Environment.TickCount64/1000d;roundSettlement.Boundary(()=>{roundActive=true;roundStarted=began;});return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundEnd>(e=>{QueueRoundSettlement(e.Winner,ready&&Core.EntitySystem.GetGameRules()?.WarmupPeriod==true);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundPrestart>(_=>{roundSettlement.Boundary(()=>{BettingWork(FlushRoundKills);roundActive=false;titleRoundEnded=true;bonusRoundEnded=true;bountyRoundEnded=true;deathCash.Clear();BettingWork(()=>EndBettingRound(0,false));BettingWork(()=>EndLotteryRound(-1));});return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerDeath>(e=>{if(e.UserIdPlayer is {} p&&Human(p))dead.Add(p.SteamID);BettingWork(()=>BonusDeath(e.UserIdPlayer,e.AttackerPlayer));BettingWork(()=>KillRewardDeath(e.UserIdPlayer,e.AttackerPlayer));BettingWork(()=>BountyDeath(e.UserIdPlayer,e.AttackerPlayer));return HookResult.Continue;});
        Core.GameEvent.HookPre<EventPlayerDisconnect>(e=>{BettingWork(()=>BountyDisconnected(e.UserIdPlayer));BettingWork(()=>PublishBankPlayers(e.UserIdPlayer?.SteamID??0));return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p&&Human(p))bountyLives[p.SteamID]=bountyLives.GetValueOrDefault(p.SteamID)+1;return HookResult.Continue;});
        amountChatHook=Core.Command.HookClientChat(AmountChat);
        WriteStatus();Core.Logger.LogInformation("LPLK_Economy 0.12.12 geladen; profil={Profile}; bereit={Healthy}",settings.Profile,failure is null);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.AdminSections.Economy.v1",
            new(){["lotto"]=(Action<IPlayer>)(p=>{RefreshSettings();if(Human(p)&&module.CanAdmin(p))LottoAdmin(p);})});
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Target.LPLK_Economy.v1",new(){["actions"]=(Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>>)TargetActions});
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Economy.v1",new(){["allowed"]=(Func<IPlayer,string,bool>)((p,c)=>CanUse(p)&&(c=="sw_bank"&&settings.BankEnabled||c=="sw_bet"&&settings.Betting.Enabled||c=="sw_lotto"&&settings.Lotto.Enabled||c=="sw_bankraub"&&settings.BankEnabled&&settings.BankRobbery.Enabled||c=="sw_titel"&&settings.BankEnabled&&settings.TitleSystem.Enabled||c=="sw_kopfgeld"&&settings.BankEnabled&&settings.Bounty.Enabled))});
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Economy.v1",new()
        {
            ["ready"]=(Func<bool>)(()=>Active),
            ["optionalMessages"]=(Func<string,bool>)(id=>Active&&BankPolicy.Steam(id)&&!bank!.Muted(id)),
            ["period"]=(Func<long>)(()=>Active?bank!.Period:throw new InvalidOperationException("Bank nicht bereit.")),
            ["balance"]=(Func<string,long>)(id=>Active&&BankPolicy.Steam(id)?bank!.Balance(id):throw new InvalidOperationException("Bank nicht bereit / ungültiges Konto."))
        });
    }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){FinishPendingRound();BettingWork(FlushRoundKills);if(bank?.Networked==true)BettingWork(()=>bank.PublishBankPresence([],RobNow));amountRequests.Clear();if(amountChatHook!=Guid.Empty)Core.Command.UnhookClientChat(amountChatHook);amountChatHook=Guid.Empty;BettingWork(()=>EndLotteryRound(-1));BettingWork(()=>EndBettingRound(0,false));SaveRoundMemory();module.Active=false;ready=false;CompleteWebExport(true);exportWriter.Drain();bank?.Dispose(); LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e){settlementRound++;titleMapEpoch++;bountyLives.Clear();killDeaths.Clear();bountyRound=null;bountyRoundEnded=true;titleRound=null;titleRoundEnded=true;bonusRound=null;bonusRoundEnded=true;cashCollectionMapBlocked=false;deathCash.Clear();collectionReports.Clear();ready=true;roundActive=false;mapBlocked=false;dead.Clear();blocked.Clear();}
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){FinishPendingRound();BettingWork(FlushRoundKills);amountRequests.Clear();BettingWork(()=>EndLotteryRound(-1));BettingWork(()=>EndBettingRound(0,false));SaveRoundMemory();ready=false;roundActive=false;}
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active||Environment.TickCount64<nextStatus)return;nextStatus=Environment.TickCount64+2000;
        CompleteWebExport();RefreshSettings();
        BettingWork(()=>DeliverBetClaims(false));BettingWork(()=>bank!.ResumeLottery());BettingWork(ResumeRobbery);BettingWork(DeliverKillRewards);BettingWork(DeliverBountyClaims);BettingWork(TitleAnnouncements);
        if(Active&&bank!.QueuedKillCount==0&&Environment.TickCount64>=nextReset)
        {
            nextReset=Environment.TickCount64+60000;
            try{bank!.CheckReset(settings,ResetNow);resetDelay=null;}catch(BankRejected e){resetDelay=e.Message;}catch(Exception e){Fail(e);}
        }
        webError=exportWriter.Error??webError;WriteStatus();
    }
    private void FlushRoundKills(){if(bank is null||failure is not null)return;bank.FlushKillEvents();DeliverKillRewards();DeliverBountyClaims();}
    private void Fail(Exception e){errors++;failure="Bank gesperrt; Daten und Journal prüfen.";Core.Logger.LogError(e,"LPLK_Economy: Kontodaten bleiben erhalten, keine weiteren Buchungen");}
    private void Guard(IPlayer p,Action action)
    {
        try{RefreshSettings();action();}catch(BankRejected e){p.SendLplkChat("[LPLK Bank] "+e.Message);}catch(Exception e){Fail(e);p.SendLplkChat("[LPLK Bank] "+failure);}
    }
    private void Require(IPlayer p,bool bankFeature=true)
    {
        if(!CanUse(p)||bankFeature&&!settings.BankEnabled)throw new BankRejected(failure??"Bank derzeit nicht verfügbar.");
        bank!.CheckReset(settings,ResetNow);bank.Open(Id(p),p.Name);
        if(bank.Blocked(Id(p)))throw new BankRejected("Offene Cash-Buchung muss durch einen Admin geprüft werden.");
    }
    private CashContext Context(IPlayer p)
    {
        if(!ready)return default;var rules=Core.EntitySystem.GetGameRules();if(rules is null)return default;
        return new(true,rules.WarmupPeriod,rules.FreezePeriod,roundActive,rules.TotalRoundsPlayed+1,Environment.TickCount64/1000d-roundStarted,p.IsAlive&&p.Controller.TeamNum is 2 or 3,p.Controller.ControllingBot,dead.Contains(p.SteamID),mapBlocked||blocked.Contains(p.SteamID),Core.Engine.GlobalVars.MapName.ToString());
    }
    private int Cash(IPlayer p)
    {
        var money=p.Controller.InGameMoneyServices??throw new BankRejected("Cash noch nicht verfügbar.");int value=money.Account;
        if(value<0||value>settings.MaxPlausibleCash)throw new BankRejected("Cash-Wert unplausibel; keine Buchung.");return value;
    }
    private void CheckMapCash()
    {
        if(!Active||!settings.BlockMapAboveCashLimit||Core.EntitySystem.GetGameRules()?.WarmupPeriod!=false)return;
        foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))
        {
            if(p.Controller.TeamNum is not (2 or 3))continue;
            try{if(Cash(p)>settings.MapCashBlockLimit){if(settings.MapCashBlockAllPlayers)mapBlocked=true;else blocked.Add(p.SteamID);}}
            catch(BankRejected){blocked.Add(p.SteamID);}
        }
    }
    private void CashMove(IPlayer p,bool deposit,string amount,string id,long expectedPeriod)
    {
        Require(p);var c=Context(p);if(BankPolicy.CashDenied(settings,c,deposit) is {} denied)throw new BankRejected(denied);
        if(bank!.Period!=expectedPeriod)throw new BankRejected("Bankperiode gewechselt; Menü erneut öffnen.");
        if(bank.Intent(id) is not null)throw new BankRejected("Diese Bestätigung wurde bereits verarbeitet.");
        int before=Cash(p);if(before>settings.MaxPlayerCash)throw new BankRejected("Cash über dem Banklimit; keine Buchung.");
        long max=deposit?Math.Min(before,Math.Max(0,settings.Limit-bank.Balance(Id(p)))):Math.Min(bank.Balance(Id(p)),settings.MaxPlayerCash-before);
        if(!deposit&&!c.Warmup&&BankPolicy.Pistol(settings,c.Round))max=Math.Min(max,c.Freeze?settings.MaxWithdrawPistolRound:settings.MaxWithdrawPistolRoundBuyTime);
        long value=BankPolicy.Amount(amount,max,deposit?settings.MinDeposit:settings.MinWithdraw,max);
        string session=p.SessionId.ToString();uint pawn=Core.EntitySystem.GetRefEHandle(p.PlayerPawn!).Raw;byte team=p.Controller.TeamNum;int after=checked(before+(int)(deposit?-value:value));
        var intent=bank.PrepareCash(id,expectedPeriod,Id(p),session,before,after);
        try
        {
            var current=Context(p);
            if(!Human(p)||p.SessionId.ToString()!=session||p.PlayerPawn is not {IsValid:true} live||Core.EntitySystem.GetRefEHandle(live).Raw!=pawn||p.Controller.TeamNum!=team||current with{Seconds=c.Seconds}!=c||BankPolicy.CashDenied(settings,current,deposit) is not null||Cash(p)!=before)throw new BankRejected("Cash-Zustand inzwischen geändert; Vorgang bleibt zur Prüfung offen.");
            var money=p.Controller.InGameMoneyServices!;
            money.Account=after;money.AccountUpdated();p.Controller.InGameMoneyServicesUpdated();
            if(Cash(p)!=after)throw new BankRejected("Cash-Änderung nicht bestätigt; Vorgang bleibt zur Prüfung offen.");
            bank.ResolveCash(id,true,"native:"+session);
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Economy: Cash-Auftrag {Id} muss geprüft werden",intent.Id);throw new BankRejected("Cash-Buchung offen. Konto bis zur Adminprüfung gesperrt; nicht wiederholen.");}
        p.SendLplkChat($"[LPLK Bank] {(deposit?"Eingezahlt":"Abgehoben")}: {value:N0} $ · Bank: {bank.Balance(Id(p)):N0} $");
    }
    private void Transfer(IPlayer p,string target,string amount,string id,long period)
    {
        Require(p);if(!settings.TransfersEnabled)throw new BankRejected("Überweisungen ausgeschaltet.");
        if(!BankPolicy.Steam(target)||target==Id(p))throw new BankRejected("Anderes gültiges Empfängerkonto wählen.");
        var c=Context(p);
        if(!c.Known||settings.BlockTransferDuringWarmup&&c.Warmup||settings.TransferOnlyDuringFreezeTime&&!c.Freeze)throw new BankRejected("Überweisen derzeit nicht erlaubt.");
        var online=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>Human(x)&&Id(x)==target);
        if(online is null)
        {
            if(settings.OnlyAliveTransfers||settings.OnlySameTeam&&!settings.AllowForeignPlayers||!(bank!.Exists(target)?settings.AllowStoredPlayerTransfers:settings.AllowUnknownSteamIdTransfers))throw new BankRejected("Offline-Empfänger nicht freigegeben.");
        }
        else if(settings.OnlyAliveTransfers&&!online.IsAlive||settings.OnlySameTeam&&!settings.AllowForeignPlayers&&online.Controller.TeamNum!=p.Controller.TeamNum)throw new BankRejected("Empfänger nicht freigegeben.");
        bank!.Open(target,online?.Name??bank.Accounts(int.MaxValue).FirstOrDefault(a=>a.Id==target)?.Name??target);
        long max=Math.Min(bank.Balance(Id(p)),Math.Max(0,settings.Limit-bank.Balance(target)));if(settings.MaxTransfer>=0)max=Math.Min(max,settings.MaxTransfer);
        long value=BankPolicy.Amount(amount,max,settings.MinTransfer,max);var receipt=bank.Transfer(id,period,Id(p),target,value);
        p.SendLplkChat($"[LPLK Bank] {(receipt.Duplicate?"Bereits überwiesen":"Überwiesen")}: {value:N0} $ an {Ledger.SafeName(online?.Name??target)}.");
    }
    private void WriteStatus()
    {
        RefreshDisplay();
        try{DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.12.12",settings,persistence=new{policy=bank?.PersistencePolicy,memoryRevision=bank?.MemoryRevision,savedRevision=bank?.PersistedRevision,running=bank?.PersistenceRunning,error=bank?.PersistenceError,snapshotMs=bank?.SnapshotMilliseconds,writeMs=bank?.PersistenceMilliseconds},ready,effective=Active,healthy=failure is null,period=bank?.Period,accounts=display?.Accounts,transactions=display?.Transactions,pending=display?.Pending,profile=settings.Profile,mapBlocked,blockedPlayers=blocked.Count,errors,failure,reloadError,resetDelay,web=new{settings.WebBank.ExportEnabled,settings.WebBank.AdminChangesEnabled,exportPolicy="RoundEnd",lastExportAt=lastWebExportAt,error=webError,webExportMs,webExportRunning=webExportTask is not null},betting=new{activeRound=betActive,openRounds=display?.OpenBetRounds,claims=display?.BetClaims},lottery=new{activeRound=lotRound is not null,openRounds=display?.OpenLotteryRounds,superpot=display?.Superpot,refunds=display?.LotteryRefunds,abilities=settings.LottoAbilityBonus.Enabled,upgradeDiscounts=settings.Lotto.PermanentTicketUpgradeDiscountEnabled,cashCollection=settings.Lotto.CashToSuperpot.Enabled},titles=new{enabled=settings.TitleSystem.Enabled,catalog=TitleCatalog.All.Length,interest=settings.InterestSystem.Enabled,tax=settings.LuxuryTax.Enabled,rulerSlots=true,progressPeriod=bank?.Period},robbery=new{openOrders=display?.OpenRobberies,claims=display?.RobberyClaims},bounty=new{active=display?.ActiveBounties,pending=display?.PendingBounties,claims=display?.BountyClaims,marker=settings.BountyMarker.Enabled},displayError,displayRefreshSeconds=15,roundEndMs,roundEndPhases,roundSettlementPending=roundSettlement.Pending,settlementDelayMs=RoundSettlementQueue.DelayMilliseconds,lastRoundEndAt,lastRoundSettlementAt,queuedKillEvents=bank?.QueuedKillCount,killSavePolicy="RoundEnd",killReward=new{enabled=settings.KillReward.Enabled,claims=display?.KillClaims},features=new{killReward=Active&&settings.KillReward.Enabled,bounty=Active&&settings.BankEnabled&&settings.Bounty.Enabled,bank=Active&&settings.BankEnabled,betting=Active&&settings.Betting.Enabled,lottery=Active&&settings.Lotto.Enabled,robbery=Active&&settings.BankEnabled&&settings.BankRobbery.Enabled,titles=Active&&settings.BankEnabled&&settings.TitleSystem.Enabled}});statusWriteFailed=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Economy: Status nicht schreibbar; Kontoführung bleibt aktiv.");statusWriteFailed=true;}
    }
    private void RefreshSettings()
    {
        if(config is null)return;
        config.Reload(s=>bank?.Configure(s));settings=config.Current;
        if(config.Error is not null&&reloadError!=config.Error)Core.Logger.LogWarning("LPLK_Economy: Letzter gültiger Einstellungsstand bleibt aktiv: {Reason}",config.Error);
        reloadError=config.Error;
    }
}


















