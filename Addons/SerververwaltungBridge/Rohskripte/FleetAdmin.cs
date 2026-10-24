using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;

namespace SerververwaltungBridge;

public sealed record FleetServer(int Id,string Name,List<FleetPlayer> Players);
public sealed record FleetPlayer(string SteamId64,string Name,string Session,string[] Actions);
public sealed record FleetAuthorization(string ActorSteamid,string TargetSession,DateTimeOffset ExpiresAt);
public sealed record FleetRequest(string Id,string ActorSteamid,string ActorSession,int TargetServer,string TargetSteamid,string TargetSession,string Action,Dictionary<string,JsonElement> Payload,DateTimeOffset CreatedAt);
public sealed record FleetResult(string Id,string ActorSteamid,string ActorSession,string State,bool Success,string Message);
internal sealed record FleetReceipt(string Hash,bool Complete,bool Success,string Message);

public sealed partial class Plugin
{
    private readonly object fleetGate=new();
    private Dictionary<string,List<FleetServer>> fleet=[];
    private DateTimeOffset fleetAt;
    private readonly ConcurrentQueue<FleetResult> fleetMessages=new();
    private static readonly HashSet<string> FleetActions=["kick","ban","mute","unmute","gag","ungag","silence","unsilence","slay","respawn","team","freeze","health","armor","private_message"];
    private static bool FleetId(string? id)=>id is {Length:32}&&id.All(c=>char.IsAsciiHexDigit(c)&&!char.IsUpper(c));
    private string FleetDirectory(string name)
    {
        string path=Path.Combine(_root,"fleet-"+name);
        if(new DirectoryInfo(path).LinkTarget is not null)throw new IOException("Verlinktes Verbundverzeichnis.");Directory.CreateDirectory(path);return path;
    }
    private string FleetRoster(ulong actor)
    {
        lock(fleetGate)return JsonSerializer.Serialize(!_unloading&&(DateTimeOffset.UtcNow-fleetAt).TotalSeconds<=12&&fleet.TryGetValue(actor.ToString(),out var rows)?rows:[]);
    }
    private string QueueFleetRequest(ulong actor,string json)
    {
        if(_unloading||json.Length>8192)throw new InvalidOperationException("Verbund nicht bereit.");
        var p=Core.PlayerManager.GetAllPlayers().FirstOrDefault(x=>x.IsValid&&x.IsAuthorized&&!x.IsFakeClient&&x.SteamID==actor)??throw new InvalidOperationException("Adminverbindung nicht mehr gültig.");
        var r=JsonSerializer.Deserialize<FleetRequest>(json)??throw new InvalidDataException("Ungültiger Verbundauftrag.");
        if(!FleetId(r.Id)||!FleetActions.Contains(r.Action)||!AdminActionPolicy.SteamId(r.TargetSteamid)||!AdminActionPolicy.Allowed(r.Action,s=>Core.Permission.PlayerHasPermission(actor,s)))throw new InvalidOperationException("Aktion nicht freigegeben.");
        lock(fleetGate)
        {
            if((DateTimeOffset.UtcNow-fleetAt).TotalSeconds>12||!fleet.TryGetValue(actor.ToString(),out var servers)||!servers.Any(s=>s.Id==r.TargetServer&&s.Players.Any(t=>t.SteamId64==r.TargetSteamid&&t.Session==r.TargetSession&&t.Actions.Contains(r.Action))))throw new InvalidOperationException("Server, Spieler oder Berechtigung inzwischen geändert; neu auswählen.");
            r=r with{ActorSteamid=actor.ToString(),ActorSession=p.SessionId.ToString(),CreatedAt=DateTimeOffset.UtcNow};
            string path=Path.Combine(FleetDirectory("outbox"),r.Id+".json");
            if(File.Exists(Path.Combine(FleetDirectory("results"),r.Id+".json"))||File.Exists(path))return "Dieser Auftrag wurde bereits gesendet.";
            WriteNewFleet(path,JsonSerializer.Serialize(r,_json));
        }
        return "Auftrag gesendet; das Ergebnis erscheint im Chat.";
    }
    private static void WriteNewFleet(string path,string text)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(text);
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);file.Write(bytes);file.Flush(true);
    }
    private List<FleetRequest> ReadFleetRequests()
    {
        var result=new List<FleetRequest>();
        lock(fleetGate)foreach(string path in Directory.EnumerateFiles(FleetDirectory("outbox"),"*.json").Order().Take(1000))
        {
            if(result.Count>=20)break;
            try{
            var info=new FileInfo(path);if(info.LinkTarget is not null||info.Length>8192)throw new InvalidDataException("Ungültige Verbundwarteschlange.");
            var r=JsonSerializer.Deserialize<FleetRequest>(File.ReadAllText(path),_json)??throw new InvalidDataException("Ungültiger Verbundauftrag.");
            if(!FleetId(r.Id)||Path.GetFileNameWithoutExtension(path)!=r.Id)throw new InvalidDataException("Verbundauftrag passt nicht zum Dateinamen.");result.Add(r);
            }catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException){
                if(fleetReadErrors.Add(path))Core.Logger.LogWarning("Ungültige Verbunddatei {File} bleibt zur Prüfung erhalten; zentraler Austausch läuft weiter.",Path.GetFileName(path));
            }
        }
        return result;
    }
    private readonly HashSet<string> fleetReadErrors=[];
    private List<FleetRequest> SafeFleetRequests()
    {
        try{return ReadFleetRequests();}
        catch(Exception e){Core.Logger.LogWarning(e,"Verbundwarteschlange nicht lesbar; übriger zentraler Austausch läuft weiter.");return [];}
    }
    private void ApplyFleetSnapshot(Dictionary<string,List<FleetServer>> next,List<FleetResult> results)
    {
        lock(fleetGate)
        {
            fleet=next;fleetAt=DateTimeOffset.UtcNow;
            foreach(var r in results.Where(r=>r.State is "complete" or "rejected"))
            {
                if(!FleetId(r.Id))continue;
                string source=Path.Combine(FleetDirectory("outbox"),r.Id+".json"),destination=Path.Combine(FleetDirectory("results"),r.Id+".json");
                if(!File.Exists(source))continue;
                var request=JsonSerializer.Deserialize<FleetRequest>(File.ReadAllText(source),_json);
                if(request?.ActorSteamid!=r.ActorSteamid||request.ActorSession!=r.ActorSession)continue;
                if(!File.Exists(destination)){WriteNewFleet(destination,JsonSerializer.Serialize(r,_json));fleetMessages.Enqueue(r);}
                File.Delete(source);
            }
        }
    }
    private void DeliverFleetResults()
    {
        while(fleetMessages.TryDequeue(out var result))
        {
            var actor=Core.PlayerManager.GetAllPlayers().FirstOrDefault(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID.ToString()==result.ActorSteamid&&p.SessionId.ToString()==result.ActorSession);
            actor?.SendChat("[LPLK Verbund] "+(result.Success?"Erfolgreich: ":"Abgewiesen: ")+Limit(result.Message,180));
        }
    }
    // Durable at-most-once execution. A crash after claiming but before recording
    // completion is reported as uncertain, never replayed against a new session.
    private void Execute(BridgeCommand command)
    {
        if(command.Fleet is not {} auth){ExecuteCore(command);return;}
        if(!command.Id.StartsWith("fleet_",StringComparison.Ordinal)||command.Id.Length>80||command.Id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new InvalidDataException("Ungültige Verbund-ID.");
        string path=Path.Combine(FleetDirectory("receipts"),command.Id+".json");
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command,_json))));
        if(File.Exists(path))
        {
            if(new FileInfo(path).LinkTarget is not null)throw new IOException("Verlinkter Verbundbeleg.");
            var prior=JsonSerializer.Deserialize<FleetReceipt>(File.ReadAllText(path),_json);
            if(prior?.Hash!=hash)throw new InvalidDataException("Abweichende Verbundwiederholung.");
            if(!prior.Complete)throw new InvalidOperationException("Ausführung nach Unterbrechung unklar; manuell prüfen. Kein erneuter Versuch.");
            if(!prior.Success)throw new InvalidOperationException(prior.Message);return;
        }
        if(!FleetActions.Contains(command.Action)||auth.ExpiresAt<DateTimeOffset.UtcNow||auth.ExpiresAt>DateTimeOffset.UtcNow.AddSeconds(45)||!AdminActionPolicy.SteamId(auth.ActorSteamid)||!AdminActionPolicy.SteamId(command.TargetSteamid))throw new InvalidOperationException("Verbundauftrag ungültig oder abgelaufen.");
        ulong actor=ulong.Parse(auth.ActorSteamid),target=ulong.Parse(command.TargetSteamid!);
        if(!AdminActionPolicy.Allowed(command.Action,s=>Core.Permission.PlayerHasPermission(actor,s))||!AdminActionPolicy.CanTarget(actor,target,AdminActionPolicy.Rank(s=>Core.Permission.PlayerHasPermission(actor,s)),AdminActionPolicy.Rank(s=>Core.Permission.PlayerHasPermission(target,s))))throw new InvalidOperationException("Berechtigung auf dem Zielserver fehlt oder Ziel ist geschützt.");
        var player=RequirePlayer(target);if(!player.IsAuthorized||player.IsFakeClient||player.SessionId.ToString()!=auth.TargetSession)throw new InvalidOperationException("Zielspieler hat den Server verlassen oder sich neu verbunden.");
        WriteNewFleet(path,JsonSerializer.Serialize(new FleetReceipt(hash,false,false,"Ausführung gestartet"),_json));
        try{ExecuteCore(command);AtomicWrite(path,JsonSerializer.Serialize(new FleetReceipt(hash,true,true,"Aktion ausgeführt."),_json));}
        catch(Exception e){AtomicWrite(path,JsonSerializer.Serialize(new FleetReceipt(hash,true,false,Limit(e.Message,300)),_json));throw;}
    }
}
