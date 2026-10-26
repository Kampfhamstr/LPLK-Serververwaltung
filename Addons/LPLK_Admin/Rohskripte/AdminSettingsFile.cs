using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK.Common;

namespace LPLK_Admin;

public sealed class AdminSettingsFile
{
    public const int MaxBytes=65536;
    private readonly string path;
    private static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public AdminSettings Current {get;private set;}
    public AdminSettingsFile(string path)
    {
        this.path=path;
        if(!File.Exists(path))AtomicJson.Write(path,new AdminSettings());
        Current=Read().Value;
    }
    public static void Validate(AdminSettings value)
    {
        AdminCommandNames.Wanted(value.Commands);
        if(value.LegacyGameMaps is null||value.LegacyWorkshopMaps is null||value.LegacyGameMaps.Count>512||value.LegacyWorkshopMaps.Count>512||value.LegacyGameMaps.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.LegacyGameMaps.Count||value.LegacyGameMaps.Any(p=>!System.Text.RegularExpressions.Regex.IsMatch(p.Key,@"^[a-z0-9_]{1,64}$")||string.IsNullOrWhiteSpace(p.Value)||p.Value.Length>100)||value.LegacyWorkshopMaps.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Key.Length>100||p.Value==0))throw new InvalidDataException("Ungültige übernommene Mapliste.");
        if(value.Durations is null||value.Reasons is null||value.Durations.Length is <1 or >20||value.Durations.Any(n=>n<0||n>525600)||value.Reasons.Length is <1 or >30||value.Reasons.Any(r=>string.IsNullOrWhiteSpace(r)||r.Length>150))throw new InvalidDataException("Ungültige Admin-Gründe/Dauern.");
    }
    public static AdminSettings Copy(AdminSettings value)=>value with{Durations=[..value.Durations],Reasons=[..value.Reasons],Commands=value.Commands.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),LegacyGameMaps=new(value.LegacyGameMaps),LegacyWorkshopMaps=new(value.LegacyWorkshopMaps)};
    public static bool Same(AdminSettings a,AdminSettings b)=>a.LegacyPlayerMenus==b.LegacyPlayerMenus&&a.LegacyGameMaps.OrderBy(x=>x.Key).SequenceEqual(b.LegacyGameMaps.OrderBy(x=>x.Key))&&a.LegacyWorkshopMaps.OrderBy(x=>x.Key).SequenceEqual(b.LegacyWorkshopMaps.OrderBy(x=>x.Key))&&a.Durations.SequenceEqual(b.Durations)&&a.Reasons.SequenceEqual(b.Reasons,StringComparer.Ordinal)&&AdminCommandNames.Same(a.Commands,b.Commands);
    private string Text()
    {
        var f=new FileInfo(path);
        if(!f.Exists||f.Length>MaxBytes||f.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidDataException("Admin-Konfiguration fehlt, ist zu groß oder verknüpft.");
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        byte[] bytes=reader.ReadBytes(MaxBytes+1);if(bytes.Length>MaxBytes)throw new InvalidDataException("Admin-Konfiguration zu groß.");
        return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
    }
    private (string Text,JsonObject Root,AdminSettings Value) Read()
    {
        string text=Text();var root=JsonNode.Parse(text) as JsonObject??throw new InvalidDataException("Admin-Konfiguration muss ein Objekt sein.");
        if(root.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=root.Count)throw new InvalidDataException("Mehrdeutige Admin-Konfiguration.");
        var commandsKey=root.Select(x=>x.Key).SingleOrDefault(k=>k.Equals("Commands",StringComparison.OrdinalIgnoreCase));
        if(commandsKey is not null)
        {
            var commands=root[commandsKey] as JsonObject??throw new InvalidDataException("Befehle müssen ein Objekt sein.");
            if(commands.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=commands.Count)throw new InvalidDataException("Mehrdeutige Befehlsgruppen.");
        }
        var value=root.Deserialize<AdminSettings>(Json)??throw new InvalidDataException("Leere Admin-Konfiguration.");Validate(value);return(text,root,value);
    }
    public void Reload()=>Current=Read().Value;
    public void Change(Func<AdminSettings,AdminSettings> edit)
    {
        var source=Read();var next=edit(Copy(source.Value));Validate(next);
        foreach(var pair in JsonSerializer.SerializeToNode(next,Json)!.AsObject())
        {string key=source.Root.Select(x=>x.Key).SingleOrDefault(k=>k.Equals(pair.Key,StringComparison.OrdinalIgnoreCase))??pair.Key;source.Root[key]=pair.Value?.DeepClone();}
        if(Encoding.UTF8.GetByteCount(source.Root.ToJsonString(Json))>MaxBytes)throw new InvalidDataException("Admin-Konfiguration zu groß.");
        if(Text()!=source.Text)throw new IOException("Admin-Konfiguration inzwischen geändert; erneut öffnen.");
        AtomicJson.Write(path,source.Root);Current=next;
    }
}
