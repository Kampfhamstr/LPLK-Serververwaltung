using LPLK.Common;
using SwiftlyS2.Shared.Players;
namespace LPLK_Parachute;
public sealed partial class Plugin
{
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p) => Page(p,"LPLK · Fallschirm",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Bewegung",MovementMenuRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{("Status",a=>a.SendChat($"[LPLK] Fallschirm: {(settings.Enabled?"AN":"AUS")}; sv_parachute={enabledConvar?.Value}; Falltempo={settings.FallSpeed}; Bremse={settings.BrakePerTick}; Konfiguration={config.Error??"gültig"}."))}))(p);
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Aktiv: {settings.Enabled}",a=>{Apply(s=>s with{Enabled=!s.Enabled},a);SettingsMenu(a);}),        ($"Geiselsperre: {settings.DisableWhenCarryingHostage}",a=>{Apply(s=>s with{DisableWhenCarryingHostage=!s.DisableWhenCarryingHostage},a);SettingsMenu(a);}),        ("Berechtigung",a=>a.SendChat("[LPLK] Benötigtes Nutzungsrecht: "+(settings.UsePermission.Length==0?"keines":settings.UsePermission)+" (UsePermission in settings.json)."))
    }))(p);
    private void MovementMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MovementMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Falltempo: {settings.FallSpeed:0.##}",a=>Values(a,false)),        ($"Bremsschritt: {settings.BrakePerTick:0.##}",a=>Values(a,true)),        ($"Gleichmäßig: {settings.Linear}",a=>{Apply(s=>s with{Linear=!s.Linear},a);MovementMenu(a);})
    }))(p);
    private void Values(IPlayer p,bool brake)=>Page(p,brake?"Bremse · 0 = sofort":"Falltempo",_=>
        (brake?new[]{0f,5,10,15,20,25,50,75,100}:new[]{20f,40,60,75,85,100,150,200,300,500})
        .Append(brake?settings.BrakePerTick:settings.FallSpeed).Distinct().Order().Select(value=>(value.ToString("0.##"),(Action<IPlayer>)(a=>{Apply(s=>brake?s with{BrakePerTick=value}:s with{FallSpeed=value},a);MovementMenu(a);}))).ToArray(),MovementMenu);
}
