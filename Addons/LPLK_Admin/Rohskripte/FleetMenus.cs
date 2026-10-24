using System.Text.Json;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

internal sealed record FleetMenuServer(int Id,string Name,FleetMenuPlayer[] Players);
internal sealed record FleetMenuPlayer(string SteamId64,string Name,string Session,string[] Actions);

public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Delegate>? FleetApi()=>active&&interfaces is not null&&interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Bridge.Fleet.v1",out var api)?api:null;
    private FleetMenuServer[] FleetServers(IPlayer p)
    {
        if(!CanManage(p)||FleetApi() is not {} api||!api.TryGetValue("roster",out var call)||call is not Func<ulong,string> get)return [];
        return JsonSerializer.Deserialize<FleetMenuServer[]>(get(p.SteamID))??[];
    }
    private void OtherServers(IPlayer p)=>Menu(p,"Andere Server",a=>FleetServers(a).Select(s=>(s.Name,(Action<IPlayer>)(b=>OtherPlayers(b,s.Id)))),PlayerManagement,CanManage);
    private FleetMenuServer FleetServer(IPlayer p,int id)=>FleetServers(p).FirstOrDefault(s=>s.Id==id)??throw new InvalidOperationException("Server nicht mehr verfügbar oder Berechtigung entzogen.");
    private FleetMenuPlayer FleetTarget(IPlayer p,int server,string steam,string session)=>FleetServer(p,server).Players.FirstOrDefault(t=>t.SteamId64==steam&&t.Session==session)??throw new InvalidOperationException("Spieler hat den Server verlassen oder sich neu verbunden.");
    private void OtherPlayers(IPlayer p,int server)
    {
        var current=FleetServer(p,server);
        Menu(p,current.Name,a=>FleetServer(a,server).Players.Select(t=>(t.Name,(Action<IPlayer>)(b=>OtherActions(b,server,t.SteamId64,t.Session)))),OtherServers,CanManage);
    }
    private void OtherActions(IPlayer p,int server,string steam,string session)
    {
        var target=FleetTarget(p,server,steam,session);Action<IPlayer> here=a=>OtherActions(a,server,steam,session);
        Menu(p,target.Name,a=>
        {
            var allowed=FleetTarget(a,server,steam,session).Actions;var rows=new List<(string,Action<IPlayer>)>();
            void Add(string label,string action,Dictionary<string,object>? payload=null)
            {
                if(!allowed.Contains(action)||!Can(a,action))return;
                rows.Add((label,b=>
                {
                    if(action is "kick" or "ban" or "mute" or "gag" or "silence")
                        Input(b,"Grund",(c,reason)=>
                        {
                            if(action=="kick")FleetConfirm(c,server,steam,session,label,action,new(){["reason"]=reason},here);
                            else Menu(c,"Dauer",_=>settings.Durations.Select(minutes=>(minutes==0?"Dauerhaft":minutes+" Minuten",(Action<IPlayer>)(d=>FleetConfirm(d,server,steam,session,label,action,new(){["reason"]=reason,["minutes"]=minutes},here)))),here,CanManage);
                        },here);
                    else if(action=="private_message")Input(b,"Nachricht",(c,text)=>FleetConfirm(c,server,steam,session,label,action,new(){["message"]=text,["type"]="chat"},here),here);
                    else FleetConfirm(b,server,steam,session,label,action,payload??[],here);
                }));
            }
            Add("Kicken","kick");Add("Bannen","ban");Add("Mute","mute");Add("Unmute","unmute");Add("Gag","gag");Add("Ungag","ungag");Add("Stummschalten","silence");Add("Freischalten","unsilence");
            Add("Team T","team",new(){["team"]="t"});Add("Team CT","team",new(){["team"]="ct"});Add("Zuschauer","team",new(){["team"]="spectator"});
            Add("Töten","slay");Add("Respawn","respawn");Add("Einfrieren","freeze",new(){["frozen"]=true});Add("Freigeben","freeze",new(){["frozen"]=false});
            Add("Heilen","health");Add("Rüsten","armor");Add("Nachricht","private_message");return rows;
        },a=>OtherPlayers(a,server),CanManage);
    }
    private void FleetConfirm(IPlayer p,int server,string steam,string session,string label,string action,Dictionary<string,object> payload,Action<IPlayer> back)
    {
        var target=FleetTarget(p,server,steam,session);string id=Guid.NewGuid().ToString("N");bool sent=false;
        p.SendChat("[LPLK] "+label+": "+target.Name+" auf "+FleetServer(p,server).Name+(payload.TryGetValue("reason",out var reason)?" · "+reason:"")+(payload.TryGetValue("minutes",out var duration)?" · "+duration+" Minuten (0 = dauerhaft)":""));
        Menu(p,label+"?",_=>[("Ja",a=>
        {
            if(sent)throw new InvalidOperationException("Auftrag bereits gesendet.");
            var current=FleetTarget(a,server,steam,session);if(!Can(a,action)||!current.Actions.Contains(action))throw new InvalidOperationException("Berechtigung entzogen.");
            if(FleetApi() is not {} api||!api.TryGetValue("request",out var call)||call is not Func<ulong,string,string> send)throw new InvalidOperationException("Verbund nicht bereit.");
            string response=send(a.SteamID,JsonSerializer.Serialize(new{Id=id,ActorSteamid="",ActorSession="",TargetServer=server,TargetSteamid=steam,TargetSession=session,Action=action,Payload=payload,CreatedAt=DateTimeOffset.UtcNow}));sent=true;a.SendChat("[LPLK] "+response);OtherPlayers(a,server);
        }),("Nein",back)],back,CanManage);
    }
}
