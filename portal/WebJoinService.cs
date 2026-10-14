using Renci.SshNet;
using System.Security.Cryptography;

namespace CS2ServerPortal;
internal static partial class SshService
{
    public static SshCommandResult SetTestSoundsEnabled(SshConnection connection,bool enabled)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Soundinitialisierung ausschließlich auf gestopptem Testserver umschalten",PanelScript("TestSoundsEnabled.php").Replace("__LPLK_SOUNDS_ENABLED__",enabled?"true":"false"),60);
    }
    public static SshCommandResult TestSoundDiagnostics(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Sound-Ressourcen und Ladefehler ausschließlich auf Testserver lesen",PanelScript("TestSoundDiagnostics.php"),60);
    }
    public static SshCommandResult ApplyTestMenuKeys(SshConnection connection,string testHash,string sourceHash,string content)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Menüsteuerung von Inner Circle ausschließlich auf Testserver übernehmen",PanelScript("TestMenuKeys.php").Replace("__TEST_HASH__",testHash).Replace("__SOURCE_HASH__",sourceHash).Replace("__CONTENT__",content),60);
    }
    public static SshCommandResult TestConnectionIsolation(SshConnection connection,string phase)
    {
        if(phase is not ("check" or "finish-stop" or "isolate" or "restore-workshop" or "restore"))throw new ArgumentException("Ungültige Prüfphase.");
        using var client=Connect(connection);
        return Run(client,connection,"Sound-/Workshop-Komponenten ausschließlich auf Testserver isolieren: "+phase,PanelScript("TestConnectionIsolation.php").Replace("__LPLK_ISOLATION_PHASE__",phase),60);
    }
    public static SshCommandResult RemoveTestPassword(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Passwort ausschließlich auf Testserver entfernen; Whitelist erhalten",PanelScript("TestPasswordRemove.php").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"),60);
    }
    public static SshCommandResult CheckWebJoin(SshConnection connection,bool repairTest=false)
    {
        using var client=Connect(connection);
        EnsureTestWebOwnership(client,connection);
        return Run(client,connection,"Webpanel Join-Adressen und isolierter Dashboard-Status",PanelScript("WebJoinCheck.php").Replace("__LPLK_REPAIR__",repairTest?"true":"false").Replace("php -d display_errors","runuser -u www-data -- php -d display_errors"),60);
    }
    public static SshCommandResult DeployWebJoin(SshConnection connection,string workspace)
    {
        const string zipName="Serververwaltung_v1.15.2.1_Join_Dashboard_Complete_Source.zip";
        string file=Path.GetFullPath(Path.Combine(workspace,"..","sERVERVERWALTUNG","dist",zipName));
        using var stream=File.OpenRead(file);
        if(stream.Length>50*1024*1024)throw new InvalidOperationException("Webpaket unerwartet groß.");
        string hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();stream.Position=0;
        using var client=Connect(connection);
        var guard=Run(client,connection,"Webpanel-Updateziel prüfen","test \"$(realpath /var/www/serververwaltung)\" = /var/www/serververwaltung && test ! -L /root/"+zipName+" && test ! -L /root/"+zipName+".incoming");
        if(guard.ExitCode!=0)throw new InvalidOperationException("Webpanel-Zielprüfung fehlgeschlagen.");
        using var sftp=new SftpClient(new PasswordConnectionInfo(connection.Host,connection.Port,connection.Username,connection.Password));
        sftp.HostKeyReceived+=(_,e)=>e.CanTrust=HostKeyMatches(connection.HostKeySha256,"SHA256:"+e.FingerPrintSHA256);
        sftp.Connect();sftp.UploadFile(stream,"/root/"+zipName+".incoming",true);
        return Run(client,connection,"Join und Dashboard-Status im Webpanel installieren",
            "test \"$(sha256sum /root/"+zipName+".incoming | cut -d' ' -f1)\" = '"+hash+"' && mv /root/"+zipName+".incoming /root/"+zipName+" && unzip -p /root/"+zipName+" Serververwaltung_v1.15.2.1/bin/update-join.sh | bash",180);
    }
}
