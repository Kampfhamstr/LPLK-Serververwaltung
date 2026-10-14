using LPLK_Sounds;

public static class SoundSendTests
{
    public static void Run(Action<bool,string> check)
    {
        var receiver=new SoundListener(7,76561198000000002,true,true,false,true,false,25);
        var preference=new SoundListeningPreference { VolumePercent=25 };
        foreach(bool setting in new[]{false,true})
        foreach(bool sourceAlive in new[]{false,true})
        foreach(bool targetAlive in new[]{false,true})
        {
            string? reason=SoundSendPolicy.Rejection(sourceAlive,setting,receiver with {Alive=targetAlive},preference,"gong");
            check((reason is null)==(!setting||sourceAlive||!targetAlive),$"Directed sound rule: enabled={setting}, sender alive={sourceAlive}, receiver alive={targetAlive}");
        }
        check(SoundSendPolicy.Rejection(true,true,receiver,preference with {Enabled=false},"gong")!.Contains("Sounds ausgeschaltet"),"Disabled recipient produces explicit sounds-off feedback");
        check(SoundSendPolicy.Rejection(true,true,receiver,preference with {VolumePercent=0},"gong")!.Contains("0%"),"Zero-volume recipient produces explicit feedback");
        check(SoundSendPolicy.Rejection(true,true,receiver,preference with {MutedSounds=["gong"]},"gong")!.Contains("diesen Sound"),"Muted individual sound cannot be sent");
        check(SoundSendPolicy.Rejection(true,true,receiver,preference with {HearDead=false},"gong")!.Contains("aktuellen Zustand"),"Dead/spectator hearing preference blocks directed sounds");
        check(SoundSendPolicy.Rejection(true,true,receiver with {Alive=true},preference with {HearAlive=false},"gong") is not null,"Alive hearing preference blocks directed sounds");
        foreach(var invalid in new[]{receiver with {Valid=false},receiver with {Authorized=false},receiver with {Fake=true},receiver with {SteamId=0},receiver with {Slot=64}})
            check(SoundSendPolicy.Rejection(true,false,invalid,preference,"gong")!.Contains("nicht mehr verfuegbar"),"Unavailable recipient cannot receive directed audio");
        foreach(int team in new[]{0,1})
            check(!SoundAudience.IsPlayingAlive(true,team),"Unassigned/spectator team is dead-side even with a stale alive bit");
        var groups=SoundAudience.Groups([receiver],deadOnly:true);
        var emitted=new List<SoundVolumeGroup>();
        var sound=SoundBroadcast.Start("gong",1000,3,groups,g=>{emitted.Add(g);return 42;},(_,_,_)=>{},deadOnly:true);
        check(emitted.Count==1 && emitted[0].VolumePercent==25 && emitted[0].Recipients.Keys.SequenceEqual(new[]{7}),"Directed playback emits only the chosen recipient at their own volume");
        check(sound.Recipients.Single().Value.SteamId==receiver.SteamId && sound.DeadOnly && !sound.AdminOverride,"Directed playback retains receiver identity and dead-only rule without override");
        check(!SoundAudience.Eligible(receiver with {Alive=true},sound.DeadOnly),"A dead-only directed sound stops being eligible when the recipient becomes alive");
        check(!SoundAudience.SameConnection(7,receiver.SteamId,receiver with {SteamId=76561198000000003}),"Reused receiver slots cannot inherit a directed sound");
        var limit=new SoundSendLimit();
        check(limit.TryStart(1,2,()=>true)==SoundStartResult.Started && limit.TryStart(1,2,()=>throw new Exception("must not emit"))==SoundStartResult.LimitReached,"One private sound per sender/recipient pair per round");
        check(limit.TryStart(1,3,()=>true)==SoundStartResult.Started && limit.TryStart(3,2,()=>true)==SoundStartResult.Started && limit.TryStart(2,1,()=>true)==SoundStartResult.Started,"Different receivers, senders and reversed pairs have independent private quotas");
        check(limit.TryStart(1,2,()=>true,bypassLimit:true)==SoundStartResult.Started && limit.TryStart(1,2,()=>true,bypassLimit:true)==SoundStartResult.Started,"Admins bypass the private pair limit repeatedly");
        check(limit.TryStart(1,2,()=>true)==SoundStartResult.LimitReached,"Revoking admin rights restores the used pair limit");
        limit.Reset();
        check(limit.TryStart(1,2,()=>false)==SoundStartResult.Failed && limit.TryStart(1,2,()=>true)==SoundStartResult.Started,"Failed private attempts do not consume a pair allowance; rounds reset it");
        try { limit.TryStart(4,5,()=>throw new IOException("failure")); } catch(IOException) { }
        check(limit.TryStart(4,5,()=>true)==SoundStartResult.Started,"Emitter exceptions release the private reservation");
        int started=0;
        Parallel.For(0,20,_=>{if(limit.TryStart(7,8,()=>true)==SoundStartResult.Started)Interlocked.Increment(ref started);});
        check(started==1,"Concurrent sends cannot exceed a pair's allowance");
        string temp=Path.Combine(Path.GetTempPath(),"sound-blocks-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var prefs=new SoundPreferenceStore(temp);
            prefs.Set("76561198000000002",new SoundListeningPreference(){BlockedSoundSenders=["76561198000000001"]});
            check(new SoundPreferenceStore(temp).Get("76561198000000002").BlockedSoundSenders.SetEquals(["76561198000000001"]),"Sender blocks persist across restart by Steam identity");
            prefs.Set("76561198000000002",false);
            check(prefs.Get("76561198000000002").BlockedSoundSenders.Count==1,"Changing master listening preserves sender blocks");
            bool invalid=false;try{prefs.Set("76561198000000002",new SoundListeningPreference(){BlockedSoundSenders=["bad"]});}catch(InvalidDataException){invalid=true;}
            check(invalid && prefs.Get("76561198000000002").BlockedSoundSenders.Count==1,"Invalid block identities cannot overwrite preferences");
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
