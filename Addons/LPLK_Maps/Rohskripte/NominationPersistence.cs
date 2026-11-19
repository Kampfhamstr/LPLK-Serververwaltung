using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Maps;
public sealed partial class Plugin
{
 private static readonly string nominationProcess=Environment.ProcessId+":"+System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
 private bool nominationStateReady;
 private bool nominationStateBlocked,nominationMapUnloading;
 private NominationSession? nominationSession;
 private int restoredNominations;
 private string? nominationStateError;
 private string NominationPath=>Path.Combine(RoundFileStore.DataPath(Core),"nominations.json");
 private NominationSession? CurrentNominationSession()
 {
  try {var map=Core.Engine.GlobalVars.MapName;return string.IsNullOrWhiteSpace(map)?null:new(nominationProcess,map,Core.Engine.WorkshopId??"",Core.Engine.GlobalVars.ServerCount);}
  catch(InvalidOperationException){return null;} // Expected before the first map exists.
 }
 private void LoadNominations()
 {
  if(nominationStateReady||nominationStateBlocked||nominationMapUnloading)return;
  InitializeNominations(CurrentNominationSession());
 }
 private void InitializeNominations(NominationSession? session)
 {
  if(session is null||nominationStateReady||nominationStateBlocked)return;
  try{RestoreNominationSnapshot(session);}
  catch(Exception e){nominationStateBlocked=true;nominationStateError=e.GetType().Name;Core.Logger.LogWarning(e,"Nominierungen konnten nicht wiederhergestellt werden; Datei bleibt erhalten.");}
 }
 private void ResetNominations()
 {
  nominations.Clear();restoredNominations=0;nominationStateReady=false;nominationSession=null;nominationMapUnloading=false;LoadNominations();
 }
 private void RestoreNominationSnapshot(NominationSession session)
 {
  var saved=RamFile.Exists(NominationPath)?NominationState.Parse(RamFile.ReadAllText(NominationPath)):null;
  var restored=saved?.Restore(session)??[];
  nominations.Clear();foreach(var item in restored)nominations[item.Key]=item.Value;
  restoredNominations=restored.Count;nominationSession=session;nominationStateReady=true;nominationStateError=null;
  SaveNominationSnapshot(session);
  if(restored.Count>0)Audit("nominations_restored",new{count=restored.Count,mapIds=restored.Values.Distinct().ToArray()});
 }
 private void SaveNominationSnapshot(NominationSession session)=>DeferredJson.Queue(NominationPath,new NominationState(1,session,new(nominations)));
 private void SaveNominations()
 {
  if(!nominationStateReady||nominationSession is null)return;
  try{SaveNominationSnapshot(nominationSession);nominationStateError=null;}
  catch(Exception e){nominationStateError=e.GetType().Name;Core.Logger.LogWarning(e,"Nominierungen konnten nicht gespeichert werden.");}
 }
 private object NominationStatus()=>new{count=nominations.Count,restored=restoredNominations,ready=nominationStateReady,error=nominationStateError,map=CurrentMap,ids=nominations.Values.Distinct().Take(32).ToArray()};

 // Fixed one-shot adapter on the empty test server only. The synthetic entry is
 // never an online voter; the test verifies a real unload/load and then clears it.
 private void NominationReloadProbe(ICommandContext ctx,string action)
 {
  if(ctx.IsSentByPlayer||ctx.Sender is not null||AnyHuman()||voteEnds>0||pending is not null||nextMap is not null)throw new InvalidOperationException("Leerer Testserver erforderlich.");
  string path=Path.Combine(RoundFileStore.DataPath(Core),"nomination-reload-test.json");
  if(!RamFile.Exists(path)||new RamFileInfo(path).Length>2048)throw new InvalidOperationException("Testfreigabe fehlt.");
  using var auth=JsonDocument.Parse(RamFile.ReadAllText(path));var expires=auth.RootElement.GetProperty("expires").GetDateTimeOffset();
  if(auth.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b"||expires<DateTimeOffset.UtcNow||expires>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidOperationException("Testfreigabe ungültig.");
  if(action=="seed")
  {
   if(nominations.Count!=0||!nominationStateReady||nominationStateError is not null||!maps.Any(m=>m.Id=="de_nuke"&&Votable(m)&&m.InPool&&m.Enabled))throw new InvalidOperationException("Testbasis ungültig.");
   nominations[1]="de_nuke";SaveNominations();
  }
  else if(action=="clear")
  {
   if(restoredNominations!=1||nominations.Count!=1||nominations.GetValueOrDefault(1UL)!="de_nuke")throw new InvalidOperationException("Reload nicht bestätigt.");
   nominations.Clear();SaveNominations();RamFile.Delete(path);
  }
  else throw new InvalidOperationException("Unbekannter Testschritt.");
  ctx.ReplyLplk("LPLK_NOMINATION "+JsonSerializer.Serialize(NominationStatus()));
 }
}
