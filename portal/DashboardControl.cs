using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed class DashboardControl : UserControl
{
    readonly string workspace;
    readonly DashboardStore store;
    DashboardState state = new();
    readonly Label summary = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 14) };
    readonly Label message = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly TextBox search = new() { Width = 240, PlaceholderText = "Plugin oder Historie suchen …", Margin = new Padding(12, 5, 6, 0) };
    readonly Button refresh = Button("Bestand aktualisieren");
    readonly Button add = Button("Addon planen");
    readonly Button edit = Button("Plan bearbeiten");
    readonly CheckBox automatic = new() { Text = "Alle 5 Min. bei geöffnetem Programm", AutoSize = true, Checked = true, Margin = new Padding(12, 8, 0, 0) };
    readonly DataGridView servers = Grid();
    readonly DataGridView matrix = Grid();
    readonly DataGridView plans = Grid();
    readonly DataGridView history = Grid();
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 300000 };
    readonly System.Windows.Forms.Timer ageTimer = new() { Interval = 30000 };
    readonly Label detail = new() { Dock = DockStyle.Fill, AutoEllipsis = true, BackColor = Color.FromArgb(239, 244, 249), Padding = new Padding(8), Text = "Zelle auswählen für Angaben zu Quelle, Alter und geplantem Ersatz." };
    readonly bool preview;
    readonly TableLayoutPanel layout;
    bool busy;

    internal static string Time(DateTimeOffset? value) => value == null ? "—" : TimeZoneInfo.ConvertTimeBySystemTimeZoneId(value.Value, "W. Europe Standard Time").ToString("dd.MM.yy HH:mm");
    internal static Button Button(string text) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 0) };
    internal static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
        MultiSelect = false, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        Font = new Font("Segoe UI", 9), RowTemplate = { Height = 21 }, EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(22, 51, 78), ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9), WrapMode = DataGridViewTriState.True },
        DefaultCellStyle = new() { SelectionBackColor = Color.FromArgb(209, 228, 249), SelectionForeColor = Color.FromArgb(20, 35, 50) },
        AlternatingRowsDefaultCellStyle = new() { BackColor = Color.FromArgb(247, 249, 251) },
        ColumnHeadersHeight = 36, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
    };

    public DashboardControl(string workspace, bool preview = false)
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        this.workspace = workspace;
        this.preview = preview;
        store = new(workspace);
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;
        Padding = new Padding(12, 4, 12, 8);
        layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new(SizeType.Absolute, 32));
        layout.RowStyles.Add(new(SizeType.Absolute, 37));
        layout.RowStyles.Add(new(SizeType.Absolute, 26));
        layout.RowStyles.Add(new(SizeType.Absolute, 190));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 40));
        Controls.Add(layout);
        summary.Text = "LPLK  /  Server & Addons";
        layout.Controls.Add(summary, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.AddRange([refresh, new Label { Text = "Suche:", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }, search, automatic]);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(message, 0, 2);
        layout.Controls.Add(servers, 0, 3);
        layout.Controls.Add(tabs, 0, 4);
        layout.Controls.Add(detail, 0, 5);
        Columns(servers, [("name", "Alle Server", 185), ("profile", "Profil", 70), ("ip", "IP / Port", 145), ("alias", "Adresse / Alias", 110), ("status", "Status", 70), ("cs2", "CS2", 70), ("plugins", "Geladen", 55), ("snapshot", "Plugin-Nachweis (Berlin)", 130), ("node", "Node", 85)]);
        servers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var matrixTab = new TabPage("Alle Plugins × alle Server") { Padding = new Padding(3) };
        matrixTab.Controls.Add(matrix);
        tabs.TabPages.Add(matrixTab);
        var planTab = new TabPage("LPLK-Zielstand / Ablösungen") { Padding = new Padding(3) };
        var planLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        planLayout.RowStyles.Add(new(SizeType.Absolute, 38)); planLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var planActions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        planActions.Controls.AddRange([add, edit, new Label { AutoSize = true, Text = "Planung und tatsächliche Laufzeit werden getrennt geführt.", Padding = new Padding(6, 8, 0, 0) }]);
        planLayout.Controls.Add(planActions, 0, 0); planLayout.Controls.Add(plans, 0, 1);
        planTab.Controls.Add(planLayout); tabs.TabPages.Add(planTab);
        Columns(plans, [("priority", "Prio", 35), ("id", "Neues Addon", 135), ("function", "Funktion", 170), ("replaces", "Ersetzt / übernimmt", 170), ("stage", "Arbeitsstand", 85), ("observed", "Als geladen gemeldet auf", 170)]);
        plans.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var historyTab = new TabPage("Historie") { Padding = new Padding(3) };
        historyTab.Controls.Add(history); tabs.TabPages.Add(historyTab);
        Columns(history, [("time", "Erfasst (Berlin)", 105), ("server", "Server / Bereich", 130), ("kind", "Änderung", 120), ("plugin", "Addon", 125), ("before", "Vorher", 160), ("after", "Nachher", 160)]);
        history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        var mapReviewTab = new TabPage("Mapgrenzen / Sperren") { Padding = new Padding(3) };
        mapReviewTab.Controls.Add(new MapReviewControl(workspace, preview)); tabs.TabPages.Add(mapReviewTab);
        refresh.Click += async (_, _) => await RefreshAsync();
        search.TextChanged += (_, _) => RenderTables();
        tabs.SelectedIndexChanged += (_, _) => ShowDetail();
        matrix.SelectionChanged += (_, _) => ShowDetail();
        servers.CellClick += (_, e) => { if (e.RowIndex >= 0) detail.Text = servers.Rows[e.RowIndex].Tag as string ?? ""; };
        plans.SelectionChanged += (_, _) => ShowDetail();
        history.SelectionChanged += (_, _) => ShowDetail();
        add.Click += (_, _) => EditPlan(null);
        edit.Click += (_, _) => EditPlan(plans.CurrentRow?.Tag as AddonPlan);
        plans.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditPlan(plans.Rows[e.RowIndex].Tag as AddonPlan); };
        timer.Tick += async (_, _) => { if (automatic.Checked && Visible) await RefreshAsync(); };
        ageTimer.Tick += (_, _) => { if (!busy) RenderServersAndMatrix(); };
        Load += async (_, _) =>
        {
            ApplySizing();
            try { state = store.Read(); Render(); }
            catch (Exception ex) { message.Text = "Gespeicherte Ansicht: " + SafeError.Message(ex); }
            if (!preview)
            {
                timer.Start(); ageTimer.Start();
                try { if (new CredentialStore(workspace).Read() != null) await RefreshAsync(); }
                catch (Exception ex) { message.Text = SafeError.Message(ex); }
            }
        };
        DpiChangedAfterParent += (_, _) => { ApplySizing(); Render(); };
    }

    void ApplySizing()
    {
        float scale = DeviceDpi / 96f;
        foreach (var grid in new[] { servers, matrix, plans, history })
        {
            grid.RowTemplate.Height = grid.Font.Height + Math.Max(2, (int)scale);
            grid.ColumnHeadersHeight = grid.Font.Height + (int)(8 * scale);
            foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
        }
        layout.RowStyles[0].Height = 26 * scale;
        layout.RowStyles[1].Height = 32 * scale;
        layout.RowStyles[2].Height = 21 * scale;
        layout.RowStyles[5].Height = 25 * scale;
        // Dense overview: use only the space actually needed by the server rows.
        layout.RowStyles[3].Height = servers.ColumnHeadersHeight + Math.Max(1, state.Servers.Count) * servers.RowTemplate.Height + (int)(8 * scale);
        foreach (var button in new[] { refresh, add, edit }) { button.AutoSize = false; button.Height = (int)(30 * scale); button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + (int)(24 * scale); }
    }

    internal void SelectView(string? view) => tabs.SelectedIndex = view == "maps" ? 3 : view == "history" ? 2 : view == "plan" ? 1 : 0;
    static void Columns(DataGridView grid, (string Key, string Title, int Weight)[] fields)
    {
        foreach (var (key, title, weight) in fields)
        {
            int index = grid.Columns.Add(key, title);
            grid.Columns[index].FillWeight = weight;
            grid.Columns[index].MinimumWidth = Math.Min(70, weight);
        }
    }

    async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; refresh.Enabled = add.Enabled = edit.Enabled = false;
        try
        {
            state = await DashboardService.RefreshAsync(workspace, text => { if (!IsDisposed) message.Text = text; });
            if (!IsDisposed) Render();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { message.Text = "Abfrage fehlgeschlagen · letzter Stand bleibt erhalten: " + SafeError.Message(ex); message.ForeColor = Color.DarkRed; }
        }
        finally { busy = false; if (!IsDisposed) refresh.Enabled = add.Enabled = edit.Enabled = true; }
    }

    void Render()
    {
        int loaded = state.Servers.Sum(s => s.Plugins.Count(p => p.Status == "Loaded"));
        summary.Text = $"LPLK  /  {state.Servers.Count(s => s.Listed)} Server  ·  {state.Servers.SelectMany(s => s.Plugins).Select(p => p.Id).Distinct().Count()} Addons  ·  {loaded} zuletzt als geladen gemeldete Zuordnungen";
        message.ForeColor = state.RefreshError == null ? Color.FromArgb(65, 81, 95) : Color.DarkRed;
        message.Text = state.RefreshError ?? $"Abfrage: {Time(state.RefreshedAt)}  ·  ✓ geladen  ·  Grün: frisch  ·  Gelb: älter/ungeklärt  ·  —: nicht im Bestand  ·  ?: kein Nachweis  ·  Testziel: 1 neuer Server, 3 Profile";
        ApplySizing();
        RenderServersAndMatrix(); RenderTables();
        ShowDetail();
    }

    void RenderServersAndMatrix()
    {
        servers.Rows.Clear();
        foreach (var s in state.Servers)
        {
            string endpoint = s.Info.Ip == null ? "Noch nicht ermittelt" : $"{(s.Info.Ip.Contains(':') ? "[" + s.Info.Ip + "]" : s.Info.Ip)}:{s.Info.Port?.ToString() ?? "?"}";
            string profile = s.Info.Identifier switch { "11110007" => "Surf", "11110006" => "Zombie", _ => s.Info.Cs2Version == null ? "Unbekannt" : "CS2" };
            string status = !s.Listed ? "Nicht gelistet" : s.Info.Status switch { "running" => "Läuft", "offline" => "Aus", "starting" => "Startet", "stopping" => "Stoppt", _ => s.Info.Status };
            bool fresh = s.IsCurrent(DateTimeOffset.UtcNow);
            int n = servers.Rows.Add(s.Info.Name, profile, endpoint, s.Info.IpAlias ?? "—", status, s.Info.Cs2Version ?? "—",
                s.SnapshotAt == null ? "?" : s.Plugins.Count(p => p.Status == "Loaded").ToString(), Time(s.SnapshotAt) + (s.SnapshotAt == null ? "" : fresh ? " · frisch" : " · letzter Stand"), s.Info.Node ?? "—");
            var row = servers.Rows[n];
            row.Tag = $"{s.Info.Name} | ID: {s.Info.Identifier} | Serverabfrage: {Time(s.ServerCheckedAt)} | Bridge: {Time(s.SnapshotAt)} | {s.Error ?? s.Info.Note ?? "Kein Abfragefehler"}";
            row.Cells[7].Style.BackColor = fresh ? Color.FromArgb(225, 243, 231) : Color.FromArgb(255, 242, 210);
            foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = (string)row.Tag;
        }
        RenderMatrix();
    }

    bool Matches(string text) => string.IsNullOrWhiteSpace(search.Text) || text.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase);

    void RenderMatrix()
    {
        string? selected = matrix.CurrentRow?.Tag as string;
        int? scroll = matrix.FirstDisplayedScrollingRowIndex >= 0 ? matrix.FirstDisplayedScrollingRowIndex : null;
        matrix.Rows.Clear(); matrix.Columns.Clear();
        Columns(matrix, [("plugin", "Aktuelles Addon", 145), ("target", "LPLK-Nachfolger (Plan)", 165)]);
        matrix.Columns[0].MinimumWidth = 145; matrix.Columns[1].MinimumWidth = 165;
        foreach (var s in state.Servers)
        {
            int col = matrix.Columns.Add(s.Info.Identifier, ShortName(s.Info.Name));
            matrix.Columns[col].FillWeight = 95; matrix.Columns[col].MinimumWidth = 90;
            matrix.Columns[col].ToolTipText = $"{s.Info.Name}\nBridge-Snapshot: {Time(s.SnapshotAt)}\n{s.Error}";
        }
        var all = state.Servers.SelectMany(s => s.Plugins).Select(p => p.Id).Distinct(StringComparer.Ordinal)
            .OrderBy(id => state.Plans.Where(p => p.Replaces.Contains(id) || p.Id == id).Select(p => p.Priority).DefaultIfEmpty(999).Min()).ThenBy(id => id);
        foreach (var id in all)
        {
            var sample = state.Servers.SelectMany(s => s.Plugins).First(p => p.Id == id);
            string target = DashboardChanges.Targets(state, id);
            if (!Matches(id + " " + sample.Name + " " + target)) continue;
            int n = matrix.Rows.Add(); var row = matrix.Rows[n]; row.Tag = id;
            row.Cells[0].Value = sample.Name; row.Cells[0].ToolTipText = $"Plugin-ID: {id}";
            var targets = target.Split(" + ");
            row.Cells[1].Value = target.Length > 0 ? targets[0] + (targets.Length > 1 ? $" (+{targets.Length - 1})" : "") : state.Plans.Any(p => p.Id == id) ? "LPLK-Zieladdon" : "Noch zuzuordnen";
            row.Cells[1].ToolTipText = target.Length > 0 ? $"Geplante Ablösung: {target}. Eine Planung ist kein Installationsnachweis." : "";
            for (int i = 0; i < state.Servers.Count; i++)
            {
                var s = state.Servers[i]; var p = s.Plugins.Find(p => p.Id == id); var cell = row.Cells[i + 2];
                bool fresh = s.IsCurrent(DateTimeOffset.UtcNow);
                cell.Value = s.SnapshotAt == null ? "?" : p == null ? "—" : p.Version + (p.Status == "Loaded" ? " ✓" : " · " + p.Status);
                cell.Style.BackColor = s.SnapshotAt == null || !fresh ? Color.FromArgb(255, 242, 210) : p == null ? Color.FromArgb(244, 246, 248) : p.Status == "Loaded" ? Color.FromArgb(225, 243, 231) : Color.FromArgb(255, 225, 220);
                cell.ToolTipText = $"{s.Info.Name} | {id}\n{(p == null ? "Nicht im letzten bestätigten Snapshot; kein Nachweis einer Deinstallation." : DashboardChanges.Format(p) + "\n" + p.Path)}\nNachweis: {Time(s.SnapshotAt)} ({(fresh ? "frisch" : "letzter Stand / ungeklärt")})\n{s.Error}";
            }
            if (selected == id) matrix.CurrentCell = row.Cells[0];
        }
        if (scroll != null && matrix.Rows.Count > 0) matrix.FirstDisplayedScrollingRowIndex = Math.Min(scroll.Value, matrix.Rows.Count - 1);
        ShowDetail();
    }

    static string ShortName(string name) => Regex.Replace(name, @"\[[^\]]*\]", "").Trim().Replace("Beispielserver", "Inner Circle").Replace("Ich bin Deko, ehrlich!", "Ich bin Deko").Replace("The Hidden Headshot", "Hidden Headshot");

    void RenderTables()
    {
        RenderMatrix();
        plans.Rows.Clear(); history.Rows.Clear();
        foreach (var p in state.Plans.OrderBy(p => p.Priority).ThenBy(p => p.Id))
        {
            if (!Matches(p.Id + " " + p.Function + " " + string.Join(" ", p.Replaces))) continue;
            var found = state.Servers.Where(s => s.Plugins.Any(a => a.Id == p.Id && a.Status == "Loaded"));
            string observed = string.Join(", ", found.Select(s => ShortName(s.Info.Name) + (s.IsCurrent(DateTimeOffset.UtcNow) ? "" : " (letzter Stand)")));
            int n = plans.Rows.Add(p.Priority, p.Id, p.Function, string.Join(", ", p.Replaces), p.Stage, observed.Length == 0 ? "Noch kein Laufzeitnachweis" : observed);
            plans.Rows[n].Tag = p;
            foreach (DataGridViewCell cell in plans.Rows[n].Cells) cell.ToolTipText = p.Notes;
        }
        foreach (var h in state.History.AsEnumerable().Reverse())
        {
            if (!Matches(h.ServerName + " " + h.PluginId + " " + h.Kind + " " + h.Before + " " + h.After)) continue;
            int n = history.Rows.Add(Time(h.ObservedAt), h.ServerName, h.Kind, h.PluginId, h.Before, h.After);
            history.Rows[n].Tag = h;
            foreach (DataGridViewCell cell in history.Rows[n].Cells) cell.ToolTipText = $"{h.Notes}\nSnapshot: {Time(h.SnapshotAt)}\nVorher: {h.Before}\nNachher: {h.After}";
        }
        ShowDetail();
    }

    void ShowDetail()
    {
        if (tabs.SelectedIndex == 0 && matrix.CurrentCell is { } cell && matrix.CurrentRow?.Tag is string pluginId)
            detail.Text = cell.ColumnIndex == 0 ? $"Plugin-ID: {pluginId}" : cell.ColumnIndex == 1
                ? $"Geplante Nachfolger: {DashboardChanges.Targets(state, pluginId)}" : cell.ToolTipText.Replace('\n', ' ');
        else if (tabs.SelectedIndex == 1 && plans.CurrentRow?.Tag is AddonPlan p) detail.Text = $"{p.Id}: {p.Notes}";
        else if (tabs.SelectedIndex == 2 && history.CurrentRow?.Tag is AddonHistory h) detail.Text = $"{h.Kind}: {h.PluginId} · {h.Notes} · Snapshot: {Time(h.SnapshotAt)}";
    }

    void EditPlan(AddonPlan? plan)
    {
        using var dialog = new AddonPlanForm(plan);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { state = store.Update(s => DashboardChanges.SavePlan(s, dialog.Plan, plan == null, DateTimeOffset.UtcNow, plan)); Render(); }
        catch (Exception ex) { MessageBox.Show(this, SafeError.Message(ex), "Plan konnte nicht gespeichert werden", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); ageTimer.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class AddonPlanForm : Form
{
    readonly TextBox id = new() { Dock = DockStyle.Fill };
    readonly TextBox function = new() { Dock = DockStyle.Fill, MaxLength = 240 };
    readonly TextBox replaces = new() { Dock = DockStyle.Fill };
    readonly NumericUpDown priority = new() { Minimum = 1, Maximum = 999, Value = 100, Dock = DockStyle.Fill };
    readonly ComboBox stage = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox notes = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    internal AddonPlan Plan => new(id.Text.Trim(), function.Text.Trim(), replaces.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(), (int)priority.Value, stage.Text, notes.Text.Trim());

    internal AddonPlanForm(AddonPlan? plan)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = plan == null ? "Neues LPLK-Addon planen" : "LPLK-Plan bearbeiten";
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 460); MinimumSize = new Size(660, 440); Icon = AppBranding.WindowIcon;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(18) };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 170)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new(SizeType.Absolute, 39));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 52)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        Control[] fields = [id, function, replaces, priority, stage, notes];
        string[] labels = ["LPLK-Name", "Funktion", "Vorgänger-IDs (Komma)", "Priorität", "Arbeitsstand", "Hinweise"];
        for (int i = 0; i < fields.Length; i++) { layout.Controls.Add(new Label { Text = labels[i], AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, i); layout.Controls.Add(fields[i], 1, i); }
        stage.Items.AddRange(DashboardChanges.Stages); stage.SelectedIndex = 0;
        var hint = new Label { Dock = DockStyle.Fill, Text = "Hier änderst du die Planung. Installationen und Ablösungen auf Servern werden dadurch nicht ausgeführt. Die Änderung bleibt in der Historie erhalten." };
        layout.Controls.Add(hint, 0, 6); layout.SetColumnSpan(hint, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = DashboardControl.Button("Plan speichern"); var cancel = DashboardControl.Button("Abbrechen"); cancel.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) =>
        {
            try { DashboardChanges.SavePlan(new DashboardState(), Plan, true, DateTimeOffset.UtcNow); DialogResult = DialogResult.OK; }
            catch (Exception ex) { MessageBox.Show(this, SafeError.Message(ex), "Eingaben prüfen"); }
        };
        actions.Controls.AddRange([cancel, save]); layout.Controls.Add(actions, 0, 7); layout.SetColumnSpan(actions, 2);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        Load += (_, _) =>
        {
            float scale = DeviceDpi / 96f;
            foreach (var button in new[] { save, cancel })
            { button.AutoSize = false; button.Height = (int)(32 * scale); button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + (int)(24 * scale); }
        };
        if (plan != null) { id.Text = plan.Id; id.ReadOnly = true; function.Text = plan.Function; replaces.Text = string.Join(", ", plan.Replaces); priority.Value = plan.Priority; stage.SelectedItem = plan.Stage; notes.Text = plan.Notes; }
        else id.Text = "LPLK_";
        ResumeLayout(true);
    }
}
