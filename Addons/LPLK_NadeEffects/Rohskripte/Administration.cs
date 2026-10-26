using System.Globalization;
using System.Text.Json;
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
    public void NadeBoom(ICommandContext ctx){if(ctx.Sender is {} p)OpenAdmin(p);else ctx.ReplyLplk("LPLK_NadeEffects: !nadeboom / !nb benötigt einen Administrator im Spiel.");}
    [Command("lplk_nadeeffects")]
    public void Administration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){OpenAdmin(p);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="probe")
        {
            if(!ready||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.ReplyLplk("LPLK_NadeEffects probe: leerer, bereiter Server erforderlich");return;}
            uint fade=uint.MaxValue;
            try
            {
                fade=world.CreateFade(settings);if(fade==uint.MaxValue||!world.VerifyFade(fade,settings))throw new InvalidOperationException("Fade-Einstellungen nicht bestätigt");
                world.ProbeInput(fade);world.Destroy(fade);if(world.Exists(fade))throw new InvalidOperationException("Fade blieb bestehen");fade=uint.MaxValue;
                ctx.ReplyLplk("LPLK_NadeEffects probe: env_fade erzeugt, Dauer/Haltezeit und ActivatorOnly bestätigt, Fade-Input ausgeführt, entfernt; keine Spieler oder Inventare verändert.");
            }
            catch(Exception e){Error(e);ctx.ReplyLplk("LPLK_NadeEffects probe: fehlgeschlagen");}
            finally{if(fade!=uint.MaxValue)world.Destroy(fade);WriteStatus();}
            return;
        }
        WriteStatus();ctx.ReplyLplk($"LPLK_NadeEffects 0.1.10: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; selected={effects.Selected}; pending={effects.PendingCount}; fades={effects.FadeCount}; cooldown={settings.TriggerCooldownSeconds}; remove={settings.RemoveNades}; errors={errors}; reloadError={reloadError??"none"}");
    }
    private IMenuBuilderAPI Builder(string title)=>Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(0)
        .SetSelectButton(Enum.Parse<KeyBind>(settings.SelectButton)).SetMoveForwardButton(Enum.Parse<KeyBind>(settings.MoveUpButton)).SetMoveBackwardButton(Enum.Parse<KeyBind>(settings.MoveDownButton)).SetExitButton(Enum.Parse<KeyBind>(settings.ExitButton))
        .Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
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
    private void OpenAdmin(IPlayer p) => Menu(p,"LPLK · Granateneffekte",0,((Func<IPlayer,(string,Action<IPlayer>)[]>)(actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Auslöser",TriggerMenuRows(actor)),("Bildschirm",FadeMenuRows(actor)),("Darstellung",AppearanceMenuRows(actor)),("Meldung",MessageMenuRows(actor)),("Warnung",WarningMenuRows(actor)),("Darstellung",MenuAppearanceRows(actor)),("Adminhinweis",AdminNoticesRows(actor)))))(p));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Spieler ({effects.Selected})",a=>Targets(a,0)),("Zurücksetzen",a=>{if(ready)effects.Clear();WriteStatus();OpenAdmin(a);})}))(p);
    private void Targets(IPlayer p,int requested)
    {
        if(!module.CanAdmin(p))return;if(!ready){p.SendLplkChat("[LPLK] Die Map ist noch nicht bereit.");return;}
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
                string result=effects.Toggle(actor.Owner.Connection,connection);a.SendLplkChat("[LPLK] "+NativeNadeWorld.Chat(name)+": "+result);
                if(result is "AN" or "AUS"&&settings.NotifyAdminsOnToggle)Notify($"{NativeNadeWorld.Chat(a.Name)}: Granateneffekt für {NativeNadeWorld.Chat(name)} {result}.",settings);
                WriteStatus();Targets(a,page.Index);
            });builder.AddOption(button);
        }
        void Navigation(string label,Action<IPlayer> action,bool back=false){var b=new ButtonMenuOption(back?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};GameThreadMenu.Bind(Core,b,p,()=>module.Active,a=>{if(module.CanAdmin(a))action(a);});builder.AddOption(b);}
        if(page.Index>0)Navigation("Vorherige Seite",a=>Targets(a,page.Index-1));if(page.Index+1<page.Pages)Navigation("Nächste Seite",a=>Targets(a,page.Index+1));Navigation("Zurück",OpenAdmin,true);
        Core.MenusAPI.OpenMenuForPlayer(p,builder.Build());
    }
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Aktiv: {(settings.Enabled?"AN":"AUS")}",a=>{Apply(a,s=>s with{Enabled=!s.Enabled});SettingsMenu(a);})}))(p);
    private void TriggerMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TriggerMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{("Granatentypen",a=>Grenades(a,0)),($"Auslösesperre: {settings.TriggerCooldownSeconds:0.##} s",a=>NumberInput(a,nameof(NadeSettings.TriggerCooldownSeconds),"Auslösesperre (Sekunden)",0.1,60,TriggerMenu)),($"Granaten entfernen: {(settings.RemoveNades?"AN":"AUS")}",a=>{Apply(a,s=>s with{RemoveNades=!s.RemoveNades});TriggerMenu(a);}),($"Danach Messer: {(settings.SwitchToKnifeAfterRemove?"AN":"AUS")}",a=>{Apply(a,s=>s with{SwitchToKnifeAfterRemove=!s.SwitchToKnifeAfterRemove});TriggerMenu(a);})}))(p);
    private static readonly (string Property,string Label)[] GrenadeRows=[(nameof(NadeSettings.EnableFlashbang),"Flashbang"),(nameof(NadeSettings.EnableHEGrenade),"HE-Granate"),(nameof(NadeSettings.EnableSmoke),"Smoke"),(nameof(NadeSettings.EnableMolotov),"Molotov"),(nameof(NadeSettings.EnableIncendiary),"Incendiary"),(nameof(NadeSettings.EnableDecoy),"Decoy"),(nameof(NadeSettings.EnableTAGrenade),"TA-Granate")];
    private void Grenades(IPlayer p,int requested)
    {
        var rows=new List<(string,Action<IPlayer>)>();
        foreach(var (name,label) in GrenadeRows)
        {var prop=typeof(NadeSettings).GetProperty(name)!;rows.Add(($"{label}: {((bool)prop.GetValue(settings)!?"AN":"AUS")}",a=>{Apply(a,s=>{var next=s with{};prop.SetValue(next,!(bool)prop.GetValue(s)!);return next;});Grenades(a,0);}));}
        rows.Add(("Zurück",TriggerMenu));Menu(p,"Granaten",1,rows.ToArray());
    }
    private void FadeMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> FadeMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Fade: {(settings.UseEnvFade?"AN":"AUS")}",a=>{Apply(a,s=>s with{UseEnvFade=!s.UseEnvFade});FadeMenu(a);}),($"Dauer: {settings.FadeDuration:0.##} s",a=>NumberInput(a,nameof(NadeSettings.FadeDuration),"Fade-Dauer (Sekunden)",0,10,FadeMenu)),($"Haltezeit: {settings.FadeHoldTime:0.##} s",a=>NumberInput(a,nameof(NadeSettings.FadeHoldTime),"Haltezeit (Sekunden)",0,10,FadeMenu))}))(p);
    private void AppearanceMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AppearanceMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Farbe: {settings.FadeColor}",ColorInput),($"Deckkraft: {settings.FadeAlpha}",a=>NumberInput(a,nameof(NadeSettings.FadeAlpha),"Deckkraft",0,255,AppearanceMenu)),($"Lebensdauer: {settings.FadeEntityRemoveDelaySeconds:0.#} s",a=>NumberInput(a,nameof(NadeSettings.FadeEntityRemoveDelaySeconds),"Lebensdauer (Sekunden)",0.1,30,AppearanceMenu))}))(p);
    private void MessageMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MessageMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{}))(p);
    private void WarningMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> WarningMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{("Warntext ändern",WarnInput),($"Bildschirmhinweis: {(settings.UseCenterWarning?"AN":"AUS")}",a=>{Apply(a,s=>s with{UseCenterWarning=!s.UseCenterWarning});WarningMenu(a);}),($"Hinweisdauer: {settings.CenterWarningDurationMs} ms",a=>NumberInput(a,nameof(NadeSettings.CenterWarningDurationMs),"Hinweisdauer (Millisekunden)",100,10000,WarningMenu))}))(p);
    private void MenuAppearance(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MenuAppearanceRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Namenslänge: {settings.MenuNameMaxLength}",a=>NumberInput(a,nameof(NadeSettings.MenuNameMaxLength),"Namenslänge",5,64,MenuAppearance)),("Tastenbelegung",a=>{a.SendLplkChat($"[LPLK] Auswahl: {settings.SelectButton}; aufwärts: {settings.MoveUpButton}; abwärts: {settings.MoveDownButton}; schließen: {settings.ExitButton}. Änderung in settings.json; vier unterschiedliche Tasten erforderlich.");MenuAppearance(a);})}))(p);
    private void AdminNotices(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminNoticesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Bei Markierung: {(settings.NotifyAdminsOnToggle?"AN":"AUS")}",a=>{Apply(a,s=>s with{NotifyAdminsOnToggle=!s.NotifyAdminsOnToggle});AdminNotices(a);}),($"Bei Auslösung: {(settings.NotifyAdminsOnTrigger?"AN":"AUS")}",a=>{Apply(a,s=>s with{NotifyAdminsOnTrigger=!s.NotifyAdminsOnTrigger});AdminNotices(a);})}))(p);
    private void WarnInput(IPlayer p)=>TextInput(p,"Granateneffekte · Warntext",settings.WarnMessage,160,NadeSettings.TextValid,(s,v)=>s with{WarnMessage=v},WarningMenu);
    private void ColorInput(IPlayer p)=>TextInput(p,"Farbe: Rot Grün Blau",settings.FadeColor,11,NadeSettings.ColorValid,(s,v)=>s with{FadeColor=v},AppearanceMenu);
    private void TextInput(IPlayer p,string title,string initial,int length,Func<string,bool> validate,Func<NadeSettings,string,NadeSettings> edit,Action<IPlayer> back)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:length,validator:validate,defaultValue:initial,hintMessage:title){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,v)=>{if(module.CanAdmin(a)&&validate(v)){Apply(a,s=>edit(s,v));back(a);}});
        var returnButton=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,returnButton,p,()=>module.Active,a=>{if(module.CanAdmin(a))back(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Builder(title).AddOption(input).AddOption(returnButton).Build());
    }
    private void NumberInput(IPlayer p,string propertyName,string title,double min,double max,Action<IPlayer> back)
    {
        if(!module.CanAdmin(p))return;
        var property=typeof(NadeSettings).GetProperty(propertyName)!;
        bool Parse(string text,out double value)=>double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value)&&value>=min&&value<=max&&(property.PropertyType!=typeof(int)||value==Math.Truncate(value));
        var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:16,validator:t=>Parse(t,out _),defaultValue:Convert.ToString(property.GetValue(settings),CultureInfo.InvariantCulture)??"",hintMessage:$"{min} bis {max} eingeben."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,text)=>
        {
            if(!module.CanAdmin(a)||!Parse(text,out double number))return;
            Apply(a,s=>{var next=s with{};object value=property.PropertyType==typeof(int)?(object)(int)number:property.PropertyType==typeof(float)?(object)(float)number:number;property.SetValue(next,value);return next;});back(a);
        });
        var returnButton=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,returnButton,p,()=>module.Active,a=>{if(module.CanAdmin(a))back(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Builder(title).AddOption(input).AddOption(returnButton).Build());
    }
    private void Apply(IPlayer actor,Func<NadeSettings,NadeSettings> edit)
    {
        if(!module.CanAdmin(actor))return;
        try{config.Change(edit);reloadError=null;}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {reloadError=config.Error??e.GetType().Name;actor.SendLplkChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
        SyncSettings();WriteStatus();
    }
}
