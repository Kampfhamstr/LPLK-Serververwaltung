using Renci.SshNet;
using System.Security.Cryptography;

namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult TestKillRewardProbe(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Kill-Belohnungsprüfung auf leerem Testserver",PanelScript("TestKillRewardProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestBountyProbe(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Kopfgeldprüfung auf leerem Testserver",PanelScript("TestBountyProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestBonusProbe(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Lottobonusprüfung auf leerem Testserver",PanelScript("TestBonusProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestTitlesProbe(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Titelprüfung auf leerem Testserver",PanelScript("TestTitlesProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestRobberyProbe(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Bankraubprüfung auf leerem Testserver",PanelScript("TestRobberyProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestLotteryProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Lottoprüfung auf leerem Testserver",PanelScript("TestLotteryProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestBettingProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Wettprüfung auf leerem Testserver",PanelScript("TestBettingProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestEconomyProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Buchungsprüfung auf leerem Testserver",PanelScript("TestEconomyProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult ReadEconomyReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle Bankregeln nur lesen",PanelScript("EconomyReference.php"));
    }
    public static SshCommandResult TestNadeEffectsProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Fade-Prüfung auf leerem Testserver",PanelScript("TestNadeEffectsProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult ReadNadeEffectsReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle NadeBoom-Einstellungen nur lesen",PanelScript("NadeEffectsReference.php"));
    }
    public static SshCommandResult TestFluchProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Hühner-Prüfung auf leerem Testserver",PanelScript("TestFluchProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestDemo(SshConnection connection,bool start)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Demoaufnahmetest auf leerem Testserver",PanelScript("TestDemoCheck.php").Replace("__LPLK_DEMO_MODE__",start?"start":"stop").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult TestThirdPersonProbe(SshConnection connection)
    {
        using var client=Connect(connection);EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Kamera-Prüfung auf leerem Testserver",PanelScript("TestThirdPersonProbe.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"));
    }
    public static SshCommandResult ReadFluchReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle Fluch-Einstellungen nur lesen",PanelScript("FluchReference.php"));
    }
    public static SshCommandResult ReadThirdPersonReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle ThirdPerson-Einstellungen nur lesen",PanelScript("ThirdPersonReference.php"));
    }
    public static SshCommandResult ReadStatisticsReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle Statistik-/Demoregeln nur lesen",PanelScript("StatisticsReference.php"));
    }
    public static SshCommandResult ReadWeaponsReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle Waffenregeln nur lesen",PanelScript("WeaponsReference.php"));
    }
    public static SshCommandResult ReadTeamsReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle Teamregeln nur lesen",PanelScript("TeamsReference.php"));
    }
    public static SshCommandResult ReadAntiTeamFlashReference(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Inner-Circle AntiTeamFlash-Regeln und Prüfsumme nur lesen",PanelScript("AntiTeamFlashReference.php"));
    }
    private static string PanelScript(string resourceName)
    {
        using var source = typeof(SshService).Assembly.GetManifestResourceStream("CS2ServerPortal.Diagnostics." + resourceName)
            ?? throw new InvalidOperationException("Das feste Testserver-Skript fehlt.");
        using var reader = new StreamReader(source);
        string script = reader.ReadToEnd().Replace("\r\n", "\n");
        return "panel_path=''; for candidate in /var/www/pterodactyl /var/www/panel /srv/pterodactyl; do if [ -f \"$candidate/artisan\" ] && [ -f \"$candidate/bootstrap/app.php\" ]; then panel_path=\"$candidate\"; break; fi; done; " +
            "if [ -z \"$panel_path\" ]; then printf 'Panelinstallation nicht gefunden.\\n'; exit 2; fi; " +
            "cd \"$panel_path\" || exit 2; php -d display_errors=0 -d log_errors=0 <<'LPLK_FIXED_TEST_SERVER_SCRIPT'\n" + script + "\nLPLK_FIXED_TEST_SERVER_SCRIPT\n";
    }

    public static SshDiagnosticReport InspectTestServer(SshConnection connection)
    {
        using var client = Connect(connection);
        return new(DateTimeOffset.UtcNow, connection.Host, connection.Port, connection.Username,
        [
            Run(client, connection, "Testserver-Vorlage und freie Zuweisungen (nur lesend)", PanelScript("TestServerInspection.php")),
            Run(client, connection, "Dateibestand und Portbelegung (nur lesend)",
                "du -sh /var/lib/pterodactyl/volumes/11110003-0000-4000-8000-000000000003; " +
                "find /var/lib/pterodactyl/volumes/11110003-0000-4000-8000-000000000003 -maxdepth 7 -type f \\( -iname '*swiftly*' -o -name 'version.json' -o -name 'version.txt' -o -name 'steam.inf' \\) -printf '%p | %s bytes\\n' | head -60; " +
                "ss -H -lntu | awk '{print $1, $5}'; " +
                "find /var/www /srv -maxdepth 4 -type f -name composer.json -print 2>/dev/null", 60)
        ]);
    }

    public static SshCommandResult CreateTestServer(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Neuen isolierten LPLK-Testserver in Pterodactyl anlegen", PanelScript("TestServerCreate.php"), 60);
    }

    public static SshCommandResult TestServerStatus(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "LPLK-Testserver-Status und Installation", PanelScript("TestServerStatus.php"), 60,32000);
    }
    public static SshCommandResult CleanupTestServer(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Alten Start-Coredump ausschließlich im geprüften Testserver entfernen", PanelScript("TestServerCleanup.php"));
    }
    public static SshCommandResult ConfigureTestMapStorage(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Speicher für vollständige Workshop-Messung ausschließlich auf Testserver",PanelScript("TestMapStorage.php"),60);
    }

    public static SshCommandResult ConfigureTestServer(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Isolierte Testserver-Grundkonfiguration", PanelScript("TestServerConfigure.php"), 60);
    }

    public static SshCommandResult InspectWebPanel(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "LPLK-Webpanel-Verbindungsmetadaten (nur lesend)", PanelScript("WebPanelInspection.php"));
    }

    public static SshCommandResult ConnectTestWebPanel(SshConnection connection)
    {
        using var client = Connect(connection);
        EnsureTestWebOwnership(client, connection);
        return Run(client, connection, "Neuen Testserver im LPLK-Webpanel anbinden", PanelScript("WebPanelConnectTest.php").Replace("php -d display_errors", "runuser -u www-data -- php -d display_errors"), 60);
    }
    public static SshCommandResult CheckTestWebPanel(SshConnection connection)
    {
        using var client = Connect(connection);
        EnsureTestWebOwnership(client, connection);
        return Run(client, connection, "Befehlsweg Webpanel zur Testserver-Bridge prüfen", PanelScript("WebPanelCheckTest.php").Replace("php -d display_errors", "runuser -u www-data -- php -d display_errors"), 60);
    }

    private static void EnsureTestWebOwnership(SshClient client, SshConnection connection)
    {
        var check = Run(client, connection, "Dateirechte ausschließlich für Webpanel-Testserver 7", "php -d display_errors=0 -d log_errors=0 <<'LPLK_TEST_OWNERSHIP'\n" +
            "<?php\n$root='/var/www/serververwaltung/storage'; if(realpath($root)!==$root) exit(2); " +
            "foreach([$root.'/cache/live-status/server-7.json',$root.'/cache/live-status/server-7.json.lock',$root.'/backups/7'] as $p) { " +
            "if(!file_exists($p)) continue; if(is_link($p)||realpath($p)!==$p) exit(3); " +
            "if(is_dir($p)) { foreach(new RecursiveIteratorIterator(new RecursiveDirectoryIterator($p,FilesystemIterator::SKIP_DOTS),RecursiveIteratorIterator::SELF_FIRST) as $f) { " +
            "if($f->isLink()||realpath($f->getPathname())!==$f->getPathname()) exit(4); if(!chown($f->getPathname(),'www-data')||!chgrp($f->getPathname(),'www-data')) exit(5); } } " +
            "if(!chown($p,'www-data')||!chgrp($p,'www-data')) exit(6); } echo 'Test ownership checked';\nLPLK_TEST_OWNERSHIP\n");
        if(check.ExitCode!=0) throw new InvalidOperationException("Testserver-Dateirechte im Webpanel konnten nicht bestätigt werden.");
    }

    public static SshCommandResult TestAdmin(SshConnection connection,string phase)
    {
        if(phase is not ("add" or "finish"))throw new ArgumentException("Ungültige Prüfphase.");
        using var client=Connect(connection);
        return Run(client,connection,"Admin / Webpanel-Persistenzprüfung: "+phase,PanelScript("TestAdminCheck.php").Replace("__LPLK_ADMIN_PHASE__",phase).Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"),60);
    }
    public static SshCommandResult TestWhitelist(SshConnection connection, string phase)
    {
        if (phase is not ("seed" or "add" or "finish")) throw new ArgumentException("Ungültige Prüfphase.");
        using var client = Connect(connection);
        var command = PanelScript(phase == "seed" ? "TestWhitelistSeed.php" : "TestWhitelistCheck.php").Replace("__LPLK_WHITELIST_PHASE__", phase);
        if (phase != "seed") command = command.Replace("php -d display_errors", "runuser -u www-data -- php -d display_errors");
        return Run(client, connection, "Isolierte Whitelist-Prüfung: " + phase, command, 60);
    }

    public static SshCommandResult DeployTestServer(SshConnection connection, string workspace, string? component=null)
    {
        if(component is not (null or "LPLK_Statistics" or "LPLK_WebEconomySet" or "LPLK_BombWires" or "LPLK_EconomySet" or "LPLK_NadeEffectsSet" or "LPLK_FluchSet" or "LPLK_ThirdPersonSet" or "LPLK_StatisticsSet" or "LPLK_WeaponsSet" or "LPLK_Admin" or "LPLK_Sounds" or "AddonsManager" or "LPLK_AntiCamp" or "LPLK_MenuSet" or "LPLK_ChatGuardSet" or "LPLK_AntiTeamFlash" or "LPLK_TeamsSet"))throw new ArgumentException("Unzulässige Einzelkomponente.");
        const string root = "/var/lib/pterodactyl/volumes/11110004-0000-4000-8000-000000000004";
        using var client = Connect(connection);
        var guard = Run(client, connection, "Test-Ziel prüfen", "test \"$(realpath '" + root + "')\" = '" + root + "' && test ! -L '" + root + "/.lplk-staging' && test \"$(docker inspect --format '{{.State.Status}}' 11110004-0000-4000-8000-000000000004)\" = exited && mkdir -p '" + root + "/.lplk-staging'");
        if (guard.ExitCode != 0) throw new InvalidOperationException("Testserver muss gestoppt sein; Zielpfad darf kein Link sein.");
        var info = new PasswordConnectionInfo(connection.Host, connection.Port, connection.Username, connection.Password) { Timeout = TimeSpan.FromSeconds(20) };
        using var sftp = new SftpClient(info);
        sftp.HostKeyReceived += (_, e) => e.CanTrust = HostKeyMatches(connection.HostKeySha256, "SHA256:" + e.FingerPrintSHA256);
        sftp.Connect();
        int count = 0;
        // Built drafts are not automatically cleared for installation. Extend only after rule/compatibility review.
        foreach (string id in new[] { "LPLK_Parachute", "LPLK_BombWires", "LPLK_Maps", "LPLK_Admin", "LPLK_Whitelist", "SerververwaltungBridge", "LPLK_Sounds", "AddonsManager", "LPLK_AntiCamp", "LPLK_ChatGuard", "LPLK_AntiTeamFlash", "LPLK_Teams", "LPLK_Weapons", "LPLK_CombatRules", "LPLK_Statistics", "LPLK_Demos", "LPLK_ThirdPerson", "LPLK_Fluch", "LPLK_NadeEffects", "LPLK_Economy" })
        {
            if(component=="LPLK_WebEconomySet"&&id is not ("LPLK_Economy" or "LPLK_Statistics"))continue;
            if(component=="LPLK_EconomySet"&&id is not ("LPLK_Admin" or "LPLK_Economy"))continue;
            if(component=="LPLK_NadeEffectsSet"&&id is not ("LPLK_Admin" or "LPLK_NadeEffects"))continue;
            if(component=="LPLK_FluchSet"&&id is not ("LPLK_Admin" or "LPLK_Fluch"))continue;
            if(component=="LPLK_ThirdPersonSet"&&id is not ("LPLK_Admin" or "LPLK_ThirdPerson"))continue;
            if(component=="LPLK_StatisticsSet"&&id is not ("LPLK_Admin" or "LPLK_Statistics" or "LPLK_Demos"))continue;
            if(component=="LPLK_WeaponsSet"&&id is not ("LPLK_Admin" or "LPLK_Weapons" or "LPLK_CombatRules" or "LPLK_Maps" or "LPLK_Sounds"))continue;
            if(component is not ("LPLK_WebEconomySet" or "LPLK_WeaponsSet" or "LPLK_StatisticsSet" or "LPLK_ThirdPersonSet" or "LPLK_FluchSet" or "LPLK_NadeEffectsSet" or "LPLK_EconomySet"))
            if(component=="LPLK_TeamsSet"?id is not ("SerververwaltungBridge" or "LPLK_Teams"):component=="LPLK_ChatGuardSet"?id is not ("SerververwaltungBridge" or "LPLK_ChatGuard"):component=="LPLK_MenuSet"?!id.StartsWith("LPLK_",StringComparison.Ordinal):component is not null && id!=component)continue;
            string file = Path.Combine(workspace, "Addons", id, "Build", id + ".zip");
            if (!File.Exists(file)) { if(component is "LPLK_MenuSet" or "LPLK_WebEconomySet" or "LPLK_EconomySet" or "LPLK_NadeEffectsSet" or "LPLK_FluchSet" or "LPLK_ThirdPersonSet" or "LPLK_StatisticsSet" or "LPLK_WeaponsSet" or "LPLK_ChatGuardSet" or "LPLK_TeamsSet")throw new InvalidOperationException("Alle Komponenten der Modulgruppe müssen vorliegen.");continue; }
            using var stream = File.OpenRead(file);
            if (stream.Length > 50 * 1024 * 1024) throw new InvalidOperationException("Pluginpaket ist unerwartet groß.");
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            stream.Position = 0;
            sftp.UploadFile(stream, root + "/.lplk-staging/" + id + ".zip.tmp", true);
            if (sftp.Exists(root + "/.lplk-staging/" + id + ".zip")) sftp.DeleteFile(root + "/.lplk-staging/" + id + ".zip");
            sftp.RenameFile(root + "/.lplk-staging/" + id + ".zip.tmp", root + "/.lplk-staging/" + id + ".zip");
            sftp.WriteAllText(root + "/.lplk-staging/" + id + ".sha256", hash);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("Keine gebauten LPLK-Pakete gefunden.");
        return Run(client, connection, "LPLK-Plugins ausschließlich auf Testserver einbinden", PanelScript("TestServerDeploy.php").Replace("__LPLK_DEPLOY_COMPONENT__",component??""), 60);
    }
}






