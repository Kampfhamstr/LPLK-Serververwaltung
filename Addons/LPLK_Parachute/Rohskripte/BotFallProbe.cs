using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
namespace LPLK_Parachute;

public sealed partial class Plugin
{
    private sealed record BotProbe(uint Pawn,Vector Origin,float Gravity,long Started,int Deployments);
    private BotProbe? botProbe;
    private string botProbeResult="not_run";
    private bool NoHumans()=>!Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient&&!p.Controller.IsHLTV);
    private string StartBotProbe()
    {
        // Explicit console-only action, fixed test-server port, no humans and no config changes.
        if(Core.ConVar.Find<int>("hostport")?.Value!=27020||!NoHumans()||botProbe is not null||!settings.Enabled||!settings.BotsAutoDeploy||enabledConvar?.Value!=true||Core.EntitySystem.GetGameRules()?.FreezePeriod!=false)return "refused";
        foreach(var p in Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&p.IsFakeClient&&!p.Controller.IsHLTV))
        {
            var pawn=ControlledPawn(p);
            if(pawn is not {IsValid:true,LifeState:0,Health:>0,AbsOrigin:{} origin}||!pawn.GroundEntity.IsValid||pawn.MoveType!=MoveType_t.MOVETYPE_WALK)continue;
            var from=new Vector(origin.X,origin.Y,origin.Z+2);var to=new Vector(origin.X,origin.Y,origin.Z+96);
            if(!Clear(Hull(pawn,from,to)))continue;
            uint handle=Core.EntitySystem.GetRefEHandle(pawn).Raw;ReleasePawn(handle);
            botProbe=new(handle,from,pawn.ActualGravityScale,Environment.TickCount64,botDeployments);
            pawn.Teleport(to,null,new Vector(0,0,-240));botProbeResult="running";return "started";
        }
        return "no_safe_grounded_bot";
    }
    private void TickBotProbe()
    {
        if(botProbe is not {} probe)return;
        var pawn=new CHandle<CCSPlayerPawn>{Raw=probe.Pawn}.Value;
        if(pawn is not {IsValid:true,LifeState:0,Health:>0}){botProbe=null;botProbeResult="aborted_pawn_changed";return;}
        if(!NoHumans()){ReleasePawn(probe.Pawn);botProbe=null;botProbeResult="aborted_human_joined";WriteStatus();return;}
        if(Environment.TickCount64-probe.Started<300)return;
        bool deployed=botDeployments>probe.Deployments&&flights.ContainsKey(probe.Pawn)&&pawn.ActualGravityScale==FallPolicy.DeployedGravity&&pawn.AbsVelocity.Z>=-settings.FallSpeed-20;
        ReleasePawn(probe.Pawn);bool restored=pawn.ActualGravityScale==probe.Gravity;
        bool clear=Clear(Hull(pawn,probe.Origin,new(probe.Origin.X,probe.Origin.Y,probe.Origin.Z+.1f)));
        if(clear)pawn.Teleport(probe.Origin,null,Vector.Zero);
        botProbe=null;botProbeResult=$"deployed={deployed};gravityRestored={restored};positionRestored={clear}";WriteStatus();
    }
}
