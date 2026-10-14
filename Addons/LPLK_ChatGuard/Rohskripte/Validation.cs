namespace LPLK_ChatGuard;

public static partial class ChatGuardPolicy
{
    public static ViolationState RegisterViolation(ViolationState? previous,DateTimeOffset now,int windowMinutes)
        =>previous is null||now-previous.FirstAt>TimeSpan.FromMinutes(Math.Clamp(windowMinutes,1,1440))
            ?new(){FirstAt=now,LastAt=now,Count=1}
            :new(){FirstAt=previous.FirstAt,LastAt=now,Count=previous.Count+1};
    public static string ViolationAction(int count,AntiAdConfig config)
        =>count>=Math.Max(3,config.KickAtViolation)?"kick":count>=Math.Max(2,config.GagAtViolation)?"gag":"warn";
    public static bool CanApplyDeferred(bool active,bool enabled,bool sameConnection,bool elevated)=>active&&enabled&&sameConnection&&!elevated;
    public static void Validate(AntiAdConfig c)
    {
        if(c.RepeatWindowMinutes is <1 or >1440||c.GagMinutes is <1 or >1440||c.GagAtViolation is <2 or >100||c.KickAtViolation is <3 or >100||c.ElevatedRoleWarningCooldownSeconds is <1 or >300)
            throw new InvalidDataException("Ungültige Zeit- oder Treffergrenze.");
        if(string.IsNullOrWhiteSpace(c.CensoredPlayerName)||c.CensoredPlayerName.Length>24||c.CensoredPlayerName.Any(char.IsControl)||string.IsNullOrWhiteSpace(c.KickReason)||c.KickReason.Length>200||c.KickReason.Any(char.IsControl))
            throw new InvalidDataException("Ersatzname (1–24) oder Kicknachricht (1–200 Zeichen) ungültig.");
        foreach(var list in new[]{c.BlockedDomains,c.AllowedDomains,c.MarketingKeywords})
            if(list is null||list.Count>256||list.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>200||x.Any(char.IsControl)))throw new InvalidDataException("Filterliste ungültig (maximal 256 Einträge zu je 200 Zeichen).");
    }
}
