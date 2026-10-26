using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;

namespace LPLK_Zombie;

public sealed class ZombieScheduler(ISwiftlyCore core) : IDisposable
{
    public ModeScope Scope {get;}=new();
    private readonly HashSet<CancellationTokenSource> timers=[];
    public int Pending=>timers.Count;
    public void NextTick(Action action)=>core.Scheduler.NextWorldUpdate(Scope.Bind(action));
    public CancellationTokenSource DelayBySeconds(float delay,Action action)=>Track((callback)=>core.Scheduler.DelayBySeconds(delay,callback),action,false);
    public CancellationTokenSource RepeatBySeconds(float interval,Action action)=>Track(callback=>core.Scheduler.RepeatBySeconds(interval,callback),action,true);
    public CancellationTokenSource DelayAndRepeatBySeconds(float delay,float interval,Action action)=>Track(callback=>core.Scheduler.DelayAndRepeatBySeconds(delay,interval,callback),action,true);
    private CancellationTokenSource Track(Func<Action,CancellationTokenSource> create,Action callback,bool repeating)
    {
        // Retired sources need not be retained for the whole map.
        timers.RemoveWhere(t=>t.IsCancellationRequested);
        CancellationTokenSource? token=null;Action guarded=Scope.Bind(callback);
        token=create(()=>{if(!repeating&&token is not null)timers.Remove(token);guarded();});timers.Add(token);return token;
    }
    public void StopOnMapChange(CancellationTokenSource token) { /* Advance cancels every owned source, including one-shot callbacks. */ }
    public Action ForPlayer(IPlayer player,Action action)
    {
        int slot=player.PlayerID;ulong steam=player.SteamID;string session=player.SessionId.ToString();
        return ()=>{var current=core.PlayerManager.GetPlayer(slot);if(current is {IsValid:true}&&current.SteamID==steam&&current.SessionId.ToString()==session)action();};
    }
    public void Advance(){Scope.Advance();Cancel();}
    private void Cancel(){foreach(var timer in timers)timer.Cancel();timers.Clear();}
    public void Dispose(){Scope.Stop();Cancel();}
}
