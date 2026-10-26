using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
namespace LPLK_Teams;

public static class BotPopulationPolicy
{
 public static int CountPlayingHumans<T>(IEnumerable<T> clients,Func<T,bool> valid,Func<T,bool> fake,Func<T,int> team)
  =>clients.Count(p=>valid(p)&&!fake(p)&&team(p) is 2 or 3);
 public static bool IsPlayingBot(bool valid,bool fake,bool hltv,int team)=>valid&&fake&&!hltv&&team is 2 or 3;
 public static T? SelectExcessBot<T>(IEnumerable<T> clients,int desired,Func<T,bool> eligible,Func<T,bool> alive) where T:class
 {
  var bots=clients.Where(eligible).OrderBy(alive).ToArray();
  return bots.Length>Math.Max(0,desired)?bots[0]:null;
 }
 public static int DesiredBots(int slots,int humans)=>Math.Max(0,Math.Clamp(slots,0,30)-Math.Max(0,humans));
}

public sealed partial class Plugin
{
 private double nextBotCheck;
 private int botHumans,botDesired,botAdjustments,botActual,botRemovalAttempts;
 private string? previousBotMode;
 private int? previousBotQuota,lastBotQuota;
 private void ReconcileBots()
 {
  if(Now<nextBotCheck)return;nextBotCheck=Now+1;
  if(settings.BotFillSlots<=0){RestoreBotQuota();return;}
  if(Core.EntitySystem.GetGameRules() is null)return;
  var players=Core.PlayerManager.GetAllPlayers();
  UpdateBotQuota(BotPopulationPolicy.CountPlayingHumans(players,p=>p.IsValid&&p.Controller.IsValid,p=>p.IsFakeClient,p=>p.Controller.TeamNum));
  // A quota change alone can leave existing game bots connected. Reconcile the
  // actual population too; never select SourceTV, spectators or a human client.
  botActual=players.Count(IsPlayingBot);
  var excess=BotPopulationPolicy.SelectExcessBot(players,botDesired,IsPlayingBot,p=>p.IsAlive);
  if(excess is not null&&IsPlayingBot(excess))
  {
   excess.Kick("Platz für menschliche Spieler",ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
   botRemovalAttempts++;
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
  botDesired=BotPopulationPolicy.DesiredBots(settings.BotFillSlots,botHumans);
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
