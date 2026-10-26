using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LPLK_Sounds;

// Separate from the mutable live object: failed edits must never change playback rules.
public sealed class SoundConfigFile
{
    public const int MaxBytes=65536;
    private readonly string path;
    public SoundsConfig Current {get;private set;}
    public SoundConfigFile(string path){this.path=path;Current=Read().Config;}
    private string ReadText()
    {
        var file=new FileInfo(path);
        if(!file.Exists||file.Length>MaxBytes||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Soundkonfiguration fehlt, ist zu groß oder verknüpft.");
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        var bytes=reader.ReadBytes(MaxBytes+1);if(bytes.Length>MaxBytes)throw new InvalidDataException("Soundkonfiguration zu groß.");
        return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
    }
    private (string Text,JsonObject Root,SoundsConfig Config) Read()
    {
        string text=ReadText();var root=JsonNode.Parse(text,documentOptions:new(){AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip}) as JsonObject??throw new InvalidDataException("Soundkonfiguration muss ein Objekt sein.");
        if(root.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=root.Count)throw new InvalidDataException("Mehrdeutige Soundkonfiguration.");
        var config=root.Deserialize<SoundsConfig>(SoundsConfig.Json)??throw new InvalidDataException("Soundkonfiguration leer.");config.Validate();return(text,root,config);
    }
    public void Reload()=>Current=Read().Config;
    public void Change(Action<SoundsConfig> edit)
    {
        var source=Read();var next=Clone(source.Config);edit(next);next.Validate();
        foreach(var item in JsonSerializer.SerializeToNode(next,SoundsConfig.Json)!.AsObject())
        {string key=source.Root.Select(x=>x.Key).SingleOrDefault(k=>k.Equals(item.Key,StringComparison.OrdinalIgnoreCase))??item.Key;source.Root[key]=item.Value?.DeepClone();}
        string text=source.Root.ToJsonString(SoundsConfig.Json);if(Encoding.UTF8.GetByteCount(text)>MaxBytes)throw new InvalidDataException("Soundkonfiguration zu groß.");
        if(ReadText()!=source.Text)throw new IOException("Soundkonfiguration inzwischen geändert; erneut versuchen.");
        DataFiles.WriteAtomic(path,text);Current=next;
    }
    public static SoundsConfig Clone(SoundsConfig c)=>JsonSerializer.Deserialize<SoundsConfig>(JsonSerializer.Serialize(c,SoundsConfig.Json),SoundsConfig.Json)!;
    public static bool Same(SoundsConfig a,SoundsConfig b)=>a.Enabled==b.Enabled&&a.AutoCatalogFromAddon==b.AutoCatalogFromAddon&&a.RequireAddonsManager==b.RequireAddonsManager&&a.BroadcastCooldownSeconds==b.BroadcastCooldownSeconds&&a.DeadPlayersOnlyToDead==b.DeadPlayersOnlyToDead&&a.WorkshopIds.SequenceEqual(b.WorkshopIds,StringComparer.Ordinal);
    public static string AssetKey(SoundsConfig c)=>JsonSerializer.Serialize(new{c.AutoCatalogFromAddon,c.WorkshopIds});
}
