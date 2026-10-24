using System.Text.Json;
using System.Text.Json.Serialization;
namespace LPLK_ChatGuard;

// The original snake_case format and all three 256-entry lists remain supported.
// Clone mutable lists before editing, so a rejected edit cannot alter active rules.
public sealed class SettingsStore
{
    public const int MaxBytes=2*1024*1024;
    private readonly string path;
    private readonly JsonSerializerOptions json;
    private string accepted;
    public AntiAdConfig Current {get;private set;}
    public string? Error {get;private set;}
    public SettingsStore(string path,JsonSerializerOptions options)
    {
        this.path=path;json=new(options){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
        accepted=Read();Current=Parse(accepted);
    }
    private string Read()
    {
        var info=new FileInfo(path);
        if(!info.Exists||info.Length>MaxBytes||info.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Konfiguration fehlt, ist verknüpft oder zu groß.");
        // Bounded read also handles a file growing after the initial check.
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var bytes=new MemoryStream();byte[] buffer=new byte[8192];int count;
        while((count=file.Read(buffer))>0){if(bytes.Length+count>MaxBytes)throw new InvalidDataException("Konfiguration zu groß.");bytes.Write(buffer,0,count);}
        using var reader=new StreamReader(new MemoryStream(bytes.ToArray()),new System.Text.UTF8Encoding(false,true),true);
        return reader.ReadToEnd();
    }
    private AntiAdConfig Parse(string text)
    {var value=JsonSerializer.Deserialize<AntiAdConfig>(text,json)??throw new InvalidDataException("Leere Konfiguration.");ChatGuardPolicy.Validate(value);return value;}
    public bool Reload()
    {
        try{string text=Read();if(text==accepted){Error=null;return false;}var next=Parse(text);Current=next;accepted=text;Error=null;return true;}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {Error=e.GetType().Name;return false;}
    }
    public AntiAdConfig Change(Action<AntiAdConfig> edit)
    {
        Reload();if(Error is not null)throw new InvalidDataException("Externe Konfiguration zuerst korrigieren.");
        var next=Parse(JsonSerializer.Serialize(Current,json));edit(next);ChatGuardPolicy.Validate(next);
        string serialized=JsonSerializer.Serialize(next,json);
        if(System.Text.Encoding.UTF8.GetByteCount(serialized)>MaxBytes)throw new InvalidDataException("Konfiguration zu groß.");
        if(Read()!=accepted)throw new IOException("Konfiguration inzwischen extern geändert.");
        string temp=path+".tmp";File.WriteAllText(temp,serialized);File.Move(temp,path,true);
        Current=next;accepted=serialized;Error=null;return next;
    }
}
