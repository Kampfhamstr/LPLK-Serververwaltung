using System.Collections.Concurrent;
using System.Runtime.InteropServices;
namespace LPLK_Demos;

// Only this bounded tmpfs is a valid recording target. No disk fallback.
public static class RamMount
{
 public const long Limit=2L*1024*1024*1024, Reserve=256L*1024*1024;
 [DllImport("libc",SetLastError=true)]private static extern int statvfs(string path,IntPtr buffer);
 public static (bool Ready,long Free,string? Error) Inspect(string directory)
 {
  if(!OperatingSystem.IsLinux())return(false,0,"RAM-Aufnahme benötigt Linux-tmpfs");
  try{
   string path=Path.GetFullPath(directory);
   var mounts=File.ReadLines("/proc/self/mountinfo").Select(x=>x.Split(' '));
   bool found=mounts.Any(x=>x.Length>9&&x[4].Replace("\\040"," ")==path&&Array.IndexOf(x,"-")is int i&&i>0&&i+2<x.Length&&x[i+1]=="tmpfs"&&x[i+2]=="lplk-demo-ram");
   if(!found)return(false,0,"Begrenzter RAM-Speicher nicht eingebunden; Aufnahme pausiert");
   // The host controller sets the container's cgroup v2 swap allowance to zero.
   // This also works on the installed kernel 6.1, which has no tmpfs noswap option.
   if(!File.Exists("/sys/fs/cgroup/memory.swap.max")||File.ReadAllText("/sys/fs/cgroup/memory.swap.max").Trim()!="0")return(false,0,"RAM-Swap-Schutz fehlt; Aufnahme pausiert");
   IntPtr data=Marshal.AllocHGlobal(256);
   try{if(statvfs(path,data)!=0)return(false,0,"RAM-Belegung nicht lesbar");long unit=Marshal.ReadInt64(data,8),total=checked(unit*Marshal.ReadInt64(data,16)),free=checked(unit*Marshal.ReadInt64(data,32));
    if(total>Limit||total<Limit-1048576)return(false,free,"RAM-Limit entspricht nicht 2 GiB");return(true,free,null);
   }finally{Marshal.FreeHGlobal(data);}
  }catch(Exception e)when(e is IOException or UnauthorizedAccessException or OverflowException){return(false,0,"RAM-Speicher nicht verfügbar");}
 }
}

public sealed record DemoExportResult(string File,long Bytes,string? Error,DemoContents? Contents=null);
public sealed class RamExporter(string source,string destination,Func<bool>? canWrite=null)
{
 private readonly ConcurrentDictionary<string,byte> pending=new();
 private readonly ConcurrentQueue<DemoExportResult> results=new();
 private readonly SemaphoreSlim serial=new(1,1);
 private readonly object gate=new();private readonly List<Task> workers=[];
 public int Pending=>pending.Count;
 public bool TryResult(out DemoExportResult? result)=>results.TryDequeue(out result);
 public void Enqueue(string file,long earliest,double expectedSeconds=0)
 {
  if(!DemoArchive.OwnedName(file))throw new InvalidDataException("Fremder RAM-Demopfad");
  if(!pending.TryAdd(file,0))return;
  var task=Task.Run(async()=>{
   try{
    long wait=earliest-Environment.TickCount64;if(wait>0)await Task.Delay((int)Math.Min(int.MaxValue,wait)).ConfigureAwait(false);
    await serial.WaitAsync().ConfigureAwait(false);
    try{var saved=await CopyClosed(file,expectedSeconds).ConfigureAwait(false);results.Enqueue(new(file,saved.Bytes,null,saved.Contents));}
    finally{serial.Release();}
   }catch(Exception e){results.Enqueue(new(file,0,e.Message));}
   finally{pending.TryRemove(file,out _);}
  });
  lock(gate){workers.RemoveAll(x=>x.IsCompleted);workers.Add(task);}
 }
 private async Task<(long Bytes,DemoContents Contents)> CopyClosed(string file,double expectedSeconds)
 {
  string from=SafePath(source,file),to=SafePath(destination,file),temporary=to+".incoming";
  if(!File.Exists(from)&&File.Exists(to)){using var existing=File.OpenRead(to);var checkedContents=DemoContents.Read(existing);checkedContents.Verify(expectedSeconds);return(existing.Length,checkedContents);}
  if(canWrite is not null&&!canWrite())throw new IOException("Archivreserve unterschritten; Demo bleibt im RAM");
  long length=new FileInfo(from).Length;DateTime stamp=File.GetLastWriteTimeUtc(from);
  if(length<16)throw new IOException("Demo noch nicht vollständig");
  // A stopped engine recorder releases its exclusive lock. Also check size/mtime
  // across the whole transfer before publishing, so a growing file stays in RAM.
  DemoContents contents;
  using(var input=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read,1048576,FileOptions.Asynchronous|FileOptions.SequentialScan)){
   contents=DemoContents.Read(input);contents.Verify(expectedSeconds);input.Position=0;
   Directory.CreateDirectory(destination);
   try{
    if(File.Exists(temporary))File.Delete(temporary);
    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1048576,FileOptions.Asynchronous|FileOptions.SequentialScan)){
     await input.CopyToAsync(output).ConfigureAwait(false);await output.FlushAsync().ConfigureAwait(false);output.Flush(true);
    }
    if(input.Length!=length||new FileInfo(temporary).Length!=length||File.GetLastWriteTimeUtc(from)!=stamp)throw new IOException("Demo wird noch geschrieben; RAM-Datei erhalten");
    if(File.Exists(to)){
     if(new FileInfo(to).Length!=length||!await SameFile(temporary,to).ConfigureAwait(false))throw new IOException("Archivkonflikt; beide Dateien erhalten");
     File.Delete(temporary);
    }else File.Move(temporary,to);
   }catch{if(File.Exists(temporary))File.Delete(temporary);throw;}
  }
  File.Delete(from);return(length,contents);
 }
 private static async Task<bool> SameFile(string a,string b)
 {
  using var left=File.OpenRead(a);using var right=File.OpenRead(b);
  var lh=await System.Security.Cryptography.SHA256.HashDataAsync(left);var rh=await System.Security.Cryptography.SHA256.HashDataAsync(right);return lh.AsSpan().SequenceEqual(rh);
 }
 public static string SafePath(string root,string file)
 {
  if(!DemoArchive.OwnedName(file))throw new InvalidDataException("Fremder Demopfad");
  // Called by disk workers; do not consult a stale gameplay metadata cache.
  string full=Path.GetFullPath(root);for(var d=new DirectoryInfo(full);d is not null;d=d.Parent)
   if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Verlinkter Demopfad");
  string path=Path.Combine(full,file);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Verlinkte Demo");return path;
 }
 public void Drain(){Task[] tasks;lock(gate)tasks=workers.ToArray();Task.WhenAll(tasks).GetAwaiter().GetResult();}
}
