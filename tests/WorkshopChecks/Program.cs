using System.Text.Json;
using AddonsManager.Config;
using AddonsManager.SteamWorkshop;
using AddonsManager.Utils;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SteamAPI;

int passed = 0;
void Check(bool valid, string message) { if (!valid) throw new Exception(message); passed++; Console.WriteLine("OK " + message); }
void Done(ulong id, uint stamp = 10, EResult result = EResult.k_EResultOK, uint app = 730, EItemState state = EItemState.k_EItemStateInstalled)
{
    var item = SteamGameServerUGC.Get(id); item.State = state; item.Timestamp = stamp;
    Callback<DownloadItemResult_t>.Fire(new() { m_unAppID = new(app), m_nPublishedFileId = new(id), m_eResult = result });
}
void Scenario(string name, Action<ISwiftlyCore, AddonsWorkshopManager, AddonsConfig> run, Func<long>? clock = null)
{
    SteamGameServerUGC.Reset();
    var core = new ISwiftlyCore(); var config = new AddonsConfig { Addons = ["3797047889"] };
    using (var manager = new AddonsWorkshopManager(core, new AddonsUtilities(core), new Options<AddonsConfig>(config), clock ?? (() => Environment.TickCount64)))
    {
        SteamGameServer.LoggedOn = false;
        manager.InitializeSteamCallbacks(apiAvailable: true);
        SteamGameServer.LoggedOn = true;
        run(core, manager, config);
    }
    Check(Callback<DownloadItemResult_t>.Count == 0, name + ": Steam callback disposed");
}

Check(JsonSerializer.Deserialize<AddonsConfig>("{\"RedownloadAddonOnMount\":false}")!.CheckForUpdatesOnMapStart,
    "Existing configuration enables map checks without changing old redownload setting");
Check(!JsonSerializer.Deserialize<AddonsConfig>("{\"CheckForUpdatesOnMapStart\":false}")!.CheckForUpdatesOnMapStart, "Explicit opt-out respected");

SteamGameServerUGC.Reset();
var inactiveCore = new ISwiftlyCore();
using (var inactive = new AddonsWorkshopManager(inactiveCore, new AddonsUtilities(inactiveCore), new Options<AddonsConfig>(new() { Addons = ["3797047889"] })))
{
    inactive.PrintDownloadProgress(); inactive.RefreshAddons(); inactive.CheckForMapUpdates(); inactiveCore.Scheduler.Run();
    Check(Callback<DownloadItemResult_t>.Count == 0 && SteamGameServerUGC.Requests.Count == 0, "Plugin load before Steam activation never registers callbacks or starts requests");
    inactive.OnSteamAPIActivated(); inactiveCore.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 1, "Steam activation starts deferred first-map check");
    Done(3797047889);
}

Scenario("Map changes", (core, manager, config) =>
{
    manager.RefreshAddons(); manager.CheckForMapUpdates();
    Check(core.GameFileSystem.Mounts == 1 && SteamGameServerUGC.Requests.Count == 0, "Mount before deferred update request");
    core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.SequenceEqual(new ulong[] { 3797047889 }), "Normal map start checks installed addon even without NeedsUpdate flag");
    manager.PrintDownloadProgress();
    Done(3797047889);
    Check(core.Engine.Commands.Count == 0, "Unchanged automatic check does not reload map");
    Check(manager.AddonStatus("3797047889").Contains("unveraendert"), "Unchanged installation reported accurately");
    core.Engine.GlobalVars.MapName.Value = "de_nuke";
    manager.RefreshAddons(); manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 2 && manager.LastMapCheck.Contains("de_nuke"), "Next map starts another Steam check");
    Done(3797047889, 20);
    Check(manager.AddonStatus("3797047889").Contains("naechsten Mapstart") && core.Engine.Commands.Count == 0, "Updated content waits for natural map start, no forced round interruption");
    manager.RefreshAddons();
    Check(manager.AddonStatus("3797047889").Contains("eingebunden"), "Only successful mount marks downloaded revision mounted");
});

Scenario("Disabled", (core, manager, config) =>
{
    config.CheckForUpdatesOnMapStart = false;
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 0, "Disabled map checks do not download");
});

Scenario("Pending and callback ordering", (core, manager, config) =>
{
    config.Addons = ["3797047889", "42", "3797047889"];
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 2, "Duplicate IDs and overlapping map events do not start duplicate transfers");
    manager.RefreshAddons();
    Check(core.GameFileSystem.Mounts == 0, "Pending VPKs are never opened by mount");
    Done(42, 20);
    Check(manager.AddonStatus("42").Contains("Update geladen") && manager.AddonStatus("3797047889").Contains("laeuft"), "Out-of-order completion removes only its own Workshop ID");
    Done(3797047889, 20, app: 440);
    Check(manager.AddonStatus("3797047889").Contains("laeuft"), "Callbacks for other applications ignored");
    Done(3797047889, 20);
    Check(core.Engine.Commands.Count == 0, "Multiple automatic completions never cause map-reload loops");
});

Scenario("Failure", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Done(3797047889, result: EResult.k_EResultTimeout);
    Check(manager.AddonStatus("3797047889").Contains("nicht bestaetigt") && core.Engine.Commands.Count == 0, "Failure is visible and does not reload map");
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 2, "Failure retried on next map check");
    Done(3797047889, result: EResult.k_EResultNone, state: EItemState.k_EItemStateInstalled | EItemState.k_EItemStateNeedsUpdate);
    Check(manager.AddonStatus("3797047889").Contains("nicht bestaetigt"), "None result with pending update is not accepted as success");
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889, result: EResult.k_EResultNone);
    Check(manager.AddonStatus("3797047889").Contains("unveraendert"), "Legacy None result accepted only after installation verification");
});

Scenario("Manual batch", (core, manager, config) =>
{
    manager.DownloadAddon("42", true, true); manager.DownloadAddon("43", true, true);
    Done(43, 20, result: EResult.k_EResultFail); Done(42, 20);
    Check(core.Engine.Commands.Count == 0, "Failed important batch never reloads even when last callback succeeds");
    manager.DownloadAddon("43", true, true); Done(43, 21);
    Check(core.Engine.Commands.Count == 1, "Successful manual retry retains deliberate reload command behavior");
});

Scenario("Manual promotion", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    manager.DownloadAddon("3797047889", true, true); Done(3797047889, 20);
    Check(SteamGameServerUGC.Requests.Count == 1 && core.Engine.Commands.Count == 1, "Manual command promotes existing transfer without second request");
});

Scenario("Steam not ready", (core, manager, config) =>
{
    SteamGameServer.LoggedOn = false; manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 0 && manager.LastMapCheck.Contains("Warte"), "Offline Steam defers check");
    SteamGameServer.LoggedOn = true; manager.PrintDownloadProgress(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 1, "Delayed Steam login resumes pending map check");
    Done(3797047889);
});

Scenario("Invalid, legacy and start failure", (core, manager, config) =>
{
    manager.DownloadAddon("3797047889;quit", false, true);
    SteamGameServerUGC.Get(42).State = EItemState.k_EItemStateLegacyItem;
    manager.DownloadAddon("42", false, true);
    Check(SteamGameServerUGC.Requests.Count == 0, "Invalid IDs and legacy addons never reach DownloadItem");
    SteamGameServerUGC.Get(3797047889).StartAccepted = false;
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(manager.AddonStatus("3797047889").Contains("nicht starten"), "Rejected transfer is not left pending");
    SteamGameServerUGC.Get(3797047889).StartAccepted = true;
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889);
});

Scenario("Disposed work", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); manager.Dispose(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 0, "Queued work cannot issue Steam request after plugin unload");
});

long time = 0;
Scenario("NoConnection recovery", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    Done(3797047889, result: EResult.k_EResultNoConnection, state: EItemState.k_EItemStateInstalled | EItemState.k_EItemStateNeedsUpdate);
    Check(manager.AddonStatus("3797047889").Contains("Wiederholung 1/3 in 30 s"), "Reported NoConnection with Installed/NeedsUpdate schedules visible retry");
    time = 29_999; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 1, "No immediate retry storm before 30 seconds");
    time = 30_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2, "NoConnection retried during same map");
    manager.PrintDownloadProgress(); manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 2, "Timer and overlapping map check coalesce active retry");
    Done(3797047889, 20);
    time = 1_000_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2 && core.Engine.Commands.Count == 0, "Successful retry clears schedule without forcing map reload");
    manager.RefreshAddons();
    Check(manager.AddonStatus("3797047889").Contains("eingebunden"), "Recovered download is available for next mount");
}, () => time);

time = 0;
Scenario("Offline retry waiting", (core, manager, config) =>
{
    manager.DownloadAddon("3797047889", true, true);
    Done(3797047889, result: EResult.k_EResultNoConnection);
    SteamGameServer.LoggedOn = false; time = 500_000;
    manager.PrintDownloadProgress(); manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 1 && manager.AddonStatus("3797047889").Contains("1/3 wartet"), "Offline waiting does not issue Steam requests or consume retry attempts");
    SteamGameServer.LoggedOn = true; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2, "Connection recovery resumes due retry without map change");
    Done(3797047889, 20);
    Check(core.Engine.Commands.Count == 0, "Delayed recovery of failed manual command also avoids surprise map reload");
}, () => time);

time = 0;
Scenario("Bounded retry budget", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889, result: EResult.k_EResultTimeout);
    int attempt = 0;
    foreach (long delay in new long[] { 30_000, 60_000, 120_000 })
    {
        time += delay - 1; manager.PrintDownloadProgress();
        Check(SteamGameServerUGC.Requests.Count == attempt + 1, $"Retry {attempt + 1} observes its backoff");
        time++; manager.PrintDownloadProgress(); attempt++;
        Check(SteamGameServerUGC.Requests.Count == attempt + 1, $"Retry {attempt} starts once due");
        Done(3797047889, result: EResult.k_EResultTimeout);
    }
    time += 10_000_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 4 && manager.AddonStatus("3797047889").Contains("3 Wiederholungen erfolglos"), "Repeated failures stop after three retries with actionable status");
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889, result: EResult.k_EResultTimeout);
    Check(SteamGameServerUGC.Requests.Count == 5 && manager.AddonStatus("3797047889").Contains("1/3 in 30 s"), "Next map begins fresh update check after exhausted retries");
}, () => time);

time = 0;
Scenario("Transient start failures and permanent errors", (core, manager, config) =>
{
    SteamGameServerUGC.Get(3797047889).StartAccepted = false;
    manager.CheckForMapUpdates(); core.Scheduler.Run();
    time = 30_000; SteamGameServerUGC.Get(3797047889).StartAccepted = true; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2, "Rejected DownloadItem start is retried with backoff");
    Done(3797047889, result: EResult.k_EResultFail);
    time = 1_000_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2, "Non-transient error does not produce automatic retry loop");
    manager.DownloadAddon("42", true, true); Done(42, result: EResult.k_EResultNoConnection);
    time = 2_000_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 3 && manager.AddonStatus("42").Contains("nicht in Main.Addons"), "Unconfigured manual item is not adopted into automatic management");
}, () => time);

time = 0;
Scenario("Offline after callback registration", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889);
    SteamGameServer.LoggedOn = false; manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 1, "Connection is checked again after initial callback registration");
    SteamGameServer.LoggedOn = true; manager.PrintDownloadProgress(); core.Scheduler.Run();
    Check(SteamGameServerUGC.Requests.Count == 2 && core.Engine.Commands.Count == 0, "Deferred map check resumes after reconnect without mid-map remount or reload");
    Done(3797047889);
}, () => time);

time = 0;
Scenario("Removed item and unload", (core, manager, config) =>
{
    manager.CheckForMapUpdates(); core.Scheduler.Run(); Done(3797047889, result: EResult.k_EResultNoConnection);
    config.Addons = []; time = 30_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 1, "Removing configured item cancels queued retry");
    config.Addons = ["3797047889"]; manager.CheckForMapUpdates(); core.Scheduler.Run();
    Done(3797047889, result: EResult.k_EResultNoConnection);
    manager.Dispose(); time = 90_000; manager.PrintDownloadProgress();
    Check(SteamGameServerUGC.Requests.Count == 2, "Unloading plugin prevents queued retry from running");
}, () => time);

Scenario("Cold start and repeated Steam activation", (core, manager, config) =>
{
    manager.OnSteamAPIActivated(); core.Scheduler.Run();
    Check(core.GameFileSystem.Mounts == 1 && core.Engine.Commands.Count == 1,
        "First late Steam activation mounts existing asset and requests one precache reload");
    manager.OnSteamAPIActivated(); core.Scheduler.Run();
    manager.RefreshAddons(); manager.CheckForMapUpdates(); core.Scheduler.Run();
    Check(core.GameFileSystem.Unmounts == 0 && core.GameFileSystem.Mounts == 1,
        "Repeated activation and map startup retain the open VPK during background download");
    Check(core.Engine.Commands.Count == 1 && SteamGameServerUGC.Requests.Count == 1,
        "Repeated Steam activation neither loops map reloads nor duplicates downloads");
    Done(3797047889, 20);
    Check(core.GameFileSystem.Unmounts == 0 && core.Engine.Commands.Count == 1,
        "Confirmed background update leaves live map untouched");
    manager.RefreshAddons();
    Check(core.GameFileSystem.Unmounts == 1 && core.GameFileSystem.Mounts == 2
        && manager.AddonStatus("3797047889").Contains("eingebunden"),
        "Confirmed new revision remounts on the next natural map startup");
    manager.OnSteamAPIActivated(); core.Scheduler.Run();
    Check(core.Engine.Commands.Count == 1, "Later Steam activation does not reload already mounted content");
});

Scenario("Removed mounted asset during update", (core, manager, config) =>
{
    manager.RefreshAddons(); manager.CheckForMapUpdates(); core.Scheduler.Run();
    config.Addons = []; manager.RefreshAddons();
    Check(core.GameFileSystem.Unmounts == 1,
        "Explicitly removed addon unmounts even when its update is pending");
});

Console.WriteLine($"PASS: {passed} assertions; production workshop manager exercised with fake Steam/engine boundaries.");
