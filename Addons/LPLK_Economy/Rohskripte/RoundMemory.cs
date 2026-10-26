using Microsoft.Data.Sqlite;
namespace LPLK_Economy;

// One private in-memory ledger on the game thread. The writer only receives
// detached in-memory copies, so slow disk IO never holds the live ledger lock.
public sealed partial class Ledger
{
    private string? persistencePath;
    private RoundMemoryWriter? memoryWriter;
    private long memoryRevision;
    private Task persistenceBarrier=Task.CompletedTask;
    private DateTimeOffset persistenceNotBefore;
    public bool InMemory=>memoryWriter is not null;
    public string PersistencePolicy=>InMemory?"MemoryUntilRoundEndPlus1s":"Immediate";
    public string? PersistenceError=>memoryWriter?.Error;
    public long PersistedRevision=>memoryWriter?.SavedRevision??0;
    public long MemoryRevision=>memoryRevision;
    public double SnapshotMilliseconds {get;private set;}
    public double PersistenceMilliseconds=>memoryWriter?.WriteMilliseconds??0;
    public bool PersistenceRunning=>memoryWriter?.Running??false;
    private string SnapshotReadPath=>persistencePath??db.DataSource;

    private static SqliteConnection OpenMemory()
    {
        var connection=new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();return connection;
    }
    private void EnableRoundMemory(string path)
    {
        if(Networked)throw new InvalidOperationException("Round memory requires a local bank.");
        var copy=OpenMemory();
        try{db.BackupDatabase(copy);}catch{copy.Dispose();throw;}
        db.Dispose();db=copy;persistencePath=path;memoryWriter=new(path,fault);
        Exec("PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;");
    }
    public Task PersistRound(DateTimeOffset? notBefore=null)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(memoryWriter is null)return Task.CompletedTask;
            if(transaction is not null)throw new InvalidOperationException("Cannot snapshot an unfinished transaction.");
            if(memoryRevision<=memoryWriter.SavedRevision)return persistenceBarrier;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var copy=OpenMemory();
            try{db.BackupDatabase(copy);}catch{copy.Dispose();throw;}
            SnapshotMilliseconds=timer.Elapsed.TotalMilliseconds;
            if(notBefore is {} boundary&&boundary>persistenceNotBefore)persistenceNotBefore=boundary;
            return persistenceBarrier=memoryWriter.Queue(copy,memoryRevision,persistenceNotBefore);
        }
    }
    public Task WaitForRoundPersistence()=>persistenceBarrier;
    // Only orderly unload uses this blocking drain. Gameplay calls PersistRound.
    public void FlushPersistence()=>PersistRound().GetAwaiter().GetResult();
}

internal sealed class RoundMemoryWriter(string path,Action<string>? fault)
{
    private sealed record Snapshot(SqliteConnection Database,long Revision,DateTimeOffset NotBefore);
    private readonly object sync=new();
    private Snapshot? pending;
    private Task? worker;
    private long saved;
    private string? error;
    private double milliseconds;
    public long SavedRevision {get{lock(sync)return saved;}}
    public string? Error {get{lock(sync)return error;}}
    public double WriteMilliseconds {get{lock(sync)return milliseconds;}}
    public bool Running {get{lock(sync)return worker is not null;}}
    public Task Queue(SqliteConnection copy,long revision,DateTimeOffset notBefore)
    {
        lock(sync)
        {
            // At most the current write and one newer full snapshot are retained.
            pending?.Database.Dispose();pending=new(copy,revision,notBefore);
            worker??=Task.Run(Write);
            return VerifySaved(worker,revision);
        }
    }
    private async Task VerifySaved(Task cycle,long revision)
    {
        await cycle.ConfigureAwait(false);
        if(SavedRevision<revision)throw new IOException(Error??"Bank snapshot was not saved.");
    }
    private async Task Write()
    {
        while(true)
        {
            Snapshot snapshot;
            lock(sync){if(pending is null){worker=null;return;}snapshot=pending;pending=null;}
            using(snapshot.Database)
            {
                try
                {
                    var remaining=snapshot.NotBefore-DateTimeOffset.UtcNow;
                    if(remaining>TimeSpan.Zero)await Task.Delay(remaining).ConfigureAwait(false);
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    fault?.Invoke("memory-before-persist");
                    foreach(var name in new[]{path,path+"-wal",path+"-shm"})
                        if(new FileInfo(name).LinkTarget is not null)throw new IOException("Linked bank snapshot target.");
                    // Existing DB only: loss/replacement must never silently create an empty bank.
                    using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=1}.ToString());
                    target.Open();using(var command=target.CreateCommand()){command.CommandText="PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=256;";command.ExecuteNonQuery();}
                    snapshot.Database.BackupDatabase(target);
                    lock(sync){saved=snapshot.Revision;error=null;milliseconds=timer.Elapsed.TotalMilliseconds;}
                }
                catch(Exception e){lock(sync)error="Bank-Speicherung fehlgeschlagen ("+e.GetType().Name+"); aktueller Stand bleibt im Arbeitsspeicher.";}
            }
        }
    }
}
