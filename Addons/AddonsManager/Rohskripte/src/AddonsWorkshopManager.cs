using AddonsManager.Config;
using AddonsManager.Structs;
using AddonsManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.SteamAPI;

namespace AddonsManager.SteamWorkshop;

public class AddonsWorkshopManager : IDisposable
{
    private ISwiftlyCore Core;
    private AddonsUtilities Utilities;
    private IOptionsMonitor<AddonsConfig> Config;
    private Callback<DownloadItemResult_t>? _downloadItemResult;
    private readonly DownloadTracker downloads = new();
    private readonly DownloadRetries retries = new();
    private readonly Func<long> now;
    private readonly Dictionary<string, string> lastResults = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> slowDownloads = [];
    private bool waitingForSteam;
    private bool steamApiAvailable;
    private bool disposed;
    private int mapGeneration;
    public string LastMapCheck { get; private set; } = "Noch kein Mapstart geprueft";
    public bool MapChecksEnabled => Config.CurrentValue.CheckForUpdatesOnMapStart;
    public string[] ConfiguredAddons => Config.CurrentValue.Addons.Distinct(StringComparer.Ordinal).ToArray();
    public string AddonStatus(string id)
    {
        if (!ulong.TryParse(id, out var number)) return "Ungueltige Workshop-ID";
        if (downloads.Contains(number)) return "Steam-Pruefung / Download laeuft";
        if (retries.Due(number) is long due)
        {
            string wait = due > now() ? $"in {(due - now() + 999) / 1000} s" : "wartet auf Steam-Verbindung";
            return lastResults.GetValueOrDefault(id, "Download fehlgeschlagen") + $"; Wiederholung {retries.Attempts(number) + 1}/3 {wait}";
        }
        if (downloads.AwaitingMount(number)) return "Update geladen; Einbinden beim naechsten Mapstart";
        return lastResults.GetValueOrDefault(id, "Noch nicht geprueft");
    }

    public AddonsWorkshopManager(ISwiftlyCore core, AddonsUtilities utils, IOptionsMonitor<AddonsConfig> config)
        : this(core, utils, config, () => Environment.TickCount64) { }

    internal AddonsWorkshopManager(ISwiftlyCore core, AddonsUtilities utils, IOptionsMonitor<AddonsConfig> config, Func<long> clock)
    {
        Core = core;
        Utilities = utils;
        Config = config;
        now = clock;
        core.Registrator.Register(this);
    }

    public bool MountAddon(string addonName, bool bAddToTail = false, bool bAllowRedownload = true)
    {
        if (addonName == string.Empty) return false;
        if (!steamApiAvailable)
        {
            waitingForSteam = true;
            return false;
        }

        var alreadyMountedAddodns = Utilities.StringToVector(Utilities.GetCurrentWorkshopMap());
        if (alreadyMountedAddodns.Contains(addonName))
        {
            Core.Logger.LogWarning("Addon {AddonName} is already mounted by the server.", addonName);
            return false;
        }

        var iAddon = new PublishedFileId_t(ulong.TryParse(addonName, out var result) ? result : 0);
        var iAddonState = (EItemState)SteamGameServerUGC.GetItemState(iAddon);

        if (iAddonState.HasFlag(EItemState.k_EItemStateLegacyItem))
        {
            Core.Logger.LogError("Addon {AddonName} is not compatible with Source 2, skipping.", addonName);
            return false;
        }

        // Never reopen a VPK while Steam is still replacing it.
        if (downloads.Contains(iAddon.m_PublishedFileId) || DownloadInProgress(iAddonState))
        {
            Core.Logger.LogInformation("Addon {AddonName}: Download laeuft noch; Einbinden beim naechsten Mapstart.", addonName);
            return false;
        }

        if (!iAddonState.HasFlag(EItemState.k_EItemStateInstalled))
        {
            Core.Logger.LogInformation("Addon {AddonName} is not installed, queuing for download.", addonName);
            DownloadAddon(addonName, true, true);
            return false;
        }

        string addonPath = Utilities.BuildAddonPath(addonName);

        if (!Core.GameFileSystem.FileExists(addonPath, string.Empty))
        {
            addonPath = Utilities.BuildAddonPath(addonName, true);

            if (!Core.GameFileSystem.FileExists(addonPath, string.Empty))
            {
                Core.Logger.LogError("Addon {AddonName} couldn't be found at {AddonPath}.", addonName, addonPath);
                return false;
            }
        }
        else
        {
            // The filesystem API requires the base .vpk path (without _dir); it appends _dir and chunk suffixes internally
            addonPath = Utilities.BuildAddonPath(addonName, true);
        }

        if (Utilities.GetMountedAddons().Contains(addonName))
        {
            Core.Logger.LogError("Addon {AddonName} is already mounted.", addonName);
            return false;
        }

        Core.Logger.LogDebug("Mounting addon {AddonName} from path {AddonPath}.", addonName, addonPath);
        Core.GameFileSystem.AddSearchPath(addonPath, "GAME", bAddToTail ? SearchPathAdd_t.PATH_ADD_TO_TAIL : SearchPathAdd_t.PATH_ADD_TO_HEAD, SearchPathPriority_t.SEARCH_PATH_PRIORITY_VPK);
        Utilities.GetMountedAddons().Add(addonName);

        if (downloads.Mounted(iAddon.m_PublishedFileId))
        {
            lastResults[addonName] = "Heruntergeladene Version eingebunden";
            Core.Logger.LogInformation("Addon {AddonName}: heruntergeladene Version eingebunden; Precache erfolgt durch die Ressourcen-Plugins beim Mapstart.", addonName);
        }

        // Defer the request until mounting/precache has finished. Map-start checks are
        // scheduled separately and collapse duplicate requests by Workshop ID.
        if (bAllowRedownload && Config.CurrentValue.RedownloadAddonOnMount)
            Core.Scheduler.NextWorldUpdate(() => { if (!disposed) DownloadAddon(addonName, false, true); });

        return true;
    }

    public bool UnmountAddon(string addonName)
    {
        if (addonName == string.Empty) return false;

        if (!Utilities.GetMountedAddons().Contains(addonName))
        {
            Core.Logger.LogError("Addon {AddonName} is not mounted.", addonName);
            return false;
        }

        string addonPath = Utilities.BuildAddonPath(addonName, true);

        if (!Core.GameFileSystem.RemoveSearchPath(addonPath, "GAME"))
            return false;

        Core.Logger.LogDebug("Unmounting addon {AddonName} from path {AddonPath}.", addonName, addonPath);
        Utilities.GetMountedAddons().Remove(addonName);

        return true;
    }

    public void PrintDownloadProgress()
    {
        if (disposed) return;
        bool newlyConnected = _downloadItemResult is null;
        if (_downloadItemResult is null)
        {
            InitializeSteamCallbacks();
            if (_downloadItemResult is null) return;
        }
        if (!SteamGameServer.BLoggedOn()) return;
        if (waitingForSteam)
        {
            waitingForSteam = false;
            if (newlyConnected) RefreshAddons(true);
            CheckForMapUpdates();
        }
        foreach (ulong id in retries.Ready(now()))
        {
            if (!ConfiguredAddons.Contains(id.ToString(), StringComparer.Ordinal)) { retries.Reset(id); continue; }
            if (downloads.Contains(id)) continue;
            retries.Take(id);
            Core.Logger.LogInformation("Addon {AddonName}: automatische Download-Wiederholung {Attempt}/3.", id, retries.Attempts(id));
            // Delayed retries must not cause a surprise map reload, even if the
            // original failed request came from an explicit download command.
            DownloadAddonInternal(id.ToString(), false, true, true);
        }
        foreach (var (id, request) in downloads.Pending.ToArray())
        {
            if (now() - request.Started > 600_000 && slowDownloads.Add(id))
                Core.Logger.LogWarning("Addon {AddonName}: seit 10 Minuten keine Downloadbestaetigung von Steam. Kein Erfolg/Precache angenommen; Steam-Verbindung und Downloadlog pruefen.", id);
            if (!SteamGameServerUGC.GetItemDownloadInfo(new PublishedFileId_t(id), out var bytesDownloaded, out var bytesTotal) || bytesTotal == 0) continue;
            Core.Logger.LogDebug("Downloading addon {AddonName}: {Downloaded}/{Total} bytes ({Progress:F2}%)", id, bytesDownloaded, bytesTotal, bytesDownloaded * 100.0 / bytesTotal);
        }
    }

    public bool DownloadAddon(string pszAddon, bool bImportant, bool bForce)
        => DownloadAddonInternal(pszAddon, bImportant, bForce, false);

    private bool DownloadAddonInternal(string pszAddon, bool bImportant, bool bForce, bool isRetry)
    {
        if (disposed) return false;
        var iAddon = new PublishedFileId_t(ulong.TryParse(pszAddon, out var result) ? result : 0);
        if (iAddon.m_PublishedFileId == 0 || result.ToString() != pszAddon)
        {
            Core.Logger.LogError("Invalid addon ID {AddonName}.", pszAddon);
            return false;
        }

        if (downloads.Contains(result))
        {
            downloads.Start(result, bImportant, null, now());
            Core.Logger.LogDebug("Addon {AddonName} is already in the download queue.", pszAddon);
            return false;
        }

        if (!isRetry) retries.Reset(result);
        InitializeSteamCallbacks();
        if (_downloadItemResult is null || !SteamGameServer.BLoggedOn())
        {
            lastResults[pszAddon] = "Steam noch nicht bereit";
            ScheduleRetry(result);
            Core.Logger.LogWarning("Addon {AddonName}: {Status}", pszAddon, AddonStatus(pszAddon));
            return false;
        }

        var iAddonState = (EItemState)SteamGameServerUGC.GetItemState(iAddon);
        if (iAddonState.HasFlag(EItemState.k_EItemStateLegacyItem))
        {
            lastResults[pszAddon] = "Kein Source-2-Addon";
            return false;
        }
        if (DownloadInProgress(iAddonState))
        {
            // Steam/another component already owns this transfer. Observe its result.
            downloads.Start(result, bImportant, null, now());
            return false;
        }

        if (!bForce && iAddonState.HasFlag(EItemState.k_EItemStateInstalled))
        {
            Core.Logger.LogInformation("Addon {AddonName} is already installed, skipping download.", pszAddon);
            return false;
        }

        var before = ReadInstalledRevision(iAddon);
        if (!SteamGameServerUGC.DownloadItem(iAddon, false))
        {
            lastResults[pszAddon] = "Steam konnte den Download nicht starten";
            ScheduleRetry(result);
            Core.Logger.LogError("Failed to start download for addon {AddonName} because the Addon ID is invalid or the server is not logged on Steam.", pszAddon);
            return false;
        }

        downloads.Start(result, bImportant, before, now());
        Core.Logger.LogInformation("Started download for addon {AddonName}.", pszAddon);
        return true;
    }

    public void RefreshAddons(bool reloadMap = false)
    {
        Core.Logger.LogDebug("Refreshing addons ([green]{addonsList}[default], bReloadMap: {reloadMap}).", Utilities.VectorToString(Utilities.GetMountedAddons()), reloadMap);

        var previouslyMounted = new HashSet<string>(Utilities.GetMountedAddons(), StringComparer.Ordinal);
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addon in previouslyMounted.Reverse())
        {
            // Keep the existing open VPK while Steam owns the transfer. Removing it
            // first leaves the next level without resources, since MountAddon must
            // not reopen a file that Steam is replacing. Removed configuration
            // entries still unmount normally. A confirmed update remounts next map.
            if (ConfiguredAddons.Contains(addon) && ulong.TryParse(addon, out var id)
                && (downloads.Contains(id) || (steamApiAvailable && DownloadInProgress(
                    (EItemState)SteamGameServerUGC.GetItemState(new PublishedFileId_t(id))))))
            {
                retained.Add(addon);
                Core.Logger.LogInformation("Addon {AddonName}: vorhandene Einbindung bleibt waehrend der Steam-Pruefung erhalten.", addon);
                continue;
            }
            UnmountAddon(addon);
        }

        var bAllAddonsMounted = true;
        var newlyMounted = false;

        foreach (var addon in ConfiguredAddons)
        {
            if (retained.Contains(addon)) continue;
            if (!MountAddon(addon, bAllowRedownload: reloadMap))
            {
                bAllAddonsMounted = false;
                Core.Logger.LogError("Failed to mount addon {AddonName}.", addon);
            }
            else if (!previouslyMounted.Contains(addon)) newlyMounted = true;
        }

        // Repeated Steam activation events must not reload an already prepared
        // level while its background update check is still in flight.
        if (bAllAddonsMounted && reloadMap && newlyMounted)
        {
            Core.Logger.LogInformation("All addons mounted successfully, reloading map.");
            ReloadMap();
        }
    }

    [EventListener<EventDelegates.OnSteamAPIActivated>]
    public void OnSteamAPIActivated()
    {
        InitializeSteamCallbacks(apiAvailable: true);
        RefreshAddons(true);
        if (waitingForSteam || Config.CurrentValue.CheckForUpdatesOnMapStart) CheckForMapUpdates();
    }

    public void InitializeSteamCallbacks(bool apiAvailable = false)
    {
        steamApiAvailable |= apiAvailable;
        if (!disposed && steamApiAvailable && _downloadItemResult is null && SteamGameServer.BLoggedOn())
            _downloadItemResult = Callback<DownloadItemResult_t>.Create(OnAddonDownloaded);
    }

    public void CheckForMapUpdates()
    {
        int generation = ++mapGeneration;
        if (!MapChecksEnabled || disposed) return;
        InitializeSteamCallbacks();
        if (_downloadItemResult is null || !SteamGameServer.BLoggedOn())
        {
            waitingForSteam = true;
            LastMapCheck = "Warte auf Steam-Verbindung";
            return;
        }
        waitingForSteam = false;
        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (disposed || generation != mapGeneration || !MapChecksEnabled) return;
            var ids = ConfiguredAddons;
            LastMapCheck = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} / {Core.Engine.GlobalVars.MapName.Value}";
            Core.Logger.LogInformation("Mapstart: pruefe {Count} Workshop-Addons auf Updates. Downloads werden beim folgenden regulaeren Mapstart eingebunden.", ids.Length);
            foreach (string id in ids) DownloadAddon(id, false, true);
        });
    }

    private static bool DownloadInProgress(EItemState state) =>
        state.HasFlag(EItemState.k_EItemStateDownloading) || state.HasFlag(EItemState.k_EItemStateDownloadPending);

    private static InstalledRevision? ReadInstalledRevision(PublishedFileId_t id)
    {
        var state = (EItemState)SteamGameServerUGC.GetItemState(id);
        if (!state.HasFlag(EItemState.k_EItemStateInstalled) || DownloadInProgress(state)) return null;
        return SteamGameServerUGC.GetItemInstallInfo(id, out var size, out _, 4096, out var stamp) ? new(size, stamp) : null;
    }

    public void ReloadMap()
    {
        if (Utilities.GetCurrentWorkshopMap().Length == 0 || Core.GameFileSystem.IsDirectory(Utilities.GetCurrentWorkshopMap(), "OFFICIAL_ADDONS"))
        {
            Core.Engine.ExecuteCommand("changelevel " + Core.Engine.GlobalVars.MapName.Value);
        }
        else
        {
            Core.Engine.ExecuteCommand("host_workshop_map " + Utilities.GetCurrentWorkshopMap());
        }
    }

    public void OnAddonDownloaded(DownloadItemResult_t pCallback)
    {
        if (disposed || pCallback.m_unAppID.m_AppId != 730 || !downloads.Contains(pCallback.m_nPublishedFileId.m_PublishedFileId)) return;
        ulong id = pCallback.m_nPublishedFileId.m_PublishedFileId;
        var state = (EItemState)SteamGameServerUGC.GetItemState(pCallback.m_nPublishedFileId);
        var after = ReadInstalledRevision(pCallback.m_nPublishedFileId);
        // Some older Swiftly builds report None. Only accept it with independently
        // verified installed state; never turn a failed/incomplete transfer into success.
        bool success = (pCallback.m_eResult == EResult.k_EResultOK || pCallback.m_eResult == EResult.k_EResultNone)
            && after is not null && !state.HasFlag(EItemState.k_EItemStateNeedsUpdate) && !DownloadInProgress(state);
        var completion = downloads.Complete(id, success, after);
        slowDownloads.Remove(id);
        if (success)
        {
            retries.Reset(id);
            lastResults[id.ToString()] = completion!.Value.Changed ? "Update geladen; naechster Mapstart" : "Steam-Pruefung abgeschlossen; installierte Version unveraendert";
            Core.Logger.LogInformation("Addon {AddonName}: {Status}.", id, AddonStatus(id.ToString()));
        }
        else
        {
            lastResults[id.ToString()] = $"Download nicht bestaetigt (Steam {pCallback.m_eResult})";
            if (pCallback.m_eResult is EResult.k_EResultNoConnection or EResult.k_EResultTimeout
                or EResult.k_EResultBusy or EResult.k_EResultServiceUnavailable)
                ScheduleRetry(id);
            else
            {
                retries.Reset(id);
                lastResults[id.ToString()] += "; erneute Pruefung beim naechsten Mapstart";
            }
            Core.Logger.LogError("Addon {AddonName}: {Status}. Installationsstatus: {State}", id, AddonStatus(id.ToString()), state);
        }
        if (completion?.ReloadMap == true)
        {
            Core.Logger.LogInformation("All important addons have been downloaded, reloading map {mapName}.", Core.Engine.GlobalVars.MapName.Value);
            ReloadMap();
        }
    }

    private void ScheduleRetry(ulong id)
    {
        if (!ConfiguredAddons.Contains(id.ToString(), StringComparer.Ordinal))
        {
            lastResults[id.ToString()] += "; nicht in Main.Addons: manuellen Download erneut anfordern";
            return;
        }
        if (!retries.Schedule(id, now()))
            lastResults[id.ToString()] += "; 3 Wiederholungen erfolglos. Steam-Verbindung pruefen; neuer Versuch beim naechsten Mapstart";
    }

    public void Dispose()
    {
        disposed = true;
        _downloadItemResult?.Dispose();
        _downloadItemResult = null;
    }
}
