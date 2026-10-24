using LPLK.Common;
using SwiftlyS2.Shared.Players;
namespace LPLK_AntiCamp;
public sealed partial class Plugin
{
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>module.Active,module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p) => Page(p,"LPLK · AntiCamp",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Erkennung",DetectionRows(actor)),("Warnung",WarningsRows(actor)),("Strafen",PenaltiesRows(actor)),("Schaden",DamageRows(actor)),("Zeit",TimingRows(actor)),("Ausnahmen",ExceptionsRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{("Status",a=>a.SendChat($"[LPLK] AntiCamp: {(settings.PluginEnabled?"AN":"AUS")}; Campzeit={settings.CampTime}s; Radius={settings.CampCheckRadius}; Warnungen={warnings}; Strafen={punishments}; Konfiguration={reloadError??"gültig"}."))}))(p);
    private void Change(IPlayer p,Func<CampSettings,CampSettings> edit,Action<IPlayer> back){Apply(edit,p);back(p);}
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Aktiv: {settings.PluginEnabled}",a=>Change(a,s=>s with{PluginEnabled=!s.PluginEnabled},SettingsMenu))}))(p);
    private void Values(IPlayer p,string title,double current,double[] values,Func<CampSettings,double,CampSettings> edit,Action<IPlayer> back,Func<double,bool>? valid=null)=>Page(p,title,_=>values.Append(current).Distinct().Where(n=>valid?.Invoke(n)??true).Order().Select(n=>(n.ToString("0.##"),(Action<IPlayer>)(a=>Change(a,s=>edit(s,n),back)))).ToArray(),back);
    private void Detection(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> DetectionRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Campzeit: {settings.CampTime}s",a=>Values(a,"Campzeit (Sekunden)",settings.CampTime,[5,10,15,20,30,45,60,90,120,180,240,300],(s,n)=>s with{CampTime=(int)n},Detection,n=>n>=settings.WarningSeconds)),        ($"Radius: {settings.CampCheckRadius:0.##}",a=>Values(a,"Radius",settings.CampCheckRadius,[10,20,30,40,50,75,100,150,200,300,500,750,1000],(s,n)=>s with{CampCheckRadius=n},Detection))}))(p);
    private void Warnings(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> WarningsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Vorlauf: {settings.WarningSeconds}s",a=>Values(a,"Vorlauf (Sekunden)",settings.WarningSeconds,[0,1,2,3,5,10,15,20,30,60],(s,n)=>s with{WarningSeconds=(int)n},Warnings,n=>n<=settings.CampTime)),        ($"Chathinweise: {!settings.DisableChatMessages}",a=>Change(a,s=>s with{DisableChatMessages=!s.DisableChatMessages},Warnings))}))(p);
    private void Penalties(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> PenaltiesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Art: {new[]{"Markierung","Slap","Slay"}[settings.SlapORSlayPunishment]}",a=>Page(a,"Strafart",_=>new[]{"Markierung","Slap","Slay"}.Select((label,n)=>(label,(Action<IPlayer>)(b=>Change(b,s=>s with{SlapORSlayPunishment=n},Penalties)))).ToArray(),Penalties)),        ($"Strafsignal: {settings.PunishBeacon}",a=>Change(a,s=>s with{PunishBeacon=!s.PunishBeacon},Penalties)),        ($"Immer Slap: {settings.PunishAnyway}",a=>Change(a,s=>s with{PunishAnyway=!s.PunishAnyway},Penalties))}))(p);
    private void Damage(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> DamageRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Schaden: {settings.SlapDamage}",a=>Values(a,"Schaden",settings.SlapDamage,[0,1,2,3,5,10,15,20,25,50,75,100],(s,n)=>s with{SlapDamage=(int)n},Damage)),        ($"HP-Reserve: {settings.MinHealthReserve}",a=>Values(a,"HP-Reserve",settings.MinHealthReserve,[0,5,10,15,20,25,50,75,100],(s,n)=>s with{MinHealthReserve=(int)n},Damage))}))(p);
    private void Timing(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> TimingRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Erste Strafe: {settings.FirstPunishDelay:0.##}s",a=>Values(a,"Zusätzliche Verzögerung (s)",settings.FirstPunishDelay,[0,.25,.5,1,2,3,5,10,15,20,30],(s,n)=>s with{FirstPunishDelay=n},Timing)),        ($"Intervall: {settings.PunishFrequency:0.##}s",a=>Values(a,"Intervall (Sekunden)",settings.PunishFrequency,[.25,.5,1,2,3,5,10,15,20,30,45,60],(s,n)=>s with{PunishFrequency=n},Timing))}))(p);
    private void Exceptions(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> ExceptionsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"T / Geiseln: {settings.AllowTCampHostageMap}",a=>Change(a,s=>s with{AllowTCampHostageMap=!s.AllowTCampHostageMap},Exceptions)),        ($"T / Bombe gelegt: {settings.AllowTCampBombPlanted}",a=>Change(a,s=>s with{AllowTCampBombPlanted=!s.AllowTCampBombPlanted},Exceptions)),        ($"CT / Bombenmap: {settings.AllowCTCampBombMap}",a=>Change(a,s=>s with{AllowCTCampBombMap=!s.AllowCTCampBombMap},Exceptions)),        ($"CT / Bombe liegt: {settings.AllowCtCampBombDropped}",a=>Change(a,s=>s with{AllowCtCampBombDropped=!s.AllowCtCampBombDropped},Exceptions))}))(p);
}
