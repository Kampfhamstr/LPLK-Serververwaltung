using System.Text.Json;

namespace LPLK.Common;

// Called on the game thread. A bad external edit keeps the last valid settings;
// menu edits cannot silently overwrite it. Content comparison also sees equal-size edits.
public sealed class WatchedSettings<T> where T : class,new()
{
    private readonly string path;
    private readonly Action<T> validate;
    private string accepted;
    public T Current {get;private set;}
    public string? Error {get;private set;}
    public WatchedSettings(string path,Action<T> validate)
    {
        this.path=path;this.validate=validate;
        if(!File.Exists(path)){var initial=new T();validate(initial);AtomicJson.Write(path,initial);}
        accepted=Read();Current=Parse(accepted);
    }
    private string Read()
    {
        var info=new FileInfo(path);
        if(!info.Exists||info.Length>65536||info.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Einstellungsdatei fehlt, ist verknüpft oder zu groß.");
        string text=File.ReadAllText(path);if(text.Length>65536)throw new InvalidDataException("Einstellungen zu groß.");return text;
    }
    private T Parse(string text)
    {
        T value=JsonSerializer.Deserialize<T>(text)??throw new InvalidDataException("Leere Einstellungen.");validate(value);return value;
    }
    public bool Reload()
    {
        try
        {
            string text=Read();if(text==accepted){Error=null;return false;}
            var next=Parse(text);Current=next;accepted=text;Error=null;return true;
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {Error=e.GetType().Name;return false;}
    }
    public T Change(Func<T,T> edit)
    {
        Reload();if(Error is not null)throw new InvalidDataException("Externe Konfiguration zuerst korrigieren; letzte gültige Regeln bleiben aktiv.");
        var next=edit(Current);validate(next);
        if(Read()!=accepted)throw new IOException("Einstellungen wurden inzwischen extern geändert; erneut öffnen.");
        AtomicJson.Write(path,next);Current=next;
        accepted=JsonSerializer.Serialize(next,new JsonSerializerOptions{WriteIndented=true});Error=null;return next;
    }
}
