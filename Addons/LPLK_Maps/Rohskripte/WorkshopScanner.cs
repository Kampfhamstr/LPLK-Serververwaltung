namespace LPLK_Maps;

public sealed record WorkshopScanResult(HashSet<string> Installed,Dictionary<string,string> Sources,int VpkReads);
// Used by one worker at a time; no engine objects or mutable catalog references.
public sealed class WorkshopScanner
{
    private sealed record Cached(long Length,long Modified,HashSet<string> Names);
    private readonly Dictionary<string,Cached> cache=new(StringComparer.Ordinal);
    public WorkshopScanResult Read(string gameDirectory,string workshopRoot,MapEntry[] maps,string[] excluded)
    {
        HashSet<string> ready=[],seen=[];Dictionary<string,string> sources=[];int reads=0;
        var known=maps.ToDictionary(m=>m.Id);var skip=excluded.ToHashSet();
        foreach(var map in maps.Where(m=>!m.Workshop))
            if(File.Exists(Path.Combine(gameDirectory,"maps",map.Id+".vpk")))ready.Add(map.Id);
        if(Directory.Exists(workshopRoot))
        foreach(var dir in new DirectoryInfo(workshopRoot).EnumerateDirectories())
        {
            string? id=MapPolicy.WorkshopId(dir.Name);if(id is null||(dir.Attributes&FileAttributes.ReparsePoint)!=0||skip.Contains(id))continue;
            HashSet<string> names=[];
            foreach(var vpk in dir.EnumerateFiles("*.vpk"))
            {
                if((vpk.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                string path=vpk.FullName;seen.Add(path);long length=vpk.Length,modified=vpk.LastWriteTimeUtc.Ticks;
                if(cache.TryGetValue(path,out var old)&&old.Length==length&&old.Modified==modified){names.UnionWith(old.Names);continue;}
                try
                {
                    using var stream=vpk.OpenRead();var found=VpkMaps.Read(stream);reads++;
                    vpk.Refresh();if(vpk.Length==length&&vpk.LastWriteTimeUtc.Ticks==modified){cache[path]=new(length,modified,found);names.UnionWith(found);}
                }
                catch(InvalidDataException){cache.Remove(path);}catch(EndOfStreamException){cache.Remove(path);}
            }
            if(names.Count==0)continue;ready.Add(id);
            string previous=known.GetValueOrDefault(id)?.SourceFolder??"";
            string source=names.Contains(previous)?previous:names.Order().First();
            var publish=new FileInfo(Path.Combine(dir.FullName,"publish_data.txt"));
            if(publish.Exists&&publish.Length<1024*1024&&(publish.Attributes&FileAttributes.ReparsePoint)==0)
            {
                var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(publish.FullName),"\"source_folder\"\\s*\"([^\"]+)\"");
                if(match.Success&&MapPolicy.SafeMapToken(match.Groups[1].Value))source=match.Groups[1].Value;
            }
            sources[id]=source;
        }
        foreach(string path in cache.Keys.Where(p=>!seen.Contains(p)).ToArray())cache.Remove(path);
        return new(ready,sources,reads);
    }
}
