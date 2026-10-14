namespace LPLK.Common;

public sealed record WeaponEntry(string Id,string Name,string Group,bool Secondary=false);
public static class WeaponCatalog
{
    public static readonly WeaponEntry[] All =
    [
        new("weapon_glock","Glock-18","Pistolen",true),new("weapon_hkp2000","P2000","Pistolen",true),new("weapon_usp_silencer","USP-S","Pistolen",true),
        new("weapon_elite","Dual Berettas","Pistolen",true),new("weapon_p250","P250","Pistolen",true),new("weapon_fiveseven","Five-SeveN","Pistolen",true),
        new("weapon_tec9","Tec-9","Pistolen",true),new("weapon_cz75a","CZ75-Auto","Pistolen",true),new("weapon_deagle","Desert Eagle","Pistolen",true),new("weapon_revolver","R8 Revolver","Pistolen",true),
        new("weapon_mac10","MAC-10","MPs"),new("weapon_mp9","MP9","MPs"),new("weapon_mp7","MP7","MPs"),new("weapon_mp5sd","MP5-SD","MPs"),new("weapon_ump45","UMP-45","MPs"),new("weapon_p90","P90","MPs"),new("weapon_bizon","PP-Bizon","MPs"),
        new("weapon_ak47","AK-47","Gewehre"),new("weapon_m4a1","M4A4","Gewehre"),new("weapon_m4a1_silencer","M4A1-S","Gewehre"),new("weapon_galilar","Galil AR","Gewehre"),new("weapon_famas","FAMAS","Gewehre"),new("weapon_aug","AUG","Gewehre"),new("weapon_sg556","SG 553","Gewehre"),
        new("weapon_awp","AWP","Scharfschützen"),new("weapon_ssg08","SSG 08","Scharfschützen"),new("weapon_scar20","SCAR-20","Scharfschützen"),new("weapon_g3sg1","G3SG1","Scharfschützen"),
        new("weapon_nova","Nova","Schrot & MG"),new("weapon_xm1014","XM1014","Schrot & MG"),new("weapon_mag7","MAG-7","Schrot & MG"),new("weapon_sawedoff","Sawed-Off","Schrot & MG"),new("weapon_m249","M249","Schrot & MG"),new("weapon_negev","Negev","Schrot & MG")
    ];
    public static readonly string[] Groups=All.Select(w=>w.Group).Distinct().ToArray();
    public static string Normalize(string name)=>name.Trim().ToLowerInvariant();
    public static WeaponEntry? Find(string name)=>All.FirstOrDefault(w=>w.Id==Normalize(name));
    public static string[] Preset(string name)=>All.Where(w=>name switch
    {
        "AllWeapons"=>true,"PistolsOnly"=>w.Secondary,"DeagleOnly"=>w.Id=="weapon_deagle",
        "RiflesOnly"=>w.Group=="Gewehre","SnipersOnly"=>w.Group=="Scharfschützen","AwpOnly"=>w.Id=="weapon_awp",
        "NoSnipers"=>w.Group!="Scharfschützen",_=>throw new ArgumentException("Unbekanntes Waffen-Preset")
    }).Select(w=>w.Id).ToArray();
    public static bool ValidId(string? id)=>id is {Length:>7 and <=64}&&id.StartsWith("weapon_",StringComparison.Ordinal)&&id.All(c=>char.IsAsciiLetterLower(c)||char.IsAsciiDigit(c)||c=='_');
    public static void ValidateList(string[]? ids)
    {
        if(ids is null||ids.Length>128||ids.Any(x=>!ValidId(x))||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)throw new InvalidDataException("Ungültige Waffenliste");
    }
}
