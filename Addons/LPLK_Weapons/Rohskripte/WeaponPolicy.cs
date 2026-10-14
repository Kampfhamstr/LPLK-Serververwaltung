using LPLK.Common;
using System.Text.Json.Serialization;

namespace LPLK_Weapons;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WeaponRule
{
    public bool BlockUsing {get;init;}
    public bool AllowPickupWhenBlocked {get;init;}
    public string BypassPermission {get;init;}="";
    public Dictionary<int,int> TeamQuota {get;init;}=[];
    public Dictionary<string,Dictionary<int,int>> MapQuota {get;init;}=[];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WeaponSettings
{
    public bool Enabled {get;init;}=true;
    public bool RestrictionEnabled {get;init;}=false;
    public bool RestrictUnknownWeapons {get;init;}=false;
    public bool ShowRestrictionAlert {get;init;}=true;
    public string[] AllowedWeapons {get;init;}=WeaponCatalog.Preset("AllWeapons");
    public string ActivePreset {get;init;}="AllWeapons";
    public bool MenuEnabled {get;init;}=false;
    public string MenuPermission {get;init;}="";
    public string[] MenuBlacklist {get;init;}=[];
    public Dictionary<string,WeaponRule> Rules {get;init;}=[];
    public static void Validate(WeaponSettings s)
    {
        WeaponCatalog.ValidateList(s.AllowedWeapons);WeaponCatalog.ValidateList(s.MenuBlacklist);
        if(s.ActivePreset is not ("AllWeapons" or "PistolsOnly" or "DeagleOnly" or "RiflesOnly" or "SnipersOnly" or "AwpOnly" or "NoSnipers" or "Custom"))throw new InvalidDataException("Ungültiges Preset");
        if(s.MenuPermission is null||s.MenuPermission.Length>128||s.Rules is null||s.Rules.Count>128)throw new InvalidDataException("Ungültige Waffenregeln");
        foreach(var (name,r) in s.Rules)
        {
            if(!WeaponCatalog.ValidId(name)||r is null||r.BypassPermission is null||r.BypassPermission.Length>128||r.MapQuota is null||r.MapQuota.Count>128)throw new InvalidDataException("Ungültige Sonderregel");
            CheckQuota(r.TeamQuota);
            if(WeaponCatalog.Find(name) is null&&(r.TeamQuota.Count>0||r.MapQuota.Count>0))throw new InvalidDataException("Teamquoten sind nur für Schusswaffen verfügbar");
            foreach(var (map,q) in r.MapQuota){if(string.IsNullOrWhiteSpace(map)||map.Length>128||map!=map.ToLowerInvariant())throw new InvalidDataException("Ungültiger Mapname");CheckQuota(q);}
        }
    }
    private static void CheckQuota(Dictionary<int,int>? q)
    {
        if(q is null||q.Count>65||q.Any(x=>x.Key is <0 or >64||x.Value is <0 or >256))throw new InvalidDataException("Ungültige Teamquote");
    }
}
public static class WeaponPolicy
{
    public static bool Restricted(WeaponSettings s,string weapon)=>s.Enabled&&s.RestrictionEnabled&&
        (WeaponCatalog.Find(weapon) is not null||s.RestrictUnknownWeapons)&&!s.AllowedWeapons.Contains(weapon,StringComparer.Ordinal);
    public static int? Quota(WeaponRule r,string map,int players)
    {
        var quota=r.MapQuota.TryGetValue(map.ToLowerInvariant(),out var q)&&q.Count>0?q:r.TeamQuota;
        if(quota.Count==0)return null;
        // Choose the greatest threshold, not the greatest allowance (limits can decrease).
        return quota.Where(x=>x.Key<=players).OrderByDescending(x=>x.Key).Select(x=>(int?)x.Value).FirstOrDefault()??0;
    }
    public static string? Denial(WeaponSettings s,string weapon,bool pickup,bool bypass,string map,int teamPlayers,int owned)
    {
        if(!s.Enabled)return null;
        if(Restricted(s,weapon))return "Waffe im aktuellen Modus gesperrt";
        if(!s.Rules.TryGetValue(weapon,out var r))return null;
        if(r.BlockUsing&&!(pickup&&r.AllowPickupWhenBlocked)&&!bypass)return "Waffe gesperrt";
        return Quota(r,map,teamPlayers) is {} limit&&owned>=limit?"Teamquote erreicht":null;
    }
    public static bool MenuAllows(WeaponSettings s,string id)=>s.Enabled&&s.MenuEnabled&&WeaponCatalog.Find(id) is not null&&!s.MenuBlacklist.Contains(id,StringComparer.Ordinal)&&!Restricted(s,id);
}
