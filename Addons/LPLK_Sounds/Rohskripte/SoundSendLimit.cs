namespace LPLK_Sounds;

public sealed class SoundSendLimit
{
    private readonly Lock sync=new();
    private readonly HashSet<(ulong Sender,ulong Recipient)> used=[];
    public SoundStartResult TryStart(ulong sender,ulong recipient,Func<bool> start,bool bypassLimit=false)
    {
        if(sender==0 || recipient==0 || sender==recipient) throw new ArgumentException("Private sounds need two distinct player identities.");
        lock(sync)
        {
            if(bypassLimit) return start()?SoundStartResult.Started:SoundStartResult.Failed;
            var pair=(sender,recipient);
            if(!used.Add(pair)) return SoundStartResult.LimitReached;
            bool started=false;
            try { started=start();return started?SoundStartResult.Started:SoundStartResult.Failed; }
            finally { if(!started) used.Remove(pair); }
        }
    }
    public void Reset() { lock(sync) used.Clear(); }
}
