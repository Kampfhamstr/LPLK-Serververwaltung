using SwiftlyS2.Shared.Events;
namespace LPLK_Economy;
public sealed partial class Plugin
{
 private readonly RoundSettlementQueue roundSettlement=new();
 private long settlementRound;
 private DateTimeOffset? lastRoundEndAt,lastRoundSettlementAt;
 private void QueueRoundSettlement(int winner,bool warmup)
 {
  roundActive=false;
  if(roundSettlement.Schedule(settlementRound,Environment.TickCount64,()=>SettleRound(winner,warmup)))lastRoundEndAt=DateTimeOffset.UtcNow;
 }
 [EventListener<EventDelegates.OnWorldUpdate>]
 public void RoundSettlementTick(){if(module.Active)roundSettlement.Drain(Environment.TickCount64);}
 private void FinishPendingRound()=>roundSettlement.Drain(Environment.TickCount64,true);
 private void SettleRound(int winner,bool warmup)
 {
  var timer=System.Diagnostics.Stopwatch.StartNew();roundEndPhases.Clear();lastRoundSettlementAt=DateTimeOffset.UtcNow;
  RoundPhase("kills",FlushRoundKills);
  RoundPhase("bets",()=>EndBettingRound(warmup?9:winner is 2 or 3?winner:0,true));
  RoundPhase("lottery",()=>EndLotteryRound(winner));
  RoundPhase("bonuses",()=>EndBonusRound(winner));
  RoundPhase("robbery",()=>EndRobberyRound(winner));
  RoundPhase("bounties",()=>EndBountyRound(winner));
  RoundPhase("titles",()=>EndTitleRound(winner));
  SaveRoundMemory();ExportWeb();roundEndMs=timer.Elapsed.TotalMilliseconds;WriteStatus();
 }
 private void SaveRoundMemory()
 {
  if(bank is null)return;
  try{_ = bank.PersistRound(lastRoundEndAt?.AddSeconds(1));}
  catch(Exception e){Fail(e);}
 }
}
