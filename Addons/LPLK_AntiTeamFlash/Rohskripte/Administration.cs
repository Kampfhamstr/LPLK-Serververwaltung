using Microsoft.Extensions.Logging;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_AntiTeamFlash;

public sealed partial class Plugin
{
    [Command("lplk_antiteamflash",permission:"lplk.antiteamflash.admin")]
    public void Command(ICommandContext context)
    {
        if(context.Sender is {} player){OpenAdmin(player);return;}
        WriteStatus();
        context.Reply($"LPLK_AntiTeamFlash 0.1.3: enabled={settings.Enabled}; flashOwner={settings.FlashOwner}; permission={settings.AccessPermission}; ready={ready}; cleared={cleared}; preserved={preserved}; errors={errors}; reloadError={reloadError??"none"}");
    }
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p) => Page(p,"LPLK · Teamflash-Schutz",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Berechtigung",PermissionMenuRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{("Status",a=>a.SendChat($"[LPLK] Teamflash: {(settings.Enabled?"AN":"AUS")}; bereit={ready}; verhindert={cleared}; Überschneidungen erhalten={preserved}; Fehler={errors}; Konfiguration={reloadError??"gültig"}.")),("Erklärung",a=>a.SendChat("[LPLK] Gegnerblendung bleibt wirksam. Bei überlappenden Blendungen bleibt die erlaubte Blendung erhalten; dabei kann auch die Teamflash wirksam bleiben. FFA behandelt Mitspieler als Gegner."))}))(p);
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Aktiv: {settings.Enabled}",a=>Apply(a,s=>s with{Enabled=!s.Enabled})),        ($"Eigenblendung: {settings.FlashOwner}",a=>Apply(a,s=>s with{FlashOwner=!s.FlashOwner}))}))(p);
    private void PermissionMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> PermissionMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ("Anzeigen",a=>a.SendChat("[LPLK] Schutzrecht: "+(settings.AccessPermission.Length==0?"alle Spieler":settings.AccessPermission))),        ("Alle schützen",a=>Apply(a,s=>s with{AccessPermission=""})),("Berechtigung",InputPermission)}))(p);
    private void InputPermission(IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Berechtigung"),maxLength:128,validator:v=>!string.IsNullOrEmpty(v)&&FlashSettings.ValidPermission(v),defaultValue:settings.AccessPermission,hintMessage:"Berechtigung eingeben, zum Beispiel antiteamflash.vip."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,value)=>{if(module.CanAdmin(a))Apply(a,s=>s with{AccessPermission=value});});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>module.Active,a=>{if(module.CanAdmin(a))PermissionMenu(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teamflash · Berechtigung")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
    private void Apply(IPlayer actor,Func<FlashSettings,FlashSettings> edit)
    {
        if(!module.CanAdmin(actor))return;
        FlashSettings? before=null;config.Change(s=>{before=s;return edit(s);});WriteStatus();
        Core.Logger.LogInformation("LPLK_AntiTeamFlash: Admin {Actor}; vorher={Before}; nachher={After}",actor.SteamID,System.Text.Json.JsonSerializer.Serialize(before),System.Text.Json.JsonSerializer.Serialize(settings));
        SettingsMenu(actor);
    }
}
