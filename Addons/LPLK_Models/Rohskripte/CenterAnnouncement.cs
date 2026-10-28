using SwiftlyS2.Shared.Players;

namespace LPLK_Models;

public sealed partial class Plugin
{
 private string halloweenHtml="";
 private int announcementRound;
 private int centerShown;
 private readonly Dictionary<int,(string Session,ulong Steam,long Until)> centerRecipients=[];
 private bool AnnouncementsActive=>module.Active&&ready&&settings.Enabled&&settings.Halloween&&settings.AnnouncementsEnabled;

 private void RoundAnnouncement()
 {
  EndCenter();int round=++announcementRound,epoch=mapEpoch;
  if(!AnnouncementsActive)return;
  // Run after the engine has finished setting up this round. No timer or tick loop.
  Core.Scheduler.NextWorldUpdate(()=>{
   if(round!=announcementRound||epoch!=mapEpoch||!AnnouncementsActive||Core.EntitySystem.GetGameRules() is not {FreezePeriod:true,WarmupPeriod:false})return;
   Announce();
   foreach(var p in Core.PlayerManager.GetAllPlayers().Where(Allowed))ShowCenter(p);
  });
 }
 private void ShowCenter(IPlayer p)
 {
  if(halloweenHtml.Length==0||Core.MenusAPI.GetCurrentMenu(p)is not null)return;
  p.SendCenterHTML(halloweenHtml,HalloweenBanner.DurationMs);
  centerRecipients[p.PlayerID]=(p.SessionId.ToString(),p.SteamID,Environment.TickCount64+HalloweenBanner.DurationMs);centerShown++;
 }
 private void EndCenter()
 {
  announcementRound++;
  foreach(var (slot,sent) in centerRecipients){
   var p=Core.PlayerManager.GetPlayer(slot);
   if(Environment.TickCount64<sent.Until&&p is {IsValid:true,IsFakeClient:false}&&p.SteamID==sent.Steam&&p.SessionId.ToString()==sent.Session&&Core.MenusAPI.GetCurrentMenu(p)is null)p.SendCenterHTML(string.Empty,1);
  }
  centerRecipients.Clear();
 }
 private void PreviewCenter(IPlayer p)
 {
  if(!module.CanAdmin(p))return;
  if(!ready){p.SendLplkChat("Vorschau nach der Modellvorbereitung beim Mapstart verfügbar.");return;}
  if(Core.MenusAPI.GetCurrentMenu(p)is {} menu)Core.MenusAPI.CloseMenuForPlayer(p,menu);
  int slot=p.PlayerID,epoch=mapEpoch;ulong steam=p.SteamID;string session=p.SessionId.ToString();
  Core.Scheduler.NextWorldUpdate(()=>{
   var current=Core.PlayerManager.GetPlayer(slot);
   if(epoch==mapEpoch&&current is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&current.SteamID==steam&&current.SessionId.ToString()==session&&module.CanAdmin(current))ShowCenter(current);
  });
 }
}
