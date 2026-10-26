using System.Collections.Concurrent;
using System.Text;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;

namespace LPLK.Common;

// Per-plugin filesystem boundary. During play the engine thread only sees
// immutable byte/metadata snapshots. Disk reads, validation and commits belong
// to workers. Loading/unloading has an explicit lifecycle scope.
public static class RoundFileStore
{
    internal sealed record Snapshot(bool Exists,bool Directory,byte[]? Bytes,long Length,DateTime Stamp,FileAttributes Attributes,string? Link,string? Error=null,string? Text=null);
    private sealed class Entry
    {
        public Snapshot View=Missing, Baseline=Missing;
        public long Revision;
        public bool Dirty;
        public bool Known,MustNotExist;
    }
    private sealed record DirectoryQuery(string Path,string Pattern,SearchOption Search);
    private static readonly Snapshot Missing=new(false,false,null,0,DateTime.MinValue,0,null);
    private static readonly ConcurrentDictionary<string,Entry> entries=new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<DirectoryQuery,string[]> directories=new();
    private static readonly ConcurrentDictionary<string,string> moveDependencies=new(StringComparer.Ordinal);
    private static readonly object gate=new();
    private static readonly ConcurrentDictionary<string,byte> frozenConfigurations=new(StringComparer.Ordinal);
    private static readonly AsyncLocal<bool> physical=new();
    private static Timer? reader;
    private static int gameThread,reading;
    private static volatile bool humanPlay;private static long nextPresenceCheck;
    private static ISwiftlyCore? activeCore;
    private static object? activePlugin;
    private static Guid? healthCommand;
    private static string dataDirectory="",pluginDirectory="",gameDirectory="",configDirectory="";
    private static bool active,loading,stopping;
    private static Task writer=Task.CompletedTask;
    private static readonly ConcurrentQueue<Action> boundaryWork=new();
    private static long revision;
    private static long gameThreadDiskReads;
    private const long MaxRead=32L*1024*1024;
    public static bool Active=>active;
    public static bool OnGameThread=>active&&Environment.CurrentManagedThreadId==gameThread;
    internal static bool Direct=>!active||loading||physical.Value;
    public static string? LastError {get;private set;}
    public static long GameThreadDiskReads=>Interlocked.Read(ref gameThreadDiskReads);
    public static long Pending {get{lock(gate)return entries.Values.Count(x=>x.Dirty);}}

    public static void BeginLoad(ISwiftlyCore core,object? plugin=null)
    {
        gameThread=Environment.CurrentManagedThreadId;active=true;loading=true;stopping=false;
        activeCore=core;activePlugin=plugin;dataDirectory=core.PluginDataDirectory;pluginDirectory=core.PluginPath;gameDirectory=core.GameFilesDirectory;configDirectory=core.Configuration.GetConfigPath("");
        core.Scheduler.NextWorldUpdate(()=>gameThread=Environment.CurrentManagedThreadId);
        humanPlay=HasPlayers(core);
        core.GameEvent.HookPost<EventRoundStart>(_=>{humanPlay=HasPlayers(core);return HookResult.Continue;});
        core.GameEvent.HookPost<EventPlayerTeam>(e=>{if(e.UserIdPlayer is {IsValid:true,IsFakeClient:false})humanPlay=true;return HookResult.Continue;});
        core.GameEvent.HookPost<EventPlayerConnectFull>(e=>{if(e.UserIdPlayer is {IsValid:true,IsFakeClient:false})humanPlay=true;return HookResult.Continue;});
        core.Event.OnTick+=()=>{long now=Environment.TickCount64;if(now<nextPresenceCheck)return;nextPresenceCheck=now+1000;humanPlay=HasPlayers(core);};
        RoundData.Attach(core);
        // Lifecycle preload makes map-specific profiles available before their
        // first engine callback. No recursive workshop/VPK/demo scan here.
        foreach(string root in new[]{dataDirectory,Path.Combine(pluginDirectory,"resources"),configDirectory})PrimeTree(root);
        core.GameEvent.HookPost<EventRoundEnd>(_=>{core.Scheduler.DelayBySeconds(1,Flush);return HookResult.Continue;});
        string id=typeof(RoundFileStore).Assembly.GetName().Name!;
        healthCommand=core.Command.RegisterCommand("sw_lplk_io_"+id.ToLowerInvariant(),c=>{
            if(c.Sender is not null)return;
            string runtime=id=="SerververwaltungBridge"?"lplk.serververwaltung.bridge":id;
            var state=new Dictionary<string,object?>();
            foreach(string name in new[]{"errors","failure","reloadError","settingsError","surfSaveError","saveError","weaponExportError","ready","ramReady"})
                if(activePlugin?.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic) is {} field)state[name]=field.GetValue(activePlugin);
            object? plugins=null;
            if(id=="SerververwaltungBridge") {var statuses=core.PluginManager.GetAllPluginStatuses();plugins=core.PluginManager.GetAllPluginMetadata().Select(x=>new{id=x.Key,version=x.Value.Version,status=statuses.TryGetValue(x.Key,out var status)?status.ToString():"Unknown"}).ToArray();}
            c.Reply("LPLK_IO "+System.Text.Json.JsonSerializer.Serialize(new{id,version=core.PluginManager.GetPluginMetadata(runtime)?.Version,at=DateTimeOffset.UtcNow,active,policy="RAM; RoundEnd+1000ms",pending=Pending,error=LastError,gameThreadDiskReads=GameThreadDiskReads,configurationPolicy="load/reload only",runtimeInputPolling="RoundEnd+1s or idle; commands/web-requests/outbox",state,plugins}));
        },true,"");
    }
    private static bool HasPlayers(ISwiftlyCore core)=>core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient&&p.Controller is {IsValid:true,TeamNum:2 or 3});
    public static void EndLoad()
    {
        loading=false;
        reader=new Timer(_=>{if(!humanPlay&&!stopping){Flush();RoundData.FlushIdle();}},null,1000,1000);
    }
    // Test hook runs the exact runtime path without an engine or timers.
    public static void BeginMemoryForChecks(){gameThread=Environment.CurrentManagedThreadId;active=true;loading=false;stopping=false;}
    public static void PrimeForChecks(string path)=>Capture(Path.GetFullPath(path));
    public static void ResetForChecks(){reader?.Dispose();reader=null;active=false;loading=false;stopping=true;entries.Clear();frozenConfigurations.Clear();directories.Clear();moveDependencies.Clear();while(boundaryWork.TryDequeue(out _)){}LastError=null;gameThreadDiskReads=0;}
    public static Task RefreshForChecks()=>Task.Run(Refresh);
    public static Task FlushForChecks(){Flush();return writer;}
    private static void PrimeTree(string root)
    {
        root=Full(root);
        for(var dir=new System.IO.DirectoryInfo(root);dir is not null;dir=dir.Parent)Capture(dir.FullName,false);
        if(!System.IO.Directory.Exists(root))return;
        var options=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false,MaxRecursionDepth=10};
        long bytes=0;
        foreach(string path in System.IO.Directory.EnumerateFileSystemEntries(root,"*",options))
        {
            if(!IsText(path)&&!System.IO.Directory.Exists(path))continue;
            if(path.Split(Path.DirectorySeparatorChar).Any(x=>x is "web" or "plugin_backups" or "previous"))continue;
            var value=Capture(path);bytes+=value.Bytes?.LongLength??0;
            if(bytes>128L*1024*1024)throw new IOException("Konfigurationscache überschreitet 128 MiB");
        }
    }
    internal static string Full(string path)=>Path.GetFullPath(path);
    private static Snapshot ReadPhysical(string path,bool contents=true)
    {
        if(OnGameThread&&!loading&&!physical.Value){Interlocked.Increment(ref gameThreadDiskReads);throw new InvalidOperationException("Dateizugriff im Spielthread verhindert");}
        try
        {
            var f=new System.IO.FileInfo(path);
            if(f.LinkTarget is not null)return new(true,false,null,0,f.LastWriteTimeUtc,f.Attributes,f.LinkTarget,"Verlinkter Dateipfad");
            if(!f.Exists){var d=new System.IO.DirectoryInfo(path);return d.Exists?new(true,true,null,0,d.LastWriteTimeUtc,d.Attributes,d.LinkTarget):Missing;}
            byte[]? bytes=contents&&f.Length<=MaxRead?System.IO.File.ReadAllBytes(path):null;
            string? text=null;if(bytes is not null&&IsText(path)){using var reader=new StreamReader(new MemoryStream(bytes,false),Encoding.UTF8,true);text=reader.ReadToEnd();}
            return new(true,false,bytes,f.Length,f.LastWriteTimeUtc,f.Attributes,null,contents&&bytes is null?"Datei überschreitet den RAM-Dateicache":null,text);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){return Missing with{Error=e.GetType().Name};}
    }
    private static Snapshot Capture(string path,bool contents=true)
    {
        if(active&&!loading&&ConfigurationFrozen(path)&&entries.TryGetValue(path,out var cached))return cached.View;
        Snapshot fresh=ReadPhysical(path,contents);var entry=entries.GetOrAdd(path,_=>new());
        var prior=Volatile.Read(ref entry.View);if(Equal(prior,fresh)&&prior.Error==fresh.Error&&(!contents||prior.Bytes is not null))fresh=prior;
        lock(gate){if(!entry.Dirty){if(!contents&&entry.View.Bytes is not null&&entry.View.Length==fresh.Length&&entry.View.Stamp==fresh.Stamp)fresh=fresh with{Bytes=entry.View.Bytes};entry.Baseline=fresh;entry.Known=true;Volatile.Write(ref entry.View,fresh);}return entry.View;}
    }
    internal static Snapshot Read(string path,bool contents=true)
    {
        path=Full(path);
        if(Direct)return active?Capture(path,contents):ReadPhysical(path,contents);
        if(entries.TryGetValue(path,out var entry))
        {
            Snapshot view=Volatile.Read(ref entry.View);
            if(OnGameThread||entry.Dirty||ConfigurationFrozen(path))return view;
        }
        if(!OnGameThread)return ReadPhysical(path,contents);
        entries.TryAdd(path,new(){View=Missing with{Error="Datei wird im Hintergrund geladen"}});
        throw new IOException("Datei wird im Hintergrund geladen; erneut versuchen.");
    }
    internal static byte[] Bytes(string path)
    {
        Snapshot s=Read(path);
        if(s.Error is not null)throw new IOException(s.Error);
        if(!s.Exists||s.Directory)throw new FileNotFoundException("Datei fehlt",path);
        return s.Bytes??throw new IOException("Datei wird im Hintergrund geladen");
    }
    internal static string Text(string path){var s=Read(path);if(s.Error is not null)throw new IOException(s.Error);if(!s.Exists||s.Directory)throw new FileNotFoundException("Datei fehlt",path);if(s.Text is {} text)return text;using var r=new StreamReader(new MemoryStream(s.Bytes??throw new IOException("Datei wird im Hintergrund geladen"),false),Encoding.UTF8,true);return r.ReadToEnd();}
    internal static bool Exists(string path,bool directory)
    {
        try{var s=Read(path,false);return s.Exists&&s.Directory==directory;}catch(IOException){return false;}
    }
    internal static void Put(string path,byte[]? bytes,bool directory=false,bool mustNotExist=false)
    {
        path=Full(path);
        if(Direct)
        {
            if(directory)System.IO.Directory.CreateDirectory(path);
            else if(bytes is null)System.IO.File.Delete(path);
            else{System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);System.IO.File.WriteAllBytes(path,bytes);}
            if(active)Capture(path);return;
        }
        var e=entries.GetOrAdd(path,_=>new());
        lock(gate)
        {
            e.Revision=++revision;e.Dirty=true;e.MustNotExist|=mustNotExist;
            Volatile.Write(ref e.View,bytes is null&&!directory?Missing:new(true,directory,bytes,bytes?.LongLength??0,DateTime.UtcNow,directory?FileAttributes.Directory:FileAttributes.Normal,null));
        }
    }
    internal static string[] Enumerate(string path,string pattern,SearchOption search,bool? dirs)
    {
        path=Full(path);var query=new DirectoryQuery(path,pattern,search);
        if(!active||!OnGameThread&&!loading)
        {
            if(!System.IO.Directory.Exists(path))return [];
            return System.IO.Directory.EnumerateFileSystemEntries(path,pattern,new EnumerationOptions{RecurseSubdirectories=search==SearchOption.AllDirectories,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}).Where(p=>dirs is null||System.IO.Directory.Exists(p)==dirs).ToArray();
        }
        string[] values;
        if(Direct||!OnGameThread){values=Scan(query);if(active)directories[query]=values;}
        else if(!directories.TryGetValue(query,out values!)){directories.TryAdd(query,[]);values=[];}
        // Include unsaved files and hide consumed command files immediately.
        var merged=values.Concat(entries.Where(x=>x.Value.Dirty&&InDirectory(x.Key,path,search)&&System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern,Path.GetFileName(x.Key))).Select(x=>x.Key)).Distinct();
        return merged.Where(p=>{var s=entries.TryGetValue(p,out var e)?e.View:Read(p,false);return s.Exists&&(dirs is null||s.Directory==dirs);}).ToArray();
    }
    private static bool InDirectory(string file,string dir,SearchOption search)=>search==SearchOption.AllDirectories?file.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal):Path.GetDirectoryName(file)==dir;
    private static string[] Scan(DirectoryQuery q)
    {
        if(!System.IO.Directory.Exists(q.Path))return [];
        var options=new EnumerationOptions{RecurseSubdirectories=q.Search==SearchOption.AllDirectories,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false,MaxRecursionDepth=16};
        var paths=System.IO.Directory.EnumerateFileSystemEntries(q.Path,q.Pattern,options).Take(16385).ToArray();
        if(paths.Length>16384)throw new IOException("Zu viele Einträge im Dateicache");
        foreach(string p in paths)Capture(p,IsText(p));
        return paths;
    }
    private static bool IsText(string path)=>Path.GetExtension(path).ToLowerInvariant() is ".json" or ".jsonc" or ".txt" or ".cfg" or ".ini";
    private static void Refresh()
    {
        if(stopping)return;
        foreach(var item in entries.ToArray())if(!item.Value.Dirty&&!ConfigurationFrozen(item.Key)&&(!item.Value.Known||RuntimeInput(item.Key)))Capture(item.Key,IsText(item.Key)||item.Value.View.Bytes is not null);
        foreach(var q in directories.Keys.Where(q=>RuntimeInput(q.Path)))try{directories[q]=Scan(q);}catch(Exception e){LastError="Hintergrundlesen: "+e.GetType().Name;}
    }
    // External configuration is a load-time snapshot. Explicit menu edits still
    // update this RAM snapshot and persist at the next round boundary.
    public static void FreezeConfiguration(string path)=>frozenConfigurations[Full(path)]=0;
    public static bool ConfigurationFrozen(string path)
    {
        string p=Full(path).Replace('\\','/');
        return frozenConfigurations.ContainsKey(Full(path))||p.Contains("/addons/swiftlys2/configs/",StringComparison.OrdinalIgnoreCase)
            ||p.EndsWith("/settings.json",StringComparison.OrdinalIgnoreCase)||p.EndsWith("/fastlive.json",StringComparison.OrdinalIgnoreCase);
    }
    // Only live inboxes are polled. Our own outputs, histories, indexes and
    // databases are not repeatedly read back from disk during gameplay.
    public static bool RuntimeInput(string path)
    {
        string p=Full(path).Replace('\\','/');
        return p.Contains("/serververwaltung_bridge/commands",StringComparison.OrdinalIgnoreCase)
            ||p.Contains("/LPLK_Economy/web-requests",StringComparison.OrdinalIgnoreCase)
            ||p.Contains("/outbox",StringComparison.OrdinalIgnoreCase);
    }
    private static bool Equal(Snapshot a,Snapshot b)=>a.Exists==b.Exists&&a.Directory==b.Directory&&a.Link==b.Link&&a.Length==b.Length&&(a.Bytes is not null&&b.Bytes is not null?a.Bytes.AsSpan().SequenceEqual(b.Bytes):a.Stamp==b.Stamp);
    public static void Flush()
    {
        if(!active)return;
        if(Interlocked.Exchange(ref reading,1)==0)_=Task.Run(()=>{try{Refresh();}finally{Volatile.Write(ref reading,0);}});
        while(boundaryWork.TryDequeue(out var action))_=Task.Run(action);
        (string Path,Entry Entry,Snapshot View,long Revision)[] batch;
        lock(gate)
        {
            batch=entries.Where(x=>x.Value.Dirty).OrderBy(x=>x.Value.View.Exists?0:1).Select(x=>(x.Key,x.Value,x.Value.View,x.Value.Revision)).ToArray();
            if(batch.Length==0)return;
            Task prior=writer;
            writer=Task.Run(async()=>
            {
                try{await prior.ConfigureAwait(false);}catch{}
                foreach(var item in batch)
                {
                    try
                    {
                        Snapshot baseline;lock(gate){if(!item.Entry.Dirty||item.Entry.Revision<item.Revision)continue;baseline=item.Entry.Baseline;}
                        if(!item.View.Exists&&moveDependencies.TryGetValue(item.Path,out var dependency)&&entries.TryGetValue(dependency,out var target)&&target.Dirty)throw new IOException("Verschiebeziel noch nicht dauerhaft gespeichert; Quelle erhalten");
                        var actual=ReadPhysical(item.Path);
                        lock(gate){if(!item.Entry.Known){baseline=actual;item.Entry.Baseline=actual;item.Entry.Known=true;}}
                        if(actual.Error is not null||actual.Link is not null)throw new IOException("Unsicheres Schreibziel");
                        if(item.Entry.MustNotExist&&actual.Exists)throw new IOException("Exklusives Schreibziel existiert bereits");
                        if(!(item.View.Directory&&actual.Directory)&&!Equal(actual,baseline)&&!Equal(actual,item.View))throw new IOException("Externe Änderung: RAM-Stand bleibt erhalten");
                        Commit(item.Path,item.View);
                        Snapshot saved=ReadPhysical(item.Path);
                        lock(gate){item.Entry.Baseline=saved;item.Entry.MustNotExist=false;if(item.Entry.Revision==item.Revision){item.Entry.Dirty=false;item.Entry.View=saved;}}
                        if(!item.View.Exists)moveDependencies.TryRemove(item.Path,out _);
                    }
                    catch(Exception e){LastError="Rundenspeicherung ("+Path.GetFileName(item.Path)+"): "+e.Message;}
                }
                if(Pending==0)LastError=null;
            });
        }
    }
    public static Task<T> RunAfterRound<T>(Func<T> work)
    {
        if(!active||loading)return Task.Run(work);
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        AfterRound(()=>{try{result.SetResult(work());}catch(Exception e){result.SetException(e);}});
        return result.Task;
    }
    public static void AfterRound(Action work){if(!active){_=Task.Run(work);return;}boundaryWork.Enqueue(work);}
    public static string DataPath(ISwiftlyCore core)=>active?dataDirectory:core.PluginDataDirectory;
    public static string PluginPath(ISwiftlyCore core)=>active?pluginDirectory:core.PluginPath;
    public static string GamePath(ISwiftlyCore core)=>active?gameDirectory:core.GameFilesDirectory;
    public static string ConfigPath(ISwiftlyCore core,string file)=>active?Path.Combine(configDirectory,file):core.Configuration.GetConfigPath(file);
    public static void AbortLoad(){stopping=true;reader?.Dispose();reader=null;active=false;loading=false;}
    public static void EndUnload(){if(healthCommand is {} command)activeCore?.Command.UnregisterCommand(command);healthCommand=null;active=false;loading=false;activeCore=null;activePlugin=null;}
    internal static void MoveDependency(string source,string target)=>moveDependencies[Full(source)]=Full(target);
    private static void Commit(string path,Snapshot value)
    {
        // Validate ancestors on the worker, never weaken existing link checks.
        for(var d=new System.IO.DirectoryInfo(Path.GetDirectoryName(path)!);d is not null;d=d.Parent)
            if(d.LinkTarget is not null)throw new IOException("Verlinktes Schreibverzeichnis");
        if(!value.Exists){System.IO.File.Delete(path);return;}
        if(value.Directory){System.IO.Directory.CreateDirectory(path);return;}
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+".roundio-"+Guid.NewGuid().ToString("N");
        try{using(var stream=new System.IO.FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(value.Bytes!);stream.Flush(true);}System.IO.File.Move(temporary,path,true);}
        finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
    }
    public static void Drain()
    {
        stopping=true;reader?.Dispose();reader=null;Flush();writer.GetAwaiter().GetResult();
        if(Pending>0)throw new IOException(LastError??"Ungespeicherte RAM-Dateien");
    }
    internal static T Physical<T>(Func<T> work){bool old=physical.Value;physical.Value=true;try{return work();}finally{physical.Value=old;}}
}

public static class RamFile
{
    public static bool Exists(string path)=>RoundFileStore.Exists(path,false);
    public static byte[] ReadAllBytes(string path)=>(byte[])RoundFileStore.Bytes(path).Clone();
    public static string ReadAllText(string path)=>RoundFileStore.Text(path);
    public static string ReadAllText(string path,Encoding encoding){using var r=new StreamReader(new MemoryStream(RoundFileStore.Bytes(path),false),encoding,true);return r.ReadToEnd();}
    public static string[] ReadAllLines(string path)=>ReadAllText(path).Split(new[]{"\r\n","\n"},StringSplitOptions.None);
    public static IEnumerable<string> ReadLines(string path)=>ReadAllLines(path);
    public static void WriteAllText(string path,string? text)=>WriteAllText(path,text,new UTF8Encoding(false));
    public static void WriteAllText(string path,string? text,Encoding encoding)=>RoundFileStore.Put(path,encoding.GetBytes(text??""));
    public static Task WriteAllTextAsync(string path,string? text){WriteAllText(path,text);return Task.CompletedTask;}
    public static void AppendAllText(string path,string? text)=>AppendAllText(path,text,new UTF8Encoding(false));
    public static void AppendAllText(string path,string? text,Encoding encoding)
    {
        if(RoundFileStore.Direct){System.IO.File.AppendAllText(path,text,encoding);return;}
        RoundData.AppendText(path,text??"");
    }
    public static Task AppendAllTextAsync(string path,string? text){AppendAllText(path,text);return Task.CompletedTask;}
    public static void Delete(string path)=>RoundFileStore.Put(path,null);
    public static void Copy(string source,string destination,bool overwrite=false){if(RoundFileStore.Direct){System.IO.File.Copy(source,destination,overwrite);return;}if(!overwrite&&Exists(destination))throw new IOException("Ziel existiert");RoundFileStore.Put(destination,ReadAllBytes(source),mustNotExist:!overwrite);}
    public static void Move(string source,string destination,bool overwrite=false){if(RoundFileStore.Direct){System.IO.File.Move(source,destination,overwrite);return;}Copy(source,destination,overwrite);RoundFileStore.MoveDependency(source,destination);Delete(source);}
    public static FileAttributes GetAttributes(string path)=>new RamFileInfo(path).Attributes;
    public static DateTime GetLastWriteTimeUtc(string path)=>new RamFileInfo(path).LastWriteTimeUtc;
    public static Stream OpenRead(string path)=>RoundFileStore.Direct||!RoundFileStore.OnGameThread?System.IO.File.OpenRead(path):new MemoryStream(RoundFileStore.Bytes(path),false);
    public static Stream Create(string path)=>Open(path,FileMode.Create,FileAccess.Write,FileShare.None);
    public static Stream Open(string path,FileMode mode,FileAccess access=FileAccess.ReadWrite,FileShare share=FileShare.None,int bufferSize=4096,FileOptions options=FileOptions.None)
    {
        if(RoundFileStore.Direct||!RoundFileStore.OnGameThread)return new System.IO.FileStream(path,mode,access,share,bufferSize,options);
        if(access==FileAccess.Read)return OpenRead(path);
        return new PendingStream(path,mode);
    }
    private sealed class PendingStream:MemoryStream
    {
        private readonly string path;private readonly bool exclusive;private bool saved;
        public PendingStream(string path,FileMode mode){this.path=path;exclusive=mode==FileMode.CreateNew;if(exclusive&&Exists(path))throw new IOException("Datei existiert");if(mode is FileMode.Open or FileMode.OpenOrCreate or FileMode.Append&&Exists(path)){Write(ReadAllBytes(path));if(mode!=FileMode.Append)Position=0;}}
        protected override void Dispose(bool disposing){if(disposing&&!saved){saved=true;RoundFileStore.Put(path,ToArray(),mustNotExist:exclusive);}base.Dispose(disposing);}
    }
}

public class RamFileInfo(string path)
{
    public string FullName {get;}=Path.GetFullPath(path);
    public string Name=>Path.GetFileName(FullName);
    private RoundFileStore.Snapshot Value=>RoundFileStore.Read(FullName,false);
    public bool Exists=>Value.Exists&&!Value.Directory;
    public long Length=>Value.Length;
    public DateTime LastWriteTimeUtc=>Value.Stamp;
    public FileAttributes Attributes=>Value.Attributes;
    public string? LinkTarget=>Value.Link;
    public Stream OpenRead()=>RamFile.Open(FullName,FileMode.Open,FileAccess.Read,FileShare.Read);
}
public sealed class RamDirectoryInfo(string path)
{
    public string FullName {get;}=Path.GetFullPath(path);
    public string Name=>Path.GetFileName(FullName.TrimEnd(Path.DirectorySeparatorChar));
    public RamDirectoryInfo? Parent=>Path.GetDirectoryName(FullName) is {} p?new(p):null;
    private RoundFileStore.Snapshot Value=>RoundFileStore.Read(FullName,false);
    public bool Exists=>Value.Exists&&Value.Directory;
    public FileAttributes Attributes=>Value.Attributes;
    public string? LinkTarget=>Value.Link;
    public IEnumerable<RamFileInfo> EnumerateFiles(string pattern="*",SearchOption search=SearchOption.TopDirectoryOnly)=>RamDirectory.EnumerateFiles(FullName,pattern,search).Select(x=>new RamFileInfo(x));
    public IEnumerable<RamFileInfo> EnumerateFiles(string pattern,EnumerationOptions options)=>EnumerateFiles(pattern,options.RecurseSubdirectories?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly);
    public IEnumerable<RamDirectoryInfo> EnumerateDirectories()=>RamDirectory.EnumerateDirectories(FullName).Select(x=>new RamDirectoryInfo(x));
}
public static class RamDirectory
{
    public static bool Exists(string path)=>RoundFileStore.Exists(path,true);
    public static RamDirectoryInfo CreateDirectory(string path){RoundFileStore.Put(path,null,true);return new(path);}
    public static RamDirectoryInfo? GetParent(string path)=>new RamDirectoryInfo(path).Parent;
    public static IEnumerable<string> EnumerateFiles(string path,string pattern="*",SearchOption search=SearchOption.TopDirectoryOnly)=>RoundFileStore.Enumerate(path,pattern,search,false);
    public static IEnumerable<string> EnumerateDirectories(string path,string pattern="*",SearchOption search=SearchOption.TopDirectoryOnly)=>RoundFileStore.Enumerate(path,pattern,search,true);
    public static IEnumerable<string> EnumerateFileSystemEntries(string path)=>RoundFileStore.Enumerate(path,"*",SearchOption.TopDirectoryOnly,null);
    public static string[] GetFiles(string path,string pattern="*",SearchOption search=SearchOption.TopDirectoryOnly)=>EnumerateFiles(path,pattern,search).ToArray();
    public static string[] GetDirectories(string path)=>EnumerateDirectories(path).ToArray();
    public static void Delete(string path,bool recursive=false)
    {
        if(RoundFileStore.OnGameThread&&!RoundFileStore.Direct)throw new IOException("Verzeichniswartung muss im Hintergrund ausgeführt werden");
        System.IO.Directory.Delete(path,recursive);
    }
}
