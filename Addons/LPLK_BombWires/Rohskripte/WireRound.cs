using System.Text.Json.Serialization;
namespace LPLK_BombWires;

public sealed class WireRound
{
    public int Generation { get; private set; }
    public ulong Planter { get; private set; }
    public bool Planting { get; private set; }
    public bool Planted { get; private set; }
    public bool Resolved { get; private set; }
    public int? Color { get; private set; }
    public void Reset() { Generation++; Planter = 0; Planting = Planted = Resolved = false; Color = null; }
    public void Begin(ulong planter) { Reset(); Planter = planter; Planting = true; }
    public bool Choose(ulong planter, int generation, int color)
    {
        if (!Planting || Planter != planter || Generation != generation || color is < 0 or > 3) return false;
        Color = color; return true;
    }
    public bool Plant(int fallback)
    {
        if(!Planting||Planted||Resolved||fallback is <0 or >3)return false;
        Color ??= fallback; Planting = false; Planted = true;return true;
    }
    public bool? Cut(int generation, int color)
    {
        if (!Planted || Resolved || Generation != generation || color is < 0 or > 3) return null;
        Resolved = true; return Color == color;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WireSettings
{
    public bool Enabled { get; init; } = true;
    public int MenuSeconds { get; init; } = 5;
    public bool RandomizeOrder { get; init; }
    public static void Validate(WireSettings s) { if (s.MenuSeconds is < 1 or > 10) throw new InvalidDataException("Menüzeit muss 1–10 Sekunden sein."); }
}
