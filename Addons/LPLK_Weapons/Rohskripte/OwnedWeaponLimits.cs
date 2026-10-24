namespace LPLK_Weapons;

// A VData definition is shared by all entities of that weapon. Track each field
// once by native definition address, not once per weapon entity.
public sealed class OwnedWeaponLimits
{
    private sealed record Change(int Original,int Applied,Func<int> Read,Action<int> Write);
    private readonly Dictionary<string,Change> changes=[];
    public int Count=>changes.Count;
    public void Set(string key,int value,Func<int> read,Action<int> write)
    {
        int before=read();int original=changes.TryGetValue(key,out var prior)&&before==prior.Applied?prior.Original:before;
        if(before==value){if(prior is not null)changes[key]=prior with{Original=original};return;}
        changes[key]=new(original,value,read,write);
        try{write(value);if(read()!=value)throw new InvalidOperationException("Waffenlimit nicht bestätigt.");}
        catch{if(read()==value)write(before);if(prior is null)changes.Remove(key);else changes[key]=prior;throw;}
    }
    public void Restore(Action<Exception> error)
    {
        foreach(var change in changes.Values)
        {
            try{if(change.Read()==change.Applied){change.Write(change.Original);if(change.Read()!=change.Original)throw new InvalidOperationException("Waffenlimit nicht zurückgesetzt.");}}
            catch(Exception e){error(e);}
        }
        changes.Clear();
    }
}
