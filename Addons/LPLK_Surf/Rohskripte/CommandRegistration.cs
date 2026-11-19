namespace LPLK_Surf;

public static class SurfCommandRegistration
{
    private static readonly HashSet<string> EngineNames = new(StringComparer.OrdinalIgnoreCase)
    { "stats", "help", "addons", "say", "say_team", "kick", "noclip", "rcon", "map", "restart", "quit", "exit", "status", "exec", "changelevel", "host_workshop_map" };

    // Register the conventional Swiftly/chat name first. Bare console aliases
    // are optional and must never replace engine or foreign plugin commands.
    public static void Register(string name, Func<string, bool> occupied, Action<string> register, Action<string> skipped)
    {
        foreach (string candidate in new[] { "sw_" + name, name })
        {
            if (EngineNames.Contains(candidate) || occupied(candidate)) { skipped(candidate); continue; }
            register(candidate);
        }
    }
}
