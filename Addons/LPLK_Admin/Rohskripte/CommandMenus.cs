using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private static readonly IReadOnlyDictionary<string,string> CommandGroupLabels=new Dictionary<string,string>
    {
        ["AddAdmin"]="Rolle vergeben",["RemoveAdmin"]="Rolle entfernen",["ListAdmins"]="Adminrollen",["ChangeMap"]="Mapwechsel",["ChangeWSMap"]="Workshopmap",
        ["AdminMenu"]="Adminmenü",["Asay"]="Adminchat",["Say"]="Chatnachricht",["Psay"]="Privatnachricht",["Csay"]="Mittentext",["Hsay"]="Hinweis",
        ["Ban"]="Bannen",["AddBan"]="Offlinebann",["Unban"]="Entbannen",["Kick"]="Kicken",["Gag"]="Text sperren",["Ungag"]="Text freigeben",
        ["Mute"]="Sprache sperren",["Unmute"]="Sprache freigeben",["Silence"]="Beides sperren",["Unsilence"]="Beides freigeben",
        ["Slay"]="Töten",["Respawn"]="Respawn",["ChangeTeam"]="Team",["NoClip"]="Noclip",["Goto"]="Hinteleportieren",["Bring"]="Herholen",
        ["Freeze"]="Einfrieren",["Unfreeze"]="Freigeben",["RestartGame"]="Rundenneustart",["Rcon"]="Serverbefehl",["Cvar"]="ConVar",
        ["ListPlayers"]="Spielerliste",["Who"]="Spielerinfo",["Health"]="Gesundheit",["Armor"]="Rüstung"
    };
    private void CommandGroupsMenu(IPlayer p,int page)=>Menu(p,"Admin · Befehle",_=>
        AdminCommandNames.Groups.Keys.Select(key=>(CommandGroupLabels[key],(Action<IPlayer>)(a=>CommandNamesMenu(a,key,page,0)))),
        SettingsMenu,CanConfigure,page,(a,next)=>CommandGroupsMenu(a,next),scroll:true);
    private static string[] Aliases(AdminSettings s,string group)
    {
        var key=s.Commands.Keys.FirstOrDefault(k=>k.Equals(group,StringComparison.OrdinalIgnoreCase));
        return key is null?[]:s.Commands[key].Select(AdminCommandNames.Normalize).Distinct(StringComparer.Ordinal).ToArray();
    }
    private static AdminSettings WithAliases(AdminSettings s,string group,string[] names)
    {
        var values=s.Commands.Where(p=>!p.Key.Equals(group,StringComparison.OrdinalIgnoreCase)).ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        if(names.Length>0)values[group]=names;
        return s with{Commands=values};
    }
    private void CommandNamesMenu(IPlayer p,string group,int groupsPage,int page)=>Menu(p,"Befehle · "+CommandGroupLabels[group],_=>
    {
        var rows=new List<(string,Action<IPlayer>)>();
        foreach(var name in AdminCommandNames.Groups[group])
            rows.Add((name,a=>a.SendChat("[LPLK] Fester Befehl: !"+name+" / sw_"+name+". Ein freier direkter Konsolenname wird zusätzlich registriert.")));
        var aliases=Aliases(settings,group).Except(AdminCommandNames.Groups[group]).ToArray();
        foreach(var name in aliases)rows.Add((name,a=>CommandAliasMenu(a,group,name,groupsPage,page)));
        if(Aliases(settings,group).Length<16)rows.Add(("Hinzufügen",a=>SettingInput(a,"Befehlsname",43,ValidAlias,
            (b,text)=>ChangeSettings(b,s=>WithAliases(s,group,Aliases(s,group).Append(AdminCommandNames.Normalize(text)).Distinct().ToArray()),
                c=>CommandNamesMenu(c,group,groupsPage,page)),b=>CommandNamesMenu(b,group,groupsPage,page))));
        return rows;
    },a=>CommandGroupsMenu(a,groupsPage),CanConfigure,page,(a,next)=>CommandNamesMenu(a,group,groupsPage,next),scroll:true);
    private static bool ValidAlias(string value){try{AdminCommandNames.Normalize(value);return true;}catch(InvalidDataException){return false;}}
    private void CommandAliasMenu(IPlayer p,string group,string name,int groupsPage,int page)=>Menu(p,"Befehl · "+name,_=>
        new (string,Action<IPlayer>)[]{("Entfernen",a=>Menu(a,"Befehl entfernen?",_=>new (string,Action<IPlayer>)[]
        {("Bestätigen",b=>ChangeSettings(b,s=>WithAliases(s,group,Aliases(s,group).Where(x=>x!=name).ToArray()),c=>CommandNamesMenu(c,group,groupsPage,page)))},
        b=>CommandAliasMenu(b,group,name,groupsPage,page),CanConfigure))},
        a=>CommandNamesMenu(a,group,groupsPage,page),CanConfigure);
}
