namespace LPLK_Economy;

// Game-thread scheduler. No event/player objects are retained by the queue.
public sealed class RoundSettlementQueue
{
 public const int DelayMilliseconds=1000;
 private Action? pending;
 private readonly Queue<Action> boundaries=new();
 private long due,lastRound=long.MinValue;
 public bool Pending=>pending is not null;
 public void Boundary(Action work){if(Pending)boundaries.Enqueue(work);else work();}
 public bool Schedule(long round,long now,Action work)
 {
  if(round==lastRound)return false;
  Drain(now,true);
  lastRound=round;due=checked(now+DelayMilliseconds);pending=work;return true;
 }
 public bool Drain(long now,bool force=false)
 {
  if(pending is null||!force&&now<due)return false;
  var work=pending;pending=null;
  try{work();}finally{while(boundaries.TryDequeue(out var boundary))boundary();}
  return true;
 }
}
