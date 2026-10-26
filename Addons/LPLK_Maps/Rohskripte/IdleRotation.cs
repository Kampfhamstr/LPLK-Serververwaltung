using LPLK.Common;
namespace LPLK_Maps;

// Monotonic time, no filesystem or network calls. Spectators count as humans.
public sealed class EmptyMapClock
{
 private long? since;
 public void Reset()=>since=null;
 public bool Due(long now,bool human,int minutes)
 {
  if(human||minutes<=0){since=null;return false;}
  since??=now;
  return now-since.Value>=minutes*60000L;
 }
}

// One cold-start transition, only after a real map has settled. A human joining
// cancels it for the whole process start; hot reloads never move the current map.
public sealed class StartupMapGate
{
 public bool Pending {get;private set;}
 private long deadline,settledAt;
 public void Arm(bool enabled,bool hotReload,long now){Pending=enabled&&!hotReload;deadline=now+120000;settledAt=now+5000;}
 public void MapLoaded(long now)=>settledAt=now+5000;
 public void Complete()=>Pending=false;
 public bool Due(long now,bool enabled,bool human,bool ready,bool busy)
 {
  if(!Pending)return false;
  if(!enabled||human||now>=deadline){Pending=false;return false;}
  return ready&&!busy&&now>=settledAt;
 }
}

public static partial class MapPolicy
{
 public static bool EmptyMapEligible(MapEntry m,MapSettings s,IReadOnlySet<string> installed)=>s.Enabled&&!s.MeasurementOnly&&
  m.Enabled&&m.InPool&&m.IsMap&&!m.Removed&&m.MinPlayers==1&&m.MaxPlayers>=1&&LimitsUsable(m,s)&&
  installed.Contains(m.Id)&&(!s.BuiltinMapsOnly||!m.Workshop)&&!s.ExcludedWorkshopIds.Contains(m.Id);
 public static MapEntry? NextEmptyMap(IEnumerable<MapEntry> choices,string current,string workshop,IReadOnlyList<string> history)
 {
  var other=choices.Where(m=>!Matches(m,current,workshop)).ToArray();if(other.Length==0)return null;
  int Last(string id){for(int i=history.Count-1;i>=0;i--)if(history[i]==id)return i;return -1;}
  int oldest=other.Min(m=>Last(m.Id));var leastRecent=other.Where(m=>Last(m.Id)==oldest).ToArray();
  return leastRecent[Random.Shared.Next(leastRecent.Length)];
 }
 public static string StartupMapCommand(MapEntry map)
 {
  if(!SafeMapToken(map.Id))throw new InvalidDataException("Ungültige Startmap");
  return (map.Workshop?"host_workshop_map ":"map ")+map.Id;
 }
}

public sealed partial class Plugin
{
 private readonly EmptyMapClock emptyMapClock=new();
 private readonly StartupMapGate startupMapGate=new();
 private bool idleTransition;
 private string preparedStartupMap="";
 private string startupRotationStatus="Nicht angefordert";
 private IEnumerable<MapEntry> EmptyMapChoices()=>maps.Where(m=>MapPolicy.EmptyMapEligible(m,settings,installed));
 private void TickEmptyRotation(long now)
 {
  bool human=AnyHuman();
  if(idleTransition&&!transitionIssued&&human){pending=null;nextMap=null;idleTransition=false;lastTransition="Leerlaufwechsel abgebrochen: Spieler verbunden";WriteStatus();}
  if(startupMapGate.Pending)return;
  if(!emptyMapClock.Due(now,human,settings.Enabled&&!settings.MeasurementOnly?settings.IdleRotationMinutes:0))return;
  if(pending is not null||nextMap is not null||voteEnds>0||probeId is not null||testDownload is {Ready:false,Failed:false})return;
  var selected=MapPolicy.NextEmptyMap(EmptyMapChoices(),CurrentMap,Workshop,history);
  emptyMapClock.Reset();if(selected is null)return;
  Schedule(selected,2);idleTransition=pending?.Id==selected.Id;
 }
 private bool CancelUnsafeIdleChange()
 {
  if(!idleTransition)return false;
  if(!AnyHuman()&&pending is {} m&&MapPolicy.EmptyMapEligible(m,settings,installed))return false;
  pending=null;nextMap=null;idleTransition=false;emptyMapClock.Reset();lastTransition="Leerlaufwechsel abgebrochen: Spieler oder Mapregeln geändert";WriteStatus();return true;
 }
 private void PrepareStartupMap()
 {
  if(!settings.RotateStartupMap||!startupMapGate.Pending)return;
  // Keep a chosen candidate stable until it is played or removed from the pool.
  var choices=EmptyMapChoices().ToArray();
  if(choices.Any(m=>m.Id==preparedStartupMap&&!MapPolicy.Matches(m,CurrentMap,Workshop)))return;
  var selected=MapPolicy.NextEmptyMap(choices,CurrentMap,Workshop,history);if(selected is null)return;
  preparedStartupMap=selected.Id;
 }
 private void TickStartupRotation(long now)
 {
  bool waiting=startupMapGate.Pending;
  if(!waiting)return;
  bool ready=CurrentMap.Length>0&&CurrentMap!="console"&&Core.EntitySystem.GetGameRules() is not null;
  bool busy=pending is not null||nextMap is not null||voteEnds>0||probeId is not null||scanTask is not null||collectionLoading||testDownload is {Ready:false,Failed:false};
  if(!startupMapGate.Due(now,settings.Enabled&&!settings.MeasurementOnly&&settings.RotateStartupMap,AnyHuman(),ready,busy))
  {
   if(waiting&&!startupMapGate.Pending){startupRotationStatus="Ausgelassen: Spieler, deaktiviert oder Zeitlimit; aktuelle Map bleibt";WriteStatus();}
   return;
  }
  PrepareStartupMap();
  var selected=EmptyMapChoices().FirstOrDefault(m=>m.Id==preparedStartupMap&&!MapPolicy.Matches(m,CurrentMap,Workshop));
  if(selected is null)return; // Wait for the cached scan, at most two minutes.
  startupMapGate.Complete();Schedule(selected,2);idleTransition=pending?.Id==selected.Id;
  startupRotationStatus=idleTransition?"Startmap angefordert: "+selected.Id:"Aktuelle Map beibehalten";WriteStatus();
 }
}
