using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Vec=System.Numerics.Vector3;
namespace LPLK_Fluch;

public sealed class NativeCurseWorld(ISwiftlyCore core,Func<IPlayer,bool> admin):ICurseWorld
{
    private static CBaseEntity? Entity(uint raw)=>new CHandle<CBaseEntity>{Raw=raw}.Value;
    public CursePlayer? Player(int slot)
    {
        var p=core.PlayerManager.GetPlayer(slot);
        if(p is not {IsValid:true,IsAuthorized:true,IsFakeClient:false}||!p.Controller.IsValid||p.PlayerPawn is not {IsValid:true} pawn||pawn.AbsOrigin is not {} pos)return null;
        return new(new(slot,p.SteamID,p.SessionId.ToString(),core.EntitySystem.GetRefEHandle(pawn).Raw,p.Controller.TeamNum),p.IsAlive&&p.Controller.TeamNum is 2 or 3,admin(p),new(pos.X,pos.Y,pos.Z));
    }
    public uint Spawn(Vec position,float yaw)
    {
        var entity=core.EntitySystem.CreateEntityByDesignerName<CBaseEntity>("chicken");if(entity is not {IsValid:true})return uint.MaxValue;
        try
        {
            using var kv=new CEntityKeyValues();kv.SetVector("origin",new(position.X,position.Y,position.Z));kv.SetQAngle("angles",new(0,yaw,0));kv.SetString("targetname","lplk_fluch_chicken");
            entity.DispatchSpawn(kv);return core.EntitySystem.GetRefEHandle(entity).Raw;
        }
        catch{if(entity.IsValid)entity.Despawn();throw;}
    }
    public bool Move(uint handle,Vec p,float yaw)
    {
        if(Entity(handle) is not {IsValid:true} entity)return false;
        entity.Teleport(new Vector(p.X,p.Y,p.Z),new QAngle(0,yaw,0),Vector.Zero);return true;
    }
    public Vec? Position(uint handle)=>Entity(handle) is {IsValid:true,AbsOrigin:{} p}?new(p.X,p.Y,p.Z):null;
    public void Destroy(uint handle){if(Entity(handle) is {IsValid:true} entity)entity.Despawn();}
    public void Damage(CurseOwner target,CurseOwner caster,float amount)
    {
        if(Player(target.Slot) is not {Alive:true} t||t.Owner!=target||Player(caster.Slot) is not {Alive:true,Admin:true} a||a.Owner!=caster)return;
        if(amount>0)core.PlayerManager.GetPlayer(target.Slot)!.TakeDamage(amount,DamageTypes_t.DMG_GENERIC,null,core.PlayerManager.GetPlayer(caster.Slot)!.PlayerPawn,null);
    }
    public void Notice(CurseOwner owner,string kind,float value,FluchSettings s)
    {
        if(Player(owner.Slot) is not {} p||p.Owner!=owner)return;var player=core.PlayerManager.GetPlayer(owner.Slot)!;
        if(kind=="start")
        {
            if(s.NotifyTargetInChat)player.SendChat("[LPLK] Du wurdest mit dem Hühnerfluch belegt.");
            if(s.ShowStartCenterMessage)player.SendCenterHTML($"<font color='orange'>Hühnerfluch!</font><br>{value:0} Hühner kreisen um deinen Kopf.",s.StartCenterMessageMs);
        }
        else if(kind=="end")
        {
            player.SendChat($"[LPLK] Der Hühnerfluch ist explodiert. Vorgesehener Schaden: {value:0.#}.");
            if(s.ShowExplosionCenterMessage)player.SendCenterHTML("<font color='red'>BOOM!</font><br>Der Hühnerfluch ist beendet.",s.ExplosionCenterMessageMs);
        }
        else if(kind=="progress")player.SendCenterHTML($"Hühnerfluch: noch {value:0.0} s",s.OrbitProgressMessageMs);
    }
}
