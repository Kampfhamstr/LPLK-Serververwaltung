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
        context.Reply($"LPLK_AntiTeamFlash 0.1.1: enabled={settings.Enabled}; flashOwner={settings.FlashOwner}; permission={settings.AccessPermission}; ready={ready}; cleared={cleared}; preserved={preserved}; errors={errors}; reloadError={reloadError??"none"}");
    }
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · Teamflash-Schutz",
        ($"Aktiv: {settings.Enabled}",a=>Apply(a,settings with{Enabled=!settings.Enabled})),
        ($"Eigenblendung: {settings.FlashOwner}",a=>Apply(a,settings with{FlashOwner=!settings.FlashOwner})),
        ($"Schutzrecht: {(settings.AccessPermission.Length==0?"alle Spieler":settings.AccessPermission)} …",PermissionMenu),
        ("Erklärung",a=>{a.SendChat("[LPLK] Schutz vor Teamflash. Gegnerflash bleibt wirksam. Laufende erlaubte Blendung wird bei Überschneidung beibehalten; dabei kann auch die Teamflash wirksam bleiben. FFA behandelt Mitspieler als Gegner.");OpenAdmin(a);}));
    private void PermissionMenu(IPlayer p)=>module.Menu(p,"Teamflash · Schutzberechtigung",1,
        ("Alle schützen",a=>Apply(a,settings with{AccessPermission=""})),
        ("Berechtigung",InputPermission),
        ("Zurück",OpenAdmin));
    private void InputPermission(IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Berechtigung"),maxLength:128,validator:v=>!string.IsNullOrEmpty(v)&&FlashSettings.ValidPermission(v),defaultValue:settings.AccessPermission,hintMessage:"Berechtigung eingeben, zum Beispiel antiteamflash.vip."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,value)=>{if(module.CanAdmin(a))Apply(a,settings with{AccessPermission=value});});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>module.Active,a=>{if(module.CanAdmin(a))PermissionMenu(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teamflash · Berechtigung")).Design.SetMaxVisibleItems(5).AddOption(input).AddOption(back).Build());
    }
    private void Apply(IPlayer actor,FlashSettings next)
    {
        if(!module.CanAdmin(actor))return;
        FlashSettings.Validate(next);module.SaveSettings(next);settings=next;
        stamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;WriteStatus();OpenAdmin(actor);
    }
}
