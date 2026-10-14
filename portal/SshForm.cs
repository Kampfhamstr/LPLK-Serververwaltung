using System.Text;

namespace CS2ServerPortal;

internal sealed class SshForm : Form
{
    readonly SshCredentialStore store;
    readonly TextBox host = new() { Dock = DockStyle.Fill, PlaceholderText = "IP-Adresse oder Hostname" };
    readonly NumericUpDown port = new() { Dock = DockStyle.Left, Width = 140, Minimum = 1, Maximum = 65535, Value = 22 };
    readonly TextBox username = new() { Dock = DockStyle.Fill, Text = "root", MaxLength = 64 };
    readonly TextBox password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 4096, PlaceholderText = "Passwort eingeben; gespeichert unverändert lassen: leer" };
    readonly TextBox fingerprint = new() { Dock = DockStyle.Fill, ReadOnly = true, Text = "Noch keine Serverkennung bestätigt.", BorderStyle = BorderStyle.None, TabStop = false };
    readonly TextBox result = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(241, 245, 249), BorderStyle = BorderStyle.FixedSingle };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 10, 0, 0), Text = "SSH-Verbindung noch nicht eingerichtet." };
    readonly Button save = Button("Prüfen und speichern", true);
    readonly Button test = Button("Verbindung testen");
    readonly Button diagnose = Button("Diagnose abrufen");
    bool busy;

    static Button Button(string text, bool primary = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 8),
        BackColor = primary ? Color.FromArgb(22, 51, 78) : Color.White,
        ForeColor = primary ? Color.White : Color.FromArgb(22, 51, 78)
    };

    public SshForm(string workspace)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        store = new SshCredentialStore(workspace);
        Text = "Debian / SSH – CS2 Serververwaltung";
        Font = new Font("Segoe UI", 11);
        ForeColor = Color.FromArgb(29, 40, 53);
        BackColor = Color.White;
        Icon = AppBranding.WindowIcon;
        ClientSize = new Size(960, 700);
        MinimumSize = new Size(900, 680);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 51, 78) };
        heading.Controls.Add(new Label { Text = "DEBIAN  /  SSH", AutoSize = true, Location = new Point(16, 7), Font = new Font("Segoe UI Semibold", 20), ForeColor = Color.White });
        heading.Controls.Add(new Label { Text = "Zugang speichern · Verbindung testen · Serverdiagnose abrufen", AutoSize = true, Location = new Point(18, 44), ForeColor = Color.FromArgb(210, 222, 233) });
        layout.Controls.Add(heading, 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 14, 0, 12) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var names = new[] { "Serveradresse", "SSH-Port", "Benutzer", "Passwort", "Serverkennung" };
        Control[] controls = [host, port, username, password, fingerprint];
        for (int i = 0; i < controls.Length; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = names[i], AutoSize = true, Padding = new Padding(0, 4, 0, 0) }, 0, i);
            fields.Controls.Add(controls[i], 1, i);
        }
        var hint = new Label { Text = "Die Verbindung wird für dein Windows-Konto verschlüsselt gespeichert. Der Verbindungstest und die Diagnose lesen nur Serverinformationen.", AutoSize = true, MaximumSize = new Size(860, 0), ForeColor = Color.FromArgb(72, 84, 96), Margin = new Padding(0, 10, 0, 4) };
        fields.Controls.Add(hint, 0, 5);
        fields.SetColumnSpan(hint, 2);
        layout.Controls.Add(fields, 0, 1);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.Controls.Add(save, 0, 0);
        actions.Controls.Add(test, 1, 0);
        actions.Controls.Add(diagnose, 2, 0);
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(result, 0, 3);
        layout.Controls.Add(status, 0, 4);

        save.Click += async (_, _) => await RunBusy(async () =>
        {
            var existing = store.Read();
            var target = SshCredentialStore.ValidateHost(host.Text);
            int targetPort = (int)port.Value;
            var user = username.Text.Trim();
            bool sameHost = existing != null && string.Equals(existing.Host, target, StringComparison.OrdinalIgnoreCase) && existing.Port == targetPort;
            var secret = password.Text;
            if (secret.Length == 0 && sameHost && existing!.Username == user) secret = existing.Password;
            var connection = new SshConnection(target, targetPort, user, secret, sameHost ? existing!.HostKeySha256 : "");
            SshCredentialStore.Validate(connection, false);
            if (!sameHost)
            {
                status.Text = "SSH-Serverkennung wird abgerufen …";
                var key = await Task.Run(() => SshService.InspectHostKey(target, targetPort));
                var answer = MessageBox.Show(this,
                    $"Server: {target}:{targetPort}\nSchlüsseltyp: {key.Algorithm}\n\n{key.Sha256}\n\nVergleiche diese Kennung mit der Serverkonsole deines Hosters oder einer bereits geprüften SSH-Verbindung.\n\nStimmt die Kennung überein und soll diese Verbindung gespeichert werden?",
                    "SSH-Serverkennung bestätigen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { status.Text = "Abgebrochen. Keine Zugangsdaten gesendet oder gespeichert."; return; }
                connection = connection with { HostKeySha256 = key.Sha256 };
            }
            status.Text = "SSH-Anmeldung wird geprüft …";
            var check = await Task.Run(() => SshService.TestConnection(connection));
            store.Save(connection);
            password.Clear();
            host.Text = target;
            fingerprint.Text = connection.HostKeySha256;
            result.Text = Normalize(check.Output);
            status.Text = $"SSH-Verbindung für {user}@{target}:{targetPort} gespeichert. Diagnosezugriff ist bereit.";
        });
        test.Click += async (_, _) => await RunBusy(async () =>
        {
            var connection = SavedConnection();
            var check = await Task.Run(() => SshService.TestConnection(connection));
            result.Text = Normalize(check.Output);
            status.Text = "Die gespeicherte SSH-Verbindung funktioniert. Es wurden keine Einstellungen geändert.";
        });
        diagnose.Click += async (_, _) => await RunBusy(async () =>
        {
            var connection = SavedConnection();
            var report = await Task.Run(() => SshService.Diagnose(connection));
            var text = new StringBuilder();
            foreach (var section in report.Results)
            {
                text.AppendLine("=== " + section.Section + " ===").AppendLine(section.Output);
                if (section.Error.Length > 0) text.AppendLine(section.Error);
                if (section.ExitCode != 0) text.AppendLine("Rückgabewert: " + section.ExitCode);
                text.AppendLine();
            }
            result.Text = Normalize(text.ToString());
            status.Text = "Diagnose abgerufen: System, Auslastung, Speicher, Dienste und Container. Keine Serveränderungen ausgeführt.";
        });
        Shown += (_, _) =>
        {
            try
            {
                var saved = store.Read();
                if (saved == null) return;
                host.Text = saved.Host;
                port.Value = saved.Port;
                username.Text = saved.Username;
                fingerprint.Text = saved.HostKeySha256;
                status.Text = "Gespeicherte SSH-Verbindung geladen. Das Passwort bleibt ausgeblendet.";
            }
            catch (Exception ex) { status.Text = SafeError.Message(ex); }
        };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Bitte die laufende Verbindungsprüfung abwarten."; } };
        FormClosed += (_, _) => password.Clear();
        ResumeLayout(true);
    }

    SshConnection SavedConnection()
    {
        var saved = store.Read() ?? throw new InvalidOperationException("Bitte zuerst die SSH-Verbindung prüfen und speichern.");
        if (!string.Equals(saved.Host, host.Text.Trim(), StringComparison.OrdinalIgnoreCase) || saved.Port != (int)port.Value || saved.Username != username.Text.Trim() || password.Text.Length > 0)
            throw new InvalidOperationException("Die Eingaben wurden geändert. Bitte zuerst „Prüfen und speichern“ verwenden.");
        return saved;
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

    async Task RunBusy(Func<Task> action)
    {
        busy = true;
        save.Enabled = test.Enabled = diagnose.Enabled = host.Enabled = port.Enabled = username.Enabled = password.Enabled = false;
        UseWaitCursor = true;
        status.Text = "SSH-Verbindung wird geprüft …";
        try { await action(); }
        catch (Exception ex) { status.Text = SafeError.Message(ex); }
        finally
        {
            busy = false;
            save.Enabled = test.Enabled = diagnose.Enabled = host.Enabled = port.Enabled = username.Enabled = password.Enabled = true;
            UseWaitCursor = false;
        }
    }
}
