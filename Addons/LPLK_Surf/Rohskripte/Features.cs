using LPLK.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;

namespace LPLK_Surf;

public sealed partial class Plugin
{
    private readonly Dictionary<ulong, ReplayPlayback> _replayViewers = [];
    private readonly Dictionary<ulong, ulong> _spectatorHudTargets = [];
    private readonly Dictionary<ulong, long> _nextSpectatorHudMs = [];

    private static bool TryBonusTrigger(string name, string suffix, out int number)
    {
        Match match = Regex.Match(name, $@"^b(\d+)_{suffix}$", RegexOptions.IgnoreCase);
        return int.TryParse(match.Success ? match.Groups[1].Value : string.Empty, out number) && number > 0;
    }

    private bool HandleBonusStartTouch(IPlayer player, string name)
    {
        if (TryBonusTrigger(name, "start", out int bonus))
        {
            ResetRun(player.SteamID);
            RunState run = State(player.SteamID);
            run.Course = $"bonus{bonus}"; run.Armed = true; run.InStart = true; run.UsingMapTriggers = true;
            Reply(player, $"[gold]Bonus {bonus} bereit.");
            return true;
        }
        if (TryBonusTrigger(name, "end", out bonus))
        {
            RunState run = State(player.SteamID);
            if (run.Running && run.Course == $"bonus{bonus}") FinishRun(player, run);
            return true;
        }
        return false;
    }

    private bool HandleBonusEndTouch(IPlayer player, string name)
    {
        if (!TryBonusTrigger(name, "start", out int bonus)) return false;
        RunState run = State(player.SteamID);
        if (run.Course != $"bonus{bonus}") { ResetRun(player.SteamID); run = State(player.SteamID); run.Course = $"bonus{bonus}"; run.Armed = true; run.UsingMapTriggers = true; }
        run.InStart = false;
        if (run.Armed && !run.Running) StartRun(player, run);
        return true;
    }

    private void PrepareRun(IPlayer player, RunState run)
    {
        run.InvalidReason = null;
        run.Ranked = !Pref(player).Practice;
        run.ReplayFrames.Clear(); run.NextReplayCaptureMs = 0;
        LoadHudReferences(player, run);
    }

    private void EnsureHudReferences(IPlayer player, RunState run)
    {
        if (run.ReferencesLoaded) return;
        LoadHudReferences(player, run);
    }

    private void LoadHudReferences(IPlayer player, RunState run)
    {
        run.ReferencesLoaded = true;
        using SqliteConnection db = OpenDb();
        run.PbSeconds = ScalarDouble(db, "SELECT best_seconds FROM course_records WHERE map=$m AND course=$c AND steam_id=$s", ("$m", _map), ("$c", run.Course), ("$s", player.SteamID.ToString()));
        run.WrSeconds = ScalarDouble(db, "SELECT MIN(best_seconds) FROM course_records WHERE map=$m AND course=$c", ("$m", _map), ("$c", run.Course));
        using (SqliteCommand wr = db.CreateCommand()) { wr.CommandText = "SELECT player_name FROM course_records WHERE map=$m AND course=$c ORDER BY best_seconds LIMIT 1"; wr.Parameters.AddWithValue("$m", _map); wr.Parameters.AddWithValue("$c", run.Course); run.WrPlayerName = wr.ExecuteScalar() as string; }
        run.PbStageTimes = LoadStageReference(db, run.Course, "steam_id=$s", ("$s", player.SteamID.ToString()));
        run.WrStageTimes = LoadStageReference(db, run.Course, "best_seconds=(SELECT MIN(best_seconds) FROM course_records WHERE map=$m AND course=$c)");
    }

    private List<double> LoadStageReference(SqliteConnection db, string course, string where, params (string,object)[] extra)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT stage_times FROM course_records WHERE map=$m AND course=$c AND {where} LIMIT 1";
        cmd.Parameters.AddWithValue("$m", _map); cmd.Parameters.AddWithValue("$c", course);
        foreach ((string key, object value) in extra) cmd.Parameters.AddWithValue(key, value);
        string? json = cmd.ExecuteScalar() as string;
        try { return json is null ? [] : JsonSerializer.Deserialize<List<double>>(json, Json) ?? []; } catch { return []; }
    }

    private string LiveDelta(IPlayer player, RunState run)
    {
        if (!run.Running) return string.Empty;
        double elapsed = Elapsed(run);
        SplitComparisonMode mode = HudComparisonMode(Pref(player));
        List<string> comparisons = [];
        if (ShowsPersonalBest(mode) && run.PbSeconds is double pb)
            comparisons.Add(FormatLiveComparison("PB", elapsed - pb));
        if (ShowsWorldRecord(mode) && run.WrSeconds is double wr)
            comparisons.Add(FormatLiveComparison("WR", elapsed - wr));
        return string.Join(" ", comparisons);
    }

    private static string FormatLiveComparison(string reference, double delta)
    {
        string color = delta <= 0 ? "#66FF99" : "#FF7777";
        return $"<span class='fontSize-xs' color='{color}'>{reference} {Signed(delta)}</span>";
    }

    private static bool ShowsPersonalBest(SplitComparisonMode mode) => mode is SplitComparisonMode.Both or SplitComparisonMode.PersonalBest;
    private static bool ShowsWorldRecord(SplitComparisonMode mode) => mode is SplitComparisonMode.Both or SplitComparisonMode.WorldRecord;

    private static string FormatHud(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private void ReplyStageSplit(IPlayer player, RunState run, int stage, double split)
    {
        ReplySplit(player, run, $"Stage {stage}", split);
    }

    private void ReplyCheckpointSplit(IPlayer player, RunState run, int checkpoint, double split)
    {
        ReplySplit(player, run, $"Checkpoint {checkpoint}", split);
    }

    private void ReplySplit(IPlayer player, RunState run, string label, double split)
    {
        // StageTimes contains splits in the order in which the map's stage and
        // checkpoint triggers were touched. PB and WR records use the same
        // order, so both comparisons work for native and manually defined maps.
        int referenceIndex = run.StageTimes.Count - 1;
        List<string> comparisons = [];
        SplitComparisonMode mode = ChatComparisonMode(Pref(player));

        if (ShowsPersonalBest(mode) && referenceIndex >= 0 && run.PbStageTimes.Count > referenceIndex)
            comparisons.Add(FormatSplitComparison("PB", split - run.PbStageTimes[referenceIndex]));

        if (ShowsWorldRecord(mode) && referenceIndex >= 0 && run.WrStageTimes.Count > referenceIndex)
        {
            string leader = SplitLeaderName(run.WrPlayerName);
            comparisons.Add(FormatSplitComparison($"WR {leader}", split - run.WrStageTimes[referenceIndex]));
        }

        string suffix = comparisons.Count == 0 ? string.Empty : " " + string.Join(" ", comparisons);
        Reply(player, $"[green]{label}: [default]{Format(split)}{suffix}");
    }

    private static string FormatSplitComparison(string reference, double delta)
    {
        string color = delta <= 0 ? "[green]" : "[red]";
        string signedSeconds = delta.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        return $"{color}({reference} {signedSeconds} s)";
    }

    private static string SplitLeaderName(string? name)
    {
        string clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c) && c is not '[' and not ']').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean)) return "Leader";
        return clean.Length > 18 ? clean[..17] + "…" : clean;
    }

    private void RememberNativeStagePosition(IPlayer player, int stage)
    {
        if (stage <= 1 || _zones.NativeStageTeleports.ContainsKey(stage)) return;
        _zones.NativeStageTeleports[stage] = Position(player);
        SaveZones();
        Core.Logger.LogInformation("[LPLK_Surf] Sicherer Stage-Teleport gespeichert: Map={Map}, Stage={Stage}", _map, stage);
    }

    private RecordResult SaveCourseRecord(IPlayer player, string course, double seconds, List<double> stages)
    {
        if(!double.IsFinite(seconds)||seconds<=0||stages.Any(t=>!double.IsFinite(t)||t<0))throw new InvalidDataException("Ungültige Laufzeit.");
        using SqliteConnection db = OpenDb();
        double? old = ScalarDouble(db, "SELECT best_seconds FROM course_records WHERE map=$m AND course=$c AND steam_id=$s", ("$m",_map),("$c",course),("$s",player.SteamID.ToString()));
        bool pb = old is null || seconds < old;
        using var transaction=db.BeginTransaction();
        using (SqliteCommand cmd = db.CreateCommand())
        {
            cmd.Transaction=transaction;
            cmd.CommandText = "INSERT INTO course_runs(map,course,steam_id,player_name,seconds,finished_at) VALUES($m,$c,$s,$n,$t,$d)";
            cmd.Parameters.AddWithValue("$m",_map);cmd.Parameters.AddWithValue("$c",course);cmd.Parameters.AddWithValue("$s",player.SteamID.ToString());cmd.Parameters.AddWithValue("$n",player.Name);cmd.Parameters.AddWithValue("$t",seconds);cmd.Parameters.AddWithValue("$d",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
        }
        if (pb)
        {
            using SqliteCommand cmd=db.CreateCommand();cmd.Transaction=transaction;
            cmd.CommandText="INSERT INTO course_records(map,course,steam_id,player_name,best_seconds,finishes,stage_times,updated_at) VALUES($m,$c,$s,$n,$t,1,$g,$d) ON CONFLICT(map,course,steam_id) DO UPDATE SET player_name=$n,best_seconds=$t,finishes=course_records.finishes+1,stage_times=$g,updated_at=$d";
            cmd.Parameters.AddWithValue("$m",_map);cmd.Parameters.AddWithValue("$c",course);cmd.Parameters.AddWithValue("$s",player.SteamID.ToString());cmd.Parameters.AddWithValue("$n",player.Name);cmd.Parameters.AddWithValue("$t",seconds);cmd.Parameters.AddWithValue("$g",JsonSerializer.Serialize(stages));cmd.Parameters.AddWithValue("$d",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery();
        }
        else { using SqliteCommand cmd=db.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="UPDATE course_records SET finishes=finishes+1,player_name=$n WHERE map=$m AND course=$c AND steam_id=$s";cmd.Parameters.AddWithValue("$n",player.Name);cmd.Parameters.AddWithValue("$m",_map);cmd.Parameters.AddWithValue("$c",course);cmd.Parameters.AddWithValue("$s",player.SteamID.ToString());cmd.ExecuteNonQuery(); }
        transaction.Commit();
        int rank=Convert.ToInt32(ScalarLong(db,"SELECT COUNT(*)+1 FROM course_records WHERE map=$m AND course=$c AND best_seconds<$t",("$m",_map),("$c",course),("$t",seconds)));
        int total=Convert.ToInt32(ScalarLong(db,"SELECT COUNT(*) FROM course_records WHERE map=$m AND course=$c",("$m",_map),("$c",course)));
        return new(old,pb,rank==1,rank,total);
    }

    private void PracticeCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;PlayerPrefs prefs=Pref(player);prefs.Practice=!prefs.Practice;ResetRun(player.SteamID);SavePrefs();Reply(player,prefs.Practice?"[gold]Practice AN – Zeiten werden nicht gespeichert.":"[green]Practice AUS – Ranked-Timer aktiv.");
    }
    private void CheckpointCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;
        if(_config.Server.KzCheckpointsEnabled&&CurrentMapCategory()==MapCategory.Kz)
        {
            string argument=Args(context);if(argument.StartsWith("delete ",StringComparison.OrdinalIgnoreCase)){if(TryCheckpointSlot(argument[7..],out int deleteSlot))DeleteKzCheckpoint(player,deleteSlot);else Reply(player,$"[red]Slot muss 1-{KzCheckpointSlotCount} sein.");return;}
            if(!string.IsNullOrWhiteSpace(argument)&&!TryCheckpointSlot(argument,out _)){Reply(player,$"[red]Slot muss 1-{KzCheckpointSlotCount} sein.");return;}
            int slot=TryCheckpointSlot(argument,out int requested)?requested:ActiveCheckpointSlot(Pref(player));SaveKzCheckpoint(player,slot);return;
        }
        if(!Pref(player).Practice){Reply(player,"[red]Aktiviere zuerst !practice.");return;}Pref(player).Checkpoint=Position(player);SavePrefs();Reply(player,"[green]Checkpoint gespeichert.");
    }
    private void CheckpointTeleportCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;
        if(_config.Server.KzCheckpointsEnabled&&CurrentMapCategory()==MapCategory.Kz){string argument=Args(context);if(!string.IsNullOrWhiteSpace(argument)&&!TryCheckpointSlot(argument,out _)){Reply(player,$"[red]Slot muss 1-{KzCheckpointSlotCount} sein.");return;}int slot=TryCheckpointSlot(argument,out int requested)?requested:ActiveCheckpointSlot(Pref(player));TeleportToKzCheckpoint(player,slot);return;}
        PlayerPrefs prefs=Pref(player);if(!prefs.Practice||prefs.Checkpoint is null){Reply(player,"[red]Kein Practice-Checkpoint vorhanden.");return;}ResetRun(player.SteamID);Teleport(player,prefs.Checkpoint);Reply(player,"[gray]Zum Checkpoint teleportiert.");
    }

    private void CheckpointsMenuCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;if(!_config.Server.KzCheckpointsEnabled||CurrentMapCategory()!=MapCategory.Kz){Reply(player,"[red]KZ-Checkpoint-Slots sind nur auf KZ-Maps verfügbar.");return;}OpenTrackedMenu(player,BuildKzCheckpointMenu(player));
    }

    private int KzCheckpointSlotCount=>Math.Clamp(_config.Server.KzCheckpointSlots,1,20);
    private int ActiveCheckpointSlot(PlayerPrefs prefs)=>Math.Clamp(prefs.ActiveCheckpointSlot,1,KzCheckpointSlotCount);
    private bool TryCheckpointSlot(string value,out int slot)=>int.TryParse(value.Trim(),out slot)&&slot>=1&&slot<=KzCheckpointSlotCount;
    private Dictionary<int,SavedCheckpoint> KzCheckpoints(PlayerPrefs prefs)
    {
        prefs.KzCheckpoints??=[];if(!prefs.KzCheckpoints.TryGetValue(_map,out Dictionary<int,SavedCheckpoint>? points))prefs.KzCheckpoints[_map]=points=[];return points;
    }
    private static bool IsOnSolidGround(IPlayer player){try{return player.PlayerPawn is not null&&player.PlayerPawn.GroundEntity.IsValid;}catch{return false;}}
    private void SaveKzCheckpoint(IPlayer player,int slot)
    {
        if(CurrentMapCategory()!=MapCategory.Kz){Reply(player,"[red]KZ-Checkpoints sind nur auf KZ-Maps verfügbar.");return;}
        if(!IsOnSolidGround(player)){Reply(player,"[red]Checkpoint nicht gespeichert: Stelle dich auf festen Boden.");return;}
        if(player.PlayerPawn?.AbsOrigin is not Vector origin){Reply(player,"[red]Spielerposition nicht verfügbar.");return;}
        QAngle angles=player.PlayerPawn.EyeAngles;PlayerPrefs prefs=Pref(player);prefs.Practice=true;prefs.ActiveCheckpointSlot=slot;
        KzCheckpoints(prefs)[slot]=new(new(origin.X,origin.Y,origin.Z),new(angles.X,angles.Y,angles.Z));ResetRun(player.SteamID);SavePrefs();Reply(player,$"[green]KZ-Checkpoint {slot} auf festem Boden gespeichert – Practice aktiv.");
    }
    private void TeleportToKzCheckpoint(IPlayer player,int slot)
    {
        PlayerPrefs prefs=Pref(player);if(!KzCheckpoints(prefs).TryGetValue(slot,out SavedCheckpoint? point)){Reply(player,$"[red]KZ-Checkpoint {slot} ist auf dieser Map leer.");return;}
        prefs.Practice=true;prefs.ActiveCheckpointSlot=slot;ResetRun(player.SteamID);SavePrefs();player.Teleport(new Vector(point.Position.X,point.Position.Y,point.Position.Z),new QAngle(point.Angles.X,point.Angles.Y,point.Angles.Z),new Vector(0,0,0));Reply(player,$"[gray]Zu KZ-Checkpoint {slot} teleportiert – Practice aktiv.");
    }
    private void DeleteKzCheckpoint(IPlayer player,int slot)
    {
        PlayerPrefs prefs=Pref(player);bool removed=KzCheckpoints(prefs).Remove(slot);SavePrefs();Reply(player,removed?$"[green]KZ-Checkpoint {slot} gelöscht.":$"[gray]KZ-Checkpoint {slot} war bereits leer.");
    }

    private void BonusCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;string arg=Args(context);string course=int.TryParse(arg,out int bonus)&&bonus>0?$"bonus{bonus}":"bonus1";OpenTrackedMenu(player,BuildCourseTopMenu(course));
    }
    private IMenuAPI BuildCourseTopMenu(string course)
    {
        IMenuBuilderAPI builder=MenuBuilder($"Rangliste {course}");using SqliteConnection db=OpenDb();using SqliteCommand cmd=db.CreateCommand();cmd.CommandText="SELECT player_name,best_seconds FROM course_records WHERE map=$m AND course=$c ORDER BY best_seconds LIMIT $n";cmd.Parameters.AddWithValue("$m",_map);cmd.Parameters.AddWithValue("$c",course);cmd.Parameters.AddWithValue("$n",_config.Rankings.TopCount);using SqliteDataReader reader=cmd.ExecuteReader();int i=0;while(reader.Read())builder.AddOption(MenuText($"#{++i} {reader.GetString(0)} – {Format(reader.GetDouble(1))}"));if(i==0)builder.AddOption(MenuText("Noch keine Bonuszeiten."));return Tagged(builder.Build());
    }

    private void ProfileCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;OpenTrackedMenu(player,BuildProfileMenu(player));
    }
    private IMenuAPI BuildProfileMenu(IPlayer player)
    {
        using SqliteConnection db=OpenDb();long finishes=ScalarLong(db,"SELECT COUNT(*) FROM course_runs WHERE steam_id=$s",("$s",player.SteamID.ToString()));long maps=ScalarLong(db,"SELECT COUNT(DISTINCT map||':'||course) FROM course_records WHERE steam_id=$s",("$s",player.SteamID.ToString()));long wrs=ScalarLong(db,"SELECT COUNT(*) FROM course_records r WHERE steam_id=$s AND best_seconds=(SELECT MIN(x.best_seconds) FROM course_records x WHERE x.map=r.map AND x.course=r.course)",("$s",player.SteamID.ToString()));double points=CalculatePoints(db,player.SteamID);IMenuBuilderAPI builder=MenuBuilder("Spielerprofil");builder.AddOption(MenuText(player.Name));builder.AddOption(MenuText($"Punkte: {points:0} | WRs: {wrs}"));builder.AddOption(MenuText($"Abschlüsse: {finishes} | Kurse: {maps}"));builder.AddOption(MenuText($"Map-Tier: {_zones.Tier}"));return Tagged(builder.Build());
    }
    private double CalculatePoints(SqliteConnection db,ulong steamId)
    {
        List<(string Map,string Course,double Time)> records=[];using(SqliteCommand cmd=db.CreateCommand()){cmd.CommandText="SELECT map,course,best_seconds FROM course_records WHERE steam_id=$s";cmd.Parameters.AddWithValue("$s",steamId.ToString());using SqliteDataReader reader=cmd.ExecuteReader();while(reader.Read())records.Add((reader.GetString(0),reader.GetString(1),reader.GetDouble(2)));}double points=0;foreach((string map,string course,double time) in records){int rank=Convert.ToInt32(ScalarLong(db,"SELECT COUNT(*)+1 FROM course_records WHERE map=$m AND course=$c AND best_seconds<$t",("$m",map),("$c",course),("$t",time)));points+=Math.Max(10,TierForMap(map)*100.0/Math.Sqrt(rank));}return points;
    }
    private int TierForMap(string map){if(map.Equals(_map,StringComparison.OrdinalIgnoreCase))return _zones.Tier;string path=Path.Combine(Core.PluginDataDirectory,"zones",Safe(map)+".json");try{return Math.Clamp((JsonSerializer.Deserialize<MapZones>(File.ReadAllText(path),Json)??new()).Tier,1,8);}catch{return 1;}}

    private void CaptureReplayFrame(IPlayer player,RunState run)
    {
        if(!run.Running||!run.Ranked)return;long now=Environment.TickCount64;if(now<run.NextReplayCaptureMs)return;run.NextReplayCaptureMs=now+50;Vector? position=player.PlayerPawn?.AbsOrigin;if(position is null)return;QAngle? angle=player.PlayerPawn?.AbsRotation;run.ReplayFrames.Add(new ReplayFrame(Elapsed(run),new Vec(position.Value.X,position.Value.Y,position.Value.Z),angle is null?new Vec(0,0,0):new Vec(angle.Value.X,angle.Value.Y,angle.Value.Z)));
    }
    private string ReplayPath(string course)=>Path.Combine(Core.PluginDataDirectory,"replays",Safe(_map)+"_"+Safe(course)+"_wr.json");
    private void SaveWorldRecordReplay(IPlayer player,RunState run,double seconds)
    {
        Directory.CreateDirectory(Path.Combine(Core.PluginDataDirectory,"replays"));ReplayFile replay=new(_map,run.Course,player.SteamID,player.Name,seconds,run.ReplayFrames);AtomicJson.Write(ReplayPath(run.Course),replay);
    }
    private void ReplayCommand(ICommandContext context)
    {
        IPlayer? player=Player(context);if(player is null)return;if(_replayViewers.Remove(player.SteamID)){Reply(player,"[gray]Replay beendet.");return;}string course=string.IsNullOrWhiteSpace(Args(context))?"main":Args(context).ToLowerInvariant();StartReplayForPlayer(player,course);
    }
    private void StartReplayForPlayer(IPlayer player,string course)
    {
        string path=ReplayPath(course);if(!File.Exists(path)){Reply(player,"[red]Für diesen Kurs existiert kein WR-Replay.");return;}try{ReplayFile? replay=JsonSerializer.Deserialize<ReplayFile>(File.ReadAllText(path),Json);if(replay is null||replay.Frames.Count==0)throw new InvalidDataException();Pref(player).Practice=true;SavePrefs();ResetRun(player.SteamID);_replayViewers[player.SteamID]=new(replay,Stopwatch.GetTimestamp());Reply(player,$"[gold]Replay: {replay.PlayerName} – {Format(replay.Seconds)}. !replay beendet.");}catch(Exception ex){Core.Logger.LogWarning(ex,"[LPLK_Surf] Replay konnte nicht geladen werden: {Path}",path);Reply(player,"[red]Replaydatei ist ungültig.");}
    }
    private List<int> DetectedBonuses()=>_detectedTriggerNames.Select(name=>TryBonusTrigger(name,"start",out int number)?number:0).Where(number=>number>0).Distinct().OrderBy(number=>number).ToList();
    private IMenuAPI BuildReplayMenu(IPlayer player)
    {
        IMenuBuilderAPI builder=MenuBuilder("WR-Replay");ButtonMenuOption main=MenuButton("Hauptstrecke abspielen");main.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{CloseSurfMenu(a.Player);StartReplayForPlayer(a.Player,"main");});builder.AddOption(main);foreach(int bonus in DetectedBonuses()){int selected=bonus;ButtonMenuOption button=MenuButton($"Bonus {selected} abspielen");button.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{CloseSurfMenu(a.Player);StartReplayForPlayer(a.Player,$"bonus{selected}");});builder.AddOption(button);}if(_replayViewers.ContainsKey(player.SteamID)){ButtonMenuOption stop=MenuButton("Laufendes Replay beenden");stop.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{_replayViewers.Remove(a.Player.SteamID);CloseSurfMenu(a.Player);});builder.AddOption(stop);}return Tagged(builder.Build());
    }
    private bool ProcessReplayViewer(IPlayer player)
    {
        if(!_replayViewers.TryGetValue(player.SteamID,out ReplayPlayback? playback))return false;double elapsed=(Stopwatch.GetTimestamp()-playback.StartTimestamp)/(double)Stopwatch.Frequency;while(playback.Index+1<playback.Replay.Frames.Count&&playback.Replay.Frames[playback.Index+1].Time<=elapsed)playback.Index++;if(playback.Index>=playback.Replay.Frames.Count-1&&elapsed>playback.Replay.Seconds+.25){_replayViewers.Remove(player.SteamID);Reply(player,"[green]Replay beendet.");return false;}ReplayFrame frame=playback.Replay.Frames[playback.Index];player.Teleport(new Vector(frame.Position.X,frame.Position.Y,frame.Position.Z),new QAngle(frame.Angle.X,frame.Angle.Y,frame.Angle.Z),new Vector(0,0,0));long now=Environment.TickCount64;if(now>=playback.NextHudMs){playback.NextHudMs=now+25;player.SendCenterHTML($"<font color='#F5C542'>WR REPLAY</font> {playback.Replay.PlayerName}<br>{Format(Math.Min(elapsed,playback.Replay.Seconds))} / {Format(playback.Replay.Seconds)}",300);}return true;
    }

    private void SpectatorHudCommand(ICommandContext context)
    {
        IPlayer? spectator=Player(context);if(spectator is null)return;string query=Args(context);if(string.IsNullOrWhiteSpace(query)||query.Equals("off",StringComparison.OrdinalIgnoreCase)){_spectatorHudTargets.Remove(spectator.SteamID);Reply(spectator,"[gray]Zuschauer-HUD aus.");return;}IPlayer? target=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>!x.IsFakeClient&&x.Name.Contains(query,StringComparison.OrdinalIgnoreCase));if(target is null){Reply(spectator,"[red]Spieler nicht gefunden.");return;}_spectatorHudTargets[spectator.SteamID]=target.SteamID;Reply(spectator,$"[green]Zuschauer-HUD folgt {target.Name}.");
    }
    private IMenuAPI BuildSpectatorMenu(IPlayer player)
    {
        IMenuBuilderAPI builder=MenuBuilder("Spieler-HUD");if(_spectatorHudTargets.ContainsKey(player.SteamID)){ButtonMenuOption off=MenuButton("Verfolgung beenden");off.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{_spectatorHudTargets.Remove(a.Player.SteamID);a.Player.SendCenterHTML(string.Empty,1);OpenTrackedMenu(a.Player,BuildSettingsMenu(a.Player));});builder.AddOption(off);}foreach(IPlayer target in Core.PlayerManager.GetAllValidPlayers().Where(x=>!x.IsFakeClient&&x.SteamID!=player.SteamID)){IPlayer selected=target;ButtonMenuOption button=MenuButton(selected.Name);button.Click+=(sender,a)=>QueueMenuAction(sender,a.Player, () =>{_spectatorHudTargets[a.Player.SteamID]=selected.SteamID;CloseSurfMenu(a.Player);Reply(a.Player,$"[green]HUD folgt {selected.Name}.");});builder.AddOption(button);}if(Core.PlayerManager.GetAllValidPlayers().Count(x=>!x.IsFakeClient&&x.SteamID!=player.SteamID)==0)builder.AddOption(MenuText("Keine anderen Spieler online."));return Tagged(builder.Build());
    }
    private void ProcessSpectatorHuds()
    {
        long now=Environment.TickCount64;foreach(IPlayer spectator in Core.PlayerManager.GetAllValidPlayers())if(_spectatorHudTargets.TryGetValue(spectator.SteamID,out ulong targetId)){if(now<_nextSpectatorHudMs.GetValueOrDefault(spectator.SteamID))continue;_nextSpectatorHudMs[spectator.SteamID]=now+25;IPlayer? target=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>x.SteamID==targetId);if(target is null||!Usable(target)){_spectatorHudTargets.Remove(spectator.SteamID);continue;}RunState run=State(targetId);Vector velocity=target.PlayerPawn!.AbsVelocity;double speed=Math.Sqrt(velocity.X*velocity.X+velocity.Y*velocity.Y);spectator.SendCenterHTML($"<font color='#F5C542'>{target.Name}</font><br>{(run.Running?Format(Elapsed(run)):"--:--.---")} | {ToMetersPerMinute(speed):0.0} | {run.Course}",300);}
    }
}

public sealed record ReplayFrame(double Time,Vec Position,Vec Angle);
public sealed record ReplayFile(string Map,string Course,ulong SteamId,string PlayerName,double Seconds,List<ReplayFrame> Frames);
public sealed class ReplayPlayback(ReplayFile replay,long startTimestamp){public ReplayFile Replay{get;}=replay;public long StartTimestamp{get;}=startTimestamp;public int Index{get;set;}public long NextHudMs{get;set;}}
