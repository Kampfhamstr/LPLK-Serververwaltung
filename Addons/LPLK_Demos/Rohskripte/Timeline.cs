using LPLK.Common;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.GameEventDefinitions;
namespace LPLK_Demos;
public sealed record DemoActor(string Id,string Name,bool Bot,int Team);
public sealed record DemoKill(double Seconds,int Tick,int Round,DemoActor? Killer,DemoActor? Victim,string Weapon,bool Headshot);
public sealed class DemoRound
{
 public int Number {get;set;}public double StartSeconds {get;set;}public double? EndSeconds {get;set;}
 public int StartTick {get;set;}public int? EndTick {get;set;}public int? Winner {get;set;}
 public bool Partial {get;set;}public List<DemoParticipant> Players {get;set;}=[];
 public DemoRound Copy()=>new(){Number=Number,StartSeconds=StartSeconds,EndSeconds=EndSeconds,StartTick=StartTick,EndTick=EndTick,Winner=Winner,Partial=Partial,Players=Players.Select(p=>p.Copy()).ToList()};
}
public sealed partial class Plugin
{
 private readonly Dictionary<string,DemoParticipant> participantLookup=[];
 private readonly Dictionary<string,DemoParticipant> roundBaseline=[];
 private long botTestUntil;private int? oldBotQuota;private bool? oldBotJoin;
 private long nextIndexFlush,lastRoundStart;private int roundNumber;
 private bool BotTest=>botTestUntil>0;
 private bool MapRecording=>settings.FullMapRecording||BotTest;
 private int EngineTick(){try{return Core.Engine.GlobalVars.TickCount;}catch(InvalidOperationException){return 0;}}
 private int EngineRound(){try{return Math.Max(1,(Core.EntitySystem.GetGameRules()?.TotalRoundsPlayed??0)+1);}catch(InvalidOperationException){return Math.Max(1,roundNumber);}}
 private double Offset=>Math.Max(0,(Environment.TickCount64-startAt)/1000.0);
 private void BeginRoundIndex(bool partial=false)
 {
  if(recording is not {} r||r.Rounds.LastOrDefault() is {EndSeconds:null})return;
  if(r.Rounds.Count>=512){r.TimelineTruncated=true;return;}
  roundNumber=EngineRound();r.Rounds.Add(new(){Number=roundNumber,StartSeconds=Offset,StartTick=EngineTick(),Partial=partial});
  roundBaseline.Clear();CapturePlayers();foreach(var p in r.Players)roundBaseline[p.SteamId]=p.Copy();
 }
 private void EndRoundIndex(int? winner=null,bool capture=true)
 {
  if(recording is not {} r||r.Rounds.LastOrDefault() is not {EndSeconds:null} round)return;
  if(capture)CapturePlayers();round.EndSeconds=Offset;round.EndTick=EngineTick();round.Winner=winner;
  round.Players=r.Players.Select(p=>{var v=p.Copy();if(roundBaseline.TryGetValue(p.SteamId,out var before)){v.Points-=before.Points;v.Kills-=before.Kills;v.Assists-=before.Assists;v.Deaths-=before.Deaths;}return v;}).ToList();archive.Save();
 }
 private void OnRoundStart()
 {
  mapReady=true;matchEnded=false;lastRoundStart=Environment.TickCount64;
  if(!MapRecording)Stop(false);
  else EndRoundIndex();
  roundOpen=true;roundAttempted=false;roundNumber=EngineRound();
  if(recording is not null)BeginRoundIndex();else if(settings.AutoRecord||settings.RequestsEnabled)Start();
 }
 private void OnRoundEnd(EventRoundEnd e)
 {
  lastRoundEnd=Environment.TickCount64;roundOpen=false;EndRoundIndex(e.Winner);
  if(!MapRecording)Stop(false);
  // Copy small bounded structures on the game thread, serialize/write on worker.
  var ended=recording;var closed=ended?.Rounds.LastOrDefault();var baseline=roundBaseline.ToDictionary(x=>x.Key,x=>x.Value.Copy());
  Core.Scheduler.DelayBySeconds(1,()=>{if(module.Active){CapturePlayers();if(ReferenceEquals(recording,ended)&&ended is not null&&closed is not null){closed.Players=ended.Players.Select(p=>{var v=p.Copy();if(baseline.TryGetValue(p.SteamId,out var before)){v.Points-=before.Points;v.Kills-=before.Kills;v.Assists-=before.Assists;v.Deaths-=before.Deaths;}return v;}).ToList();archive.Save();}archive.FlushBuffered();}});
 }
 private string StartBotTest(long requestedAt)
 {
  if(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-requestedAt)>90)return "Testauftrag abgelaufen";
  if(BotTest||recording is not null)return "Aufnahme bereits aktiv";
  if(!mapReady||!settings.Enabled||!settings.RamRecording||!TvReady())return "Map/SourceTV nicht bereit";
  if(!RamAvailable()){Core.Scheduler.DelayBySeconds(1,()=>{if(module.Active)StartBotTest(requestedAt);});return "RAM-Prüfung läuft; Test startet automatisch nach Bestätigung";}
  if(Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))return "Menschen verbunden; Bot-Test nicht gestartet";
  var quota=Core.ConVar.Find<int>("bot_quota");var join=Core.ConVar.Find<bool>("bot_join_after_player");
  if(quota is null||join is null)return "Bot-Einstellungen nicht verfügbar";
  oldBotQuota=quota.Value;oldBotJoin=join.Value;join.Value=false;quota.Value=Math.Max(10,quota.Value);
  botTestUntil=Environment.TickCount64+300000;Start();
  if(recording is null){RestoreBotTest();return "Testaufnahme konnte nicht gestartet werden";}
  return "Bot-Test gestartet: 300 Sekunden, automatischer Stopp und Wiederherstellung";
 }
 private void RestoreBotTest()
 {
  botTestUntil=0;
  if(oldBotQuota is {} q&&Core.ConVar.Find<int>("bot_quota")is {} quota)quota.Value=q;
  if(oldBotJoin is {} j&&Core.ConVar.Find<bool>("bot_join_after_player")is {} join)join.Value=j;
  oldBotQuota=null;oldBotJoin=null;
 }
 private void TestAndIndexTick(long now)
 {
  if(BotTest&&(now>=botTestUntil||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient))){Stop(false);RestoreBotTest();}
  // Empty servers have no natural round boundary. An ended test/record can still
  // publish after its >=1s delay. Active matches publish only at round end.
  if(!humansPlaying&&recording is null&&now>=nextIndexFlush){nextIndexFlush=now+5000;archive.FlushBuffered();}
 }
 private DemoActor? Actor(IPlayer? p)=>p is {IsValid:true}?new(PlayerKey(p),p.Name,p.IsFakeClient,p.Controller.TeamNum):null;
 private string PlayerKey(IPlayer p)=>p.IsFakeClient?$"bot:{p.PlayerID}:{p.SessionId}":p.SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
