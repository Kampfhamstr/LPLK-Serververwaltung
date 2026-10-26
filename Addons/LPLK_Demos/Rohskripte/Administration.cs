using System.Globalization;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;
namespace LPLK_Demos;
public sealed partial class Plugin
{
 private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> entries,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,"Demos · "+title,()=>module.Active,module.CanAdmin,entries,back);
 private void OpenAdmin(IPlayer p) => Page(p,"Verwaltung",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Aufnahme",ControlRows(actor)),("Regeln",RulesRows(actor)),("Runden",RoundRulesRows(actor)),("Leerlauf",IdleRulesRows(actor)),("Aufbewahrung",RetentionRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{}))(p);
 private void Rules(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RulesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Modul aktiv: {settings.Enabled}",a=>{Save(a,s=>s with{Enabled=!s.Enabled});Rules(a);}),($"Automatische Aufnahme: {settings.AutoRecord}",a=>{Save(a,s=>s with{AutoRecord=!s.AutoRecord});Rules(a);}),($"Spieleranfragen: {settings.RequestsEnabled}",a=>{Save(a,s=>s with{RequestsEnabled=!s.RequestsEnabled});Rules(a);})}))(p);
 private void RoundRules(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RoundRulesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Gesamte Map mit Rundenmarken: {settings.FullMapRecording}",a=>{Save(a,s=>s with{FullMapRecording=!s.FullMapRecording});RoundRules(a);}),($"Warmup aufnehmen: {settings.RecordWarmup}",a=>{Save(a,s=>s with{RecordWarmup=!s.RecordWarmup});RoundRules(a);}),($"Anfragehinweis: {settings.PrintRequests}",a=>{Save(a,s=>s with{PrintRequests=!s.PrintRequests});RoundRules(a);})}))(p);
 private void Control(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> ControlRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{("Starten",a=>{Start();a.SendLplkChat("[LPLK] "+(recording?.State??lastError??"Aufnahme momentan nicht möglich"));Control(a);}),("Stoppen",a=>{Stop(false);Control(a);}),("Status",a=>a.SendLplkChat($"[LPLK] {recording?.State??"idle"}; vollständige Demos {archive.Records.Count(r=>r.State=="complete")}; Fehler {errors}; {lastError}"))}))(p);
 private void Retention(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RetentionRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Bereinigung: {settings.CleanupEnabled}",a=>{Save(a,s=>s with{CleanupEnabled=!s.CleanupEnabled});Retention(a);}),($"Unangefragte löschen: {settings.DeleteUnrequested}",a=>{Save(a,s=>s with{DeleteUnrequested=!s.DeleteUnrequested});Retention(a);}),Number(nameof(DemoSettings.MinimumSeconds),"Mindestlänge (s)",0,3600,Retention),Number(nameof(DemoSettings.RetentionHours),"Aufbewahrung (h)",1,8760,Retention)}))(p);
 private void IdleRules(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> IdleRulesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Leerlaufstopp: {settings.StopOnIdle}",a=>{Save(a,s=>s with{StopOnIdle=!s.StopOnIdle});IdleRules(a);}),Number(nameof(DemoSettings.IdlePlayerThreshold),"Spielerschwelle",1,64,IdleRules),Number(nameof(DemoSettings.IdleSeconds),"Wartezeit (s)",5,86400,IdleRules)}))(p);
 private (string,Action<IPlayer>) Number(string property,string label,int min,int max,Action<IPlayer> back)
 {var info=typeof(DemoSettings).GetProperty(property)!;return($"{label}: {info.GetValue(settings)}",p=>NumberInput(p,property,label,min,max,back));}
 private void NumberInput(IPlayer p,string property,string title,int min,int max,Action<IPlayer> back)
 {
  if(!module.CanAdmin(p))return;var info=typeof(DemoSettings).GetProperty(property)!;
  bool Parse(string text,out int n)=>int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out n)&&n>=min&&n<=max;
  var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:10,validator:t=>Parse(t,out _),defaultValue:Convert.ToString(info.GetValue(settings),CultureInfo.InvariantCulture)??"",hintMessage:$"{min} bis {max} eingeben."){CloseAfterClick=false};
  GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,text)=>{if(!module.CanAdmin(a)||!Parse(text,out int n))return;Save(a,s=>{var next=s with{};info.SetValue(next,n);return next;});back(a);});
  var button=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,button,p,()=>module.Active,a=>{if(module.CanAdmin(a))back(a);});
  Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(button).Build());
 }
}
