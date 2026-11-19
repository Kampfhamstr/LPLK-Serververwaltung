using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;
using Vec=System.Numerics.Vector3;
namespace LPLK_Parachute;

public sealed partial class Plugin
{
    private readonly Dictionary<(int,ulong,uint),TakeoverWatch> takeovers=[];
    private int takeoverChecks,takeoverRecoveries,takeoverBlocked;
    private string? recoveryError;
    private static Vec V(Vector p)=>new(p.X,p.Y,p.Z);
    private static Vector N(Vec p)=>new(p.X,p.Y,p.Z);
    private CCSPlayerPawn? ControlledPawn(IPlayer player)
    {
        if(!player.IsValid||!player.Controller.IsValid||player.Controller.IsHLTV||
            player.Pawn is not {IsValid:true,DesignerName:"player"} current)return null;
        var pawn=new CHandle<CCSPlayerPawn>{Raw=Core.EntitySystem.GetRefEHandle(current).Raw}.Value;
        // A bot's old PlayerPawn may still point to the human-controlled pawn.
        // Only the controller that the pawn itself names may apply physics.
        if(pawn is not {IsValid:true}||pawn.Controller.Value is not {IsValid:true} owner||owner.Index!=player.Controller.Index)return null;
        return pawn;
    }
    private TraceParams Parameters(CCSPlayerPawn pawn)=>new(){ObjectQuery=RnQueryObjectSet.All,
        InteractWith=MaskTrace.Player|MaskTrace.Solid|MaskTrace.WorldGeometry|MaskTrace.Window,
        Collision=CollisionGroup.Default,EntitiesToIgnore=[pawn]};
    private static BBox_t Bounds(CCSPlayerPawn pawn)
    {
        var min=pawn.Collision.Mins;var max=pawn.Collision.Maxs;
        if(!ControlPolicy.Finite(V(min))||!ControlPolicy.Finite(V(max))||max.X-min.X is <16 or >80||max.Y-min.Y is <16 or >80||max.Z-min.Z is <24 or >100)
            throw new InvalidOperationException("Unexpected player hull");
        return new(){Mins=min,Maxs=max};
    }
    private TraceResult Hull(CCSPlayerPawn pawn,Vector from,Vector to)=>Core.Trace.TracePlayerBBox(from,to,Bounds(pawn),Parameters(pawn));
    private static bool Clear(TraceResult t)=>!t.StartInSolid&&!t.DidHit&&float.IsFinite(t.Fraction)&&t.Fraction>=.999f;
    private bool Visible(CCSPlayerPawn pawn,Vector from,Vector to)=>Clear(Core.Trace.TraceShapeLine(from,to,Parameters(pawn)));
    private void CheckTakeover((int,ulong,uint) key,CCSPlayerPawn pawn,GameButtonFlags buttons)
    {
        if(pawn.LifeState!=0||pawn.Health<=0||pawn.AbsOrigin is not {} origin)return;
        long now=Environment.TickCount64;
        if(!takeovers.TryGetValue(key,out var watch)){takeovers[key]=watch=new(now,V(origin));return;}
        if(!watch.Due(now))return;
        try
        {
            bool ladder=pawn.MoveType==MoveType_t.MOVETYPE_LADDER;
            if(!ladder&&pawn.MoveType!=MoveType_t.MOVETYPE_WALK){watch.Complete();return;}
            var trace=Hull(pawn,origin,new(origin.X,origin.Y,origin.Z+.1f));takeoverChecks++;
            bool input=(buttons&(GameButtonFlags.W|GameButtonFlags.S|GameButtonFlags.A|GameButtonFlags.D|GameButtonFlags.A2|GameButtonFlags.D2|GameButtonFlags.Space))!=0;
            if(!watch.Check(now,V(origin),ladder,input,trace.StartInSolid,Core.EntitySystem.GetGameRules()?.FreezePeriod!=false))return;
            var normal=pawn.MovementServices is {} movement?V(movement.LadderNormal):Vec.Zero;
            foreach(var offset in ControlPolicy.RecoveryOffsets(normal,ladder))
            {
                var target=N(V(origin)+offset);
                if(!Clear(Hull(pawn,target,new(target.X,target.Y,target.Z+.1f))))continue;
                // Never jump through an obstacle. When the hull overlaps, at least the
                // centre and eye paths must be clear; non-overlapping moves also sweep the hull.
                if(!trace.StartInSolid&&!Clear(Hull(pawn,origin,target)))continue;
                if(!Visible(pawn,new(origin.X,origin.Y,origin.Z+32),new(target.X,target.Y,target.Z+32))||
                   !Visible(pawn,new(origin.X,origin.Y,origin.Z+56),new(target.X,target.Y,target.Z+56)))continue;
                ReleasePawn(key.Item3);
                pawn.Teleport(target,null,Vector.Zero);
                if(ladder){pawn.MoveType=MoveType_t.MOVETYPE_WALK;pawn.ActualMoveType=MoveType_t.MOVETYPE_WALK;pawn.MoveTypeUpdated();}
                takeoverRecoveries++;watch.Complete();recoveryError=null;return;
            }
            takeoverBlocked++;watch.Complete(); // No repeated nudging if no safe destination exists.
        }
        catch(Exception e){recoveryError=e.GetType().Name;watch.Complete();}
    }
    private object ProbeSnapshot()
    {
        var pawns=Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.Controller.IsHLTV).Take(64).Select(p=>
        {
            var pawn=ControlledPawn(p);
            bool? solid=null;string? traceError=null;
            if(pawn is {LifeState:0,AbsOrigin:{} origin})try{solid=Hull(pawn,origin,new(origin.X,origin.Y,origin.Z+.1f)).StartInSolid;}catch(Exception e){traceError=e.GetType().Name;}
            return new{slot=p.PlayerID,bot=p.IsFakeClient,takeover=p.Controller.ControllingBot,pawn=pawn is null?(uint?)null:Core.EntitySystem.GetRefEHandle(pawn).Raw,
                alive=pawn is {LifeState:0,Health:>0},gravity=pawn?.ActualGravityScale,velocityZ=pawn?.AbsVelocity.Z,move=pawn?.MoveType.ToString(),solid,traceError};
        }).ToArray();
        return new{version="0.1.13",botDeployments,humanDeployments,takeoverDeployments,takeoverChecks,takeoverRecoveries,takeoverBlocked,recoveryError,botProbeResult,pawns};
    }
}
