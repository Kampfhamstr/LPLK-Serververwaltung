namespace LPLK_Admin;

// Reserve space for previous/next/back even on middle pages. At most five rows.
public sealed record MenuPage<T>(T[] Items, int Index, int Count, int Offset);
public static class MenuLayout
{
    public static MenuPage<T> Page<T>(IEnumerable<T> entries, int requested, bool hasBack = true)
    {
        var items = entries.ToArray();
        int capacity = hasBack ? 4 : 5;
        int size = items.Length <= capacity ? capacity : hasBack ? 2 : 3;
        int count = Math.Max(1, (items.Length + size - 1) / size);
        int index = Math.Clamp(requested, 0, count - 1);
        return new(items.Skip(index * size).Take(size).ToArray(), index, count, index * size);
    }

    public static string ModuleGroup(string id) => id switch
    {
        "LPLK_Demos" or "LPLK_Statistics" => "server",
        "LPLK_Whitelist" or "LPLK_ChatGuard" or "LPLK_Teams" => "players",
        "LPLK_Parachute" or "LPLK_AntiCamp" or "LPLK_ThirdPerson" or "LPLK_BombWires" or "LPLK_AntiTeamFlash" or "LPLK_Weapons" or "LPLK_CombatRules" => "rules",
        "LPLK_Fluch" or "LPLK_NadeEffects" => "effects",
        "LPLK_Maps" => "maps",
        "LPLK_Sounds" => "sounds",
        "LPLK_Economy" or "LPLK_Betting" or "LPLK_Lottery" or "LPLK_BankRobbery" or "LPLK_Titles" => "economy",
        _ => "other"
    };

    // Escape markup only; Unicode letters must remain directly readable in CS2.
    public static string Label(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
