using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
namespace LPLK_Teams;

public sealed record SavedReturnTeams(int Schema,BotVoteSession Session,ReturnTeamsSnapshot State);
public sealed partial class Plugin
{
    private readonly ReconnectTeams returnTeams=new();
    private BotVoteSession? returnSession;
    private bool returnReady,returnBlocked;
    private string? returnError;
    private string ReturnPath=>Path.Combine(RoundFileStore.DataPath(Core),"team-return.json");
    private void LoadReturnTeams(BotVoteSession session)
    {
        if(returnReady||returnBlocked)return;
        try
        {
            if(RamFile.Exists(ReturnPath))
            {
                var saved=JsonSerializer.Deserialize<SavedReturnTeams>(RamFile.ReadAllText(ReturnPath))??throw new InvalidDataException("Leerer Rückkehrzustand.");
                if(saved.Schema!=1||saved.Session is null||saved.State is null)throw new InvalidDataException("Ungültiger Rückkehrzustand.");
                if(saved.Session==session)returnTeams.Restore(saved.State);
            }
            returnSession=session;returnReady=true;SaveReturnTeams();
        }
        catch(Exception e){returnBlocked=true;returnError=e.GetType().Name;Core.Logger.LogWarning(e,"Team-Rückkehrzustand nicht lesbar; keine ungeprüfte Ausnahme.");}
    }
    private void SaveReturnTeams()
    {
        if(!returnReady||returnSession is null)return;
        try{DeferredJson.Queue(ReturnPath,new SavedReturnTeams(1,returnSession,returnTeams.Snapshot()));returnError=null;}
        catch(Exception e){returnError=e.GetType().Name;Core.Logger.LogWarning(e,"Team-Rückkehrzustand nicht gespeichert.");}
    }
    private void ClearReturnTeams(){returnTeams.Clear();SaveReturnTeams();}
    private void ObserveReturnTeam(IPlayer p,int team)
    {
        if(returnReady&&returnTeams.Observe(Key(p),team))SaveReturnTeams();
    }
    private void SyncReturnTeams()
    {
        if(!returnReady)return;
        var players=Core.PlayerManager.GetAllPlayers().Where(IsHuman).ToArray();
        bool changed=returnTeams.ReconcileMissing(players.Select(Key));
        foreach(var p in players)
            if(!intents.ContainsKey(p.PlayerID))changed|=returnTeams.Observe(Key(p),p.Controller.TeamNum);
        if(changed)SaveReturnTeams();
    }
}
