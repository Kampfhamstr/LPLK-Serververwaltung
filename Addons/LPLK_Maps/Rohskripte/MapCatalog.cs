using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Maps;

public sealed partial class Plugin
{
    private HashSet<string> installed=[];
    private CancellationTokenSource? collectionCancellation;
    private bool collectionLoading;
    private long nextPoolRefresh,emptySince;
    private string? probeId,probeOrigin;
    private string poolMessage="Workshop-Abgleich ausstehend";
    private MapSettingsFile? configuration;
    private string? reloadError;
    private bool statusWriteFailed;
    private long nextConfigCheck;
    private string ConfigPath=>Path.Combine(Core.GameFilesDirectory,"addons","swiftlys2","configs","plugins","LPLK_Maps","config.jsonc");
    private string CollectionPath=>Path.Combine(Core.PluginDataDirectory,"workshop-cache.json");
    private string WorkshopRoot=>settings.WorkshopPath.Length>0?settings.WorkshopPath:Path.GetFullPath(Path.Combine(Core.GameFilesDirectory,"..","bin","linuxsteamrt64","steamapps","workshop","content","730"));
    private bool Available(MapEntry m)=>m.IsMap&&!m.Removed&&!settings.ExcludedWorkshopIds.Contains(m.Id)&&
        (installed.Contains(m.Id)||(!settings.RequireVpkFile&&m.RemoteMapVerified));
    private bool Votable(MapEntry m)=>Available(m)&&MapPolicy.LimitsUsable(m,settings);
    private bool AnyHuman()=>Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient);
    private MapSettings ReadConfiguration()
    {
        configuration=new(ConfigPath,()=>
        {
            string seed=Path.Combine(Core.PluginPath,"resources","settings.seed.json");
            return File.Exists(seed)?JsonSerializer.Deserialize<MapSettings>(File.ReadAllText(seed))??new():new MapSettings();
        });
        return configuration.Current;
    }
    private void ActivateConfiguration(MapSettings next,bool resetVotes)
    {
        if(MapSettingsFile.Equivalent(settings,next)){settings=next;return;}
        settings=next;
        if(resetVotes){CancelVote();rtv.Clear();extendVotes.Clear();directVotes.Clear();endVoteStarted=nextMap is not null;nextPoolRefresh=0;Scan();}
    }
    private void RefreshSettings()
    {
        if(module?.Active!=true||configuration is null)return;
        string? previous=reloadError;
        if(configuration.Reload())ActivateConfiguration(configuration.Current,true);
        reloadError=configuration.Error;
        if(reloadError is not null&&reloadError!=previous)Core.Logger.LogWarning("LPLK_Maps: ungültige Konfiguration; bisherige Regeln bleiben aktiv: {Error}",reloadError);
    }
    private void ChangeConfiguration(Func<MapSettings,MapSettings> edit,bool resetVotes,Func<MapSettings,bool>? allowed=null)
    {
        if(module?.Active!=true||configuration is null)throw new InvalidOperationException("Mapmodul nicht aktiv.");
        RefreshSettings();
        if(allowed is not null&&!allowed(settings))throw new InvalidDataException("Keine aktuelle Berechtigung.");
        var next=configuration.Change(edit);ActivateConfiguration(next,resetVotes);reloadError=null;WriteStatus();
    }
    private void CheckConfiguration(long now)
    {
        if(now<nextConfigCheck)return;nextConfigCheck=now+5000;
        RefreshSettings();
        if(now>=nextPoolRefresh&&!collectionLoading)RefreshCollection();
    }
    private void LoadCollectionCache()
    {
        if(!File.Exists(CollectionPath))return;
        try{var cached=JsonSerializer.Deserialize<List<WorkshopItem>>(File.ReadAllText(CollectionPath));if(cached is not null)poolMessage=$"Letzter erfolgreicher Abgleich: {cached.Count} Einträge";}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Maps: Kollektionscache nicht lesbar; bestehender Mapkatalog bleibt erhalten.");}
    }
    private void RefreshCollection()
    {
        if(collectionLoading)return;
        nextPoolRefresh=Environment.TickCount64+settings.PoolRefreshMinutes*60000L;
        string collection=settings.CollectionId;
        if(collection.Length==0)collection=WorkshopCollections.ReadStartupCollection()??"";
        var explicitIds=settings.WorkshopIds.ToArray();
        // The migrated seed is also resolved once: it supplies authoritative titles and map/asset type.
        if(collection.Length==0&&explicitIds.Length==0)explicitIds=maps.Where(m=>m.Workshop&&m.InPool).Select(m=>m.Id).ToArray();
        if(collection.Length==0&&explicitIds.Length==0){poolMessage="Keine Workshop-ID eingetragen";return;}
        collectionLoading=true;collectionCancellation?.Dispose();collectionCancellation=new(TimeSpan.FromSeconds(90));var token=collectionCancellation.Token;
        string expectedConfig=JsonSerializer.Serialize(settings);bool keepBuiltin=collection.Length==0&&settings.WorkshopIds.Length==0;
        _=Task.Run(async()=>
        {
            List<WorkshopItem>? items=null;string? error=null;
            try
            {
                HashSet<string> ids=explicitIds.ToHashSet();
                if(collection.Length>0){var result=await WorkshopCollections.TryFetchAsync(collection,token);if(result.Snapshot is null)throw new InvalidDataException(result.Error);ids.UnionWith(result.Snapshot.MemberIds);}
                if(ids.Count>2000)throw new InvalidDataException("Mehr als 2000 Maps im Pool.");
                items=await WorkshopDetails.Fetch(ids,token);
            }
            catch(Exception e){error=e is InvalidDataException?e.Message:e.GetType().Name;}
            if(!module.Active)return;
            Core.Scheduler.NextWorldUpdate(()=>
            {
                if(!module.Active)return;collectionLoading=false;
                if(JsonSerializer.Serialize(settings)!=expectedConfig){nextPoolRefresh=0;return;}
                if(error is not null||items is null){poolMessage="Steam-Abgleich fehlgeschlagen; letzter Pool bleibt erhalten: "+error;Core.Logger.LogWarning("LPLK_Maps: {Message}",poolMessage);return;}
                try
                {
                    var next=WorkshopDetails.Merge(maps,items);
                    if(keepBuiltin)next=next.Select(m=>!m.Workshop?m with{InPool=true}:m).ToList();
                    MapPolicy.Validate(next);AtomicJson.Write(MapsPath,next);maps=next;AtomicJson.Write(CollectionPath,items);
                    poolMessage=$"Workshop-Abgleich erfolgreich: {items.Count} Einträge";Core.Logger.LogInformation("LPLK_Maps: {Message}",poolMessage);Scan();WriteStatus();
                }
                catch(Exception e){poolMessage="Workshop-Abgleich konnte nicht vollständig gespeichert werden";Core.Logger.LogWarning(e,"LPLK_Maps: {Message}",poolMessage);}
            });
        });
    }
    private void Scan()
    {
        try
        {
            HashSet<string> ready=[];var next=maps.ToDictionary(m=>m.Id);bool changed=false;
            foreach(var map in maps.Where(m=>!m.Workshop))
                if(File.Exists(Path.Combine(Core.GameFilesDirectory,"maps",map.Id+".vpk")))ready.Add(map.Id);
            if(Directory.Exists(WorkshopRoot))
            foreach(var dir in new DirectoryInfo(WorkshopRoot).EnumerateDirectories())
            {
                string? id=MapPolicy.WorkshopId(dir.Name);if(id is null||(dir.Attributes&FileAttributes.ReparsePoint)!=0||settings.ExcludedWorkshopIds.Contains(id))continue;
                HashSet<string> names=[];
                foreach(var vpk in dir.EnumerateFiles("*.vpk"))
                {
                    if((vpk.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                    try{using var stream=vpk.OpenRead();names.UnionWith(VpkMaps.Read(stream));}catch(InvalidDataException){}catch(EndOfStreamException){}
                }
                if(names.Count==0)continue;ready.Add(id);
                if(!next.TryGetValue(id,out var map))map=new(){Id=id,Name=names.Order().First(),InPool=false,MaxPlayers=128};
                string source=names.Contains(map.SourceFolder)?map.SourceFolder:names.Order().First();
                var publish=new FileInfo(Path.Combine(dir.FullName,"publish_data.txt"));
                if(publish.Exists&&publish.Length<1024*1024&&(publish.Attributes&FileAttributes.ReparsePoint)==0)
                {
                    var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(publish.FullName),"\"source_folder\"\\s*\"([^\"]+)\"");
                    if(match.Success&&MapPolicy.SafeMapToken(match.Groups[1].Value))source=match.Groups[1].Value;
                }
                var updated=map with{IsMap=true,SourceFolder=source};
                if(updated!=map||!next.ContainsKey(id)){next[id]=updated;changed=true;}
            }
            if(changed){var list=next.Values.ToList();MapPolicy.Validate(list);AtomicJson.Write(MapsPath,list);maps=list;}
            installed=ready;
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Maps: Workshop-Scan fehlgeschlagen; letzter Katalog bleibt erhalten.");}
    }
    private void MeasureCurrent(IPlayer? admin,int attempt=0)
    {
        var map=maps.FirstOrDefault(m=>MapPolicy.Confirmed(m,CurrentMap,Workshop));if(map is null)return;
        try
        {
            int t=Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").Count(x=>x.IsValid&&x.Enabled);
            int ct=Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").Count(x=>x.IsValid&&x.Enabled);
            var targets=Core.EntitySystem.GetAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").Where(x=>x.IsValid).ToList();
            int designations=targets.Select(x=>(x.IsBombSiteB,x.BombSiteDesignation)).Distinct().Count();
            var goals=Core.EntitySystem.GetAllEntitiesByClass<CCSPlayerResource>().FirstOrDefault(x=>x.IsValid&&x.FoundGoalPositions);
            int? goalSites=null;
            if(goals is not null)
            {
                var a=goals.BombsiteCenterA;var b=goals.BombsiteCenterB;
                bool finite=float.IsFinite(a.X)&&float.IsFinite(a.Y)&&float.IsFinite(a.Z)&&float.IsFinite(b.X)&&float.IsFinite(b.Y)&&float.IsFinite(b.Z);
                if(finite)goalSites=(a.X!=0||a.Y!=0||a.Z!=0?1:0)+(b.X!=0||b.Y!=0||b.Z!=0?1:0);
            }
            int bomb=MapPolicy.BombSiteCount(targets.Count,designations,goalSites);
            int hostages=Core.EntitySystem.GetAllEntitiesByDesignerName<CHostage>("hostage_entity").Count(x=>x.IsValid);
            var measured=MapPolicy.CaptureMeasurement(map,t,ct,bomb,hostages) with{BombVolumes=targets.Count,ObjectiveEvidence=$"Volumes={targets.Count}; Designations={designations}; EngineGoals={goalSites?.ToString()??"unbekannt"}"};var limits=measured.LimitProposal!;
            ReplaceMap(measured);
            Core.Logger.LogInformation("LPLK_Maps: {Id} gemessen: T={T}, CT={CT}, Bomb={Bomb}, Geiseln={Hostages}; Vorschlag {Min}–{Max} Spieler, Freigabe={Approved}",map.Id,t,ct,bomb,hostages,limits.MinPlayers,limits.MaxPlayers,MapPolicy.LimitsApproved(measured));
            admin?.SendChat($"[LPLK] {map.Name}: Vorschlag {limits.MinPlayers}–{limits.MaxPlayers} Spieler; {limits.Reserve} Plätze Reserve. Bisherige Grenzen bleiben bis zur Freigabe erhalten.");
        }
        catch(Exception e)
        {
            if(attempt<3)
            {
                int serial=generation;Core.Scheduler.DelayBySeconds(3,()=>{if(module.Active&&serial==generation&&MapPolicy.Confirmed(map,CurrentMap,Workshop))MeasureCurrent(null,attempt+1);});
                return;
            }
            ReplaceMap(map with{MeasuredAt=null,TSpawns=null,CtSpawns=null,BombSites=null,Hostages=null,ProbeError=e is InvalidDataException?e.Message:"Messung fehlgeschlagen",LastProbeAt=DateTimeOffset.UtcNow});
            Core.Logger.LogWarning(e,"LPLK_Maps: Messung nicht bestätigt: {Id}; aus normalen Votes ausgeschlossen.",map.Id);admin?.SendChat("[LPLK] Messung fehlgeschlagen; keine Freigabe für Votes.");
        }
        WriteStatus();
    }
    private void ReplaceMap(MapEntry value){var next=maps.Select(m=>m.Id==value.Id?value:m).ToList();MapPolicy.Validate(next);AtomicJson.Write(MapsPath,next);maps=next;}
    private void AutoProbe(long now)
    {
        if(AnyHuman()){emptySince=0;return;}
        if(emptySince==0){emptySince=now;return;}
        if(!settings.AutoMeasureEmptyServer||!settings.Enabled||pending is not null||voteEnds>0||now-emptySince<settings.EmptySecondsBeforeProbe*1000L)return;
        if(probeId is not null)
        {
            var measured=maps.FirstOrDefault(m=>m.Id==probeId);
            if(measured is not null&&MapPolicy.Confirmed(measured,CurrentMap,Workshop)&&!MapPolicy.Measured(measured)&&measured.ProbeError is null)
            {
                // OnMapLoad owns the bounded measurement retries; do not switch away while they are pending.
                if(now-mapStarted<30000)return;
                ReplaceMap(measured with{ProbeError="Messung nicht rechtzeitig bestätigt",LastProbeAt=DateTimeOffset.UtcNow});
            }
            probeId=null;
        }
        var candidate=maps.FirstOrDefault(m=>m.InPool&&m.Enabled&&!m.Removed&&m.IsMap&&!MapPolicy.Measured(m)&&
            (m.LastProbeAt is null||DateTimeOffset.UtcNow-m.LastProbeAt>TimeSpan.FromHours(6))&&!settings.ExcludedWorkshopIds.Contains(m.Id)&&(installed.Contains(m.Id)||m.RemoteMapVerified));
        if(candidate is null)
        {
            if(probeOrigin is {} origin){probeOrigin=null;var original=maps.FirstOrDefault(m=>m.Id==origin&&Available(m));if(original is not null&&!MapPolicy.Confirmed(original,CurrentMap,Workshop))Schedule(original,3);}
            return;
        }
        if(!EnoughDisk(candidate))return;
        probeOrigin??=maps.FirstOrDefault(m=>MapPolicy.Confirmed(m,CurrentMap,Workshop))?.Id;
        probeId=candidate.Id;ReplaceMap(candidate with{LastProbeAt=DateTimeOffset.UtcNow,ProbeError=null});
        Schedule(candidate,3,true);
    }
    private bool EnoughDisk(MapEntry candidate)
    {
        if(installed.Contains(candidate.Id))return true;
        try
        {
            long size=Directory.Exists(WorkshopRoot)?new DirectoryInfo(WorkshopRoot).EnumerateFiles("*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}).Sum(f=>f.Length):0;
            var drive=new DriveInfo(Path.GetPathRoot(Core.GameFilesDirectory)!);long bytes=Math.Max(candidate.DownloadBytes,512L*1024*1024);
            if(size+bytes>settings.MaxWorkshopGiB*1024L*1024*1024||drive.AvailableFreeSpace-bytes<settings.MinimumFreeDiskGiB*1024L*1024*1024)
            {poolMessage="Automatische Mapprüfung wartet: Speicherreserve erreicht";emptySince=Environment.TickCount64;return false;}
            return true;
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Maps: Speicherreserve nicht prüfbar; automatische Downloads pausieren.");emptySince=Environment.TickCount64;return false;}
    }
}
