using SwiftlyS2.Shared.Players;
namespace LPLK_Sounds;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Action<IPlayer>> TargetActions(IPlayer actor,IPlayer target)
    {
        var result=new Dictionary<string,Action<IPlayer>>();
        if(!module.CanAdmin(actor)||!menusEnabled||!config.Enabled||!RealSoundPlayer(target)||actor.SteamID==target.SteamID)return result;
        var recipient=new SoundRecipient(target.Slot,target.SteamID);string session=target.SessionId.ToString();
        result["Sound"]=a=>{if(module.CanAdmin(a)&&menusEnabled&&config.Enabled)OpenPlayerMenu(a,()=>ShowSendSoundMenu(a,0,recipient,session));};
        return result;
    }
}
