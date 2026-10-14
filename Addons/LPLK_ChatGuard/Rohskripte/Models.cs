namespace LPLK_ChatGuard;

public sealed class GlobalProtectionSnapshot
{
    public int Version { get; set; } = 2;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<string> BlockedSteamIds { get; set; } = [];
    public List<string> Domains { get; set; } = [];
    public List<string> AllowedDomains { get; set; } = [];
    public List<string> BlockedPhrases { get; set; } = [];
    public List<string> MarketingKeywords { get; set; } = [];
}

public sealed class GlobalAdvertisingEvent
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string SteamId64 { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string MatchValue { get; set; } = "";
    public string Action { get; set; } = "";
    public string MessageExcerpt { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class AntiAdConfig
{
    public bool ExportBridgeEvents { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public bool CheckPlayerNames { get; set; } = true;
    public bool MaskAdvertisingPlayerNames { get; set; } = true;
    public bool CensorAdvertisingSystemMessages { get; set; } = true;
    public bool SuppressAdvertisingDisconnectMessages { get; set; } = true;
    public bool SuppressAdvertisingServerMessages { get; set; } = true;
    public bool HardActionsOnlyForPlayerRole { get; set; } = true;
    public int ElevatedRoleWarningCooldownSeconds { get; set; } = 15;
    public string CensoredPlayerName { get; set; } = "Werbe-Bot";
    public bool BroadcastSuccess { get; set; } = true;
    public bool BroadcastOffenderName { get; set; } = false;
    public bool LogBlockedText { get; set; } = true;
    public int RepeatWindowMinutes { get; set; } = 10;
    public int GagAtViolation { get; set; } = 2;
    public int GagMinutes { get; set; } = 30;
    public int KickAtViolation { get; set; } = 3;
    public string KickReason { get; set; } = "Werbung / Spam ist auf diesem Server nicht erlaubt.";
    public List<string> BlockedDomains { get; set; } = [];
    public List<string> AllowedDomains { get; set; } = [];
    public List<string> MarketingKeywords { get; set; } = [];

    public static AntiAdConfig CreateDefault() => new()
    {
        BlockedDomains = [
            "cs2commends.com",
            "jaegerservice.xyz",
            "sinlyxe.cc",
            "clash.gg",
            "steamlvls.com",
            "steamlevels.com"
        ],
        AllowedDomains = ["community.example.invalid"],
        MarketingKeywords = [
            "free commends",
            "free commend",
            "free cs2",
            "cs2 commends",
            "commends",
            "commend boost",
            "cheap skins",
            "free skins",
            "free cases",
            "deposit bonus",
            "bonus code",
            "promo code",
            "cheap steam levels",
            "steam level boost",
            "steam level service",
            "discord.gg"
        ]
    };
}

public sealed class ViolationState
{
    public int Count { get; set; }
    public DateTimeOffset FirstAt { get; set; }
    public DateTimeOffset LastAt { get; set; }
}
