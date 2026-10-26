using Microsoft.Data.Sqlite;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
namespace LPLK_Surf;
public sealed partial class Plugin
{
 private SqliteConnection? surfMemory;
 private string surfMemoryConnection="";
 private long surfRevision,surfSavedRevision;
 private Task surfWriter=Task.CompletedTask;
 private string? surfSaveError;
 private void InitializeMemoryRecords()
 {
  surfMemoryConnection=new SqliteConnectionStringBuilder{DataSource="lplk-surf-"+Guid.NewGuid().ToString("N"),Mode=SqliteOpenMode.Memory,Cache=SqliteCacheMode.Shared,Pooling=false}.ToString();
  using var disk=OpenDb();
  var memory=new SqliteConnection(surfMemoryConnection);memory.Open();
  try{disk.BackupDatabase(memory);}catch{memory.Dispose();throw;}
  using(var command=memory.CreateCommand()){command.CommandText="PRAGMA journal_mode=MEMORY; PRAGMA temp_store=MEMORY; PRAGMA synchronous=OFF;";command.ExecuteNonQuery();}
  surfMemory=memory;
  Core.GameEvent.HookPost<EventRoundEnd>(_=>{Core.Scheduler.DelayBySeconds(1,SaveMemoryRecords);return HookResult.Continue;});
 }
 private void SaveMemoryRecords()
 {
  if(surfMemory is null||surfRevision<=Interlocked.Read(ref surfSavedRevision))return;
  var copy=new SqliteConnection("Data Source=:memory:;Pooling=False");copy.Open();
  try{surfMemory.BackupDatabase(copy);}catch{copy.Dispose();throw;}
  string path=_dbFile;long version=surfRevision;Task prior=surfWriter;
  surfWriter=Task.Run(async()=>{
   using(copy){try{await prior.ConfigureAwait(false);}catch{}
    try{using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());target.Open();copy.BackupDatabase(target);Interlocked.Exchange(ref surfSavedRevision,version);surfSaveError=null;}
    catch(Exception e){surfSaveError=e.GetType().Name;throw;}
   }
  });
 }
 private void CloseMemoryRecords(){SaveMemoryRecords();surfWriter.GetAwaiter().GetResult();surfMemory?.Dispose();surfMemory=null;}
}
