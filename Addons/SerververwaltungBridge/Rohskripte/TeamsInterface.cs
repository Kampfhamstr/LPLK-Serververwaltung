using SwiftlyS2.Shared.Players;
namespace SerververwaltungBridge;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Delegate>? TeamsApi()
    {
        return !_unloading && _chatGuardInterfaces?.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Teams.Control.v1",out var api)==true?api:null;
    }
    private bool TeamsReady()=>TeamsApi() is {} api&&api.TryGetValue("ready",out var ready)&&ready is Func<bool> check&&check();
    private void PermitTeamChange(IPlayer player,int team)
    {
        if(!Core.PluginManager.GetAllPlugins().Contains("LPLK_Teams"))return;
        var api=TeamsApi();
        if(!TeamsReady()||api is null||!api.TryGetValue("permit",out var permit)||permit is not Action<IPlayer,int> grant)
            throw new InvalidOperationException("Teammodul nicht bereit; Admin-Teamwechsel nicht ausgeführt.");
        grant(player,team);
    }
}
