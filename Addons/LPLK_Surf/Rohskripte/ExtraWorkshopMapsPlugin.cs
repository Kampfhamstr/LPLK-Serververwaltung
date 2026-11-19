using LPLK.Common;
using LPLK_Maps;
namespace LPLK_Surf;
public sealed partial class Plugin
{
 private ExtraWorkshopMapFile? extraMapFile;
 private WorkshopMaintenance? extraMapMaintenance;
 private long nextExtraMapTick,nextExtraMapStatus;
 private IReadOnlyList<MapEntry> extraMapCatalog=[];
 private void LoadExtraWorkshopMaps()
 {
  extraMapFile=new(Path.Combine(RoundFileStore.GamePath(Core),ExtraWorkshopMapList.RelativePath));extraMapFile.Load();
  extraMapCatalog=extraMapFile.Ids.Select(id=>new MapEntry{Id=id,Name=id}).ToArray();
  string root=ResolveWorkshopPath(_config.MapVote.WorkshopPath);
  extraMapMaintenance=new(new SteamWorkshopMaintenanceBackend(root,10,0));
 }
 private void TickExtraWorkshopMaps(long now)
 {
  if(extraMapFile is null||extraMapMaintenance is null||now<nextExtraMapTick)return;
  nextExtraMapTick=now+1000;
  if(extraMapFile.Poll(now,work=>RoundFileStore.RunAfterRound(work)))extraMapCatalog=extraMapFile.Ids.Select(id=>new MapEntry{Id=id,Name=id}).ToArray();
  bool humans=Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient);
  extraMapMaintenance.Tick(now,DateTimeOffset.UtcNow,extraMapCatalog,CurrentWorkshopId(),"",humans,_config.Enabled&&_config.MapVote.Enabled&&extraMapFile.Error is null,false);
  // The downloader already validated the package off-thread. Use that immutable
  // result directly; never rescan directories on the engine thread after download.
  foreach(var item in extraMapMaintenance.Results.Where(r=>r.State is "current" or "updated"))
  {
   var map=new WorkshopMap(item.Id,item.Name,ClassifyMap(item.Name,[item.Source]),item.Source);
   int index=_workshopMaps.FindIndex(m=>m.Id==item.Id);
   if(index<0)_workshopMaps.Add(map);else if(_workshopMaps[index]!=map)_workshopMaps[index]=map;
  }
  if(now>=nextExtraMapStatus)
  {
   nextExtraMapStatus=now+10000;
   DeferredJson.Queue(Path.Combine(RoundFileStore.DataPath(Core),"extra-workshop-status.json"),new{version="0.1.13",updatedAt=DateTimeOffset.UtcNow,input=extraMapFile.Status,state=extraMapMaintenance.State,maps=extraMapMaintenance.Results.ToArray()});
  }
 }
}
