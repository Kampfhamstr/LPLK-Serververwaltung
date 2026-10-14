using LPLK.Common;
namespace LPLK_NadeEffects;

public sealed record NadeSettings
{
    public bool Enabled {get;init;}=true;
    public string AdminPermission {get;init;}="@css/root";
    public string AdminMessagePermission {get;init;}="@css/root";
    public bool NotifyAdminsOnToggle {get;init;}=true;
    public bool NotifyAdminsOnTrigger {get;init;}=true;
    public string WarnMessage {get;init;}="Lerne Nades zu schmeissen";
    public bool UseCenterWarning {get;init;}=true;
    public int CenterWarningDurationMs {get;init;}=3000;
    public bool UseEnvFade {get;init;}=true;
    public bool EnvFadeActivatorOnly {get;init;}=true;
    public double FadeDuration {get;init;}=.15;
    public double FadeHoldTime {get;init;}=2.4;
    public string FadeColor {get;init;}="255 255 255";
    public int FadeAlpha {get;init;}=255;
    public float FadeEntityRemoveDelaySeconds {get;init;}=3;
    public bool RemoveNades {get;init;}=true;
    public bool SwitchToKnifeAfterRemove {get;init;}=true;
    public double TriggerCooldownSeconds {get;init;}=1.25;
    public bool EnableFlashbang {get;init;}=true;
    public bool EnableHEGrenade {get;init;}=true;
    public bool EnableSmoke {get;init;}=true;
    public bool EnableMolotov {get;init;}=true;
    public bool EnableIncendiary {get;init;}=true;
    public bool EnableDecoy {get;init;}=true;
    public bool EnableTAGrenade {get;init;}=true;
    public string SelectButton {get;init;}="E";
    public string MoveUpButton {get;init;}="W";
    public string MoveDownButton {get;init;}="S";
    public string ExitButton {get;init;}="Tab";
    public int MenuNameMaxLength {get;init;}=13;
    public static void Validate(NadeSettings s)
    {
        static void Range(double n,double min,double max){if(!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException("Ungültiger Zahlenwert.");}
        foreach(var p in new[]{s.AdminPermission,s.AdminMessagePermission})if(string.IsNullOrWhiteSpace(p)||p.Length>128||p.Any(char.IsWhiteSpace))throw new InvalidDataException("Ungültiges Adminrecht.");
        if(!TextValid(s.WarnMessage)||!ColorValid(s.FadeColor)||!s.EnvFadeActivatorOnly)throw new InvalidDataException("Text/Farbe ungültig oder Fade nicht auf Ziel begrenzt.");
        Range(s.FadeDuration,0,10);Range(s.FadeHoldTime,0,10);Range(s.FadeAlpha,0,255);Range(s.FadeEntityRemoveDelaySeconds,.1,30);Range(s.TriggerCooldownSeconds,.1,60);Range(s.CenterWarningDurationMs,100,10000);Range(s.MenuNameMaxLength,5,64);
        var keys=new[]{s.SelectButton,s.MoveUpButton,s.MoveDownButton,s.ExitButton};
        if(keys.Any(k=>k is not ("E" or "W" or "S" or "Tab" or "Space" or "A" or "D" or "R"))||keys.Distinct().Count()!=4)throw new InvalidDataException("Ungültige oder doppelte Menütasten.");
    }
    public static bool TextValid(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=160&&!s.Any(char.IsControl);
    public static bool ColorValid(string? s)=>s?.Split(' ',StringSplitOptions.RemoveEmptyEntries) is {Length:3} parts&&parts.All(p=>int.TryParse(p,out var n)&&n is >=0 and <=255);
}
public static class NadePolicy
{
    public static string Normalize(string? item){var s=item?.Trim().ToLowerInvariant()??"";return s.StartsWith("weapon_")?s[7..]:s;}
    public static bool Configured(NadeSettings s,string item)=>Normalize(item) switch
    {"flashbang"=>s.EnableFlashbang,"hegrenade"=>s.EnableHEGrenade,"smokegrenade"=>s.EnableSmoke,"molotov"=>s.EnableMolotov,"incgrenade"=>s.EnableIncendiary,"decoy"=>s.EnableDecoy,"tagrenade"=>s.EnableTAGrenade,_=>false};
    public static string[] Weapons(NadeSettings s)=>new[]{"flashbang","hegrenade","smokegrenade","molotov","incgrenade","decoy","tagrenade"}.Where(x=>Configured(s,x)).Select(x=>"weapon_"+x).ToArray();
    public static string Label(string item)=>Normalize(item) switch{"flashbang"=>"Flashbang","hegrenade"=>"HE-Granate","smokegrenade"=>"Smoke","molotov"=>"Molotov","incgrenade"=>"Incendiary","decoy"=>"Decoy","tagrenade"=>"TA-Granate",_=>"Granate"};
    public static bool CanSelect(NadePlayer actor,NadePlayer target)=>actor.Admin&&AdminActionPolicy.CanTarget(actor.Owner.Connection.Steam,target.Owner.Connection.Steam,actor.Rank,target.Rank);
    public static (int Index,int Pages,int Offset,int Size) Page(int count,int requested){int size=count<=4?4:2,pages=Math.Max(1,(count+size-1)/size),index=Math.Clamp(requested,0,pages-1);return(index,pages,index*size,size);}
}
public readonly record struct NadeConnection(int Slot,ulong Steam,ulong Session);
public readonly record struct NadeOwner(NadeConnection Connection,uint Pawn,int Team);
public sealed record NadePlayer(NadeOwner Owner,bool Alive,bool Admin,int Rank);
public interface INadeWorld
{
    NadePlayer? Player(int slot);
    uint Fade(NadeOwner owner,NadeSettings settings);
    void Destroy(uint handle);
    int Remove(NadeOwner owner,string[] weapons,bool knife);
    void Notice(NadeOwner owner,string item,NadeSettings settings,int removed);
}
public sealed class NadeController(INadeWorld world,Action<Exception> error)
{
    private sealed record Selection(NadeConnection Target,NadeConnection Actor);
    private sealed record Pending(NadeOwner Owner,Selection Selection,string Item,NadeSettings Settings,double Due);
    private sealed record FadeLease(uint Handle,NadeOwner Owner,double Expires);
    private readonly Dictionary<int,Selection> selected=[];
    private readonly Dictionary<int,Pending> pending=[];
    private readonly Dictionary<NadeOwner,double> cooldown=[];
    private readonly List<FadeLease> fades=[];
    public int Selected=>selected.Count;
    public int PendingCount=>pending.Count;
    public int FadeCount=>fades.Count;
    public int Triggered {get;private set;}
    public int Removed {get;private set;}
    public bool IsSelected(NadeConnection target)=>selected.TryGetValue(target.Slot,out var x)&&x.Target==target;
    public string Toggle(NadeConnection actor,NadeConnection target)
    {
        var a=world.Player(actor.Slot);var b=world.Player(target.Slot);
        if(a is null||b is null||a.Owner.Connection!=actor||b.Owner.Connection!=target||!NadePolicy.CanSelect(a,b))return "Spieler oder Zielberechtigung hat sich geändert.";
        if(selected.TryGetValue(target.Slot,out var old)&&old.Target==target)
        {selected.Remove(target.Slot);ReleaseEffects(target.Slot);return "AUS";}
        ReleaseEffects(target.Slot);selected[target.Slot]=new(target,actor);return "AN";
    }
    private bool Current(Selection s)
    {
        var a=world.Player(s.Actor.Slot);var b=world.Player(s.Target.Slot);
        return a is not null&&b is not null&&a.Owner.Connection==s.Actor&&b.Owner.Connection==s.Target&&NadePolicy.CanSelect(a,b);
    }
    public bool Equip(int slot,string item,NadeSettings settings,double now)
    {
        if(!settings.Enabled||!NadePolicy.Configured(settings,item)||!selected.TryGetValue(slot,out var choice)||!Current(choice)||world.Player(slot) is not {Alive:true} p)return false;
        if(pending.ContainsKey(slot)||cooldown.TryGetValue(p.Owner,out var until)&&now<until)return false;
        pending[slot]=new(p.Owner,choice,NadePolicy.Normalize(item),settings,now+.03);
        cooldown[p.Owner]=now+settings.TriggerCooldownSeconds;return true;
    }
    public void Tick(bool enabled,double now)
    {
        foreach(var pair in selected.ToArray())if(!Current(pair.Value)){selected.Remove(pair.Key);ReleaseEffects(pair.Key);}
        foreach(var f in fades.ToArray())if(!enabled||now>=f.Expires||world.Player(f.Owner.Connection.Slot) is not {Alive:true} p||p.Owner!=f.Owner){fades.Remove(f);Destroy(f.Handle);}
        foreach(var action in pending.Values.ToArray())
        {
            int slot=action.Owner.Connection.Slot;
            if(!pending.TryGetValue(slot,out var current)||!ReferenceEquals(current,action))continue;
            if(!enabled||!selected.TryGetValue(slot,out var choice)||choice!=action.Selection||!Current(choice)||world.Player(slot) is not {Alive:true} p||p.Owner!=action.Owner){pending.Remove(slot);continue;}
            if(now<action.Due)continue;pending.Remove(slot);
            var s=action.Settings;
            // Replace the prior fade for this target; repeated equip cannot accumulate entities.
            foreach(var old in fades.Where(f=>f.Owner.Connection.Slot==slot).ToArray()){fades.Remove(old);Destroy(old.Handle);}
            if(s.UseEnvFade)try{uint h=world.Fade(action.Owner,s);if(h==uint.MaxValue)throw new InvalidOperationException("Fade fehlt");fades.Add(new(h,action.Owner,now+s.FadeEntityRemoveDelaySeconds));}catch(Exception e){error(e);}
            int removed=0;
            try{if(s.RemoveNades)removed=world.Remove(action.Owner,NadePolicy.Weapons(s),s.SwitchToKnifeAfterRemove);Removed+=removed;}catch(Exception e){removed=-1;error(e);}
            try{world.Notice(action.Owner,action.Item,s,removed);}catch(Exception e){error(e);}
            Triggered++;
        }
    }
    private void Destroy(uint h){try{world.Destroy(h);}catch(Exception e){error(e);}}
    public void ReleaseEffects(int slot)
    {
        pending.Remove(slot);foreach(var k in cooldown.Keys.Where(k=>k.Connection.Slot==slot).ToArray())cooldown.Remove(k);
        foreach(var f in fades.Where(f=>f.Owner.Connection.Slot==slot).ToArray()){fades.Remove(f);Destroy(f.Handle);}
    }
    public void Disconnect(int slot){selected.Remove(slot);foreach(var x in selected.Where(x=>x.Value.Actor.Slot==slot).Select(x=>x.Key).ToArray()){selected.Remove(x);ReleaseEffects(x);}ReleaseEffects(slot);}
    public void CancelEffects(){pending.Clear();cooldown.Clear();foreach(var f in fades)Destroy(f.Handle);fades.Clear();}
    public void Clear(){selected.Clear();CancelEffects();}
    public void ForgetMap(){pending.Clear();cooldown.Clear();fades.Clear();}
}
