namespace LPLK_Statistics;
public enum WeaponOrigin{Unknown,Own,Teammate,Enemy}
// The serial is part of Raw, so a recycled entity index cannot inherit another gun's owner.
public sealed class WeaponOwnership
{
 private sealed class Entry{public string Last="";public int Team;public bool Held;public WeaponOrigin Origin;public long Kills;}
 private readonly Dictionary<uint,Entry> weapons=[];
 public void Observe(uint handle,string owner,int team,bool ffa)
 {
  if(!weapons.TryGetValue(handle,out var e)){weapons[handle]=new(){Last=owner,Team=team,Held=true};return;}
  if(e.Last!=owner){e.Origin=e.Team is 2 or 3&&team is 2 or 3?(ffa||e.Team!=team?WeaponOrigin.Enemy:WeaponOrigin.Teammate):WeaponOrigin.Unknown;e.Kills=0;}
  else if(!e.Held){e.Origin=WeaponOrigin.Own;e.Kills=0;}
  e.Last=owner;e.Team=team;e.Held=true;
 }
 public void FinishScan(IReadOnlySet<uint> held,IReadOnlySet<uint> existing)
 {
  foreach(var (h,e) in weapons.ToArray()){if(!existing.Contains(h)){weapons.Remove(h);continue;}if(!held.Contains(h)){e.Held=false;e.Kills=0;}}
 }
 public (WeaponOrigin Origin,long Streak) Kill(uint handle,string owner)
 {
  if(!weapons.TryGetValue(handle,out var e)||!e.Held||e.Last!=owner)return(WeaponOrigin.Unknown,0);
  return(e.Origin,++e.Kills);
 }
 public void EndLife(string owner){foreach(var e in weapons.Values.Where(e=>e.Last==owner)){e.Held=false;e.Kills=0;}}
 public void Clear()=>weapons.Clear();
}
