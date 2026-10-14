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
        WriteStatus();ctx.Reply($"LPLK_ThirdPerson 0.1.0: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; permission={settings.UseTpPermission}; distance={settings.ThirdPersonDistance}; height={settings.ThirdPersonHeight}; smooth={settings.UseSmoothCam}; damage={settings.DamageMode}; cameras={cameras.Count}; errors={errors}; reloadError={reloadError??"none"}");
    }
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · ThirdPerson-Kamera",
        ($"Aktiv: {(Enabled?"AN":"AUS")}",a=>{Apply(a,settings with{Enabled=!Enabled});Core.ConVar.Find<bool>("thirdperson_enabled")!.Value=settings.Enabled;OpenAdmin(a);}),
        ("Abstand, Höhe & Bewegung",CameraMenu),
        ("Schaden & Messerhinweise",DamageMenu),
        ("Zugriffsberechtigung",PermissionMenu));
    private void CameraMenu(IPlayer p)=>module.Menu(p,"ThirdPerson · Kamera",1,
        ($"Abstand: {settings.ThirdPersonDistance:0}",a=>{Apply(a,settings with{ThirdPersonDistance=Next(settings.ThirdPersonDistance,[60,80,100,120,150,200])});CameraMenu(a);}),
        ($"Höhe: {settings.ThirdPersonHeight:0}",a=>{Apply(a,settings with{ThirdPersonHeight=Next(settings.ThirdPersonHeight,[40,60,80,100])});CameraMenu(a);}),
        ($"Weiche Bewegung: {(settings.UseSmoothCam?"AN":"AUS")}",a=>{Apply(a,settings with{UseSmoothCam=!settings.UseSmoothCam});CameraMenu(a);}),
        ($"Glättung: {settings.SmoothCameraSpeed:0.00}",a=>{Apply(a,settings with{SmoothCameraSpeed=Next(settings.SmoothCameraSpeed,[.1f,.2f,.3f,.5f,1f])});CameraMenu(a);}),
        ("Zurück",OpenAdmin));
    private static float Next(float value,float[] values)=>values.FirstOrDefault(v=>v>value+.001f,values[0]);
    private void DamageMenu(IPlayer p)=>module.Menu(p,"ThirdPerson · Schaden",1,
        ($"Schadensmodus: {settings.DamageMode}",a=>{Apply(a,settings with{DamageMode=settings.DamageMode=="back"?"none":settings.DamageMode=="none"?"all":"back"});DamageMenu(a);}),
        ($"Messerhinweise: {(settings.EnableKnifeWarnings?"AN":"AUS")}",a=>{Apply(a,settings with{EnableKnifeWarnings=!settings.EnableKnifeWarnings});DamageMenu(a);}),
        ("Modi erklären",a=>a.SendChat("[LPLK] back: Schaden an Spielern hinter dir sperren. none: eigenen Spielerschaden in ThirdPerson sperren. all: keine zusätzliche Sperre. Kameraposition kann Treffer beeinflussen; !tp schaltet zur Egoansicht.")),
        ("Zurück",OpenAdmin));
    private void PermissionMenu(IPlayer p)=>module.Menu(p,"ThirdPerson · Zugriff",1,
        ($"Recht: {(settings.UseTpPermission.Length==0?"alle Spieler":settings.UseTpPermission)}",InputPermission),
        ("Für alle Spieler freigeben",a=>{Apply(a,settings with{UseTpPermission=""});PermissionMenu(a);}),
        ("Zurück",OpenAdmin));
    private void InputPermission(IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Berechtigung eingeben"),maxLength:128,validator:v=>v.Length>0&&CameraSettings.PermissionValid(v),defaultValue:settings.UseTpPermission,hintMessage:"Zum Beispiel thirdperson.use"){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,v)=>{if(module.CanAdmin(a)){Apply(a,settings with{UseTpPermission=v});PermissionMenu(a);}});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,p,()=>module.Active,PermissionMenu);
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("ThirdPerson · Berechtigung")).Design.SetMaxVisibleItems(5).AddOption(input).AddOption(back).Build());
    }
    private void Apply(IPlayer actor,CameraSettings next)
    {
        if(!module.CanAdmin(actor))return;
        CameraSettings.Validate(next);module.SaveSettings(next);settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;
        if(ready)cameras.Tick(settings,Enabled,0,Error);WriteStatus();
    }
}
