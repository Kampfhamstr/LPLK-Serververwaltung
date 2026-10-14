using LPLK_Sounds;

public static class SoundOverrideTests
{
    public static void Run(Action<bool,string> check)
    {
        var muted=new SoundListener(1,76561198000000001,true,true,false,false,true,0);
        var quiet=muted with {Slot=2,SteamId=76561198000000002,Enabled=true,Alive=false,VolumePercent=25};
        var invalid=muted with {Slot=3,Valid=false};
        var bot=muted with {Slot=4,Fake=true};
        var pending=muted with {Slot=5,Authorized=false};
        var snapshot=new[]{muted,quiet,invalid,bot,pending,muted with {Slot=64},muted with {Slot=-1},muted with {Slot=6,SteamId=0},muted};
        var forced=SoundAudience.Groups(snapshot,true,true);
        check(forced.Length==1&&forced[0].VolumePercent==100&&forced[0].Recipients.Keys.Order().SequenceEqual([1,2]),
            "Override reaches muted living and quiet dead humans once at catalog volume; invalid slots/bots/pending clients excluded");
        var normal=SoundAudience.Groups(snapshot,true);
        check(normal.Length==1&&normal[0].VolumePercent==25&&normal[0].Recipients.Keys.SequenceEqual([2]),
            "Normal playback retains personal volume and mute after an override calculation");
        check(muted.VolumePercent==0&&!muted.Enabled&&quiet.VolumePercent==25,"Override never rewrites personal preference snapshots");
        var off=new SoundListeningPreference {Enabled=false,HearAlive=false,HearDead=false,VolumePercent=0,MutedSounds=["gong"]};
        var allFiltered=muted with {Enabled=off.Allows(true,"gong")};
        check(SoundAudience.Eligible(allFiltered,true,true)&&!SoundAudience.Eligible(allFiltered,true),
            "Master, state, specific mute, zero volume and dead-only rule are bypassed only for explicit override");
        var active=SoundBroadcast.Start("gong",100,3,forced,_=>80,(_,_,_)=>{},true,true);
        check(active.AdminOverride&&!active.DeadOnly&&active.SoundId=="gong"&&active.Recipients.Length==2,
            "Broadcast retains override mode and cannot accidentally keep a dead-only spawn restriction");
        check(SoundAudience.Eligible(muted with {Alive=false},active.DeadOnly,active.AdminOverride)&&
            SoundAudience.Eligible(muted with {Alive=true},active.DeadOnly,active.AdminOverride),
            "Active override remains eligible across listener death and spawn");
        var regular=SoundBroadcast.Start("gong",100,3,normal,_=>81,(_,_,_)=>{},true);
        check(!regular.AdminOverride&&regular.DeadOnly,"Subsequent regular broadcasts do not inherit admin override");
        check(active.Expired(3100)&&!active.Expired(3099),"Override retains normal duration cleanup");
        check(!SoundAudience.SameConnection(1,muted.SteamId,muted with {SteamId=quiet.SteamId})&&
            !SoundAudience.Eligible(muted with {Valid=false},false,true),"Override never follows stale slots or disconnected players");
        var quota=new SoundRoundLimit();
        for(int i=0;i<3;i++)quota.TryStart(muted.SteamId,()=>{_=SoundBroadcast.Start("gong",0,3,forced,_=>90,(_,_,_)=>{},adminOverride:true);return true;});
        check(quota.TryStart(muted.SteamId,()=>throw new Exception("Must not start a fourth override"))==SoundStartResult.LimitReached,
            "Audience override does not bypass the shared three-sound quota");
    }
}
