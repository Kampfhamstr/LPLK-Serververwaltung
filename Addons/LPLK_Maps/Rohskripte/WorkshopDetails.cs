using System.Text.Json;

namespace LPLK_Maps;

public sealed record WorkshopItem(string Id,string Name,bool IsMap,long Bytes,long Updated=0);
public static class WorkshopDetails
{
    public static List<WorkshopItem> Parse(string json,IReadOnlySet<string> expected)
    {
        using var doc=JsonDocument.Parse(json);var result=new List<WorkshopItem>();
        foreach(var item in doc.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            string? id=MapPolicy.WorkshopId(item.GetProperty("publishedfileid").ToString());
            if(id is null||!expected.Contains(id)||result.Any(x=>x.Id==id))throw new InvalidDataException("Unbekannte/doppelte Workshop-Antwort.");
            if(item.GetProperty("result").GetInt32()!=1)throw new InvalidDataException("Workshop-Item nicht erreichbar: "+id);
            bool isMap=item.TryGetProperty("consumer_app_id",out var app)&&app.GetInt32()==730&&item.TryGetProperty("tags",out var tags)&&
                tags.EnumerateArray().Any(t=>t.GetProperty("tag").GetString()?.Equals("Map",StringComparison.OrdinalIgnoreCase)==true);
            string name=item.GetProperty("title").GetString()??id;
            name=new string(name.Where(c=>!char.IsControl(c)).Take(120).ToArray());if(name.Length==0)name=id;
            long bytes=item.TryGetProperty("file_size",out var size)&&long.TryParse(size.ToString(),out var value)?value:0;
            long updated=item.TryGetProperty("time_updated",out var time)&&time.TryGetInt64(out var seconds)?seconds:0;
            result.Add(new(id,name,isMap,Math.Max(0,bytes),updated));
        }
        if(result.Count!=expected.Count)throw new InvalidDataException("Workshop-Antwort unvollständig.");return result;
    }
    public static async Task<List<WorkshopItem>> Fetch(IEnumerable<string> ids,CancellationToken token)
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=4*1024*1024};
        List<WorkshopItem> result=[];
        foreach(var batch in ids.Distinct().Chunk(100))
        {
            Dictionary<string,string> data=new(){["itemcount"]=batch.Length.ToString()};
            for(int i=0;i<batch.Length;i++)data[$"publishedfileids[{i}]"]=batch[i];
            using var form=new FormUrlEncodedContent(data);
            using var response=await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",form,token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();result.AddRange(Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false),batch.ToHashSet()));
        }
        return result;
    }
    public static List<MapEntry> Merge(IReadOnlyList<MapEntry> previous,IReadOnlyList<WorkshopItem> items)
    {
        var membership=items.Select(i=>i.Id).ToHashSet();
        var merged=previous.Select(m=>m with{InPool=membership.Contains(m.Id)}).ToDictionary(m=>m.Id);
        foreach(var item in items)
        {
            if(merged.TryGetValue(item.Id,out var old))
            {
                var next=old with{Name=item.Name,InPool=true,IsMap=item.IsMap,RemoteMapVerified=item.IsMap,DownloadBytes=item.Bytes,WorkshopUpdated=item.Updated};
                if(old.WorkshopUpdated>0&&item.Updated>old.WorkshopUpdated)next=next with{MeasuredAt=null,LastProbeAt=null,ProbeError=null,TSpawns=null,CtSpawns=null,BombSites=null,Hostages=null};
                merged[item.Id]=next;
            }
            else merged[item.Id]=new(){Id=item.Id,Name=item.Name,InPool=true,IsMap=item.IsMap,RemoteMapVerified=item.IsMap,DownloadBytes=item.Bytes,WorkshopUpdated=item.Updated,MaxPlayers=128};
        }
        return merged.Values.OrderBy(m=>m.Name).ToList();
    }
}
