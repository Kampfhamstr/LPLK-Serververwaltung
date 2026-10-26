using System.Diagnostics;
using LPLK.Common;
using Microsoft.Extensions.Logging;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 private Task<long>? statisticsWrite;
 private ulong[] writingExports=[];
 private bool killSavePending;
 private long statisticsWriteMs;
 private bool roundSaveScheduled;
 private void RequestKillSave()
 {
  killSavePending=true;
 }
 private void RequestRoundSave()
 {
  killSavePending=true;
  if(roundSaveScheduled)return;roundSaveScheduled=true;
  Core.Scheduler.DelayBySeconds(1,()=>{roundSaveScheduled=false;if(module.Active){StartKillSave();FlushRanking();}});
 }
 private void StartKillSave()
 {
  CompleteStatisticsWrite();
  if(statisticsWrite is not null||!killSavePending||!dirty)return;
  // Only detached managed values cross the thread boundary. No engine objects,
  // shared dictionaries, JSON serialization or file I/O in the kill callback.
  var snapshot=data.Copy();var exportSettings=settings;var exports=pendingExports.ToArray();
  string mainPath=dataPath,exportDirectory=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"web");
  writingExports=exports;pendingExports.Clear();dirty=false;killSavePending=false;
  statisticsWrite=Task.Run(()=>RoundFileStore.Physical(()=>{
   var timer=Stopwatch.StartNew();snapshot.Validate();WriteRoundSnapshot(mainPath,snapshot);
   foreach(ulong id in exports)WriteRoundSnapshot(Path.Combine(exportDirectory,id+".json"),WebExport.Player(id,snapshot.Players[id],exportSettings,DateTimeOffset.UtcNow));
   WriteRoundSnapshot(Path.Combine(exportDirectory,"ranking.json"),RankingExport.Create(snapshot,exportSettings,DateTimeOffset.UtcNow));
   return timer.ElapsedMilliseconds;
  }));
 }
 private static void WriteRoundSnapshot<T>(string path,T value){AtomicJson.Write(path,value);_ = RamFile.ReadAllText(path);}
 private void CompleteStatisticsWrite(bool wait=false)
 {
  if(statisticsWrite is null||!wait&&!statisticsWrite.IsCompleted)return;
  try{statisticsWriteMs=statisticsWrite.GetAwaiter().GetResult();saveError=null;}
  catch(Exception e){dirty=true;killSavePending=true;pendingExports.UnionWith(writingExports);if(saveError is null){errors++;Core.Logger.LogError(e,"LPLK_Statistics: Hintergrundspeicherung fehlgeschlagen; aktueller Stand wird erneut gesichert.");}saveError="Speichern fehlgeschlagen";}
  statisticsWrite=null;writingExports=[];
 }
}
