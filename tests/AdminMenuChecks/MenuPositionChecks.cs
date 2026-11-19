using LPLK_Admin;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;

static class MenuPositionChecks
{
 public static async Task Run(Action<bool,string> check)
 {
  var w=new MenuWorld();w.Permissions.Add("lplk.admin");var plugin=new Plugin(w.Core);plugin.Load(false);plugin.UseSharedInterface(w.Interfaces);
  bool on=false;int count=24;bool prepend=false;
  void Open(IPlayer p)
  {
   var builder=w.Core.MenusAPI.CreateBuilder().Design.SetMenuTitle("Positionsprüfung");
   if(prepend)builder.AddOption(new ButtonMenuOption("Neu"));
   for(int i=0;i<count;i++){
    var button=new ButtonMenuOption($"{i+1}. Einstellung {i}: {on}"){CloseAfterClick=false};
    GameThreadMenu.Bind(w.Core,button,p,()=>true,a=>{if(w.Permissions.Contains("lplk.admin")){on=!on;Open(a);}});builder.AddOption(button);
   }
   w.Core.MenusAPI.OpenMenuForPlayer(p,builder.Build());
  }
  w.AddModule("LPLK_AntiCamp","AntiCamp",Open);
  plugin.Admin(w.Context);await w.Click("Serververwaltung");await w.Click("AntiCamp");
  await w.Click("Einstellung 17:");check(w.Cursor==17,"module toggle keeps row 18 despite changed value");
  prepend=true;Open(w.Player);check(w.Cursor==18,"same setting follows inserted row");
  await w.Click("Zurück");check(w.Current!.GetCurrentOption(w.Player)!.Text.Contains("AntiCamp"),"return to server list restores module selection");
  await w.Click("AntiCamp");check(w.Cursor==18,"reopening module restores position");
  count=3;Open(w.Player);check(w.Cursor>=0&&w.Cursor<w.Current!.Options.Count,"removed selected entry clamps to available options");
  w.Session++;plugin.Admin(w.Context);await w.Click("Serververwaltung");await w.Click("AntiCamp");check(w.Cursor==0,"new player session never inherits old cursor");
  w.Permissions.Clear();var before=w.Current;await w.Click("Einstellung 1:");check(ReferenceEquals(before,w.Current),"cursor restoration does not bypass revoked access");
  plugin.Unload();
 }
}
