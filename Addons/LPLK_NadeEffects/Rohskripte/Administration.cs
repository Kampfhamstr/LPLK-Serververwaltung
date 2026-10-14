using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
namespace LPLK_NadeEffects;

public sealed partial class Plugin
{
    [Command("nadeboom")][CommandAlias("nb")]
    public void NadeBoom(ICommandContext ctx){if(ctx.Sender is {} p)OpenAdmin(p);else ctx.Reply("LPLK_NadeEffects: !nadeboom / !nb benötigt einen Administrator im Spiel.");}
    [Command("lplk_nadeeffects")]
    public void Administration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){OpenAdmin(p);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="probe")
        {
            if(!ready||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_NadeEffects probe: leerer, bereiter Server erforderlich");return;}
            uint fade=uint.MaxValue;
            try
            {
                fade=world.CreateFade(settings);if(fade==uint.MaxValue||!world.VerifyFade(fade,settings))throw new InvalidOperationException("Fade-Einstellungen nicht bestätigt");
                world.ProbeInput(fade);world.Destroy(fade);if(world.Exists(fade))throw new InvalidOperationException("Fade blieb bestehen");fade=uint.MaxValue;
                ctx.Reply("LPLK_NadeEffects probe: env_fade erzeugt, Dauer/Haltezeit und ActivatorOnly bestätigt, Fade-Input ausgeführt, entfernt; keine Spieler oder Inventare verändert.");
            }
            catch(Exception e){Error(e);ctx.Reply("LPLK_NadeEffects probe: fehlgeschlagen");}
            finally{if(fade!=uint.MaxValue)world.Destroy(fade);WriteStatus();}
            return;
        }
        WriteStatus();ctx.Reply($"LPLK_NadeEffects 0.1.1: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; selected={effects.Selected}; pending={effects.PendingCount}; fades={effects.FadeCount}; cooldown={settings.TriggerCooldownSeconds}; remove={settings.RemoveNades}; errors={errors}; reloadError={reloadError??"none"}");
    }
    private IMenuBuilderAPI Builder(string title)=>Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(0)
        .SetSelectButton(Enum.Parse<KeyBind>(settings.SelectButton)).SetMoveForwardButton(Enum.Parse<KeyBind>(settings.MoveUpButton)).SetMoveBackwardButton(Enum.Parse<KeyBind>(settings.MoveDownButton)).SetExitButton(Enum.Parse<KeyBind>(settings.ExitButton))
        .Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5);
    private void Menu(IPlayer player,string title,int back,params (string Label,Action<IPlayer> Action)[] rows)
    {
        if(!module.CanAdmin(player))return;var builder=Builder(title);var numbers=new MenuNumbers();int index=0;
        foreach(var (label,action) in rows)
        {
            var button=new ButtonMenuOption(index++<rows.Length-back?numbers.Next(label,enabled:MenuColors.SwitchValue(label)):MenuColors.Back(label)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,player,()=>module.Active,a=>{if(module.CanAdmin(a))action(a);});builder.AddOption(button);
        }
        Core.MenusAPI.OpenMenuForPlayer(player,builder.Build());
    }
    private void OpenAdmin(IPlayer p)=>Menu(p,"LPLK · Granateneffekte",0,
        ($"Spieler ({effects.Selected})",a=>Targets(a,0)),
        ("Zurücksetzen",a=>{if(ready)effects.Clear();WriteStatus();OpenAdmin(a);}),
        ("Einstellungen",SettingsMenu));
    private void Targets(IPlayer p,int requested)
    {
        if(!module.CanAdmin(p))return;if(!ready){p.SendChat("[LPLK] Die Map ist noch nicht bereit.");return;}
        var targets=Core.PlayerManager.GetAllValidPlayers().Where(x=>x is {IsValid:true,IsAuthorized:true,IsFakeClient:false})
            .Select(x=>(Player:x,State:world.Player(x.PlayerID))).Where(x=>x.State is not null).OrderBy(x=>x.Player.Name).ToArray();
        var page=NadePolicy.Page(targets.Length,requested);var builder=Builder(targets.Length==0?"Granateneffekte · Keine Spieler":$"Granateneffekte · Ziele {page.Index+1}/{page.Pages}");var numbers=new MenuNumbers();
        foreach(var x in targets.Skip(page.Offset).Take(page.Size))
        {
            var connection=x.State!.Owner.Connection;string name=x.Player.Name;
            string shortName=name.Length>settings.MenuNameMaxLength?name[..(settings.MenuNameMaxLength-1)]+"…":name;
            bool marked=effects.IsSelected(connection);
            var button=new ButtonMenuOption(numbers.Next($"{(marked?"AN":"AUS")} | {NativeNadeWorld.Html(shortName)}",enabled:marked)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>module.Active,a=>
            {
                if(!ready||!module.CanAdmin(a)||world.Player(a.PlayerID) is not {} actor)return;
                string result=effects.Toggle(actor.Owner.Connection,connection);a.SendChat("[LPLK] "+NativeNadeWorld.Chat(name)+": "+result);
                if(result is "AN" or "AUS"&&settings.NotifyAdminsOnToggle)Notify($"{NativeNadeWorld.Chat(a.Name)}: Granateneffekt für {NativeNadeWorld.Chat(name)} {result}.",settings);
                WriteStatus();Targets(a,page.Index);
            });builder.AddOption(button);
        }
        void Navigation(string label,Action<IPlayer> action,bool back=false){var b=new ButtonMenuOption(back?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};GameThreadMenu.Bind(Core,b,p,()=>module.Active,a=>{if(module.CanAdmin(a))action(a);});builder.AddOption(b);}
        if(page.Index>0)Navigation("Vorherige Seite",a=>Targets(a,page.Index-1));if(page.Index+1<page.Pages)Navigation("Nächste Seite",a=>Targets(a,page.Index+1));Navigation("Zurück",OpenAdmin,true);
        Core.MenusAPI.OpenMenuForPlayer(p,builder.Build());
    }
    private void SettingsMenu(IPlayer p)=>Menu(p,"Granateneffekte · Einstellungen",1,
        ($"Aktiv: {(settings.Enabled?"AN":"AUS")}",a=>{Apply(a,settings with{Enabled=!settings.Enabled});SettingsMenu(a);}),("Auslöser",TriggerMenu),("Bildschirm-Fade",FadeMenu),("Hinweise",MessageMenu),("Zurück",OpenAdmin));
    private void TriggerMenu(IPlayer p)=>Menu(p,"Granateneffekte · Auslöser",1,
        ("Granatentypen",a=>Grenades(a,0)),
        ($"Auslösesperre: {settings.TriggerCooldownSeconds:0.##} s",a=>{Apply(a,settings with{TriggerCooldownSeconds=Next(settings.TriggerCooldownSeconds,[.5,1.25,2.5,5,10])});TriggerMenu(a);}),
        ($"Granaten entfernen: {(settings.RemoveNades?"AN":"AUS")}",a=>{Apply(a,settings with{RemoveNades=!settings.RemoveNades});TriggerMenu(a);}),
        ($"Danach Messer: {(settings.SwitchToKnifeAfterRemove?"AN":"AUS")}",a=>{Apply(a,settings with{SwitchToKnifeAfterRemove=!settings.SwitchToKnifeAfterRemove});TriggerMenu(a);}),
        ("Zurück",SettingsMenu));
    private static readonly (string Property,string Label)[] GrenadeRows=[(nameof(NadeSettings.EnableFlashbang),"Flashbang"),(nameof(NadeSettings.EnableHEGrenade),"HE-Granate"),(nameof(NadeSettings.EnableSmoke),"Smoke"),(nameof(NadeSettings.EnableMolotov),"Molotov"),(nameof(NadeSettings.EnableIncendiary),"Incendiary"),(nameof(NadeSettings.EnableDecoy),"Decoy"),(nameof(NadeSettings.EnableTAGrenade),"TA-Granate")];
    private void Grenades(IPlayer p,int requested)
    {
        var page=NadePolicy.Page(GrenadeRows.Length,requested);var rows=new List<(string,Action<IPlayer>)>();
        foreach(var (name,label) in GrenadeRows.Skip(page.Offset).Take(page.Size))
        {var prop=typeof(NadeSettings).GetProperty(name)!;rows.Add(($"{label}: {((bool)prop.GetValue(settings)!?"AN":"AUS")}",a=>{var next=settings with{};prop.SetValue(next,!(bool)prop.GetValue(settings)!);Apply(a,next);Grenades(a,page.Index);}));}
        if(page.Index>0)rows.Add(("Vorherige Seite",a=>Grenades(a,page.Index-1)));if(page.Index+1<page.Pages)rows.Add(("Nächste Seite",a=>Grenades(a,page.Index+1)));rows.Add(("Zurück",TriggerMenu));Menu(p,$"Granaten · {page.Index+1}/{page.Pages}",1,rows.ToArray());
    }
    private void FadeMenu(IPlayer p)=>Menu(p,"Granateneffekte · Bildschirm",1,
        ($"Fade: {(settings.UseEnvFade?"AN":"AUS")}",a=>{Apply(a,settings with{UseEnvFade=!settings.UseEnvFade});FadeMenu(a);}),
        ($"Dauer: {settings.FadeDuration:0.##} s",a=>{Apply(a,settings with{FadeDuration=Next(settings.FadeDuration,[.15,.3,.5,1,2])});FadeMenu(a);}),
        ($"Haltezeit: {settings.FadeHoldTime:0.##} s",a=>{Apply(a,settings with{FadeHoldTime=Next(settings.FadeHoldTime,[0,1,2.4,3,5])});FadeMenu(a);}),
        ("Darstellung",AppearanceMenu),("Zurück",SettingsMenu));
    private void AppearanceMenu(IPlayer p)=>Menu(p,"Granateneffekte · Darstellung",1,
        ($"Farbe: {settings.FadeColor}",a=>{string[] colors=["255 255 255","255 100 100","100 100 255","0 0 0"];Apply(a,settings with{FadeColor=colors[(Array.IndexOf(colors,settings.FadeColor)+1)%colors.Length]});AppearanceMenu(a);}),
        ($"Deckkraft: {settings.FadeAlpha}",a=>{Apply(a,settings with{FadeAlpha=(int)Next(settings.FadeAlpha,[64,128,192,255])});AppearanceMenu(a);}),
        ($"Lebensdauer: {settings.FadeEntityRemoveDelaySeconds:0.#} s",a=>{Apply(a,settings with{FadeEntityRemoveDelaySeconds=(float)Next(settings.FadeEntityRemoveDelaySeconds,[1,3,5,10])});AppearanceMenu(a);}),
        ("Zurück",FadeMenu));
    private void MessageMenu(IPlayer p)=>Menu(p,"Granateneffekte · Hinweise",1,
        ("Warntext ändern",WarnInput),
        ($"Bildschirmhinweis: {(settings.UseCenterWarning?"AN":"AUS")}",a=>{Apply(a,settings with{UseCenterWarning=!settings.UseCenterWarning});MessageMenu(a);}),
        ($"Hinweisdauer: {settings.CenterWarningDurationMs} ms",a=>{Apply(a,settings with{CenterWarningDurationMs=(int)Next(settings.CenterWarningDurationMs,[1000,2000,3000,5000])});MessageMenu(a);}),
        ("Adminhinweise",AdminNotices),("Zurück",SettingsMenu));
    private void AdminNotices(IPlayer p)=>Menu(p,"Granateneffekte · Adminhinweise",1,
        ($"Bei Markierung: {(settings.NotifyAdminsOnToggle?"AN":"AUS")}",a=>{Apply(a,settings with{NotifyAdminsOnToggle=!settings.NotifyAdminsOnToggle});AdminNotices(a);}),
        ($"Bei Auslösung: {(settings.NotifyAdminsOnTrigger?"AN":"AUS")}",a=>{Apply(a,settings with{NotifyAdminsOnTrigger=!settings.NotifyAdminsOnTrigger});AdminNotices(a);}),
        ("Zurück",MessageMenu));
    private void WarnInput(IPlayer p)
    {
        if(!module.CanAdmin(p))return;var input=new InputMenuOption(new MenuNumbers().Next("Warntext eingeben"),maxLength:160,validator:NadeSettings.TextValid,defaultValue:settings.WarnMessage,hintMessage:"Bis zu 160 Zeichen"){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,v)=>{if(module.CanAdmin(a)){Apply(a,settings with{WarnMessage=v});MessageMenu(a);}});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,p,()=>module.Active,MessageMenu);Core.MenusAPI.OpenMenuForPlayer(p,Builder("Granateneffekte · Warntext").AddOption(input).AddOption(back).Build());
    }
    private static double Next(double v,double[] values)=>values.FirstOrDefault(x=>x>v+.001,values[0]);
    private void Apply(IPlayer actor,NadeSettings next)
    {
        if(!module.CanAdmin(actor))return;NadeSettings.Validate(next);module.SaveSettings(next);if(ready)effects.CancelEffects();settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;WriteStatus();
    }
}
