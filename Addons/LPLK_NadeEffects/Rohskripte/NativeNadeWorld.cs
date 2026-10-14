using System.Globalization;
using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
namespace LPLK_NadeEffects;

public sealed class NativeNadeWorld(ISwiftlyCore core,Func<IPlayer,bool> admin,Action<string,NadeSettings> notify):INadeWorld
{
    private static CEnvFade? Entity(uint raw)=>new CHandle<CEnvFade>{Raw=raw}.Value;
    public NadePlayer? Player(int slot)
    {
        var p=core.PlayerManager.GetPlayer(slot);if(p is not {IsValid:true,IsAuthorized:true,IsFakeClient:false}||p.Controller is not {IsValid:true})return null;
        var pawn=p.PlayerPawn;uint raw=pawn is {IsValid:true}?core.EntitySystem.GetRefEHandle(pawn).Raw:uint.MaxValue;
        bool canAdmin=admin(p);int rank=core.Permission.PlayerHasPermission(p.SteamID,"@css/root")?100:Math.Max(canAdmin?10:0,AdminActionPolicy.Rank(x=>core.Permission.PlayerHasPermission(p.SteamID,x)));
        return new(new(new(slot,p.SteamID,p.SessionId),raw,p.Controller.TeamNum),raw!=uint.MaxValue&&p.IsAlive&&p.Controller.TeamNum is 2 or 3,canAdmin,rank);
    }
    private IPlayer? Same(NadeOwner owner)=>Player(owner.Connection.Slot) is {Alive:true} p&&p.Owner==owner?core.PlayerManager.GetPlayer(owner.Connection.Slot):null;
    public uint CreateFade(NadeSettings s)
    {
        var fade=core.EntitySystem.CreateEntityByDesignerName<CEnvFade>("env_fade");if(fade is not {IsValid:true})return uint.MaxValue;
        try
        {
            using var kv=new CEntityKeyValues();kv.SetString("targetname","lplk_nadeeffects_fade");kv.SetString("duration",s.FadeDuration.ToString(CultureInfo.InvariantCulture));kv.SetString("holdtime",s.FadeHoldTime.ToString(CultureInfo.InvariantCulture));
            kv.SetString("rendercolor",s.FadeColor);kv.SetString("renderamt",s.FadeAlpha.ToString(CultureInfo.InvariantCulture));kv.SetString("spawnflags","4");
            fade.DispatchSpawn(kv);return core.EntitySystem.GetRefEHandle(fade).Raw;
        }
        catch{if(fade.IsValid)fade.Despawn();throw;}
    }
    public bool VerifyFade(uint h,NadeSettings s)=>Entity(h) is {IsValid:true} e&&Math.Abs(e.Duration-s.FadeDuration)<.001&&Math.Abs(e.HoldDuration-s.FadeHoldTime)<.001&&e.Spawnflags==4;
    public void ProbeInput(uint h)=>Entity(h)?.AcceptInput<string>("Fade",string.Empty,null,null);
    public bool Exists(uint h)=>Entity(h) is {IsValid:true};
    public uint Fade(NadeOwner owner,NadeSettings settings)
    {
        if(Same(owner) is null)throw new InvalidOperationException("Spielerfigur geändert");
        uint h=CreateFade(settings);if(h==uint.MaxValue)return h;
        try{var p=Same(owner)??throw new InvalidOperationException("Spielerfigur geändert");Entity(h)!.AcceptInput<string>("Fade",string.Empty,p.PlayerPawn,p.PlayerPawn);return h;}
        catch{Destroy(h);throw;}
    }
    public void Destroy(uint handle){if(Entity(handle) is {IsValid:true} entity)entity.Despawn();}
    public int Remove(NadeOwner owner,string[] weapons,bool knife)
    {
        var p=Same(owner)??throw new InvalidOperationException("Spielerfigur geändert");
        var services=p.PlayerPawn?.WeaponServices??throw new InvalidOperationException("Waffenverwaltung fehlt");
        var candidates=services.MyWeapons.Where(h=>h.Value is {IsValid:true} w&&weapons.Contains(w.DesignerName)).Select(h=>h.Raw).ToArray();int removed=0;
        foreach(uint raw in candidates)
        {
            p=Same(owner)??throw new InvalidOperationException("Spielerfigur geändert");services=p.PlayerPawn!.WeaponServices!;
            var weapon=new CHandle<CBasePlayerWeapon>{Raw=raw}.Value;
            if(weapon is not {IsValid:true}||!weapons.Contains(weapon.DesignerName)||!services.MyWeapons.Any(h=>h.Raw==raw))continue;
            services.RemoveWeapon(weapon);removed++;
        }
        if(knife&&Same(owner) is {} current)current.ExecuteCommand("slot3");
        return removed;
    }
    public static string Html(string s)=>s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");
    public static string Chat(string s)=>new string(s.Where(c=>!char.IsControl(c)).ToArray()).Replace('[','(').Replace(']',')');
    public void Notice(NadeOwner owner,string item,NadeSettings settings,int removed)
    {
        if(Same(owner) is not {} p)return;string label=NadePolicy.Label(item);
        string outcome=!settings.RemoveNades?"":removed<0?" Granaten konnten nicht vollständig entfernt werden.":removed==0?" Keine passenden Granaten im Inventar.":" Konfigurierte Granaten entfernt.";
        p.SendChat($"[LPLK] {label}: {Chat(settings.WarnMessage)}"+outcome);
        if(settings.UseCenterWarning)p.SendCenterHTML($"<font color='#ff5555'>Granatenhinweis</font><br>{Html(settings.WarnMessage)}<br>{label}",settings.CenterWarningDurationMs);
        if(settings.NotifyAdminsOnTrigger)notify($"Granateneffekt: {Chat(p.Name)}, {label}."+outcome,settings);
    }
}
