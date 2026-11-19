using System.Text.Json;

namespace LPLK_Maps;

public sealed record MapAuditEvent(string Id, DateTimeOffset At, string Kind, string Map, int Round,
    string? VoteId, object? Data);
public sealed record MapAuditDocument(int Schema, long Pruned, MapAuditEvent[] Events);

// No engine objects, serializers or filesystem calls enter the capture path.
// The caller releases a fixed RAM batch at round_end + 1s. Later events stay queued.
public sealed class MapAuditJournal(string path)
{
    public const int Limit = 10000;
    private readonly object gate = new();
    private readonly Queue<MapAuditEvent> pending = new();
    private Task writer = Task.CompletedTask;
    private MapAuditDocument? saved;
    private string? error;
    private long dropped;
    public int Pending { get { lock(gate) return pending.Count; } }
    public long Dropped { get { lock(gate) return dropped; } }
    public string? Error { get { lock(gate) return error; } }
    public int Persisted { get { lock(gate) return saved?.Events.Length ?? 0; } }
    public void Capture(MapAuditEvent item)
    {
        lock(gate)
        {
            if(pending.Count >= Limit) { dropped++; error="RAM-Protokoll voll; Ereignis nicht aufgenommen"; return; }
            pending.Enqueue(item);
        }
    }
    public void ReleaseAfterRound()
    {
        lock(gate)
        {
            if(!writer.IsCompleted || pending.Count==0) return;
            var batch=pending.ToArray();
            writer=Task.Run(()=>Commit(batch));
        }
    }
    public void WaitForReleasedWrites() { Task task; lock(gate) task=writer; task.GetAwaiter().GetResult(); }
    private void Commit(MapAuditEvent[] batch)
    {
        string temporary=path+".incoming";
        try
        {
            // All reads, serialization, writes and fsync are confined to this worker.
            for(var dir=new DirectoryInfo(Path.GetDirectoryName(path)!);dir is not null;dir=dir.Parent)
                if(dir.LinkTarget is not null)throw new IOException("Verlinktes Protokollverzeichnis");
            if(new FileInfo(path).LinkTarget is not null || new FileInfo(temporary).LinkTarget is not null)
                throw new IOException("Verlinktes Protokollziel");
            var previous=saved;
            if(previous is null)
            {
                if(File.Exists(path))
                {
                    if(new FileInfo(path).Length>32*1024*1024)throw new IOException("Protokolldatei überschreitet 32 MiB");
                    previous=JsonSerializer.Deserialize<MapAuditDocument>(File.ReadAllText(path));
                    if(previous is null || previous.Schema!=1 || previous.Events is null)throw new IOException("Ungültiges vorhandenes Protokoll; erhalten");
                }
                else previous=new(1,0,[]);
            }
            var combined=previous.Events.Concat(batch).DistinctBy(e=>e.Id).ToArray();
            var document=new MapAuditDocument(1,previous.Pruned+Math.Max(0,combined.Length-Limit),combined.TakeLast(Limit).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,document); stream.Flush(true); }
            File.Move(temporary,path,true);
            lock(gate)
            {
                saved=document;
                foreach(var item in batch)
                {
                    if(pending.Count==0 || pending.Peek().Id!=item.Id)throw new InvalidOperationException("Protokollreihenfolge verändert");
                    pending.Dequeue();
                }
                error=dropped==0?null:"RAM-Protokollgrenze wurde erreicht; siehe Dropped";
            }
        }
        catch(Exception e) { lock(gate) error=e.Message; } // Keep RAM batch; retry at the next round boundary.
    }
}
