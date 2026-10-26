using SwiftlyS2.Shared.Players;
namespace LPLK_Fluch;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Action<IPlayer>> TargetActions(IPlayer actor,IPlayer target)
    {
        var result=new Dictionary<string,Action<IPlayer>>();
        if(!module.CanAdmin(actor)||!Enabled||(!settings.AllowSelfTarget&&actor.SteamID==target.SteamID)||world.Player(target.PlayerID) is not {Alive:true} state)return result;
        var owner=state.Owner;
        result["Fluch"]=a=>{if(module.CanAdmin(a)&&Enabled&&world.Player(a.PlayerID) is {} caster){a.SendLplkChat("[LPLK] "+curses.Start(caster.Owner,owner,settings,Environment.TickCount64/1000d));WriteStatus();}};
        return result;
    }
}
