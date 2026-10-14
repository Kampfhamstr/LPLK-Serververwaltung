using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    [Command("kick")] public void Kick(ICommandContext c)=>PlayerCommand(c,"kick");
    [Command("ban")][CommandAlias("addban")] public void Ban(ICommandContext c)=>PlayerCommand(c,"ban");
    [Command("unban")] public void Unban(ICommandContext c)=>PlayerCommand(c,"unban");
    [Command("mute")] public void Mute(ICommandContext c)=>PlayerCommand(c,"mute");
    [Command("unmute")] public void Unmute(ICommandContext c)=>PlayerCommand(c,"unmute");
    [Command("gag")] public void Gag(ICommandContext c)=>PlayerCommand(c,"gag");
    [Command("ungag")] public void Ungag(ICommandContext c)=>PlayerCommand(c,"ungag");
    [Command("silence")] public void Silence(ICommandContext c)=>PlayerCommand(c,"silence");
    [Command("unsilence")] public void Unsilence(ICommandContext c)=>PlayerCommand(c,"unsilence");
    [Command("slay")] public void Slay(ICommandContext c)=>PlayerCommand(c,"slay");
    [Command("respawn")][CommandAlias("1up")] public void Respawn(ICommandContext c)=>PlayerCommand(c,"respawn");
    [Command("team")] public void Team(ICommandContext c)=>PlayerCommand(c,"team");
    [Command("freeze")] public void Freeze(ICommandContext c)=>PlayerCommand(c,"freeze");
    [Command("unfreeze")] public void Unfreeze(ICommandContext c)=>PlayerCommand(c,"freeze",false);
    [Command("noclip")] public void Noclip(ICommandContext c)=>PlayerCommand(c,"noclip");
    [Command("goto")] public void Goto(ICommandContext c)=>PlayerCommand(c,"goto_player");
    [Command("bring")] public void Bring(ICommandContext c)=>PlayerCommand(c,"bring_player");
    [Command("health")] public void Health(ICommandContext c)=>PlayerCommand(c,"health");
    [Command("armor")] public void Armor(ICommandContext c)=>PlayerCommand(c,"armor");
    [Command("psay")] public void Psay(ICommandContext c)=>PlayerCommand(c,"private_message");

    private string[] Targets(ICommandContext c,string token,bool offline)
    {
        var players=Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized).ToArray();
        if(AdminActionPolicy.SteamId(token)) { if(!offline&&Find(ulong.Parse(token)) is null)throw new ArgumentException("Spieler nicht verbunden.");return [token]; }
        if(token=="@me"&&c.Sender is {} self)return [self.SteamID.ToString()];
        var filtered=token.ToLowerInvariant() switch {
            "@all" or "@humans"=>players,
            "@t"=>players.Where(p=>p.Controller.TeamNum==2).ToArray(),
            "@ct"=>players.Where(p=>p.Controller.TeamNum==3).ToArray(),
            "@alive"=>players.Where(p=>p.PlayerPawn is {IsValid:true,Health:>0}).ToArray(),
            "@dead"=>players.Where(p=>p.PlayerPawn is not {IsValid:true,Health:>0}).ToArray(),
            _=>null};
        if(filtered is not null){if(filtered.Length==0)throw new ArgumentException("Keine passenden Spieler.");return filtered.Select(p=>p.SteamID.ToString()).ToArray();}
        if(token.StartsWith('#')&&int.TryParse(token[1..],out int slot))filtered=players.Where(p=>p.PlayerID==slot).ToArray();
        else { filtered=players.Where(p=>string.Equals(p.Controller.PlayerName,token,StringComparison.OrdinalIgnoreCase)).ToArray();if(filtered.Length==0)filtered=players.Where(p=>p.Controller.PlayerName.Contains(token,StringComparison.OrdinalIgnoreCase)).ToArray(); }
        if(filtered.Length!=1)throw new ArgumentException("Spieler fehlt oder Name ist mehrdeutig. SteamID64 oder #Slot verwenden.");
        return [filtered[0].SteamID.ToString()];
    }
    private void PlayerCommand(ICommandContext c,string action,bool enabled=true)=>Guard(()=>
    {
        if(c.Sender is {} actor&&!Can(actor,action))throw new InvalidOperationException("Keine Berechtigung.");
        if(c.Args.Length<1)throw new ArgumentException("Ziel fehlt: Name, SteamID64, #Slot oder @me.");
        bool timed=action is "ban" or "mute" or "gag" or "silence";
        var payload=new Dictionary<string,object>();
        if(timed){if(c.Args.Length<2)throw new ArgumentException("Dauer in Minuten fehlt; 0 = dauerhaft.");payload["minutes"]=AdminActionPolicy.Minutes(c.Args[1]);payload["reason"]=c.Args.Length>2?AdminActionPolicy.Text(string.Join(' ',c.Args.Skip(2)),150):"Regelverstoß";}
        if(action=="kick")payload["reason"]=c.Args.Length>1?AdminActionPolicy.Text(string.Join(' ',c.Args.Skip(1)),150):"Regelverstoß";
        if(action=="team")payload["team"]=c.Args.ElementAtOrDefault(1)?.ToLowerInvariant() switch {"t" or "2"=>"t","ct" or "3"=>"ct","spec" or "spectator" or "1"=>"spectator",_=>throw new ArgumentException("Team: t, ct oder spec.")};
        if(action=="freeze"){payload["frozen"]=enabled;payload["seconds"]=c.Args.Length>1&&int.TryParse(c.Args[1],out var seconds)&&seconds>=0&&seconds<=3600?seconds:c.Args.Length<=1?0:throw new ArgumentException("Sekunden: 0–3600.");}
        if(action=="noclip")payload["enabled"]=c.Args.ElementAtOrDefault(1)?.ToLowerInvariant() switch {"off" or "0"=>false,"on" or "1" or null=>true,_=>throw new ArgumentException("Noclip: on oder off.")};
        if(action is "health" or "armor")payload["value"]=c.Args.Length==2&&int.TryParse(c.Args[1],out var value)&&value>=(action=="health"?1:0)&&value<=500?value:throw new ArgumentException("Wert fehlt oder liegt außerhalb 0/1–500.");
        if(action=="private_message"){payload["message"]=AdminActionPolicy.Text(string.Join(' ',c.Args.Skip(1)));payload["type"]="chat";}
        var targets=Targets(c,c.Args[0],timed||action is "unban" or "unmute" or "ungag" or "unsilence");
        // Validate every immunity before a grouped action so it cannot partly affect protected players.
        if(c.Sender is {} a)foreach(var id in targets)if(!AdminActionPolicy.CanTarget(a.SteamID,ulong.Parse(id),AdminActionPolicy.Rank(p=>Has(a,p)),AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(ulong.Parse(id),p))))throw new InvalidOperationException("Mindestens ein Ziel hat gleiche/höhere Adminrechte.");
        foreach(var id in targets)Run(c.Sender,action,id,payload,c.Reply);
    },c.Reply);

    [Command("rr")][CommandAlias("restart")]
    public void Restart(ICommandContext c)=>Guard(()=>Run(c.Sender,"round_restart",null,new(){["countdown_seconds"]=5},c.Reply),c.Reply);
    [Command("say")] public void Say(ICommandContext c)=>Broadcast(c,"chat");
    [Command("csay")] public void Csay(ICommandContext c)=>Broadcast(c,"center");
    [Command("hsay")] public void Hsay(ICommandContext c)=>Broadcast(c,"hint");
    private void Broadcast(ICommandContext c,string type)=>Guard(()=>Run(c.Sender,"broadcast",null,new(){["message"]=AdminActionPolicy.Text(string.Join(' ',c.Args)),["type"]=type},c.Reply),c.Reply);
    [Command("asay")]
    public void Asay(ICommandContext c)=>Guard(()=>{if(c.Sender is {} sender&&!Can(sender,"broadcast"))throw new InvalidOperationException("Keine Berechtigung.");string text=AdminActionPolicy.Text(string.Join(' ',c.Args));foreach(var p in Core.PlayerManager.GetAllPlayers().Where(CanManage))p.SendChat("[LPLK Admin] "+(c.Sender?.Controller.PlayerName??"Konsole")+": "+text);c.Reply("[LPLK] Adminnachricht versendet.");},c.Reply);
    [Command("rcon")]
    public void Rcon(ICommandContext c)=>Guard(()=>Run(c.Sender,"server_command",null,new(){["command"]=AdminActionPolicy.Text(string.Join(' ',c.Args),500)},c.Reply),c.Reply);
    [Command("cvar")]
    public void Cvar(ICommandContext c)=>Guard(()=>{if(c.Args.Length!=2)throw new ArgumentException("cvar <Name> <Wert>");Run(c.Sender,"cvar_set",null,new(){["name"]=c.Args[0],["value"]=c.Args[1]},c.Reply);},c.Reply);
    [Command("players")][CommandAlias("list")]
    public void Players(ICommandContext c)=>Guard(()=>{if(c.Sender is {} p&&!CanManage(p))throw new InvalidOperationException("Keine Berechtigung.");foreach(var t in Core.PlayerManager.GetAllPlayers().Where(t=>t.IsValid&&!t.IsFakeClient))c.Reply($"#{t.PlayerID} {t.Controller.PlayerName} | {t.SteamID} | Team {t.Controller.TeamNum}");},c.Reply);
    [Command("who")]
    public void Who(ICommandContext c)=>Guard(()=>{if(c.Sender is {} p&&!CanManage(p))throw new InvalidOperationException("Keine Berechtigung.");if(c.Args.Length!=1)throw new ArgumentException("who <Spieler>");foreach(var id in Targets(c,c.Args[0],false)){var t=Target(id,null);c.Reply($"{t.Controller.PlayerName} | {id} | Rechte: "+string.Join(", ",Core.Permission.GetPlayerPermissions(t.SteamID)));}},c.Reply);
}
