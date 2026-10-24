using System.Text.Json.Serialization;
using System.Numerics;
namespace LPLK_Fluch;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FluchSettings
{
    public bool Enabled {get;init;}=true;
    public string AdminPermission {get;init;}="@css/root";
    public bool AllowSelfTarget {get;init;}
    public bool NotifyTargetInChat {get;init;}=true;
    public int ChickenCount {get;init;}=20;
    public float OrbitDurationSeconds {get;init;}=10;
    public float OrbitTickSeconds {get;init;}=.08f;
    public float HeadOrbitHeight {get;init;}=76;
    public float HeadOrbitRadius {get;init;}=58;
    public float OrbitSpeed {get;init;}=2.7f;
    public float OrbitWaveHeight {get;init;}=9;
    public float OrbitWaveSpeed {get;init;}=2;
    public float ChickenExplosionDamagePerChicken {get;init;}=5;
    public bool ShowStartCenterMessage {get;init;}=true;
    public bool ShowExplosionCenterMessage {get;init;}=true;
    public bool ShowOrbitProgressMessage {get;init;}
    public int StartCenterMessageMs {get;init;}=2500;
    public int ExplosionCenterMessageMs {get;init;}=2500;
    public int OrbitProgressMessageMs {get;init;}=300;
    public float MenuAutoCloseSeconds {get;init;}=30;
    public int MaxActiveCurses {get;init;}=4;
    public float TotalDamage=>ChickenCount*ChickenExplosionDamagePerChicken;
    public static void Validate(FluchSettings s)
    {
        static void Range(float x,float min,float max){if(!float.IsFinite(x)||x<min||x>max)throw new InvalidDataException("Fluch-Wert außerhalb des gültigen Bereichs.");}
        if(string.IsNullOrWhiteSpace(s.AdminPermission)||s.AdminPermission.Length>128||s.AdminPermission.Any(char.IsWhiteSpace))throw new InvalidDataException("Adminrecht fehlt oder ist ungültig.");
        Range(s.ChickenCount,1,64);Range(s.MaxActiveCurses,1,4);Range(s.OrbitDurationSeconds,1,60);Range(s.OrbitTickSeconds,.03f,1);
        Range(s.HeadOrbitHeight,0,250);Range(s.HeadOrbitRadius,10,250);Range(s.OrbitSpeed,0,20);Range(s.OrbitWaveHeight,0,100);Range(s.OrbitWaveSpeed,0,20);
        Range(s.ChickenExplosionDamagePerChicken,0,100);Range(s.StartCenterMessageMs,100,10000);Range(s.ExplosionCenterMessageMs,100,10000);Range(s.OrbitProgressMessageMs,100,2000);Range(s.MenuAutoCloseSeconds,5,120);
    }
}
public readonly record struct CurseOwner(int Slot,ulong Steam,string Session,uint Pawn,int Team);
public sealed record CursePlayer(CurseOwner Owner,bool Alive,bool Admin,Vector3 Origin);
public interface ICurseWorld
{
    CursePlayer? Player(int slot);
    uint Spawn(Vector3 position,float yaw);
    bool Move(uint entity,Vector3 position,float yaw);
    void Destroy(uint entity);
    void Damage(CurseOwner target,CurseOwner caster,float amount);
    void Notice(CurseOwner target,string kind,float value,FluchSettings settings);
}
public sealed class CurseLease(CurseOwner caster,CurseOwner target,FluchSettings settings,double now)
{
    public CurseOwner Caster {get;}=caster;
    public CurseOwner Target {get;}=target;
    public FluchSettings Settings {get;}=settings;
    public double Started {get;}=now;
    public double NextMove {get;set;}=now;
    public List<(uint Handle,int Index)> Chickens {get;}=[];
}
public static class CurseMath
{
    public static float Angle(int index,FluchSettings s,double elapsed)=>(float)(Math.PI*2*index/s.ChickenCount+elapsed*s.OrbitSpeed);
    public static Vector3 Position(Vector3 origin,float angle,FluchSettings s)=>origin+new Vector3(MathF.Cos(angle)*s.HeadOrbitRadius,MathF.Sin(angle)*s.HeadOrbitRadius,s.HeadOrbitHeight+MathF.Sin(angle*s.OrbitWaveSpeed)*s.OrbitWaveHeight);
}
// Runs exclusively on the game thread. No queued timers retain native player/entity wrappers.
public sealed class CurseController(ICurseWorld world,Action<Exception> error)
{
    private readonly Dictionary<int,CurseLease> active=[];
    public int Count=>active.Count;
    public int ChickenCount=>active.Values.Sum(x=>x.Chickens.Count);
    public int Explosions {get;private set;}
    public string Start(CurseOwner caster,CurseOwner target,FluchSettings s,double now)
    {
        if(!s.Enabled)return "Der Fluch ist ausgeschaltet.";
        var a=world.Player(caster.Slot);var b=world.Player(target.Slot);
        if(a is not {Alive:true,Admin:true}||a.Owner!=caster||b is not {Alive:true}||b.Owner!=target)return "Spieler oder Berechtigung hat sich geändert.";
        if(!s.AllowSelfTarget&&caster.Slot==target.Slot)return "Selbstverfluchen ist ausgeschaltet.";
        if(!active.ContainsKey(target.Slot)&&Count>=s.MaxActiveCurses)return "Zu viele aktive Flüche. Erst einen Fluch beenden.";
        Release(target.Slot);
        var lease=new CurseLease(caster,target,s,now);
        try
        {
            for(int i=0;i<s.ChickenCount;i++)
            {
                float angle=CurseMath.Angle(i,s,0);uint h=world.Spawn(CurseMath.Position(b.Origin,angle,s),angle*180/MathF.PI+90);
                if(h==uint.MaxValue)throw new InvalidOperationException("Huhn konnte nicht erzeugt werden.");
                lease.Chickens.Add((h,i));
            }
            active[target.Slot]=lease;
            world.Notice(target,"start",s.ChickenCount,s);
            return $"Fluch gestartet: {s.ChickenCount} Hühner, {s.OrbitDurationSeconds:0.#} s, {s.TotalDamage:0.#} Schaden am Ende.";
        }
        catch(Exception e){active.Remove(target.Slot);Cleanup(lease);error(e);return "Fluch konnte nicht gestartet werden; erzeugte Hühner wurden entfernt.";}
    }
    public void Tick(bool enabled,double now)
    {
        foreach(var lease in active.Values.ToArray())
        {
            // Damage callbacks may synchronously stop all curses or replace a lease.
            if(!active.TryGetValue(lease.Target.Slot,out var current)||!ReferenceEquals(current,lease))continue;
            try
            {
                var target=world.Player(lease.Target.Slot);var caster=world.Player(lease.Caster.Slot);
                if(!enabled||target is not {Alive:true}||target.Owner!=lease.Target||caster is not {Alive:true,Admin:true}||caster.Owner!=lease.Caster){Release(lease.Target.Slot);continue;}
                var s=lease.Settings;double elapsed=now-lease.Started;
                if(elapsed>=s.OrbitDurationSeconds)
                {
                    active.Remove(lease.Target.Slot); // Exactly once, including reentrant death hooks.
                    Cleanup(lease);
                    world.Damage(lease.Target,lease.Caster,s.TotalDamage);Explosions++;
                    world.Notice(lease.Target,"end",s.TotalDamage,s);continue;
                }
                if(now<lease.NextMove)continue;lease.NextMove=now+s.OrbitTickSeconds;
                for(int i=lease.Chickens.Count-1;i>=0;i--)
                {
                    var chicken=lease.Chickens[i];float angle=CurseMath.Angle(chicken.Index,s,elapsed);
                    if(!world.Move(chicken.Handle,CurseMath.Position(target.Origin,angle,s),angle*180/MathF.PI+90))lease.Chickens.RemoveAt(i);
                }
                if(lease.Chickens.Count==0){Release(lease.Target.Slot);continue;}
                if(s.ShowOrbitProgressMessage)world.Notice(lease.Target,"progress",(float)(s.OrbitDurationSeconds-elapsed),s);
            }
            catch(Exception e){Release(lease.Target.Slot);error(e);}
        }
    }
    private void Cleanup(CurseLease lease){foreach(var c in lease.Chickens)try{world.Destroy(c.Handle);}catch(Exception e){error(e);}lease.Chickens.Clear();}
    public void Release(int target){if(active.Remove(target,out var lease))Cleanup(lease);}
    public void ReleasePlayer(int slot){foreach(var x in active.Values.Where(x=>x.Caster.Slot==slot||x.Target.Slot==slot).ToArray())Release(x.Target.Slot);}
    public void Clear(){foreach(int slot in active.Keys.ToArray())Release(slot);}
    public void Forget()=>active.Clear(); // Map entities have already gone away; never dereference old handles.
}
