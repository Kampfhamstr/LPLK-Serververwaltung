using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace LPLK_Admin;

// Names are compatible with T3's Commands section. Rights stay in the existing
// action policy and Bridge; aliases never carry an independent permission grant.
public static class AdminCommandNames
{
    public static readonly IReadOnlyDictionary<string,string[]> Groups=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["AdminMenu"]=["admin","adminmenu"],["Asay"]=["asay"],["Say"]=["say"],["Psay"]=["psay"],["Csay"]=["csay"],["Hsay"]=["hsay"],
        ["Ban"]=["ban"],["AddBan"]=["addban"],["Unban"]=["unban"],["Kick"]=["kick"],
        ["Gag"]=["gag"],["Ungag"]=["ungag"],["Mute"]=["mute"],["Unmute"]=["unmute"],["Silence"]=["silence"],["Unsilence"]=["unsilence"],
        ["Slay"]=["slay"],["Respawn"]=["respawn","1up"],["ChangeTeam"]=["team"],["NoClip"]=["noclip"],
        ["Goto"]=["goto"],["Bring"]=["bring"],["Freeze"]=["freeze"],["Unfreeze"]=["unfreeze"],
        ["RestartGame"]=["rr","restart"],["Rcon"]=["rcon"],["Cvar"]=["cvar"],["ListPlayers"]=["players","list"],["Who"]=["who"],
        ["Health"]=["health"],["Armor"]=["armor"],
        ["AddAdmin"]=["addadmin"],["RemoveAdmin"]=["removeadmin"],["ListAdmins"]=["listadmins","admins"],
        ["ChangeMap"]=["admin_map"],["ChangeWSMap"]=["wsmap"]
    };
    // These belong to the map service or the existing central role management.
    public static readonly string[] Unsupported=[];
    private static readonly HashSet<string> Reserved=new(["menu","lplk_admin_commands",
        "say_team","quit","exit","exec","execifexists","changelevel","host_workshop_map","connect","disconnect","status","bind","unbind","alias","sv_password","rcon_password"],StringComparer.OrdinalIgnoreCase);
    public static string Normalize(string? value)
    {
        if(value is null)throw new InvalidDataException("Befehlsname fehlt.");
        value=value.Trim().ToLowerInvariant();
        if(value.StartsWith("sw_",StringComparison.Ordinal))value=value[3..];
        if(value.Length is <1 or >40||value.StartsWith("sw_",StringComparison.Ordinal)||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-' and not '.'))
            throw new InvalidDataException("Ungültiger Befehlsname.");
        return value;
    }
    public static Dictionary<string,string> Wanted(Dictionary<string,string[]> configured)
    {
        if(configured is null||configured.Count>Groups.Count+Unsupported.Length||configured.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=configured.Count)
            throw new InvalidDataException("Ungültige Befehlsgruppen.");
        var wanted=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var group in Groups)foreach(var name in group.Value)wanted.Add(name,group.Key);
        foreach(var pair in configured)
        {
            var key=Groups.Keys.FirstOrDefault(k=>k.Equals(pair.Key,StringComparison.OrdinalIgnoreCase));
            bool unsupported=Unsupported.Contains(pair.Key,StringComparer.OrdinalIgnoreCase);
            if(key is null&&!unsupported)throw new InvalidDataException("Unbekannte Befehlsgruppe: "+pair.Key);
            if(pair.Value is null||pair.Value.Length>16)throw new InvalidDataException("Zu viele oder ungültige Befehlsnamen.");
            foreach(var raw in pair.Value)
            {
                string name=Normalize(raw);
                if(unsupported)continue; // Preserve the field, never silently grant a role/map action.
                if(Reserved.Contains(name)||name.StartsWith("lplk_",StringComparison.Ordinal))throw new InvalidDataException("Befehlsname ist reserviert: "+name);
                if(wanted.TryGetValue(name,out var owner)&&owner!=key)throw new InvalidDataException("Befehlsname gehört zu anderer Aktion: "+name);
                wanted[name]=key!;
            }
        }
        return wanted;
    }
    public static bool Same(Dictionary<string,string[]> a,Dictionary<string,string[]> b)
    {
        var aa=Wanted(a);var bb=Wanted(b);
        return aa.Count==bb.Count&&aa.All(p=>bb.TryGetValue(p.Key,out var value)&&p.Value==value);
    }
}

public sealed partial class Plugin
{
    private sealed class AdminRegistration(string name,string group)
    { public readonly string Name=name;public string Group=group;public Guid Id;public bool Active; }
    private readonly Dictionary<string,AdminRegistration> aliasRegistrations=new(StringComparer.Ordinal);
    private readonly List<AdminRegistration> retiredAliases=[];
    private string? commandError;
    private readonly HashSet<string> fixedCommandNames=AdminCommandNames.Groups.Values.SelectMany(x=>x).Select(x=>"sw_"+x)
        .Concat(["sw_menu","sw_lplk_admin_commands"]).ToHashSet(StringComparer.Ordinal);
    private void InvokeAlias(string group,ICommandContext c)
    {
        switch(group)
        {
            case "AdminMenu":Admin(c);break;case "Asay":Asay(c);break;case "Say":Say(c);break;case "Psay":Psay(c);break;
            case "Csay":Csay(c);break;case "Hsay":Hsay(c);break;case "Ban":Ban(c);break;case "AddBan":Ban(c);break;case "Unban":Unban(c);break;
            case "Kick":Kick(c);break;case "Gag":Gag(c);break;case "Ungag":Ungag(c);break;case "Mute":Mute(c);break;case "Unmute":Unmute(c);break;
            case "Silence":Silence(c);break;case "Unsilence":Unsilence(c);break;case "Slay":Slay(c);break;case "Respawn":Respawn(c);break;
            case "ChangeTeam":Team(c);break;case "NoClip":Noclip(c);break;case "Goto":Goto(c);break;case "Bring":Bring(c);break;
            case "Freeze":Freeze(c);break;case "Unfreeze":Unfreeze(c);break;case "RestartGame":Restart(c);break;case "Rcon":Rcon(c);break;
            case "Cvar":Cvar(c);break;case "ListPlayers":Players(c);break;case "Who":Who(c);break;case "Health":Health(c);break;case "Armor":Armor(c);break;
            case "AddAdmin":AddAdmin(c);break;case "RemoveAdmin":RemoveAdmin(c);break;case "ListAdmins":ListAdmins(c);break;
            case "ChangeMap":LegacyMap(c);break;case "ChangeWSMap":LegacyWorkshop(c);break;
            default:throw new InvalidOperationException("Nicht unterstützte Adminaktion.");
        }
    }
    private bool ReleaseAlias(AdminRegistration entry)
    {
        entry.Active=false;
        try{Core.Command.UnregisterCommand(entry.Id);return true;}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Admin: Alter Befehlsname ist gesperrt, konnte aber noch nicht entfernt werden.");return false;}
    }
    // A matching prefixed plugin command does not prove that the bare engine
    // name is free. Never override a positive native occupancy result.
    // sw_map belongs to Maps even when that plugin has not loaded yet. Native
    // discovery alone is insufficient for configured aliases during cold start.
    private bool CommandNameOccupied(string name)=>name is "help" or "addons" or "stats" or "say" or "say_team" or "kick" or "noclip" or "rcon" or "map" or "restart" or "quit" or "exit" or "status" or "exec" or "changelevel" or "host_workshop_map" or "sw_map" || Core.Command.IsCommandRegistered(name);
    private void ReconcileAdminCommands()
    {
        if(!active)return;
        foreach(var old in retiredAliases.ToArray())if(ReleaseAlias(old))retiredAliases.Remove(old);
        var wanted=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in AdminCommandNames.Wanted(settings.Commands))
            foreach(string name in new[]{pair.Key,"sw_"+pair.Key})if(!fixedCommandNames.Contains(name))wanted[name]=pair.Value;
        foreach(var old in aliasRegistrations.Values.Where(e=>!wanted.ContainsKey(e.Name)).ToArray())
        {aliasRegistrations.Remove(old.Name);if(!ReleaseAlias(old))retiredAliases.Add(old);}
        var failures=new List<string>();
        foreach(var pair in wanted)
        {
            if(aliasRegistrations.TryGetValue(pair.Key,out var existing)){existing.Group=pair.Value;continue;}
            if(CommandNameOccupied(pair.Key)){failures.Add(pair.Key);continue;}
            var entry=new AdminRegistration(pair.Key,pair.Value);
            try
            {
                entry.Id=Core.Command.RegisterCommand(entry.Name,c=>
                {
                    if(!active||!entry.Active)return;
                    string group=entry.Group;ReloadSettings();
                    if(!active||!entry.Active||entry.Group!=group||!aliasRegistrations.TryGetValue(entry.Name,out var current)||!ReferenceEquals(entry,current))
                    {c.ReplyLplk("[LPLK] Befehlszuordnung geändert; erneut aufrufen.");return;}
                    InvokeAlias(group,c);
                },registerRaw:true,permission:"");
                if(entry.Id==Guid.Empty)throw new InvalidOperationException("Leere Befehlsregistrierung.");
                if(!Core.Command.IsCommandRegistered(entry.Name))throw new InvalidOperationException("Befehlsregistrierung nicht bestätigt.");
                aliasRegistrations.Add(entry.Name,entry);entry.Active=true;
            }
            catch(Exception e)
            {
                if(entry.Id!=Guid.Empty&&!ReleaseAlias(entry))retiredAliases.Add(entry);
                failures.Add(entry.Name);Core.Logger.LogWarning(e,"LPLK_Admin: Zusatzbefehl nicht registriert: {Command}",entry.Name);
            }
        }
        string? next=failures.Count>0?"Befehlsnamen belegt/nicht verfügbar: "+string.Join(", ",failures):retiredAliases.Count>0?"Alte Befehle sind gesperrt; Entfernung wird wiederholt.":null;
        if(next!=commandError&&next is not null)Core.Logger.LogWarning("LPLK_Admin: {CommandError}",next);
        commandError=next;
    }
    private void RemoveAdminCommands()
    {
        foreach(var entry in aliasRegistrations.Values.Concat(retiredAliases).Distinct())ReleaseAlias(entry);
        aliasRegistrations.Clear();retiredAliases.Clear();
    }
    [Command("lplk_admin_commands")]
    public void CommandStatus(ICommandContext c)
    {
        if(!active)return;
        if(c.Sender is {} p&&!CanManage(p)){c.ReplyLplk("[LPLK] Keine Berechtigung.");return;}
        ReloadSettings();
        c.ReplyLplk("LPLK_Admin commands: "+JsonSerializer.Serialize(new{version="0.11.11",registered=aliasRegistrations.Keys.Order().ToArray(),
            unsupported=settings.Commands.Keys.Where(k=>AdminCommandNames.Unsupported.Contains(k,StringComparer.OrdinalIgnoreCase)).Order().ToArray(),commandError,reloadError=settingsError}));
    }

    [Command("lplk_command_audit")]
    public void CommandAudit(ICommandContext c)
    {
        // Read-only server-console diagnostic: names and owners, no handlers,
        // configuration values, permissions, player data or game actions.
        if(c.Sender is not null){c.ReplyLplk("[LPLK] Nur über die Serverkonsole.");return;}
        var groups=Core.Command.GetAllCommandsByPlugin();
        var entries=groups.OrderBy(x=>x.Key).SelectMany(group=>group.Value.Select(info=>new{
            plugin=group.Key,name=info.CommandName,raw=info.RegisterRaw,
            effective=info.RegisterRaw||info.CommandName.StartsWith("sw_",StringComparison.Ordinal)?info.CommandName:"sw_"+info.CommandName
        })).ToArray();
        var collisions=entries.GroupBy(x=>x.effective,StringComparer.OrdinalIgnoreCase)
            .Where(g=>g.Select(x=>x.plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1)
            .Select(g=>new{name=g.Key,owners=g.Select(x=>x.plugin).Distinct().Order().ToArray()}).ToArray();
        // CS2 truncates individual console messages near 2048 bytes. Each
        // numbered chunk stays well below that limit; consumers require all.
        string run=Guid.NewGuid().ToString("N");var chunks=entries.Chunk(8).ToArray();
        for(int index=0;index<chunks.Length;index++)
            c.Reply("LPLK_COMMAND_AUDIT "+JsonSerializer.Serialize(new{version="0.11.11",run,index,total=chunks.Length+1,entries=chunks[index]}));
        c.Reply("LPLK_COMMAND_AUDIT "+JsonSerializer.Serialize(new{version="0.11.11",run,index=chunks.Length,total=chunks.Length+1,
            entries=Array.Empty<object>(),collisions,entryCount=entries.Length,exactCount=Core.Command.GetAllCommands().Count}));
    }
}
