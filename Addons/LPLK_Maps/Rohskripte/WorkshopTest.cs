using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using LPLK.Common;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SteamAPI;
using SwiftlyS2.Shared.Menus;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace LPLK_Maps;

public sealed record WorkshopTransfer(bool Installed,bool Updating,bool Downloading,ulong Done,ulong Total);
public static class WorkshopTestPolicy
{
    public static bool Complete(WorkshopTransfer state,bool confirmed,bool vpk)=>confirmed&&state.Installed&&!state.Updating&&!state.Downloading&&vpk;
}

public sealed partial class Plugin
{
    private sealed class TestDownload(string id,IPlayer? owner)
    {
        public readonly string Id=id;
        public readonly long Started=Environment.TickCount64;
        public string Name=id,Status="Workshop wird geprüft";
        public bool Confirmed,Ready,Failed;
        public ulong Done,Total;
        public readonly int Slot=owner?.PlayerID??-1;
        public readonly ulong Steam=owner?.SteamID??0,Session=owner?.SessionId??0;
        public IMenuAPI? Menu;
        public IMenuOption? StatusOption;
        public bool Notified;
    }
    private TestDownload? testDownload;
    private CancellationTokenSource? testDownloadCancellation;
    private IWorkshopTestBackend workshopBackend=new SteamWorkshopTestBackend();
    private long nextDownloadPoll;

    private IPlayer? TestDownloadOwner(TestDownload job)
    {if(job.Slot<0)return null;var p=Core.PlayerManager.GetPlayer(job.Slot);return p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&p.SteamID==job.Steam&&p.SessionId==job.Session?p:null;}
    [Command("lplk_maptest")]
    public void WorkshopTestCommand(ICommandContext context)
    {
        RefreshSettings();if(context.Sender is {} actor&&!module.CanAdmin(actor)){context.ReplyLplk("[LPLK] Keine Berechtigung.");return;}
        try
        {
            if(context.Args is ["download",var id]){StartWorkshopTest(context.Sender,id);context.ReplyLplk("[LPLK] Maptest angefordert: "+id);return;}
            if(context.Args is ["start",var startId]&&testDownload is {} ready&&ready.Id==startId)
            {if(context.Sender is {} player)ConfirmWorkshopTest(player,ready);else StartTestMap(ready);return;}
            if(context.Args.Length==0&&context.Sender is {} p){MapTestMenu(p);return;}
            if(context.Args is ["status"]){PollWorkshopTest(Environment.TickCount64,true);context.ReplyLplk(testDownload is {} job?$"LPLK_Maptest: id={job.Id}; ready={job.Ready}; failed={job.Failed}; confirmed={job.Confirmed}; bytes={job.Done}/{job.Total}; {job.Status}":"LPLK_Maptest: kein Auftrag");return;}
            context.ReplyLplk("lplk_maptest download <Workshop-ID> | status | start <Workshop-ID>");
        }
        catch(InvalidDataException e){context.ReplyLplk("[LPLK] "+e.Message);}
    }
    private readonly Dictionary<string,Task<bool>> fileReadiness=[];
    private bool TestMapFileReady(string id)
    {
        if(!fileReadiness.TryGetValue(id,out var read)){string root=WorkshopRoot;fileReadiness[id]=read=LPLK.Common.RoundFileStore.RunAfterRound(()=>ReadTestMapFile(root,id));}
        if(!read.IsCompleted)return false;
        bool ready=read.GetAwaiter().GetResult();if(!ready)fileReadiness.Remove(id);return ready;
    }
    private static bool ReadTestMapFile(string root,string id)
    {
        try
        {
            var directory=new DirectoryInfo(Path.Combine(root,id));
            if(!directory.Exists||directory.Attributes.HasFlag(FileAttributes.ReparsePoint))return false;
            foreach(var file in directory.EnumerateFiles("*.vpk"))
            {
                if(file.Attributes.HasFlag(FileAttributes.ReparsePoint))continue;
                try{using var stream=file.OpenRead();if(VpkMaps.Read(stream).Count>0)return true;}
                catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){}
            }
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        return false;
    }
    private void WorkshopTestInput(IPlayer p)=>Input(p,"Workshop-ID",21,v=>v.Length<=20&&MapPolicy.WorkshopId(v)==v.Trim(),StartWorkshopTest);
    private void StartWorkshopTest(IPlayer? p,string value)
    {
        RefreshSettings();if(p is not null&&!module.CanAdmin(p))return;
        string id=MapPolicy.WorkshopId(value)??throw new InvalidDataException("Ungültige Workshop-ID.");
        if(settings.ExcludedWorkshopIds.Contains(id))throw new InvalidDataException("Dieses Workshop-Item ist gesperrt.");
        if(testDownload is {Ready:false,Failed:false})throw new InvalidDataException("Ein Mapdownload läuft bereits. Bitte dessen Status öffnen.");
        testDownloadCancellation?.Cancel();testDownloadCancellation?.Dispose();testDownloadCancellation=new(TimeSpan.FromSeconds(30));
        var token=testDownloadCancellation.Token;var job=new TestDownload(id,p);testDownload=job;
        WriteTestDownload();if(p is not null)ShowWorkshopTest(p);
        string diskRoot=WorkshopRoot,diskDrive=Path.GetPathRoot(RoundFileStore.GamePath(Core))!;bool bounded=settings.MaxWorkshopGiB>0;
        _=Task.Run(async()=>
        {
            (long Used,long Free) space=default;
            WorkshopItem? item=null;string? error=null;
            try{item=await workshopBackend.Fetch(id,token);if(item.Id!=id||!item.IsMap)throw new InvalidDataException("Das Item ist keine CS2-Map.");space=await LPLK.Common.RoundFileStore.RunAfterRound(()=>ReadWorkshopSpace(diskRoot,diskDrive,bounded));}
            catch(Exception e){error=e is InvalidDataException?e.Message:"Workshop-Abfrage fehlgeschlagen: "+e.GetType().Name;}
            if(!module.Active)return;
            Core.Scheduler.NextWorldUpdate(()=>
            {
                if(!module.Active||!ReferenceEquals(testDownload,job))return;
                try
                {
                    RefreshSettings();if(error is not null||item is null)throw new InvalidDataException(error??"Workshop-Antwort fehlt.");
                    if(job.Slot>=0&&(TestDownloadOwner(job) is not {} actor||!module.CanAdmin(actor)))throw new InvalidDataException("Download abgebrochen: Verbindung oder Berechtigung geändert.");
                    if(settings.ExcludedWorkshopIds.Contains(id))throw new InvalidDataException("Dieses Workshop-Item ist inzwischen gesperrt.");
                    if(!workshopBackend.Connected)throw new InvalidDataException("Steam ist noch nicht verbunden. Später erneut versuchen.");
                    var map=maps.FirstOrDefault(m=>m.Id==id)??new MapEntry{Id=id,Name=item.Name,InPool=false,Enabled=false,MaxPlayers=128};
                    if(map.Removed)throw new InvalidDataException("Die Karte wurde manuell gesperrt.");
                    map=map with{IsMap=true,RemoteMapVerified=true,DownloadBytes=item.Bytes,WorkshopUpdated=item.Updated};
                    diskReserve=Task.FromResult(space);diskReserveAt=Environment.TickCount64;
                    if(!EnoughDisk(map))throw new InvalidDataException(poolMessage);
                    var next=maps.Where(m=>m.Id!=id).Append(map).ToList();SaveCatalog(next);
                    job.Name=item.Name;
                    var state=workshopBackend.Read(id);
                    if(state.Installed&&!state.Updating&&!state.Downloading){job.Confirmed=true;Scan();}
                    else
                    {
                        if(!workshopBackend.Download(id,(downloadId,success,outcome)=>WorkshopTestDownloaded(job,downloadId,success,outcome)))throw new InvalidDataException("Steam hat den Download nicht angenommen.");
                    }
                    job.Status="Download läuft";PollWorkshopTest(Environment.TickCount64,true);
                }
                catch(Exception e){job.Status=e.Message;job.Failed=true;WriteTestDownload();}
            });
        });
    }
    private void WorkshopTestDownloaded(TestDownload expected,string id,bool success,string outcome)
    {
        Core.Scheduler.NextWorldUpdate(()=>
        {
            if(!module.Active||testDownload is not {} job||!ReferenceEquals(expected,job)||job.Id!=id||job.Failed)return;
            if(!success){job.Status="Steam-Download fehlgeschlagen: "+outcome;job.Failed=true;WriteTestDownload();return;}
            job.Confirmed=true;PollWorkshopTest(Environment.TickCount64,true);
        });
    }
    private void PollWorkshopTest(long now,bool force=false)
    {
        if(testDownload is not {} job||job.Failed||job.Ready||!force&&now<nextDownloadPoll||job.Status=="Workshop wird geprüft")return;
        nextDownloadPoll=now+2000;
        try
        {
            if(now-job.Started>30*60_000){job.Failed=true;job.Status="Download nach 30 Minuten nicht bestätigt. Status prüfen und erneut versuchen.";WriteTestDownload();return;}
            if(!workshopBackend.Connected){job.Status="Warte auf Steam-Verbindung";WriteTestDownload();return;}
            var state=workshopBackend.Read(job.Id);job.Done=state.Done;job.Total=state.Total;
            if(job.Confirmed&&state.Installed&&!state.Updating&&!state.Downloading)Scan();
            job.Ready=WorkshopTestPolicy.Complete(state,job.Confirmed,TestMapFileReady(job.Id));
            job.Status=job.Ready?"Heruntergeladen · startbereit":state.Total>0?$"Download: {Math.Min(100,100m*state.Done/state.Total):0}%":"Download läuft · Bestätigung ausstehend";
            WriteTestDownload();
        }
        catch(Exception e){job.Failed=true;job.Status="Downloadprüfung fehlgeschlagen: "+e.GetType().Name;WriteTestDownload();}
    }
    private void ShowWorkshopTest(IPlayer p)
    {
        RefreshSettings();if(!module.CanAdmin(p))return;
        if(testDownload is not {} job){p.SendLplkChat("[LPLK] Noch kein Workshop-Maptest angefordert.");MapTestMenu(p);return;}
        PollWorkshopTest(Environment.TickCount64,true);
        var actions=new List<(string,Action<IPlayer>)>{("Status: "+job.Status,a=>{a.SendLplkChat($"[LPLK] {job.Name} ({job.Id}): {job.Status}");ShowWorkshopTest(a);})};
        if(job.Ready)actions.Add(("Starten",a=>ConfirmWorkshopTest(a,job)));
        if(job.Failed)actions.Add(("Erneut versuchen",a=>StartWorkshopTest(a,job.Id)));
        actions.Add(("Zurück",MapTestMenu));module.Menu(p,"Maptest · "+MapPolicy.Escape(job.Name),1,actions.ToArray());
        if(p.PlayerID==job.Slot&&p.SteamID==job.Steam&&p.SessionId==job.Session){job.Menu=Core.MenusAPI.GetCurrentMenu(p);job.StatusOption=job.Menu?.Options.FirstOrDefault();}
    }
    private void ConfirmWorkshopTest(IPlayer p,TestDownload job)
    {
        if(!module.CanAdmin(p)||!ReferenceEquals(testDownload,job)||!job.Ready)return;
        module.Menu(p,"Map jetzt starten? · "+MapPolicy.Escape(job.Name),1,("Jetzt starten",a=>
        {
            RefreshSettings();if(!module.CanAdmin(a)||!ReferenceEquals(testDownload,job))return;
            try{StartTestMap(job);}catch(InvalidDataException e){a.SendLplkChat("[LPLK] "+e.Message);}
        }),("Später",ShowWorkshopTest));
    }
    private void StartTestMap(TestDownload job)
    {
        RefreshSettings();Scan();var map=maps.FirstOrDefault(m=>m.Id==job.Id);
        if(!module.Active||!ReferenceEquals(testDownload,job)||!job.Ready||job.Failed||map is null||map.Removed||settings.ExcludedWorkshopIds.Contains(job.Id)
            ||!workshopBackend.Connected||!WorkshopTestPolicy.Complete(workshopBackend.Read(job.Id),job.Confirmed,TestMapFileReady(job.Id)))
            throw new InvalidDataException("Download nicht mehr vollständig verfügbar. Status neu prüfen.");
        if(pending is not null)throw new InvalidDataException("Ein anderer Mapwechsel läuft bereits.");
        Schedule(map,settings.ChangeDelaySeconds,manualTest:true);
    }
    private void WriteTestDownload()
    {
        if(testDownload is not {} job)return;
        if(job.StatusOption is {} option)option.Text=new MenuNumbers().Next("Status: "+MapPolicy.Escape(job.Status));
        try{LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"workshop-test.json"),new{updatedAt=DateTimeOffset.UtcNow,id=job.Id,name=job.Name,status=job.Status,ready=job.Ready,failed=job.Failed,downloadedBytes=job.Done,totalBytes=job.Total});}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){Core.Logger.LogWarning(e,"Maptest-Statusdatei konnte nicht geschrieben werden.");}
        if((job.Ready||job.Failed)&&!job.Notified)
        {
            job.Notified=true;
            if(TestDownloadOwner(job) is {} p&&module.CanAdmin(p))
            {
                p.SendLplkChat($"[LPLK] Maptest {job.Name} ({job.Id}): {job.Status}."+(job.Ready?" Start über Maptest → Downloadstatus → Starten.":""));
                if(job.Ready)ConfirmWorkshopTest(p,job);
                else if(job.Menu is not null&&ReferenceEquals(Core.MenusAPI.GetCurrentMenu(p),job.Menu))ShowWorkshopTest(p);
            }
        }
    }
    private void StopWorkshopTest(){testDownloadCancellation?.Cancel();testDownloadCancellation?.Dispose();testDownloadCancellation=null;workshopBackend.Dispose();testDownload=null;}
}
