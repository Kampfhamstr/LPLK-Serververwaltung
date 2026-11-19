using System.Collections.Concurrent;

namespace LPLK_Maps;

public sealed record WorkshopLocalState(bool Installed,bool NeedsUpdate,bool Downloading,long Revision,ulong Bytes,string Folder,ulong Done=0,ulong Total=0);
public sealed record WorkshopFileCheck(bool Valid,string Source,string Error);
public sealed record WorkshopCheck(string Id,string Name,string State="pending",long RemoteRevision=0,long InstalledRevision=0,DateTimeOffset? CheckedAt=null,DateTimeOffset? UpdatedAt=null,ulong Done=0,ulong Total=0,string Error="",string Source="");
public interface IWorkshopMaintenanceBackend:IDisposable
{
 bool Connected {get;}
 Task<WorkshopItem> Fetch(string id,CancellationToken token);
 WorkshopLocalState Read(string id);
 (ulong Done,ulong Total) Progress(string id);
 bool Download(string id,Action<string,bool,string> done);
 void Suspend(bool paused);
 Task<WorkshopFileCheck> Validate(string id,WorkshopLocalState local,CancellationToken token);
 Task<bool> Reserve(long bytes,CancellationToken token);
}

// One serialized maintenance job. Network and file work return immutable results;
// Steam controls are called only while idle, apart from the one pause on join.
// A result never requests a map change: the caller owns the current selection.
public sealed class WorkshopMaintenance(IWorkshopMaintenanceBackend backend):IDisposable
{
 private sealed class Job(string id,long now)
 {
  public string Id=id;public long ActiveSince=now,Elapsed;public Task<WorkshopItem>? Metadata;
  public Task<bool>? Reserve;public Task<WorkshopFileCheck>? Files;public WorkshopLocalState? Local;
  public bool Requested,Confirmed,Updated;public string? Failure;
  public CancellationTokenSource Cancellation=new();
 }
 private readonly Dictionary<string,WorkshopCheck> results=[];
 private readonly ConcurrentQueue<(string Id,bool Ok,string Error)> completed=[];
 private Job? job;private bool suspended,disposed;private long lastTick;
 public string State {get;private set;}="waiting";
 public string ActiveId=>job?.Id??"";
 public long Revision {get;private set;}
 public IReadOnlyCollection<WorkshopCheck> Results=>results.Values;
 public bool Ready(string id,long remoteRevision,DateTimeOffset now)=>results.TryGetValue(id,out var r)&&r.State is "current" or "updated"&&r.RemoteRevision>=remoteRevision&&r.InstalledRevision>=r.RemoteRevision&&r.CheckedAt is {} at&&now-at<TimeSpan.FromHours(2);
 public bool Failed(string id)=>results.TryGetValue(id,out var r)&&r.State=="failed";
 private void Put(WorkshopCheck r){if(!results.TryGetValue(r.Id,out var old)||old!=r){results[r.Id]=r;Revision++;}}
 public void Restore(IEnumerable<WorkshopCheck> saved){foreach(var r in saved.Take(2000))if(MapPolicy.WorkshopId(r.Id)==r.Id)Put(r with{State="pending"});}
 public void Tick(long now,DateTimeOffset utc,IReadOnlyList<MapEntry> catalog,string current,string priority,bool humans,bool enabled,bool externallyBusy)
 {
  if(disposed)return;long delta=lastTick==0?0:Math.Clamp(now-lastTick,0,2000);lastTick=now;
  var valid=catalog.Where(m=>m.Workshop&&m.InPool&&m.Enabled&&!m.Removed&&m.IsMap).ToDictionary(m=>m.Id);
  foreach(var m in valid.Values)if(!results.ContainsKey(m.Id))Put(new(m.Id,m.Name));
  foreach(string id in results.Keys.Where(id=>!valid.ContainsKey(id)).ToArray()){results.Remove(id);Revision++;}
  if(humans||!enabled||externallyBusy)
  {
   State=humans?"paused_players":!enabled?"disabled":"paused_manual";
   job?.Cancellation.Cancel();
   if(job?.Requested==true&&!suspended&&!externallyBusy){backend.Suspend(true);suspended=true;}
   return;
  }
  if(suspended){backend.Suspend(false);suspended=false;}
  if(!backend.Connected){State="waiting_steam";return;}
  while(completed.TryDequeue(out var c))if(job is {} active&&active.Id==c.Id&&active.Requested){active.Confirmed=c.Ok;active.Failure=c.Ok?null:"Steam: "+c.Error;}
  if(job is {} obsolete&&(!valid.ContainsKey(obsolete.Id)||obsolete.Id==current))
  {obsolete.Cancellation.Cancel();obsolete.Cancellation.Dispose();job=null;}
  if(job is null)
  {
   var candidates=valid.Values.Where(m=>m.Id!=current).OrderByDescending(m=>m.Id==priority).ThenBy(m=>results[m.Id].CheckedAt??DateTimeOffset.MinValue);
   var choice=candidates.FirstOrDefault(m=>results[m.Id].State=="pending"||results[m.Id].CheckedAt is not {} at||utc-at>TimeSpan.FromMinutes(m.Id==priority&&results[m.Id].State=="failed"?15:30)||results[m.Id].RemoteRevision<m.WorkshopUpdated);
   if(choice is null){State="idle";return;}
   job=new(choice.Id,now);Put(results[choice.Id] with{State="checking",Error="",Name=choice.Name});
   var initialToken=job.Cancellation.Token;job.Metadata=Task.Run(()=>backend.Fetch(choice.Id,initialToken));State="checking";return;
  }
  var j=job;j.Elapsed+=delta;
  if(j.Cancellation.IsCancellationRequested)
  {
   // Never reuse a canceled/incomplete filesystem check after somebody joins.
   j.Cancellation.Dispose();j.Cancellation=new();j.Files=null;
   if(j.Metadata?.IsCompletedSuccessfully!=true){var token=j.Cancellation.Token;j.Metadata=Task.Run(()=>backend.Fetch(j.Id,token));}
   if(j.Reserve?.IsCompletedSuccessfully!=true)j.Reserve=null;
  }
  try
  {
   if(j.Elapsed>1800000)throw new IOException("Zeitlimit der Workshop-Vorbereitung");
   if(j.Failure is not null)throw new IOException(j.Failure);
   if(j.Metadata?.IsCompleted!=true)return;
   var item=j.Metadata.GetAwaiter().GetResult();
   if(item.Id!=j.Id||!item.IsMap||item.Updated<=0)throw new InvalidDataException("Keine bestätigte CS2-Maprevision");
   var record=results[j.Id];Put(record with{RemoteRevision=item.Updated,Name=item.Name});
   // Steam's callback is required before reading the installed package again.
   if(j.Requested&&!j.Confirmed){var progress=backend.Progress(j.Id);Put(results[j.Id] with{Done=progress.Done,Total=progress.Total});State="downloading";return;}
   var local=backend.Read(j.Id);j.Local=local;
   Put(results[j.Id] with{InstalledRevision=local.Revision,Done=local.Done,Total=local.Total});
   if(!local.Installed||local.NeedsUpdate||local.Downloading||local.Revision<item.Updated)
   {
    if(j.Confirmed)throw new IOException("Steam meldet nach Download noch keine aktuelle Installation");
    j.Reserve??=backend.Reserve(Math.Max(item.Bytes,512L*1024*1024),j.Cancellation.Token);
    if(!j.Reserve.IsCompleted)return;
    if(!j.Reserve.GetAwaiter().GetResult())throw new IOException("Freie Speicherreserve reicht nicht");
    j.Requested=true;j.Updated=true;
    if(!backend.Download(j.Id,(id,ok,error)=>completed.Enqueue((id,ok,error))))throw new IOException("Steam hat den Download nicht angenommen");
    Put(results[j.Id] with{State="downloading"});State="downloading";return;
   }
   j.Files??=backend.Validate(j.Id,local,j.Cancellation.Token);
   if(!j.Files.IsCompleted){Put(results[j.Id] with{State="checking_files"});State="checking_files";return;}
   var check=j.Files.GetAwaiter().GetResult();if(!check.Valid)throw new InvalidDataException(check.Error);
   Put(results[j.Id] with{State=j.Updated?"updated":"current",CheckedAt=utc,UpdatedAt=j.Updated?utc:results[j.Id].UpdatedAt,Error="",Source=check.Source});
   j.Cancellation.Dispose();job=null;State="idle";
  }
  catch(OperationCanceledException){j.Cancellation.Cancel();State="paused_players";}
  catch(Exception e){Put(results[j.Id] with{State="failed",CheckedAt=utc,Error=e is IOException or InvalidDataException?e.Message:e.GetType().Name});j.Cancellation.Dispose();job=null;State="failed";}
 }
 public void Dispose(){disposed=true;job?.Cancellation.Cancel();if(suspended)backend.Suspend(false);backend.Dispose();job?.Cancellation.Dispose();}
}
