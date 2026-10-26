using System.Text.Json;

namespace LPLK.Common;

// Derived displays only: immutable JSON is captured on the game thread. The
// worker never touches a player, entity, settings object or live collection.
public static class DeferredJson
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string,string> Pending = new();
    private static readonly Dictionary<string,string> Errors = new();
    private static Task? worker;
    public static void Queue<T>(string path,T value) => QueueText(path,JsonSerializer.Serialize(value));
    public static void QueueText(string path,string text)
    {
        if(RoundFileStore.Active&&Path.GetFileName(path)=="status.json"&&text.TrimEnd().EndsWith('}'))
        {
            string trimmed=text.TrimEnd();string io=JsonSerializer.Serialize(new{policy="RAM; RoundEnd+1000ms",pendingFiles=RoundFileStore.Pending,error=RoundFileStore.LastError,gameThreadDiskReads=RoundFileStore.GameThreadDiskReads});
            text=trimmed[..^1]+(trimmed.Trim().Length>2?",":"")+"\"io\":"+io+"}";
        }
        if(RoundData.Attached){RoundData.SaveText(path,text);return;}
        string? previous;
        lock(Gate)
        {
            previous=Errors.GetValueOrDefault(path);
            Pending[path]=text;
            worker ??= Task.Run(Write);
        }
        // Callers already report status-write failures. Retry the new snapshot
        // while reporting an earlier failure instead of silently masking it.
        if(previous is not null)throw new IOException(previous);
    }
    private static void Write()
    {
        while(true)
        {
            KeyValuePair<string,string> item;
            lock(Gate)
            {
                if(Pending.Count==0){worker=null;return;}
                item=Pending.First();Pending.Remove(item.Key);
            }
            string temporary=item.Key+".display-"+Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);
                File.WriteAllText(temporary,item.Value);
                File.Move(temporary,item.Key,true);
                lock(Gate)Errors.Remove(item.Key);
            }
            catch(Exception e){lock(Gate)Errors[item.Key]=e.GetType().Name+": Hintergrundanzeige nicht gespeichert.";}
            finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception){}}
        }
    }
    // Only at unload/tests, never from a gameplay event.
    public static void Drain()
    {
        if(RoundData.Attached)RoundData.Drain();
        while(true){Task? active;lock(Gate)active=worker;if(active is null)return;active.GetAwaiter().GetResult();}
    }
}
