using System.Diagnostics;

namespace CS2ServerPortal;

internal sealed class MainForm : Form
{
    readonly string workspace;
    readonly CredentialStore store;
    readonly TextBox panel = new() { Dock = DockStyle.Fill, PlaceholderText = "https://panel.example.de" };
    readonly TextBox apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "Client-API-Schlüssel hier eingeben" };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Panel-Verbindung noch nicht eingerichtet.", Padding = new Padding(0, 12, 0, 0) };
    readonly Label versions = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Versionen wurden noch nicht geprüft.", Padding = new Padding(14), BackColor = Color.FromArgb(234, 239, 244) };
    readonly DataGridView servers = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    readonly Button save = MakeButton("Prüfen und speichern", true);
    readonly Button refresh = MakeButton("Server prüfen");
    readonly Button updates = MakeButton("Versionen prüfen");
    readonly Button ssh = MakeButton("Debian / SSH");
    readonly Button github = MakeButton("GitHub");
    readonly LinkLabel help = new() { Text = "Client-API-Schlüssel im Panel öffnen", AutoSize = true, Margin = new Padding(0, 6, 0, 8) };

    static Button MakeButton(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, Height = 42, Padding = new Padding(14, 7, 14, 7), FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Color.FromArgb(22, 51, 78) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(22, 51, 78), Margin = new Padding(0, 0, 10, 0)
    };

    internal readonly DashboardControl Dashboard;
    internal readonly TabControl Navigation;

    public MainForm(string workspace, bool preview = false)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        this.workspace = workspace;
        store = new CredentialStore(workspace);
        Text = "CS2 Serververwaltung – Pterodactyl & SwiftlyS2";
        Icon = AppBranding.WindowIcon;
        ShowIcon = true;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 11);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(29, 40, 53);
        ClientSize = new Size(1380, 920);
        MinimumSize = new Size(1150, 760);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(24) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 51, 78) };
        heading.Controls.Add(new PictureBox { Image = AppBranding.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Location = new Point(10, 8), TabStop = false, AccessibleName = "CS2 Serververwaltung Logo" });
        heading.Controls.Add(new Label { Text = "CS2  /  SERVERVERWALTUNG", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 21), Location = new Point(88, 9) });
        heading.Controls.Add(new Label { Text = "Pterodactyl verbinden · SwiftlyS2 prüfen · Plugins lokal bauen", AutoSize = true, ForeColor = Color.FromArgb(210, 222, 233), Location = new Point(90, 49) });
        layout.Controls.Add(heading, 0, 0);

        var connection = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 18, 0, 8) };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connection.Controls.Add(new Label { Text = "Panel-Adresse", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, 0, 0);
        connection.Controls.Add(panel, 1, 0);
        connection.Controls.Add(new Label { Text = "Client-API-Schlüssel", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, 0, 1);
        connection.Controls.Add(apiKey, 1, 1);
        connection.Controls.Add(help, 1, 2);
        var storage = new Label { Text = "Der Schlüssel wird für dein Windows-Konto verschlüsselt gespeichert. Bei bestehender Verbindung leer lassen, um den gespeicherten Schlüssel zu verwenden.", AutoSize = true, MaximumSize = new Size(780, 0), ForeColor = Color.FromArgb(72, 84, 96), Margin = new Padding(0, 0, 0, 10) };
        connection.Controls.Add(storage, 0, 3);
        connection.SetColumnSpan(storage, 2);
        layout.Controls.Add(connection, 0, 1);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        foreach (var button in new[] { save, refresh, updates, ssh, github })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 8, 8);
            actions.Controls.Add(button);
        }
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(new Label { Text = "Erreichbare Server", Font = new Font("Segoe UI Semibold", 13), AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 3);
        servers.Columns.Add("name", "Server");
        servers.Columns.Add("id", "Kennung");
        servers.Columns.Add("status", "Status");
        servers.Columns.Add("cs2", "CS2-Serverversion");
        servers.Columns[0].FillWeight = 170;
        servers.Columns[1].FillWeight = 75;
        servers.RowTemplate.Height = 34;
        servers.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10);
        layout.Controls.Add(servers, 0, 4);
        layout.Controls.Add(versions, 0, 5);
        layout.Controls.Add(status, 0, 6);

        ssh.Click += (_, _) => { using var dialog = new SshForm(workspace); dialog.ShowDialog(this); };
        github.Click += (_, _) => { using var dialog = new GitHubForm(workspace); dialog.ShowDialog(this); };

        save.Click += async (_, _) => await RunBusy(async () =>
        {
            var address = CredentialStore.ValidateUrl(panel.Text);
            var existing = store.Read();
            var key = apiKey.Text.Trim();
            if (key.Length == 0 && existing?.PanelUrl == address) key = existing.ApiKey;
            PanelAccessErrors.ValidateClientKey(key);
            var connectionData = new PanelConnection(address, key);
            using var client = new PanelClient(connectionData);
            var rows = await client.GetServersAsync(false);
            store.Save(connectionData);
            apiKey.Clear();
            ShowServers(rows);
            status.Text = $"Verbindung gespeichert. {rows.Count} Server erreichbar. Updates werden erst nach deiner Freigabe installiert.";
        });
        refresh.Click += async (_, _) => await RunBusy(async () =>
        {
            var c = store.Read() ?? throw new InvalidOperationException("Bitte zuerst die Panel-Verbindung speichern.");
            using var client = new PanelClient(c);
            var rows = await client.GetServersAsync(true);
            ShowServers(rows);
            status.Text = $"{rows.Count} Server geprüft. Unbekannte CS2-Versionen müssen anhand des Installationspfads geprüft werden.";
        });
        updates.Click += async (_, _) => await RunBusy(async () =>
        {
            var report = await UpdateCheck.RunAsync(workspace);
            ShowServers(report.Servers);
            ShowVersions(report);
            status.Text = report.Errors.Count == 0 ? "Versionsprüfung abgeschlossen. Es wurden keine Updates installiert." : string.Join("  ", report.Errors);
        });
        help.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(CredentialStore.ValidateUrl(panel.Text) + "/account/api") { UseShellExecute = true }); }
            catch (Exception ex) { status.Text = SafeError.Message(ex); }
        };
        Shown += (_, _) =>
        {
            try
            {
                var saved = store.Read();
                if (saved != null) { panel.Text = saved.PanelUrl; status.Text = "Gespeicherte Verbindung geladen. Mit „Server prüfen“ kannst du den Zugriff testen."; }
                var lastCheck = Path.Combine(workspace, "state", "latest-check.json");
                if (File.Exists(lastCheck))
                {
                    var report = System.Text.Json.JsonSerializer.Deserialize<UpdateReport>(File.ReadAllText(lastCheck), Program.JsonOptions);
                    if (report != null) ShowVersions(report);
                }
            }
            catch (Exception ex) { status.Text = SafeError.Message(ex); }
        };
        Controls.Remove(layout);
        Navigation = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11) };
        Dashboard = new DashboardControl(workspace, preview);
        var dashboardTab = new TabPage("Dashboard · alle Server & Plugins");
        dashboardTab.Controls.Add(Dashboard);
        var connectionTab = new TabPage("Verbindung & Versionen");
        connectionTab.Controls.Add(layout);
        Navigation.TabPages.Add(dashboardTab); Navigation.TabPages.Add(connectionTab);
        Controls.Add(Navigation);
        if (!preview) WindowState = FormWindowState.Maximized;
        ResumeLayout(true);
    }

    void ShowVersions(UpdateReport report)
    {
        versions.Text = $"SwiftlyS2 stabil: {report.SwiftlyStable ?? "nicht ermittelt"}   |   Plugin-API: {report.SwiftlyApiStable ?? "nicht ermittelt"}\n" +
            $"CS2 erforderliche Serverversion: {report.Cs2RequiredServerVersion ?? "nicht ermittelt"}\n" +
            $".NET SDK: installiert {report.DotnetInstalledSdk ?? "unbekannt"}, verfügbar {report.DotnetLatestSdk ?? "unbekannt"}\n" +
            $"Stand: {report.CheckedAt.LocalDateTime:dd.MM.yyyy HH:mm} · Installierte SwiftlyS2-Version im Panel prüfen.";
    }

    void ShowServers(IEnumerable<ServerInfo> rows)
    {
        servers.Rows.Clear();
        foreach (var server in rows)
        {
            int index = servers.Rows.Add(server.Name, server.Identifier, server.Status, server.Cs2Version ?? "Noch nicht ermittelt");
            servers.Rows[index].Cells[2].ToolTipText = server.Note ?? "";
        }
    }

    async Task RunBusy(Func<Task> action)
    {
        save.Enabled = refresh.Enabled = updates.Enabled = ssh.Enabled = panel.Enabled = apiKey.Enabled = help.Enabled = false;
        UseWaitCursor = true;
        status.Text = "Prüfung läuft …";
        try { await action(); }
        catch (Exception ex)
        {
            status.Text = SafeError.Message(ex);
            MessageBox.Show(this, status.Text, "CS2 Serververwaltung – Prüfung fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            save.Enabled = refresh.Enabled = updates.Enabled = ssh.Enabled = panel.Enabled = apiKey.Enabled = help.Enabled = true;
            UseWaitCursor = false;
        }
    }
}
