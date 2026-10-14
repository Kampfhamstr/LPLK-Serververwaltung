namespace LPLK_Whitelist;

public sealed record AccessSettings
{
    public bool Enabled { get; init; } = true;
    public int Mode { get; init; } = 1;
    public bool AdminBypass { get; init; }
    public string PermissionForCommands { get; init; } = "admin.ban";
    public string KickMessage { get; init; } = "Dieser LPLK-Testserver ist nur für freigeschaltete Tester zugänglich.";
    public static void Validate(AccessSettings s)
    {
        if(s.Mode is not (1 or 2) || string.IsNullOrWhiteSpace(s.KickMessage) || s.KickMessage.Length>200 || s.KickMessage.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(s.PermissionForCommands)||s.PermissionForCommands.Length>100||s.PermissionForCommands.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '.' and not '_' and not '-'))
            throw new InvalidDataException("Ungültige Whitelist-Einstellungen.");
    }
}

public static class AccessPolicy
{
    public static bool ValidId(string value) => value.Length==17 && value.All(char.IsAsciiDigit) &&
        ulong.TryParse(value,out ulong id) && id>=76561197960265728UL && id<=76561202255233023UL;
    public static HashSet<string> Parse(string text)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(string raw in text.Split('\n'))
        {
            string value=raw.Trim(); if(value.Length==0 || value.StartsWith('#') || value.StartsWith("//"))continue;
            if(!ValidId(value))throw new InvalidDataException("Whitelist enthält eine ungültige SteamID64.");
            ids.Add(value);
        }
        if(ids.Count>100000)throw new InvalidDataException("Whitelist ist zu groß.");return ids;
    }
    public static bool Allows(AccessSettings settings,bool listed,bool admin) => !settings.Enabled || (settings.AdminBypass && admin) ||
        (settings.Mode==1?listed:!listed);
}
