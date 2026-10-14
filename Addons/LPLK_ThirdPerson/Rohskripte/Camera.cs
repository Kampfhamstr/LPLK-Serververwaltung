using System.Numerics;
using System.Text.RegularExpressions;

namespace LPLK_ThirdPerson;

public sealed record CameraSettings
{
    public bool Enabled { get; init; } = true;
    public string UseTpPermission { get; init; } = "thirdperson.use";
    public bool UseSmoothCam { get; init; } = true;
    public float ThirdPersonDistance { get; init; } = 100;
    public float ThirdPersonHeight { get; init; } = 80;
    public float SmoothCameraSpeed { get; init; } = .3f;
    public string DamageMode { get; init; } = "back";
    public bool EnableKnifeWarnings { get; init; } = true;
    public static bool PermissionValid(string? value) => value is not null && (value.Length == 0 || Regex.IsMatch(value,@"^[a-zA-Z0-9_.:-]{1,128}$"));
    public static void Validate(CameraSettings s)
    {
        if(!PermissionValid(s.UseTpPermission) || s.DamageMode is not ("back" or "none" or "all") ||
            !float.IsFinite(s.ThirdPersonDistance) || s.ThirdPersonDistance is < 20 or > 250 ||
            !float.IsFinite(s.ThirdPersonHeight) || s.ThirdPersonHeight is < 20 or > 120 ||
            !float.IsFinite(s.SmoothCameraSpeed) || s.SmoothCameraSpeed is < .01f or > 1)
            throw new InvalidDataException("Ungültige ThirdPerson-Einstellungen.");
    }
}

public readonly record struct CameraOwner(int Slot, ulong Steam, string Session, uint Pawn, int Team);
public sealed record CameraPlayer(CameraOwner Owner, bool Alive, bool Allowed, bool ForeignView, uint View, Vector3 Origin, Vector3 Eye, float Yaw, float Pitch);
public sealed record CameraLease(CameraOwner Owner, uint Camera);
public interface ICameraWorld
{
    CameraPlayer? Player(int slot);
    uint Create();
    Vector3? Position(uint camera);
    void Move(uint camera, Vector3 position, CameraPlayer player);
    bool Attach(CameraPlayer player, uint camera);
    void Restore(CameraLease lease);
    void Destroy(uint camera);
    Vector3 Clip(CameraPlayer player, Vector3 from, Vector3 to);
}

public static class CameraMath
{
    public static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    public static Vector3 Backward(float yaw)=>new(-MathF.Cos(yaw*MathF.PI/180),-MathF.Sin(yaw*MathF.PI/180),0);
    public static float SmoothFactor(float speed,float delta)=>1-MathF.Pow(1-speed,Math.Clamp(delta,0,.1f)*64);
    public static Vector3 StopBefore(Vector3 from,Vector3 to,Vector3 hit)
    {
        var delta=to-from;float length=delta.Length();if(length<.001f)return from;
        float distance=Math.Clamp(Vector3.Dot(hit-from,delta/length)-5,0,length);
        return from+delta/length*distance;
    }
    public static bool BlockDamage(string mode,Vector3 attacker,Vector3 victim,float yaw) =>
        mode=="none" || mode=="back" && (!Finite(attacker)||!Finite(victim)||!float.IsFinite(yaw)||Vector3.Dot(victim-attacker,-Backward(yaw))<0);
}

// Engine calls remain behind one boundary. Session + pawn serial + team own a lease,
// never just the reusable player slot. Only our own view handle may be restored.
public sealed class CameraController(ICameraWorld world)
{
    private readonly Dictionary<int,CameraLease> leases=[];
    public IReadOnlyCollection<CameraLease> Leases=>leases.Values;
    public int Count=>leases.Count;
    public bool Owns(CameraPlayer p)=>leases.TryGetValue(p.Owner.Slot,out var l)&&l.Owner==p.Owner&&l.Camera==p.View;
    public bool Allowed(CameraPlayer? p,CameraSettings s,bool enabled)=>enabled&&s.Enabled&&p is {Alive:true,Allowed:true,ForeignView:false} &&
        p.Owner.Team is 2 or 3 && CameraMath.Finite(p.Origin)&&CameraMath.Finite(p.Eye)&&float.IsFinite(p.Yaw)&&float.IsFinite(p.Pitch);
    private Vector3 Target(CameraPlayer p,CameraSettings s)
    {
        // Clip the height offset as well, so a crouching player cannot lift a camera through a ceiling.
        var anchor=world.Clip(p,p.Eye,p.Origin+new Vector3(0,0,s.ThirdPersonHeight));
        return world.Clip(p,anchor,anchor+CameraMath.Backward(p.Yaw)*s.ThirdPersonDistance);
    }
    public string Toggle(int slot,CameraSettings s,bool enabled)
    {
        var p=world.Player(slot);
        if(leases.TryGetValue(slot,out var old))
        {
            bool same=p?.Owner==old.Owner;Release(slot);if(same)return "Kamera AUS";
        }
        if(!Allowed(p,s,enabled))return "Kamera derzeit nicht freigegeben";
        uint camera=uint.MaxValue;
        try
        {
            // Preserve the source plugin's close-front-wall restriction.
            float pitch=p!.Pitch*MathF.PI/180, yaw=p.Yaw*MathF.PI/180;
            var ahead=p.Eye+new Vector3(MathF.Cos(yaw)*MathF.Cos(pitch),MathF.Sin(yaw)*MathF.Cos(pitch),-MathF.Sin(pitch))*40;
            if(Vector3.Distance(world.Clip(p,p.Eye,ahead),ahead)>1)return "Zu nah an einer Wand";
            var position=Target(p,s);if(!CameraMath.Finite(position))throw new InvalidOperationException("Ungültige Kameraposition");
            camera=world.Create();if(camera==uint.MaxValue)throw new InvalidOperationException("Kamera konnte nicht erzeugt werden");
            world.Move(camera,position,p);
            if(!world.Attach(p,camera))throw new InvalidOperationException("Spielerzustand hat sich geändert");
            leases.Add(slot,new(p.Owner,camera));return "Kamera AN";
        }
        catch
        {
            if(camera!=uint.MaxValue){try{world.Restore(new(p!.Owner,camera));}finally{world.Destroy(camera);}}
            throw;
        }
    }
    public void Release(int slot)
    {
        if(!leases.Remove(slot,out var l))return;
        try{world.Restore(l);}finally{world.Destroy(l.Camera);}
    }
    public void Tick(CameraSettings s,bool enabled,float delta,Action<Exception> error)
    {
        foreach(var l in leases.Values.ToArray())
        {
            try
            {
                var p=world.Player(l.Owner.Slot);
                if(p is null||p.Owner!=l.Owner||p.View!=l.Camera||!Allowed(p,s,enabled)||world.Position(l.Camera) is not {} current)
                {Release(l.Owner.Slot);continue;}
                var target=Target(p,s);
                var next=s.UseSmoothCam?Vector3.Lerp(current,target,CameraMath.SmoothFactor(s.SmoothCameraSpeed,delta)):target;
                // Smoothing must not cut a corner or retain a position beyond a newly closed wall.
                next=world.Clip(p,current,next);next=world.Clip(p,p.Eye,next);
                if(!CameraMath.Finite(next))throw new InvalidOperationException("Ungültige Kameraposition");
                world.Move(l.Camera,next,p);
            }
            catch(Exception e){try{Release(l.Owner.Slot);}catch(Exception cleanup){error(cleanup);}error(e);}
        }
    }
    public void Clear(Action<Exception> error){foreach(int slot in leases.Keys.ToArray())try{Release(slot);}catch(Exception e){error(e);}}
    // Map destruction owns entity cleanup. No native access during MapUnload.
    public void Forget()=>leases.Clear();
}
