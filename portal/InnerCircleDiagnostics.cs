namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult InspectInnerCirclePowerAudit(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Inner Circle: Herkunft von Stop/Restart nur lesend prüfen", PanelScript("InnerCirclePowerAudit.php"), 30, 24000);
    }
    public static SshCommandResult InspectInnerCircleHostHealth(SshConnection connection)
    {
        using var source = typeof(SshService).Assembly.GetManifestResourceStream("CS2ServerPortal.Diagnostics.HostHealth.py") ?? throw new InvalidOperationException("Health source missing");
        using var reader = new StreamReader(source);
        string script = reader.ReadToEnd().Replace("\r\n", "\n");
        string command = "python3 -B <<'LPLK_READONLY_HEALTH'\n__name__='lplk_readonly_probe'\n" + script + "\nnow=time.time()\nsample,root=collect('11110003-0000-4000-8000-000000000003',Path('/var/lib/pterodactyl/volumes'),now)\npublic,saved=evaluate(sample,{},now)\nprint(json.dumps({'readOnly':True,'observation':public,'bridge_age':sample.get('bridge_age'),'sources_complete':sample.get('sources_complete'),'crash_reports_read':len(sample.get('crashes',[]))}))\nLPLK_READONLY_HEALTH\n";
        using var client = Connect(connection);
        return Run(client, connection, "Inner Circle: vorbereitete Host-Erkennung nur lesend prüfen", command, 40);
    }
    public static SshCommandResult InspectInnerCircleUpdater(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Beispielserver: Startskript nur lesen", PanelScript("InnerCircleUpdaterReadOnly.php"), 25, 32000);
    }
    public static SshCommandResult InspectInnerCircleCrash(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Beispielserver: Crashursache nur lesend prüfen", PanelScript("InnerCircleCrashReadOnly.php"), 40, 32000);
    }
    public static SshCommandResult InspectInnerCircle(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Beispielserver: nur lesende Störungsdiagnose", PanelScript("InnerCircleReadOnly.php"), 55, 32000);
    }
}
