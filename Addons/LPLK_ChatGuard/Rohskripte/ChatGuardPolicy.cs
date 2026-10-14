using System.Text;
using System.Text.RegularExpressions;
namespace LPLK_ChatGuard;

public static partial class ChatGuardPolicy
{
    private static readonly string[] BuiltInBlockedDomains =
    [
        "cs2commends.com",
        "jaegerservice.xyz",
        "sinlyxe.cc",
        "clash.gg",
        "steamlvls.com",
        "steamlevels.com"
    ];

    private static readonly string[] BuiltInMarketingKeywords =
    [
        "free cases",
        "deposit bonus",
        "bonus code",
        "promo code",
        "cheap steam levels",
        "steam level boost",
        "steam level service"
    ];

    public static bool Match(string value, bool isPlayerName, AntiAdConfig config, GlobalProtectionSnapshot rules, out string match)
    {
        match = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = Normalize(value);
        string compact = Regex.Replace(normalized, @"\s+", "");

        foreach (string blocked in BuiltInBlockedDomains)
        {
            string domain = NormalizeDomain(blocked);
            if (domain.Length > 0 && compact.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                match = domain;
                return true;
            }
        }

        var globalBlockedDomains=rules.Domains;
        var globalAllowedDomains=rules.AllowedDomains;
        var globalBlockedPhrases=rules.BlockedPhrases;
        var globalMarketingKeywords=rules.MarketingKeywords;

        foreach (string phrase in globalBlockedPhrases.OrderByDescending(item => item.Length))
        {
            if (normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                match = "phrase:" + phrase;
                return true;
            }
        }

        foreach (string blocked in globalBlockedDomains)
        {
            string domain = NormalizeDomain(blocked);
            if (domain.Length > 0 && compact.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                match = domain;
                return true;
            }
        }

        foreach (string blocked in config.BlockedDomains)
        {
            string domain = NormalizeDomain(blocked);
            if (domain.Length > 0 && compact.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                match = domain;
                return true;
            }
        }

        bool hasMarketingKeyword = BuiltInMarketingKeywords
            .Concat(globalMarketingKeywords)
            .Concat(config.MarketingKeywords)
            .Any(keyword =>
        {
            string k = Normalize(keyword);
            return k.Length > 1 && normalized.Contains(k, StringComparison.OrdinalIgnoreCase);
        });

        var allowedDomains = config.AllowedDomains
            .Concat(globalAllowedDomains)
            .Select(NormalizeDomain)
            .Where(domain => domain.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Preserve word boundaries: compacting the whole sentence turned
        // "promo code community.example.invalid" into a different, supposedly unapproved host.
        Match? domainMatch = Regex.Matches(normalized,
                @"(?:https?://)?(?:www\.)?[a-z0-9][a-z0-9-]{2,}\.(?:com|xyz|cc|net|gg|io|ru|org|de|shop|site|online|pro)(?:/[^\s]*)?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .FirstOrDefault(candidate => !allowedDomains.Any(allowed => {
                string host=NormalizeDomain(candidate.Value);
                return host==allowed||host.EndsWith("."+allowed,StringComparison.OrdinalIgnoreCase);
            }));

        bool discordInvite = compact.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("discord.com/invite/", StringComparison.OrdinalIgnoreCase);
        bool discordAllowed = allowedDomains.Contains("discord.gg") || allowedDomains.Contains("discord.com");

        // Namen werden absichtlich konservativ behandelt: eine beliebige Domain allein
        // reicht nicht. Eine Blacklist-Domain reicht; sonst muss Werbungssprache dazukommen.
        // Unbekannte Domains bleiben für Spielernamen nicht pauschal verboten.
        // Die sehr eindeutige Kombination aus Domain + Steam-Level-Angebot ist aber
        // Werbung, auch wenn der Betreiber eine noch unbekannte Domain verwendet.
        bool steamLevelPromotion = isPlayerName && HasSteamLevelPromotionSignature(normalized);
        if (domainMatch is not null && domainMatch.Success && (hasMarketingKeyword || steamLevelPromotion))
        {
            match = domainMatch.Value;
            return true;
        }

        if (discordInvite && !discordAllowed && (hasMarketingKeyword || !isPlayerName))
        {
            match = "discord-invite";
            return true;
        }

        if (isPlayerName && hasMarketingKeyword &&
            (normalized.Contains("free cs2", StringComparison.OrdinalIgnoreCase)
             || normalized.Contains("commends", StringComparison.OrdinalIgnoreCase)
             || normalized.Contains("commend", StringComparison.OrdinalIgnoreCase)))
        {
            match = "name-marketing";
            return true;
        }

        return false;
    }

    private static bool HasSteamLevelPromotionSignature(string normalized)
    {
        bool steam = normalized.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("steamlvls", StringComparison.OrdinalIgnoreCase);
        bool level = normalized.Contains("level", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("lvls", StringComparison.OrdinalIgnoreCase);
        bool offer = normalized.Contains("cheap", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("buy", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("shop", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("boost", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("service", StringComparison.OrdinalIgnoreCase);
        return steam && level && offer;
    }

    public static string Normalize(string value)
    {
        string s = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        s = s.Replace("[.]", ".", StringComparison.Ordinal)
             .Replace("(.)", ".", StringComparison.Ordinal)
             .Replace("{.}", ".", StringComparison.Ordinal)
             .Replace(" dot ", ".", StringComparison.Ordinal)
             .Replace("\u200b", string.Empty, StringComparison.Ordinal)
             .Replace("\u200c", string.Empty, StringComparison.Ordinal)
             .Replace("\u200d", string.Empty, StringComparison.Ordinal);
        s = Regex.Replace(s, @"\s*\.\s*", ".");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    public static string NormalizeDomain(string value)
    {
        string s = Normalize(value);
        s = s.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        int slash = s.IndexOf('/');
        if (slash >= 0) s = s[..slash];
        return Regex.Replace(s, @"\s+", string.Empty);
    }

    public static string ResolveHighestGameRole(IEnumerable<string> groups)
    {
        string role = "spieler";
        int level = GameRoleLevel(role);
        foreach (string group in groups)
        {
            string candidate = NormalizeGameRole(group);
            int candidateLevel = GameRoleLevel(candidate);
            if (candidateLevel > level)
            {
                role = candidate;
                level = candidateLevel;
            }
        }
        return role;
    }

    public static string NormalizeGameRole(string? role)
    {
        string normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "spieler" or "vip" or "member" or "admin" or "founder"
            ? normalized
            : "spieler";
    }

    public static int GameRoleLevel(string? role) => NormalizeGameRole(role) switch
    {
        "founder" => 5,
        "admin" => 4,
        "member" => 3,
        "vip" => 2,
        _ => 1
    };

    public static string FormatGameRole(string? role) => NormalizeGameRole(role) switch
    {
        "founder" => "Founder",
        "admin" => "Admin",
        "member" => "Member",
        "vip" => "VIP",
        _ => "Spieler"
    };

}
