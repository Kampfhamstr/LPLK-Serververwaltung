using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    [Command("addadmin")] public void AddAdmin(ICommandContext c)=>CentralRoles(c,"admin.root");
    [Command("removeadmin")] public void RemoveAdmin(ICommandContext c)=>CentralRoles(c,"admin.root");
    [Command("listadmins")][CommandAlias("admins")] public void ListAdmins(ICommandContext c)=>CentralRoles(c,"admin.menu");
    private void CentralRoles(ICommandContext c,string permission)=>Guard(()=>
    {
        if(c.Sender is {} p&&!Has(p,permission))throw new InvalidOperationException("Keine Berechtigung.");
        c.ReplyLplk("[LPLK] Adminrollen werden zentral unter https://verwaltung.lplk.de verwaltet: Spieler / Rollen. Hier wurden keine Rollen geändert.");
    },c.ReplyLplk);
    private bool LegacyMapAllowed(IPlayer p)=>settings.LegacyPlayerMenus&&(Has(p,"admin.map")||Has(p,"lplk.admin"));
    [Command("map")] public void LegacyMap(ICommandContext c)=>LegacyMapRequest(c,false);
    [Command("wsmap")] public void LegacyWorkshop(ICommandContext c)=>LegacyMapRequest(c,true);
    private void LegacyMapRequest(ICommandContext c,bool workshop)=>Guard(()=>
    {
        ReloadSettings();
        if(!settings.LegacyPlayerMenus||Loaded("LPLK_Maps"))throw new InvalidOperationException("Mapwechsel über !admin > Maps ausführen.");
        if(c.Sender is {} p&&!LegacyMapAllowed(p))throw new InvalidOperationException("Keine Berechtigung.");
        if(c.Args.Length!=1){c.ReplyLplk("[LPLK] "+(workshop?"wsmap <Workshop-ID oder Name>: "+string.Join(", ",settings.LegacyWorkshopMaps.Keys):"map <Mapname>: "+string.Join(", ",settings.LegacyGameMaps.Keys)));return;}
        string argument=c.Args[0];string command;
        if(workshop)
        {
            if(!uint.TryParse(argument,out uint id))
            {
                var matches=settings.LegacyWorkshopMaps.Where(x=>x.Key.Contains(argument,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length!=1)throw new InvalidOperationException("Workshopname fehlt oder ist mehrdeutig.");
                id=matches[0].Value;
            }
            if(id==0)throw new InvalidOperationException("Ungültige Workshop-ID.");
            command="host_workshop_map "+id;
        }
        else
        {
            var match=settings.LegacyGameMaps.Keys.SingleOrDefault(x=>x.Equals(argument,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("Map nicht in der übernommenen Mapliste.");
            command="changelevel "+match;
        }
        ScheduleLegacyMap(c.Sender,command,c.ReplyLplk);
    },c.ReplyLplk);
    private void ScheduleLegacyMap(IPlayer? actor,string command,Action<string> reply)
    {
        int serial=generation;string? session=actor?.SessionId.ToString();
        foreach(var p in Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid))p.SendLplkChat("[LPLK] Mapwechsel in 3 Sekunden: "+command.Split(' ')[1]);
        Core.Scheduler.DelayBySeconds(3,()=>Guard(()=>
        {
            ReloadSettings();
            if(!active||serial!=generation||!settings.LegacyPlayerMenus||Loaded("LPLK_Maps")||actor is not null&&(!LegacyMapAllowed(actor)||actor.SessionId.ToString()!=session))throw new InvalidOperationException("Mapwechsel wegen geänderter Freigabe/Verbindung verworfen.");
            Core.Engine.ExecuteCommand(command);
            Core.Logger.LogInformation("LPLK_Admin: {Actor} requested {MapCommand}",actor?.SteamID.ToString()??"console",command);
        },reply));
        reply("[LPLK] Mapwechsel angefordert.");
    }
    private void LegacyAdminMaps(IPlayer p)=>Menu(p,"Maps",_=>settings.LegacyGameMaps.Select(x=>(x.Value,(Action<IPlayer>)(a=>
        Menu(a,"Mapwechsel?",_=>[("Ja",b=>ScheduleLegacyMap(b,"changelevel "+x.Key,b.SendLplkChat)),("Nein",LegacyAdminMaps)],LegacyAdminMaps,LegacyMapAllowed)))),OpenAdministration,LegacyMapAllowed);
}
