using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private string? titleRound;
    private long titleRoundPeriod;
    private bool titleRoundEnded;
    private int titleMapEpoch;
    private readonly HashSet<string> titleJoined=[];
    private Dictionary<string,string> titlePioneers=[];
    private long titleAnnouncementPeriod;
    private bool TitlesAvailable(IPlayer p)=>CanUse(p)&&settings.BankEnabled&&settings.TitleSystem.Enabled;
    private void RequireTitles(IPlayer p){Require(p);if(!TitlesAvailable(p))throw new BankRejected("Titel ausgeschaltet.");bank!.ObserveTitles(Id(p));}
    [Command("titel")][CommandAlias("title")][CommandAlias("titles")][CommandAlias("titel",registerRaw:true)][CommandAlias("title",registerRaw:true)][CommandAlias("titles",registerRaw:true)]
    public void TitleCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p){WriteStatus();ctx.ReplyLplk($"LPLK_Economy Titel: enabled={Active&&settings.BankEnabled&&settings.TitleSystem.Enabled}; catalog={TitleCatalog.All.Count(t=>settings.Bounty.Enabled||TitlePolicy.Group(t)!="Kopfgeld")}; !titel Nummer|aus.");return;}
        Guard(p,()=>
        {
            RequireTitles(p);string arg=ctx.Args.FirstOrDefault()?.Trim().ToLowerInvariant()??"";
            if(arg is "aus" or "off" or "clear" or "none"){ChooseTitle(p,"",bank!.Period);return;}
            if(int.TryParse(arg,out int index)){var owned=bank!.OwnedTitles(Id(p));if(index<1||index>owned.Length)throw new BankRejected("Ungültige Titelnummer; !titel öffnet die Übersicht.");ChooseTitle(p,owned[index-1].Id,bank.Period);return;}
            TitlesMenu(p);
        });
    }
    private void ChooseTitle(IPlayer p,string id,long period){RequireTitles(p);bank!.SelectTitle(NewId(),period,Id(p),id);p.SendLplkChat("[LPLK Titel] "+(id==""?"Aktiver Titel ausgeblendet.":"Aktiver Titel: "+bank.ActiveTitle(Id(p))));}
    private void TitlesMenu(IPlayer p)
    {
        RequireTitles(p);
        Page(p,"Titel",_=>[("Freigeschaltete Titel",OwnedTitleGroups),("Fortschritt",TitleProgressGroups),("Vorteile",TitleInfo),("Ausblenden",a=>ChooseTitle(a,"",bank!.Period))],a=>a.ExecuteCommand("sw_menu"));
    }
    private void OwnedTitleGroups(IPlayer p)
    {
        RequireTitles(p);Page(p,"Titel · Bereiche",_=>bank!.OwnedTitles(Id(p)).Select(TitlePolicy.Group).Distinct().Select(group=>(group,(Action<IPlayer>)(a=>TitleList(a,group)))).ToArray(),TitlesMenu);
    }
    private void TitleList(IPlayer p,string group)
    {
        RequireTitles(p);long period=bank!.Period;
        Page(p,group,a=>bank.OwnedTitles(Id(a)).Select((t,i)=>(Title:t,Index:i+1)).Where(x=>TitlePolicy.Group(x.Title)==group).Select(x=>($"#{x.Index} · {x.Title.Text}",(Action<IPlayer>)(b=>ChooseTitle(b,x.Title.Id,period)))).ToArray(),OwnedTitleGroups);
    }
    private void TitleProgressGroups(IPlayer p)
    {
        RequireTitles(p);Page(p,"Fortschritt · Bereiche",_=>TitleCatalog.All.Where(t=>settings.Bounty.Enabled||TitlePolicy.Group(t)!="Kopfgeld").Select(TitlePolicy.Group).Distinct().Select(group=>(group,(Action<IPlayer>)(a=>TitleProgressList(a,group)))).ToArray(),TitlesMenu);
    }
    private void TitleProgressList(IPlayer p,string group)
    {
        RequireTitles(p);Page(p,group+" · Fortschritt",a=>
        {
            var owned=bank!.OwnedTitles(Id(a)).Select(t=>t.Id).ToHashSet();var progress=bank.TitlesProgress(Id(a));
            return TitleCatalog.All.Where(t=>TitlePolicy.Group(t)==group&&!owned.Contains(t.Id)).GroupBy(t=>t.Metric).Select(g=>g.First()).Select(t=>
            {
                string info=t.Metric=="Pioneer"?"Erster Spieler der Bankperiode":t.Metric.StartsWith("Ruler:")?"Aktuelle Führung; frühester Gleichstand gewinnt":$"{(t.Metric=="RobberSkill"?bank.Security(Id(a),settings.BankRobbery,RobNow).RobberSkill:progress.Values.GetValueOrDefault(t.Metric)):N0} / {t.Required:N0}";
                return (t.Text,(Action<IPlayer>)(b=>b.SendLplkChat($"[LPLK Titel] {t.Text}: {info}. Fortschritt seit {progress.Since}; Bankreset beginnt eine neue Titelperiode.")));
            }).ToArray();
        },TitleProgressGroups);
    }
    private void TitleInfo(IPlayer p)
    {
        RequireTitles(p);var owned=bank!.OwnedTitles(Id(p));string active=bank.ActiveTitle(Id(p));
        p.SendLplkChat($"[LPLK Titel] Aktiv: {(active==""?"keiner":active)} · {owned.Length} freigeschaltet · !titel Nummer / aus.");
        p.SendLplkChat($"[LPLK Titel] Zinsen: {bank.TitleInterestRate(Id(p))/100m:0.##}% pro gültiger Runde ab {settings.InterestSystem.MinActivePlayersForInterest} aktiven Menschen; nur online im Team. Herrscher-Dauerlose: +{bank.RulerSlots(Id(p))}, Gesamtlimit bleibt gültig.");
        p.SendLplkChat("[LPLK Titel] Titel, Erstleistungen und Fortschritt gelten für die Bankperiode. Lottofähigkeiten und Ausbaurabatte sind unter Lotto erreichbar. Kopfgeldtitel zählen gesetzte und kassierte Prämien sowie überlebte Runden.");
    }
    private void TitleAnnouncements()
    {
        if(!Active||!settings.TitleSystem.Enabled||!ready)return;
        var players=Core.PlayerManager.GetAllValidPlayers().Where(Human).ToArray();var sessions=players.Select(p=>Id(p)+":"+p.SessionId).ToHashSet();titleJoined.IntersectWith(sessions);
        foreach(var p in players)
        {
            if(!CanUse(p)||!titleJoined.Add(Id(p)+":"+p.SessionId))continue;
            bank!.Open(Id(p),p.Name);bank.ObserveTitles(Id(p));
            string active=bank.ActiveTitle(Id(p));if(settings.TitleSystem.AnnounceTitlesOnJoin&&(active!=""||settings.TitleSystem.AnnounceJoinWithoutTitles))
                foreach(var receiver in players.Where(x=>!bank.Muted(Id(x))))receiver.SendLplkChat($"[LPLK Titel] {Ledger.SafeName(p.Name)} ist beigetreten · Aktiver Titel: {(active==""?"keiner":active)}.");
        }
        if(titleAnnouncementPeriod!=bank!.Period){titlePioneers=[];titleAnnouncementPeriod=bank.Period;}
        var owners=bank.PioneerOwners();
        if(settings.TitleSystem.AnnouncePioneerMilestones)foreach(var item in owners.Where(x=>!titlePioneers.ContainsKey(x.Key)))
        {
            var title=TitleCatalog.All.Single(t=>t.Id==item.Key);string name=bank.Accounts(int.MaxValue).FirstOrDefault(a=>a.Id==item.Value)?.Name??"Spieler";
            foreach(var p in players.Where(x=>!bank.Muted(Id(x))))p.SendLplkChat($"[LPLK Titel] Servermeilenstein: {Ledger.SafeName(name)} · {title.Text}!");
        }
        titlePioneers=owners;
    }
    private IPlayer[] TitleTeamPlayers()=>Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&p.Controller.TeamNum is 2 or 3).GroupBy(Id).Select(g=>g.First()).ToArray();
    private void BeginTitleRound()
    {
        if(!Active||!ready)return;var game=Core.EntitySystem.GetGameRules();if(game is null||game.WarmupPeriod)return;
        string id=boot+":"+titleMapEpoch+":"+game.TotalRoundsPlayed;if(titleRound==id)return;titleRound=id;titleRoundPeriod=bank!.Period;titleRoundEnded=false;
        var players=TitleTeamPlayers();if(players.Length<settings.LuxuryTax.MinActivePlayersForLuxuryTax)return;
        foreach(var p in players.Where(CanUse))bank!.Open(Id(p),p.Name);
        var payments=bank!.ApplyTitleRound("tax:"+id,bank.Period,players.Where(CanUse).Select(Id).ToArray(),true,players.Length);
        foreach(var payment in payments)if(settings.LuxuryTax.AnnounceToPlayer&&!bank.Muted(payment.Account))players.Single(p=>Id(p)==payment.Account).SendLplkChat($"[LPLK Bank] Luxussteuer: {payment.Amount:N0} $ ({payment.BasisPoints/100m:0.##}%) · Superpot {payment.Superpot:N0} $.");
        if(settings.LuxuryTax.AnnounceSummaryToChat&&payments.Length>0)foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(p=>Human(p)&&!bank.Muted(Id(p))))p.SendLplkChat($"[LPLK Bank] Luxussteuer: {payments.Sum(x=>(decimal)x.Amount):N0} $ von {payments.Length} Spielern · {payments.Sum(x=>(decimal)x.Superpot):N0} $ in den Superpot.");
    }
    private void EndTitleRound(int winner)
    {
        if(titleRound is null||titleRoundEnded||bank?.Period!=titleRoundPeriod)return;titleRoundEnded=true;
        if(!Active||!ready)return;var game=Core.EntitySystem.GetGameRules();var players=TitleTeamPlayers();
        if(!RobberyPolicy.ValidRound(settings.BankRobbery,game is not null,game?.WarmupPeriod??true,players.Length,winner)||players.Length<settings.InterestSystem.MinActivePlayersForInterest)return;
        foreach(var p in players.Where(CanUse))bank!.Open(Id(p),p.Name);
        var payments=bank!.ApplyTitleRound("interest:"+titleRound,bank.Period,players.Where(CanUse).Select(Id).ToArray(),false,players.Length);
        foreach(var payment in payments)if(settings.InterestSystem.AnnounceToPlayer&&payment.Amount>=settings.InterestSystem.AnnounceOnlyIfInterestAtLeast&&!bank.Muted(payment.Account))players.Single(p=>Id(p)==payment.Account).SendLplkChat($"[LPLK Titel] Titelzinsen: {payment.Amount:N0} $ ({payment.BasisPoints/100m:0.##}%).");
    }
    private void TitlesAdmin(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TitlesAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>[("Status",a=>a.SendLplkChat("[LPLK Titel] Fortschritt und Erstleistungen gehören zur Bankperiode; Zinsen/Steuer nur für aktive Menschen im Team. Aktueller Stand bleibt bei ungültiger Konfiguration aktiv."))]))(p);
    private void TitleSwitches(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TitleSwitchesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>[($"Titel: {(settings.TitleSystem.Enabled?"AN":"AUS")}",a=>{Save(a,current=>current with{TitleSystem=current.TitleSystem with{Enabled=!current.TitleSystem.Enabled}});TitleSwitches(a);}),($"Beitrittshinweise: {(settings.TitleSystem.AnnounceTitlesOnJoin?"AN":"AUS")}",a=>{Save(a,current=>current with{TitleSystem=current.TitleSystem with{AnnounceTitlesOnJoin=!current.TitleSystem.AnnounceTitlesOnJoin}});TitleSwitches(a);}),($"Pionierhinweise: {(settings.TitleSystem.AnnouncePioneerMilestones?"AN":"AUS")}",a=>{Save(a,current=>current with{TitleSystem=current.TitleSystem with{AnnouncePioneerMilestones=!current.TitleSystem.AnnouncePioneerMilestones}});TitleSwitches(a);}),($"Herrscherlose: {settings.TitleSystem.PermanentTicketsPerRulerTitle}",a=>a.SendLplkChat("[LPLK Titel] Zusätzliche Slots gelten bis zum vorhandenen Gesamtlimit. Erweitert: settings.json > TitleSystem."))]))(p);
    private void InterestAdmin(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> InterestAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>[($"Zinsen: {(settings.InterestSystem.Enabled?"AN":"AUS")}",a=>{Save(a,current=>current with{InterestSystem=current.InterestSystem with{Enabled=!current.InterestSystem.Enabled}});InterestAdmin(a);}),($"Mindestspieler: {settings.InterestSystem.MinActivePlayersForInterest}",a=>a.SendLplkChat("[LPLK Titel] Aktive menschliche T/CT-Spieler zählen; Tote zählen mit, Zuschauer und Bots nicht.")),("Zinssätze",a=>a.SendLplkChat($"[LPLK Titel] Roh-Basispunkte normal/Herrscher {settings.InterestSystem.InterestBasisPointsPerTitle}/{settings.InterestSystem.InterestBasisPointsPerRulerTitle}; Deckel {settings.InterestSystem.MaxTotalInterestBasisPoints} vor Teiler {settings.InterestSystem.InterestRateDivisor}. Weitere Regeln: settings.json > InterestSystem."))]))(p);
    private void TaxAdmin(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TaxAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>[($"Steuer: {(settings.LuxuryTax.Enabled?"AN":"AUS")}",a=>{Save(a,current=>current with{LuxuryTax=current.LuxuryTax with{Enabled=!current.LuxuryTax.Enabled}});TaxAdmin(a);}),($"Mindestspieler: {settings.LuxuryTax.MinActivePlayersForLuxuryTax}",a=>a.SendLplkChat("[LPLK Bank] Steuer nur am Rundenstart außerhalb des Warmups, online im Team.")),("Steuerstufen",a=>Page(a,"Steuerstufen",_=>settings.LuxuryTax.TaxTiers.OrderBy(t=>t.Threshold).Select(t=>($"Ab {t.Threshold:N0} $: {t.TaxBasisPoints/100m:0.##}%",(Action<IPlayer>)(b=>b.SendLplkChat("[LPLK Bank] Die höchste erreichte Stufe gilt für das gesamte Bankguthaben.")))).ToArray(),TaxAdmin,true)),($"Superpotanteil: {settings.LuxuryTax.SuperpotShareBasisPoints/100m:0.##}%",a=>a.SendLplkChat("[LPLK Bank] Der Rest wird aus der Spielerwirtschaft entfernt. Erweiterte Regeln: settings.json > LuxuryTax."))]))(p);
}



