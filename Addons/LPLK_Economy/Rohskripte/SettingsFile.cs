using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK.Common;

namespace LPLK_Economy;

// Game-thread settings edits; the ledger gate joins validation, persistence and activation.
public sealed class EconomySettingsFile
{
    private readonly string path;
    private string accepted;
    public EconomySettings Current {get;private set;}
    public string? Error {get;private set;}
    public EconomySettingsFile(string path)
    {
        this.path=path;
        if(!File.Exists(path)&&!Directory.Exists(path))AtomicJson.Write(path,new EconomySettings());
        accepted=Read();Current=Parse(accepted);
        // Keep the established upgrade behavior: add only absent fields, never replace existing values.
        var root=JsonNode.Parse(accepted)!.AsObject();var defaults=JsonSerializer.SerializeToNode(Current)!.AsObject();
        // These two established implicit defaults were never part of startup migration.
        defaults.Remove("BankEnabled");defaults.Remove("Betting");
        if(AddMissing(root,defaults))
        {
            if(Read()!=accepted)throw new IOException("Einstellungen während des Ladens geändert.");
            AtomicJson.Write(path,root);accepted=Read();Current=Parse(accepted);
        }
    }
    private string Read()
    {
        var info=new FileInfo(path);
        if(!info.Exists||info.Length>262144||info.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Einstellungsdatei fehlt, ist verknüpft oder zu groß.");
        string text=File.ReadAllText(path);if(text.Length>262144)throw new InvalidDataException("Einstellungen zu groß.");return text;
    }
    private static EconomySettings Parse(string text)
    {
        try
        {
            var value=JsonSerializer.Deserialize<EconomySettings>(text)??throw new InvalidDataException("Leere Einstellungen.");
            EconomySettings.Validate(value);return value;
        }
        catch(Exception e)when(e is JsonException or NullReferenceException or ArgumentException)
        {throw new InvalidDataException("Ungültige Bankeinstellungen.",e);}
    }
    public bool Reload(Action<EconomySettings> activate)
    {
        try
        {
            string text=Read();if(text==accepted){Error=null;return false;}
            var next=Parse(text);
            if(JsonSerializer.Serialize(next)!=JsonSerializer.Serialize(Current))activate(next);
            Current=next;accepted=text;Error=null;return true;
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or BankRejected)
        {Error=e.Message;return false;}
    }
    public EconomySettings Change(Func<EconomySettings,EconomySettings> edit,Action<EconomySettings,Action> commit)
    {
        if(Error is not null)throw new InvalidDataException("Externe Konfiguration zuerst korrigieren.");
        // Arrays and nested records are not shared with the active settings during editing.
        var next=Parse(JsonSerializer.Serialize(edit(Parse(JsonSerializer.Serialize(Current)))));
        var root=JsonNode.Parse(accepted)!.AsObject();Merge(root,JsonSerializer.SerializeToNode(next)!.AsObject());
        string text=root.ToJsonString(new(){WriteIndented=true});
        next=Parse(text);
        commit(next,()=>{if(Read()!=accepted)throw new IOException("Einstellungen wurden inzwischen geändert; erneut öffnen.");AtomicJson.Write(path,root);});
        Current=next;accepted=text;Error=null;return next;
    }
    private static void Merge(JsonObject target,JsonObject values)
    {
        foreach(var pair in values)
        {
            if(target[pair.Key] is JsonObject old&&pair.Value is JsonObject child)Merge(old,child);
            else target[pair.Key]=pair.Value?.DeepClone();
        }
    }
    private static bool AddMissing(JsonObject target,JsonObject defaults)
    {
        bool changed=false;
        foreach(var pair in defaults)
        {
            if(!target.ContainsKey(pair.Key)){target[pair.Key]=pair.Value?.DeepClone();changed=true;}
            else if(target[pair.Key] is JsonObject child&&pair.Value is JsonObject values)changed|=AddMissing(child,values);
        }
        return changed;
    }
}
