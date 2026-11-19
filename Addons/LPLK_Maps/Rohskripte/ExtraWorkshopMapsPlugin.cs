using System.Text.Json;
using LPLK.Common;
namespace LPLK_Maps;
public sealed partial class Plugin
{
 private ExtraWorkshopMapFile? extraMapFile;
 private string[] fileOnlyMapIds=[];
 private NewMapMarkers newMapMarkers=new();
 private NewMapActivationState? newMapActivations;
 private string? newMapMarkerError;
 private string NewMapMarkerPath=>Path.Combine(RoundFileStore.DataPath(Core),"new-map-markers.json");
 private string NewMapActivationPath=>Path.Combine(RoundFileStore.DataPath(Core),"new-map-activations.json");
 private string ExtraSourcePath=>Path.Combine(RoundFileStore.DataPath(Core),"extra-workshop-source.json");
 private void LoadExtraWorkshopMaps()
 {
  extraMapFile=new(Path.Combine(RoundFileStore.GamePath(Core),ExtraWorkshopMapList.RelativePath));extraMapFile.Load();
  try{if(RamFile.Exists(ExtraSourcePath))fileOnlyMapIds=JsonSerializer.Deserialize<string[]>(RamFile.ReadAllText(ExtraSourcePath))?.Where(id=>MapPolicy.WorkshopId(id)==id).Distinct().Take(2000).ToArray()??[];}
  catch(JsonException){fileOnlyMapIds=[];}
 }
 private void LoadNewMapActivations()
 {
  try
  {
   if(RamFile.Exists(NewMapActivationPath))newMapActivations=NewMapActivationState.Parse(RamFile.ReadAllText(NewMapActivationPath));
   else
   {
    var legacy=RamFile.Exists(NewMapMarkerPath)?NewMapMarkers.Parse(RamFile.ReadAllText(NewMapMarkerPath)):new NewMapMarkers();
    // Grandfather the established pool once, independent of scan completion and
    // today's player count. Pending/unapproved maps start only when ready later.
    newMapActivations=NewMapActivationState.Migrate(maps.Where(m=>m.IsMap&&m.InPool&&m.Enabled&&!m.Removed&&MapPolicy.LimitsUsable(m,settings)).Select(m=>m.Id),legacy,DateTimeOffset.UtcNow);
    DeferredJson.Queue(NewMapActivationPath,newMapActivations.Snapshot());
   }
   newMapMarkers=newMapActivations.Markers;
  }
  catch(Exception e)when(e is JsonException or InvalidDataException){newMapMarkerError=e.Message;}
 }
 private bool ObserveNewMapActivations(NewMapActivationState state,DateTimeOffset now)=>settings.Enabled&&!settings.MeasurementOnly&&state.Observe(maps.Where(m=>m.InPool&&m.Enabled&&!m.Removed&&Votable(m)).Select(m=>m.Id),now);
 private void TrackNewMapActivations()
 {
  if(newMapMarkerError is not null||newMapActivations is null)return;
  try{if(ObserveNewMapActivations(newMapActivations,DateTimeOffset.UtcNow))DeferredJson.Queue(NewMapActivationPath,newMapActivations.Snapshot());}
  catch(InvalidDataException e){newMapMarkerError=e.Message;}
 }
 private object NewMapStatus()=>new{days=NewMapMarkers.Days,automatic=true,initializedAt=newMapActivations?.InitializedAt,error=newMapMarkerError,maps=maps.Where(m=>newMapMarkers.IsNew(m.Id,DateTimeOffset.UtcNow)).Select(m=>new{m.Id,m.Name,votable=Votable(m),inPool=m.InPool,m.Enabled,m.Removed,label=newMapMarkers.Label(m.Id,MapPolicy.VoteLabel(m,false),DateTimeOffset.UtcNow)}).ToArray()};
 private object PoolReadout()
 {
  var builtin=maps.Where(m=>!m.Workshop).ToArray();var eligible=Eligible();var now=DateTimeOffset.UtcNow;
  // Console replies are bounded. Keep this read-only probe compact even on a large pool.
  return new{at=now,builtin=builtin.Select(m=>m.Id).ToArray(),available=builtin.Where(Available).Select(m=>m.Id).ToArray(),votable=builtin.Where(m=>Votable(m)&&m.InPool&&m.Enabled&&!m.Removed).Select(m=>m.Id).ToArray(),mapChange=builtin.Where(m=>MapChangeEntries(m.MinPlayers).Any(x=>x.Id==m.Id)).Select(m=>m.Id).ToArray(),eligibleNew=eligible.Where(m=>newMapMarkers.IsNew(m.Id,now)).Select(m=>m.Id).ToArray(),nominationFirstPage=newMapMarkers.OrderNominations(eligible,now).Take(10).Select(m=>m.Id).ToArray()};
 }
 private void PollExtraWorkshopMaps(long now)
 {
  if(extraMapFile is null)return;
  var previous=extraMapFile.CheckedAt;
  if(extraMapFile.Poll(now,work=>RoundFileStore.RunAfterRound(work)))nextPoolRefresh=0;
  if(extraMapFile.CheckedAt!=previous)WriteStatus();
 }
}
