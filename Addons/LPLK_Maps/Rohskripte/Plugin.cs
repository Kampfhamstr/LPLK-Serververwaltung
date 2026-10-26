using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Maps;

[PluginMetadata(Id="LPLK_Maps",Name="LPLK_Maps",Version="0.4.17",Author="LPLK",Description="MapChooser, RTV, Nominate und Workshop-Verwaltung",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private MapSettings settings=new();
    private List<MapEntry> maps=[];
    private List<string> history=[];
    private readonly HashSet<ulong> rtv=[];
    private readonly Dictionary<ulong,string> nominations=[],ballots=[];
    private readonly Dictionary<int,(IPlayer Player,IMenuAPI Menu,Guid Token)> menus=[];
    private List<MapEntry> candidates=[];
    private HashSet<string> nominatedCandidates=[];
    private enum Reason { Rtv, Admin, EndOfMap }
    private Reason reason;
    private long mapStarted, voteEnds, nextCheck, transitionDue;
    private int generation, rounds, attempts;
    private bool warmup=true,matchEnded,endVoteStarted,changeAtRoundEnd,transitionIssued;
    private MapEntry? nextMap,pending;
    private string lastTransition="Kein Wechsel angefordert";
    private string MapsPath=>Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"maps.json");
    private string HistoryPath=>Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"history.json");
    private string CurrentMap { get { try{return Core.Engine.GlobalVars.MapName;}catch{return "";} } }
    private string Workshop { get { try{return Core.Engine.WorkshopId??"";}catch{return "";} } }
    private List<IPlayer> Humans()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID>0).ToList();
    private bool CanVote(IPlayer p)=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&MapPolicy.CanVote(p.Controller?.TeamNum??0,settings.AllowSpectatorsToVote);
    private List<IPlayer> Voters()=>Humans().Where(CanVote).ToList();
    private List<MapEntry> Eligible(bool relax=false,bool forceOther=false)=>MapPolicy.Eligible(maps.Where(Votable),CurrentMap,Workshop,Humans().Count,history,settings.CooldownMaps,relax,settings.AllowCurrentMap&&!forceOther);

    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private string startupCollection="";
    private void LoadCore(bool hotReload)
    { startupCollection=WorkshopCollections.ReadStartupCollection()??""; ChatTheme.Initialize("LPLK_Maps",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Maps","Maps","lplk.maps.admin",OpenAdmin);
        settings=ReadConfiguration();
        startupMapGate.Arm(settings.RotateStartupMap,hotReload,Environment.TickCount64);startupRotationStatus=startupMapGate.Pending?"Warte auf bereite Basiskarte":"Kein Kaltstartwechsel";
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        maps=JsonSerializer.Deserialize<List<MapEntry>>(File.ReadAllText(File.Exists(MapsPath)?MapsPath:Path.Combine(LPLK.Common.RoundFileStore.PluginPath(Core),"resources","maps.seed.json")),options)??[];
        if(maps.Any(m=>m.MeasurementVersion<2&&m.MeasuredAt is not null))
        {
            maps=maps.Select(m=>m.MeasurementVersion<2?m with{MeasuredAt=null,LastProbeAt=null,ProbeError=null,TSpawns=null,CtSpawns=null,BombSites=null,Hostages=null,LimitProposal=null}:m).ToList();
            MapPolicy.Validate(maps);AtomicJson.Write(MapsPath,maps);
        }
        if(maps.Any(m=>m.MeasuredAt is null&&m.ProbeError is null&&m.LastProbeAt is not null))
        {
            maps=maps.Select(m=>m.MeasuredAt is null&&m.ProbeError is null?m with{LastProbeAt=null}:m).ToList();AtomicJson.Write(MapsPath,maps);
        }
        MapPolicy.Validate(maps);if(!File.Exists(MapsPath))AtomicJson.Write(MapsPath,maps);
        if(File.Exists(HistoryPath))history=JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryPath))??[];
        LoadCollectionCache();module.Active=true;mapStarted=Environment.TickCount64;Scan();
        Core.GameEvent.HookPost<EventRoundAnnounceWarmup>(_=>{warmup=true;rounds=0;rtv.Clear();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventWarmupEnd>(_=>{warmup=false;rounds=0;return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundStart>(_=>{CheckEndVote(false);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundEnd>(_=>
        {
            if(!warmup){rounds++;if(changeAtRoundEnd&&nextMap is {} selected&&voteEnds==0){changeAtRoundEnd=false;Schedule(selected,2);}else CheckEndVote(false);}
            return HookResult.Continue;
        });
        Core.GameEvent.HookPost<EventRoundAnnounceMatchPoint>(_=>{CheckEndVote(true);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventGamePhaseChanged>(e=>{if(e.NewPhase==(short)GamePhase.GAMEPHASE_MATCH_ENDED)MatchEnded();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventCsWinPanelMatch>(_=>{if(Core.Game.MatchData.Phase!=GamePhase.GAMEPHASE_HALFTIME)MatchEnded();return HookResult.Continue;});
        Core.Scheduler.DelayBySeconds(3,()=>{if(!module.Active)return;ReadWarmup();RecordMap();MeasureCurrent(null);});
        Core.Logger.LogInformation("LPLK_Maps 0.4.17: {Count} Katalogeinträge, {Available} verfügbar; {Candidates} Kandidaten / {Seconds}s",maps.Count,maps.Count(Available),settings.MapsToShow,settings.VoteSeconds);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Maps.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,command)=>module.Active&&settings.Enabled&&!settings.MeasurementOnly&&CanVote(p)&&command switch
            {
                "sw_nominate"=>settings.NominationEnabled&&voteEnds==0&&nextMap is null&&pending is null,
                "sw_rtv"=>settings.RtvEnabled&&pending is null&&!matchEnded,
                "sw_mapchooser"=>voteEnds>Environment.TickCount64,
                _=>false
            }),
            ["unavailable"]=(Func<IPlayer,string,string>)((p,command)=>
                !module.Active||!settings.Enabled?"Mapabstimmungen sind auf diesem Server ausgeschaltet.":
                settings.MeasurementOnly?"Der Server ist im Map-Prüfmodus. RTV und Nominierungen sind dabei ausgeschaltet.":
                !CanVote(p)?"Du bist derzeit nicht stimmberechtigt.":
                pending is not null?"Ein Mapwechsel läuft bereits.":
                nextMap is not null?"Die nächste Map steht bereits fest: "+nextMap.Name:
                command=="sw_rtv"&&!settings.RtvEnabled?"RTV ist auf diesem Server ausgeschaltet.":
                command=="sw_nominate"&&!settings.NominationEnabled?"Nominierungen sind auf diesem Server ausgeschaltet.":
                voteEnds>0?"Eine Mapabstimmung läuft bereits. Öffne Abstimmung.":"Diese Mapfunktion ist zurzeit geschlossen.")
        });
    }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){StopWorkshopTest();ResetParity();if(module is not null)module.Active=false;generation++;collectionCancellation?.Cancel();CloseMenus(); LPLK.Common.DeferredJson.Drain(); }
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e)
    {
        ResetParity();generation++;CancelVote();rtv.Clear();nominations.Clear();nextMap=null;changeAtRoundEnd=false;
        startupMapGate.MapLoaded(Environment.TickCount64);emptyMapClock.Reset();mapStarted=Environment.TickCount64;rounds=0;warmup=true;matchEnded=false;endVoteStarted=false;
        if(settings.RescanOnMapLoad)Scan();
        int serial=generation;
        Core.Scheduler.DelayBySeconds(3,()=>{if(module.Active&&serial==generation){ReadWarmup();RecordMap();MeasureCurrent(null);if(transitionIssued)transitionDue=Environment.TickCount64;}});
    }
    [EventListener<EventDelegates.OnMapUnload>]public void MapUnload(IOnMapUnloadEvent e){ResetParity();generation++;CancelVote();}
    [EventListener<EventDelegates.OnClientDisconnected>]public void Disconnect(IOnClientDisconnectedEvent e)=>Close(e.PlayerId);
    private void ReadWarmup(){if(Core.EntitySystem.GetGameRules() is {} rules)warmup=rules.WarmupPeriod;}
    private void RecordMap()
    {
        var map=maps.FirstOrDefault(m=>MapPolicy.Confirmed(m,CurrentMap,Workshop));if(map is null)return;
        if(history.LastOrDefault()!=map.Id){history.Add(map.Id);history=history.TakeLast(40).ToList();DeferredJson.Queue(HistoryPath,history.ToArray());}
        PrepareStartupMap();
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        long now=Environment.TickCount64;if(module?.Active!=true||now<nextCheck)return;nextCheck=now+500;
        CheckConfiguration(now);
        PollWorkshopTest(now);
        TickStartupRotation(now);TickEmptyRotation(now);
        if(probeId is not null&&pending is not null&&!transitionIssued&&AnyHuman())
        {var cancelled=maps.FirstOrDefault(m=>m.Id==probeId);if(cancelled is not null)ReplaceMap(cancelled with{LastProbeAt=null});probeId=null;pending=null;lastTransition="Automatische Prüfung pausiert: Spieler verbunden";WriteStatus();}
        if(pending is not null&&now>=transitionDue){if(transitionIssued)VerifyPending();else IssuePending();}
        AutoProbe(now);
        if(!settings.Enabled||settings.MeasurementOnly)return;
        TickParity(now);
        if(voteEnds>0&&now>=voteEnds)FinishVote();
        else if(voteEnds==0&&pending is null&&nextMap is null&&!matchEnded)
        {
            var voters=Voters();rtv.IntersectWith(voters.Select(p=>p.SteamID));
            if(rtv.Count>0&&MapPolicy.RtvAllowed(settings,warmup,rounds,voters.Count)&&rtv.Count>=MapPolicy.RequiredRtv(voters.Count,settings.RtvPercent))StartVote(Reason.Rtv);
            else if(settings.AutoVoteMinutes>0&&!warmup&&!endVoteStarted&&now>=nextVoteAllowed&&now>=autoVotePostponedUntil&&now-mapStarted>=settings.AutoVoteMinutes*60000L)StartVote(Reason.EndOfMap);
        }
    }
    private void CheckEndVote(bool force)
    {
        if(Environment.TickCount64<nextVoteAllowed||rounds<earliestEndRound||!settings.Enabled||settings.MeasurementOnly||!settings.EndOfMapVote||warmup||endVoteStarted||voteEnds>0||nextMap is not null||pending is not null||matchEnded)return;
        int total=0,max=0;
        foreach(var team in Core.EntitySystem.GetAllEntitiesByClass<CCSTeam>())
        {int score=team.ScoreFirstHalf+team.ScoreSecondHalf+team.ScoreOvertime;total+=score;max=Math.Max(max,score);}
        if(force||MapPolicy.NearMatchEnd(total,max,Core.ConVar.Find<int>("mp_maxrounds")?.Value??0,Core.ConVar.Find<int>("mp_winlimit")?.Value??0,settings.TriggerRoundsBeforeEnd))endVoteStarted=StartVote(Reason.EndOfMap);
    }
    private void MatchEnded()
    {
        if(!settings.Enabled||settings.MeasurementOnly||matchEnded)return;matchEnded=true;if(voteEnds>0)FinishVote();if(pending is not null)return;
        if(nextMap is not null&&settings.ForceDifferentMapAtMatchEnd&&MapPolicy.Matches(nextMap,CurrentMap,Workshop))nextMap=null;
        if(nextMap is null&&settings.FallbackRandomMap)
        {
            if(settings.RescanBeforeVote)RescanForVote();var pool=Eligible(false,true);if(pool.Count==0)pool=Eligible(true,true);
            if(pool.Count>0)nextMap=settings.CycleEnabled&&!settings.CycleRandom?MapPolicy.NextCycle(maps,pool.Select(m=>m.Id).ToHashSet(),history.LastOrDefault()??""):pool[Random.Shared.Next(pool.Count)];
        }
        if(nextMap is {} selected)Schedule(selected,settings.MatchEndDelaySeconds);
        else Core.Logger.LogWarning("LPLK_Maps: Matchende ohne gültige andere Map; Pool und Workshop-Dateien prüfen.");
    }
    [Command("rtv")]
    public void Rtv(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p||!CanVote(p)){ctx.ReplyLplk("[LPLK] Du bist derzeit nicht stimmberechtigt.");return;}
        if(!settings.Enabled||settings.MeasurementOnly||!settings.RtvEnabled||pending is not null||matchEnded){ctx.ReplyLplk("[LPLK] RTV derzeit nicht verfügbar.");return;}
        if(voteEnds>0){ShowVote(p);return;}if(nextMap is not null){ctx.ReplyLplk("[LPLK] Nächste Map steht bereits fest: "+nextMap.Name);return;}
        int count=Voters().Count;
        if(!MapPolicy.RtvAllowed(settings,warmup,rounds,count)){ctx.ReplyLplk($"[LPLK] RTV benötigt {settings.RtvMinPlayers} Spieler und {settings.RtvMinRounds} gespielte Runden; Warmup: {(settings.RtvInWarmup?"erlaubt":"gesperrt")}.");return;}
        if(Environment.TickCount64-mapStarted<settings.RtvDelaySeconds*1000L){ctx.ReplyLplk("[LPLK] RTV ist kurz nach Mapbeginn noch gesperrt.");return;}
        if(!rtv.Add(p.SteamID)){ctx.ReplyLplk("[LPLK] Deine RTV-Stimme ist bereits gespeichert.");return;}
        Core.PlayerManager.SendLplkChat($"[LPLK] RTV: {rtv.Count}/{MapPolicy.RequiredRtv(count,settings.RtvPercent)} Stimmen.");
    }
    [Command("nominate")][CommandAlias("nom")][CommandAlias("yd")]
    public void Nominate(ICommandContext ctx)
    {
        if(ctx.Sender is not {} p||!CanVote(p))return;
        if(!settings.NominationEnabled||!settings.Enabled||settings.MeasurementOnly||voteEnds>0||nextMap is not null||pending is not null){ctx.ReplyLplk("[LPLK] Nominierung derzeit geschlossen.");return;}
        if(settings.RescanBeforeVote)RescanForVote();var choices=Eligible();
        if(ctx.Args.Length>0)
        {
            string query=string.Join(" ",ctx.Args);var found=MapPolicy.Resolve(choices,query);
            if(found is null){ctx.ReplyLplk("[LPLK] Map nicht wählbar: Spielerzahl, aktuelle Map, Sperre, Cooldown oder nicht installiert.");return;}SetNomination(p,found);return;
        }
        Categories(p,"Map nominieren",()=>Eligible(),SetNomination,a=>GameplayAvailable&&settings.NominationEnabled&&CanVote(a)&&voteEnds==0&&nextMap is null);
    }
    private void SetNomination(IPlayer p,MapEntry map)
    {
        if(!settings.NominationEnabled||!CanVote(p)||!settings.Enabled||settings.MeasurementOnly||voteEnds>0||nextMap is not null||pending is not null||!Eligible().Any(m=>m.Id==map.Id)){p.SendLplkChat("[LPLK] Map inzwischen nicht mehr nominierbar.");return;}
        nominations[p.SteamID]=map.Id;p.SendLplkChat("[LPLK] Nominiert: "+map.Name);Close(p.PlayerID);
    }
    [Command("mapchooser")]
    public void MapChooser(ICommandContext ctx){if(ctx.Sender is {} p){if(voteEnds>0&&CanVote(p))ShowVote(p);else ctx.ReplyLplk("[LPLK] Mit !nominate vorschlagen, mit !rtv abstimmen.");}}
    private bool StartVote(Reason source)=>BeginVote(source,null);
    private bool BeginVote(Reason source,IReadOnlySet<string>? selected)
    {
        if(Environment.TickCount64<nextVoteAllowed||!settings.Enabled||settings.MeasurementOnly||voteEnds>0||pending is not null||nextMap is not null||matchEnded)return false;
        if(settings.RescanBeforeVote)RescanForVote();var pool=VotePool(source);if(selected is not null)pool=pool.Where(m=>selected.Contains(m.Id)).ToList();
        if(pool.Count==0){rtv.Clear();nextVoteAllowed=Environment.TickCount64+Math.Max(5,settings.FailedVoteCooldownSeconds)*1000L;Core.PlayerManager.SendLplkChat("[LPLK] Keine zulässige Map für eine Abstimmung vorhanden.");return false;}
        var voters=Voters();var online=voters.Select(p=>p.SteamID).ToHashSet();
        nominatedCandidates=nominations.Where(n=>online.Contains(n.Key)).Select(n=>n.Value).ToHashSet();
        int limit=source==Reason.EndOfMap&&settings.EndVoteMapsToShow>0?settings.EndVoteMapsToShow:settings.MapsToShow;
        candidates=selected is not null?pool:MapPolicy.CandidateSelection(pool.OrderBy(_=>Random.Shared.Next()).ToList(),nominatedCandidates,limit,settings.BalancedCategories);
        int seconds=(source==Reason.EndOfMap||selected is not null)&&settings.EndVoteSeconds>0?settings.EndVoteSeconds:settings.VoteSeconds;
        reason=source;if(source==Reason.EndOfMap)endVoteStarted=true;ballots.Clear();rtv.Clear();directVotes.Clear();voteEnds=Environment.TickCount64+seconds*1000L;
        Core.PlayerManager.SendLplkChat($"[LPLK] Map-Abstimmung läuft {seconds}s. Menü: !mapchooser");foreach(var p in voters)ShowVote(p);return true;
    }
    private void ShowVote(IPlayer p)=>Selection(p,"LPLK · Nächste Map",BallotChoices(),CastBallot,true);
    private void CancelVote(){voteEnds=0;ballots.Clear();candidates.Clear();CloseMenus();}
    private void FinishVote()
    {
        voteEnds=0;CloseMenus();var online=Voters().Select(p=>p.SteamID).ToHashSet();var legal=Eligible().Select(m=>m.Id).ToHashSet();
        var leaders=MapPolicy.Leaders(candidates.Where(m=>legal.Contains(m.Id)).ToList(),ballots.Where(x=>online.Contains(x.Key)).Select(x=>x.Value),nominatedCandidates);
        int extensionVotes=ballots.Count(x=>online.Contains(x.Key)&&x.Value==ExtendChoice);
        int topMapVotes=leaders.Count==0?0:leaders.Max(m=>ballots.Count(x=>online.Contains(x.Key)&&x.Value==m.Id));
        bool extended=reason==Reason.EndOfMap&&extensionVotes>0&&extensionVotes>topMapVotes&&ExtendMap(true);
        ballots.Clear();candidates.Clear();rtv.Clear();if(extended)return;
        if(leaders.Count==0){endVoteStarted=false;nextVoteAllowed=Environment.TickCount64+Math.Max(5,settings.FailedVoteCooldownSeconds)*1000L;Core.PlayerManager.SendLplkChat("[LPLK] Abstimmung ohne gültiges Ergebnis: Mapkriterien haben sich geändert.");return;}
        nextMap=leaders[Random.Shared.Next(leaders.Count)];Core.PlayerManager.SendLplkChat("[LPLK] Nächste Map: "+nextMap.Name);
        if(reason==Reason.Rtv){if(settings.RtvAtRoundEnd&&!matchEnded)changeAtRoundEnd=true;else Schedule(nextMap,settings.ChangeDelaySeconds);}
        else if(reason==Reason.Admin&&!matchEnded){changeAtRoundEnd=true;Core.PlayerManager.SendLplkChat("[LPLK] Admin-Abstimmung: Wechsel am Rundenende.");}
        else if(matchEnded)Schedule(nextMap,settings.MatchEndDelaySeconds);
        else Core.PlayerManager.SendLplkChat("[LPLK] Die gewählte Map startet nach dem Match.");
    }
    private void Schedule(MapEntry map,int seconds,bool automatic=false,bool manualTest=false)
    {
        if(settings.BuiltinMapsOnly&&map.Workshop&&!manualTest)return;if(pending is not null)return;if(!Available(map)&&!(map.RemoteMapVerified&&map.IsMap&&!map.Removed&&!settings.ExcludedWorkshopIds.Contains(map.Id))){Core.Logger.LogWarning("LPLK_Maps: Zielmap nicht verfügbar: {Id}",map.Id);return;}
        CancelVote();idleTransition=false;pending=map;attempts=0;transitionIssued=false;transitionDue=Environment.TickCount64+seconds*1000L;
        lastTransition="Geplant: "+map.Id;Core.PlayerManager.SendLplkChat($"[LPLK] Wechsel zu {map.Name} in {seconds}s.");WriteStatus();
    }
    private void IssuePending()
    {
        if(CancelUnsafeIdleChange())return;
        if(pending is not {} map)return;if(map.Workshop&&!EnoughDisk(map)){transitionDue=Environment.TickCount64+5000;lastTransition=poolMessage;return;}attempts++;transitionIssued=true;transitionDue=Environment.TickCount64+(installed.Contains(map.Id)?settings.VerifySeconds:240)*1000L;
        // Tick runs on the game thread. Both supported APIs expose ExecuteCommand;
        // VerifyPending confirms success from the loaded map, not console output.
        if(map.Workshop&&settings.SetNextLevel&&map.SourceFolder.Length>0&&MapPolicy.SafeMapToken(map.SourceFolder))Core.Engine.ExecuteCommand("nextlevel "+map.SourceFolder);
        Core.Engine.ExecuteCommand(map.Workshop?"host_workshop_map "+map.Id:"changelevel "+map.Id);
        lastTransition=$"Angefordert: {map.Id}, Versuch {attempts}/{settings.MaxAttempts}";WriteStatus();
    }
    private void VerifyPending()
    {
        if(pending is not {} map)return;
        if(MapPolicy.Confirmed(map,CurrentMap,Workshop)){lastTransition="Bestätigt: "+map.Id;Core.Logger.LogInformation("LPLK_Maps: Mapwechsel bestätigt: {Id} / {Map}",map.Id,CurrentMap);pending=null;nextMap=null;idleTransition=false;WriteStatus();return;}
        if(CancelUnsafeIdleChange())return;
        if(probeId is not null&&AnyHuman()){transitionDue=Environment.TickCount64+5000;return;}
        if(attempts>=settings.MaxAttempts){lastTransition=$"Fehlgeschlagen: {map.Id}; tatsächlich {CurrentMap}/{Workshop}";Core.Logger.LogError("LPLK_Maps: {Error}",lastTransition);Core.PlayerManager.SendLplkChat("[LPLK] Mapwechsel fehlgeschlagen; siehe Serverprotokoll.");if(probeId==map.Id)ReplaceMap(map with{ProbeError="Mapwechsel nicht bestätigt",LastProbeAt=DateTimeOffset.UtcNow});pending=null;nextMap=null;idleTransition=false;WriteStatus();return;}
        IssuePending();
    }
    private void Selection(IPlayer player,string title,IReadOnlyList<MapEntry> entries,Action<IPlayer,MapEntry> action,bool vote=false,Action<IPlayer>? back=null,int page=0)
    {
        if(entries.Count==0){player.SendLplkChat("[LPLK] Keine passende Map verfügbar.");return;}Close(player.PlayerID);
        int pages=vote?1:(entries.Count+9)/10;page=Math.Clamp(page,0,pages-1);
        int serial=generation;ulong steamId=player.SteamID;Guid token=Guid.NewGuid();var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title+(pages>1?$" · {page+1}/{pages}":""))).Design.SetMaxVisibleItems(5)
            .Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
        var numbers=new MenuNumbers();
        int number=page*10;
        foreach(var map in vote?entries:entries.Skip(page*10).Take(10))
        {
            var b=new ButtonMenuOption(numbers.Next(vote?MapPolicy.VoteLabel(map,nominatedCandidates.Contains(map.Id)):MapPolicy.SelectionLabel(map),++number)) { CloseAfterClick=false };
            GameThreadMenu.Bind(Core,b,player,()=>module.Active,a=>{if(a.IsValid&&a.SteamID==steamId&&serial==generation&&menus.TryGetValue(a.PlayerID,out var m)&&m.Token==token)action(a,map);});builder.AddOption(b);
        }
        void Navigate(string label,int next){var option=new ButtonMenuOption(MenuColors.Back(label)){CloseAfterClick=false};GameThreadMenu.Bind(Core,option,player,()=>module.Active&&serial==generation,a=>Selection(a,title,entries,action,vote,back,next));builder.AddOption(option);}
        if(page>0)Navigate("Vorherige Seite",page-1);
        if(page+1<pages)Navigate("Weiter",page+1);
        if(back is not null){var b=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,b,player,()=>module.Active&&serial==generation,back);builder.AddOption(b);}
        var menu=builder.Build();MenuPresentation.Apply(menu);menus[player.PlayerID]=(player,menu,token);Core.MenusAPI.OpenMenuForPlayer(player,menu);
    }
    private void Close(int id){if(menus.Remove(id,out var m)&&m.Player.IsValid)Core.MenusAPI.CloseMenuForPlayer(m.Player,m.Menu);}
    private void CloseMenus(){foreach(int id in menus.Keys.ToArray())Close(id);}
    private void WriteStatus()
    {
        try{LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new {updatedAt=DateTimeOffset.UtcNow,version="0.4.17",reloadError,scan=new{running=scanTask is not null,error=scanError,milliseconds=scanMilliseconds,vpkReads=scanVpkReads},currentMap=CurrentMap,workshop=Workshop,idleRotationMinutes=settings.IdleRotationMinutes,rotateStartupMap=settings.RotateStartupMap,preparedStartupMap,startupRotationStatus,startupRotationPending=startupMapGate.Pending,catalog=maps.Count,available=maps.Count(Available),measured=maps.Count(MapPolicy.Measured),approved=maps.Count(MapPolicy.LimitsApproved),measurementOnly=settings.MeasurementOnly,limitReview=maps.OrderBy(m=>m.Name).Select(m=>new {m.Id,m.Name,m.InPool,m.Enabled,m.Removed,m.MinPlayers,m.MaxPlayers,m.ConfiguredMaxPlayers,m.TSpawns,m.CtSpawns,m.BombSites,m.Hostages,m.MeasuredAt,m.ProbeError,m.LimitProposal,approved=MapPolicy.LimitsApproved(m)}),activePool=maps.Count(m=>m.InPool&&m.Enabled&&!m.Removed),poolMessage,probeId,voteSeconds=settings.VoteSeconds,mapsToShow=settings.MapsToShow,transition=lastTransition});statusWriteFailed=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Maps: Statusdatei nicht schreibbar");statusWriteFailed=true;}
    }
    [Command("lplk_maps")]
    public void Status(ICommandContext ctx){if(ctx.Sender is {} p)OpenAdmin(p);else{RefreshSettings();if(ctx.Args is ["parityprobe"]){ParityProbe(ctx);return;}WriteStatus();ctx.ReplyLplk($"LPLK_Maps 0.4.17: catalog={maps.Count}; available={maps.Count(Available)}; vote={voteEnds>0}; duration={settings.VoteSeconds}; candidates={settings.MapsToShow}; reloadError={reloadError??"none"}; {lastTransition}");}}
    [Command("lplk_map")][CommandAlias("map")][CommandAlias("setmap")]
    public void ChangeCommand(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!MayManage(p,s=>s.ChangeMapPermission)){ctx.ReplyLplk("[LPLK] Keine Berechtigung.");return;}
        if(ctx.Args.Length!=1){ctx.ReplyLplk("lplk_map <Map-ID>");return;}Scan();var map=MapPolicy.Resolve(maps.Where(Available),ctx.Args[0]);
        if(map is null){ctx.ReplyLplk("[LPLK] Keine installierte, zulässige Map mit dieser ID.");return;}Schedule(map,settings.ChangeDelaySeconds);ctx.ReplyLplk("[LPLK] Mapwechsel angefordert: "+map.Id);
    }
}



