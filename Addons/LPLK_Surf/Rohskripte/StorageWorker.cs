using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using LPLK.Common;
using SwiftlyS2.Shared.Players;

namespace LPLK_Surf;
public sealed partial class Plugin
{
    private Task storageWork=Task.CompletedTask;
    private readonly ConcurrentQueue<Action> storageCompletions=new();
    private string? storageError;
    private void QueueStorage<T>(Func<T> work,Action<T> completed)
    {
        if(surfMemory is not null){try{T result=work();storageError=null;completed(result);}catch(Exception e){storageError=e.GetType().Name;throw;}return;}
        Task previous=storageWork;
        storageWork=Task.Run(async()=>{
            await previous.ConfigureAwait(false);
            try{T result=work();storageCompletions.Enqueue(()=>{storageError=null;completed(result);});}
            catch(Exception e){storageCompletions.Enqueue(()=>{storageError=e.GetType().Name;Core.Logger.LogError(e,"LPLK_Surf: Hintergrundspeicherung fehlgeschlagen.");});}
        });
    }
    private void CompleteStorage()
    {
        int budget=8;
        while(budget-->0&&storageCompletions.TryDequeue(out var callback))callback();
    }
    private static SqliteConnection OpenWorkerDb(string path)
    {
        var db=new SqliteConnection(path.StartsWith("Data Source=",StringComparison.OrdinalIgnoreCase)?path:new SqliteConnectionStringBuilder{DataSource=path}.ToString());db.Open();using(var c=db.CreateCommand()){c.CommandText="PRAGMA temp_store=MEMORY";c.ExecuteNonQuery();}return db;
    }
    private void StoreFinishedRun(IPlayer player,RunState run,double seconds)
    {
        string map=_map,course=run.Course,name=player.Name,path=surfMemoryConnection,replayPath=ReplayPath(course),session=player.SessionId.ToString();
        ulong steam=player.SteamID;int slot=player.PlayerID;
        var stages=run.StageTimes.ToList();var frames=run.ReplayFrames.ToList();
        bool announceWr=_config.Rankings.AnnounceWorldRecord,announcePb=_config.Rankings.AnnouncePersonalBest;
        Reply(player,$"[green]Ziel! [default]{Format(seconds)} – Rang wird gespeichert.");
        QueueStorage(()=>{
            var result=SaveCourseRecord(path,map,steam,name,course,seconds,stages);
            surfRevision++;
            if(result.IsWorldRecord)AtomicJson.Write(replayPath,new ReplayFile(map,course,steam,name,seconds,frames));
            return result;
        },result=>{
            if(!_lifetime.Active||_map!=map)return;
            var current=Core.PlayerManager.GetPlayer(slot);
            if(current is {IsValid:true}&&current.SteamID==steam&&current.SessionId.ToString()==session)
            {
                string delta=result.OldPb is null?"erste Zeit":Signed(seconds-result.OldPb.Value);
                Reply(current,$"[green]Gespeichert: [default]{Format(seconds)} [gray]({delta}) – Rang #{result.Rank}/{result.Total}");
            }
            if(result.IsWorldRecord&&announceWr)Broadcast($"[gold]NEUER WELTREKORD! [default]{name} – {Format(seconds)} auf {map} ({course})");
            else if(result.IsPb&&announcePb)Broadcast($"[green]Neue Bestzeit: [default]{name} – {Format(seconds)} ({course}, #{result.Rank})");
        });
    }
}
