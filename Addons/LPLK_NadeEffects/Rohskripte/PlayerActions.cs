using SwiftlyS2.Shared.Players;
namespace LPLK_NadeEffects;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Action<IPlayer>> TargetActions(IPlayer actor,IPlayer target)
    {
        var result=new Dictionary<string,Action<IPlayer>>();
        if(!module.CanAdmin(actor)||!Enabled||world.Player(target.PlayerID) is not {} state)return result;
        var connection=state.Owner.Connection;string name=target.Name;
        result["Granateneffekt"]=a=>
        {
            if(!module.CanAdmin(a)||!Enabled||world.Player(a.PlayerID) is not {} caster)return;
            string changed=effects.Toggle(caster.Owner.Connection,connection);a.SendChat("[LPLK] "+NativeNadeWorld.Chat(name)+": "+changed);
            if(changed is "AN" or "AUS"&&settings.NotifyAdminsOnToggle)Notify($"{NativeNadeWorld.Chat(a.Name)}: Granateneffekt für {NativeNadeWorld.Chat(name)} {changed}.",settings);
            WriteStatus();
        };
        return result;
    }
}
