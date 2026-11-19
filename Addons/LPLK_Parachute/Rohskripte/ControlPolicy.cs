using System.Numerics;
namespace LPLK_Parachute;

public static class ControlPolicy
{
    public static bool Deploy(bool autonomousBot, bool botsEnabled, bool use, bool allowed, bool alive,
        bool ground, bool walking, float velocityZ, bool hostageBlocked) =>
        alive && !ground && walking && float.IsFinite(velocityZ) && velocityZ < 0 && !hostageBlocked &&
        (autonomousBot ? botsEnabled : use && allowed);
    public static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public static IEnumerable<Vector3> RecoveryOffsets(Vector3 normal, bool ladder)
    {
        if (ladder)
        {
            var horizontal = new Vector3(normal.X, normal.Y, 0);
            if (!Finite(horizontal) || horizontal.LengthSquared() < .25f) yield break;
            horizontal = Vector3.Normalize(horizontal);
            foreach (float d in new[] { 4f, 8f, 12f, 16f }) yield return horizontal * d + new Vector3(0, 0, 2);
        }
        else
        {
            // Only a tiny upward release from overlapping ground; no general-purpose wall teleport.
            yield return new(0, 0, 2); yield return new(0, 0, 4); yield return new(0, 0, 8);
        }
    }
}

public sealed class TakeoverWatch(long started, Vector3 initial)
{
    private long next = started + 100, stalledSince;
    private Vector3 previous = initial;
    public bool Finished { get; private set; }
    public bool Due(long now) => !Finished && now >= next;
    public bool Check(long now, Vector3 position, bool ladder, bool input, bool inSolid, bool frozen)
    {
        next = now + 100;
        if (now - started > 2500 || !ControlPolicy.Finite(position)) { Finished = true; return false; }
        bool moved = Vector3.DistanceSquared(previous, position) > 1;
        previous = position;
        if (frozen) { stalledSince = 0; return false; }
        if (inSolid) return true;
        if (!ladder || !input || moved) { stalledSince = 0; return false; }
        if (stalledSince == 0) stalledSince = now;
        return now - stalledSince >= 700;
    }
    public void Complete() => Finished = true;
}
