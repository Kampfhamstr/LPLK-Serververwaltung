using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;

namespace SerververwaltungBridge;

public sealed partial class Plugin
{
    // Explicit fleet decision: administrative actions and sanctions belong to this server.
    private static bool LocalAdministrationOnly => true;
    private void PreserveSanctionsLocally()
    {
        string receipt=Path.Combine(_root,"local-sanctions-migration.json");
        if(!File.Exists(receipt))
        {
            // Retain the source before changing any effective sanction. A repeated
            // load after a partial write merges the same strongest values again.
            string backup=Path.Combine(_root,"local-sanctions-before.json");
            if(!File.Exists(backup))WriteNewFleet(backup,JsonSerializer.Serialize(new{Bans=_bans,Mutes=_mutes,Gags=_gags,GlobalBans=_globalBans,GlobalMutes=_globalMutes,GlobalGags=_globalGags},_json));
            foreach(var (id,ban) in _globalBans)
            {
                DateTimeOffset? expiry=ban.Permanent?null:ban.ExpiresAt;
                if(!_bans.TryGetValue(id,out var prior)||prior.ExpiresAt is not null&&(expiry is null||expiry>prior.ExpiresAt))
                    _bans[id]=new BanEntry{Reason=ban.Reason,CreatedAt=ban.CreatedAt,ExpiresAt=expiry};
            }
            foreach(var (id,entry) in _globalMutes)StrongestComm(_mutes,id,entry);
            foreach(var (id,entry) in _globalGags)StrongestComm(_gags,id,entry);
            SaveBans();SaveMutes();SaveGags();
            WriteNewFleet(receipt,JsonSerializer.Serialize(new{At=DateTimeOffset.UtcNow,Scope="local",Bans=_bans.Count,Mutes=_mutes.Count,Gags=_gags.Count},_json));
        }
        _globalBans.Clear();_globalMutes.Clear();_globalGags.Clear();
    }

    private void RetireGlobalIdentityCaches()
    {
        string receipt=Path.Combine(_root,"local-identity-migration.json");
        if(!File.Exists(receipt))
        {
            string chatRules=Path.Combine(Path.GetDirectoryName(_root)!,"LPLK_ChatGuard","global_protection.json");
            var paths=new[]{_antiAdGlobalFile,chatRules}.Where(p=>!string.IsNullOrWhiteSpace(p)&&File.Exists(p)).Distinct().ToArray();
            string backup=Path.Combine(_root,"local-identity-before.json");
            if(!File.Exists(backup))WriteNewFleet(backup,JsonSerializer.Serialize(paths.ToDictionary(p=>p,File.ReadAllText),_json));
            var snapshots=paths.ToDictionary(p=>p,p=>JsonSerializer.Deserialize<GlobalAntiAdvertisementPayload>(File.ReadAllText(p),_json)??throw new InvalidDataException("Leere Schutzregeln."));
            foreach(var snapshot in snapshots.Values)
                foreach(string raw in snapshot.BlockedSteamIds)
                    if(ulong.TryParse(raw,out ulong id)&&id!=0)
                        StrongestComm(_gags,id,new CommEntry{Reason="Übernommene lokale Werbeschutzsperre",CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=null});
            // Save local sanctions before clearing any legacy identity cache.
            SaveGags();
            foreach(var (path,snapshot) in snapshots){snapshot.BlockedSteamIds.Clear();AtomicWrite(path,JsonSerializer.Serialize(snapshot,_json));}
            // ChatGuard reads this legacy file as well. Leaving an old ban here
            // would silently prevent chatting even after a successful local unban.
            SaveGlobalBans();SaveGlobalComms();
            WriteNewFleet(receipt,JsonSerializer.Serialize(new{At=DateTimeOffset.UtcNow,Scope="local",Sources=paths.Length},_json));
        }
        lock(_antiAdProtectionSync)_antiAdBlockedSteamIds.Clear();
    }
}
