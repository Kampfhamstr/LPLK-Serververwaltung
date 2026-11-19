using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
namespace LPLK_Teams;
public sealed record BotVoteSession(string Process,string Map,string Workshop,int ServerCount);
public sealed record BotVoteSaved(int Schema,BotVoteSession Session,bool Asked,bool Suppressed,int? PreviousQuota=null,string? PreviousMode=null);
public sealed partial class Plugin
{
 private readonly TwoPlayerBotVote botVote=new();
 private readonly Dictionary<int,(IPlayer Player,IMenuAPI Menu,ConnectionKey Key)> botVoteMenus=[];
 private static readonly string botVoteProcess=Environment.ProcessId+":"+System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
 private string? botVoteError;
 private bool botVoteStateReady;
 private bool botVoteStateBlocked,mapRuntimeUnloading;
 private BotVoteSession? botVoteSession;
 private double twoPlayersSince;
 private string botVotePair="";
 // Keep the native controller boundary separate from voting/menu behavior.
 private Func<IPlayer,bool> botVoteEligibility=p=>p.IsValid&&!p.IsFakeClient&&p.IsAuthorized&&p.SteamID>0&&p.Controller is {IsValid:true,TeamNum:2 or 3};
 private string BotVotePath=>Path.Combine(RoundFileStore.DataPath(Core),"two-player-bot-vote.json");
 private BotVoteSession? BotVoteIdentity()
 {
  try{var map=Core.Engine.GlobalVars.MapName;return string.IsNullOrWhiteSpace(map)?null:new(botVoteProcess,map,Core.Engine.WorkshopId??"",Core.Engine.GlobalVars.ServerCount);}
  catch(InvalidOperationException){return null;}
 }
 private bool InitializeMapRuntime()
 {
  if(mapRuntimeUnloading)return false;
  var session=BotVoteIdentity();if(session is null)return false;
  try{if(Core.EntitySystem.GetGameRules() is null)return false;}
  catch(InvalidOperationException){return false;}
  LoadBotVote(session);LoadReturnTeams(session);return true;
 }
 private IPlayer[] BotVotePlayers()
 {
  var all=Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).ToArray();
  return all.Length==2&&all.All(botVoteEligibility)?all:[];
 }
 private bool MapSelectionActive()=>interfaces?.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player.Maps.v1",out var api)==true&&api.TryGetValue("selectionActive",out var value)&&value is Func<bool> busy&&busy();
 private void LoadBotVote(BotVoteSession? session)
 {
  if(session is null||botVoteStateReady||botVoteStateBlocked)return;
  try
  {
   if(RamFile.Exists(BotVotePath))
   {
    var saved=JsonSerializer.Deserialize<BotVoteSaved>(RamFile.ReadAllText(BotVotePath))??throw new InvalidDataException("Leerer Botvotestatus.");
    if(saved.Schema!=1||saved.Session is null||saved.Session.Process is null||saved.Session.Map is null||saved.Session.Workshop is null||saved.Suppressed&&!saved.Asked)throw new InvalidDataException("Ungültiger Botvotestatus.");
    if(saved.Session==session)
    {
     botVote.Restore(saved.Asked,saved.Suppressed);
     if(saved.Suppressed&&Core.ConVar.Find<int>("bot_quota")?.Value==0&&Core.ConVar.Find<string>("bot_quota_mode")?.Value=="normal")
     {previousBotQuota=saved.PreviousQuota;previousBotMode=saved.PreviousMode;lastBotQuota=0;}
    }
   }
   botVoteSession=session;botVoteStateReady=true;SaveBotVote();
  }
  catch(Exception e){botVoteStateBlocked=true;botVoteError=e.GetType().Name;Core.Logger.LogWarning(e,"Bot-Abstimmungsstatus nicht lesbar; keine automatische Abstimmung.");}
 }
 private void SaveBotVote()
 {
  if(!botVoteStateReady||botVoteSession is null)return;
  try{DeferredJson.Queue(BotVotePath,new BotVoteSaved(1,botVoteSession,botVote.Asked,botVote.Suppressed,previousBotQuota,previousBotMode));botVoteError=null;}
  catch(Exception e){botVoteError=e.GetType().Name;Core.Logger.LogWarning(e,"Bot-Abstimmung konnte nicht gespeichert werden.");}
 }
 private void ResetBotVote(){CloseBotVoteMenus();botVote.NewMap();twoPlayersSince=0;botVotePair="";SaveBotVote();nextBotCheck=0;}
 private void TickBotVote()
 {
  if(!settings.TwoPlayerBotVoteEnabled||settings.BotFillSlots<=0||!botVoteStateReady||botVoteError is not null)
  {if(botVote.Active){botVote.Cancel();CompleteBotVote();}return;}
  var players=BotVotePlayers();
  bool sameConnections=botVoteMenus.Values.All(m=>players.Any(p=>Key(p)==m.Key));
  if(botVote.Active)
  {
   if(!sameConnections||MapSelectionActive()){botVote.Cancel();CompleteBotVote();return;}
   if(botVote.Check(Now,players.Select(p=>p.SteamID)))CompleteBotVote();
   return;
  }
  string pair=string.Join(',',players.Select(p=>Key(p)).OrderBy(k=>k.Steam));
  if(players.Length!=2||pair!=botVotePair){botVotePair=pair;twoPlayersSince=Now;return;}
  if(botVote.Asked||botVote.Suppressed||Now-twoPlayersSince<10||MapSelectionActive())return;
  if(!Core.PlayerManager.GetAllPlayers().Any(IsPlayingBot))return;
  if(botVote.Start(players.Select(p=>p.SteamID),Now))
  {
   Core.PlayerManager.SendLplkChat("[LPLK] Zwei Spieler: Bots für den Rest dieser Map ausschalten? Beide müssen innerhalb von 30 Sekunden mit Ja stimmen. !botvote öffnet die Abstimmung.");
   foreach(var player in players)OpenBotVote(player);
  }
 }
 private void OpenBotVote(IPlayer player)
 {
  if(!botVote.Active||!BotVotePlayers().Any(p=>Key(p)==Key(player)))return;
  var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Bots bis Mapende ausschalten? · 2× Ja nötig")).Design.SetMaxVisibleItems(5);
  var numbers=new MenuNumbers();var identity=Key(player);
  foreach(bool yes in new[]{true,false})
  {
   var option=new ButtonMenuOption(numbers.Next(yes?"Ja · Bots für diese Map aus":"Nein · Bots behalten")){CloseAfterClick=false};
   GameThreadMenu.Bind(Core,option,player,()=>loaded,p=>
   {
    var current=BotVotePlayers();
    if(Key(p)!=identity||botVoteMenus.Values.Any(m=>!current.Any(x=>Key(x)==m.Key)))return;
    if(!botVote.Cast(p.SteamID,yes,Now,current.Select(x=>x.SteamID)))return;
    if(botVoteMenus.TryGetValue(p.PlayerID,out var shown))Core.MenusAPI.CloseMenuForPlayer(p,shown.Menu);
    p.SendLplkChat(yes?"[LPLK] Ja-Stimme gespeichert.":"[LPLK] Nein-Stimme gespeichert.");
    if(!botVote.Active)CompleteBotVote();
   });builder.AddOption(option);
  }
  if(botVoteMenus.Remove(player.PlayerID,out var previous))Core.MenusAPI.CloseMenuForPlayer(player,previous.Menu);
  var menu=builder.Build();MenuPresentation.Apply(menu);botVoteMenus[player.PlayerID]=(player,menu,identity);Core.MenusAPI.OpenMenuForPlayer(player,menu);
 }
 private void CompleteBotVote()
 {
  CloseBotVoteMenus();SaveBotVote();nextBotCheck=0;
  Core.PlayerManager.SendLplkChat(botVote.Suppressed?"[LPLK] Beide haben zugestimmt: Bots bleiben bis zum nächsten Mapwechsel aus.":"[LPLK] Keine gemeinsame Zustimmung: Die Bots bleiben eingeschaltet.");
 }
 private void CloseBotVoteMenus(){foreach(var m in botVoteMenus.Values)if(m.Player.IsValid&&Key(m.Player)==m.Key)Core.MenusAPI.CloseMenuForPlayer(m.Player,m.Menu);botVoteMenus.Clear();}
 private object BotVoteStatus()=>new{enabled=settings.TwoPlayerBotVoteEnabled,ready=botVoteStateReady,active=botVote.Active,asked=botVote.Asked,suppressed=botVote.Suppressed,yes=botVote.Yes,seconds=botVote.Active?Math.Max(0,(int)Math.Ceiling(botVote.Deadline-Now)):0,result=botVote.Result,error=botVoteError,quota=Core.ConVar.Find<int>("bot_quota")?.Value,actualBots=Core.PlayerManager.GetAllPlayers().Count(IsPlayingBot)};
 private void BotReloadProbe(ICommandContext ctx,string action)
 {
  if(ctx.IsSentByPlayer||ctx.Sender is not null||Core.PlayerManager.GetAllPlayers().Any(p=>p.IsValid&&!p.IsFakeClient)||MapSelectionActive())throw new InvalidOperationException("Leerer Testserver erforderlich.");
  string path=Path.Combine(RoundFileStore.DataPath(Core),"bot-vote-reload-test.json");
  if(!RamFile.Exists(path)||new RamFileInfo(path).Length>2048)throw new InvalidOperationException("Testfreigabe fehlt.");
  using var auth=JsonDocument.Parse(RamFile.ReadAllText(path));var expires=auth.RootElement.GetProperty("expires").GetDateTimeOffset();
  if(auth.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b"||expires<DateTimeOffset.UtcNow||expires>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidOperationException("Testfreigabe ungültig.");
  if(action=="seed")
  {
   if(!botVoteStateReady||botVote.Asked||settings.BotFillSlots!=4||!botVote.Start([1,2],Now))throw new InvalidOperationException("Testbasis ungültig.");
   if(!botVote.Cast(1,true,Now,[1,2])||botVote.Suppressed||!botVote.Cast(2,true,Now,[1,2])||!botVote.Suppressed)throw new InvalidOperationException("Zustimmungsprüfung fehlgeschlagen.");
   CompleteBotVote();ReconcileBots();
  }
  else if(action=="reset")
  {
   if(!botVote.Suppressed||Core.ConVar.Find<int>("bot_quota")?.Value!=0)throw new InvalidOperationException("Botpause nicht übernommen.");
   ResetBotVote();ReconcileBots();
  }
  else if(action=="finish")
  {
   if(botVote.Suppressed||Core.ConVar.Find<int>("bot_quota")?.Value!=4)throw new InvalidOperationException("Normale Auffüllung nicht wiederhergestellt.");
   int original=auth.RootElement.GetProperty("originalFillSlots").GetInt32();if(original!=0)throw new InvalidOperationException("Unbekanntes Testprofil.");
   settings=config.Change(current=>current with{BotFillSlots=original});nextBotCheck=0;ReconcileBots();
   foreach(var bot in Core.PlayerManager.GetAllPlayers().Where(IsPlayingBot).ToArray())bot.Kick("Isolierter Bot-Test beendet",SwiftlyS2.Shared.ProtobufDefinitions.ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
   RamFile.Delete(path);
  }
  else throw new InvalidOperationException("Unbekannter Testschritt.");
  ctx.ReplyLplk("LPLK_BOT_VOTE "+JsonSerializer.Serialize(BotVoteStatus()));
 }
 [Command("botvote")]
 public void BotVoteCommand(ICommandContext ctx){if(ctx.Sender is {} player){if(botVote.Active)OpenBotVote(player);else player.SendLplkChat(botVote.Suppressed?"[LPLK] Bots sind für diese Map ausgeschaltet.":"[LPLK] Zurzeit läuft keine Bot-Abstimmung.");}else if(ctx.Args is ["probe",var step])BotReloadProbe(ctx,step);else ctx.ReplyLplk("LPLK_BOT_VOTE "+JsonSerializer.Serialize(BotVoteStatus()));}
}
