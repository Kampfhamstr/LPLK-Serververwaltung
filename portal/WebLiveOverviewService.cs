namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult CheckWebLiveOverview(SshConnection connection)
    {
        using var client = Connect(connection);
        return Run(client, connection, "Map, Teambelegung und Rundenstand lesen",
            PanelScript("WebLiveOverviewCheck.php").Replace("php -d display_errors", "runuser -u www-data -- php -d display_errors"), 90);
    }
}
