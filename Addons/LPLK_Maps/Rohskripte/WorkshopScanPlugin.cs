using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
namespace LPLK_Maps;
public sealed partial class Plugin
{
    private readonly WorkshopScanner scanner=new();
    private Task<WorkshopScanResult>? scanTask;
    private bool scanAgain;
    private long nextScan;
    private string scanSettings="";
    private double scanMilliseconds;
    private int scanVpkReads;
    private string? scanError;
    private void Scan()=>StartScan(true);
    private void RescanForVote()=>StartScan(false);
    private void StartScan(bool force)
    {
        if(scanTask is not null){if(force)scanAgain=true;return;}
        if(!force&&Environment.TickCount64<nextScan)return;
        nextScan=Environment.TickCount64+60000;
        var snapshot=maps.ToArray();string root=WorkshopRoot,game=LPLK.Common.RoundFileStore.GamePath(Core);var excluded=settings.ExcludedWorkshopIds.ToArray();
        scanSettings=System.Text.Json.JsonSerializer.Serialize(settings);
        scanTask=LPLK.Common.RoundFileStore.RunAfterRound(()=>{var timer=System.Diagnostics.Stopwatch.StartNew();var result=scanner.Read(game,root,snapshot,excluded);scanMilliseconds=timer.Elapsed.TotalMilliseconds;return result;});
    }
    [EventListener<EventDelegates.OnWorldUpdate>]
    public void CompleteWorkshopScan()
    {
        if(module?.Active!=true||scanTask?.IsCompleted!=true)return;
        var completed=scanTask;scanTask=null;
        try
        {
            var result=completed.GetAwaiter().GetResult();
            if(scanSettings!=System.Text.Json.JsonSerializer.Serialize(settings)){scanAgain=true;return;}
            // Merge only discovered file metadata into the current catalog. A
            // measurement/approval/admin edit made during the scan wins.
            var next=maps.ToDictionary(m=>m.Id);bool changed=false;
            foreach(var (id,source) in result.Sources)
            {
                if(settings.ExcludedWorkshopIds.Contains(id))continue;
                if(!next.TryGetValue(id,out var map))map=new(){Id=id,Name=source,InPool=false,MaxPlayers=128};
                var updated=map with{IsMap=true,SourceFolder=source};
                if(updated!=map||!next.ContainsKey(id)){next[id]=updated;changed=true;}
            }
            if(changed)SaveCatalog(next.Values.ToList());
            installed=result.Installed;scanVpkReads=result.VpkReads;scanError=null;PrepareStartupMap();WriteStatus();
        }
        catch(Exception e){scanError=e.GetType().Name;Core.Logger.LogWarning(e,"LPLK_Maps: Hintergrundscan fehlgeschlagen; letzter Katalog bleibt erhalten.");}
        finally{if(scanAgain){scanAgain=false;StartScan(true);}}
    }
    private void SaveCatalog(List<MapEntry> next)
    {
        MapPolicy.Validate(next);LPLK.Common.DeferredJson.Queue(MapsPath,next);maps=next;PrepareStartupMap();
    }
}
