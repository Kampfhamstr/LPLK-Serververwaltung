using SwiftlyS2.Shared.Players;
namespace LPLK_Admin;
public sealed partial class Plugin
{
    private IEnumerable<(string,Action<IPlayer>)> TargetModuleActions(IPlayer actor,IPlayer target)
    {
        foreach(var id in new[]{"LPLK_Teams","LPLK_Fluch","LPLK_NadeEffects","LPLK_Economy","LPLK_Sounds"})
        {
            if(!Loaded(id)||!ModuleAllowed(actor,id)||interfaces is null ||
               !interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Target."+id+".v1",out var api)||
               !api.TryGetValue("actions",out var value)||value is not Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>> factory)continue;
            ulong steam=target.SteamID;string session=target.SessionId.ToString();
            foreach(var label in factory(actor,target).Keys)
                yield return(label,a=>
                {
                    var current=Target(steam.ToString(),session);
                    if(!Loaded(id)||!ModuleAllowed(a,id)){a.SendChat("[LPLK] Funktion nicht mehr verfügbar.");return;}
                    if(factory(a,current).TryGetValue(label,out var action))action(a);
                    else a.SendChat("[LPLK] Funktion für diesen Spieler nicht mehr verfügbar.");
                });
        }
    }
}
