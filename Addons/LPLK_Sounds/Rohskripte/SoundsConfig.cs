using System.Text.Json;
namespace LPLK_Sounds;
public sealed class SoundsConfig
{
    public bool Enabled{get;set;}=true;
    public bool AutoCatalogFromAddon{get;set;}=true;
    public bool RequireAddonsManager{get;set;}=true;
    public string[] WorkshopIds{get;set;}=["3797047889"];
    public double BroadcastCooldownSeconds{get;set;}=5;
    public bool DeadPlayersOnlyToDead{get;set;}=true;
    public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true,WriteIndented=true};
    public void Validate()
    {
        if(!double.IsFinite(BroadcastCooldownSeconds)||BroadcastCooldownSeconds is <1 or >60)throw new InvalidDataException("Soundpause: 1–60 Sekunden.");
        if(WorkshopIds is null||WorkshopIds.Length>16||WorkshopIds.Any(id=>!ulong.TryParse(id,out var n)||n==0||n.ToString()!=id)||WorkshopIds.Distinct().Count()!=WorkshopIds.Length)throw new InvalidDataException("Ungültige Workshop-IDs.");
    }
}
public static class PlaybackPolicy
{
    public static bool Expired(long started,long now,double seconds)=>now-started>=Math.Ceiling(seconds*1000);
    public static string[] MissingAddons(IEnumerable<string> requested,IEnumerable<string> configured)=>requested.Except(configured,StringComparer.Ordinal).ToArray();
}
