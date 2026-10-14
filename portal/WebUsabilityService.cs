namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult CheckWebUsability(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Navigation, Suche und Website-Dateistand prüfen",
            PanelScript("WebUsabilityCheck.php").Replace("php -d display_errors", "runuser -u www-data -- php -d display_errors"), 90);
    }
}
