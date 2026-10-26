using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Delegate>? ModeApi(IPlayer player,string mode,string page)
    {
        if(!Connected(player)||!Loaded("LPLK_"+mode)||interfaces is null)return null;
        if(!interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player."+mode+".v1",out var api))return null;
        return api.TryGetValue("allowed",out var allowed)&&allowed is Func<IPlayer,string,bool> check&&check(player,page)
            &&api.TryGetValue("open",out var open)&&open is Action<IPlayer,string>?api:null;
    }
    private void OpenMode(IPlayer player,string mode,string page)
    {
        if(ModeApi(player,mode,page) is not {} api){player.SendLplkChat("[LPLK] Spielmodus derzeit nicht verfügbar.");return;}
        ((Action<IPlayer,string>)api["open"])(player,page);
    }
}
