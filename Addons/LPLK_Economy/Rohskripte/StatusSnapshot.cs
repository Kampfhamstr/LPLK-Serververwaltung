namespace LPLK_Economy;
public sealed record BankDisplay(long Period,long Accounts,long Transactions,long Pending,long OpenBetRounds,long BetClaims,long OpenLotteryRounds,long Superpot,long LotteryRefunds,long OpenRobberies,long RobberyClaims,long ActiveBounties,long PendingBounties,long BountyClaims,long KillClaims);
public sealed partial class Ledger
{
 public Func<BankDisplay> CreateDisplayReader()
 {
  lock(gate)
  {
   ObjectDisposedException.ThrowIf(disposed,this);
   string path=SnapshotReadPath,server=scope;var rules=titleRules;
   return ()=>{
    using var r=new Ledger(path,server,rules);
    var bounties=r.Bounties();
    return new(r.Period,r.AccountCount,r.TransactionCount,r.PendingCount,r.OpenBetRounds,r.BetClaims,r.OpenLotteryRounds,r.Superpot,r.LotteryRefunds,r.RobberyOrders(true,10000).Count(o=>o.Status!="done"),r.RobberyClaims,bounties.Count(o=>o.Status=="active"),bounties.Count(o=>o.Status=="pending"),r.BountyClaims().Sum(c=>c.Remaining),r.KillRewardClaims().Sum(c=>(long)c.Remaining));
   };
  }
 }
}
public sealed partial class Plugin
{
 private Task<BankDisplay>? displayTask;
 private BankDisplay? display;
 private long nextDisplay;
 private string? displayError;
 private void RefreshDisplay()
 {
  if(displayTask?.IsCompleted==true)
  {
   try{display=displayTask.GetAwaiter().GetResult();displayError=null;}catch{displayError="Bankanzeige wird erneut geladen.";}
   displayTask=null;
  }
  if(bank is null||displayTask is not null||Environment.TickCount64<nextDisplay)return;
  nextDisplay=Environment.TickCount64+15000;
  var read=bank.CreateDisplayReader();displayTask=LPLK.Common.RoundFileStore.RunAfterRound(read);
 }
}
