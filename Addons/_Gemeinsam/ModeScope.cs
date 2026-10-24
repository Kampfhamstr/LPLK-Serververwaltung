namespace LPLK.Common;

// A queued native callback may outlive the map or the plugin that created it.
// Invalidating the scope happens before hooks, services or player state are torn down.
public sealed class ModeScope
{
    private long generation;
    private bool active;
    public bool Active => Volatile.Read(ref active);
    public long Generation => Interlocked.Read(ref generation);
    public void Start() { Interlocked.Increment(ref generation); Volatile.Write(ref active,true); }
    public void Advance() => Interlocked.Increment(ref generation);
    public void Stop() { Volatile.Write(ref active,false); Interlocked.Increment(ref generation); }
    public Action Bind(Action callback)
    {
        long lease=Generation;
        return () => { if(Active && Generation==lease) callback(); };
    }
}
