using System.Globalization;
using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_ThirdPerson;

public sealed partial class Plugin
{
    [Command("lplk_thirdperson")]
    public void Administration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){OpenAdmin(p);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="probe")
        {
            if(!ready||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_ThirdPerson probe: nur bei leerem, bereitem Testserver");return;}
            uint camera=uint.MaxValue;
            try
            {
                camera=world.Create();if(camera==uint.MaxValue)throw new InvalidOperationException("point_camera fehlt");
                var sample=new CameraPlayer(new(0,0,"probe",0,2),true,true,false,uint.MaxValue,new(),new(),0,0);
                world.Move(camera,new(0,0,64),sample);
                if(world.Position(camera) is not {} pos||System.Numerics.Vector3.Distance(pos,new(0,0,64))>.1f)throw new InvalidOperationException("Kameraposition nicht bestätigt");
                if(!world.ProbeTrace())throw new InvalidOperationException("Kameratrace ungültig");
                world.Destroy(camera);if(world.Position(camera) is not null)throw new InvalidOperationException("Kamera blieb bestehen");camera=uint.MaxValue;
                ctx.Reply("LPLK_ThirdPerson probe: point_camera erzeugt, Position und Hull-Trace bestätigt, entfernt; keine Spieleransicht verändert.");
            }
            catch(Exception e){Error(e);ctx.Reply("LPLK_ThirdPerson probe: fehlgeschlagen");}
            finally{if(camera!=uint.MaxValue)world.Destroy(camera);WriteStatus();}
            return;
        }
        WriteStatus();ctx.Reply($"LPLK_ThirdPerson 0.1.4: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; permission={settings.UseTpPermission}; distance={settings.ThirdPersonDistance}; height={settings.ThirdPersonHeight}; smooth={settings.UseSmoothCam}; damage={settings.DamageMode}; cameras={cameras.Count}; errors={errors}; reloadError={reloadError??"none"}; command={activeCommandName}; commandError={commandError??"none"}");
    }
    private void OpenAdmin(IPlayer p) => module.Menu(p,"LPLK · ThirdPerson-Kamera",((Func<IPlayer,(string,Action<IPlayer>)[]>)(actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Camera",CameraMenuRows(actor)),("Schaden",DamageMenuRows(actor)),("Berechtigung",PermissionMenuRows(actor)))))(p));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Aktiv: {(Enabled?"AN":"AUS")}",ToggleEnabled)}))(p);
    private void CameraMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> CameraMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Abstand: {settings.ThirdPersonDistance:0.##}",a=>NumberInput(a,"Abstand",20,250,settings.ThirdPersonDistance,(s,v)=>s with{ThirdPersonDistance=v})),($"Höhe: {settings.ThirdPersonHeight:0.##}",a=>NumberInput(a,"Höhe",20,120,settings.ThirdPersonHeight,(s,v)=>s with{ThirdPersonHeight=v})),($"Weiche Bewegung: {(settings.UseSmoothCam?"AN":"AUS")}",a=>{Apply(a,s=>s with{UseSmoothCam=!s.UseSmoothCam});CameraMenu(a);}),($"Glättung: {settings.SmoothCameraSpeed:0.00}",a=>NumberInput(a,"Glättung",.01f,1,settings.SmoothCameraSpeed,(s,v)=>s with{SmoothCameraSpeed=v}))}))(p);
    private void DamageMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> DamageMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Schadensmodus: {settings.DamageMode}",a=>{Apply(a,s=>s with{DamageMode=s.DamageMode=="back"?"none":s.DamageMode=="none"?"all":"back"});DamageMenu(a);}),($"Messerhinweise: {(settings.EnableKnifeWarnings?"AN":"AUS")}",a=>{Apply(a,s=>s with{EnableKnifeWarnings=!s.EnableKnifeWarnings});DamageMenu(a);}),("Modi erklären",a=>a.SendChat("[LPLK] back: Schaden an Spielern hinter dir sperren. none: eigenen Spielerschaden in ThirdPerson sperren. all: keine zusätzliche Sperre. Kameraposition kann Treffer beeinflussen; !tp schaltet zur Egoansicht."))}))(p);
    private void PermissionMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> PermissionMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Recht: {(settings.UseTpPermission.Length==0?"alle Spieler":settings.UseTpPermission)}",InputPermission),("Alle erlauben",a=>{Apply(a,s=>s with{UseTpPermission=""});PermissionMenu(a);}),($"Befehl: {(activeCommandName.Length==0?"keiner":activeCommandName)}",InputCommand)}))(p);
    private void InputCommand(IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        bool Valid(string text){try{CameraCommandName.Normalize(text);return true;}catch(InvalidDataException){return false;}}
        var input=new InputMenuOption(new MenuNumbers().Next("Befehl eingeben"),maxLength:44,validator:Valid,defaultValue:settings.CustomTPCommand,hintMessage:"Name ohne !; leer entfernt den Zusatzbefehl."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,v)=>{if(!module.CanAdmin(a))return;if(Apply(a,s=>s with{CustomTPCommand=CameraCommandName.Normalize(v)})&&commandError is not null)a.SendChat("[LPLK] "+commandError);PermissionMenu(a);});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,p,()=>module.Active,a=>{if(module.CanAdmin(a))PermissionMenu(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Kamerabefehl")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
    private void InputPermission(IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Berechtigung eingeben"),maxLength:128,validator:v=>v.Length>0&&CameraSettings.PermissionValid(v),defaultValue:settings.UseTpPermission,hintMessage:"Zum Beispiel thirdperson.use"){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,v)=>{if(module.CanAdmin(a)){Apply(a,s=>s with{UseTpPermission=v});PermissionMenu(a);}});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,p,()=>module.Active,PermissionMenu);
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("ThirdPerson · Berechtigung")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
    private void NumberInput(IPlayer p,string title,float min,float max,float current,Func<CameraSettings,float,CameraSettings> edit)
    {
        if(!module.CanAdmin(p))return;
        bool Parse(string text,out float value)=>float.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&float.IsFinite(value)&&value>=min&&value<=max;
        var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:16,validator:t=>Parse(t,out _),defaultValue:current.ToString(CultureInfo.InvariantCulture),hintMessage:$"{min} bis {max} eingeben."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,text)=>
        {if(!module.CanAdmin(a)||!Parse(text,out float value))return;Apply(a,s=>edit(s,value));CameraMenu(a);});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>module.Active,a=>{if(module.CanAdmin(a))CameraMenu(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
    private void ToggleEnabled(IPlayer actor)
    {
        if(!module.CanAdmin(actor))return;
        var convar=Core.ConVar.Find<bool>("thirdperson_enabled");
        if(convar is null){actor.SendChat("[LPLK] Kameraschalter derzeit nicht verfügbar.");return;}
        // The explicit admin switch may change the legacy ConVar only after a successful save.
        if(Apply(actor,s=>s with{Enabled=!(s.Enabled&&convar.Value)}))
        {convar.Value=settings.Enabled;if(ready)cameras.Tick(settings,Enabled,0,Error);WriteStatus();}
        OpenAdmin(actor);
    }
    private bool Apply(IPlayer actor,Func<CameraSettings,CameraSettings> edit)
    {
        if(!module.CanAdmin(actor))return false;
        bool saved=false;
        try{config.Change(edit);reloadError=null;saved=true;}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {reloadError=config.Error??e.GetType().Name;actor.SendChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
        SyncSettings();if(ready)cameras.Tick(settings,Enabled,0,Error);WriteStatus();return saved;
    }
}
