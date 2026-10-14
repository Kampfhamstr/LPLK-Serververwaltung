namespace LPLK.Common;

// Same authorization contract for the menu and the execution backend.
public static class AdminActionPolicy
{
    public static readonly IReadOnlyDictionary<string,string> Permissions = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["kick"]="admin.kick",["ban"]="admin.ban",["unban"]="admin.unban",
        ["mute"]="admin.mute",["unmute"]="admin.mute",["gag"]="admin.chat",["ungag"]="admin.chat",
        ["silence"]="admin.silence",["unsilence"]="admin.silence",["slay"]="admin.slay",
        ["respawn"]="admin.respawn",["team"]="admin.team",["freeze"]="admin.freeze",
        ["noclip"]="admin.cheats",["goto_player"]="admin.goto",["bring_player"]="admin.bring",
        ["health"]="admin.cheats",["armor"]="admin.cheats",["broadcast"]="admin.chat",
        ["private_message"]="admin.chat",["round_restart"]="admin.generic",
        ["server_command"]="admin.rcon",["cvar_set"]="admin.cvar"
    };
    public static bool Allowed(string action, Func<string,bool> has) => Permissions.TryGetValue(action,out var permission)
        && (has(permission) || (action is not ("server_command" or "cvar_set") && has("lplk.admin")));
    public static int Rank(Func<string,bool> has)=>has("admin.root")?100:has("lplk.admin")||has("admin.menu")?10:0;
    public static bool CanTarget(ulong actor,ulong target,int actorRank,int targetRank)=>actor==0||actor==target||targetRank==0||actorRank>targetRank;
    public static bool SteamId(string? value)=>value?.Length==17&&ulong.TryParse(value,out var id)&&id>=76561197960265728&&id<=76561202255233023;
    public static string Text(string input,int limit=200)
    {
        var value=new string(input.Where(c=>!char.IsControl(c)).ToArray()).Trim();
        if(value.Length==0||value.Length>limit)throw new ArgumentException($"Text muss 1–{limit} Zeichen enthalten.");
        return value;
    }
    public static int Minutes(string value)=>int.TryParse(value,out var minutes)&&minutes>=0&&minutes<=525600?minutes:throw new ArgumentException("Dauer: 0 (dauerhaft) bis 525600 Minuten.");
}
