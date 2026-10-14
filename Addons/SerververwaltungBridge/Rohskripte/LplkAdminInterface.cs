using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SerververwaltungBridge;

public sealed partial class Plugin
{
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Bridge.Admin.v1",new()
        {
            ["execute"]=(Func<ulong,string,string,string>)ExecuteLplkAdmin,
            ["teamsReady"]=(Func<bool>)TeamsReady,
            ["chatguardReady"]=(Func<bool>)(()=>FilterWithChatGuard(-1,"",false)==SwiftlyS2.Shared.Misc.HookResult.Continue)
        });

    // Called synchronously on the game thread. Both UI paths use Execute and the same local data.
    private string ExecuteLplkAdmin(ulong actor,string action,string request)
    {
        try
        {
            if(_unloading||!Directory.Exists(_root))throw new InvalidOperationException("Bridge nicht bereit.");
            if(!AdminActionPolicy.Permissions.ContainsKey(action))throw new InvalidOperationException("Unbekannte Admin-Aktion.");
            var sender=actor==0?null:Core.PlayerManager.GetAllPlayers().FirstOrDefault(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID==actor);
            if(actor!=0&&(sender is null||!AdminActionPolicy.Allowed(action,p=>Core.Permission.PlayerHasPermission(actor,p))))throw new InvalidOperationException("Keine Berechtigung.");
            if(request.Length>8192)throw new InvalidOperationException("Anfrage zu groß.");
            using var doc=JsonDocument.Parse(request);var root=doc.RootElement;
            string? target=root.TryGetProperty("target",out var t)?t.GetString():null;
            if(target is not null)
            {
                if(!AdminActionPolicy.SteamId(target))throw new InvalidOperationException("Ungültige SteamID64.");
                ulong targetId=ulong.Parse(target);
                if(!AdminActionPolicy.CanTarget(actor,targetId,AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(actor,p)),AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(targetId,p))))throw new InvalidOperationException("Ziel hat gleiche oder höhere Adminrechte.");
            }
            var payload=root.TryGetProperty("payload",out var v)?v.Deserialize<Dictionary<string,JsonElement>>()??[]:[];
            if(action is "goto_player" or "bring_player")
            {
                if(sender is null)throw new InvalidOperationException("Teleport benötigt einen Spieler als Absender.");
                payload["admin_steamid"]=JsonSerializer.SerializeToElement(actor.ToString());
            }
            var command=new BridgeCommand { Id="lplk_"+Guid.NewGuid().ToString("N"),Action=action,TargetSteamid=target,
                Payload=payload,RequestedBy=sender?.Controller.PlayerName??"LPLK-Konsole",RequestedByRole="admin",RequestedAt=DateTimeOffset.UtcNow,NotifyScope="admins",NotifyTarget=true };
            Execute(command);
            try { RecordAdminAction(command,true,null,true); } catch(Exception e) { Core.Logger.LogWarning(e,"LPLK-Adminaktion ausgeführt, Protokollierung fehlgeschlagen"); }
            return JsonSerializer.Serialize(new { success=true,message="Aktion ausgeführt." });
        }
        catch(Exception e) { return JsonSerializer.Serialize(new { success=false,message=e.Message }); }
    }
}

