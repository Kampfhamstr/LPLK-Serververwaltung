using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
namespace LPLK_Demos;
public sealed class DemoParticipant
{
 public bool Bot {get;set;}
 public DemoParticipant Copy(){var p=(DemoParticipant)MemberwiseClone();p.Names=[..Names];return p;}
 public string SteamId {get;set;}="";public string Name {get;set;}="";
 public int Team {get;set;}public int Points {get;set;}public int Kills {get;set;}public int Assists {get;set;}public int Deaths {get;set;}
 public List<string> Names {get;set;}=[];
 [System.Text.Json.Serialization.JsonIgnore]public int LastScore {get;set;}
 [System.Text.Json.Serialization.JsonIgnore]public ulong Session {get;set;}
}
public sealed partial class Plugin
{
 private bool PlayingHuman(IPlayer p)=>Human(p)&&p.Controller is {IsValid:true}&&p.Controller.TeamNum is 2 or 3;
 private DemoParticipant? CapturePlayer(IPlayer? p)
 {
  if(recording is not {} r||p is null||(!PlayingHuman(p)&&!(r.IsTest&&p.IsValid&&p.IsFakeClient&&p.Controller.TeamNum is 2 or 3)))return null;
  string steam=PlayerKey(p);
  participantLookup.TryGetValue(steam,out var row);int score=p.Controller.Score;
  if(row is null){if(r.Players.Count>=128)return null;row=new(){SteamId=steam,Name=p.Name,Bot=p.IsFakeClient,LastScore=score,Session=p.SessionId};r.Players.Add(row);participantLookup[steam]=row;}
  if(row.Session==p.SessionId)row.Points+=score-row.LastScore;
  row.LastScore=score;row.Session=p.SessionId;row.Name=p.Name;row.Team=p.Controller.TeamNum;
  if(!row.Names.Contains(p.Name)&&row.Names.Count<12)row.Names.Add(p.Name);
  return row;
 }
 private void CapturePlayers(){if(recording is null||!mapReady)return;foreach(var p in Core.PlayerManager.GetAllValidPlayers())CapturePlayer(p);}
 private void IndexDeath(EventPlayerDeath e)
 {
  if(recording is null)return;
  if(CapturePlayer(e.UserIdPlayer)is {} victim)victim.Deaths++;
  if(e.AttackerPlayer?.PlayerID!=e.UserIdPlayer?.PlayerID&&CapturePlayer(e.AttackerPlayer)is {} attacker)attacker.Kills++;
  if(CapturePlayer(e.AssisterPlayer)is {} assist)assist.Assists++;
  var r=recording;if(r.Kills.Count<8192)r.Kills.Add(new(Offset,EngineTick(),roundNumber,Actor(e.AttackerPlayer),Actor(e.UserIdPlayer),e.Weapon,e.Headshot));else r.TimelineTruncated=true;
  // No file access, JSON serialization, exports or player-list scan on a kill.
 }
}
