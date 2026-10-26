using System.Text.Json.Serialization;
namespace LPLK_Parachute;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParachuteSettings
{
    public bool Enabled { get; init; } = true;
    public float FallSpeed { get; init; } = 85;
    public bool Linear { get; init; } = true;
    public float BrakePerTick { get; init; } = 15;
    public string UsePermission { get; init; } = "";
    public bool DisableWhenCarryingHostage { get; init; }

    public static void Validate(ParachuteSettings value)
    {
        if (!float.IsFinite(value.FallSpeed) || value.FallSpeed is < 20 or > 500 ||
            !float.IsFinite(value.BrakePerTick) || value.BrakePerTick is < 0 or > 100 ||
            value.UsePermission is null || value.UsePermission.Length > 100 || value.UsePermission.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Fallschirm: ungültige Geschwindigkeit oder Berechtigung.");
    }
}

public static class FallPolicy
{
    public static float Brake(float currentZ, ParachuteSettings settings)
    {
        // Preserve v5's gradual braking and reduced gravity, but stop the brake
        // at the target. Overshooting it made the next tick snap back repeatedly
        // (especially with Atlas' fall speed 30 / brake step 30).
        if (!float.IsFinite(currentZ) || currentZ >= 0) return currentZ;
        float terminal = -settings.FallSpeed;
        if (settings.BrakePerTick == 0) return Math.Max(currentZ, terminal);
        if (settings.Linear)
            return currentZ >= terminal ? currentZ : Math.Min(terminal, currentZ + settings.BrakePerTick);
        // The optional floating mode must never turn braking into an upward
        // impulse, which would repeatedly release and redeploy the parachute.
        return Math.Min(-1f, currentZ + settings.BrakePerTick);
    }

    public const float DeployedGravity = 0.1f;
    public static float ReleaseGravity(float current, float original) => current == DeployedGravity ? original : current;
}
