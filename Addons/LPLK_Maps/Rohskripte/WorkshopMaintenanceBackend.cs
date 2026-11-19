using SwiftlyS2.Shared.SteamAPI;

namespace LPLK_Maps;

internal sealed class SteamWorkshopMaintenanceBackend(string root,int minimumFreeGiB,int maxGiB):IWorkshopMaintenanceBackend
{
 private Callback<DownloadItemResult_t>? callback;private Action<string,bool,string>? completed;
 public bool Connected=>SteamGameServer.BLoggedOn();
 public async Task<WorkshopItem> Fetch(string id,CancellationToken token)=>(await WorkshopDetails.Fetch([id],token).ConfigureAwait(false)).Single();
 public WorkshopLocalState Read(string id)
 {
  var file=new PublishedFileId_t(ulong.Parse(id));var state=(EItemState)SteamGameServerUGC.GetItemState(file);
  bool updating=state.HasFlag(EItemState.k_EItemStateNeedsUpdate),downloading=state.HasFlag(EItemState.k_EItemStateDownloading)||state.HasFlag(EItemState.k_EItemStateDownloadPending);
  if(updating||downloading){SteamGameServerUGC.GetItemDownloadInfo(file,out ulong done,out ulong total);return new(false,updating,downloading,0,0,"",done,total);}
  if(!state.HasFlag(EItemState.k_EItemStateInstalled)||!SteamGameServerUGC.GetItemInstallInfo(file,out ulong size,out string? folder,4096,out uint stamp))return new(false,false,false,0,0,"");
  return new(size>0,false,false,stamp,size,folder??"");
 }
 public (ulong Done,ulong Total) Progress(string id){SteamGameServerUGC.GetItemDownloadInfo(new PublishedFileId_t(ulong.Parse(id)),out ulong done,out ulong total);return(done,total);}
 public bool Download(string id,Action<string,bool,string> done)
 {
  completed=done;callback??=Callback<DownloadItemResult_t>.Create(r=>{if(r.m_unAppID.m_AppId==730)completed?.Invoke(r.m_nPublishedFileId.m_PublishedFileId.ToString(),r.m_eResult==EResult.k_EResultOK,r.m_eResult.ToString());});
  return SteamGameServerUGC.DownloadItem(new PublishedFileId_t(ulong.Parse(id)),false);
 }
 public void Suspend(bool paused)=>SteamGameServerUGC.SuspendDownloads(paused);
 public Task<bool> Reserve(long bytes,CancellationToken token)=>Task.Run(()=>
 {
  token.ThrowIfCancellationRequested();var drive=new DriveInfo(Path.GetPathRoot(root)!);
  if(drive.AvailableFreeSpace-bytes<minimumFreeGiB*1024L*1024*1024)return false;
  if(maxGiB<=0)return true;
  long used=0;foreach(var file in new DirectoryInfo(root).EnumerateFiles("*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint})){token.ThrowIfCancellationRequested();used+=file.Length;}
  return used+bytes<=maxGiB*1024L*1024*1024;
 },token);
 public Task<WorkshopFileCheck> Validate(string id,WorkshopLocalState local,CancellationToken token)=>Task.Run(()=>WorkshopPackageCheck.Read(root,id,local.Folder,token),token);
 public void Dispose(){callback?.Dispose();callback=null;completed=null;}
}

public static class WorkshopPackageCheck
{
 public static WorkshopFileCheck Read(string root,string id,string installedFolder,CancellationToken token)
 {
  string expected=Path.GetFullPath(Path.Combine(root,id));
  if(MapPolicy.WorkshopId(id)!=id||Path.GetFullPath(installedFolder)!=expected)return new(false,"","Unerwarteter Workshop-Pfad");
  var directory=new DirectoryInfo(expected);if(!directory.Exists||directory.Attributes.HasFlag(FileAttributes.ReparsePoint))return new(false,"","Workshop-Verzeichnis fehlt oder ist verlinkt");
  foreach(var file in directory.EnumerateFiles("*.vpk"))
  {
   token.ThrowIfCancellationRequested();if(file.Attributes.HasFlag(FileAttributes.ReparsePoint))continue;
   long length=file.Length,stamp=file.LastWriteTimeUtc.Ticks;
   using var stream=file.OpenRead();using var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
   if(stream.Length<12||reader.ReadUInt32()!=0x55aa1234)continue;
   uint version=reader.ReadUInt32(),tree=reader.ReadUInt32();long required=12L+tree;
   if(version==2){if(stream.Length<28)continue;required+=16;for(int i=0;i<4;i++)required+=reader.ReadUInt32();}
   if(version is not (1 or 2)||required>length)continue;
   token.ThrowIfCancellationRequested();stream.Position=0;
   var archives=new Dictionary<string,(long Length,long Stamp)>();
   void CheckArchive(ushort index,uint offset,uint bytes)
   {
    token.ThrowIfCancellationRequested();if(bytes==0)return;
    if(index==0x7fff){if((long)(version==2?28:12)+tree+offset+bytes>length)throw new InvalidDataException("VPK-Daten abgeschnitten");return;}
    if(!file.Name.EndsWith("_dir.vpk",StringComparison.Ordinal))throw new InvalidDataException("Externe VPK ohne Verzeichnisdatei");
    string path=Path.Combine(directory.FullName,file.Name[..^8]+"_"+index.ToString("D3")+".vpk");
    if(!archives.TryGetValue(path,out var data)){var part=new FileInfo(path);if(!part.Exists||part.LinkTarget is not null)throw new InvalidDataException("VPK-Teil fehlt");data=(part.Length,part.LastWriteTimeUtc.Ticks);archives[path]=data;}
    if((long)offset+bytes>data.Length)throw new InvalidDataException("VPK-Teil unvollständig");
   }
   var names=VpkMaps.Read(stream,CheckArchive);
   foreach(var (path,data) in archives){var part=new FileInfo(path);if(!part.Exists||part.Length!=data.Length||part.LastWriteTimeUtc.Ticks!=data.Stamp)return new(false,"","VPK-Teil wurde während der Prüfung verändert");}
   file.Refresh();
   if(file.Length!=length||file.LastWriteTimeUtc.Ticks!=stamp)return new(false,"","Paket wurde während der Prüfung verändert");
   if(names.Count>0)return new(true,names.Order().First(),"");
  }
  return new(false,"","Keine vollständige VPK mit Map gefunden");
 }
}
