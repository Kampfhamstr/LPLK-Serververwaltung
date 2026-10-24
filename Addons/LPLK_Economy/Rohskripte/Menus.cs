using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Economy;

public sealed partial class Plugin
{
    private static string NewId()=>"ui:"+Guid.NewGuid().ToString("N");
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null,bool admin=false,bool scroll=true)=>
        CompactMenu.Open(Core,p,title,()=>module.Active,a=>{RefreshSettings();return Human(a)&&(admin?module.CanAdmin(a):CanUse(a));},a=>rows(a).Select(x=>(Escape(x.Item1),(Action<IPlayer>)(b=>Guard(b,()=>x.Item2(b))))).ToArray(),back,autoCloseSeconds:settings.MenuAutoCloseSeconds,scroll:scroll);
    private static string Escape(string s)=>s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    private void Confirm(IPlayer p,string title,Action<IPlayer> action,Action<IPlayer> back,bool admin=false)
    {
        bool used=false;Page(p,title,_=>[("Bestätigen",a=>{if(used)throw new BankRejected("Bereits bestätigt.");used=true;action(a);back(a);})],back,admin);
    }
    [Command("bank")][CommandAlias("konto")][CommandAlias("bank",registerRaw:true)][CommandAlias("konto",registerRaw:true)]
    public void BankCommand(ICommandContext ctx){if(ctx.Sender is {} p)Guard(p,()=>OpenBank(p));else ctx.Reply("LPLK_Economy: !bank / !konto öffnet das Spielerkonto.");}
    private void OpenBank(IPlayer p)
    {
        if(!CanUse(p)||!settings.BankEnabled){p.SendChat("[LPLK Bank] "+(failure??"Bank derzeit nicht verfügbar."));return;}
        bank!.Open(Id(p),p.Name);
        Page(p,$"Bank · {bank.Balance(Id(p)):N0} $",a=>
        {
            var rows=new List<(string,Action<IPlayer>)>{("Einzahlen",b=>Amounts(b,true)),("Abheben",b=>Amounts(b,false))};
            if(settings.TransfersEnabled)rows.Add(("Überweisen",Targets));
            if(settings.BankRobbery.Enabled)rows.Add(("Bankraub",RobberyMenu));
            if(settings.Betting.Enabled)rows.Add(("Wetten",OpenBet));
            if(settings.Lotto.Enabled)rows.Add(("Lotto",OpenLotto));
            if(settings.Bounty.Enabled)rows.Add(("Kopfgeld",BountyMenu));
            if(settings.TitleSystem.Enabled)rows.Add(("Titel",TitlesMenu));
            if(settings.KillReward.Enabled)rows.Add(("Kill-Belohnungen",KillRewardInfo));
            rows.Add(("Buchungen",History));rows.Add(("Rangliste",Ranking));
            rows.Add(($"Meldungen: {(bank.Muted(Id(a))?"AUS":"AN")}",b=>{bank.SetMuted(Id(b),!bank.Muted(Id(b)));OpenBank(b);}));
            rows.Add(("Hilfe",Help));return rows;
        },a=>a.ExecuteCommand("sw_menu"));
    }
    private void CashMenu(IPlayer p)=>OpenBank(p);
    private void BankExtras(IPlayer p)=>OpenBank(p);
    private void Amounts(IPlayer p,bool deposit)=>Page(p,deposit?"Bank · Einzahlen":"Bank · Abheben",_=>
        (deposit?settings.DepositButtons:settings.WithdrawButtons).Select(v=>(v.ToString("N0")+" $",(Action<IPlayer>)(a=>CashConfirmation(a,deposit,v.ToString(System.Globalization.CultureInfo.InvariantCulture)))))
        .Prepend(("Eigener Betrag",(Action<IPlayer>)(a=>AmountInput(a,deposit?"Einzahlen":"Abheben",(b,v)=>CashConfirmation(b,deposit,v.ToString(System.Globalization.CultureInfo.InvariantCulture)),b=>Amounts(b,deposit)))))
        .Append(("Alles Mögliche",a=>CashConfirmation(a,deposit,"all"))).ToArray(),CashMenu);
    private void CashConfirmation(IPlayer p,bool deposit,string amount)
    {
        string id=NewId();long period=bank!.Period;
        Confirm(p,$"{(deposit?"Einzahlen":"Abheben")}: {amount} $",a=>CashMove(a,deposit,amount,id,period),CashMenu);
    }
    private void AccountMenu(IPlayer p)=>OpenBank(p);
    [Command("topbank")][CommandAlias("banktop")][CommandAlias("rich")][CommandAlias("ranking")][CommandAlias("topbank",registerRaw:true)][CommandAlias("banktop",registerRaw:true)][CommandAlias("rich",registerRaw:true)][CommandAlias("ranking",registerRaw:true)]
    public void RankingCommand(ICommandContext ctx){if(ctx.Sender is {} p)Guard(p,()=>{Require(p);Ranking(p);});else ctx.Reply("Bankrangliste im Spiel: !topbank / !banktop / !rich.");}
    private void Ranking(IPlayer p)=>Page(p,"Bank · Rangliste",_=>bank!.Accounts(40).Select((a,i)=>($"{i+1}. {Ledger.SafeName(a.Name)} · {a.Balance:N0} $",(Action<IPlayer>)(b=>b.SendChat($"[LPLK Bank] Platz {i+1}: {Ledger.SafeName(a.Name)} mit {a.Balance:N0} $.")))).ToArray(),AccountMenu);
    private void History(IPlayer p)=>Page(p,"Bank · Buchungen",a=>bank!.History(Id(a)).Select(e=>($"{e.Delta:+#,0;-#,0;0} $ · {e.Reason}",(Action<IPlayer>)(b=>b.SendChat($"[LPLK Bank] {e.At} · {e.Reason} · Änderung {e.Delta:N0} $ · danach {e.Balance:N0} $ · Nr. {e.Id}")))).ToArray(),AccountMenu);
    private void Targets(IPlayer p)=>TargetsWithAmount(p,null);
    private void TargetsWithAmount(IPlayer p,string? fixedAmount)=>Page(p,"Bank · Empfänger",a=>
    {
        var online=Core.PlayerManager.GetAllValidPlayers().Where(Human).Where(x=>x.SteamID!=a.SteamID&&(!settings.OnlyAliveTransfers||x.IsAlive)&&(!settings.OnlySameTeam||settings.AllowForeignPlayers||x.Controller.TeamNum==a.Controller.TeamNum))
            .Select(x=>(Id:Id(x),Name:Ledger.SafeName(x.Name))).ToList();
        if(settings.AllowStoredPlayerTransfers&&!settings.OnlyAliveTransfers&&(!settings.OnlySameTeam||settings.AllowForeignPlayers))
            online.AddRange(bank!.Accounts(settings.MaxStoredTransferTargets).Where(x=>x.Id!=Id(a)&&online.All(y=>y.Id!=x.Id)).Select(x=>(x.Id,x.Name+" (offline)")));
        return online.Select(x=>(x.Name+" · "+x.Id[^4..],(Action<IPlayer>)(b=>{if(fixedAmount is null)TransferAmounts(b,x.Id,x.Name);else TransferConfirmation(b,x.Id,x.Name,fixedAmount);}))).ToArray();
    },OpenBank);
    private void TransferAmounts(IPlayer p,string target,string name)=>Page(p,"Überweisen · "+name,_=>settings.TransferButtons.Select(v=>(v.ToString("N0")+" $",(Action<IPlayer>)(a=>TransferConfirmation(a,target,name,v.ToString(System.Globalization.CultureInfo.InvariantCulture)))))
        .Prepend(("Eigener Betrag",(Action<IPlayer>)(a=>AmountInput(a,"Überweisen",(b,v)=>TransferConfirmation(b,target,name,v.ToString(System.Globalization.CultureInfo.InvariantCulture)),b=>TransferAmounts(b,target,name)))))
        .Append(("Alles Mögliche",a=>TransferConfirmation(a,target,name,"all"))).ToArray(),Targets);
    private void TransferConfirmation(IPlayer p,string target,string name,string amount)
    {
        string id=NewId();long period=bank!.Period;Confirm(p,$"{amount} $ an {name}",a=>Transfer(a,target,amount,id,period),Targets);
    }
    [Command("help")][CommandAlias("bbhelp")][CommandAlias("betbankhelp")][CommandAlias("help",registerRaw:true)][CommandAlias("bbhelp",registerRaw:true)][CommandAlias("betbankhelp",registerRaw:true)]
    public void HelpCommand(ICommandContext ctx){if(ctx.Sender is {} p)Guard(p,()=>Help(p));else ctx.Reply("Bankhilfe im Spiel: !help / !bbhelp / !betbankhelp.");}
    private void HelpLine(IPlayer p,bool enabled,string message)
    {
        if(!CanUse(p)||!enabled)throw new BankRejected("Dieser Bereich ist derzeit ausgeschaltet.");p.SendChat("[LPLK Economy] "+message);
    }
    private void Help(IPlayer p)
    {
        if(!CanUse(p))throw new BankRejected("Economy derzeit nicht verfügbar.");
        Page(p,"Economy · Hilfe",_=>
        {
            var rows=new List<(string,Action<IPlayer>)>();
            if(settings.BankEnabled)rows.Add(("Bankregeln",BankHelp));
            if(settings.Betting.Enabled)rows.Add(("Wetten",a=>HelpLine(a,settings.Betting.Enabled,"!bet t|ct Betrag|all, !wette und !bt; !bets zeigt die Pools. Auch terror/terrorist/terrorists/tt und counter/counterterrorist/counterterrorists funktionieren.")));
            if(settings.Lotto.Enabled)rows.Add(("Lotterie",a=>HelpLine(a,settings.Lotto.Enabled,"!lotto kaufen Anzahl|all; dauerlos Anzahl|all; kündigen; status. Alte Formen buy/tickets, abo/permanent und stop/cancel bleiben gültig.")));
            if(settings.BankEnabled&&settings.BankRobbery.Enabled)rows.Add(("Bankraub",a=>HelpLine(a,settings.BankEnabled&&settings.BankRobbery.Enabled,"!bankraub öffnet Aufträge und Schutz; !spionlog / !spylog / !bankraublog öffnet die Berichte.")));
            if(settings.BankEnabled&&settings.Bounty.Enabled)rows.Add(("Kopfgeld",a=>HelpLine(a,settings.BankEnabled&&settings.Bounty.Enabled,"!kopfgeld / !bounty Spieler|SteamID64 Betrag; nur ein Betrag öffnet die Zielauswahl. k/m/b und all/alles/max werden unterstützt.")));
            if(settings.BankEnabled&&settings.TitleSystem.Enabled)rows.Add(("Titel",a=>HelpLine(a,settings.BankEnabled&&settings.TitleSystem.Enabled,"!titel / !title / !titles Nummer wählt den Titel; aus/off/clear/none blendet ihn aus.")));
            rows.Add(("Befehle",a=>HelpLine(a,true,"!bankmsg an|aus; ohne Argument nur Status. Beträge verstehen k/m/b. Console/Binds behalten ihre alten Befehle und sw_-Formen.")));
            return rows;
        },a=>a.ExecuteCommand("sw_menu"));
    }
    private void BankHelp(IPlayer p)
    {
        if(!CanUse(p)||!settings.BankEnabled)throw new BankRejected("Bank derzeit ausgeschaltet.");
        Page(p,"Hilfe · Bank",_=>
        {
            var rows=new List<(string,Action<IPlayer>)>{("Bargeld",a=>HelpLine(a,settings.BankEnabled,"!einzahlen / !deposit Betrag|all; !abheben / !withdraw Betrag|all. Zeit- und Pistolenregeln gelten weiterhin.")),
                ("Bankregeln",a=>HelpLine(a,settings.BankEnabled,$"Reset: {settings.ResetMode} · Limit: {(settings.MaxBank<0?"unbegrenzt (technischer Schutz aktiv)":settings.MaxBank.ToString("N0"))} · Periode {bank!.Period}."))};
            if(settings.TransfersEnabled)rows.Add(("Überweisen",a=>HelpLine(a,settings.BankEnabled&&settings.TransfersEnabled,"!transfer / !ueberweisen / !überweisen Spielername|SteamID64 Betrag|all; nur ein Betrag öffnet die Zielauswahl.")));
            rows.Add(("Rangliste",a=>HelpLine(a,settings.BankEnabled,"!bank / !konto; Rangliste über !topbank / !banktop / !rich / !ranking.")));return rows;
        },Help);
    }
    [Command("deposit")][CommandAlias("einzahlen")][CommandAlias("deposit",registerRaw:true)][CommandAlias("einzahlen",registerRaw:true)]
    public void DepositCommand(ICommandContext ctx)=>CashCommand(ctx,true);
    [Command("bankmsg")][CommandAlias("bbmsg")][CommandAlias("betbankmsg")][CommandAlias("bankmsg",registerRaw:true)][CommandAlias("bbmsg",registerRaw:true)][CommandAlias("betbankmsg",registerRaw:true)]
    public void MessagesCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){ctx.Reply("Nachrichteneinstellung benötigt einen Spieler.");return;}
        Guard(p,()=>
        {
            if(!CanUse(p))throw new BankRejected("Economy derzeit nicht verfügbar.");bank!.Open(Id(p),p.Name);
            if(ctx.Args.Length==0){p.SendChat($"[LPLK Bank] BankMSG ist aktuell {(bank.Muted(Id(p))?"aus":"an")}. Nutzung: !bankmsg an / aus.");return;}
            bool? enabled=ctx.Args[0].Trim().ToLowerInvariant() switch{"an" or "on" or "ein" or "1" or "true" or "yes" or "ja"=>true,"aus" or "off" or "0" or "false" or "no" or "nein"=>false,_=>null};
            if(enabled is null){p.SendChat("[LPLK Bank] Nutzung: !bankmsg an / aus.");return;}
            bank.SetMuted(Id(p),!enabled.Value);p.SendChat($"[LPLK Bank] Bankmeldungen sind jetzt {(enabled.Value?"eingeschaltet":"ausgeschaltet")}. Mit !bankmsg an wieder einschalten.");
        });
    }
    [Command("withdraw")][CommandAlias("abheben")][CommandAlias("withdraw",registerRaw:true)][CommandAlias("abheben",registerRaw:true)]
    public void WithdrawCommand(ICommandContext ctx)=>CashCommand(ctx,false);
    private void CashCommand(ICommandContext ctx,bool deposit)
    {
        if(ctx.Sender is not {} p){ctx.Reply("Cash-Aktion benötigt einen Spieler.");return;}
        Guard(p,()=>{Require(p);if(ctx.Args.Length==0){Amounts(p,deposit);return;}CashMove(p,deposit,LegacyAmount(ctx.Args[0]),NewId(),bank!.Period);});
    }
    [Command("transfer")][CommandAlias("ueberweisen")][CommandAlias("überweisen")][CommandAlias("transfer",registerRaw:true)][CommandAlias("ueberweisen",registerRaw:true)][CommandAlias("überweisen",registerRaw:true)]
    public void TransferCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){ctx.Reply("Überweisung benötigt einen Spieler.");return;}
        Guard(p,()=>{Require(p);if(!settings.TransfersEnabled)throw new BankRejected("Überweisungen ausgeschaltet.");if(ctx.Args.Length==0){Targets(p);return;}if(ctx.Args.Length==1){TargetsWithAmount(p,LegacyAmount(ctx.Args[0]));return;}string target=LegacyTarget(string.Join(" ",ctx.Args[..^1]));Transfer(p,target,LegacyAmount(ctx.Args[^1]),NewId(),bank!.Period);});
    }
    [Command("bbadmin")][CommandAlias("bankadmin")][CommandAlias("betbankadmin")][CommandAlias("bbadmin",registerRaw:true)][CommandAlias("bankadmin",registerRaw:true)][CommandAlias("betbankadmin",registerRaw:true)]
    public void LegacyAdministration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){Guard(p,()=>{if(module.CanAdmin(p))OpenAdmin(p);else OpenBank(p);});return;}
        ctx.Reply("Bankverwaltung im Spiel: !bbadmin / !bankadmin / !betbankadmin; passende Rechte erforderlich.");
    }
    [Command("lplk_economy")][CommandAlias("lplk_economy",registerRaw:true)]
    public void Administration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){Guard(p,()=>OpenAdmin(p));return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="probe"){Probe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="betprobe"){BettingProbe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="robprobe"){RobberyProbe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="titleprobe"){TitlesProbe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="killprobe"){KillRewardProbe(ctx);return;}if(ctx.Args.Length==1&&ctx.Args[0]=="bountyprobe"){BountyProbe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="bonusprobe"){BonusProbe(ctx);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="lottoprobe"){LotteryProbe(ctx);return;}
        WriteStatus();ctx.Reply($"LPLK_Economy 0.10.0: healthy={failure is null}; effective={Active}; profile={settings.Profile}; period={bank?.Period}; accounts={bank?.AccountCount}; transactions={bank?.TransactionCount}; pending={bank?.PendingCount}; reset={settings.ResetMode}; errors={errors}; reloadError={reloadError??"none"}; resetDelay={resetDelay??"none"}");
    }
    private void OpenAdmin(IPlayer p) => Page(p,"Bank & Bankraub",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Bank",TimingMenuRows(actor)),("Raubpreise",RobberyAdminPricesRows(actor)),("Raubschutz",RobberyAdminProtectionRows(actor)),("Bankraub",RobberyAdminStatusRows(actor)),("Webbank",WebSettingsRows(actor)),("Bank",AdminStatusRows(actor)),("Periode",AdminPeriodRows(actor)),("Wetten",BetAdminRows(actor)),("Wettstart",BetOpeningRows(actor)),("Wettlimits",BetLimitsRows(actor)),("Kopfgeld",BountyAdminRows(actor)),("Kopfgeldzahlung",BountyFundingAdminRows(actor)),("Kopfgeldregeln",BountyRulesAdminRows(actor)),("Markierung",BountyMarkerAdminRows(actor)),("Titel",TitlesAdminRows(actor)),("Titel",TitleSwitchesRows(actor)),("Zinsen",InterestAdminRows(actor)),("Steuern",TaxAdminRows(actor)),("Killbelohnung",KillRewardAdminRows(actor))),a=>a.ExecuteCommand("sw_admin"),true,scroll:true);
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Konten",AdminAccounts),        ($"Bank: {(settings.BankEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{BankEnabled=!current.BankEnabled});OpenAdmin(a);}),        ($"Überweisungen: {(settings.TransfersEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{TransfersEnabled=!current.TransfersEnabled});OpenAdmin(a);}),        ("Guthabenlimit",LimitMenu),        ($"Bankraub: {(settings.BankRobbery.Enabled?"AN":"AUS")}",a=>{Save(a,current=>current with{BankRobbery=current.BankRobbery with{Enabled=!current.BankRobbery.Enabled}});OpenAdmin(a);}),        ($"Economy gesamt: {(settings.Enabled?"AN":"AUS")}",a=>{Save(a,current=>current with{Enabled=!current.Enabled});OpenAdmin(a);})]))(p);
    private void AdminStatus(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminStatusRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Status",a=>a.SendChat($"[LPLK Bank] Aktiv: {Active}; Konten: {bank?.AccountCount}; offene Buchungen: {bank?.PendingCount}; {failure??"Journal bereit."}")),        ("Cashvorgänge",PendingMenu),("Journal prüfen",a=>{bank?.Verify();a.SendChat(bank is null?"[LPLK Bank] Datenbank nicht geöffnet.":"[LPLK Bank] Buchungen und Kontostände stimmen überein.");})]))(p);
    private void PendingMenu(IPlayer p)=>Page(p,"Bank · Offene Cash-Vorgänge",_=>(bank?.Pending()??[]).Select(i=>($"{bank!.CashPurpose(i.Id)} {i.Account[^4..]}: {i.Before} → {i.After} $",(Action<IPlayer>)(a=>ResolveMenu(a,i)))).ToArray(),AdminStatus,true);
    private void ResolveMenu(IPlayer p,CashIntent i)=>Page(p,$"Prüfen · {i.Account}",_=>
        [("Details",a=>a.SendChat($"[LPLK Bank] {bank!.CashPurpose(i.Id)} · {i.Id} · Konto {i.Account} · Cash vorher {i.Before}, nachher {i.After}. Abschluss betrifft den zugehörigen Bank-, Wett-, Bonus-, Kill- oder Kopfgeldvorgang. Nur nach tatsächlicher Prüfung entscheiden.")),
        ("Cash geändert",a=>Confirm(a,"Cash-Änderung bestätigt?",b=>Resolve(b,i,true),PendingMenu,true)),
        ("Cash unverändert",a=>Confirm(a,"Keine Cash-Änderung bestätigt?",b=>Resolve(b,i,false),PendingMenu,true))],PendingMenu,true);
    private void Resolve(IPlayer p,CashIntent i,bool applied)
    {
        if(!MayManage(p,i.Account))throw new BankRejected("Keine Berechtigung für dieses Konto.");
        bank!.ResolveCash(i.Id,applied,"admin:"+Id(p));WriteStatus();
    }
    private bool MayManage(IPlayer p,string target)
    {
        if(!Human(p)||!module.CanAdmin(p)||!settings.AllowEditPlayerBank||!BankPolicy.Steam(target))return false;
        int Rank(ulong steam)=>Math.Max(Core.Permission.PlayerHasPermission(steam,settings.AdminPermission)||Core.Permission.PlayerHasPermission(steam,"lplk.economy.admin")?10:0,AdminActionPolicy.Rank(x=>Core.Permission.PlayerHasPermission(steam,x)));
        return p.SteamID==ulong.Parse(target)||Rank(p.SteamID)>Rank(ulong.Parse(target))||Rank(ulong.Parse(target))==0;
    }
    private void AdminAccounts(IPlayer p)=>Page(p,"Bank · Konten",a=>(bank?.Accounts(100)??[]).Where(x=>MayManage(a,x.Id)).Select(x=>($"{x.Name} · {x.Balance:N0} $",(Action<IPlayer>)(b=>AccountAdmin(b,x.Id,x.Name)))).ToArray(),OpenAdmin,true);
    private void AccountAdmin(IPlayer p,string target,string name)=>Page(p,"Konto · "+name,_=>
        [("Gutschrift",a=>AdjustAmounts(a,target,"add")),("Abziehen",a=>AdjustAmounts(a,target,"remove")),("Guthaben setzen",a=>AdjustAmounts(a,target,"set")),("Letzte Buchungen",a=>{if(!MayManage(a,target))throw new BankRejected("Berechtigung entzogen.");foreach(var e in bank!.History(target,5))a.SendChat($"[LPLK Bank] {e.At}: {e.Reason} {e.Delta:N0} $; danach {e.Balance:N0} $");})],AdminAccounts,true);
    private void AdjustAmounts(IPlayer p,string target,string operation)=>Page(p,"Bank · "+operation,_=>BankPolicy.MenuAmounts(operation=="set"?settings.AdminSetButtons:operation=="add"?settings.AdminAddButtons:settings.AdminRemoveButtons,operation=="set"?0:1,operation=="set"?Math.Min(settings.Limit,settings.MaxManualBankSet<0?settings.Limit:settings.MaxManualBankSet):operation=="add"?Math.Max(0,settings.Limit-bank!.Balance(target)):bank!.Balance(target))
        .Select(v=>(v.ToString("N0")+" $",(Action<IPlayer>)(a=>AdjustConfirm(a,target,v,operation))))
        .Prepend(("Eigener Betrag",(Action<IPlayer>)(a=>AmountInput(a,"Bankbetrag",(b,v)=>AdjustConfirm(b,target,v,operation),b=>AdjustAmounts(b,target,operation),true,operation=="set"?0:1)))).ToArray(),AdminAccounts,true);
    private void AdjustConfirm(IPlayer p,string target,long amount,string operation)
    {
        if(!MayManage(p,target))throw new BankRejected("Keine Kontoberechtigung.");string id=NewId();long period=bank!.Period;
        Confirm(p,$"{operation} {amount:N0} $ · {target[^4..]}",a=>
        {
            ApplyAdminAdjustment(a,target,amount,operation,id,period);
        },AdminAccounts,true);
    }
    [Command("bankset")][CommandAlias("bankset",registerRaw:true)]
    public void SetCommand(ICommandContext ctx)=>AdjustCommand(ctx,"set");
    [Command("bankadd")][CommandAlias("bankadd",registerRaw:true)]
    public void AddCommand(ICommandContext ctx)=>AdjustCommand(ctx,"add");
    [Command("bankremove")][CommandAlias("bankdel")][CommandAlias("bankremove",registerRaw:true)][CommandAlias("bankdel",registerRaw:true)]
    public void RemoveCommand(ICommandContext ctx)=>AdjustCommand(ctx,"remove");
    [Command("bankresetplayer")][CommandAlias("bankresetkonto")][CommandAlias("bankresetplayer",registerRaw:true)][CommandAlias("bankresetkonto",registerRaw:true)]
    public void ResetPlayerCommand(ICommandContext ctx)=>AdjustCommand(ctx,"set",true);
    private void AdjustCommand(ICommandContext ctx,string operation,bool reset=false)
    {
        if(ctx.Sender is not {} p){ctx.Reply("Kontokorrektur über einen berechtigten Administrator im Spiel.");return;}
        Guard(p,()=>
        {
            if(ctx.Args.Length<(reset?1:2)||bank is null||failure is not null||!module.CanAdmin(p))throw new BankRejected("Nutzung: !bankset / !bankadd / !bankremove Spieler|SteamID64 Betrag; passende Rechte erforderlich.");
            string target=LegacyTarget(string.Join(" ",reset?ctx.Args:ctx.Args[..^1]));if(!MayManage(p,target))throw new BankRejected("Keine Kontoberechtigung.");
            long amount=reset?0:LegacyAdminAmount(ctx.Args[^1]);
            ApplyAdminAdjustment(p,target,amount,operation,NewId(),bank.Period);
        });
    }
    private void ApplyAdminAdjustment(IPlayer p,string target,long amount,string operation,string id,long period)
    {
        if(bank is null||failure is not null||!MayManage(p,target))throw new BankRejected("Keine Kontoberechtigung oder Journal nicht verfügbar.");
        if(period!=bank.Period)throw new BankRejected("Bankperiode inzwischen gewechselt.");
        if(!bank.Exists(target))bank.Open(target,target);long before=bank.Balance(target);
        bank.AdjustLegacy(id,period,Id(p),target,amount,operation,settings.MaxManualBankSet);long after=bank.Balance(target);
        p.SendChat($"[LPLK Bank] Bank geändert: {target} · {before:N0} → {after:N0} $. Angefordert: {operation} {amount:N0} $; Kontolimit berücksichtigt.");
        foreach(var online in Core.PlayerManager.GetAllValidPlayers().Where(Human).Where(a=>Id(a)==target&&a.SteamID!=p.SteamID))online.SendChat($"[LPLK Bank] Dein Bankguthaben wurde von einem Admin auf {after:N0} $ angepasst.");
    }
    private void WebSettings(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> WebSettingsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [($"Webansicht: {(settings.WebBank.ExportEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{WebBank=current.WebBank with{ExportEnabled=!current.WebBank.ExportEnabled}});WebSettings(a);}),        ($"Web-Adminänderungen: {(settings.WebBank.AdminChangesEnabled?"AN":"AUS")}",a=>{Save(a,current=>current with{WebBank=current.WebBank with{AdminChangesEnabled=!current.WebBank.AdminChangesEnabled}});WebSettings(a);}),        ("Berechtigungen",a=>a.SendChat("[LPLK Bank] Webänderungen benötigen eigene Panelrechte und einen Buchungsbeleg. Anbindung nur für Testprofil 89450e53 freigegeben. Produktivverbund vorbereitet, noch nicht aktiv."))]))(p);
    private void TimingMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TimingMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [($"Pistolensperre: {(settings.BlockWithdrawDuringPistolRounds?"AN":"AUS")}",a=>{Save(a,current=>current with{BlockWithdrawDuringPistolRounds=!current.BlockWithdrawDuringPistolRounds});TimingMenu(a);}),        ($"Kaufzeit: {(settings.AllowWithdrawDuringBuyTime?"AN":"AUS")}",a=>{Save(a,current=>current with{AllowWithdrawDuringBuyTime=!current.AllowWithdrawDuringBuyTime});TimingMenu(a);}),        ($"Warmupsperre: {(settings.BlockDepositDuringWarmup?"AN":"AUS")}",a=>{Save(a,current=>current with{BlockDepositDuringWarmup=!current.BlockDepositDuringWarmup});TimingMenu(a);}),        ("Details",a=>a.SendChat($"[LPLK Bank] Min. Einzahlung {settings.MinDeposit}; Min. Auszahlung {settings.MinWithdraw}; Buytime {settings.BuyTimeSeconds}s; gesperrte Einzahlmaps {string.Join(", ",settings.BlockDepositOnMapPrefixes)}. Weitere Werte in settings.json."))]))(p);
    private void ChangeLimit(IPlayer p,long value)
    {
        Save(p,current=>current.AllowChangeMaxBank?current with{MaxBank=value}:throw new BankRejected("Limitänderung gesperrt."));
        p.SendChat("[LPLK Bank] Bestehende Guthaben bleiben erhalten; Limit begrenzt neue Gutschriften.");OpenAdmin(p);
    }
    private void LimitMenu(IPlayer p)=>Page(p,"Bank · Guthabenlimit",_=>new long[]{50000,1000000,-1}.Select(v=>(v<0?"Unbegrenzt":v.ToString("N0")+" $",(Action<IPlayer>)(a=>ChangeLimit(a,v))))
        .Append(("Eigener Betrag",(Action<IPlayer>)(a=>AmountInput(a,"Guthabenlimit",(b,v)=>Confirm(b,$"Guthabenlimit: {v:N0} $",c=>ChangeLimit(c,v),LimitMenu,true),LimitMenu,true,0,settings.UnlimitedLimit)))).ToArray(),OpenAdmin,true);
    private void AdminPeriod(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminPeriodRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>
        [("Resetmodus: "+settings.ResetMode,ResetModeMenu),("Zurücksetzen",a=>
        {
            if(!settings.AllowManualBankReset||bank is null)throw new BankRejected("Reset gesperrt.");long period=bank.Period;string id=NewId();
            Confirm(a,"Bank zurücksetzen?",b=>{if(!settings.AllowManualBankReset)throw new BankRejected("Reset inzwischen gesperrt.");bank.Reset(id,period,Id(b),settings,ResetNow);b.SendChat("[LPLK Bank] Neue Bankperiode gestartet.");},AdminPeriod,true);
        })]))(p);
    private void ResetModeMenu(IPlayer p)=>Page(p,"Bank · Resetmodus",_=>new[]{"Never","Daily","Weekly","Monthly"}.Select(v=>(v,(Action<IPlayer>)(a=>
        {if(!settings.AllowChangeResetMode)throw new BankRejected("Resetmodusänderung gesperrt.");Save(a,current=>current.AllowChangeResetMode?current with{ResetMode=v}:throw new BankRejected("Resetmodusänderung gesperrt."));bank?.CheckReset(settings,ResetNow);AdminPeriod(a);}))).ToArray(),AdminPeriod,true);
    private void Save(IPlayer actor,Func<EconomySettings,EconomySettings> edit)
    {
        RefreshSettings();
        if(!module.Active||!Human(actor)||!module.CanAdmin(actor))throw new BankRejected("Keine Einstellungsberechtigung.");
        if(config is null||config.Error is not null)throw new BankRejected("Externe Konfiguration zuerst korrigieren; bisherige Regeln bleiben aktiv.");
        try
        {
            settings=config.Change(edit,(next,persist)=>{if(bank is {} ledger)ledger.ConfigureWithPersistence(next,persist);else persist();});
            reloadError=null;WriteStatus();
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or BankRejected)
        {reloadError=e.Message;throw new BankRejected("Nicht gespeichert: "+e.Message);}
    }
}
