using System.Numerics;

namespace LPLK_AntiCamp;

public sealed record CampSettings
{
    public bool PluginEnabled { get; init; } = true;
    public int CampTime { get; init; } = 30;
    public int WarningSeconds { get; init; } = 5;
    public int SlapORSlayPunishment { get; init; } = 1;
    public int SlapDamage { get; init; } = 5;
    public int MinHealthReserve { get; init; } = 15;
    public double FirstPunishDelay { get; init; } = 2;
    public double PunishFrequency { get; init; } = 2;
    public double CampCheckRadius { get; init; } = 50;
    public bool PunishBeacon { get; init; } = true;
    public bool PunishAnyway { get; init; } = true;
    public bool AllowTCampHostageMap { get; init; } = true;
    public bool AllowTCampBombPlanted { get; init; } = true;
    public bool AllowCTCampBombMap { get; init; } = true;
    public bool AllowCtCampBombDropped { get; init; } = true;
    public bool DisableChatMessages { get; init; }

    public static void Validate(CampSettings s)
    {
        if(s.CampTime is < 5 or > 300 || s.WarningSeconds < 0 || s.WarningSeconds > s.CampTime ||
            s.SlapORSlayPunishment is < 0 or > 2 || s.SlapDamage is < 0 or > 100 || s.MinHealthReserve is < 0 or > 100 ||
            !double.IsFinite(s.FirstPunishDelay) || s.FirstPunishDelay is < 0 or > 30 ||
            !double.IsFinite(s.PunishFrequency) || s.PunishFrequency is < .25 or > 60 ||
            !double.IsFinite(s.CampCheckRadius) || s.CampCheckRadius is < 10 or > 1000)
            throw new InvalidDataException("AntiCamp-Einstellungen außerhalb der gültigen Grenzen.");
    }
}

public readonly record struct CampDecision(bool Warn, bool Punish, bool Announce=false);
public static class CampPolicy
{
    public static bool Exempt(int team,bool bombMap,bool hostageMap,bool planted,bool dropped,CampSettings s) =>
        team==2 && (hostageMap && s.AllowTCampHostageMap || planted && s.AllowTCampBombPlanted) ||
        team==3 && bombMap && (s.AllowCTCampBombMap || dropped && s.AllowCtCampBombDropped);
    public static int Damage(int health,CampSettings s) => Math.Min(s.SlapDamage,Math.Max(0,health-s.MinHealthReserve));
    public static double AngleDistance(float a,float b) => Math.Abs(Math.IEEERemainder(a-b,360));
    public static double NextMenuValue(double value,double step,double maximum,double restart) => value>=maximum?restart:Math.Min(maximum,value+step);
}

// Monotonic seconds are supplied by the host, so server ticks and wall-clock corrections cannot accelerate punishment.
public sealed class CampClock(Vector3 position, Vector2 angles)
{
    private Vector3 origin=position;
    private Vector3? punishOrigin;
    private Vector2 lastAngles=angles;
    private double? started;
    private double? nextPunish;
    private bool warned;
    public bool HasMoved(Vector3 position,double radius)=>Vector3.DistanceSquared(origin,position)>=radius*radius || punishOrigin is {} punishment && Vector3.DistanceSquared(punishment,position)>=radius*radius;
    public CampDecision Step(double now,Vector3 position,Vector2 angles,CampSettings settings)
    {
        double look=CampPolicy.AngleDistance(lastAngles.X,angles.X)+CampPolicy.AngleDistance(lastAngles.Y,angles.Y);
        lastAngles=angles;
        if(started is null){if(look>15){started=now;origin=position;}return default;}
        bool announce=punishOrigin is null&&now>=started.Value+settings.CampTime;
        if(announce)punishOrigin=position;
        double due=nextPunish??(started.Value+settings.CampTime+settings.FirstPunishDelay);
        if(!warned && settings.WarningSeconds>0 && now>=due-settings.WarningSeconds){
            warned=true;nextPunish=Math.Max(due,now+settings.WarningSeconds);return new(true,false,announce);
        }
        if(now<due)return new(false,false,announce);
        nextPunish=now+settings.PunishFrequency;
        return new(false,true,announce); // One action per interval; no burst to catch up after a stalled frame.
    }
}
