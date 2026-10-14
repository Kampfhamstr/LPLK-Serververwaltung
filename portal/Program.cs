using System.Text.Json;

namespace CS2ServerPortal;

internal static class Program
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [STAThread]
    static int Main(string[] args)
    {
        string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        int wi = Array.IndexOf(args, "--workspace");
        if (wi >= 0 && wi + 1 < args.Length) workspace = Path.GetFullPath(args[wi + 1]);
        try
        {
            if (args.Contains("--inner-circle-power-audit"))
            {
                var result = SshService.InspectInnerCirclePowerAudit(new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions)); return result.ExitCode ?? 1;
            }
            if (args.Contains("--inner-circle-host-health-probe"))
            {
                var result = SshService.InspectInnerCircleHostHealth(new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));return result.ExitCode ?? 1;
            }
            if (args.Contains("--inner-circle-recovery"))
            {
                int pi = Array.IndexOf(args, "--inner-circle-recovery");
                string phase = pi + 1 < args.Length ? args[pi + 1] : "check";
                if (phase != "check" && !args.Contains("--approve-inner-circle-1.4.14")) throw new InvalidOperationException("Konkrete Wartungsfreigabe für Inner Circle / 1.4.14 erforderlich.");
                var ssh = new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt.");
                int zi = Array.IndexOf(args, "--package");
                var result = SshService.InnerCircleRecovery(ssh, phase == "stop" ? "check" : phase == "start" ? "start-check" : phase, zi >= 0 && zi + 1 < args.Length ? args[zi + 1] : null);
                if (result.ExitCode == 0 && phase is "stop" or "start")
                {
                    using var report = JsonDocument.Parse(result.Output);
                    if (phase == "stop" && (report.RootElement.GetProperty("humans").GetInt32() != 0 || report.RootElement.GetProperty("bridgeAgeSeconds").GetInt32() is < 0 or > 15))
                        throw new InvalidOperationException("Frische Leermessung erforderlich; Wartung nicht gestartet.");
                    using var panel = new PanelClient(new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Panelverbindung fehlt."));
                    panel.InnerCircleRecoveryPowerAsync(phase).GetAwaiter().GetResult();
                }
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));return result.ExitCode ?? 1;
            }
            if (args.Contains("--inner-circle-updater-diagnostics"))
            {
                var result = SshService.InspectInnerCircleUpdater(new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return result.ExitCode ?? 1;
            }
            if (args.Contains("--inner-circle-crash-diagnostics"))
            {
                var result = SshService.InspectInnerCircleCrash(new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return result.ExitCode ?? 1;
            }
            if (args.Contains("--inner-circle-diagnostics"))
            {
                var result = SshService.InspectInnerCircle(new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return result.ExitCode ?? 1;
            }
            if (args.Contains("--github-release-check") || args.Contains("--github-release-publish"))
            {
                int hi=Array.IndexOf(args,"--plan-sha256");
                if(hi<0||hi+1>=args.Length)throw new InvalidOperationException("Geprüfter Releaseplan-Hash fehlt.");
                var connection=new GitHubCredentialStore(workspace).Read()??throw new InvalidOperationException("GitHub-Zugang fehlt.");
                using var release=new GitHubRelease(workspace,connection);
                Console.WriteLine(JsonSerializer.Serialize(release.RunAsync(args[hi+1],args.Contains("--github-release-publish")).GetAwaiter().GetResult(),JsonOptions));
                return 0;
            }
            if (args.Contains("--github-import"))
            {
                int mi = Array.IndexOf(args, "--github-import"), hi = Array.IndexOf(args, "--manifest-sha256");
                if (mi + 1 >= args.Length || hi < 0 || hi + 1 >= args.Length) throw new InvalidOperationException("Importmanifest und geprüfter SHA-256 fehlen.");
                var connection = new GitHubCredentialStore(workspace).Read() ?? throw new InvalidOperationException("GitHub-Zugang fehlt.");
                using var import = new GitHubImport(workspace, connection);
                Console.WriteLine(JsonSerializer.Serialize(import.RunAsync(args[mi + 1], args[hi + 1]).GetAwaiter().GetResult(), JsonOptions));
                return 0;
            }
            if (args.Contains("--github-status") || args.Contains("--github-test") || args.Contains("--github-repositories"))
            {
                var connection = new GitHubCredentialStore(workspace).Read();
                if (args.Contains("--github-test") || args.Contains("--github-repositories"))
                {
                    using var github = new GitHubClient(connection ?? throw new InvalidOperationException("Bitte den GitHub-Zugang zuerst im Programm eintragen."));
                    Console.WriteLine(JsonSerializer.Serialize(github.CheckAsync().GetAwaiter().GetResult(), JsonOptions));
                }
                else Console.WriteLine(JsonSerializer.Serialize(new { configured = connection != null, login = connection?.Login, accountId = connection?.AccountId, tokenKind = connection == null ? null : GitHubCredentialStore.TokenKind(connection.Token), accessMode = "account", repositoryBound = false }, JsonOptions));
                return 0;
            }
            if(args.Contains("--test-legacy-economy-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-legacy-economy-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("economyVersion").GetString()!="0.8.1"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("legacyAdminClampVerified").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Befehlsnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Altbefehle 0.8.1:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    // SavePlan retains the complete previous notes in history. Compact only the
                    // generated release paragraphs; preserve all other user-entered lines.
                    if(notes.Length>3000)
                    {
                        string[] generated=["Economy 13.10.2026:","Wetten 13.10.2026:","Lotto 13.10.2026:","Bankraub 13.10.2026:","Titel 13.10.2026:","Lottoboni 13.10.2026:","Kopfgeld 13.10.2026:","Kill-Belohnung 0.8.0:"];
                        notes=string.Join("\n",notes.Split('\n').Where(l=>!generated.Any(p=>l.StartsWith(p,StringComparison.Ordinal)))).Trim();
                        notes+="\nEconomy-Gesamtstand: Bank, Wetten, Lotto, Bankraub, Titel/Zinsen/Steuern, Lottoboni, Kopfgeld und Kill-Belohnungen sind intern in einer DLL umgesetzt und einzeln schaltbar. SQLite-Journal, offene Cashprüfung und dauerhafte Zahlungsansprüche. Frühere Versionsnachweise bleiben vollständig im Dashboard-Verlauf sowie in docs/TESTSERVER-ADDONS.md erhalten.";
                    }
                    notes+="\n"+prefix+" Adminbeträge samt Limits, alte Wett-Teamnamen, Zusatzargumente, Admin-Einstieg und aktive Hilfe korrigiert. 46 konfigurierte Economy-Aliase lokal geprüft und 92 native Konsolenformen registriert. Separate Linux-Buchungsprobe bestätigt die Adminbegrenzung und Wiederholbarkeit. Alle20 Komponenten Loaded nur auf11110004; Admin bleibt0.3.7. Client-/Mehrspielerabnahme, weitere BetBank-Einstellungen, Datenmigration und Altbefehlsabgleich der übrigen Addons bleiben offen.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                });
                Console.WriteLine("Economy-Altbefehle 0.8.1 als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-killreward-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-killreward-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Kill-Belohnungsnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Kill-Belohnung 0.8.0:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.8.0: KillReward intern mit Originalberechnung, dauerhaften Zahlungsansprüchen und geprüfter Cash-/Bankaufteilung. 46 BetBank-Aliase in 92 nativen Konsolenformen registriert; bankmsg an/aus samt Statusabfrage korrigiert. Prozessabbrüche und separate Linux-Neustartprobe geprüft. Alle20 Komponenten Loaded nur auf11110004; Admin bleibt0.3.7. Client-/Mehrspielerabnahme, vollständiger Argument-/Berechtigungsabgleich aller Altbefehle, weitere BetBank-Regeln und produktive Datenmigration bleiben offen.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                });
                Console.WriteLine("Kill-Belohnung innerhalb Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-killreward-probe"))
            {
                var result=SshService.TestKillRewardProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-bounty-probe"))
            {
                var result=SshService.TestBountyProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-bounty-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-bounty-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Kopfgeldnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Kopfgeld 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.7.0 und Admin 0.3.7: Kopfgeld aus Bank/Cash, prozentuale Beträge, gestapelte Prämien, Cash-/Bankauszahlung, Disconnect-Erstattung und alle16 Kopfgeldtitel intern umgesetzt. Alte kopfgeld/bounty-Chat-/Konsolenbefehle und Argumente erhalten. Atomare Finanzierung, dauerhafte Ansprüche und Bankperiodenübernahme; Prozessabbrüche und separate Linux-Neustartprobe geprüft. Alle20 Komponenten Loaded nur auf11110004. Client-/Mehrspielerabnahme offen. Nächster Schritt: verbleibende BetBank-Regeln einschließlich KillReward sowie vollständiger Altbefehlsabgleich aller Addons; produktive Datenmigration bleibt offen.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                });
                Console.WriteLine("Kopfgeld innerhalb Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-bonuses-probe"))
            {
                var result=SshService.TestBonusProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-titles-probe"))
            {
                var result=SshService.TestTitlesProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-robbery-probe"))
            {
                var result=SshService.TestRobberyProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-lottery-probe"))
            {
                var result=SshService.TestLotteryProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-bonuses-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-bonuses-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Bonusnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Lottoboni 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.6.0: Lottofähigkeiten, einmalige Dauerlos-Ausbaurabatte und Rest-Cash-Sammlung intern umgesetzt. Pistolenrunde ohne Cashbonus, Heilung/Rüstung bewusst ausgeschlossen. Gratislose finanziert, nur bezahlte Lose erstattet. Atomare Rabattnutzung, gespeicherte Bonuswahl und prüfbare Cashabsichten; Prozessabbrüche und Linux-Neustart geprüft. Alle 20 Komponenten Loaded nur auf 11110004. Client-Abnahme offen. Nächster Bereich Kopfgeld samt Titeln; vollständiger Altbefehlsabgleich aller Addons bleibt offen.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                });
                Console.WriteLine("Lottoboni innerhalb Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-titles-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-titles-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Titelnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Titel 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.5.0: interne Titel, atomarer Fortschritt, Pionier-/Herrschertitel, aktive Auswahl, Herrscher-Dauerlose, Titelzinsen und Rundenstart-Luxussteuer. Prozessabbrüche und Linux-Neustartprobe geprüft, alte Titelbefehle erhalten. Alle 20 Komponenten Loaded nur auf 11110004. Client-Abnahme offen. Noch offen: Lottofähigkeiten, Ausbaurabatte, Rest-Cash und Kopfgeld samt Titeln; vollständiger Altbefehlsabgleich aller Addons.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    var historical=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Titles");
                    if(historical is not null)DashboardChanges.SavePlan(state,historical with{Function="Titel – in LPLK_Economy 0.5.0 enthalten",Notes=historical.Notes+"\nTitel seit 13.10.2026 innerhalb Economy im Test; keine eigene DLL. Kopfgeldtitel folgen mit Kopfgeld."},false,DateTimeOffset.UtcNow,historical);
                });
                Console.WriteLine("Interne Titel in Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-robbery-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-robbery-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Bankraubnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Bankraub 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.4.0: interne Bankraubaufträge, Tresore, Schutz, Fähigkeiten, Spionage, High-Roller-Bypass und Robin-Hood-Bonus. Atomare Gebühren, gespeicherte Ergebnisse, Beuteansprüche und Linux-Neustartprobe geprüft. Alle 20 Komponenten Loaded nur auf 11110004. Alte Spion-/Ranking-/Hilfe-Aliasse erhalten. Client-Abnahme offen; Lotto-Rabatte, Titel und weitere Boni folgen.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    var historical=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_BankRobbery");
                    if(historical is not null)DashboardChanges.SavePlan(state,historical with{Function="Bankraub – in LPLK_Economy 0.4.0 enthalten",Notes=historical.Notes+"\nBankraub seit 13.10.2026 innerhalb Economy im Test; keine eigene DLL."},false,DateTimeOffset.UtcNow,historical);
                });
                Console.WriteLine("Interner Bankraub in Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-lottery-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-lottery-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Lottonachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Lotto 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.3.0: interne Lose, Dauerlose, Slotfortschritt, Ziehung, Superpot und Bankperiodenreset. Schema-2-Migration, Prozessabbruch und Linux-Neustartprobe geprüft. Alle 20 Komponenten Loaded nur auf 11110004. Client-Abnahme offen. Rest-Cash-Sammlung, Lottofähigkeiten, Raubrabatte und Titelboni folgen mit den abhängigen Bereichen. Nächster interner Bereich Bankraub.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    var historical=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Lottery");
                    if(historical is not null)DashboardChanges.SavePlan(state,historical with{Function="Lotto – in LPLK_Economy 0.3.0 enthalten",Notes=historical.Notes+"\nLotto-Kern seit 13.10.2026 innerhalb Economy im Test; keine eigene DLL."},false,DateTimeOffset.UtcNow,historical);
                });
                Console.WriteLine("Internes Lotto in Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-betting-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-betting-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Wettnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.First(p=>p.Id=="LPLK_Economy");const string prefix="Wetten 13.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                    notes+="\n"+prefix+" Economy 0.2.0: Wetten als interner, separat schaltbarer Bereich. Cash-Einsätze, anteilige Gewinne, Erstattungen, dauerhafte offene Auszahlungen und Schema-1-Migration geprüft. Linux-Neustartprobe bestätigt; keine Produktivdaten importiert. Alle 20 Komponenten Loaded nur auf 11110004. Client-Cash und Gameplay-Abnahme offen. Nächster interner Bereich Lotto.";
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    var historical=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Betting");
                    if(historical is not null)DashboardChanges.SavePlan(state,historical with{Function="Wetten – in LPLK_Economy 0.2.0 enthalten",Notes=historical.Notes+"\nWetten seit 13.10.2026 innerhalb Economy im Test; keine eigene DLL."},false,DateTimeOffset.UtcNow,historical);
                });
                Console.WriteLine("Interne Wetten in Economy als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-betting-probe"))
            {
                var result=SshService.TestBettingProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-economy-probe"))
            {
                var result=SshService.TestEconomyProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-economy-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-economy-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=20||!proof.GetProperty("restartProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Banknachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_Economy","0.1.0: gemeinsames Addon mit SQLite-Journal, atomaren Überweisungen, wiederholungssicheren Buchungen und offenen Cash-Vorgängen. Gruppierte Bank-/Adminmenüs, monatlicher Reset mit gemeinsamer Bankperiode. Isolierte Prüfkonten über regulären Serverneustart erhalten; keine Produktivguthaben importiert. Wetten/Lotto/Bankraub/Titel sowie Bonus-/Steuerregeln folgen als einzeln schaltbare interne Bereiche desselben Addons. Client-Cash und Gameplay-Abnahme offen."),
                        ("LPLK_Admin","0.3.6: Bank & Wirtschaft für aktive Spielermodule sowie unter Spielregeln & Sounds. Fünf Haupteinträge passen auf eine Seite; Unterseiten einschließlich Navigation maximal fünf Zeilen.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Modulplan fehlt: "+id);const string prefix="Economy 13.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        notes=(notes.Trim()+"\n"+prefix+" "+note+" Alle 20 Komponenten Loaded, ausschließlich Testserver 11110004.").Trim();
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                    foreach(var (id,feature) in new[]{("LPLK_Betting","Wetten"),("LPLK_Lottery","Lotto"),("LPLK_BankRobbery","Bankraub"),("LPLK_Titles","Titel")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id);if(old is null)continue;
                        const string prefix="Zusammenführung 13.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix))).Trim();
                        notes+="\n"+prefix+" Historischer Einzeladdon-Plan. Auf Benutzerwunsch künftig interner, einzeln schaltbarer Bereich von LPLK_Economy; keine separate DLL. Umsetzung noch offen.";
                        DashboardChanges.SavePlan(state,old with{Function=feature+" – künftig innerhalb LPLK_Economy",Stage="Zurückgestellt",Replaces=[],Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Economy und Admin im Dashboard als Im Test dokumentiert.");return 0;
            }
            if(args.Contains("--test-nadeeffects-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-nadeeffects-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=19||!proof.GetProperty("nativeFadeProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Granateneffekt-Nachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_NadeEffects","0.1.0: gezielte Admin-Markierung, Warnung/Fade und Entzug konfigurierter Granaten, danach Messer. Live-Referenz übernommen, Spielerauswahl leer. Sitzungs-/Figurenschutz, Rechte/Immunität, begrenzter Fade-Lebenszyklus. Nativer env_fade mit Zeiten, ActivatorOnly und Input/Entfernung bestätigt. Client-Fade, tatsächlicher Inventarentzug und Messerwechsel offen."),
                        ("LPLK_Admin","0.3.5: Granateneffekte unter Spielregeln & Sounds > Kampf & Bomben; nur geladenes und erlaubtes Modul. Maximal fünf Zeilen; !menu führt berechtigte Admins zur gleichen Administration.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Modulplan fehlt: "+id);const string prefix="NadeEffects 13.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        notes=(notes.Trim()+"\n"+prefix+" "+note+" Alle 19 Komponenten Loaded, ausschließlich Testserver 11110004; keine Produktionsänderung.").Trim();
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Granateneffekte und Adminmenü im Dashboard mit Historie erfasst.");return 0;
            }
            if(args.Contains("--test-nadeeffects-probe"))
            {
                var result=SshService.TestNadeEffectsProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-fluch-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-fluch-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=18||!proof.GetProperty("nativeChickenProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())throw new InvalidOperationException("Bestätigter Fluch-Nachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_Fluch","0.1.0: Admin-Hühnerfluch mit referenzierten 20 Hühnern, 10 Sekunden und 100 vorgesehenem Schaden; Sitzung/Figur/Team/Rechte erneut geprüft; Bereinigung bei Abbruch, Tod, Rundenwechsel und Entladen. Nativer chicken-Lebenszyklus mit Teleport/Entfernung bestätigt. Sichtbarer Orbit und echte Trefferwirkung im Client offen."),
                        ("LPLK_Admin","0.3.4: Hühnerfluch unter Spielregeln & Sounds > Kampf & Bomben, nur bei geladenem Modul und erteilter Berechtigung. Maximal fünf Menüzeilen; !menu führt berechtigte Admins zur gleichen Administration.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Modulplan fehlt: "+id);
                        const string prefix="Fluch 13.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        notes=(notes.Trim()+"\n"+prefix+" "+note+" Alle 18 Komponenten Loaded, ausschließlich Testserver 11110004; keine Produktionsänderung.").Trim();
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Fluch und Adminmenü im Dashboard mit Historie erfasst.");return 0;
            }
            if(args.Contains("--test-fluch-probe"))
            {
                var result=SshService.TestFluchProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-thirdperson-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-thirdperson-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004"||proof.GetProperty("runtimePlugins").GetInt32()!=17||
                   !proof.GetProperty("nativeCameraProbeConfirmed").GetBoolean()||proof.GetProperty("productionChanged").GetBoolean())
                    throw new InvalidOperationException("Bestätigter Kameranachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_ThirdPerson","0.1.0 auf Testserver 11110004: !tp / !thirdperson, Kamerakollision und Glättung, Schaden back/none/all, Recht thirdperson.use. Unter !menu nur bei aktiver und erlaubter Nutzung; Fachverwaltung unter !admin > Spielregeln & Sounds > Bewegung & Teams. 114 Kameraprüfungen bestanden; nativer point_camera-Lebenszyklus mit Position/Hull-Trace/Entfernung bestätigt. Clientdarstellung und echte Trefferwirkung offen."),
                        ("LPLK_Admin","0.3.3 auf Testserver 11110004: !menu > Waffen, Kamera & Sounds mit ThirdPerson-Schalter bei aktueller Modulfreigabe. Erneute Prüfung bei Klick; unveränderte Adminrechte und maximal fünf Zeilen. 1223 Menüprüfungen bestanden; ThirdPerson unter Bewegung & Teams registriert.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Modulplan fehlt: "+id);
                        const string prefix="ThirdPerson 12.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        notes=(notes.Trim()+"\n"+prefix+" "+note+" Alle 17 Komponenten Loaded; keine Produktionsänderung.").Trim();
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("ThirdPerson und Adminmenü im Dashboard mit Historie erfasst.");return 0;
            }
            if(args.Contains("--test-thirdperson-probe"))
            {
                var result=SshService.TestThirdPersonProbe(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--reference-economy"))
            {
                var result=SshService.ReadEconomyReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--reference-nadeeffects"))
            {
                var result=SshService.ReadNadeEffectsReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--reference-fluch"))
            {
                var result=SshService.ReadFluchReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--reference-thirdperson"))
            {
                var result=SshService.ReadThirdPersonReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--web-whitelist-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","whitelist-web-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004" || proof.GetProperty("version").GetString()!="1.15.2.3" ||
                   !proof.GetProperty("productionInstalled").GetBoolean() || proof.GetProperty("productionGameServersChanged").GetBoolean() ||
                   !proof.GetProperty("nativeRoundtrip").GetProperty("originalMembershipPreserved").GetBoolean() ||
                   !proof.GetProperty("nativeRoundtrip").GetProperty("addConfirmed").GetBoolean() ||
                   !proof.GetProperty("nativeRoundtrip").GetProperty("removeConfirmed").GetBoolean() ||
                   !proof.GetProperty("nativeRoundtrip").GetProperty("temporaryEntryRemoved").GetBoolean())
                    throw new InvalidOperationException("Bestätigter Whitelist-Webnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    var old=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Whitelist")??throw new InvalidOperationException("Whitelist-Plan fehlt.");
                    const string prefix="Whitelist-Webansicht 12.10.2026:";
                    string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                    notes=notes.Replace("Spezielle Webpanel-Whitelistansicht erkennt neue ID noch nicht;", "Damals noch fehlende spezielle Webpanel-Whitelistansicht seit 12.10.2026 ergänzt;");
                    notes=(notes.Trim()+"\n"+prefix+" Website 1.15.2.3 installiert. LPLK_Whitelist 0.2.2 ausschließlich für Testserver 11110004 angebunden; Rollen/Namen und ausklappbare Liste, Einzel-/Mehrfachänderungen mit bestätigter Addonwirkung. 50 Whitelist- und 17 Statistikprüfungen, 206 PHP-Syntaxprüfungen bestanden. Echter Webpanel-Rundlauf 7 → 8 → 7 bestätigt, alle bisherigen Einträge erhalten. Alle 16 Komponenten Loaded, kein Gameserver-Neustart und keine produktiven Gameserver geändert. Ingame-Zutrittsprüfung und Clientdarstellung bleiben offen.").Trim();
                    DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=notes},false,DateTimeOffset.UtcNow,old);
                });
                Console.WriteLine("Whitelist-Webansicht im Dashboard mit Historie erfasst.");return 0;
            }
            if(args.Contains("--web-live-overview-check"))
            {
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report=SshService.CheckWebLiveOverview(ssh);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--web-showcase-deploy")||args.Contains("--web-showcase-check")||args.Contains("--web-usability-check"))
            {
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report=args.Contains("--web-showcase-deploy")?SshService.DeployWebShowcase(ssh,workspace):args.Contains("--web-usability-check")?SshService.CheckWebUsability(ssh):SshService.CheckWebShowcase(ssh);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--web-economy-deploy")||args.Contains("--web-economy-check"))
            {
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report=args.Contains("--web-economy-deploy")?SshService.DeployWebEconomy(ssh,workspace):SshService.CheckWebEconomy(ssh);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--web-whitelist-deploy")||args.Contains("--web-whitelist-check")||args.Contains("--web-whitelist-roundtrip"))
            {
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report=args.Contains("--web-whitelist-deploy")?SshService.DeployWebWhitelist(ssh,workspace):SshService.CheckWebWhitelist(ssh,args.Contains("--web-whitelist-roundtrip"));
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--web-statistics-deploy")||args.Contains("--web-statistics-check"))
            {
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report=args.Contains("--web-statistics-deploy")?SshService.DeployWebStatistics(ssh,workspace):SshService.CheckWebStatistics(ssh);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--production-sounds-record"))
            {
                var rollout=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","production-sounds-rollout.json")));
                new DashboardStore(workspace).Update(state=>{
                    var ready=new List<string>();
                    foreach(var server in rollout.RootElement.GetProperty("servers").EnumerateArray())
                    {
                        string id=server.GetProperty("id").GetString()!;if(!server.TryGetProperty("runtimeReady",out var ok)||!ok.GetBoolean())continue;
                        string name=state.Servers.FirstOrDefault(s=>s.Info.Identifier==id)?.Info.Name??id;ready.Add(name);
                        var deploy=server.GetProperty("deployment");string when=deploy.GetProperty("appliedAt").GetString()!;
                        if(!state.History.Any(h=>h.ServerId==id&&h.Kind=="Soundwechsel durchgeführt"&&h.Notes.Contains(when,StringComparison.Ordinal)))
                            state.History.Add(new(DateTimeOffset.UtcNow,null,id,name,"Soundwechsel durchgeführt","LPLK_Sounds","DeathEffects 0.3.17 / AddonsManager 2.0.3-dc.2","LPLK_Sounds 0.1.4 / AddonsManager 2.0.3-lplk.3",when+"; 88 Sounds, Dateien und Rückfallkopie geprüft, vorherige übrige Plugins geladen. Kein eigener Client-Hörtest auf diesem Server behauptet."));
                    }
                    var old=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Sounds");if(old is not null){string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith("Soundwechsel 11.10.2026:")));
                        notes=(notes.Trim()+"\nSoundwechsel 11.10.2026: 0.1.4 produktiv mit 88 Sounds bestätigt auf "+string.Join(", ",ready)+". Atlas nur bei leerem Server umstellen; aktuellen Stand in Matrix/Historie prüfen. Bestehendes T3_Admin beibehalten; Soundeinstellungen dort vorerst über !lplk_sounds. Gemeinsames !admin bereits auf Testserver; Produktions-Adminumbau weiterhin offen.").Trim();
                        DashboardChanges.SavePlan(state,old with{Notes=notes},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Soundwechsel in Matrix-Historie und Plan dokumentiert.");return 0;
            }
            if(args.Contains("--production-sounds-inspect"))
            {
                var result=SshService.InspectProductionSounds(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            var soundPhases=new[]{"prepare","check","deploy","start","stop","status","rollback","console","seed"}.Where(p=>args.Contains("--production-sounds-"+p)).ToArray();
            if(soundPhases.Length>0)
            {
                if(soundPhases.Length!=1)throw new ArgumentException("Eine Soundwartungsphase wählen.");
                string phase=soundPhases[0];int ix=Array.IndexOf(args,"--production-sounds-"+phase);if(ix+1>=args.Length)throw new ArgumentException("Serverkennung fehlt.");string id=args[ix+1];
                var ssh=new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var result=SshService.ProductionSounds(ssh,workspace,id,phase is "stop" or "console"?"check":phase=="start"?"start-check":phase);
                if(result.ExitCode==0&&phase=="console"){
                    if(ix+2>=args.Length)throw new ArgumentException("Konsolenaktion fehlt.");
                    using var check=JsonDocument.Parse(result.Output);using var client=new PanelClient(new CredentialStore(workspace).Read()??throw new InvalidOperationException("Panelverbindung fehlt."));
                    client.ProductionSoundConsoleAsync(id,check.RootElement.GetProperty("uuid").GetString()!,args[ix+2]).GetAwaiter().GetResult();
                }
                if(result.ExitCode==0&&phase is "stop" or "start")
                {
                    using var check=JsonDocument.Parse(result.Output);string uuid=check.RootElement.GetProperty("uuid").GetString()!;
                    using var client=new PanelClient(new CredentialStore(workspace).Read()??throw new InvalidOperationException("Panelverbindung fehlt."));
                    client.ProductionSoundPowerAsync(id,uuid,phase).GetAwaiter().GetResult();
                }
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            int previewIndex = Array.IndexOf(args, "--render-preview");
            if (previewIndex >= 0 && previewIndex + 1 < args.Length)
            {
                AppBranding.InitializeTaskbarIdentity();
                ApplicationConfiguration.Initialize();
                int requestedView = Array.IndexOf(args, "--preview-view");
                bool editorPreview = requestedView >= 0 && requestedView + 1 < args.Length && args[requestedView + 1] == "plan-editor";
                using Form form = args.Contains("--github") ? new GitHubForm(workspace, true) : args.Contains("--ssh") ? new SshForm(workspace) : editorPreview
                    ? new AddonPlanForm(new DashboardStore(workspace).Read().Plans.OrderBy(p => p.Priority).FirstOrDefault()) : new MainForm(workspace, true);
                form.Show();
                if (form is MainForm) form.WindowState = FormWindowState.Maximized;
                Application.DoEvents();
                if (form is MainForm main)
                {
                    int viewIndex = Array.IndexOf(args, "--preview-view");
                    if (viewIndex >= 0 && viewIndex + 1 < args.Length)
                    {
                        if (args[viewIndex + 1] == "connection") main.Navigation.SelectedIndex = 1;
                        else main.Dashboard.SelectView(args[viewIndex + 1]);
                        Application.DoEvents();
                    }
                }
                Console.WriteLine(JsonSerializer.Serialize(new { form.DeviceDpi, form.Width, form.Height, fontHeight = form.Font.Height, form.AutoScaleDimensions, form.CurrentAutoScaleDimensions, screen = Screen.FromControl(form).WorkingArea }, JsonOptions));
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(args[previewIndex + 1], System.Drawing.Imaging.ImageFormat.Png);
                form.Close();
                return 0;
            }
            if (args.Contains("--self-test"))
            {
                CredentialStore.SelfTest();
                if (PanelClient.ParseCs2Version("ServerVersion=14190\nPatchVersion=1.41.9.0") != "14190") throw new Exception("Version parser failed.");
                foreach (var name in new[] { "TestEconomyProbe.php", "TestBettingProbe.php", "TestLotteryProbe.php", "TestRobberyProbe.php", "TestTitlesProbe.php","TestBonusProbe.php","TestBountyProbe.php","TestKillRewardProbe.php" })
                {
                    using var resource = typeof(SshService).Assembly.GetManifestResourceStream("CS2ServerPortal.Diagnostics." + name)
                        ?? throw new Exception("Missing bundled diagnostic: " + name);
                    if (resource.Length == 0) throw new Exception("Empty bundled diagnostic: " + name);
                }
                Console.WriteLine("Credential protection, URL validation, version parsing and bundled Economy diagnostics: PASS");
                return 0;
            }
            if (args.Contains("--status"))
            {
                var c = new CredentialStore(workspace).Read();
                Console.WriteLine(JsonSerializer.Serialize(new { configured = c != null, panelUrl = c?.PanelUrl, workspace }, JsonOptions));
                return 0;
            }
            if (args.Contains("--ssh-status"))
            {
                var connection = new SshCredentialStore(workspace).Read();
                Console.WriteLine(JsonSerializer.Serialize(new { configured = connection != null, host = connection?.Host, port = connection?.Port, username = connection?.Username, hostKeySha256 = connection?.HostKeySha256 }, JsonOptions));
                return 0;
            }
            if(args.Contains("--test-admin-add-check")||args.Contains("--test-admin-finish-check"))
            {
                var phase=args.Contains("--test-admin-add-check")?"add":"finish";
                var report=SshService.TestAdmin(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),phase);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if(args.Contains("--test-whitelist-seed") || args.Contains("--test-whitelist-add-check") || args.Contains("--test-whitelist-finish-check"))
            {
                string phase=args.Contains("--test-whitelist-seed")?"seed":args.Contains("--test-whitelist-add-check")?"add":"finish";
                var report=SshService.TestWhitelist(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),phase);
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if (args.Contains("--test-server-start") || args.Contains("--test-server-stop"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Panelverbindung fehlt.");
                using var client = new PanelClient(c);
                string signal = args.Contains("--test-server-start") ? "start" : "stop";
                client.TestServerPowerAsync(signal).GetAwaiter().GetResult();
                Console.WriteLine("Testserver 11110004: " + signal + " angefordert.");
                return 0;
            }
            if(args.Contains("--test-map-approval-record"))
            {
                TestMapApproval.RecordConfirmed(workspace);Console.WriteLine("90 Mapfreigaben im Laufzeitstand bestätigt und in Übersicht/Historie dokumentiert.");return 0;
            }
            if(args.Contains("--test-map-approval-check")||args.Contains("--test-map-approve-reviewed"))
            {
                var report=SshService.ApproveReviewedTestMaps(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),workspace,args.Contains("--test-map-approve-reviewed"));
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if (args.Contains("--test-map-review"))
            {
                var review=MapReview.RefreshAsync(workspace).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new {review.UpdatedAt,review.Catalog,review.Measured,review.Approved,review.MeasurementOnly,review.PoolMessage,review.ProbeId,review.Transition,file=MapReview.PathFor(workspace)},JsonOptions));return 0;
            }
            if(args.Contains("--test-map-storage"))
            {
                var report=SshService.ConfigureTestMapStorage(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(report,JsonOptions));return report.ExitCode??1;
            }
            if (args.Contains("--test-server-cleanup"))
            {
                var connection = new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var report = SshService.CleanupTestServer(connection);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return report.ExitCode ?? 1;
            }
            if(args.Contains("--zombie-read-only"))
            {
                var result=SshService.InspectZombieReadOnly(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--test-navigation-record-progress"))
            {
                new DashboardStore(workspace).Update(state =>
                {
                    var old = state.Plans.FirstOrDefault(p => p.Id == "LPLK_Admin");
                    if (old is null) throw new InvalidOperationException("Admin-Plan fehlt.");
                    const string prefix = "Menüstand 12.10.2026:";
                    string notes = string.Join("\n", old.Notes.Split('\n').Where(l => !l.StartsWith(prefix)));
                    notes = (notes.Trim() + "\n" + prefix + " LPLK_Admin 0.3.0 nur auf Testserver 11110004: !menu mit Maps, Sounds, Hilfe und berechtigtem Adminzugang; !admin und !adminmenu öffnen dieselbe gruppierte Verwaltung. Spieleraktionen in Untergruppen, zentrale Seiten höchstens fünf Zeilen mit Rücksprung. 1209 Menüchecks und 93 Fachprüfungen bestanden; Start und Webpanel-Befehle bestätigt. Ingame-Darstellung und echte Spieleraktionen weiterhin offen; keine Produktionsinstallation.").Trim();
                    DashboardChanges.SavePlan(state, old with { Stage = "Im Test", Notes = notes }, false, DateTimeOffset.UtcNow, old);
                });
                Console.WriteLine("Gruppierte Menüs im Dashboard mit Historie erfasst.");
                return 0;
            }
            if(args.Contains("--test-statistics-record-progress"))
            {
                using var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace,"state","test-statistics-verification.json")));
                var proof=evidence.RootElement;
                if(proof.GetProperty("server").GetString()!="11110004" || proof.GetProperty("runtimePlugins").GetInt32()!=16 ||
                   !proof.GetProperty("actualConsoleResponsesConfirmed").GetBoolean() || proof.GetProperty("productionChanged").GetBoolean() ||
                   !proof.GetProperty("website").GetProperty("productionInstalled").GetBoolean())
                    throw new InvalidOperationException("Bestätigter Statistik-Testnachweis fehlt.");
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_Statistics","0.1.0 nur auf Testserver 11110004: PvP, Bottraining, Waffen-/Granatenkills, Inspect-StatTrak und Schadenübersicht. Zusätzliche Spezialkills und beobachtete Waffenübernahmen mit Herkunft/Seriennummer, Team-/Bombenwerte und Spielzeit. 194 Statistik-/Demoprüfungen bestanden, Laufzeit fehlerfrei. Eigene leere Testdaten; kein Import produktiver Rankings. Webpanel 1.15.2.2 mit Spieleransicht am 12.10. ausdrücklich freigegeben, installiert und gegen die Testserverquelle geprüft. Produktive Gameserver unverändert. Mehrspieler-/Client-Abnahme offen; Surf/Zombie separat."),
                        ("LPLK_Demos","0.1.0 nur auf Testserver 11110004: lokale Aufnahmen mit eigenem Index und konservativer Finalisierung. Native Aufnahme 34,92 Sekunden / 370496 Bytes beendet, gültiger CS2-Dateikopf bestätigt und Index nach Neustart erhalten. Automatische Aufnahme, Anfragen, Bereinigung und externe Uploads ausgeschaltet. Client-Wiedergabe und Mehrspielerwirkung weiterhin offen."),
                        ("LPLK_Admin","0.3.2 nur auf Testserver 11110004: !menu > Statistik & Hilfe ergänzt; Statistiken nur mit aktiver Modulfreigabe, Demo-Aufbewahrung nur bei laufender freigegebener Aufnahme. !admin/!adminmenu > Server & Maps enthält beide neuen Fachmodule. 1218 Menüprüfungen, 16 reale Webpanel-Befehle; Client-Abnahme offen.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Addon-Plan fehlt: "+id);
                        const string prefix="Statistikstand 12.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=(notes.Trim()+"\n"+prefix+" "+note).Trim()},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Statistik, Demos und Menüstand mit Historie erfasst.");return 0;
            }
            if(args.Contains("--test-weapons-record-progress"))
            {
                new DashboardStore(workspace).Update(state=>
                {
                    foreach(var (id,note) in new[]{
                        ("LPLK_Maps","0.2.5 nur Testserver: öffentliche Menüpunkte melden ihre tatsächliche Freigabe. MeasurementOnly=true blendet Nominierung/RTV/Abstimmung in !menu aus; 90 Freigaben und Spielregeln unverändert. Keine Produktionsinstallation."),
                        ("LPLK_Sounds","0.1.6 nur Testserver: Sounds melden den aktiven Modulzustand an !menu. Deaktivierte Sounds werden dort ausgeblendet. Soundlogik, IDs und Katalog unverändert; Produktion bleibt auf 0.1.4."),
                        ("LPLK_Weapons","0.1.0 nur auf Testserver 11110004. Inner-Circle-Laufzeitstand: Waffenbeschränkung AUS, kostenlose Auswahl AUS, keine AWS-Sonderwerte. Gruppenmenüs, 34 Schusswaffen, Presets und Teamquoten. Keine Produktionsdaten importiert. Native Ausgabereihenfolge und Gameplay-Abnahme offen; Zombie-/Surf-Migration separat."),
                        ("LPLK_CombatRules","0.1.0 nur auf Testserver 11110004. Tatsächliches Headshot-only AUS übernommen; Goldene Regel AN für AWP/SCAR-20/G3SG1, ab einer Runde Vorsprung, ohne Warmup. Moderne Damage-Hooks; Fachregeln und Menücallbacks geprüft. Reale Treffer/Scope-Abnahme offen."),
                        ("LPLK_Admin","0.3.1 nur auf Testserver 11110004. !menu mit Maps, Waffen & Sounds, Hilfe und berechtigtem Adminzugang; !admin und !adminmenu gleicher Einstieg. Neue Waffen-/Kampfmodule unter Spielregeln & Sounds > Kampf & Bomben. Ausgeschaltete Waffenauswahl wird ausgeblendet. Maximal fünf Zeilen je neuer Seite; Client-Abnahme offen.")})
                    {
                        var old=state.Plans.FirstOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Addon-Plan fehlt: "+id);
                        const string prefix="Waffenstand 12.10.2026:";
                        string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith(prefix)));
                        DashboardChanges.SavePlan(state,old with{Stage="Im Test",Notes=(notes.Trim()+"\n"+prefix+" "+note).Trim()},false,DateTimeOffset.UtcNow,old);
                    }
                });
                Console.WriteLine("Waffen, Kampfregeln und Menüstand mit Historie erfasst.");return 0;
            }
            if (args.Contains("--test-server-record-progress")||args.Contains("--test-anticamp-record-progress")||args.Contains("--test-chatguard-record-progress")||args.Contains("--test-antiteamflash-record-progress")||args.Contains("--test-teams-record-progress"))
            {
                new DashboardStore(workspace).Update(state =>
                {
                    foreach (var (id, stage, note) in new[] {
                        ("LPLK_Teams", "Im Test", "0.1.0 auf Testserver 11110004 mit Bridge 1.6.4: Teamregeln des Inner Circle übernommen; dort gespeichertes Enabled=false beibehalten. Keine produktiven Spielerzuordnungen importiert. Rundenzahl, Halbzeit, Matchende und verbindungsgebundene Korrekturen fachlich geprüft. Gemeinsamer Adminbereich und Webpanel-Befehlsweg bestätigt. Client-Abnahme auf Benutzerwunsch offen; keine Produktionsinstallation."),
                        ("LPLK_AntiTeamFlash", "Im Test", "0.1.0 auf Testserver 11110004: Live-Regeln von Inner Circle übernommen, 50 fachliche Prüfungen bestanden. Team-/Eigen-/Gegnerflash, Schutzrecht und FFA abgegrenzt; laufende erlaubte Blendungen werden bei Überlappung konservativ beibehalten. !admin-Anbindung und Webpanel-Befehlsweg bestätigt. Ingame-Abnahme auf Benutzerwunsch verschoben; keine Produktionsinstallation."),
                        ("LPLK_ChatGuard", "Im Test", "0.1.0 auf Testserver 11110004 mit Bridge 1.6.3: 80 fachliche Prüfungen bestanden. Inner-Circle-Regeln und NAS-Fixes bis AntiAdvertisement 1.0.14 übernommen. Gemeinsamer Filter vor Chatarchivierung durch echte Bridge-Vertragsprobe bestätigt. !admin-Anbindung und Webpanel-Befehlsweg geprüft; alle zehn Komponenten geladen, 88/88 Sounds bereit. Eigene Testdaten, keine produktiven Sperridentitäten oder zentralen Exporte. Ingame-Abnahme auf Benutzerwunsch vorerst verschoben; keine Produktionsinstallation."),
                        ("LPLK_Parachute", "Im Test", "0.1.3 auf Testserver 11110004. Parachute-v5-Physik beibehalten; sichere Menüausführung auf dem Spielthread. Ingame-Abnahme offen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_BombWires", "Im Test", "0.1.2 auf Testserver 11110004. Vier Farben, 5 Sekunden und feste Reihenfolge; sichere Menüausführung auf dem Spielthread. Ingame-Abnahme offen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_Maps", "Im Test", "0.2.4 auf Testserver 11110004. 90 Grenzen nach Benutzerfreigabe gespeichert; Manor und de_sewer offen. Messbetrieb bleibt aktiv. Menüaktionen auf dem Spielthread. Normale Abstimmungen und Ingame-Abnahme noch offen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_Whitelist", "Im Test", "0.2.2 auf Testserver: 7 Testrollen, Modus 1, kein Admin-Bypass. Webpanel-Konsole → echte Listenänderung → Neustart → Entfernen geprüft. Spezielle Webpanel-Whitelistansicht seit Website 1.15.2.3 mit echtem Addon-Rundlauf geprüft; Ingame-Zutrittsprüfung offen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_Admin", "Im Test", "0.2.2: gemeinsames !admin mit Spieler-/Serveraktionen über Bridge 1.6.2. Testban gespeichert, nach Neustart vorhanden und entfernt. Rollen weiterhin aus dem Webpanel; keine T3-Produktionsdaten übernommen. Ingame-Abnahme offen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_Sounds", "Im Test", "0.1.5 mit neuer Menügestaltung auf dem Testserver, 88 Sounds. Veralteter nativer Addons-Offset 344 im installierten Workshop-Manager gefunden; Herstellerkorrektur auf 376 in AddonsManager 2.0.3-lplk.2 einschließlich Startprüfung installiert. Benutzer bestätigt für den Vorgänger 0.1.3 Beitritt, Soundmenü und Ton; FULL und !sound 1 im Serverlog. Mehrspielerregeln noch offen. Keine Kartenfunktionen. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen."),
                        ("LPLK_AntiCamp", "Im Test", "0.1.2 auf Testserver 11110004: tatsächliche Inner-Circle-Regeln und historische Timer-/Warnkorrekturen abgeglichen; 42 fachliche Prüfungen bestanden. Alle neun Komponenten geladen, AntiCamp über Webpanel-Befehlsweg bestätigt. Quelle und aktuelles ZIP unter Addons/LPLK_AntiCamp. Ingame-Abnahme offen; keine Produktionsinstallation. Menüstand: nummerierte Aktionen; Überschrift/Zurück weiß, Aktionen gelb, Schalter grün/rot. Neue Darstellung im Spiel noch offen.") })
                    {
                        if(args.Contains("--test-anticamp-record-progress")&&id!="LPLK_AntiCamp")continue;
                        if(args.Contains("--test-chatguard-record-progress")&&id!="LPLK_ChatGuard")continue;
                        if(args.Contains("--test-antiteamflash-record-progress")&&id!="LPLK_AntiTeamFlash")continue;
                        if(args.Contains("--test-teams-record-progress")&&id!="LPLK_Teams")continue;
                        var old = state.Plans.FirstOrDefault(p => p.Id == id);
                        if (old is null) continue;
                        string notes = string.Join("\n", old.Notes.Split('\n').Where(l => !l.StartsWith("Teststand 11.10.2026:")));
                        notes = (notes.Trim() + "\nTeststand 11.10.2026: " + note).Trim();
                        DashboardChanges.SavePlan(state, old with { Stage = stage, Notes = notes }, false, DateTimeOffset.UtcNow, old);
                    }
                });
                Console.WriteLine("Prüfstand und offene Arbeiten in der Dashboard-Historie erfasst.");
                return 0;
            }
            if (args.Contains("--test-server-inspect") || args.Contains("--test-server-create") || args.Contains("--test-server-status") || args.Contains("--test-server-configure") || args.Contains("--web-panel-inspect") || args.Contains("--test-server-deploy") || args.Contains("--test-server-web-connect") || args.Contains("--test-server-web-check"))
            {
                var connection = new SshCredentialStore(workspace).Read()
                    ?? throw new InvalidOperationException("Bitte zuerst die SSH-Verbindung speichern.");
                object report = args.Contains("--test-server-create") ? SshService.CreateTestServer(connection)
                    : args.Contains("--test-server-status") ? SshService.TestServerStatus(connection)
                    : args.Contains("--test-server-configure") ? SshService.ConfigureTestServer(connection)
                    : args.Contains("--web-panel-inspect") ? SshService.InspectWebPanel(connection)
                    : args.Contains("--test-server-deploy") ? SshService.DeployTestServer(connection, workspace)
                    : args.Contains("--test-server-web-connect") ? SshService.ConnectTestWebPanel(connection)
                    : args.Contains("--test-server-web-check") ? SshService.CheckTestWebPanel(connection) : SshService.InspectTestServer(connection);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return report is SshCommandResult commandResult ? commandResult.ExitCode ?? 1 : 0;
            }
            if(args.Contains("--test-connection-check") || args.Contains("--test-connection-finish-stop") || args.Contains("--test-connection-isolate") || args.Contains("--test-connection-restore-workshop") || args.Contains("--test-connection-restore"))
            {
                string phase=args.Contains("--test-connection-finish-stop")?"finish-stop":args.Contains("--test-connection-isolate")?"isolate":args.Contains("--test-connection-restore-workshop")?"restore-workshop":args.Contains("--test-connection-restore")?"restore":"check";
                var result=SshService.TestConnectionIsolation(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),phase);
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--reference-core-menus")||args.Contains("--test-copy-menu-keys"))
            {
                using var client=new PanelClient(new CredentialStore(workspace).Read()??throw new InvalidOperationException("Panelverbindung fehlt."));
                var result=args.Contains("--test-copy-menu-keys")?ReferenceCoreMenus.ApplyAsync(client,new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),workspace).GetAwaiter().GetResult():ReferenceCoreMenus.ReadAsync(client,workspace).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return 0;
            }
            if(args.Contains("--test-sounds-disable")||args.Contains("--test-sounds-enable"))
            {
                if(args.Contains("--test-sounds-disable")&&args.Contains("--test-sounds-enable"))throw new ArgumentException("Nur eine Sound-Prüfphase angeben.");
                var result=SshService.SetTestSoundsEnabled(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),args.Contains("--test-sounds-enable"));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-sound-diagnostics"))
            {
                var result=SshService.TestSoundDiagnostics(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-weaponrank-deploy"))
            {
                var result=SshService.DeployTestServer(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),workspace,"LPLK_Statistics");
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-web-economy-deploy")||args.Contains("--test-bombwires-deploy")||args.Contains("--test-economy-deploy")||args.Contains("--test-nadeeffects-deploy")||args.Contains("--test-fluch-deploy")||args.Contains("--test-thirdperson-deploy")||args.Contains("--test-statistics-deploy")||args.Contains("--test-weapons-deploy")||args.Contains("--test-adminmenu-deploy")||args.Contains("--test-sounds-deploy")||args.Contains("--test-addons-deploy")||args.Contains("--test-anticamp-deploy")||args.Contains("--test-menus-deploy")||args.Contains("--test-chatguard-deploy")||args.Contains("--test-antiteamflash-deploy")||args.Contains("--test-teams-deploy"))
            {
                if(new[]{"--test-web-economy-deploy","--test-bombwires-deploy","--test-economy-deploy","--test-nadeeffects-deploy","--test-fluch-deploy","--test-thirdperson-deploy","--test-statistics-deploy","--test-weapons-deploy","--test-adminmenu-deploy","--test-sounds-deploy","--test-addons-deploy","--test-anticamp-deploy","--test-menus-deploy","--test-chatguard-deploy","--test-antiteamflash-deploy","--test-teams-deploy"}.Count(args.Contains)!=1)throw new ArgumentException("Nur eine Komponente bzw. Menügruppe angeben.");
                var component=args.Contains("--test-web-economy-deploy")?"LPLK_WebEconomySet":args.Contains("--test-bombwires-deploy")?"LPLK_BombWires":args.Contains("--test-economy-deploy")?"LPLK_EconomySet":args.Contains("--test-nadeeffects-deploy")?"LPLK_NadeEffectsSet":args.Contains("--test-fluch-deploy")?"LPLK_FluchSet":args.Contains("--test-thirdperson-deploy")?"LPLK_ThirdPersonSet":args.Contains("--test-statistics-deploy")?"LPLK_StatisticsSet":args.Contains("--test-weapons-deploy")?"LPLK_WeaponsSet":args.Contains("--test-adminmenu-deploy")?"LPLK_Admin":args.Contains("--test-teams-deploy")?"LPLK_TeamsSet":args.Contains("--test-antiteamflash-deploy")?"LPLK_AntiTeamFlash":args.Contains("--test-chatguard-deploy")?"LPLK_ChatGuardSet":args.Contains("--test-addons-deploy")?"AddonsManager":args.Contains("--test-anticamp-deploy")?"LPLK_AntiCamp":args.Contains("--test-menus-deploy")?"LPLK_MenuSet":"LPLK_Sounds";
                var result=SshService.DeployTestServer(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),workspace,component);
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if(args.Contains("--test-password-remove"))
            {
                var result=SshService.RemoveTestPassword(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--web-join-check") || args.Contains("--web-join-deploy") || args.Contains("--test-web-status-repair"))
            {
                var connection = new SshCredentialStore(workspace).Read() ?? throw new InvalidOperationException("SSH-Verbindung fehlt.");
                var result = args.Contains("--web-join-deploy") ? SshService.DeployWebJoin(connection, workspace) : SshService.CheckWebJoin(connection,args.Contains("--test-web-status-repair"));
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return result.ExitCode ?? 1;
            }
            if (args.Contains("--ssh-test") || args.Contains("--ssh-diagnostics") || args.Contains("--ssh-panel-access"))
            {
                var connection = new SshCredentialStore(workspace).Read()
                    ?? throw new InvalidOperationException("Bitte unter „Debian / SSH“ zuerst die Verbindung speichern.");
                object report = args.Contains("--ssh-panel-access") ? SshService.DiagnosePanelAccess(connection)
                    : args.Contains("--ssh-diagnostics") ? SshService.Diagnose(connection) : SshService.TestConnection(connection);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            if (args.Contains("--check-updates"))
            {
                var report = UpdateCheck.RunAsync(workspace).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return report.Errors.Count == 0 ? 0 : 2;
            }
            if (args.Contains("--dashboard-refresh"))
            {
                var dashboard = DashboardService.RefreshAsync(workspace, text => Console.Error.WriteLine(text)).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new { dashboard.RefreshedAt, servers = dashboard.Servers.Count, historyEntries = dashboard.History.Count, file = Path.Combine(workspace, "state", "addon-dashboard.json") }, JsonOptions));
                return 0;
            }
            if (args.Contains("--servers"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte zuerst das Portal öffnen und die Panel-Verbindung speichern.");
                using var client = new PanelClient(c);
                Console.WriteLine(JsonSerializer.Serialize(client.GetServersAsync(true).GetAwaiter().GetResult(), JsonOptions));
                return 0;
            }
            if (args.Contains("--reference-map-plan"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Panelverbindung fehlt.");
                using var client = new PanelClient(c);
                var json = client.ReadFileAsync("11110003", "/game/csgo/addons/swiftlys2/configs/plugins/headshotonly/maps.jsonc").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var maps = doc.RootElement.GetProperty("HeadshotOnlyMaps").GetProperty("Maps").EnumerateArray()
                    .Select(m => new { id = m.GetProperty("Id").GetString(), name = m.GetProperty("Name").GetString(), minPlayers = m.GetProperty("MinPlayers").GetInt32(), maxPlayers = m.GetProperty("MaxPlayers").GetInt32() }).ToArray();
                string output = Path.Combine(workspace, "state", "test-map-seed.json");
                File.WriteAllText(output, JsonSerializer.Serialize(maps, JsonOptions));
                Console.WriteLine(JsonSerializer.Serialize(new { maps = maps.Length, file = output }, JsonOptions));
                return 0;
            }
            if (args.Contains("--reference-teams"))
            {
                var result=SshService.ReadTeamsReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));
                return result.ExitCode??1;
            }
            if(args.Contains("--test-demo-start")||args.Contains("--test-demo-stop"))
            {
                var result=SshService.TestDemo(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."),args.Contains("--test-demo-start"));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--reference-statistics"))
            {
                var result=SshService.ReadStatisticsReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--reference-weapons"))
            {
                var result=SshService.ReadWeaponsReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--reference-antiteamflash"))
            {
                var result=SshService.ReadAntiTeamFlashReference(new SshCredentialStore(workspace).Read()??throw new InvalidOperationException("SSH-Verbindung fehlt."));
                Console.WriteLine(JsonSerializer.Serialize(result,JsonOptions));return result.ExitCode??1;
            }
            if (args.Contains("--reference-chatguard-rules"))
            {
                using var client=new PanelClient(new CredentialStore(workspace).Read()??throw new InvalidOperationException("Panelverbindung fehlt."));
                ReferenceGameplay.ReadChatGuardAsync(client,workspace).GetAwaiter().GetResult();
                Console.WriteLine("ChatGuard-Regeln ohne Spieleridentitäten gelesen.");return 0;
            }
            if (args.Contains("--reference-gameplay-rules"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Panelverbindung fehlt.");
                using var client = new PanelClient(c);
                ReferenceGameplay.SaveAsync(client, workspace).GetAwaiter().GetResult();
                Console.WriteLine("Ausgewählte Spielregeln gespeichert: state/reference-gameplay-rules.json");
                return 0;
            }
            int listIndex = Array.IndexOf(args, "--list-files");
            if (listIndex >= 0)
            {
                if (listIndex + 2 >= args.Length) throw new ArgumentException("--list-files benötigt Serverkennung und Verzeichnis.");
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte zuerst die Panel-Verbindung speichern.");
                using var client = new PanelClient(c);
                Console.WriteLine(JsonSerializer.Serialize(client.ListFilesAsync(args[listIndex + 1], args[listIndex + 2]).GetAwaiter().GetResult(), JsonOptions));
                return 0;
            }
            if (args.Contains("--addon-inventory"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte zuerst die Panel-Verbindung speichern.");
                using var client = new PanelClient(c);
                var report = AddonInventory.CollectAsync(client).GetAwaiter().GetResult();
                string output = Path.Combine(workspace, "state", "addon-inventory.json");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output + ".tmp", JsonSerializer.Serialize(report, JsonOptions));
                File.Move(output + ".tmp", output, true);
                Console.WriteLine(output);
                return 0;
            }
            if (args.Contains("--addon-analysis"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte zuerst die Panel-Verbindung speichern.");
                using var client = new PanelClient(c);
                var inventory = JsonSerializer.Deserialize<AddonInventoryReport>(File.ReadAllText(Path.Combine(workspace, "state", "addon-inventory.json")), JsonOptions)
                    ?? throw new InvalidOperationException("Bitte zuerst --addon-inventory ausführen.");
                var report = AddonAnalysis.CollectAsync(client, inventory).GetAwaiter().GetResult();
                string output = Path.Combine(workspace, "state", "addon-analysis.json");
                File.WriteAllText(output + ".tmp", JsonSerializer.Serialize(report, JsonOptions));
                File.Move(output + ".tmp", output, true);
                Console.WriteLine(output);
                return 0;
            }
            if (args.Contains("--addon-runtime"))
            {
                var c = new CredentialStore(workspace).Read() ?? throw new InvalidOperationException("Bitte zuerst die Panel-Verbindung speichern.");
                using var client = new PanelClient(c);
                var inventory = JsonSerializer.Deserialize<AddonInventoryReport>(File.ReadAllText(Path.Combine(workspace, "state", "addon-inventory.json")), JsonOptions)
                    ?? throw new InvalidOperationException("Bitte zuerst --addon-inventory ausführen.");
                var report = AddonRuntime.CollectAsync(client, inventory).GetAwaiter().GetResult();
                string output = Path.Combine(workspace, "state", "addon-runtime.json");
                File.WriteAllText(output + ".tmp", JsonSerializer.Serialize(report, JsonOptions));
                File.Move(output + ".tmp", output, true);
                Console.WriteLine(output);
                return 0;
            }
            AppBranding.InitializeTaskbarIdentity();
            ApplicationConfiguration.Initialize();
            Form appForm=args.Contains("--github") ? new GitHubForm(workspace) : args.Contains("--ssh") ? new SshForm(workspace) : new MainForm(workspace);
            if(appForm is MainForm mainForm&&args.Contains("--map-review"))appForm.Shown+=(_,_)=>mainForm.Dashboard.SelectView("maps");
            Application.Run(appForm);
            return 0;
        }
        catch (Exception ex)
        {
            var message = SafeError.Message(ex);
            if (args.Any(a => a.StartsWith("--"))) Console.Error.WriteLine(message);
            else MessageBox.Show(message, "CS2 Serververwaltung", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class SafeError
{
    public static string Message(Exception ex) => ex switch
    {
        PanelException p => p.Message,
        SshHostKeyException => "Die SSH-Serverkennung hat sich geändert. Verbindung vor der Anmeldung abgebrochen; bitte den Serverwechsel zuerst prüfen.",
        Renci.SshNet.Common.SshAuthenticationException => "SSH-Anmeldung abgelehnt. Benutzer und Passwort prüfen; der Server muss diese Anmeldeart erlauben.",
        Renci.SshNet.Common.SshOperationTimeoutException => "Die SSH-Verbindung hat zu lange gedauert. Serveradresse, Port und Erreichbarkeit prüfen.",
        Renci.SshNet.Common.SshConnectionException => "Die SSH-Verbindung wurde unterbrochen oder abgelehnt. Serveradresse, Port und SSH-Dienst prüfen.",
        Renci.SshNet.Common.SshException => "Die SSH-Abfrage konnte nicht abgeschlossen werden. SSH-Dienst und Benutzerrechte prüfen.",
        System.Net.Sockets.SocketException => "Der SSH-Server ist nicht erreichbar. Serveradresse, Port und Netzwerkverbindung prüfen.",
        ArgumentException a => a.Message,
        InvalidOperationException i => i.Message,
        System.Security.Cryptography.CryptographicException => "Die gespeicherten Zugangsdaten können unter diesem Windows-Benutzer nicht entschlüsselt werden.",
        HttpRequestException => "Verbindung fehlgeschlagen. Adresse, Internetzugang und HTTPS-Zertifikat prüfen.",
        OperationCanceledException => "Zeitüberschreitung bei der Verbindung. Bitte erneut versuchen.",
        _ => "Der Vorgang konnte nicht abgeschlossen werden (" + ex.GetType().Name + ")."
    };
}











