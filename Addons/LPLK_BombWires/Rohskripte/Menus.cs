using LPLK.Common;
using SwiftlyS2.Shared.Players;
namespace LPLK_BombWires;
public sealed partial class Plugin
{
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p) => Page(p,"LPLK · Bombendrähte",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ("Status",a=>a.SendChat($"[LPLK] Bombendrähte: {(settings.Enabled?"AN":"AUS")}; Menüzeit={settings.MenuSeconds}s; gemischt={settings.RandomizeOrder}; Bombe={round.Planted}; Konfiguration={config.Error??"gültig"}."))
    }))(p);
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]
    {
        ($"Aktiv: {settings.Enabled}",a=>{Apply(s=>s with{Enabled=!s.Enabled},a);SettingsMenu(a);}),        ($"Menüzeit: {settings.MenuSeconds}s",a=>Page(a,"Menüzeit",_=>Enumerable.Range(1,10).Select(n=>($"{n}s",(Action<IPlayer>)(b=>{Apply(s=>s with{MenuSeconds=n},b);SettingsMenu(b);}))).ToArray(),SettingsMenu)),        ($"Mischen: {settings.RandomizeOrder}",a=>{Apply(s=>s with{RandomizeOrder=!s.RandomizeOrder},a);SettingsMenu(a);})
    }))(p);
}
