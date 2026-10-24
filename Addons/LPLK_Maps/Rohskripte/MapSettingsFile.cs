using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK.Common;

namespace LPLK_Maps;

// Game-thread access. Preserve the historical flat/wrapped JSONC layout and extension fields.
public sealed class MapSettingsFile
{
    private readonly string path;
    private string accepted;
    private static readonly JsonDocumentOptions Jsonc = new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};
    public MapSettings Current {get;private set;}
    public string? Error {get;private set;}
    public MapSettingsFile(string path,Func<MapSettings> seed)
    {
        this.path=path;
        if(!File.Exists(path)&&!Directory.Exists(path)){var initial=seed();MapSettings.Validate(initial);AtomicJson.Write(path,new{LPLK_Maps=initial});}
        accepted=Read();Current=Parse(accepted);
    }
    private string Read()
    {
        var file=new FileInfo(path);
        if(!file.Exists||file.Length>262144||file.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Mapkonfiguration fehlt, ist verknüpft oder zu groß.");
        string text=File.ReadAllText(path);if(text.Length>262144)throw new InvalidDataException("Mapkonfiguration zu groß.");return text;
    }
    private static JsonObject Root(string text)
    {
        using var document=JsonDocument.Parse(text,Jsonc);
        void Unique(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>();foreach(var item in element.EnumerateObject()){if(!keys.Add(item.Name))throw new InvalidDataException("Doppelte Konfigurationsfelder.");Unique(item.Value);}}
            else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())Unique(item);
        }
        Unique(document.RootElement);
        return JsonNode.Parse(text,documentOptions:Jsonc) as JsonObject??throw new InvalidDataException("Mapkonfiguration muss ein Objekt sein.");
    }
    private static JsonObject Section(JsonObject root)=>root.ContainsKey("LPLK_Maps")?root["LPLK_Maps"] as JsonObject??throw new InvalidDataException("Ungültiger LPLK_Maps-Abschnitt."):root;
    private static MapSettings Parse(string text)
    {
        try{var value=Section(Root(text)).Deserialize<MapSettings>()??throw new InvalidDataException("Leere Mapkonfiguration.");MapSettings.Validate(value);return value;}
        catch(Exception e)when(e is JsonException or NullReferenceException or ArgumentException){throw new InvalidDataException("Ungültige Mapkonfiguration.",e);}
    }
    public static bool Equivalent(MapSettings a,MapSettings b)=>JsonNode.DeepEquals(JsonSerializer.SerializeToNode(a),JsonSerializer.SerializeToNode(b));
    public bool Reload()
    {
        try{string text=Read();if(text==accepted){Error=null;return false;}var next=Parse(text);Current=next;accepted=text;Error=null;return true;}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){Error=e.Message;return false;}
    }
    public MapSettings Change(Func<MapSettings,MapSettings> edit)
    {
        if(Error is not null)throw new InvalidDataException("Externe Mapkonfiguration zuerst korrigieren; letzte gültige Regeln bleiben aktiv.");
        var next=Parse(JsonSerializer.Serialize(edit(Parse(JsonSerializer.Serialize(Current)))));
        if(Read()!=accepted)throw new IOException("Mapkonfiguration wurde inzwischen geändert; erneut öffnen.");
        if(Equivalent(Current,next))return Current;
        var root=Root(accepted);var section=Section(root);
        foreach(var field in JsonSerializer.SerializeToNode(next)!.AsObject())section[field.Key]=field.Value?.DeepClone();
        string text=root.ToJsonString(new(){WriteIndented=true});
        if(Read()!=accepted)throw new IOException("Mapkonfiguration wurde inzwischen geändert; erneut öffnen.");
        AtomicJson.Write(path,root);Current=next;accepted=text;Error=null;return next;
    }
}
