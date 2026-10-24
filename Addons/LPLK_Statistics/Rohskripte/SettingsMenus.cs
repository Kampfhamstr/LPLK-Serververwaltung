using System.Globalization;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 private (string,Action<IPlayer>) Number(string property,string label,double min,double max,Action<IPlayer> back,bool positive=false)
 {
  var info=typeof(StatisticsSettings).GetProperty(property)!;
  return ($"{label}: {Convert.ToString(info.GetValue(settings),CultureInfo.InvariantCulture)}",p=>NumberInput(p,property,label,min,max,back,positive));
 }
 private void NumberInput(IPlayer p,string property,string title,double min,double max,Action<IPlayer> back,bool positive)
 {
  if(!module.CanAdmin(p))return;var info=typeof(StatisticsSettings).GetProperty(property)!;
  bool Parse(string text,out double n)=>double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&double.IsFinite(n)&&n>=min&&n<=max&&(!positive||n>0)&&(info.PropertyType!=typeof(int)||n==Math.Truncate(n));
  var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:32,validator:t=>Parse(t,out _),defaultValue:Convert.ToString(info.GetValue(settings),CultureInfo.InvariantCulture)??"",hintMessage:positive?"Positive Zahl eingeben.":$"{min} bis {max} eingeben."){CloseAfterClick=false};
  GameThreadMenu.BindInput(Core,input,p,()=>module.Active,(a,text)=>{if(!module.CanAdmin(a)||!Parse(text,out double n))return;Save(a,s=>{var next=s with{};info.SetValue(next,info.PropertyType==typeof(int)?(object)(int)n:n);return next;});back(a);});
  var button=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,button,p,()=>module.Active,a=>{if(module.CanAdmin(a))back(a);});
  Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(button).Build());
 }
 private void AdminRanking(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminRankingRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{Number(nameof(StatisticsSettings.TopCount),"Topplätze",1,100,AdminRanking)}))(p);
 private void AdminMinimums(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminMinimumsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new[]{Number(nameof(StatisticsSettings.MinShots),"PvP-Schüsse",1,int.MaxValue,AdminMinimums),Number(nameof(StatisticsSettings.MinKills),"PvP-Kills",1,int.MaxValue,AdminMinimums),Number(nameof(StatisticsSettings.MinBotShots),"Botschüsse",1,int.MaxValue,AdminMinimums),Number(nameof(StatisticsSettings.MinBotKills),"Botkills",1,int.MaxValue,AdminMinimums)}))(p);
 private void AdminWeights(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminWeightsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new[]{Number(nameof(StatisticsSettings.HeadshotWeight),"Kopfquote",0,double.MaxValue,AdminWeights),Number(nameof(StatisticsSettings.KillEfficiencyWeight),"Kill-Effizienz",0,double.MaxValue,AdminWeights),Number(nameof(StatisticsSettings.HeadshotEfficiencyWeight),"Kopf-Effizienz",0,double.MaxValue,AdminWeights),Number(nameof(StatisticsSettings.VolumeWeight),"Menge",0,double.MaxValue,AdminWeights)}))(p);
 private void AdminFullScore(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminFullScoreRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new[]{Number(nameof(StatisticsSettings.FullKillEfficiency),"Kill-Effizienz",0,double.MaxValue,AdminFullScore,true),Number(nameof(StatisticsSettings.FullHeadshotEfficiency),"Kopf-Effizienz",0,double.MaxValue,AdminFullScore,true),Number(nameof(StatisticsSettings.FullHeadshots),"Kopftreffer",1,int.MaxValue,AdminFullScore)}))(p);
 private void AdminWeaponDetails(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminWeaponDetailsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Standard-HUD: {settings.DefaultHud}",a=>{Save(a,s=>s with{DefaultHud=!s.DefaultHud});AdminWeaponDetails(a);}),Number(nameof(StatisticsSettings.InspectCooldownMs),"Inspect-Pause (ms)",100,10000,AdminWeaponDetails),Number(nameof(StatisticsSettings.HudDurationMs),"Anzeigezeit (ms)",250,10000,AdminWeaponDetails),Number(nameof(StatisticsSettings.InfernoSeconds),"Feuerzuordnung (s)",1,120,AdminWeaponDetails),($"Unbekanntes Feuer: {settings.UnknownInferno}",a=>{Save(a,s=>s with{UnknownInferno=s.UnknownInferno=="molotov"?"incgrenade":s.UnknownInferno=="incgrenade"?"inferno":"molotov"});AdminWeaponDetails(a);})}))(p);
 private void AdminDamageRules(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> AdminDamageRulesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{($"Teamschaden: {settings.ShowFriendlyDamage}",a=>{Save(a,s=>s with{ShowFriendlyDamage=!s.ShowFriendlyDamage});AdminDamageRules(a);}),($"Nur Angreifer: {settings.ShowOnlyKiller}",a=>{Save(a,s=>s with{ShowOnlyKiller=!s.ShowOnlyKiller});AdminDamageRules(a);}),($"Alle Schäden: {settings.ShowAllDamages}",a=>{Save(a,s=>s with{ShowAllDamages=!s.ShowAllDamages});AdminDamageRules(a);}),($"Ohne Runden: {settings.NoRoundsMode}",a=>{Save(a,s=>s with{NoRoundsMode=!s.NoRoundsMode});AdminDamageRules(a);})}))(p);
}
