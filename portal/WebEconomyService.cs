using Renci.SshNet;
using System.Security.Cryptography;

namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult DeployWebEconomy(SshConnection connection,string workspace)
    {
        const string zipName="Serververwaltung_v1.15.2.4_Economy_Complete_Source.zip";
        const string expected="443f14aa116277b60ce0f35ba740f5a9c5b715d3c016ab1c46ceffa416d5da3d";
        string file=Path.GetFullPath(Path.Combine(workspace,"..","sERVERVERWALTUNG","dist",zipName));
        using var stream=File.OpenRead(file);
        if(stream.Length>50*1024*1024||Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=expected)
            throw new InvalidOperationException("Das Websitepaket entspricht nicht der konkret freigegebenen Version.");
        stream.Position=0;
        using var client=Connect(connection);
        var guard=Run(client,connection,"Freigegebenes Website-Updateziel prüfen",
            "test \"$(id -u)\" -eq 0 && test \"$(realpath /var/www/serververwaltung)\" = /var/www/serververwaltung && test ! -L /root/"+zipName+
            " && test ! -e /root/"+zipName+".incoming && test ! -L /root/"+zipName+".incoming && { test ! -e /root/"+zipName+
            " || test \"$(sha256sum /root/"+zipName+" | cut -d' ' -f1)\" = '"+expected+"'; }");
        if(guard.ExitCode!=0)return guard;
        using var sftp=new SftpClient(new PasswordConnectionInfo(connection.Host,connection.Port,connection.Username,connection.Password){Timeout=TimeSpan.FromSeconds(20)});
        sftp.HostKeyReceived+=(_,e)=>e.CanTrust=HostKeyMatches(connection.HostKeySha256,"SHA256:"+e.FingerPrintSHA256);
        sftp.Connect();sftp.UploadFile(stream,"/root/"+zipName+".incoming",false);sftp.Disconnect();
        string command="""
set -eu
ZIP=/root/Serververwaltung_v1.15.2.4_Economy_Complete_Source.zip
test "$(sha256sum "$ZIP.incoming" | cut -d' ' -f1)" = 443f14aa116277b60ce0f35ba740f5a9c5b715d3c016ab1c46ceffa416d5da3d
mv -- "$ZIP.incoming" "$ZIP"
LAUNCHER="$(mktemp /tmp/lplk-economy-launch.XXXXXX)"
trap 'rm -f -- "$LAUNCHER"' EXIT
unzip -p "$ZIP" Serververwaltung_v1.15.2.4/bin/update-economy.sh > "$LAUNCHER"
bash "$LAUNCHER"
""";
        return Run(client,connection,"Freigegebene Bank- und Rankingansicht 1.15.2.4 auf verwaltung.example.invalid installieren",command,180);
    }

    public static SshCommandResult CheckWebEconomy(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Website-Bankansicht, Test-Ranglisten und Dateistand lesen",PanelScript("WebEconomyCheck.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"),60);
    }
}
