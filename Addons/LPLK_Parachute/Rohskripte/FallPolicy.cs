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
        // Match Parachute v5, including its step before the next linear-speed tick.
        // ActualGravityScale=0.1 during deployment is part of that same mechanic.
        if (!float.IsFinite(currentZ) || currentZ >= 0) return currentZ;
        return (settings.Linear && currentZ >= -settings.FallSpeed) || settings.BrakePerTick == 0
            ? -settings.FallSpeed : currentZ + settings.BrakePerTick;
    }

    public const float DeployedGravity = 0.1f;
    public static float ReleaseGravity(float current, float original) => current == DeployedGravity ? original : current;
}
