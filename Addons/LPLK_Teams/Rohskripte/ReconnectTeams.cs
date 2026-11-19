namespace LPLK_Teams;

public sealed record ReturnConnection(ConnectionKey Key,int Team,bool Returned=false);
public sealed record ReturnTeamsSnapshot(Dictionary<ulong,int> Pending,ReturnConnection[] Connected);

// Authenticated Steam identity, actual connection and map-bound state; no names.
public sealed class ReconnectTeams
{
    private readonly Dictionary<ulong,int> pending=[];
    private readonly Dictionary<int,ReturnConnection> connected=[];
    public int PendingCount=>pending.Count;
    public void Clear(){pending.Clear();connected.Clear();}
    public bool CanReturn(ConnectionKey key,int current,int requested)
        =>current is not (2 or 3)&&pending.TryGetValue(key.Steam,out int team)&&(requested==team||requested==0);
    public bool Restored(ConnectionKey key,int team)
        =>connected.TryGetValue(key.Slot,out var c)&&c.Key==key&&c.Team==team&&c.Returned;
    public bool TryReturn(ConnectionKey key,int current,int team)
    {
        if(team is not (2 or 3)||!CanReturn(key,current,team))return false;
        pending.Remove(key.Steam);connected[key.Slot]=new(key,team,true);return true;
    }
    public bool Observe(ConnectionKey key,int team)
    {
        if(team is <0 or >3)return false;
        connected.TryGetValue(key.Slot,out var old);
        bool returned=old?.Key==key&&old.Team==team&&old.Returned;
        var next=new ReturnConnection(key,team,returned);
        if(old==next)return false;
        connected[key.Slot]=next;
        // A new connection's unassigned/spectator state keeps its return ticket.
        // Choosing a playing team, or leaving it voluntarily, consumes that ticket.
        if(team is 2 or 3||old?.Key==key&&old.Team is 2 or 3)pending.Remove(key.Steam);
        return true;
    }
    public bool Disconnect(int slot)
    {
        if(!connected.Remove(slot,out var old))return false;
        if(old.Team is 2 or 3)
        {
            if(pending.Count>=1024&&!pending.ContainsKey(old.Key.Steam))pending.Remove(pending.Keys.First());
            pending[old.Key.Steam]=old.Team;
        }
        return true;
    }
    public ReturnTeamsSnapshot Snapshot()=>new(new(pending),connected.Values.ToArray());
    public void Restore(ReturnTeamsSnapshot saved)
    {
        static bool Steam(ulong id)=>id is >=76561197960265728UL and <=76561202255233023UL;
        if(saved.Pending is null||saved.Connected is null||saved.Pending.Count>1024||saved.Connected.Length>128
            ||saved.Pending.Any(p=>!Steam(p.Key)||p.Value is not (2 or 3))
            ||saved.Connected.Any(c=>c is null||!Steam(c.Key.Steam)||c.Key.Slot is <0 or >127||c.Team is <0 or >3)
            ||saved.Connected.Select(c=>c.Key.Slot).Distinct().Count()!=saved.Connected.Length)
            throw new InvalidDataException("Ungültiger Team-Rückkehrzustand.");
        Clear();foreach(var p in saved.Pending)pending[p.Key]=p.Value;foreach(var c in saved.Connected)connected[c.Key.Slot]=c;
    }
    public bool ReconcileMissing(IEnumerable<ConnectionKey> current)
    {
        var keys=current.ToHashSet();bool changed=false;
        foreach(var c in connected.Values.ToArray())if(!keys.Contains(c.Key))changed|=Disconnect(c.Key.Slot);
        return changed;
    }
}
