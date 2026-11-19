using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
namespace LPLK_Teams;

public static class BotPopulationPolicy
{
 public static int CountPlayingHumans<T>(IEnumerable<T> clients,Func<T,bool> valid,Func<T,bool> fake,Func<T,int> team)
  =>clients.Count(p=>valid(p)&&!fake(p)&&team(p) is 2 or 3);
 public static bool IsPlayingBot(bool valid,bool fake,bool hltv,int team)=>valid&&fake&&!hltv&&team is 2 or 3;
 public static T? SelectExcessBot<T>(IEnumerable<T> clients,int desired,Func<T,bool> eligible,Func<T,bool> alive,Func<T,int>? team=null,int preferredTeam=0) where T:class
 {
  var bots=clients.Where(eligible).OrderBy(p=>team is not null&&preferredTeam!=0&&team(p)!=preferredTeam).ThenBy(alive).ToArray();
  return bots.Length>Math.Max(0,desired)?bots[0]:null;
 }
 public static int DesiredBots(int slots,int humans)=>Math.Max(0,Math.Clamp(slots,0,30)-Math.Max(0,humans));
 public static (int T,int Ct) TeamTargets(int desired,int humanT,int humanCt,int botT,int botCt)
 {
  desired=Math.Clamp(desired,0,30);humanT=Math.Max(0,humanT);humanCt=Math.Max(0,humanCt);
  int best=0,difference=int.MaxValue,moves=int.MaxValue;
  for(int t=0;t<=desired;t++){
   int diff=Math.Abs(humanT+t-humanCt-(desired-t)),cost=Math.Abs(botT-t)+Math.Abs(botCt-(desired-t));
   if(diff<difference||diff==difference&&cost<moves){best=t;difference=diff;moves=cost;}
  }
  return(best,desired-best);
 }
 public static bool MayMoveBot(bool alive,bool beforeRoundSpawn)=>!alive||beforeRoundSpawn;
}

public sealed partial class Plugin
{
 private double nextBotCheck;
 private int botHumans,botDesired,botAdjustments,botActual,botRemovalAttempts;
 private int botHumanT,botHumanCt,botT,botCt,botTargetT,botTargetCt,botTeamMoves;
 private string botBalanceState="waiting";
 private string? previousBotMode;
 private int? previousBotQuota,lastBotQuota;
 private void ReconcileBots(bool beforeRoundSpawn=false)
 {
  if(!beforeRoundSpawn&&Now<nextBotCheck)return;nextBotCheck=Now+1;
  if(settings.BotFillSlots<=0){botBalanceState="disabled";RestoreBotQuota();return;}
  if(Core.EntitySystem.GetGameRules() is null)return;
  var players=Core.PlayerManager.GetAllPlayers();
  UpdateBotQuota(BotPopulationPolicy.CountPlayingHumans(players,p=>p.IsValid&&p.Controller.IsValid,p=>p.IsFakeClient,p=>p.Controller.TeamNum));
  botHumanT=players.Count(p=>p.IsValid&&!p.IsFakeClient&&p.Controller.IsValid&&p.Controller.TeamNum==2);
  botHumanCt=botHumans-botHumanT;
  botT=players.Count(p=>IsPlayingBot(p)&&p.Controller.TeamNum==2);botCt=players.Count(p=>IsPlayingBot(p)&&p.Controller.TeamNum==3);
  (botTargetT,botTargetCt)=BotPopulationPolicy.TeamTargets(botDesired,botHumanT,botHumanCt,botT,botCt);
  botActual=botT+botCt;
  // A human can be controlling a game bot. Do not remove or move that pawn.
  if(players.Any(p=>p.IsValid&&!p.IsFakeClient&&p.Controller.IsValid&&p.Controller.ControllingBot)){botBalanceState="human_bot_control";return;}
  // A quota change alone can leave existing game bots connected. Reconcile the
  // actual population too; never select SourceTV, spectators or a human client.
  int excessTeam=botT-botTargetT>botCt-botTargetCt?2:3;
  var excess=BotPopulationPolicy.SelectExcessBot(players,botDesired,IsPlayingBot,p=>p.IsAlive,p=>p.Controller.TeamNum,excessTeam);
  if(excess is not null&&IsPlayingBot(excess))
  {
   excess.Kick("Platz für menschliche Spieler",ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
   botRemovalAttempts++;botBalanceState="removing_surplus";return;
  }
  if(botActual!=botDesired){botBalanceState="waiting_for_fill";return;}
  int source=botT>botTargetT?2:botCt>botTargetCt?3:0;
  if(source==0){botBalanceState="balanced";return;}
  // Alive bots keep their team during combat. Dead bots can move immediately;
  // all remaining corrections happen before the next round's spawn, never at
  // the old team's spawn during freeze time and never by moving a human.
  int needed=source==2?botT-botTargetT:botCt-botTargetCt;
  var candidates=players.Where(p=>IsPlayingBot(p)&&p.Controller.TeamNum==source&&BotPopulationPolicy.MayMoveBot(p.IsAlive,beforeRoundSpawn)).OrderBy(p=>p.IsAlive).Take(beforeRoundSpawn?needed:1).ToArray();
  botBalanceState="waiting_for_round_spawn";
  foreach(var bot in candidates){
   if(!IsPlayingBot(bot)||bot.Controller.TeamNum!=source||!BotPopulationPolicy.MayMoveBot(bot.IsAlive,beforeRoundSpawn))continue;
   bot.SwitchTeam((Team)(source==2?3:2));botTeamMoves++;botBalanceState="bot_moved";
  }
 }
 private static bool IsPlayingBot(IPlayer p)
 {
  if(!p.IsValid||!p.IsFakeClient)return false;
  var controller=p.Controller;
  return controller.IsValid&&BotPopulationPolicy.IsPlayingBot(true,true,controller.IsHLTV,controller.TeamNum);
 }
 private void UpdateBotQuota(int humans)
 {
  // Only humans in T/CT replace game bots. Spectators, connecting clients and
  // SourceTV do not count. The native quota is changed without any kick command.
  botHumans=humans;
  botDesired=settings.TwoPlayerBotVoteEnabled&&botVote.Suppressed?0:BotPopulationPolicy.DesiredBots(settings.BotFillSlots,botHumans);
  var mode=Core.ConVar.Find<string>("bot_quota_mode");var quota=Core.ConVar.Find<int>("bot_quota");
  if(mode is null||quota is null)return;
  previousBotMode??=mode.Value;previousBotQuota??=quota.Value;
  if(mode.Value!="normal"){mode.Value="normal";botAdjustments++;}
  if(quota.Value!=botDesired){quota.Value=botDesired;botAdjustments++;}
  lastBotQuota=botDesired;
 }
 private void RestoreBotQuota()
 {
  if(previousBotMode is null)return;
  var mode=Core.ConVar.Find<string>("bot_quota_mode");var quota=Core.ConVar.Find<int>("bot_quota");
  if(mode is not null&&quota is not null&&mode.Value=="normal"&&quota.Value==lastBotQuota)
  {mode.Value=previousBotMode;if(previousBotQuota is {} original)quota.Value=original;}
  previousBotMode=null;previousBotQuota=null;lastBotQuota=null;
 }
}
