using System.Globalization;
using LPLK.Common;
using System.Text.Json.Serialization;

namespace LPLK_CombatRules;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DamageRule
{
    public bool OnlyHeadshot {get;init;}
    public string? Damage {get;init;}
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CombatSettings
{
    public bool Enabled {get;init;}=true;
    public bool HeadshotOnly {get;init;}=false;
    public bool FirearmsOnly {get;init;}=true;
    public bool GoldenRule {get;init;}=true;
    public string[] GoldenWeapons {get;init;}=["weapon_awp","weapon_scar20","weapon_g3sg1"];
    public int MinimumLeadRounds {get;init;}=1;
    public bool EnabledInWarmup {get;init;}=false;
    public bool AnnounceAtRoundStart {get;init;}=true;
    public bool ShowBlockedShotAlert {get;init;}=true;
    public Dictionary<string,DamageRule> Rules {get;init;}=[];
    public static void Validate(CombatSettings s)
    {
        WeaponCatalog.ValidateList(s.GoldenWeapons);
        if(s.MinimumLeadRounds is <1 or >1000||s.Rules is null||s.Rules.Count>128)throw new InvalidDataException("Ungültige Kampfregeln");
        if(s.GoldenWeapons.Any(x=>WeaponCatalog.Find(x) is null))throw new InvalidDataException("Goldene Regel benötigt eine Schusswaffe");
        foreach(var (id,r) in s.Rules)if(!WeaponCatalog.ValidId(id)||r is null||!DamageFormula.Valid(r.Damage))throw new InvalidDataException("Ungültige Schadensregel");
    }
}
public static class CombatPolicy
{
    public static bool BlockHeadshot(CombatSettings s,string weapon,bool head)=>s.Enabled&&!head&&
        (s.HeadshotOnly&&(!s.FirearmsOnly||WeaponCatalog.Find(weapon) is not null)||s.Rules.TryGetValue(weapon,out var r)&&r.OnlyHeadshot);
    public static int Leader(int ct,int t,int lead)=>ct<0||t<0||ct==t||Math.Abs((long)ct-t)<lead?0:ct>t?3:2;
    public static bool BlockGolden(CombatSettings s,string weapon,int team,int ct,int t,bool scoped,bool? warmup,bool? ffa)=>
        s.Enabled&&s.GoldenRule&&scoped&&team is 2 or 3&&ffa==false&&(warmup==false||warmup==true&&s.EnabledInWarmup)&&
        s.GoldenWeapons.Contains(weapon,StringComparer.Ordinal)&&Leader(ct,t,s.MinimumLeadRounds)==team;
}
public static class DamageFormula
{
    public static bool Valid(string? expression)=>string.IsNullOrEmpty(expression)||TryParse(expression,out _,out _);
    private static bool TryParse(string text,out char op,out double number)
    {
        op=text.Length>0?text[0]:'?';number=0;
        return text.Length is >=2 and <=24&&"=+-*/".Contains(op)&&double.TryParse(text[1..],NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out number)&&double.IsFinite(number)&&Math.Abs(number)<=100000&&!(op=='/'&&number==0);
    }
    public static float Apply(float damage,string? expression)
    {
        if(!float.IsFinite(damage)||string.IsNullOrEmpty(expression))return damage;
        if(!TryParse(expression,out var op,out var n))throw new InvalidDataException("Ungültige Schadensformel");
        double value=op switch{'='=>n,'+'=>damage+n,'-'=>damage-n,'*'=>damage*n,'/'=>damage/n,_=>damage};
        return (float)Math.Clamp(value,0,100000);
    }
}
