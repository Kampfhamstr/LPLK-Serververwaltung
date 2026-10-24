using SwiftlyS2.Shared.SteamAPI;

namespace LPLK_Maps;

// The transport boundary keeps the full asynchronous workflow testable without
// loading a native game server. Only this implementation calls Steam.
public interface IWorkshopTestBackend : IDisposable
{
    bool Connected { get; }
    Task<WorkshopItem> Fetch(string id,CancellationToken cancellation);
    WorkshopTransfer Read(string id);
    bool Download(string id,Action<string,bool,string> completed);
}

internal sealed class SteamWorkshopTestBackend : IWorkshopTestBackend
{
    private Callback<DownloadItemResult_t>? callback;
    private Action<string,bool,string>? completed;
    public bool Connected=>SteamGameServer.BLoggedOn();
    public async Task<WorkshopItem> Fetch(string id,CancellationToken cancellation)=>(await WorkshopDetails.Fetch([id],cancellation)).Single();
    public WorkshopTransfer Read(string id)
    {
        var file=new PublishedFileId_t(ulong.Parse(id));var state=(EItemState)SteamGameServerUGC.GetItemState(file);
        SteamGameServerUGC.GetItemDownloadInfo(file,out ulong done,out ulong total);
        bool installed=state.HasFlag(EItemState.k_EItemStateInstalled)&&SteamGameServerUGC.GetItemInstallInfo(file,out ulong size,out _,4096,out _)&&size>0;
        return new(installed,state.HasFlag(EItemState.k_EItemStateNeedsUpdate),state.HasFlag(EItemState.k_EItemStateDownloading)||state.HasFlag(EItemState.k_EItemStateDownloadPending),done,total);
    }
    public bool Download(string id,Action<string,bool,string> done)
    {
        completed=done;
        callback??=Callback<DownloadItemResult_t>.Create(result=>completed?.Invoke(result.m_nPublishedFileId.m_PublishedFileId.ToString(),result.m_eResult==EResult.k_EResultOK,result.m_eResult.ToString()));
        // Calling DownloadItem for an existing transfer also ensures Steam sends
        // our completion callback; it does not authorize a map change.
        return SteamGameServerUGC.DownloadItem(new PublishedFileId_t(ulong.Parse(id)),false);
    }
    public void Dispose(){callback?.Dispose();callback=null;completed=null;}
}
