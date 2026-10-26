using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;

namespace SerververwaltungBridge;

public sealed partial class Plugin
{
    private IInterfaceManager? _chatGuardInterfaces;
    public override void UseSharedInterface(IInterfaceManager manager)=>_chatGuardInterfaces=manager;
    private HookResult? FilterWithChatGuard(int playerId,string text,bool teamOnly)
    {
        if(!Core.PluginManager.GetAllPlugins().Contains("LPLK_ChatGuard"))return null;
        try
        {
            if(_chatGuardInterfaces?.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.ChatGuard.Filter.v1",out var api)!=true
                ||api is null||!((Func<bool>)api["ready"])())return HookResult.Stop;
            return (HookResult)((Func<int,string,bool,int>)api["filter"])(playerId,text,teamOnly);
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_ChatGuard nicht erreichbar; Nachricht weder versendet noch archiviert.");return HookResult.Stop;}
    }
}
