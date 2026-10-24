using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Teams;

public enum SelectionFilter { All, Active, Inactive }
public sealed partial class Plugin
{
    public static bool SelectionMatches(SelectionFilter filter,bool active)=>filter==SelectionFilter.All || (filter==SelectionFilter.Active)==active;
    // Existing module rights and target immunity remain authoritative. No role migration here.
    private bool CanUseSelection(IPlayer p)
    {
        if(!loaded)return false;
        ReloadSettings();
        return module.CanAdmin(p)&&settings.EnableLosingTeamMenu&&reloadError is null;
    }
    [Command("tsw")]
    public void LegacySelectionCommand(ICommandContext c)
    {
        if(c.Sender is {} p){PlayersRoot(p);return;}
        AdminCommand(c);
    }
    private void PlayersRoot(IPlayer p)=>CompactMenu.Open(Core,p,"Teams · Verliererteam",()=>loaded,CanUseSelection,_=>new (string,Action<IPlayer>)[]{
        ($"Aktiv: {settings.Enabled}",a=>{Set(a,nameof(TeamSettings.Enabled),false,toggle:true);PlayersRoot(a);}),
        ("Ausgewählte",a=>PlayersMenu(a,0,SelectionFilter.Active)),
        ("Nicht ausgewählte",a=>PlayersMenu(a,0,SelectionFilter.Inactive)),
        ("Alle Spieler",a=>PlayersMenu(a,0))},OpenAdmin);
    private void TextSettings(IPlayer p)=>OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TextSettingRows()=>new (string,Action<IPlayer>)[]{
        ("Chatpräfix",a=>TextInput(a,nameof(TeamSettings.ChatPrefix),"Chatpräfix",128)),
        ("Chatmeldung",a=>TextInput(a,nameof(TeamSettings.BlockedMessage),"Chatmeldung",512)),
        ("Bildschirmmeldung",a=>TextInput(a,nameof(TeamSettings.CenterMessage),"Bildschirmmeldung",512)),
        ("Teambefehle",a=>TextInput(a,nameof(TeamSettings.BlockedCommands),"Teambefehle",2079))};
    public static bool ProfileInput(string name,string text,int max,out object value)
    {
        value=text;
        if(!TeamSettings.MessageText(text,max))return false;
        if(name!=nameof(TeamSettings.BlockedCommands))return true;
        var commands=text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(commands.Length>32||commands.Any(c=>!TeamSettings.CommandName(c))||commands.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=commands.Length)return false;
        value=commands;return true;
    }
    private void TextInput(IPlayer p,string name,string label,int max)
    {
        if(!module.CanAdmin(p))return;
        string value=name==nameof(TeamSettings.BlockedCommands)?string.Join(", ",settings.BlockedCommands):(string)typeof(TeamSettings).GetProperty(name)!.GetValue(settings)!;
        var input=new InputMenuOption(new MenuNumbers().Next(label),maxLength:max+1,defaultValue:value,
            validator:text=>ProfileInput(name,text,max,out _),hintMessage:name==nameof(TeamSettings.BlockedCommands)?"Befehlsnamen mit Komma trennen. Leer = keine abgefangenen Teambefehle.":"Vorhandenen Text ändern oder leer lassen.");
        GameThreadMenu.BindInput(Core,input,p,()=>loaded,(a,text)=>{if(module.CanAdmin(a)&&ProfileInput(name,text,max,out var next)){Set(a,name,next);TextSettings(a);}});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>loaded,a=>{if(module.CanAdmin(a))TextSettings(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(label)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
}
