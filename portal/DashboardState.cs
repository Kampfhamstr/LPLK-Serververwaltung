using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed class DashboardState
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset? RefreshedAt { get; set; }
    public string? RefreshError { get; set; }
    public List<DashboardServer> Servers { get; set; } = [];
    public List<AddonPlan> Plans { get; set; } = [];
    public List<AddonHistory> History { get; set; } = [];
}

internal sealed class DashboardServer
{
    public ServerInfo Info { get; set; } = new("", "", "Unbekannt", null, null);
    public DateTimeOffset? ServerCheckedAt { get; set; }
    public bool Listed { get; set; } = true;
    public DateTimeOffset? SnapshotAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? Error { get; set; }
    public List<RuntimeAddon> Plugins { get; set; } = [];
    public bool IsCurrent(DateTimeOffset now) => Listed && Info.Status == "running" && Error == null && SnapshotAt != null &&
        now >= SnapshotAt && now - SnapshotAt <= TimeSpan.FromMinutes(2);
}

internal sealed record AddonPlan(string Id, string Function, List<string> Replaces, int Priority, string Stage, string Notes);
internal sealed record AddonHistory(DateTimeOffset ObservedAt, DateTimeOffset? SnapshotAt, string ServerId,
    string ServerName, string Kind, string PluginId, string Before, string After, string Notes);

internal static class DashboardChanges
{
    internal static readonly string[] Stages = ["Geplant", "In Entwicklung", "Bereit für Test", "Im Test", "Abgenommen", "Zurückgestellt"];
    internal static string Format(RuntimeAddon p) => $"{p.Version} · {p.Status}";

    internal static void Apply(DashboardState state, IEnumerable<ServerInfo> servers, AddonRuntimeReport runtime, DateTimeOffset now)
    {
        var incoming = servers.ToList();
        if (incoming.Select(s => s.Identifier).Distinct(StringComparer.Ordinal).Count() != incoming.Count)
            throw new InvalidOperationException("Doppelte Serverkennungen; Bestand nicht gespeichert.");
        foreach (var old in state.Servers) old.Listed = false;
        foreach (var info in incoming)
        {
            var server = state.Servers.Find(s => s.Info.Identifier == info.Identifier);
            if (server == null) { server = new() { Info = info }; state.Servers.Add(server); }
            else if (server.ServerCheckedAt != null && (server.Info.Ip != info.Ip || server.Info.Port != info.Port || server.Info.Name != info.Name))
                state.History.Add(new(now, null, info.Identifier, info.Name, "Serverdaten geändert", "", Address(server.Info), Address(info), "Panel-Abfrage"));
            server.Info = info;
            server.Listed = true;
            server.ServerCheckedAt = now;
            server.LastAttemptAt = now;
            var snapshot = runtime.Servers.FirstOrDefault(s => s.Server == info.Identifier);
            if (snapshot == null)
            {
                server.Error = info.Cs2Version == null ? "Kein CS2-/SwiftlyS2-Bestand ermittelt." : "Keine Plugin-Abfrage vorhanden.";
                continue;
            }
            if (snapshot.Error != null || !snapshot.FreshAtRead || snapshot.SnapshotAt == null || snapshot.SnapshotAt > now ||
                now - snapshot.SnapshotAt > TimeSpan.FromMinutes(3))
            { server.Error = snapshot.Error ?? "Bridge-Snapshot veraltet oder Zeitstempel ungültig; letzter bestätigter Bestand bleibt erhalten."; continue; }
            if (info.Status != "running")
            { server.Error = "Server nicht als laufend bestätigt; letzter Pluginbestand bleibt erhalten."; continue; }
            if (snapshot.Plugins.Any(p => string.IsNullOrWhiteSpace(p.Id)) ||
                snapshot.Plugins.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Plugins.Count)
            { server.Error = "Ungültige oder doppelte Plugin-IDs; letzter bestätigter Bestand bleibt erhalten."; continue; }
            if (server.SnapshotAt != null && snapshot.SnapshotAt < server.SnapshotAt)
            { server.Error = "Älterer Snapshot ignoriert; letzter bestätigter Bestand bleibt erhalten."; continue; }
            if (server.SnapshotAt == snapshot.SnapshotAt)
            { server.Error = null; continue; }
            var before = server.Plugins.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var after = snapshot.Plugins.ToDictionary(p => p.Id, StringComparer.Ordinal);
            foreach (var p in snapshot.Plugins)
            {
                if (!before.TryGetValue(p.Id, out var old))
                    Add(server.SnapshotAt == null ? "Erstbestand erfasst" : "Neu gemeldet", p.Id, "", Format(p), "Erfassungszeit, kein nachgewiesenes Installationsdatum.");
                else if (old.Version != p.Version || old.Status != p.Status || old.Path != p.Path)
                    Add(old.Version != p.Version ? "Version geändert" : old.Status != p.Status ? "Ladezustand geändert" : "Pluginpfad geändert",
                        p.Id, Format(old), Format(p), old.Path != p.Path ? $"Pfad: {old.Path} → {p.Path}" : "Bridge-Laufzeitmeldung");
            }
            foreach (var old in server.Plugins.Where(p => !after.ContainsKey(p.Id)))
                Add("Nicht mehr gemeldet", old.Id, Format(old), "Nicht im neuen Snapshot", "Keine Aussage über gelöschte Dateien oder eine endgültige Deinstallation.");
            server.Plugins = snapshot.Plugins.ToList();
            server.SnapshotAt = snapshot.SnapshotAt;
            server.Error = null;

            void Add(string kind, string id, string previous, string next, string note) =>
                state.History.Add(new(now, snapshot.SnapshotAt, info.Identifier, info.Name, kind, id, previous, next, note));
        }
        state.RefreshedAt = now;
        state.RefreshError = null;
    }

    internal static string Address(ServerInfo s) => $"{s.Name} · {s.Ip ?? "IP unbekannt"}:{s.Port?.ToString() ?? "?"}";
    internal static string Targets(DashboardState state, string id) => string.Join(" + ", state.Plans.Where(p => p.Replaces.Contains(id, StringComparer.Ordinal)).OrderBy(p => p.Priority).ThenBy(p => p.Id).Select(p => p.Id));

    internal static void SavePlan(DashboardState state, AddonPlan plan, bool isNew, DateTimeOffset now, AddonPlan? expected = null)
    {
        if (!Regex.IsMatch(plan.Id, "^LPLK_[A-Za-z][A-Za-z0-9_]{1,60}$")) throw new ArgumentException("Name muss eindeutig sein und mit LPLK_ beginnen (Buchstaben, Zahlen, Unterstrich).");
        if (string.IsNullOrWhiteSpace(plan.Function) || plan.Function.Length > 240 || plan.Notes.Length > 4000 ||
            plan.Priority is < 1 or > 999 || !Stages.Contains(plan.Stage)) throw new ArgumentException("Funktion, Priorität oder Bearbeitungsstand ungültig.");
        plan = plan with { Replaces = plan.Replaces.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList() };
        if (plan.Replaces.Any(s => s.Length > 100 || s.Any(char.IsControl) || s.Equals(plan.Id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ungültiger Vorgänger; ein Addon darf nicht sich selbst ersetzen.");
        var old = state.Plans.FirstOrDefault(p => p.Id.Equals(plan.Id, StringComparison.OrdinalIgnoreCase));
        if (isNew && old != null) throw new ArgumentException("Dieser LPLK-Name ist bereits vergeben.");
        if (!isNew && old == null) throw new InvalidOperationException("Planeintrag nicht mehr vorhanden. Ansicht neu laden.");
        string Describe(AddonPlan p) => $"{p.Stage} | Prio {p.Priority} | {p.Function} | ersetzt: {string.Join(", ", p.Replaces)} | {p.Notes}";
        if (expected != null && old != null && Describe(expected) != Describe(old))
            throw new InvalidOperationException("Dieser Plan wurde inzwischen in einer anderen Ansicht geändert. Bitte die Ansicht aktualisieren und erneut bearbeiten.");
        if (old != null && Describe(old) == Describe(plan)) return;
        if (old != null) state.Plans.Remove(old);
        state.Plans.Add(plan);
        state.History.Add(new(now, null, "", "Planung", old == null ? "Addon geplant" : "Plan geändert", plan.Id, old == null ? "" : Describe(old), Describe(plan), "Planung ändert keine Serverinstallation."));
    }
}

internal sealed class DashboardStore(string workspace)
{
    readonly string folder = Path.Combine(workspace, "state");
    string FilePath => Path.Combine(folder, "addon-dashboard.json");

    internal DashboardState Read()
    {
        if (!File.Exists(FilePath)) return Seed();
        var state = JsonSerializer.Deserialize<DashboardState>(File.ReadAllText(FilePath), Program.JsonOptions)
            ?? throw new InvalidOperationException("Dashboard-Datei ist leer. Sie wurde nicht überschrieben.");
        if (state.SchemaVersion != 1 || state.Servers == null || state.History == null || state.Plans == null)
            throw new InvalidOperationException("Dashboard-Dateiformat wird nicht unterstützt. Datei bleibt erhalten.");
        return state;
    }

    internal DashboardState Update(Action<DashboardState> update)
    {
        Directory.CreateDirectory(folder);
        // An exclusive SMB file handle also coordinates different PCs using this NAS workspace.
        using var gate = new FileStream(Path.Combine(folder, "addon-dashboard.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var state = Read();
        update(state);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Program.JsonOptions));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return state;
    }

    DashboardState Seed()
    {
        var state = new DashboardState();
        string catalogPath = Path.Combine(folder, "addon-catalog.json");
        if (File.Exists(catalogPath))
        {
            using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var items = catalog.RootElement.EnumerateArray().ToList();
            string[] first = ["LPLK_Parachute", "LPLK_BombWires", "LPLK_Maps", "LPLK_Whitelist", "LPLK_Admin", "LPLK_Sounds"];
            foreach (var id in items.SelectMany(e => e.GetProperty("target").GetString()!.Split(" + ")).Distinct(StringComparer.Ordinal))
            {
                var source = items.Where(e => e.GetProperty("target").GetString()!.Split(" + ").Contains(id)).ToList();
                int rank = Array.IndexOf(first, id);
                state.Plans.Add(new(id, string.Join(" / ", source.Select(e => e.GetProperty("function").GetString()).Distinct()),
                    source.Select(e => e.GetProperty("id").GetString()!).Distinct(StringComparer.Ordinal).ToList(), rank < 0 ? 100 : rank + 1, "Geplant",
                    string.Join(" ", source.Select(e => e.GetProperty("migration").GetString()).Distinct())));
            }
        }
        string inventoryPath = Path.Combine(folder, "addon-inventory.json"), runtimePath = Path.Combine(folder, "addon-runtime.json");
        if (File.Exists(inventoryPath))
        {
            var inventory = JsonSerializer.Deserialize<AddonInventoryReport>(File.ReadAllText(inventoryPath), Program.JsonOptions)!;
            var runtime = File.Exists(runtimePath) ? JsonSerializer.Deserialize<AddonRuntimeReport>(File.ReadAllText(runtimePath), Program.JsonOptions)! : new AddonRuntimeReport(inventory.CheckedAt, "", []);
            // The report start time may precede its individual sequential reads.
            var captured = runtime.Servers.Where(s => s.FreshAtRead && s.SnapshotAt != null).Select(s => s.SnapshotAt!.Value).DefaultIfEmpty(runtime.CheckedAt).Max();
            DashboardChanges.Apply(state, inventory.Servers.Select(s => s.Server), runtime, captured > runtime.CheckedAt ? captured : runtime.CheckedAt);
            foreach (var server in state.Servers) server.ServerCheckedAt = inventory.CheckedAt;
        }
        return state;
    }
}
