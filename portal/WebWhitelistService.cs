using Renci.SshNet;
using System.Security.Cryptography;

namespace CS2ServerPortal;

internal static partial class SshService
{
    public static SshCommandResult DeployWebWhitelist(SshConnection connection,string workspace)
    {
        const string zipName="Serververwaltung_v1.15.2.3_Whitelist_Complete_Source.zip";
        const string expected="c03d51370f6f8c57eaf4da5c0b3a3658cd2f9515d98bb8c0652a23169b473198";
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
ZIP=/root/Serververwaltung_v1.15.2.3_Whitelist_Complete_Source.zip
test "$(sha256sum "$ZIP.incoming" | cut -d' ' -f1)" = c03d51370f6f8c57eaf4da5c0b3a3658cd2f9515d98bb8c0652a23169b473198
mv -- "$ZIP.incoming" "$ZIP"
LAUNCHER="$(mktemp /tmp/lplk-whitelist-launch.XXXXXX)"
trap 'rm -f -- "$LAUNCHER"' EXIT
unzip -p "$ZIP" Serververwaltung_v1.15.2.3/bin/update-whitelist.sh > "$LAUNCHER"
bash "$LAUNCHER"
""";
        return Run(client,connection,"Freigegebene Whitelistansicht 1.15.2.3 auf verwaltung.example.invalid installieren",command,180);
    }

    public static SshCommandResult CheckWebWhitelist(SshConnection connection,bool roundtrip=false)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Installierte Whitelistansicht und Testserver-Datenpfad prüfen",
            PanelScript("WebWhitelistCheck.php").Replace("__LPLK_WHITELIST_WEB_PHASE__",roundtrip?"roundtrip":"inspect").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"),60);
    }
}
