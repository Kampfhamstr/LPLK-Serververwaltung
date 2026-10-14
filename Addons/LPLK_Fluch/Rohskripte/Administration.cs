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
    private void Status(ICommandContext ctx){WriteStatus();ctx.Reply($"LPLK_Fluch 0.1.1: enabled={settings.Enabled}; effective={Enabled}; ready={ready}; chickens={settings.ChickenCount}; duration={settings.OrbitDurationSeconds}; damage={settings.TotalDamage}; active={curses.Count}; errors={errors}; reloadError={reloadError??"none"}");}
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · Hühnerfluch",
        ("Spieler",a=>Targets(a,0)),
        ($"Beenden ({curses.Count})",a=>{if(ready)curses.Clear();OpenAdmin(a);}),
        ("Einstellungen",SettingsMenu));
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
            .Design.SetMenuTitle(MenuColors.Heading(targets.Length==0?"Fluch · Kein lebender Zielspieler":$"Fluch · Ziele {layout.Index+1}/{layout.Pages}")).Design.SetMaxVisibleItems(5);
        var numbers=new MenuNumbers();int index=0;
        foreach(var (label,action) in actions)
        {
            var button=new ButtonMenuOption(index++==actions.Count-1?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>module.Active,a=>{if(module.CanAdmin(a))action(a);});menu.AddOption(button);
        }
        Core.MenusAPI.OpenMenuForPlayer(p,menu.Build());
    }
    public static string SafeLabel(string name)=>name.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    private void SettingsMenu(IPlayer p)=>module.Menu(p,"Fluch · Einstellungen",1,
        ($"Aktiv: {(settings.Enabled?"AN":"AUS")}",a=>{Apply(a,settings with{Enabled=!settings.Enabled});SettingsMenu(a);}),
        ("Wirkung",EffectMenu),
        ("Kreisbahn",OrbitMenu),
        ("Hinweise",MessageMenu),
        ("Zurück",OpenAdmin));
    private void EffectMenu(IPlayer p)=>module.Menu(p,"Fluch · Wirkung",1,
        ($"Hühner: {settings.ChickenCount}",a=>{Apply(a,settings with{ChickenCount=(int)Next(settings.ChickenCount,[5,10,20,32,64])});EffectMenu(a);}),
        ($"Dauer: {settings.OrbitDurationSeconds:0.#} s",a=>{Apply(a,settings with{OrbitDurationSeconds=Next(settings.OrbitDurationSeconds,[3,5,10,15,30,60])});EffectMenu(a);}),
        ($"Hühnerschaden: {settings.ChickenExplosionDamagePerChicken:0.#} (gesamt {settings.TotalDamage:0.#})",a=>{Apply(a,settings with{ChickenExplosionDamagePerChicken=Next(settings.ChickenExplosionDamagePerChicken,[0,1,2,5,10])});EffectMenu(a);}),
        ($"Gleichzeitige Flüche: {settings.MaxActiveCurses}",a=>{Apply(a,settings with{MaxActiveCurses=settings.MaxActiveCurses%4+1});EffectMenu(a);}),
        ("Zurück",SettingsMenu));
    private void OrbitMenu(IPlayer p)=>module.Menu(p,"Fluch · Kreisbahn",1,
        ($"Höhe: {settings.HeadOrbitHeight:0}",a=>{Apply(a,settings with{HeadOrbitHeight=Next(settings.HeadOrbitHeight,[40,60,76,100,150])});OrbitMenu(a);}),
        ($"Radius: {settings.HeadOrbitRadius:0}",a=>{Apply(a,settings with{HeadOrbitRadius=Next(settings.HeadOrbitRadius,[30,40,58,80,120])});OrbitMenu(a);}),
        ($"Geschwindigkeit: {settings.OrbitSpeed:0.0}",a=>{Apply(a,settings with{OrbitSpeed=Next(settings.OrbitSpeed,[1,2,2.7f,4,6])});OrbitMenu(a);}),
        ("Bewegung",WaveMenu),
        ("Zurück",SettingsMenu));
    private void WaveMenu(IPlayer p)=>module.Menu(p,"Fluch · Bewegung",1,
        ($"Wellenhöhe: {settings.OrbitWaveHeight:0}",a=>{Apply(a,settings with{OrbitWaveHeight=Next(settings.OrbitWaveHeight,[0,5,9,15,30])});WaveMenu(a);}),
        ($"Wellenfrequenz: {settings.OrbitWaveSpeed:0.0}",a=>{Apply(a,settings with{OrbitWaveSpeed=Next(settings.OrbitWaveSpeed,[0,1,2,3,4])});WaveMenu(a);}),
        ($"Aktualisierung: {settings.OrbitTickSeconds:0.00} s",a=>{Apply(a,settings with{OrbitTickSeconds=Next(settings.OrbitTickSeconds,[.03f,.05f,.08f,.1f,.2f])});WaveMenu(a);}),
        ("Zurück",OrbitMenu));
    private void MessageMenu(IPlayer p)=>module.Menu(p,"Fluch · Ziele & Hinweise",1,
        ($"Selbstverfluchen: {(settings.AllowSelfTarget?"AN":"AUS")}",a=>{Apply(a,settings with{AllowSelfTarget=!settings.AllowSelfTarget});MessageMenu(a);}),
        ($"Startchat: {(settings.NotifyTargetInChat?"AN":"AUS")}",a=>{Apply(a,settings with{NotifyTargetInChat=!settings.NotifyTargetInChat});MessageMenu(a);}),
        ("Bildschirmhinweise",CenterMenu),
        ("Zurück",SettingsMenu));
    private void CenterMenu(IPlayer p)=>module.Menu(p,"Fluch · Bildschirmhinweise",1,
        ($"Start: {(settings.ShowStartCenterMessage?"AN":"AUS")}",a=>{Apply(a,settings with{ShowStartCenterMessage=!settings.ShowStartCenterMessage});CenterMenu(a);}),
        ($"Ende: {(settings.ShowExplosionCenterMessage?"AN":"AUS")}",a=>{Apply(a,settings with{ShowExplosionCenterMessage=!settings.ShowExplosionCenterMessage});CenterMenu(a);}),
        ($"Countdown: {(settings.ShowOrbitProgressMessage?"AN":"AUS")}",a=>{Apply(a,settings with{ShowOrbitProgressMessage=!settings.ShowOrbitProgressMessage});CenterMenu(a);}),
        ("Zurück",MessageMenu));
    private static float Next(float v,float[] values)=>values.FirstOrDefault(x=>x>v+.001f,values[0]);
    private void Apply(IPlayer actor,FluchSettings next)
    {
        if(!module.CanAdmin(actor))return;
        FluchSettings.Validate(next);module.SaveSettings(next);settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);reloadError=null;
        if(!settings.Enabled&&ready)curses.Clear();WriteStatus();
    }
}
public static class TargetPages
{
    public static (int Index,int Pages,int Offset,int Size) Make(int count,int requested)
    {int size=count<=4?4:2,pages=Math.Max(1,(count+size-1)/size),index=Math.Clamp(requested,0,pages-1);return(index,pages,index*size,size);}
}
