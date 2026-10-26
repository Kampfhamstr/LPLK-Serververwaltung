using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
namespace LPLK.Common;

// Non-critical indexes/preferences/logs stay in RAM until the safe round boundary.
// Engine-owned demo recording and administrative configuration use their own APIs.
public static class RoundData
{
 private static readonly Dictionary<string,string> snapshots=[];
 private static readonly Dictionary<string,List<string>> logs=[];
 private static readonly object gate=new();
 private static readonly Dictionary<string,string> errors=[];
 private static readonly HashSet<string> displayPaths=[];
 private static Task writer=Task.CompletedTask;
 private static bool attached;
 private static ILogger? logger;
 public static void Attach(ISwiftlyCore core)
 {
  if(attached)return;attached=true;logger=core.Logger;
  core.GameEvent.HookPost<EventRoundEnd>(_=>{core.Scheduler.DelayBySeconds(1,FlushNow);return HookResult.Continue;});
 }
 public static void Save<T>(string path,T value){string json=JsonSerializer.Serialize(value);lock(gate)snapshots[path]=json;}
 public static void SaveText(string path,string text){lock(gate){snapshots[path]=text;displayPaths.Add(path);if(errors.TryGetValue(path,out var error))throw new IOException(error);}}
 public static bool Attached=>attached;
 public static void Append(string path,string line)
 =>AppendText(path,line+Environment.NewLine);
 public static void AppendText(string path,string line)
 {
  lock(gate){if(!logs.TryGetValue(path,out var list))logs[path]=list=[];list.Add(line);}
 }
 // Gameplay callers can request a flush, but cannot advance the round boundary.
 public static void Flush(){if(!attached)FlushNow();}
 internal static void FlushIdle()=>FlushNow();
 private static void FlushNow()
 {
  lock(gate)
  {
  if(logs.Count==0&&snapshots.Count==0)return;
  var copies=new Dictionary<string,string>(snapshots);snapshots.Clear();
  var batch=logs.ToDictionary(x=>x.Key,x=>x.Value);logs.Clear();
  Task previous=writer;
  writer=Task.Run(async()=>{
   try{await previous.ConfigureAwait(false);}catch{/* Failed batches were retained for this or the next round. */}
   Exception? failure=null;
   foreach(var item in copies)
   {
    string temporary=item.Key+".round-"+Guid.NewGuid().ToString("N");
    try{Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);await File.WriteAllTextAsync(temporary,item.Value).ConfigureAwait(false);File.Move(temporary,item.Key,true);lock(gate)errors.Remove(item.Key);}
    catch(Exception e){lock(gate){errors[item.Key]=e.GetType().Name;snapshots.TryAdd(item.Key,item.Value);if(!displayPaths.Contains(item.Key))failure=e;}}
    finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
   }
   foreach(var item in batch)
   {
    try{Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);await File.AppendAllTextAsync(item.Key,string.Concat(item.Value)).ConfigureAwait(false);}
    catch(Exception e){failure=e;lock(gate){if(logs.TryGetValue(item.Key,out var newer))item.Value.AddRange(newer);logs[item.Key]=item.Value;}}
   }
   if(failure is not null){logger?.LogError(failure,"Rundendaten konnten nicht gespeichert werden; aktueller Stand bleibt im Arbeitsspeicher.");throw new IOException("Round data write failed; entries retained in memory.",failure);}
  });
  }
 }
 public static void Drain(){try{writer.GetAwaiter().GetResult();}catch{/* Retry the retained batch at orderly unload. */}FlushNow();writer.GetAwaiter().GetResult();}
}
