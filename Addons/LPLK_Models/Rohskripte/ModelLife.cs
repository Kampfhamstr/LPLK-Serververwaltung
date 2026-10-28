namespace LPLK_Models;

// RAM-only lifetime state: never reuse a baseline across sessions, pawns, teams or maps.
public readonly record struct ModelIdentity(int Slot,string Session,ulong Steam,uint Pawn,int Team,int Map);
public sealed class ModelLife
{
 private readonly Dictionary<int,(ModelIdentity Owner,string Path)> originals=[];
 private readonly Dictionary<int,long> revisions=[];
 private long nextRevision;
 private HashSet<string> customModels=new(StringComparer.OrdinalIgnoreCase);
 public void ConfigureModels(IEnumerable<string> paths)=>customModels=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
 public void Capture(ModelIdentity owner,string path,bool naturalSpawn)
 {
  if(string.IsNullOrWhiteSpace(path)||customModels.Contains(path)||path.StartsWith("characters/models/nozb1/",StringComparison.OrdinalIgnoreCase))return;
  if(naturalSpawn||!originals.TryGetValue(owner.Slot,out var saved)||saved.Owner!=owner)originals[owner.Slot]=(owner,path);
 }
 public string? Original(ModelIdentity owner)=>originals.TryGetValue(owner.Slot,out var saved)&&saved.Owner==owner?saved.Path:null;
 public long Queue(int slot){long revision=++nextRevision;revisions[slot]=revision;return revision;}
 public bool Current(int slot,long revision)=>revisions.GetValueOrDefault(slot)==revision;
 public void Clear(){originals.Clear();revisions.Clear();}
 public void Remove(int slot){originals.Remove(slot);revisions.Remove(slot);}
}
