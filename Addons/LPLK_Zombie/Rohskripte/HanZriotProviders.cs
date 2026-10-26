using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
// LPLK: parse and validate the complete wave/zombie pair before publishing either half.
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LPLK_Zombie;

public interface IStageConfigProvider { HanZriotDayConfig GetConfig(); }
public interface IZombieConfigProvider { ZombieDataConfig GetConfig(); }
public sealed class ZombieProfile : IStageConfigProvider, IZombieConfigProvider
{
    private HanZriotDayConfig days=new();
    private ZombieDataConfig zombies=new();
    public string Map {get;private set;}="";
    public bool High {get;private set;}
    HanZriotDayConfig IStageConfigProvider.GetConfig()=>days;
    ZombieDataConfig IZombieConfigProvider.GetConfig()=>zombies;
    public HanZriotDayConfig Days=>days;
    public ZombieDataConfig Zombies=>zombies;
    private static readonly JsonSerializerOptions Json=new(){ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true,PropertyNameCaseInsensitive=true};
    public static T ReadSection<T>(string text,string key)
    {
        using var doc=JsonDocument.Parse(text,new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        if(!doc.RootElement.TryGetProperty(key,out var section))throw new InvalidDataException("Missing section: "+key);
        return section.Deserialize<T>(Json)??throw new InvalidDataException("Empty section: "+key);
    }
    public static string MapName(string value)
    {
        if(Regex.IsMatch(value,@"^workshop/[0-9]+/[A-Za-z0-9_-]+$"))value=value.Split('/')[^1];
        if(!Regex.IsMatch(value,@"^[A-Za-z0-9_-]{1,128}$"))throw new InvalidDataException("Unsafe map name");
        return value;
    }
    public static bool SafeMapTarget(string value)=>Regex.IsMatch(value,@"^[A-Za-z][A-Za-z0-9_-]{0,127}$") || (Regex.IsMatch(value,@"^[0-9]{1,20}$")&&ulong.TryParse(value,out var id)&&id>0);
    public void Reload(string directory,string map,bool high)
    {
        map=MapName(map);string folder=Path.Combine(directory,map);
        if(!Directory.Exists(folder))folder=directory;
        for(var parent=new DirectoryInfo(Path.GetFullPath(folder));parent is not null;parent=parent.Parent)
            if(parent.LinkTarget is not null)throw new IOException("Linked profile path");
        string suffix=high?"_hight":"";
        string Read(string name){string path=Path.Combine(folder,name+suffix+".jsonc");var file=new FileInfo(path);if(file.LinkTarget is not null||file.Length>2*1024*1024)throw new IOException("Unsafe profile file");return File.ReadAllText(path);}
        var candidateDays=ReadSection<HanZriotDayConfig>(Read("HanZriotDayConfig"),"ZriotDayCFG");
        var candidateZombies=ReadSection<ZombieDataConfig>(Read("ZombieDataConfig"),"ZriotZombieCFG");
        Validate(candidateDays,candidateZombies);
        days=candidateDays;zombies=candidateZombies;Map=map;High=high;
    }
    public static void Validate(HanZriotDayConfig d,ZombieDataConfig z)
    {
        bool Range(double n,double min,double max)=>double.IsFinite(n)&&n>=min&&n<=max;
        if(d.Days is null||d.Days.Count is <1 or >500||z.ZombieList is null||z.ZombieList.Count is <1 or >500)throw new InvalidDataException("Empty or excessive mode profile");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var zombie in z.ZombieList)
        {
            if(zombie is null||string.IsNullOrWhiteSpace(zombie.Name)||!names.Add(zombie.Name)||string.IsNullOrWhiteSpace(zombie.Model)||!Regex.IsMatch(zombie.Model,@"^[A-Za-z0-9_/-]+\.vmdl$")||!Range(zombie.Health,1,10000000)||!Range(zombie.Speed,0,20)||!Range(zombie.Damage,0,100000)||!Range(zombie.Gravity,0,20)||!Range(zombie.HealthRevive,0,1)||!Range(zombie.HealthReviveHp,0,100000)||!Range(zombie.HealthReviveSec,zombie.HealthRevive>0?0.1:0,3600)||!Range(zombie.Percent,0,100)||!Range(zombie.ZombieScale,0,20))throw new InvalidDataException("Invalid zombie definition");
        }
        foreach(var day in d.Days)
        {
            if(day is null||string.IsNullOrWhiteSpace(day.DayName)||!Range(day.Count,1,100000)||!Range(day.HealthBoost,0,10000000)||!Range(day.BeforeZombie,0,100000)||day.ZombieOverride is null)throw new InvalidDataException("Invalid wave definition");
            foreach(var name in day.ZombieOverride.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))if(!names.Contains(name))throw new InvalidDataException("Wave references missing zombie: "+name);
        }
    }
    public static HanZriotCFG ReadMain(string text)
    {
        var c=ReadSection<HanZriotCFG>(text,"ZriotCFG");
        bool Range(double n,double min,double max)=>double.IsFinite(n)&&n>=min&&n<=max;
        if(!Range(c.useworkshopmap,0,3)||!Range(c.DeathMoney,0,16000)||!Range(c.SpawnProtectCount,0,300)||!Range(c.FreezeZombie,0,300)||!Range(c.HumanRebornSec,0,300)||!Range(c.AmbSoundLoopTime,1,3600)||c.AdminCommandPermission is null||c.AdminCommandPermission.Any(char.IsWhiteSpace))throw new InvalidDataException("Invalid zombie settings");
        foreach(var property in typeof(HanZriotCFG).GetProperties().Where(p=>p.PropertyType==typeof(string)))if(property.GetValue(c) is not string value||value.Length>8192||value.Any(char.IsControl))throw new InvalidDataException("Invalid string setting");
        return c;
    }
}
public sealed class ZombieOptions(HanZriotCFG value):IOptionsMonitor<HanZriotCFG>
{
    public HanZriotCFG CurrentValue {get;private set;}=value;
    public HanZriotCFG Get(string? name)=>CurrentValue;
    public IDisposable? OnChange(Action<HanZriotCFG,string?> listener)=>null;
    public void Replace(HanZriotCFG candidate)=>CurrentValue=candidate;
}
