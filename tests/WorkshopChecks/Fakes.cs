// Test-only boundary fakes: the production workshop manager is linked unchanged.
namespace AddonsManager.Structs { }
namespace Microsoft.Extensions.Options
{
    public interface IOptionsMonitor<T> { T CurrentValue { get; } }
    public sealed record Options<T>(T CurrentValue) : IOptionsMonitor<T>;
}
namespace Microsoft.Extensions.Logging
{
    public sealed class Logger
    {
        public readonly List<string> Messages = [];
        public void LogInformation(string message, params object?[] args) => Messages.Add(message + " " + string.Join(" | ", args));
        public void LogWarning(string message, params object?[] args) => Messages.Add(message + " " + string.Join(" | ", args));
        public void LogError(string message, params object?[] args) => Messages.Add(message + " " + string.Join(" | ", args));
        public void LogDebug(string message, params object?[] args) => Messages.Add(message + " " + string.Join(" | ", args));
    }
}
namespace SwiftlyS2.Shared.Events
{
    public static class EventDelegates { public delegate void OnSteamAPIActivated(); }
    [AttributeUsage(AttributeTargets.Method)] public sealed class EventListenerAttribute<T> : Attribute;
}
namespace SwiftlyS2.Shared.FileSystem
{
    public enum SearchPathAdd_t { PATH_ADD_TO_TAIL, PATH_ADD_TO_HEAD }
    public enum SearchPathPriority_t { SEARCH_PATH_PRIORITY_VPK }
    public enum GetSearchPathTypes_t { GET_SEARCH_PATH_ALL }
    public sealed class FileSystem
    {
        public int Mounts;
        public int Unmounts;
        public bool Exists = true;
        public bool FileExists(string path, string id) => Exists;
        public bool IsDirectory(string path, string id) => false;
        public void AddSearchPath(string path, string id, SearchPathAdd_t position, SearchPathPriority_t priority) => Mounts++;
        public bool RemoveSearchPath(string path, string id) { Unmounts++; return true; }
        public string GetSearchPath(string id, GetSearchPathTypes_t type, int count) => "/home/container/game/bin/linuxsteamrt64/";
    }
}
namespace SwiftlyS2.Shared
{
    public sealed class ISwiftlyCore
    {
        public Registrator Registrator { get; } = new();
        public Engine Engine { get; } = new();
        public SwiftlyS2.Shared.FileSystem.FileSystem GameFileSystem { get; } = new();
        public Microsoft.Extensions.Logging.Logger Logger { get; } = new();
        public Scheduler Scheduler { get; } = new();
    }
    public sealed class Registrator { public void Register(object target) { } }
    public sealed class Engine
    {
        public Globals GlobalVars { get; } = new();
        public List<string> Commands { get; } = [];
        public void ExecuteCommand(string command) => Commands.Add(command);
    }
    public sealed class Globals { public StringValue MapName { get; } = new(); }
    public sealed class StringValue { public string Value { get; set; } = "de_dust2"; }
    public sealed class Scheduler
    {
        private readonly Queue<Action> work = [];
        public void NextWorldUpdate(Action action) => work.Enqueue(action);
        public void Run() { while (work.TryDequeue(out var action)) action(); }
    }
}
namespace SwiftlyS2.Shared.SteamAPI
{
    public readonly record struct PublishedFileId_t(ulong m_PublishedFileId);
    public readonly record struct AppId_t(uint m_AppId);
    [Flags] public enum EItemState
    {
        k_EItemStateNone = 0, k_EItemStateLegacyItem = 2, k_EItemStateInstalled = 4,
        k_EItemStateNeedsUpdate = 8, k_EItemStateDownloading = 16, k_EItemStateDownloadPending = 32
    }
    public enum EResult { k_EResultNone = 0, k_EResultOK = 1, k_EResultFail = 2, k_EResultNoConnection = 3, k_EResultBusy = 10, k_EResultTimeout = 16, k_EResultServiceUnavailable = 20 }
    public struct DownloadItemResult_t
    {
        public AppId_t m_unAppID;
        public PublishedFileId_t m_nPublishedFileId;
        public EResult m_eResult;
    }
    public static class SteamGameServer { public static bool LoggedOn = true; public static bool BLoggedOn() => LoggedOn; }
    public sealed class Callback<T>(Action<T> action) : IDisposable where T : struct
    {
        private static readonly List<Callback<T>> callbacks = [];
        public static int Count => callbacks.Count;
        public static Callback<T> Create(Action<T> action) { var item = new Callback<T>(action); callbacks.Add(item); return item; }
        public static void Fire(T value) { foreach (var item in callbacks.ToArray()) item.Invoke(value); }
        private void Invoke(T value) => action(value);
        public void Dispose() => callbacks.Remove(this);
    }
    public static class SteamGameServerUGC
    {
        public sealed class Item
        {
            public EItemState State = EItemState.k_EItemStateInstalled;
            public ulong Size = 100;
            public uint Timestamp = 10;
            public bool StartAccepted = true;
            public bool InstallInfo = true;
        }
        public static Dictionary<ulong, Item> Items = [];
        public static readonly List<ulong> Requests = [];
        public static void Reset() { Items.Clear(); Requests.Clear(); SteamGameServer.LoggedOn = true; }
        public static Item Get(ulong id) => Items.TryGetValue(id, out var item) ? item : Items[id] = new();
        public static uint GetItemState(PublishedFileId_t id) => (uint)Get(id.m_PublishedFileId).State;
        public static bool DownloadItem(PublishedFileId_t id, bool highPriority)
        {
            Requests.Add(id.m_PublishedFileId);
            var item = Get(id.m_PublishedFileId);
            if (!item.StartAccepted) return false;
            item.State |= EItemState.k_EItemStateDownloading;
            return true;
        }
        public static bool GetItemInstallInfo(PublishedFileId_t id, out ulong size, out string folder, uint max, out uint stamp)
        {
            var item = Get(id.m_PublishedFileId);
            if ((item.State & (EItemState.k_EItemStateDownloading | EItemState.k_EItemStateDownloadPending)) != 0)
                throw new InvalidOperationException("Install info read before download completed");
            size = item.Size; stamp = item.Timestamp; folder = "/tmp/workshop";
            return item.InstallInfo;
        }
        public static bool GetItemDownloadInfo(PublishedFileId_t id, out ulong completed, out ulong total)
        { completed = 0; total = 0; return true; }
    }
}
