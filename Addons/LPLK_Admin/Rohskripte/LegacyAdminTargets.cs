using System.Globalization;
using LPLK.Common;

namespace LPLK_Admin;

public static class LegacyAdminTargets
{
    public static bool TrySteamId(string token,out string id)
    {
        id="";
        if(AdminActionPolicy.SteamId(token)){id=token;return true;}
        const ulong first=76561197960265728;
        ulong account;
        if(token.StartsWith("STEAM_",StringComparison.OrdinalIgnoreCase))
        {
            var parts=token[6..].Split(':');
            if(parts.Length!=3||parts[0] is not ("0" or "1")||parts[1] is not ("0" or "1")
                ||!ulong.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out ulong number)||number>uint.MaxValue/2)return false;
            account=number*2+(parts[1]=="1"?1UL:0);
        }
        else if(token.StartsWith("[U:1:",StringComparison.OrdinalIgnoreCase)&&token.EndsWith(']'))
        {if(!ulong.TryParse(token[5..^1],NumberStyles.None,CultureInfo.InvariantCulture,out account)||account>uint.MaxValue)return false;}
        else return false;
        id=(first+account).ToString(CultureInfo.InvariantCulture);return AdminActionPolicy.SteamId(id);
    }
    public static int RestartDelay(string[] args)
    {
        // Preserve LPLK's existing default; accept the legacy optional argument.
        if(args.Length==0)return 5;
        if(args.Length!=1||!int.TryParse(args[0],NumberStyles.None,CultureInfo.InvariantCulture,out int delay)||delay is <0 or >60)
            throw new ArgumentException("Rundenneustart: 0–60 Sekunden.");
        return delay;
    }
}
