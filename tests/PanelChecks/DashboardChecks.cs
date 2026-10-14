using CS2ServerPortal;
using System.Net;
using System.Text.Json;

internal static class DashboardChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static async Task Run()
    {
        using var primaryJson = JsonDocument.Parse("""
        {"relationships":{"allocations":{"data":[
          {"attributes":{"ip":"192.0.2.5","port":27016,"is_default":false}},
          {"attributes":{"ip":"2001:db8::1","ip_alias":"games.example.test","port":27015,"is_default":true}}
        ]}},"sftp_details":{"ip":"NOT_A_GAME_ENDPOINT","port":2022}}
        """);
        var (ip, port, alias) = PanelClient.ParseAllocation(primaryJson.RootElement);
        Check(ip == "2001:db8::1" && port == 27015 && alias == "games.example.test", "Primary game allocation/IPv6 incorrectly selected.");
        using var missing = JsonDocument.Parse("{\"sftp_details\":{\"ip\":\"NOT_A_GAME_ENDPOINT\",\"port\":2022}}");
        Check(PanelClient.ParseAllocation(missing.RootElement).Ip == null, "SFTP must not be displayed as game IP.");
        const string key = "pt" + "lc_LOCAL_FIXTURE_NOT_A_REAL_KEY";
        string serverBody = "{\"data\":[{\"attributes\":{\"identifier\":\"12345678\",\"name\":\"Fixture\",\"node\":\"Test node\"," + primaryJson.RootElement.GetRawText()[1..^1] + "}}]}";
        using (var client = new PanelClient(new("https://panel.example.test", key), new FixtureHandler(HttpStatusCode.OK, serverBody)))
        {
            var rows = await client.GetServersAsync(false);
            Check(rows.Single().Ip == ip && rows[0].Port == port && rows[0].Node == "Test node", "Server API allocation not carried into dashboard model.");
        }
        var now = DateTimeOffset.Parse("2026-10-11T10:00:00Z");
        var server = new ServerInfo("12345678", "Test", "running", "2000930", null, "192.0.2.1", 27015);
        var old = new RuntimeAddon("Parachute", "Parachute", "v5", "", "Loaded", "/old");
        var next = new RuntimeAddon("LPLK_Parachute", "LPLK_Parachute", "1.0.0", "1.4.13", "Loaded", "/new");
        AddonRuntimeReport Report(DateTimeOffset at, params RuntimeAddon[] plugins) => new(at, "Fixture", [new(server.Identifier, at, true, plugins.ToList(), null)]);
        var state = new DashboardState();
        DashboardChanges.Apply(state, [server], Report(now, old), now);
        Check(state.History.Count == 1 && state.History[0].Kind == "Erstbestand erfasst", "Initial baseline must not claim installation.");
        DashboardChanges.Apply(state, [server], Report(now, old), now);
        DashboardChanges.Apply(state, [server], Report(now.AddSeconds(15), old), now.AddSeconds(15));
        Check(state.History.Count == 1, "Unchanged refresh created history duplicates.");
        Check(state.Servers[0].IsCurrent(now.AddSeconds(20)) && !state.Servers[0].IsCurrent(now.AddMinutes(10)), "Cached freshness must age without new fetches.");
        var failed = new AddonRuntimeReport(now.AddSeconds(30), "Fixture", [new(server.Identifier, null, false, [], "Unavailable")]);
        DashboardChanges.Apply(state, [server], failed, now.AddSeconds(30));
        Check(state.Servers[0].Plugins.Single().Id == old.Id && state.History.Count == 1, "Failed read incorrectly removed plugins.");
        DashboardChanges.Apply(state, [server], Report(now.AddMinutes(-10)), now.AddMinutes(1));
        Check(state.Servers[0].Plugins.Count == 1, "Stale empty snapshot removed plugins.");
        DashboardChanges.Apply(state, [server], Report(now.AddMinutes(5)), now.AddMinutes(1));
        Check(state.Servers[0].Plugins.Count == 1, "Future snapshot replaced current baseline.");
        DashboardChanges.Apply(state, [server], Report(now.AddSeconds(10)), now.AddMinutes(1));
        Check(state.Servers[0].Plugins.Count == 1 && state.Servers[0].Error != null, "Out-of-order snapshot accepted.");
        DashboardChanges.Apply(state, [server], Report(now.AddMinutes(1), old, old), now.AddMinutes(1));
        Check(state.Servers[0].Plugins.Count == 1 && state.History.Count == 1, "Duplicate plugin IDs accepted.");
        DashboardChanges.Apply(state, [server with { Status = "offline" }], Report(now.AddMinutes(1)), now.AddMinutes(1));
        Check(state.Servers[0].Plugins.Count == 1, "Offline bridge file interpreted as live removal.");
        DashboardChanges.Apply(state, [server], Report(now.AddMinutes(1), old with { Version = "v6", Status = "Error" }), now.AddMinutes(1));
        Check(state.History.Last().Before.Contains("v5") && state.History.Last().After.Contains("v6") && state.History.Last().After.Contains("Error"), "Version/status change missing previous data.");
        var plan = new AddonPlan("LPLK_Parachute", "Fallschirm", ["Parachute"], 1, "Geplant", "Test only");
        DashboardChanges.SavePlan(state, plan, true, now);
        DashboardChanges.Apply(state, [server], Report(now.AddMinutes(2), next), now.AddMinutes(2));
        Check(state.History.Count(h => h.Kind == "Neu gemeldet") == 1 && state.History.Count(h => h.Kind == "Nicht mehr gemeldet") == 1, "Observed replacement transitions missing.");
        Check(DashboardChanges.Targets(state, "Parachute") == next.Id && state.Plans[0].Stage == "Geplant", "Runtime must not silently approve migration plan.");
        int count = state.History.Count;
        DashboardChanges.SavePlan(state, plan, false, now);
        Check(state.History.Count == count, "Unchanged plan created duplicate history.");
        DashboardChanges.SavePlan(state, plan with { Stage = "Im Test", Replaces = ["Parachute", "Legacy"] }, false, now.AddMinutes(3));
        Check(state.History.Last().Before.Contains("Geplant") && state.History.Last().After.Contains("Legacy"), "Plan history lost replacement mapping.");
        try { DashboardChanges.SavePlan(state, plan with { Stage = "Abgenommen" }, false, now, plan); throw new Exception("Stale dialog overwrote a newer plan."); }
        catch (InvalidOperationException) { }
        Check(state.Plans[0].Stage == "Im Test", "Concurrent plan edit lost.");
        try { DashboardChanges.SavePlan(state, plan with { Id = "lplk_Parachute" }, true, now); throw new Exception("Invalid/duplicate name accepted."); }
        catch (ArgumentException) { }
        DashboardChanges.Apply(state, [], new(now.AddMinutes(4), "Fixture", []), now.AddMinutes(4));
        Check(!state.Servers[0].Listed && state.Servers[0].Plugins.Count == 1, "Server missing from panel erased retained history/baseline.");

        string folder = Path.Combine(Path.GetTempPath(), "LPLK-DashboardChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new DashboardStore(folder);
            store.Update(s => DashboardChanges.SavePlan(s, plan, true, now));
            Check(new DashboardStore(folder).Read().Plans.Single().Id == plan.Id, "State lost after reopen.");
            using (var locked = new FileStream(Path.Combine(folder, "state", "addon-dashboard.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { store.Update(s => s.Plans.Clear()); throw new Exception("Concurrent writer bypassed shared file lock."); }
                catch (IOException) { }
            }
            Check(store.Read().Plans.Count == 1, "Failed concurrent save changed state.");
            string path = Path.Combine(folder, "state", "addon-dashboard.json");
            File.WriteAllText(path, "broken fixture");
            try { store.Update(s => s.Plans.Clear()); throw new Exception("Corrupt state silently reset."); }
            catch (JsonException) { }
            Check(File.ReadAllText(path) == "broken fixture", "Corrupt state overwritten.");
        }
        finally
        {
            // Only these files, created inside this test's unique directory, are removed.
            foreach (string file in Directory.EnumerateFiles(Path.Combine(folder, "state"))) File.Delete(file);
            Directory.Delete(Path.Combine(folder, "state")); Directory.Delete(folder);
        }
        Console.WriteLine("PASS: dashboard game endpoints, aging, baseline/diff history, failed/stale/offline/out-of-order reads, plan transitions, persistence and concurrent-writer protection.");
    }
}
