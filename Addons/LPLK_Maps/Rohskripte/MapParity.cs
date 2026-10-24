using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Maps;

public sealed partial class Plugin
{
    private const string ExtendChoice="$extend";
    private readonly HashSet<ulong> extendVotes=[];
    private readonly Dictionary<ulong,string> directVotes=[];
    private int extensions,earliestEndRound;
    private long nextVoteAllowed,autoVotePostponedUntil;
    private MapExtension? originalLimits,appliedLimits;
    private bool GameplayAvailable=>module.Active&&settings.Enabled&&!settings.MeasurementOnly&&!matchEnded&&pending is null;
    private bool MayManageCurrent(IPlayer p,string permission)=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&(module.CanAdmin(p)||Core.Permission.PlayerHasPermission(p.SteamID,permission));
    private bool MayManage(IPlayer p,Func<MapSettings,string> permission){RefreshSettings();return MayManageCurrent(p,permission(settings));}
    private MapExtension Limits()=>new(Core.ConVar.Find<float>("mp_timelimit")?.Value??0,Core.ConVar.Find<int>("mp_maxrounds")?.Value??0,Core.ConVar.Find<int>("mp_winlimit")?.Value??0);
    private void PutLimits(MapExtension limits)
    {
        if(Core.ConVar.Find<float>("mp_timelimit") is {} time)time.Value=limits.Minutes;
        if(Core.ConVar.Find<int>("mp_maxrounds") is {} max)max.Value=limits.Rounds;
        if(Core.ConVar.Find<int>("mp_winlimit") is {} wins)wins.Value=limits.Wins;
    }
    private bool CanExtend(bool endVote=false)
    {
        if(!GameplayAvailable||!(endVote?settings.EndVoteAllowExtend:settings.ExtendEnabled)||warmup&&!settings.ExtendInWarmup||rounds<settings.ExtendMinRounds||Voters().Count<settings.ExtendMinPlayers||extensions>=settings.ExtendLimit)return false;
        try{var l=Limits();MapPolicy.Extension(settings,extensions,l.Minutes,l.Rounds,l.Wins);return true;}catch{return false;}
    }
    private bool ExtendMap(bool endVote=false)
    {
        if(!CanExtend(endVote))return false;
        var before=Limits();var after=MapPolicy.Extension(settings,extensions,before.Minutes,before.Rounds,before.Wins);
        try
        {
            PutLimits(after);if(Limits()!=after)throw new InvalidOperationException("Runden-/Zeitlimit nicht bestätigt.");
        }
        catch(Exception e)
        {
            // Roll back only values that still equal our attempted write.
            RestoreOwnedLimits(before,after);Core.Logger.LogError(e,"LPLK_Maps: Verlängerung fehlgeschlagen");return false;
        }
        originalLimits=originalLimits is {} original&&appliedLimits is {} previous
            ?new(before.Minutes==previous.Minutes?original.Minutes:before.Minutes,before.Rounds==previous.Rounds?original.Rounds:before.Rounds,before.Wins==previous.Wins?original.Wins:before.Wins):before;
        appliedLimits=after;extensions++;extendVotes.Clear();directVotes.Clear();rtv.Clear();nextMap=null;changeAtRoundEnd=false;endVoteStarted=false;
        earliestEndRound=rounds+1;nextVoteAllowed=Environment.TickCount64+60000;
        autoVotePostponedUntil=Environment.TickCount64+Math.Max(settings.ExtendMinutes,settings.AutoVoteMinutes)*60000L;
        Core.PlayerManager.SendChat($"[LPLK] Map verlängert ({extensions}/{settings.ExtendLimit}): Zeitlimit {after.Minutes:0.##} Minuten, Rundenlimit {after.Rounds}, Sieglimit {after.Wins}.");WriteStatus();return true;
    }
    private void RestoreOwnedLimits(MapExtension before,MapExtension after)
    {
        try
        {
            if(Core.ConVar.Find<float>("mp_timelimit") is {} t&&t.Value==after.Minutes)t.Value=before.Minutes;
            if(Core.ConVar.Find<int>("mp_maxrounds") is {} r&&r.Value==after.Rounds)r.Value=before.Rounds;
            if(Core.ConVar.Find<int>("mp_winlimit") is {} w&&w.Value==after.Wins)w.Value=before.Wins;
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Maps: eigene Verlängerung konnte nicht zurückgesetzt werden");}
    }
    private void ResetParity()
    {
        if(originalLimits is {} before&&appliedLimits is {} after)RestoreOwnedLimits(before,after);
        originalLimits=appliedLimits=null;extensions=earliestEndRound=0;nextVoteAllowed=autoVotePostponedUntil=0;extendVotes.Clear();directVotes.Clear();
    }
    private double? RemainingSeconds()
    {
        var rules=Core.EntitySystem.GetGameRules();float minutes=Core.ConVar.Find<float>("mp_timelimit")?.Value??0;
        if(rules is null||rules.WarmupPeriod||!float.IsFinite(minutes)||minutes<=0)return null;
        double elapsed=Core.Engine.GlobalVars.CurrentTime-rules.GameStartTime;
        return double.IsFinite(elapsed)&&elapsed>=0?Math.Max(0,minutes*60d-elapsed):null;
    }
    private void TickParity(long now)
    {
        if(!GameplayAvailable)return;
        var ids=Voters().Select(p=>p.SteamID).ToHashSet();extendVotes.IntersectWith(ids);
        foreach(var id in directVotes.Keys.Where(id=>!ids.Contains(id)).ToArray())directVotes.Remove(id);
        if(voteEnds==0&&extendVotes.Count>0&&extendVotes.Count>=MapPolicy.RequiredRtv(ids.Count,settings.ExtendPercent))ExtendMap();
        if(voteEnds==0&&nextMap is null&&settings.DirectVotemapEnabled&&ids.Count>=settings.DirectVotemapMinPlayers)
        {
            var legal=Eligible().ToDictionary(m=>m.Id);var choice=directVotes.Values.Where(legal.ContainsKey).GroupBy(x=>x).OrderByDescending(g=>g.Count()).FirstOrDefault();
            if(choice is not null&&choice.Count()>=MapPolicy.RequiredRtv(ids.Count,settings.DirectVotemapPercent))
            {nextMap=legal[choice.Key];directVotes.Clear();if(settings.DirectVotemapAtRoundEnd)changeAtRoundEnd=true;else Schedule(nextMap,settings.ChangeDelaySeconds);}
        }
        if(settings.EndOfMapVote&&!warmup&&RemainingSeconds() is {} seconds)
        {
            if(seconds<=0){MatchEnded();return;}
            if(settings.EndVoteSecondsBeforeTimeEnd>0&&seconds<=settings.EndVoteSecondsBeforeTimeEnd)CheckEndVote(true);
        }
    }
    private List<MapEntry> VotePool(Reason source)
    {
        var pool=Eligible();
        if(source==Reason.EndOfMap&&settings.EndOfMapSameCategoryOnly)
        {
            var current=maps.FirstOrDefault(m=>MapPolicy.Confirmed(m,CurrentMap,Workshop));var category=current is null?MapPolicy.Category(CurrentMap):MapPolicy.Category(current);
            if(category!=MapCategory.Other)pool=pool.Where(m=>MapPolicy.Category(m)==category).ToList();
        }
        return pool;
    }
    private void Categories(IPlayer p,string title,Func<List<MapEntry>> choices,Action<IPlayer,MapEntry> action,Func<IPlayer,bool> allowed)
    {
        if(!allowed(p))return;
        if(!settings.CategorizedMenus){Selection(p,title,choices(),(a,m)=>{if(allowed(a))action(a,m);});return;}
        int serial=generation;
        CompactMenu.Open(Core,p,title,()=>module.Active&&serial==generation,allowed,a=>Enum.GetValues<MapCategory>().Where(c=>choices().Any(m=>MapPolicy.Category(m)==c)).Select(c=>(MapPolicy.CategoryName(c),(Action<IPlayer>)(b=>Selection(b,title+" · "+MapPolicy.CategoryName(c),choices().Where(m=>MapPolicy.Category(m)==c).ToList(),(actor,m)=>{if(allowed(actor))action(actor,m);},back:actor=>Categories(actor,title,choices,action,allowed))))).ToArray(),a=>a.ExecuteCommand("sw_menu"));
    }
    [Command("extend")][CommandAlias("ext")][CommandAlias("extendmap")]
    public void ExtendCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p||!CanVote(p)||voteEnds>0||!CanExtend()){ctx.Reply("[LPLK] Verlängerung derzeit nicht möglich.");return;}
        if(!extendVotes.Add(p.SteamID)){ctx.Reply("[LPLK] Deine Verlängerungsstimme ist bereits gespeichert.");return;}
        Core.PlayerManager.SendChat($"[LPLK] Verlängern: {extendVotes.Count}/{MapPolicy.RequiredRtv(Voters().Count,settings.ExtendPercent)} Stimmen.");
        if(extendVotes.Count>=MapPolicy.RequiredRtv(Voters().Count,settings.ExtendPercent))ExtendMap();
    }
    [Command("unrtv")]
    public void UnRtv(ICommandContext ctx){if(ctx.Sender is {} p)ctx.Reply(rtv.Remove(p.SteamID)?"[LPLK] RTV-Stimme zurückgezogen.":"[LPLK] Keine RTV-Stimme gespeichert.");}
    [Command("revote")]
    public void Revote(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p||!CanVote(p)||voteEnds<=Environment.TickCount64)return;
        if(ctx.Args.Length==0){ShowVote(p);return;}
        var choices=BallotChoices();if(ctx.Args.Length!=1||!int.TryParse(ctx.Args[0],out int number)||number<1||number>choices.Count){ctx.Reply("[LPLK] Ungültige Stimmnummer; !revote öffnet die Auswahl.");return;}
        CastBallot(p,choices[number-1]);
    }
    [Command("votemap")]
    public void Votemap(ICommandContext ctx)
    {
        if(!settings.DirectVotemapEnabled){MapChooser(ctx);return;}
        if(ctx.Sender is not {} p||!CanVote(p)||!GameplayAvailable||voteEnds>0||nextMap is not null||Voters().Count<settings.DirectVotemapMinPlayers){ctx.Reply("[LPLK] Direkte Mapabstimmung derzeit nicht verfügbar.");return;}
        if(ctx.Args.Length>0){var map=MapPolicy.Resolve(Eligible(),string.Join(" ",ctx.Args));if(map is null)ctx.Reply("[LPLK] Map nicht eindeutig oder nicht wählbar.");else DirectVote(p,map);return;}
        Categories(p,"Map direkt wählen",()=>Eligible(),DirectVote,a=>CanVote(a)&&GameplayAvailable&&voteEnds==0&&nextMap is null);
    }
    private void DirectVote(IPlayer p,MapEntry selected)
    {
        if(!settings.DirectVotemapEnabled||!GameplayAvailable||!CanVote(p)||voteEnds>0||nextMap is not null||!Eligible().Any(m=>m.Id==selected.Id))return;
        directVotes[p.SteamID]=selected.Id;Close(p.PlayerID);Core.PlayerManager.SendChat($"[LPLK] {selected.Name}: {directVotes.Values.Count(v=>v==selected.Id)}/{MapPolicy.RequiredRtv(Voters().Count,settings.DirectVotemapPercent)} Stimmen.");
    }
    [Command("nextmap")]
    public void NextMapCommand(ICommandContext ctx)=>ctx.Reply((pending??nextMap) is {} map?"[LPLK] Nächste Map: "+map.Name:"[LPLK] Nächste Map noch nicht gewählt.");
    [Command("timeleft")]
    public void Timeleft(ICommandContext ctx)
    {
        var remaining=RemainingSeconds();var limits=Limits();ctx.Reply($"[LPLK] Zeit: {(remaining is {} s?TimeSpan.FromSeconds(s).ToString(@"hh\:mm\:ss"):"kein aktives Zeitlimit")} · Rundenlimit {limits.Rounds} · Sieglimit {limits.Wins} · Verlängerungen {extensions}/{settings.ExtendLimit}.");
    }
    [Command("maplist")][CommandAlias("maps")][CommandAlias("hsmaps")][CommandAlias("workshopmaps")]
    public void MapList(ICommandContext ctx)
    {
        if(ctx.Sender is {} p)Categories(p,"Mapliste",()=>maps.Where(m=>m.IsMap&&!m.Removed).ToList(),(a,m)=>a.SendChat($"[LPLK] {m.Name} · {m.Id} · {m.MinPlayers}–{m.MaxPlayers} Spieler · {(MapPolicy.LimitsApproved(m)?"Grenzen freigegeben":"Grenzprüfung offen")}"),a=>module.Active&&a.IsValid);
        else ctx.Reply($"[LPLK] {maps.Count(m=>m.IsMap&&!m.Removed)} Maps im Katalog.");
    }
    [Command("setnextmap")]
    public void SetNextMap(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!MayManage(p,s=>s.SetNextMapPermission)){ctx.Reply("[LPLK] Keine Berechtigung.");return;}
        if(!GameplayAvailable){ctx.Reply("[LPLK] Mapwahl derzeit nicht aktiv.");return;}
        var map=MapPolicy.Resolve(maps.Where(m=>Available(m)&&m.Enabled&&m.InPool),string.Join(" ",ctx.Args));
        if(map is null){ctx.Reply("[LPLK] setnextmap <eindeutige Map oder ID>");return;}
        CancelVote();nextMap=map;endVoteStarted=true;ctx.Reply("[LPLK] Nächste Map: "+map.Name);
    }
    [Command("mapsvote")]
    public void AdminMapsVote(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){if(!MayManage(p,s=>s.MapsVotePermission)){ctx.Reply("[LPLK] Keine Berechtigung.");return;}CustomVoteMenu(p,new HashSet<string>());return;}
        ctx.Reply(StartVote(Reason.Admin)?"[LPLK] Mapabstimmung gestartet.":"[LPLK] Abstimmung derzeit nicht möglich.");
    }
    private void CustomVoteMenu(IPlayer player,HashSet<string> selected)
    {
        int serial=generation;
        bool Allowed(IPlayer p)=>MayManage(p,s=>s.MapsVotePermission)&&GameplayAvailable&&voteEnds==0&&nextMap is null;
        selected.IntersectWith(Eligible().Select(m=>m.Id));
        CompactMenu.Open(Core,player,$"Admin-Mapwahl · {selected.Count} ausgewählt",()=>module.Active&&serial==generation,Allowed,_=>
        [("Maps auswählen",p=>Categories(p,"Map zur Abstimmung",()=>Eligible(),(actor,map)=>{if(!selected.Add(map.Id))selected.Remove(map.Id);CustomVoteMenu(actor,selected);},Allowed)),
         ("Abstimmung starten",p=>{if(!StartSelectedVote(p,selected))p.SendChat("[LPLK] Mindestens eine weiterhin zulässige Map auswählen; Abstimmung muss frei sein.");}),
         ("Auswahl leeren",p=>CustomVoteMenu(p,new HashSet<string>()))],p=>p.ExecuteCommand("sw_admin"));
    }
    private bool StartSelectedVote(IPlayer player,IReadOnlySet<string> selected)
    {
        if(!MayManage(player,s=>s.MapsVotePermission)||selected.Count==0)return false;
        return BeginVote(Reason.Admin,selected);
    }
    [Command("cyclemenu")][CommandAlias("mapcycle")]
    public void CycleMenu(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p||!MayManage(p,s=>s.CyclePermission)){ctx.Reply("[LPLK] Keine Berechtigung.");return;}
        int serial=generation;
        CompactMenu.Open(Core,p,"Maprotation",()=>module.Active&&serial==generation,a=>MayManage(a,s=>s.CyclePermission),_=>
        [($"Rotation: {(settings.CycleEnabled?"AN":"AUS")}",a=>CycleSave(a,s=>s with{CycleEnabled=!s.CycleEnabled})),
         ($"Zufällig: {(settings.CycleRandom?"AN":"AUS")}",a=>CycleSave(a,s=>s with{CycleRandom=!s.CycleRandom})),
         ("Maps",a=>Selection(a,"Rotation · Entfernen",maps.Where(m=>m.InPool&&m.Enabled&&!m.Removed).ToList(),(actor,m)=>{if(MayManage(actor,s=>s.CyclePermission)){ReplaceMap(m with{Enabled=false});actor.ExecuteCommand("sw_cyclemenu");}}))],a=>a.ExecuteCommand("sw_admin"));
    }
    // Preserve the engine context only at command dispatch; menu callbacks call the
    // view directly, rather than retaining an old ICommandContext or native pointer.
    private void CycleSave(IPlayer p,Func<MapSettings,MapSettings> edit)
    {
        if(!MayManage(p,s=>s.CyclePermission))return;
        try{ChangeConfiguration(edit,false,s=>MayManageCurrent(p,s.CyclePermission));p.SendChat("[LPLK] Maprotation gespeichert.");p.ExecuteCommand("sw_cyclemenu");}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException){p.SendChat("[LPLK] Maprotation nicht gespeichert: "+e.Message);}
    }
    [Command("addmap")]
    public void AddMap(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!MayManage(p,s=>s.CyclePermission)){ctx.Reply("[LPLK] Keine Berechtigung.");return;}
        string query=string.Join(" ",ctx.Args).Trim();var map=MapPolicy.Resolve(maps,query);
        if(map is not null){ReplaceMap(map with{Enabled=true,Removed=false,InPool=true});ctx.Reply("[LPLK] Map im Pool aktiviert; bestehende Messungen und Freigaben bleiben erhalten.");return;}
        if(MapPolicy.WorkshopId(query) is not {} id){ctx.Reply("[LPLK] addmap <eindeutige bekannte Map oder Workshop-ID>");return;}
        if(settings.ExcludedWorkshopIds.Contains(id)){ctx.Reply("[LPLK] Workshop-Eintrag ist ausdrücklich ausgeschlossen.");return;}
        try{ChangeConfiguration(s=>s with{WorkshopIds=s.WorkshopIds.Append(id).Distinct().ToArray()},false,s=>ctx.Sender is not {} actor||MayManageCurrent(actor,s.CyclePermission));RefreshCollection();}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException){ctx.Reply("[LPLK] Workshop-ID nicht gespeichert: "+e.Message);return;}
        ctx.Reply("[LPLK] Workshop-ID vorgemerkt. Maptyp, Messung und Grenzen müssen vor der Wahl bestätigt werden.");
    }
    [Command("removemap")]
    public void RemoveMap(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!MayManage(p,s=>s.CyclePermission)){ctx.Reply("[LPLK] Keine Berechtigung.");return;}
        var map=MapPolicy.Resolve(maps,string.Join(" ",ctx.Args));if(map is null){ctx.Reply("[LPLK] removemap <eindeutige Map oder ID>");return;}
        ReplaceMap(map with{Enabled=false});ctx.Reply("[LPLK] Map aus der aktiven Rotation entfernt; Dateien, Messwerte und Freigaben bleiben erhalten.");
    }
    private List<MapEntry> BallotChoices()=>candidates.Concat(reason==Reason.EndOfMap&&CanExtend(true)?[new MapEntry{Id=ExtendChoice,Name="Aktuelle Map verlängern"}]:Array.Empty<MapEntry>()).ToList();
    private void CastBallot(IPlayer actor,MapEntry map)
    {
        if(!GameplayAvailable||!CanVote(actor)||voteEnds<=Environment.TickCount64)return;
        if(map.Id==ExtendChoice){if(reason!=Reason.EndOfMap||!CanExtend(true))return;}
        else if(!candidates.Any(m=>m.Id==map.Id)||!Eligible().Any(m=>m.Id==map.Id)){actor.SendChat("[LPLK] Map inzwischen nicht mehr wählbar.");return;}
        ballots[actor.SteamID]=map.Id;actor.SendChat("[LPLK] Stimme gespeichert: "+map.Name);Close(actor.PlayerID);
    }
}
