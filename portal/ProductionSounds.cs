using System.Text.Json;
using System.Security.Cryptography;
using Renci.SshNet;
using System.Net.Http.Json;

namespace CS2ServerPortal;

internal static partial class SshService
{
    internal static readonly Dictionary<string,int> SoundServers = new() {
        ["11110001"]=27015, ["11110002"]=27016, ["11110006"]=27017,
        ["11110007"]=27018, ["11110003"]=27019, ["11110005"]=27021
    };

    public static SshCommandResult InspectProductionSounds(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Soundmigration: sechs freigegebene CS2-Server prüfen",PanelScript("ProductionSoundsInspect.php"),60);
    }

    public static SshCommandResult InspectZombieReadOnly(SshConnection connection)
    {
        using var client=Connect(connection);
        return Run(client,connection,"Last Stand: separate Zombie-Ausnahme nur lesend erfassen",PanelScript("ZombieReadOnly.php"),60);
    }

    public static SshCommandResult ProductionSounds(SshConnection connection,string workspace,string id,string phase)
    {
        if(!SoundServers.ContainsKey(id)||phase is not ("prepare" or "check" or "deploy" or "start-check" or "status" or "rollback" or "seed"))throw new ArgumentException("Unzulässiger Soundauftrag.");
        var hashes=new Dictionary<string,string>();
        if(phase is "prepare" or "deploy")foreach(var name in new[]{"LPLK_Sounds","AddonsManager"})
        {
            using var f=File.OpenRead(ApprovedProductionSoundPackage(workspace,name));
            if(f.Length>50*1024*1024)throw new InvalidOperationException("Paket zu groß.");
            hashes[name]=Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
        }
        using var client=Connect(connection);
        var result=Run(client,connection,"Produktiver Soundwechsel: "+id+" / "+phase,PanelScript("ProductionSoundsDeploy.php")
            .Replace("__LPLK_SOUND_SERVER__",id).Replace("__LPLK_SOUND_PHASE__",phase).Replace("__LPLK_SOUND_HASHES__",JsonSerializer.Serialize(hashes)),60);
        if(result.ExitCode!=0||phase!="prepare")return result;
        using var report=JsonDocument.Parse(result.Output);string path=report.RootElement.GetProperty("staging").GetString()!;
        string uuid=report.RootElement.GetProperty("uuid").GetString()!;
        if(!Guid.TryParseExact(uuid,"D",out _)||!uuid.StartsWith(id+"-",StringComparison.Ordinal)||path!="/var/lib/pterodactyl/volumes/"+uuid+"/.lplk-sounds")throw new InvalidOperationException("Staging identity mismatch.");
        var info=new PasswordConnectionInfo(connection.Host,connection.Port,connection.Username,connection.Password){Timeout=TimeSpan.FromSeconds(20)};
        using var sftp=new SftpClient(info);sftp.HostKeyReceived+=(_,e)=>e.CanTrust=HostKeyMatches(connection.HostKeySha256,"SHA256:"+e.FingerPrintSHA256);sftp.Connect();
        foreach(var name in hashes.Keys)
        {
            foreach(var suffix in new[]{".zip",".zip.tmp"})if(sftp.Exists(path+"/"+name+suffix)&&sftp.GetAttributes(path+"/"+name+suffix).IsSymbolicLink)throw new InvalidOperationException("Linked staging file.");
            using var f=File.OpenRead(ApprovedProductionSoundPackage(workspace,name));
            sftp.UploadFile(f,path+"/"+name+".zip.tmp",true);
            if(sftp.Exists(path+"/"+name+".zip"))sftp.DeleteFile(path+"/"+name+".zip");
            sftp.RenameFile(path+"/"+name+".zip.tmp",path+"/"+name+".zip");
        }
        return result;
    }

    // Keep the explicitly authorized production release separate from newer test-menu builds.
    private static string ApprovedProductionSoundPackage(string workspace,string name)
    {
        string expected=name switch {
            "LPLK_Sounds"=>"730b75730a405ccaa615ed9156d30157fd79a3b71885a0b6eee861b1cef7d191",
            "AddonsManager"=>"a14a55e9235e6f728dbfc82e4fac149283d3065c87d53374b920366af999d6ee",
            _=>throw new ArgumentException("Unapproved production component.")};
        string approved=Path.Combine(workspace,"state","production-sounds-approved",name+".zip");
        string path=File.Exists(approved)?approved:Path.Combine(workspace,"Addons",name,"Build",name+".zip");
        using var stream=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Freigegebenes Produktionspaket verändert; keine Wartung.");
        return path;
    }
}

internal sealed partial class PanelClient
{
    public async Task ProductionSoundConsoleAsync(string id,string uuid,string action)
    {
        if(!SshService.SoundServers.TryGetValue(id,out int expectedPort))throw new ArgumentException("Soundwartung außerhalb Scope.");
        string command=action switch {"check"=>"sv_hibernate_when_empty; sw_addonupdates; sw_lplk_sounds", "wake"=>"sv_hibernate_when_empty 0", "sleep"=>"sv_hibernate_when_empty 1", "download"=>"sw_downloadaddon 3797047889", _=>throw new ArgumentException("Unbekannte Soundaktion.")};
        using var current=JsonDocument.Parse(await GetAsync($"api/client/servers/{id}"));var a=current.RootElement.GetProperty("attributes");var(ip,port,_)=ParseAllocation(a);
        if(a.GetProperty("uuid").GetString()!=uuid||ip!="203.0.113.10"||port!=expectedPort)throw new InvalidOperationException("Produktionsidentität verändert.");
        using var response=await http.PostAsJsonAsync($"api/client/servers/{id}/command",new{command});
        if(!response.IsSuccessStatusCode)throw new PanelException("Soundwartung Console-API: HTTP "+(int)response.StatusCode);
    }
    public async Task ProductionSoundPowerAsync(string id,string uuid,string signal)
    {
        if(!SshService.SoundServers.TryGetValue(id,out int expectedPort)||signal is not("start" or "stop"))throw new ArgumentException("Soundwartung außerhalb Scope.");
        using var current=JsonDocument.Parse(await GetAsync($"api/client/servers/{id}"));var a=current.RootElement.GetProperty("attributes");var(ip,port,_)=ParseAllocation(a);
        if(a.GetProperty("uuid").GetString()!=uuid||ip!="203.0.113.10"||port!=expectedPort)throw new InvalidOperationException("Produktionsidentität verändert.");
        using var response=await http.PostAsJsonAsync($"api/client/servers/{id}/power",new{signal});
        if(!response.IsSuccessStatusCode)throw new PanelException("Soundwartung Power-API: HTTP "+(int)response.StatusCode);
    }
}
