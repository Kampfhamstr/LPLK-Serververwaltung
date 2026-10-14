using System.Text.Json;
using LPLK_Sounds;

public static class SoundAudienceRuleTests
{
    public static void Run(Action<bool, string> check)
    {
        check(new SoundsConfig().DeadPlayersOnlyToDead &&
            JsonSerializer.Deserialize<SoundsConfig>("{}", SoundsConfig.Json)!.DeadPlayersOnlyToDead,
            "Dead-to-dead rule is on for new and existing configs without manual editing");
        check(!JsonSerializer.Deserialize<SoundsConfig>("{\"DeadPlayersOnlyToDead\":false}", SoundsConfig.Json)!.DeadPlayersOnlyToDead,
            "Explicit configuration can disable the dead-to-dead rule");
        check(SoundAudience.IsPlayingAlive(true, 2) && SoundAudience.IsPlayingAlive(true, 3) && !SoundAudience.IsPlayingAlive(false, 2),
            "Only living T/CT players count as living sound recipients");
        check(!SoundAudience.IsPlayingAlive(true, 1) && !SoundAudience.IsPlayingAlive(true, 0),
            "Spectators and unassigned players use the dead audience even if an observer pawn reports alive");
        var dead = new SoundListener(1, 76561198000000001, true, true, false, true, false);
        var alive = dead with { Slot = 2, SteamId = 76561198000000002, Alive = true };
        var mutedDead = dead with { Slot = 3, SteamId = 76561198000000003, Enabled = false };
        var bot = dead with { Slot = 4, SteamId = 76561198000000004, Fake = true };
        var disconnected = dead with { Slot = 5, SteamId = 76561198000000005, Valid = false };
        var audience = new[] { dead, alive, mutedDead, bot, disconnected };
        check(SoundAudience.RestrictToDead(false, true) && !SoundAudience.RestrictToDead(true, true) &&
            !SoundAudience.RestrictToDead(false, false), "Only non-living sources with the setting enabled restrict the audience");
        var deadRecipients = SoundAudience.Select(audience, SoundAudience.RestrictToDead(false, true));
        check(deadRecipients.Count == 1 && deadRecipients.ContainsKey(dead.Slot),
            "Dead source reaches only eligible dead listeners, never living/muted/bot/disconnected players");
        check(SoundAudience.Select(audience, SoundAudience.RestrictToDead(true, true)).Keys.Order().SequenceEqual(new[] { 1, 2 }),
            "Living source still reaches enabled living and dead listeners");
        check(SoundAudience.Select(audience, SoundAudience.RestrictToDead(false, false)).Keys.Order().SequenceEqual(new[] { 1, 2 }),
            "Disabled rule lets dead sources reach both audiences while preserving personal mute");
        check(SoundAudience.Select([alive, mutedDead], true).Count == 0,
            "No eligible dead listeners produces an empty recipient list, never an all-player fallback");
        var active = SoundBroadcast.Start("gong", 0, 12, [new(100, deadRecipients)], _ => 42, (_, _, _) => {}, deadOnly: true);
        check(active.DeadOnly && !SoundAudience.Eligible(dead with { Alive = true }, active.DeadOnly),
            "A dead-only clip stops being eligible for a listener who respawns");
        check(SoundAudience.Eligible(dead, active.DeadOnly), "Other dead listeners remain eligible for the same clip");
        check(SoundAudience.Eligible(alive, SoundBroadcast.Start("gong", 0, 12, [], _ => 43, (_, _, _) => {}).DeadOnly),
            "Unrestricted existing sound behavior remains available to living listeners");
    }
}
