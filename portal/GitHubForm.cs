using System.Diagnostics;

namespace CS2ServerPortal;

internal sealed class GitHubForm : Form
{
    readonly GitHubCredentialStore store;
    readonly TextBox token = new() { Dock = DockStyle.Fill, MaxLength = 1024, UseSystemPasswordChar = true, PlaceholderText = "ghp_… oder github_pat_… · gespeicherten Token weiterverwenden: leer lassen", AccessibleName = "GitHub-Zugriffstoken" };
    readonly Label account = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "Konto wird beim Speichern erkannt." };
    readonly Button save = MakeButton("Prüfen und speichern", true);
    readonly Button test = MakeButton("Repositories laden");
    readonly Button cancel = MakeButton("Abbrechen");
    readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), Text = "Noch kein GitHub-Zugang eingerichtet." };
    readonly Label summary = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(1050, 0), Margin = new Padding(0, 4, 0, 10), Text = "Nach der Anmeldung erscheinen hier die Repositories, auf die dein Token zugreifen kann. Die Prüfung verändert keine Repositories." };
    readonly TextBox filter = new() { Dock = DockStyle.Fill, PlaceholderText = "Repositories nach Besitzer oder Namen filtern …", AccessibleName = "Repository-Liste filtern" };
    readonly DataGridView repositories = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Erreichbare GitHub-Repositories"
    };
    readonly FlowLayoutPanel links = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    GitHubCheck? lastCheck;
    CancellationTokenSource? pending;

    static Button MakeButton(string text, bool primary = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 8),
        BackColor = primary ? Color.FromArgb(22, 51, 78) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(22, 51, 78)
    };

    public GitHubForm(string workspace, bool preview = false)
    {
        SuspendLayout();
        store = new GitHubCredentialStore(workspace);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "GitHub – Alle Repositories"; Icon = AppBranding.WindowIcon;
        Font = new Font("Segoe UI", 11); ForeColor = Color.FromArgb(29, 40, 53); BackColor = Color.White;
        ClientSize = new Size(1140, 760); MinimumSize = new Size(960, 730); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(24) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 51, 78) };
        heading.Controls.Add(new Label { Text = "GITHUB  /  DEINE REPOSITORIES", AutoSize = true, Location = new Point(16, 7), Font = new Font("Segoe UI Semibold", 20), ForeColor = Color.White });
        heading.Controls.Add(new Label { Text = "Zentraler Kontozugang · private und öffentliche Projekte", AutoSize = true, Location = new Point(18, 46), ForeColor = Color.FromArgb(210, 222, 233) });
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Trage deinen allgemeinen GitHub-Token ein. Eine Repository-Adresse ist nicht erforderlich.\nClassic: repo für Repository-Zugriff; workflow für Änderungen an Actions-Dateien.\nFine-grained: All repositories beim gewünschten Besitzer auswählen und die benötigten Rechte vergeben.",
            AutoSize = true, MaximumSize = new Size(1050, 0), Margin = new Padding(0, 14, 0, 12)
        }, 0, 1);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(new Label { Text = "GitHub-Konto", AutoSize = true }, 0, 0); fields.Controls.Add(account, 1, 0);
        fields.Controls.Add(new Label { Text = "Zugriffstoken", AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, 0, 1); fields.Controls.Add(token, 1, 1);
        var hint = new Label { Text = "Der Token bleibt verschlüsselt für dieses Windows-Konto gespeichert. Nach dem Speichern bleibt das Tokenfeld leer.", AutoSize = true, MaximumSize = new Size(1050, 0), ForeColor = Color.FromArgb(72, 84, 96), Margin = new Padding(0, 8, 0, 10) };
        fields.Controls.Add(hint, 0, 2); fields.SetColumnSpan(hint, 2); layout.Controls.Add(fields, 0, 2);
        AddLink("Classic-Token erstellen", () => "https://github.com/settings/tokens/new");
        AddLink("Fine-grained Token erstellen", () => "https://github.com/settings/personal-access-tokens/new");
        AddLink("GitHub öffnen", () => "https://github.com/");
        layout.Controls.Add(links, 0, 3);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 8, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        actions.Controls.Add(save, 0, 0); actions.Controls.Add(test, 1, 0); actions.Controls.Add(cancel, 2, 0); cancel.Enabled = false;
        layout.Controls.Add(actions, 0, 4); layout.Controls.Add(summary, 0, 5); layout.Controls.Add(filter, 0, 6);
        repositories.Columns.Add("name", "Repository"); repositories.Columns.Add("visibility", "Sichtbarkeit"); repositories.Columns.Add("rights", "Kontoberechtigung"); repositories.Columns.Add("state", "Status");
        repositories.Columns[0].FillWeight = 230; repositories.Columns[1].FillWeight = 90; repositories.Columns[2].FillWeight = 125; repositories.Columns[3].FillWeight = 90;
        repositories.DefaultCellStyle.Padding = new Padding(2, 5, 2, 5);
        layout.Controls.Add(repositories, 0, 7); layout.Controls.Add(status, 0, 8);
        filter.TextChanged += (_, _) => ShowRepositories();
        repositories.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedRepository(); };
        repositories.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; OpenSelectedRepository(); } };
        cancel.Click += (_, _) => pending?.Cancel();
        save.Click += async (_, _) => await RunBusy(async ct =>
        {
            var connection = token.Text.Trim().Length > 0 ? new GitHubConnection(token.Text.Trim())
                : store.Read() ?? throw new InvalidOperationException("Bitte einen GitHub-Token eintragen.");
            using var client = new GitHubClient(connection);
            var check = await client.CheckAsync(ct);
            ct.ThrowIfCancellationRequested();
            store.Save(connection with { AccountId = check.AccountId, Login = check.Login });
            token.Clear(); ShowResult(check);
            status.Text = "GitHub-Zugang für " + check.Login + " verschlüsselt gespeichert. Repositories per Doppelklick öffnen.";
        });
        test.Click += async (_, _) => await RunBusy(async ct =>
        {
            if (token.Text.Length > 0) throw new InvalidOperationException("Ein neuer Token wurde eingegeben. Bitte zuerst „Prüfen und speichern“ verwenden.");
            var connection = store.Read() ?? throw new InvalidOperationException("Bitte zuerst den GitHub-Zugang prüfen und speichern.");
            using var client = new GitHubClient(connection);
            ShowResult(await client.CheckAsync(ct));
            status.Text = "Repository-Liste aktualisiert. Repositories per Doppelklick öffnen.";
        });
        Shown += (_, _) =>
        {
            if (preview)
            {
                ShowResult(new("beispielkonto", 42, "Classic", [new(1, "beispielkonto/LPLK", "https://github.com/beispielkonto/LPLK", "private", false, false, true, true, true), new(2, "beispielkonto/Website", "https://github.com/beispielkonto/Website", "public", false, false, true, true, true), new(3, "beispielkonto/Archiv", "https://github.com/beispielkonto/Archiv", "private", true, false, true, true, true)], true, false, ["repo", "workflow"], DateTimeOffset.UtcNow));
                status.Text = "Vorschau mit erfundenen Beispieldaten. Keine Verbindung zu GitHub.";
                return;
            }
            try
            {
                var saved = store.Read(); if (saved == null) return;
                account.Text = saved.Login ?? "Gespeicherter Zugang – Konto noch nicht geprüft";
                status.Text = "Gespeicherter GitHub-Zugang geladen. „Repositories laden“ prüft den aktuellen Zugriff.";
            }
            catch (Exception ex) { status.Text = SafeError.Message(ex); }
        };
        FormClosing += (_, e) =>
        {
            if (pending != null) { pending.Cancel(); e.Cancel = true; status.Text = "Abfrage wird abgebrochen. Danach kann das Fenster geschlossen werden."; }
            else if (!preview && token.Text.Length > 0 && MessageBox.Show(this, "Der eingegebene Token wurde noch nicht gespeichert. Fenster trotzdem schließen?", "GitHub-Zugang", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) e.Cancel = true;
        };
        FormClosed += (_, _) => token.Clear(); ResumeLayout(true);
    }

    void AddLink(string label, Func<string> url)
    {
        var link = new LinkLabel { Text = label, AutoSize = true, Margin = new Padding(0, 0, 24, 8) };
        link.LinkClicked += (_, _) => OpenUrl(url()); links.Controls.Add(link);
    }
    void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = SafeError.Message(ex); }
    }
    void OpenSelectedRepository() { if (pending == null && repositories.CurrentRow?.Tag is GitHubRepositoryInfo repo) OpenUrl(repo.Url); }

    void ShowResult(GitHubCheck check)
    {
        lastCheck = check; account.Text = check.Login + " · " + check.TokenKind;
        string completeness = check.ListingComplete ? "Erreichbare Liste geladen." : "Liste wegen der Abfragegrenze unvollständig.";
        string scopes = check.ReportedScopes.Count == 0 ? "Tokenrechte nicht vollständig auslesbar." : "Gemeldete Tokenrechte: " + string.Join(", ", check.ReportedScopes) + ".";
        summary.Text = $"{check.Repositories.Count} erreichbare Repositories. {completeness} {scopes}\nDie Kontoberechtigung ist kein Schreibtest. Token- und Organisationsregeln können einzelne Aktionen begrenzen.";
        ShowRepositories();
    }
    void ShowRepositories()
    {
        repositories.Rows.Clear();
        if (lastCheck == null) return;
        foreach (var repo in lastCheck.Repositories.Where(r => r.FullName.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            string rights = repo.AccountCanAdmin == true ? "Administration" : repo.AccountCanPush == true ? "Schreiben" : repo.AccountCanRead == true ? "Lesen" : "Nicht gemeldet";
            string visibility = repo.Visibility switch { "private" => "Privat", "public" => "Öffentlich", _ => "Intern" };
            int row = repositories.Rows.Add(repo.FullName, visibility, rights, repo.Disabled ? "Deaktiviert" : repo.Archived ? "Archiviert" : "Aktiv");
            repositories.Rows[row].Tag = repo;
        }
    }
    async Task RunBusy(Func<CancellationToken, Task> action)
    {
        using var cancellation = new CancellationTokenSource(); pending = cancellation;
        save.Enabled = test.Enabled = token.Enabled = links.Enabled = false; cancel.Enabled = true; UseWaitCursor = true;
        lastCheck = null; repositories.Rows.Clear(); summary.Text = "Konto und erreichbare Repositories werden geprüft …";
        status.Text = "GitHub wird abgefragt …";
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { summary.Text = "Abfrage abgebrochen."; status.Text = "Gespeicherter Zugang unverändert."; }
        catch (Exception ex) { summary.Text = "Keine aktuelle Repository-Liste verfügbar."; status.Text = SafeError.Message(ex); }
        finally { pending = null; save.Enabled = test.Enabled = token.Enabled = links.Enabled = true; cancel.Enabled = false; UseWaitCursor = false; }
    }
}
