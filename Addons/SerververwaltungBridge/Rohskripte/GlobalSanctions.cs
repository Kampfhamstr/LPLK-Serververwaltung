using System.Text.Json;
using LPLK.Common;

namespace SerververwaltungBridge;

public sealed partial class Plugin
{
    private DateTimeOffset globalSanctionsAt;
    private static readonly HashSet<string> GlobalSanctionActions=["ban","unban","mute","unmute","gag","ungag","silence","unsilence"];
    private void GlobalSanctionsProtocol(int version){lock(fleetGate)globalSanctionsAt=version==1?DateTimeOffset.UtcNow:default;}
    private bool GlobalSanctionsReady(){if(LocalAdministrationOnly)return false;lock(fleetGate)return !_unloading&&(DateTimeOffset.UtcNow-globalSanctionsAt).TotalSeconds<=12;}
    private string QueueGlobalSanction(ulong actor,string actorSession,string action,string? target,Dictionary<string,JsonElement> payload)
    {
        if(!GlobalSanctionsReady())throw new InvalidOperationException("Zentrale Sperrverwaltung derzeit nicht erreichbar; keine lokale Ersatzsperre angelegt.");
        if(!GlobalSanctionActions.Contains(action)||!AdminActionPolicy.SteamId(target))throw new InvalidOperationException("Ungültiger globaler Sperrauftrag.");
        if(action is "ban" or "mute" or "gag" or "silence"){
            var reason=payload.TryGetValue("reason",out var value)?value.GetString():"Regelverstoß";
            payload["reason"]=JsonSerializer.SerializeToElement(AdminActionPolicy.Text(reason??"",150));
            int minutes=payload.TryGetValue("minutes",out var duration)?duration.GetInt32():60;
            if(minutes is <0 or >525600)throw new InvalidOperationException("Ungültige Sperrdauer.");
            payload["minutes"]=JsonSerializer.SerializeToElement(minutes);
        }
        var player=Core.PlayerManager.GetAllPlayers().FirstOrDefault(p=>p.IsValid&&p.IsAuthorized&&!p.IsFakeClient&&p.SteamID.ToString()==target);
        var request=new FleetRequest(Guid.NewGuid().ToString("N"),actor.ToString(),actorSession,0,target!,player?.SessionId.ToString()??"",action,payload,DateTimeOffset.UtcNow);
        lock(fleetGate){if(!GlobalSanctionsReady())throw new InvalidOperationException("Zentrale Sperrverwaltung nicht mehr aktuell.");WriteNewFleet(Path.Combine(FleetDirectory("outbox"),request.Id+".json"),JsonSerializer.Serialize(request,_json));}
        return "Globale Aktion angefordert; Bestätigung folgt nach zentraler Speicherung.";
    }
    private static void StrongestComm(Dictionary<ulong,CommEntry> entries,ulong steam,CommEntry next)
    {
        if(!entries.TryGetValue(steam,out var previous)||previous.ExpiresAt is not null&&(next.ExpiresAt is null||next.ExpiresAt>previous.ExpiresAt))entries[steam]=next;
    }
}
