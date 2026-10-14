using System.Text.Json;

namespace CS2ServerPortal;

internal sealed record MapLimitSuggestion(int MinPlayers,int MaxPlayers,int Reserve,string Signature);
internal sealed record MapReviewRow(string Id,string Name,bool InPool,bool Enabled,bool Removed,int MinPlayers,int MaxPlayers,
    int? ConfiguredMaxPlayers,int? TSpawns,int? CtSpawns,int? BombSites,int? Hostages,DateTimeOffset? MeasuredAt,string? ProbeError,MapLimitSuggestion? LimitProposal,bool Approved)
{
    public bool? IsMap { get; init; }
    public bool Excluded { get; init; }
    public int? BeforeMinPlayers { get; init; }
    public int? BeforeMaxPlayers { get; init; }
    public string Result => Excluded ? "In Map-Konfiguration ausgeschlossen" : IsMap is false ? "Kein Map-Inhalt; keine Vermessung" : ProbeError is not null ? "Messfehler: "+ProbeError : MeasuredAt is null || LimitProposal is null ? "Messung offen" : Approved ? "Freigegeben" : "Freigabe offen";
    public string Pool => Removed ? "Manuell gesperrt" : !Enabled ? "Manuell deaktiviert" : !InPool ? "Außerhalb der Kollektion" : "In Kollektion";
    public string ProposedExclusion => Excluded ? "Bereits durch Konfiguration ausgeschlossen" : IsMap is false ? "Kein spielbarer Map-Eintrag" : MeasuredAt is not null && ProbeError is null && LimitProposal is {} p ? $"Unter {p.MinPlayers} / über {p.MaxPlayers} Spielern" : "Noch nicht berechenbar";
}
internal sealed record MapReviewSnapshot(DateTimeOffset UpdatedAt,bool MeasurementOnly,int Catalog,int Measured,int Approved,string PoolMessage,string? ProbeId,string Transition,List<MapReviewRow> LimitReview);
internal static class MapReview
{
    static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    internal static string PathFor(string workspace)=>Path.Combine(workspace,"state","map-limit-review.json");
    internal static MapReviewSnapshot Parse(string json)
    {
        if(json.Length>4*1024*1024)throw new InvalidDataException("Mapübersicht zu groß.");
        var value=JsonSerializer.Deserialize<MapReviewSnapshot>(json,Options)??throw new InvalidDataException("Mapübersicht leer.");
        if(value.UpdatedAt==default||value.LimitReview is null||value.LimitReview.Count>2000||value.LimitReview.Select(m=>m.Id).Distinct().Count()!=value.LimitReview.Count)
            throw new InvalidDataException("Mapübersicht unvollständig oder enthält doppelte IDs.");
        return value;
    }
    internal static MapReviewSnapshot? Read(string workspace)=>File.Exists(PathFor(workspace))?Parse(File.ReadAllText(PathFor(workspace))):null;
    // The 0.2.2 status does not include content kinds; read only this metadata from its catalog.
    internal static MapReviewSnapshot WithContentKinds(MapReviewSnapshot snapshot,string json)
    {
        if(json.Length>4*1024*1024)throw new InvalidDataException("Mapkatalog zu groß.");
        var catalog=JsonSerializer.Deserialize<List<ContentKind>>(json,Options)??throw new InvalidDataException("Mapkatalog leer.");
        if(catalog.Count>2000||catalog.Any(m=>string.IsNullOrWhiteSpace(m.Id))||catalog.Select(m=>m.Id).Distinct().Count()!=catalog.Count)
            throw new InvalidDataException("Mapkatalog enthält ungültige oder doppelte IDs.");
        var kinds=catalog.ToDictionary(m=>m.Id,m=>m.IsMap);
        return snapshot with{LimitReview=snapshot.LimitReview.Select(m=>m with{IsMap=m.IsMap??kinds.GetValueOrDefault(m.Id)}).ToList()};
    }
    private sealed record ContentKind(string Id,bool? IsMap);
    internal static MapReviewSnapshot WithExclusions(MapReviewSnapshot snapshot,string json)
    {
        if(json.Length>1024*1024)throw new InvalidDataException("Map-Konfiguration zu groß.");
        using var doc=JsonDocument.Parse(json,new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        var root=doc.RootElement.TryGetProperty("LPLK_Maps",out var section)?section:doc.RootElement;
        if(!root.TryGetProperty("ExcludedWorkshopIds",out var values)||values.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Ausschlüsse der Map-Konfiguration fehlen.");
        var ids=values.EnumerateArray().Select(v=>v.GetString()??throw new InvalidDataException("Ungültiger Workshop-Ausschluss.")).ToHashSet();
        return snapshot with{LimitReview=snapshot.LimitReview.Select(m=>m with{Excluded=ids.Contains(m.Id)}).ToList()};
    }
    internal static async Task<MapReviewSnapshot> RefreshAsync(string workspace)
    {
        using var client=new PanelClient(new CredentialStore(workspace).Read()??throw new InvalidOperationException("Panelverbindung fehlt."));
        const string path="/game/csgo/addons/swiftlys2/data/LPLK_Maps";
        string parent="";
        foreach(string segment in path.Split('/',StringSplitOptions.RemoveEmptyEntries))
        {
            var entries=await client.ListFilesAsync(PanelClient.TestServerId,parent.Length==0?"/":parent);
            if(!entries.Any(f=>f.Name==segment&&!f.IsFile&&!f.IsSymlink))throw new InvalidDataException("Mapdaten fehlen oder Pfad ist eine Verknüpfung.");
            parent+="/"+segment;
        }
        var files=await client.ListFilesAsync(PanelClient.TestServerId,path);
        if(!files.Any(f=>f.Name=="status.json"&&f.IsFile&&!f.IsSymlink&&f.Size<=4*1024*1024))throw new InvalidDataException("Mapstatus fehlt, ist zu groß oder verknüpft.");
        var snapshot=Parse(await client.ReadFileAsync(PanelClient.TestServerId,path+"/status.json"));
        if(!files.Any(f=>f.Name=="maps.json"&&f.IsFile&&!f.IsSymlink&&f.Size<=4*1024*1024))throw new InvalidDataException("Mapkatalog fehlt, ist zu groß oder verknüpft.");
        snapshot=WithContentKinds(snapshot,await client.ReadFileAsync(PanelClient.TestServerId,path+"/maps.json"));
        const string configPath="/game/csgo/addons/swiftlys2/configs/plugins/LPLK_Maps";
        parent="/game/csgo/addons/swiftlys2"; // Verified above; verify the remaining fixed configuration path.
        foreach(string segment in new[]{"configs","plugins","LPLK_Maps"})
        {
            var entries=await client.ListFilesAsync(PanelClient.TestServerId,parent);
            if(!entries.Any(f=>f.Name==segment&&!f.IsFile&&!f.IsSymlink))throw new InvalidDataException("Map-Konfiguration fehlt oder ist verknüpft.");
            parent+="/"+segment;
        }
        var configFiles=await client.ListFilesAsync(PanelClient.TestServerId,configPath);
        if(!configFiles.Any(f=>f.Name=="config.jsonc"&&f.IsFile&&!f.IsSymlink&&f.Size<=1024*1024))throw new InvalidDataException("Map-Konfiguration fehlt, ist zu groß oder verknüpft.");
        snapshot=WithExclusions(snapshot,await client.ReadFileAsync(PanelClient.TestServerId,configPath+"/config.jsonc"));
        string baseline=TestMapApproval.BaselinePath(workspace);
        if(File.Exists(baseline))
        {
            var before=Parse(File.ReadAllText(baseline)).LimitReview.ToDictionary(m=>m.Id);
            snapshot=snapshot with{LimitReview=snapshot.LimitReview.Select(m=>before.TryGetValue(m.Id,out var old)&&old.LimitProposal is {} p&&p.Signature==m.LimitProposal?.Signature
                ?m with{BeforeMinPlayers=old.MinPlayers,BeforeMaxPlayers=old.MaxPlayers}:m).ToList()};
        }
        string target=PathFor(workspace),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try{File.WriteAllText(temp,JsonSerializer.Serialize(snapshot,Options));File.Move(temp,target,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return snapshot;
    }
}
