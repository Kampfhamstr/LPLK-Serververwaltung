using LPLK.Common;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
namespace LPLK_Statistics;
public sealed partial class Plugin
{
 private readonly WeaponOwnership ownership=new();private readonly Dictionary<uint,SwiftlyS2.Shared.Natives.CHandle<CBasePlayerWeapon>> knownWeapons=[];
 private readonly Dictionary<string,(ulong Id,int Team)> roundPlayers=[];private readonly Dictionary<string,long> playClocks=[];
 private long nextOwnership;
 private ExtraStats Extra(IPlayer p){var x=Get(p).Extra;x.Since??=DateTimeOffset.UtcNow;return x;}
 private bool Opponents(IPlayer a,IPlayer v)=>Human(a)&&Human(v)&&a.PlayerID!=v.PlayerID&&Team(a)is 2 or 3&&Team(v)is 2 or 3&&(Ffa()||Team(a)!=Team(v));
 private void RegisterExtended()
 {
  Core.GameEvent.HookPost<EventItemPickup>(e=>{if(Live)ScanWeapons();return HookResult.Continue;});
  Core.GameEvent.HookPost<EventBombPlanted>(e=>{if(Live&&settings.PvpEnabled&&!Warmup()&&Human(e.UserIdPlayer))Extra(e.UserIdPlayer!).BombsPlanted++;return HookResult.Continue;});
  Core.GameEvent.HookPost<EventBombDefused>(e=>{if(Live&&settings.PvpEnabled&&!Warmup()&&Human(e.UserIdPlayer))Extra(e.UserIdPlayer!).BombsDefused++;return HookResult.Continue;});
 }
 private void BeginRound()
 {
  roundPlayers.Clear();if(!Live||!settings.PvpEnabled||Warmup())return;
  foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))if(Team(p)is 2 or 3)roundPlayers[Session(p)]=(p.SteamID,Team(p));
 }
 private void EndRound(EventRoundEnd e)
 {
  if(Live&&settings.PvpEnabled&&!Warmup())foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))
   if(roundPlayers.TryGetValue(Session(p),out var r)&&Team(p)==r.Team){var x=Extra(p);x.Rounds++;if((int)e.Winner==r.Team)x.Wins++;}
  roundPlayers.Clear();
 }
 private void ExtendedHurt(IPlayer a,IPlayer v,int amount)
 {if(settings.PvpEnabled&&!Warmup()&&Opponents(a,v)){Extra(a).Damage+=amount;Extra(v).DamageTaken+=amount;}}
 private void ExtendedDeath(EventPlayerDeath e,IPlayer a,IPlayer v)
 {
  if(!settings.PvpEnabled||Warmup()||!Opponents(a,v))return;var x=Extra(a);
  if(e.NoScope)x.NoScope++;if(e.Penetrated>0)x.Wallbang++;if(e.ThruSmoke)x.ThroughSmoke++;if(e.AttackerBlind)x.Blind++;if(e.AttackerInAir)x.InAir++;
  var assist=e.AssisterPlayer;if(assist is not null&&Opponents(assist,v)&&assist.PlayerID!=a.PlayerID){var ax=Extra(assist);ax.Assists++;if(e.AssistedFlash)ax.FlashAssists++;}
  var h=a.PlayerPawn?.WeaponServices?.ActiveWeapon;
  if(h is {} handle&&handle.Value is {IsValid:true} weapon&&StatPolicy.Weapon(e.Weapon)is {} w&&WeaponCatalog.Find("weapon_"+w)is not null&&StatPolicy.Weapon(Core.Helpers.GetClassnameByDefinitionIndex(weapon.AttributeManager.Item.ItemDefinitionIndex))==w)
  {
   // Read the known ownership BEFORE observing this kill. Unknown provenance stays unknown.
   var result=ownership.Kill(handle.Raw,Session(a));if(result.Origin==WeaponOrigin.Enemy)x.EnemyWeapon++;else if(result.Origin==WeaponOrigin.Teammate)x.TeamWeapon++;else if(result.Origin==WeaponOrigin.Unknown)x.UnknownWeaponOrigin++;
   if(result.Origin is WeaponOrigin.Enemy or WeaponOrigin.Teammate)x.BestPickupStreak=Math.Max(x.BestPickupStreak,result.Streak);
  }
 }
 private void ScanWeapons()
 {
  var held=new HashSet<uint>();bool ffa=Ffa();
  foreach(var p in Core.PlayerManager.GetAllValidPlayers())if(p.PlayerPawn is {IsValid:true} pawn&&pawn.WeaponServices is {} service)
   foreach(var h in service.MyWeapons)if(h.IsValid&&h.Value is {IsValid:true}){held.Add(h.Raw);knownWeapons[h.Raw]=h;ownership.Observe(h.Raw,Session(p),Team(p),ffa);}
  foreach(var (h,handle) in knownWeapons.ToArray())if(!handle.IsValid||handle.Value is not {IsValid:true})knownWeapons.Remove(h);
  ownership.FinishScan(held,knownWeapons.Keys.ToHashSet());
 }
 private void ExtendedTick(long now)
 {
  if(!Live||!settings.PvpEnabled){playClocks.Clear();ownership.Clear();knownWeapons.Clear();return;}
  if(now>=nextOwnership){nextOwnership=now+200;ScanWeapons();}
  if(now<nextStatus)return;
  var current=new HashSet<string>();bool warm=Warmup();
  foreach(var p in Core.PlayerManager.GetAllValidPlayers().Where(Human))
  {
   if(Team(p)is not (2 or 3)||warm)continue;string key=Session(p);current.Add(key);
   if(playClocks.TryGetValue(key,out long old)){long elapsed=Math.Clamp((now-old)/1000,0,5);if(elapsed>0){Extra(p).PlaySeconds+=elapsed;playClocks[key]=old+elapsed*1000;}}else playClocks[key]=now;
  }
  foreach(string key in playClocks.Keys.Where(k=>!current.Contains(k)).ToArray())playClocks.Remove(key);
 }
}
