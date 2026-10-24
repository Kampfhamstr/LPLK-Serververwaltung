using System.Globalization;
using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
namespace LPLK_Fluch;

public sealed partial class Plugin
{
    [Command("fluch")]
    public void Fluch(ICommandContext ctx){if(ctx.Sender is {} p)OpenAdmin(p);else ctx.Reply("LPLK_Fluch: !fluch benötigt einen Administrator im Spiel.");}
    [Command("fluchstop")]
    public void Stop(ICommandContext ctx)
    {
        if(ctx.Sender is {} p&&!module.CanAdmin(p))return;
        if(ready)curses.Clear();ctx.Reply("LPLK_Fluch: alle aktiven Flüche beendet.");WriteStatus();
    }
    [Command("fluchconfig")]
    public void Config(ICommandContext ctx){if(ctx.Sender is {} p&&!module.CanAdmin(p))return;Status(ctx);}
    [Command("lplk_fluch")]
    public void Administration(ICommandContext ctx)
    {
        if(ctx.Sender is {} p){OpenAdmin(p);return;}
        if(ctx.Args.Length==1&&ctx.Args[0]=="probe")
        {
            if(!ready||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_Fluch probe: leerer, bereiter Server erforderlich");return;}
            uint chicken=uint.MaxValue;
            try
            {
                chicken=world.Spawn(new(0,0,64),0);if(chicken==uint.MaxValue)throw new InvalidOperationException("Huhn fehlt");
                if(!world.Move(chicken,new(16,24,96),90)||world.Position(chicken) is not {} pos||System.Numerics.Vector3.Distance(pos,new(16,24,96))>.1f)throw new InvalidOperationException("Hühnerposition nicht bestätigt");
                world.Destroy(chicken);if(world.Position(chicken) is not null)throw new InvalidOperationException("Huhn blieb bestehen");chicken=uint.MaxValue;
                ctx.Reply("LPLK_Fluch probe: chicken erzeugt, Teleport bestätigt, entfernt; kein Spielerschaden ausgelöst.");
            }
            catch(Exception e){Error(e);ctx.Reply("LPLK_Fluch probe: fehlgeschlagen");}
            finally{if(chicken!=uint.MaxValue)world.Destroy(chicken);WriteStatus();}
            return;
        }
        Status(ctx);
    }
    private void Status(ICommandContext ctx){WriteStatus();ctx.Reply($"LPLK_Fluch 0.1.3: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; chickens={settings.ChickenCount}; duration={settings.OrbitDurationSeconds}; damage={settings.TotalDamage}; active={curses.Count}; errors={errors}; reloadError={reloadError??"none"}");}
    private void OpenAdmin(IPlayer p) => module.Menu(p,"LPLK · Hühnerfluch",((Func<IPlayer,(string,Action<IPlayer>)[]>)(actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Wirkung",EffectMenuRows(actor)),("Kreisbahn",OrbitMenuRows(actor)),("Bewegung",WaveMenuRows(actor)),("Meldung",MessageMenuRows(actor)),("Bildschirm",CenterMenuRows(actor)),("Anzeigezeit",MessageTimesRows(actor)))))(p));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{("Spieler",a=>Targets(a,0)),($"Beenden ({curses.Count})",a=>{if(ready)curses.Clear();OpenAdmin(a);})}))(p);
    private void Targets(IPlayer p,int page)
    {
        if(!module.CanAdmin(p))return;
        if(!Enabled){p.SendChat("[LPLK] Der Fluch ist ausgeschaltet oder die Map noch nicht bereit.");OpenAdmin(p);return;}
        var targets=Core.PlayerManager.GetAllValidPlayers().Where(x=>settings.AllowSelfTarget||x.PlayerID!=p.PlayerID)
            .Select(x=>(Player:x,State:world.Player(x.PlayerID))).Where(x=>x.State is {Alive:true}).OrderBy(x=>x.Player.Name).ToArray();
        var layout=TargetPages.Make(targets.Length,page);
        var actions=new List<(string,Action<IPlayer>)>();
        foreach(var x in targets.Skip(layout.Offset).Take(layout.Size))
        {
            var owner=x.State!.Owner;string name=SafeLabel(x.Player.Name);
            actions.Add((name,a=>
            {
                if(!module.CanAdmin(a)||!Enabled)return;
                if(world.Player(a.PlayerID) is not {} caster)return;
                a.SendChat("[LPLK] "+curses.Start(caster.Owner,owner,settings,Environment.TickCount64/1000d));WriteStatus();Targets(a,layout.Index);
            }));
        }
        if(layout.Index>0)actions.Add(("Vorherige Seite",a=>Targets(a,layout.Index-1)));
        if(layout.Index+1<layout.Pages)actions.Add(("Nächste Seite",a=>Targets(a,layout.Index+1)));
        actions.Add(("Zurück",OpenAdmin));
        var menu=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(settings.MenuAutoCloseSeconds)
            .Design.SetMenuTitle(MenuColors.Heading(targets.Length==0?"Fluch · Kein lebender Zielspieler":$"Fluch · Ziele {layout.Index+1}/{layout.Pages}")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
        var numbers=new MenuNumbers();int index=0;
        foreach(var (label,action) in actions)
        {
            var button=new ButtonMenuOption(index++==actions.Count-1?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>module.Active,a=>{if(module.CanAdmin(a))action(a);});menu.AddOption(button);
        }
        Core.MenusAPI.OpenMenuForPlayer(p,menu.Build());
    }
    public static string SafeLabel(string name)=>name.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Aktiv: {(settings.Enabled?"AN":"AUS")}",a=>{Apply(a,s=>s with{Enabled=!s.Enabled});SettingsMenu(a);})}))(p);
    private void EffectMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> EffectMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Hühner: {settings.ChickenCount}",a=>NumberInput(a,nameof(FluchSettings.ChickenCount),"Hühner",1,64,EffectMenu)),($"Dauer: {settings.OrbitDurationSeconds:0.#} s",a=>NumberInput(a,nameof(FluchSettings.OrbitDurationSeconds),"Dauer (Sekunden)",1,60,EffectMenu)),($"Hühnerschaden: {settings.ChickenExplosionDamagePerChicken:0.#} (gesamt {settings.TotalDamage:0.#})",a=>NumberInput(a,nameof(FluchSettings.ChickenExplosionDamagePerChicken),"Schaden je Huhn",0,100,EffectMenu)),($"Gleichzeitige Flüche: {settings.MaxActiveCurses}",a=>NumberInput(a,nameof(FluchSettings.MaxActiveCurses),"Gleichzeitige Flüche",1,4,EffectMenu))}))(p);
    private void OrbitMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> OrbitMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Höhe: {settings.HeadOrbitHeight:0}",a=>NumberInput(a,nameof(FluchSettings.HeadOrbitHeight),"Höhe",0,250,OrbitMenu)),($"Radius: {settings.HeadOrbitRadius:0}",a=>NumberInput(a,nameof(FluchSettings.HeadOrbitRadius),"Radius",10,250,OrbitMenu)),($"Geschwindigkeit: {settings.OrbitSpeed:0.0}",a=>NumberInput(a,nameof(FluchSettings.OrbitSpeed),"Geschwindigkeit",0,20,OrbitMenu))}))(p);
    private void WaveMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> WaveMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Wellenhöhe: {settings.OrbitWaveHeight:0}",a=>NumberInput(a,nameof(FluchSettings.OrbitWaveHeight),"Wellenhöhe",0,100,WaveMenu)),($"Wellenfrequenz: {settings.OrbitWaveSpeed:0.0}",a=>NumberInput(a,nameof(FluchSettings.OrbitWaveSpeed),"Wellenfrequenz",0,20,WaveMenu)),($"Aktualisierung: {settings.OrbitTickSeconds:0.00} s",a=>NumberInput(a,nameof(FluchSettings.OrbitTickSeconds),"Aktualisierung (Sekunden)",0.03,1,WaveMenu))}))(p);
    private void MessageMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MessageMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Selbstverfluchen: {(settings.AllowSelfTarget?"AN":"AUS")}",a=>{Apply(a,s=>s with{AllowSelfTarget=!s.AllowSelfTarget});MessageMenu(a);}),($"Startchat: {(settings.NotifyTargetInChat?"AN":"AUS")}",a=>{Apply(a,s=>s with{NotifyTargetInChat=!s.NotifyTargetInChat});MessageMenu(a);}),($"Menüdauer: {settings.MenuAutoCloseSeconds:0.##} s",a=>NumberInput(a,nameof(FluchSettings.MenuAutoCloseSeconds),"Menüdauer (Sekunden)",5,120,MessageMenu))}))(p);
    private void CenterMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> CenterMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Start: {(settings.ShowStartCenterMessage?"AN":"AUS")}",a=>{Apply(a,s=>s with{ShowStartCenterMessage=!s.ShowStartCenterMessage});CenterMenu(a);}),($"Ende: {(settings.ShowExplosionCenterMessage?"AN":"AUS")}",a=>{Apply(a,s=>s with{ShowExplosionCenterMessage=!s.ShowExplosionCenterMessage});CenterMenu(a);}),($"Countdown: {(settings.ShowOrbitProgressMessage?"AN":"AUS")}",a=>{Apply(a,s=>s with{ShowOrbitProgressMessage=!s.ShowOrbitProgressMessage});CenterMenu(a);})}))(p);
    private void MessageTimes(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MessageTimesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Start: {settings.StartCenterMessageMs} ms",a=>NumberInput(a,nameof(FluchSettings.StartCenterMessageMs),"Startanzeige (Millisekunden)",100,10000,MessageTimes)),($"Ende: {settings.ExplosionCenterMessageMs} ms",a=>NumberInput(a,nameof(FluchSettings.ExplosionCenterMessageMs),"Endanzeige (Millisekunden)",100,10000,MessageTimes)),($"Fortschritt: {settings.OrbitProgressMessageMs} ms",a=>NumberInput(a,nameof(FluchSettings.OrbitProgressMessageMs),"Fortschritt (Millisekunden)",100,2000,MessageTimes))}))(p);
    private void NumberInput(IPlayer p,string propertyName,string title,double min,double max,Action<IPlayer> back)
    {
        if(!module.CanAdmin(p))return;
        var property=typeof(FluchSettings).GetProperty(propertyName)!;
        bool Parse(string text,out double value)=>double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&double.IsFinite(value)&&value>=min&&value<=max&&(property.PropertyType!=typeof(int)||value==Math.Truncate(value));
        var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:16,validator:t=>Parse(t,out _),defaultValue:Convert.ToString(property.GetValue(settings),CultureInfo.InvariantCulture)??"",hintMessage:$"{min} bis {max} eingeben."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,text)=>
        {
            if(!module.CanAdmin(a)||!Parse(text,out double number))return;
            Apply(a,s=>{var next=s with{};object value=property.PropertyType==typeof(int)?(object)(int)number:property.PropertyType==typeof(float)?(object)(float)number:number;property.SetValue(next,value);return next;});back(a);
        });
        var returnButton=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,returnButton,p,()=>module.Active,a=>{if(module.CanAdmin(a))back(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).SetAutoCloseDelay(settings.MenuAutoCloseSeconds).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(returnButton).Build());
    }
    private void Apply(IPlayer actor,Func<FluchSettings,FluchSettings> edit)
    {
        if(!module.CanAdmin(actor))return;
        try{config.Change(edit);reloadError=null;}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {reloadError=config.Error??e.GetType().Name;actor.SendChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
        SyncSettings();WriteStatus();
    }
}
public static class TargetPages
{
    public static (int Index,int Pages,int Offset,int Size) Make(int count,int requested)
    {int size=count<=4?4:2,pages=Math.Max(1,(count+size-1)/size),index=Math.Clamp(requested,0,pages-1);return(index,pages,index*size,size);}
}
