using System.Text.Json;
using LPLK.Common;

namespace LPLK_Economy;

public sealed record NetworkStorage(string Server,string Profile,string DatabaseIdentity)
{
    public static Ledger Open(string dataDirectory,EconomySettings settings)
    {
        string config=Path.Combine(dataDirectory,"network.json");
        string binding=Path.Combine(dataDirectory,"network-binding.json");
        if(!File.Exists(config))
        {
            if(File.Exists(binding))throw new InvalidDataException("Bankverbund-Konfiguration fehlt; lokaler Rückfall gesperrt.");
            return new Ledger(Path.Combine(dataDirectory,"economy.sqlite3"),settings,roundMemory:true);
        }
        var info=new FileInfo(config);
        if(info.LinkTarget is not null||info.Length>4096)throw new InvalidDataException("Ungültige Bankverbund-Konfiguration.");
        var value=JsonSerializer.Deserialize<NetworkStorage>(File.ReadAllText(config))??throw new InvalidDataException("Bankverbund-Konfiguration fehlt.");
        if(value.Profile!=settings.Profile||value.Server is not {Length:8}||value.DatabaseIdentity is not {Length:32}||!value.DatabaseIdentity.All(char.IsAsciiHexDigit))throw new InvalidDataException("Bankverbund-Identität stimmt nicht.");
        const string path="/lplk-bank/economy.sqlite3";
        if(!OperatingSystem.IsLinux()||!File.Exists(path)||new DirectoryInfo("/lplk-bank").LinkTarget is not null)throw new InvalidDataException("Gemeinsames Banklaufwerk nicht eingebunden.");
        var bank=new Ledger(path,settings,server:value.Server);
        try
        {
            if(bank.NetworkIdentity!=value.DatabaseIdentity)throw new InvalidDataException("Falsche gemeinsame Bank eingebunden.");
            if(File.Exists(binding))
            {
                if(new FileInfo(binding).LinkTarget is not null||new FileInfo(binding).Length>4096||JsonSerializer.Deserialize<NetworkStorage>(File.ReadAllText(binding))!=value)throw new InvalidDataException("Bankzuordnung geändert; Migration erforderlich.");
            }
            else AtomicJson.Write(binding,value);
        }
        catch{bank.Dispose();throw;}
        return bank;
    }
}
