using System.Diagnostics;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Teams;

[PluginMetadata(Id="LPLK_Teams",Name="LPLK_Teams",Version="0.1.0",Author="LPLK; reference Kampfhamster",Description="Teamwechselregeln und Gewinnerteam-Schutz",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private IInterfaceManager? interfaces;
    private TeamSettings settings=new();
    private Dictionary<ulong,ulong> selected=[]; // Target -> selecting administrator. Isolated test data.
    private readonly ScoreTracker tracker=new();
    private readonly MatchWindows windows=new();
    private readonly Dictionary<int,long> sequences=[];
    private readonly Dictionary<int,TeamIntent> intents=[];
    private readonly TeamPermits permits=new();
    private int epoch,phase=-1,mask,rounds=20,blocked,corrected,errors;
    private long revision;
    private bool loaded,ready,endApplied;
    private Score? score;
    private double nextTick,nextStatus;
    private DateTime stamp;
    private string? reloadError;
    private string roundsSource="fallback";
    private static double Now=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    private string SelectionPath=>Path.Combine(Core.PluginDataDirectory,"selected-players.json");
    private static ConnectionKey Key(IPlayer p)=>new(p.PlayerID,p.SteamID,p.SessionId);
    private long Sequence(int slot)=>sequences.GetValueOrDefault(slot);
    private void Invalidate(int slot){sequences[slot]=Sequence(slot)+1;permits.Remove(slot);intents.Remove(slot);}
    private bool IsHuman(IPlayer? p)=>p is {IsValid:true,IsFakeClient:false,IsAuthorized:true}&&p.Controller.IsValid;
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_Teams","Teamwechsel / Gewinnerteam","lplk.teams.admin",OpenAdmin,p=>Core.Permission.PlayerHasPermission(p.SteamID,"wl.admin"));
        settings=module.ReadSettings<TeamSettings>(TeamSettings.Validate);
        stamp=File.GetLastWriteTimeUtc(module.SettingsPath);
        if(File.Exists(SelectionPath))
        {
            selected=JsonSerializer.Deserialize<Dictionary<ulong,ulong>>(File.ReadAllText(SelectionPath))??throw new InvalidDataException("Auswahldaten fehlen.");
            if(selected.Count>256||selected.Any(x=>!AdminActionPolicy.SteamId(x.Key.ToString())||!AdminActionPolicy.SteamId(x.Value.ToString())))throw new InvalidDataException("Ungültige Auswahldaten.");
        }
        module.Active=loaded=true;Reset(false);
        Core.Command.HookClientCommand(ClientCommand);
        Core.GameEvent.HookPost<EventPlayerTeam>(PlayerTeam);
        Core.GameEvent.HookPost<EventRoundStart>(_=>{Refresh();tracker.RoundStart(ReadLive());return HookResult.Continue;});
        Core.GameEvent.HookPost<EventRoundEnd>(RoundEnd);
        Core.GameEvent.HookPost<EventBeginNewMatch>(_=>{Reset(true);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventGamePhaseChanged>(e=>{PhaseChanged(e.NewPhase);return HookResult.Continue;});
        Core.GameEvent.HookPost<EventCsWinPanelMatch>(_=>{Refresh();if(phase==(int)GamePhase.GAMEPHASE_MATCH_ENDED)EndMatch();return HookResult.Continue;});
        WriteStatus();
        Core.Logger.LogInformation("LPLK_Teams 0.1.0 geladen; enabled={Enabled}; Auswahl={Count}",settings.Enabled,selected.Count);
    }
    public override void UseSharedInterface(IInterfaceManager manager)=>interfaces=manager;
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Teams.Control.v1",new()
        {
            ["ready"]=(Func<bool>)(()=>loaded&&module.Active),
            // Trusted server plugins only; there is no client command that issues exemptions.
            ["permit"]=(Action<IPlayer,int>)Permit
        });
    }
    public override void Unload(){loaded=false;if(module is not null)module.Active=false;epoch++;permits.Clear();sequences.Clear();intents.Clear();}
    [EventListener<EventDelegates.OnClientDisconnected>]
    public void Disconnect(IOnClientDisconnectedEvent e)=>Invalidate(e.PlayerId);
    [EventListener<EventDelegates.OnMapUnload>]
    public void MapUnload(IOnMapUnloadEvent e){Reset(false);ready=false;}
    [EventListener<EventDelegates.OnMapLoad>]
    public void MapLoad(IOnMapLoadEvent e)=>Reset(false);
    private void Reset(bool complete)
    {
        epoch++;revision++;tracker.Reset(complete);windows.Reset();permits.Clear();sequences.Clear();intents.Clear();
        score=null;mask=0;phase=-1;ready=false;endApplied=false;
    }
    private Score? ReadLive()
    {
        try{var m=Core.Game.MatchData;var s=new Score(m.CTScoreTotal,m.TerroristScoreTotal);return s.Valid?s:null;}
        catch{return null;}
    }
    private void PhaseChanged(int next)
    {
        if(next==phase)return;
        if(next==(int)GamePhase.GAMEPHASE_WARMUP_ROUND){Reset(false);phase=next;return;}
        if(phase==(int)GamePhase.GAMEPHASE_HALFTIME)windows.Half(Now,settings.HalftimeSwitchGraceSeconds);
        phase=next;
        if(next==(int)GamePhase.GAMEPHASE_HALFTIME)
        {
            revision++;permits.Clear();intents.Clear(); // Cancel moves queued before the engine starts switching sides.
        }
    }
    private void Refresh()
    {
        var rules=Core.EntitySystem.GetGameRules();
        if(rules is null){ready=false;mask=0;return;}
        try{PhaseChanged((int)Core.Game.MatchData.Phase);}catch{ready=false;return;}
        int? configured=Core.ConVar.Find<int>("mp_maxrounds")?.Value;
        rounds=TeamPolicy.MaxRounds(settings,configured);
        roundsSource=settings.AutoDetectMaxRounds&&configured is >=2 and <=1000?"mp_maxrounds":"FallbackMaxRounds";
        score=tracker.Read(ReadLive(),settings.UseRoundEndFallback);
        ready=score is not null&&!rules.WarmupPeriod&&phase!=(int)GamePhase.GAMEPHASE_WARMUP_ROUND;
        int previous=mask;mask=score is {} s?TeamPolicy.Protected(settings,s,rounds):0;
        if(settings.Enabled&&ready&&!Halftime&&mask!=0&&mask!=previous&&settings.AnnounceWhenTeamBecomesProtected)
            Core.PlayerManager.SendChat($"[LPLK] Wechsel in {ProtectedText()} ab {TeamPolicy.Threshold(settings,rounds)} Punkten gesperrt.");
        if(phase==(int)GamePhase.GAMEPHASE_MATCH_ENDED)EndMatch();
    }
    private bool Halftime=>phase==(int)GamePhase.GAMEPHASE_HALFTIME || settings.AllowHalftimeTeamSwitchException&&windows.HalfActive(Now);
    private bool Effective=>loaded&&module.Active&&settings.Enabled&&ready;
    private int GuardWinner=>settings.BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd?windows.Winner(Now):0;
    private bool Selected(ulong target)=>selected.TryGetValue(target,out var actor)&&CanAdminId(actor)&&CanTarget(actor,target);
    private bool CanAdminId(ulong id)=>new[]{"lplk.admin","lplk.teams.admin","wl.admin"}.Any(p=>Core.Permission.PlayerHasPermission(id,p));
    private bool CanTarget(ulong actor,ulong target)=>AdminActionPolicy.CanTarget(actor,target,AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(actor,p)),AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(target,p)));
    private HookResult RoundEnd(EventRoundEnd e)
    {
        Refresh();
        if(!ready||Halftime)return HookResult.Continue;
        tracker.RoundEnd(ReadLive(),e.Winner);Refresh();
        bool overtime=Core.Game.MatchData.NOvertimePlaying>0;
        bool clinch=Core.ConVar.Find<bool>("mp_match_can_clinch")?.Value==true;
        if(score is {} s&&TeamPolicy.ScoreEndsMatch(settings,s,rounds,overtime,clinch))EndMatch();
        return HookResult.Continue;
    }
    private void EndMatch()
    {
        if(!Effective||Halftime||score is not {} s||s.Leader==0)return;
        windows.End(Now,settings.MatchEndGuardWindowSeconds,s.Leader);
        if(endApplied)return;endApplied=true;
        if(!settings.BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd)return;
        foreach(var p in Core.PlayerManager.GetAllValidPlayers().ToArray())
            if(IsHuman(p)&&p.Controller.TeamNum==GuardWinner&&Selected(p.SteamID))QueueMove(p,GuardWinner==3?2:3,true);
    }
    private HookResult ClientCommand(int slot,string line)
    {
        int? team=TeamPolicy.ParseRequest(line);
        if(team is null)return HookResult.Continue;
        var p=Core.PlayerManager.GetPlayer(slot);if(!IsHuman(p))return HookResult.Continue;
        Refresh();
        if(!Effective || phase==(int)GamePhase.GAMEPHASE_HALFTIME)return HookResult.Continue;
        // An event alone does not identify its initiator. Correct only a matching recent client request.
        // Admin/engine/mode changes without such an intent remain authoritative.
        intents[slot]=new(Key(p!),p!.Controller.TeamNum,team.Value,Now+1);
        if(!TeamPolicy.Block(settings,p!.Controller.TeamNum,team.Value,mask,Halftime,GuardWinner,Selected(p.SteamID)))return HookResult.Continue;
        blocked++;Message(p);return HookResult.Stop;
    }
    private void Permit(IPlayer p,int target)
    {
        if(!loaded||!p.IsValid||target is <1 or >3)throw new InvalidOperationException("Teamfreigabe nicht möglich.");
        Invalidate(p.PlayerID);
        permits.Grant(Key(p),target,Now);
    }
    private bool ConsumePermit(IPlayer p,int team)
    {
        return permits.Consume(Key(p),team,Now);
    }
    private HookResult PlayerTeam(EventPlayerTeam e)
    {
        var p=e.UserIdPlayer;if(!IsHuman(p)||e.Disconnect)return HookResult.Continue;
        sequences[p!.PlayerID]=Sequence(p.PlayerID)+1;
        if(ConsumePermit(p,e.Team))return HookResult.Continue;
        bool requested=intents.Remove(p.PlayerID,out var intent)&&intent.Matches(Key(p),e.OldTeam,e.Team,Now);
        Refresh();
        // Engine halftime, warmup and authorized server-side moves never trigger a rollback.
        if(!requested||!Effective||phase==(int)GamePhase.GAMEPHASE_HALFTIME||e.Team==e.OldTeam||e.Team is not (2 or 3))return HookResult.Continue;
        if(GuardWinner==e.Team&&Selected(p.SteamID)){QueueMove(p,e.Team==3?2:3,true);return HookResult.Continue;}
        if(settings.ForceBackIfSwitchStillHappened&&!Halftime&&TeamPolicy.IsProtected(mask,e.Team))
            QueueMove(p,TeamPolicy.FallbackTeam(e.OldTeam,mask),false);
        return HookResult.Continue;
    }
    private void QueueMove(IPlayer player,int target,bool forced)
    {
        var move=new PendingMove(Key(player),player.Controller.TeamNum,target,epoch,revision,Sequence(player.PlayerID));
        Core.Scheduler.NextTick(()=>
        {
            if(!loaded)return;
            try
            {
                var p=Core.PlayerManager.GetPlayer(move.Connection.Slot);
                if(!IsHuman(p))return;
                Refresh();
                if(!MovePolicy.StillCurrent(move,Key(p!),p!.Controller.TeamNum,epoch,revision,Sequence(p.PlayerID),Effective)||Halftime)return;
                if(forced ? GuardWinner!=p.Controller.TeamNum||!Selected(p.SteamID) : !settings.ForceBackIfSwitchStillHappened||!TeamPolicy.IsProtected(mask,p.Controller.TeamNum))return;
                Permit(p,target);
                p.ChangeTeam((Team)target);corrected++;
                if(forced){if(settings.ShowForceToLosingTeamMessage)p.SendCenter("MATCHENDE: Für dich ist das Verliererteam ausgewählt.");}
                else Message(p);
            }
            catch(Exception ex){errors++;Core.Logger.LogError(ex,"LPLK_Teams: Teamkorrektur fehlgeschlagen.");}
        });
    }
    private void Message(IPlayer p)
    {
        if(settings.ShowBlockedMessageInCenter)p.SendCenter("TEAMWECHSEL GESPERRT: Bitte ein anderes Team wählen.");
        if(settings.ShowBlockedMessageInChat)p.SendChat("[LPLK] Dieser Teamwechsel ist nach den aktuellen Teamregeln gesperrt.");
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!loaded||Now<nextTick)return;nextTick=Now+.25;
        try
        {
            if(Now>=nextStatus)
            {
                try
                {
                    var changed=File.GetLastWriteTimeUtc(module.SettingsPath);
                    if(changed!=stamp)
                    {
                        var next=JsonSerializer.Deserialize<TeamSettings>(File.ReadAllText(module.SettingsPath))??throw new InvalidDataException();
                        TeamSettings.Validate(next);settings=next;stamp=changed;revision++;reloadError=null;
                    }
                }
                catch(Exception){reloadError="Ungültige Änderung; letzter gültiger Stand bleibt aktiv.";}
            }
            Refresh();
            if(Now>=nextStatus){nextStatus=Now+2;WriteStatus();}
        }
        catch(Exception ex){ready=false;if(errors++==0)Core.Logger.LogError(ex,"LPLK_Teams: Status unbekannt; keine Teamkorrektur.");}
    }
    private bool BridgeReady()=>interfaces?.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",out var api)==true&&api.TryGetValue("teamsReady",out var call)&&call is Func<bool> probe&&probe();
    private string ProtectedText()=>mask==((1<<2)|(1<<3))?"T und CT":TeamPolicy.IsProtected(mask,3)?"CT":TeamPolicy.IsProtected(mask,2)?"T":"kein Team";
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.1.0",settings,ready,effective=Effective,score,scoreSource=tracker.Source,maxRounds=rounds,roundsSource,threshold=TeamPolicy.Threshold(settings,rounds),protectedTeams=ProtectedText(),halftime=Halftime,matchEndWinner=GuardWinner,selectedCount=selected.Count,bridgeReady=BridgeReady(),blocked,corrected,errors,reloadError});
}
