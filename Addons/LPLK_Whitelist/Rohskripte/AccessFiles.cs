using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK.Common;

namespace LPLK_Whitelist;

// Treat configuration and list as one validated snapshot. Preserve legacy fields
// (including command labels) when editing one of the implemented rules.
public sealed record AccessSnapshot(string ConfigText,string ListText,AccessSettings Settings,HashSet<string> Ids);
public static class AccessFiles
{
    public const int MaxBytes=4*1024*1024;
    private static readonly JsonDocumentOptions DocumentOptions=new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};
    private static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static string ReadBounded(string path)
    {
        var file=new FileInfo(path);
        if(!file.Exists||file.Length>MaxBytes||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Zugangsdatei fehlt, ist zu groß oder verknüpft.");
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        var bytes=reader.ReadBytes(MaxBytes+1);if(bytes.Length>MaxBytes)throw new InvalidDataException("Zugangsdatei zu groß.");
        return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
    }
    private static JsonObject Parse(string text,out JsonObject section)
    {
        var root=JsonNode.Parse(text,documentOptions:DocumentOptions) as JsonObject??throw new InvalidDataException("Konfiguration muss ein Objekt sein.");
        static void Unique(JsonObject obj){if(obj.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=obj.Count)throw new InvalidDataException("Mehrdeutige Konfiguration.");}
        Unique(root);string? key=root.Select(x=>x.Key).SingleOrDefault(x=>x.Equals("Whitelist",StringComparison.OrdinalIgnoreCase));
        section=key is null?root:root[key] as JsonObject??throw new InvalidDataException("Whitelist muss ein Objekt sein.");Unique(section);return root;
    }
    public static AccessSnapshot Read(string configPath,string listPath)
    {
        string config=ReadBounded(configPath),list=ReadBounded(listPath);Parse(config,out var section);
        var settings=section.Deserialize<AccessSettings>(Json)??throw new InvalidDataException("Konfiguration leer.");AccessSettings.Validate(settings);
        return new(config,list,settings,AccessPolicy.Parse(list));
    }
    public static void EnsureCurrent(string configPath,string listPath,AccessSnapshot snapshot)
    {
        if(ReadBounded(configPath)!=snapshot.ConfigText||ReadBounded(listPath)!=snapshot.ListText)throw new IOException("Zugangsdateien wurden inzwischen geändert; erneut versuchen.");
    }
    public static void WriteSettings(string configPath,string listPath,AccessSnapshot snapshot,AccessSettings next)
    {
        AccessSettings.Validate(next);var root=Parse(snapshot.ConfigText,out var section);
        var values=JsonSerializer.SerializeToNode(next,Json)!.AsObject();
        foreach(var value in values){string key=section.Select(x=>x.Key).SingleOrDefault(k=>k.Equals(value.Key,StringComparison.OrdinalIgnoreCase))??value.Key;section[key]=value.Value?.DeepClone();}
        EnsureCurrent(configPath,listPath,snapshot);AtomicJson.Write(configPath,root);
    }
    public static void WriteIds(string configPath,string listPath,AccessSnapshot snapshot,HashSet<string> next)
    {
        string content=string.Join('\n',next.Order())+"\n";AccessPolicy.Parse(content);
        var tmp=listPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(tmp,content,new UTF8Encoding(false));EnsureCurrent(configPath,listPath,snapshot);File.Move(tmp,listPath,true);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
