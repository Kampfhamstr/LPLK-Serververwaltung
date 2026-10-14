using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;
using Vec=System.Numerics.Vector3;

namespace LPLK_ThirdPerson;

public sealed class NativeCameraWorld(ISwiftlyCore core,Func<CameraSettings> settings,Func<IReadOnlyCollection<CameraLease>> leases):ICameraWorld
{
    private static Vec V(Vector v)=>new(v.X,v.Y,v.Z);
    private static Vector N(Vec v)=>new(v.X,v.Y,v.Z);
    private static CPointCamera? Camera(uint raw)=>new CHandle<CPointCamera>{Raw=raw}.Value;
    public CameraPlayer? Player(int slot)
    {
        var p=core.PlayerManager.GetPlayer(slot);
        if(p is not {IsValid:true,IsAuthorized:true,IsFakeClient:false}||!p.Controller.IsValid||p.PlayerPawn is not {IsValid:true} pawn||pawn.CameraServices is not {} services||pawn.AbsOrigin is not {} pos||pawn.EyePosition is not {} eye)return null;
        var owner=new CameraOwner(slot,p.SteamID,p.SessionId.ToString(),core.EntitySystem.GetRefEHandle(pawn).Raw,p.Controller.TeamNum);
        uint view=services.ViewEntity.Raw;
        bool own=leases().Any(l=>l.Owner==owner&&l.Camera==view);
        var permission=settings().UseTpPermission;
        // EyePosition follows crouching. Origin + configured height is separately clipped.
        return new(owner,p.IsAlive,permission.Length==0||core.Permission.PlayerHasPermission(p.SteamID,permission),services.ViewEntity.IsValid&&!own,view,V(pos),V(eye),pawn.EyeAngles.Yaw,pawn.EyeAngles.Pitch);
    }
    public uint Create()
    {
        var camera=core.EntitySystem.CreateEntityByDesignerName<CPointCamera>("point_camera");
        if(camera is not {IsValid:true})return uint.MaxValue;
        try{camera.DispatchSpawn();return core.EntitySystem.GetRefEHandle(camera).Raw;}catch{camera.Despawn();throw;}
    }
    public Vec? Position(uint camera)=>Camera(camera) is {IsValid:true,AbsOrigin:{} p}?V(p):null;
    public void Move(uint camera,Vec position,CameraPlayer player)
    {
        var c=Camera(camera);if(c is not {IsValid:true})throw new InvalidOperationException("Kamera fehlt");
        c.Teleport(N(position),new QAngle(player.Pitch,player.Yaw,0),Vector.Zero);
    }
    public bool Attach(CameraPlayer player,uint camera)
    {
        var current=Player(player.Owner.Slot);if(current is null||current.Owner!=player.Owner||current.View!=player.View||!current.Alive||!current.Allowed||current.ForeignView)return false;
        var pawn=core.PlayerManager.GetPlayer(player.Owner.Slot)!.PlayerPawn!;
        pawn.CameraServices!.ViewEntity.Raw=camera;pawn.CameraServices.ViewEntityUpdated();return true;
    }
    public void Restore(CameraLease lease)
    {
        var p=core.PlayerManager.GetPlayer(lease.Owner.Slot);
        if(p is not {IsValid:true}||p.SteamID!=lease.Owner.Steam||p.SessionId.ToString()!=lease.Owner.Session||p.PlayerPawn is not {IsValid:true} pawn||core.EntitySystem.GetRefEHandle(pawn).Raw!=lease.Owner.Pawn||pawn.CameraServices is not {} services||services.ViewEntity.Raw!=lease.Camera)return;
        services.ViewEntity.Raw=uint.MaxValue;services.ViewEntityUpdated();
    }
    public void Destroy(uint camera){if(Camera(camera) is {IsValid:true} c)c.Despawn();}
    public bool ProbeTrace()
    {
        var trace=core.Trace.TracePlayerBBox(new(0,0,64),new(0,0,96),new BBox_t{Mins=new(-4,-4,-4),Maxs=new(4,4,4)},new TraceParams{InteractWith=MaskTrace.Solid|MaskTrace.WorldGeometry|MaskTrace.Window,Collision=CollisionGroup.Default});
        return float.IsFinite(trace.Fraction)&&trace.Fraction is >=0 and <=1;
    }
    public Vec Clip(CameraPlayer player,Vec from,Vec to)
    {
        var p=core.PlayerManager.GetPlayer(player.Owner.Slot);var pawn=p?.PlayerPawn;
        if(pawn is not {IsValid:true}||core.EntitySystem.GetRefEHandle(pawn).Raw!=player.Owner.Pawn)throw new InvalidOperationException("Spielerfigur geändert");
        var trace=core.Trace.TracePlayerBBox(N(from),N(to),new BBox_t{Mins=new(-4,-4,-4),Maxs=new(4,4,4)},new TraceParams
        {ObjectQuery=RnQueryObjectSet.All,InteractWith=MaskTrace.Player|MaskTrace.Solid|MaskTrace.WorldGeometry|MaskTrace.Window,Collision=CollisionGroup.Default,EntitiesToIgnore=[pawn]});
        if(trace.StartInSolid)return from;
        return trace.DidHit?CameraMath.StopBefore(from,to,V(trace.HitPoint)):to;
    }
}
