using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Weapons;
public sealed partial class Plugin
{
    private readonly OwnedWeaponLimits ammoLimits=new();
    private readonly Dictionary<int,long> reloadRequests=[];
    private long reloadSequence;
    private bool nativeReady;
    private void ApplyAmmoLimits(CCSWeaponBase weapon,WeaponRule rule)
    {
        if(rule.Clip is null&&rule.Ammo is null)return;
        ApplyAmmoDefinition(weapon.WeaponBaseVData,rule);
    }
    private void ApplyAmmoDefinition(CCSWeaponBaseVData data,WeaponRule rule)
    {
        if(!data.IsValid)return;
        string key=data.Address.ToString();
        int ReadClip(){if(!data.IsValid)throw new InvalidOperationException("Waffendefinition ungültig.");return data.MaxClip1;}
        int ReadAmmo(){if(!data.IsValid)throw new InvalidOperationException("Waffendefinition ungültig.");return data.PrimaryReserveAmmoMax;}
        if(rule.Clip is {} clip)ammoLimits.Set(key+":clip",clip,ReadClip,v=>{if(!data.IsValid)throw new InvalidOperationException("Waffendefinition ungültig.");data.MaxClip1=v;});
        if(rule.Ammo is {} ammo)ammoLimits.Set(key+":ammo",ammo,ReadAmmo,v=>{if(!data.IsValid)throw new InvalidOperationException("Waffendefinition ungültig.");data.PrimaryReserveAmmoMax=v;});
    }
    [EventListener<EventDelegates.OnEntitySpawned>]
    public void WeaponSpawn(IOnEntitySpawnedEvent e)
    {
        if(!module.Active||!settings.Enabled||settings.Rules.Count==0||!e.Entity.IsValid||!e.Entity.DesignerName.StartsWith("weapon_",StringComparison.Ordinal))return;
        try{var weapon=e.Entity.As<CCSWeaponBase>();if(settings.Rules.TryGetValue(WeaponName(weapon),out var rule))ApplyAmmoLimits(weapon,rule);}
        catch(Exception error){Error(error);}
    }
    [EventListener<EventDelegates.OnMapLoad>]
    public void AmmoMapLoad(IOnMapLoadEvent e){nativeReady=true;}
    private HookResult WeaponEquip(EventItemEquip e)
    {
        if(!module.Active||!settings.Enabled||!nativeReady||settings.Rules.Count==0)return HookResult.Continue;
        try
        {
            if(e.UserIdPlayer?.PlayerPawn?.WeaponServices?.ActiveWeapon.Value is {IsValid:true} active&&settings.Rules.TryGetValue(WeaponName(active),out var rule))ApplyAmmoLimits(active.As<CCSWeaponBase>(),rule);
        }
        catch(Exception error){Error(error);}
        return HookResult.Continue;
    }
    private HookResult WeaponFire(EventWeaponFire e)
    {
        if(!module.Active||!settings.Enabled||!nativeReady||settings.Rules.Count==0)return HookResult.Continue;
        try
        {
            if(e.UserIdPlayer is not {IsValid:true,IsAlive:true} p||p.PlayerPawn?.WeaponServices?.ActiveWeapon.Value is not {IsValid:true} active)return HookResult.Continue;
            if(!settings.Rules.TryGetValue(WeaponName(active),out var rule))return HookResult.Continue;
            ApplyAmmoLimits(active.As<CCSWeaponBase>(),rule);RefillAmmo(active.As<CCSWeaponBase>(),rule);
            if(rule.ReloadAfterShoot)QueueWeaponCycle(p,active);
        }
        catch(Exception error){Error(error);}
        return HookResult.Continue;
    }
    private static void RefillAmmo(CCSWeaponBase active,WeaponRule rule)
    {
        // Preserve AWS: one unit per weapon_fire, bounded only against overflow.
        // PrimaryReserveAmmoMax is a definition value (e.g. 3 magazines), not
        // the live ReserveAmmo bullet count (e.g. 90). Never clamp bullets to it.
        if(rule.UnlimitedClip){active.Clip1=WeaponPolicy.Replenish(active.Clip1,int.MaxValue);active.Clip1Updated();}
        if(rule.UnlimitedAmmo){active.ReserveAmmo[0]=WeaponPolicy.Replenish(active.ReserveAmmo[0],int.MaxValue);active.ReserveAmmoUpdated();}
    }
    private void QueueWeaponCycle(IPlayer player,CBasePlayerWeapon active)
    {
        if(player.PlayerPawn is not {IsValid:true} pawn||pawn.WeaponServices is not {} services)return;
        uint weaponRaw=Core.EntitySystem.GetRefEHandle(active).Raw;
        var other=services.MyWeapons.FirstOrDefault(h=>h.Raw!=weaponRaw&&h.Value is {IsValid:true});if(other.Value is not {IsValid:true})return;
        int slot=player.PlayerID;ulong steam=player.SteamID;string session=player.SessionId.ToString();uint pawnRaw=Core.EntitySystem.GetRefEHandle(pawn).Raw,otherRaw=other.Raw;
        int team=player.Controller.TeamNum;long life=lives.GetValueOrDefault(slot),request=++reloadSequence;reloadRequests[slot]=request;
        var cycle=new WeaponCycle(weaponRaw,otherRaw);
        void Select(bool returning)
        {
            try
            {
                if(!module.Active||!nativeReady||!settings.Enabled||reloadRequests.GetValueOrDefault(slot)!=request||lives.GetValueOrDefault(slot)!=life)return;
                var p=Core.PlayerManager.GetPlayer(slot);
                if(p is not {IsValid:true,IsAlive:true}||p.SteamID!=steam||p.SessionId.ToString()!=session||p.Controller.TeamNum!=team||p.PlayerPawn is not {IsValid:true} currentPawn||Core.EntitySystem.GetRefEHandle(currentPawn).Raw!=pawnRaw||currentPawn.WeaponServices is not {} weapons)return;
                uint next=returning?weaponRaw:otherRaw;
                if(!weapons.MyWeapons.Any(h=>h.Raw==next)||!weapons.MyWeapons.Any(h=>h.Raw==weaponRaw))return;
                var original=new SwiftlyS2.Shared.Natives.CHandle<CBasePlayerWeapon>{Raw=weaponRaw}.Value;
                var target=new SwiftlyS2.Shared.Natives.CHandle<CBasePlayerWeapon>{Raw=next}.Value;
                if(original is not {IsValid:true}||target is not {IsValid:true}||!settings.Rules.TryGetValue(WeaponName(original),out var currentRule)||!currentRule.ReloadAfterShoot)return;
                cycle.Step(returning,weapons.ActiveWeapon.Raw,_=>{weapons.SelectWeapon(target);return weapons.ActiveWeapon.Raw==next;});
            }
            catch(Exception error){Error(error);}
        }
        Core.Scheduler.DelayBySeconds(.1f,()=>Select(false));Core.Scheduler.DelayBySeconds(.2f,()=>Select(true));
    }
    private void ResetAmmo(){reloadRequests.Clear();ammoLimits.Restore(Error);}
}
