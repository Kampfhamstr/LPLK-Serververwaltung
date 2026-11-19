using SwiftlyS2.Shared.Players;
namespace LPLK_Admin;
public readonly record struct PlayerControlState(bool? Frozen,bool? Noclip);
public sealed partial class Plugin
{
    private Func<IPlayer,PlayerControlState> playerControlState=ReadPlayerControlState;
    private static PlayerControlState ReadPlayerControlState(IPlayer p)
    {
        try
        {
            if(!p.IsValid||!p.Controller.IsValid)return default;
            bool noclip=p.Controller.NoClipEnabled;
            var pawn=p.PlayerPawn;
            return new(pawn is {IsValid:true,Health:>0}?(int)pawn.MoveType==0:null,noclip);
        }
        catch{return default;}
    }
    private bool? CurrentControl(IPlayer p,string action)
    {
        var current=playerControlState(p);return action=="freeze"?current.Frozen:current.Noclip;
    }
}
