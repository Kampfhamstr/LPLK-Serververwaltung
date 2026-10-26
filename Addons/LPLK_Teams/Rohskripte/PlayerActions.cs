using SwiftlyS2.Shared.Players;
namespace LPLK_Teams;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Action<IPlayer>> TargetActions(IPlayer actor,IPlayer target)
    {
        var result=new Dictionary<string,Action<IPlayer>>();
        if(!Effective||!CanUseSelection(actor)||!IsHuman(target)||!CanTarget(actor.SteamID,target.SteamID))return result;
        var identity=Key(target);ulong id=target.SteamID;
        result["Verliererteam"]=a=>
        {
            var current=Core.PlayerManager.GetPlayer(identity.Slot);
            if(!Effective||!CanUseSelection(a)||!IsHuman(current)||Key(current!)!=identity||!CanTarget(a.SteamID,id))return;
            if(HasSelection(id))
            {
                if(selected.TryGetValue(id,out var owner)&&owner!=a.SteamID&&!CanTarget(a.SteamID,owner)){a.SendLplkChat("[LPLK] Auswahl eines gleich/höher berechtigten Admins.");return;}
                SaveSelection(id,null);a.SendLplkChat("[LPLK] Verliererteam-Zuordnung aufgehoben.");
            }
            else ConfirmSelection(a,id,identity,0);
        };
        return result;
    }
}
