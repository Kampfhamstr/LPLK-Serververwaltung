using LPLK_Sounds;
using System.Text.Json;

public static class SoundListeningTests
{
    public static void Run(Action<bool,string> check)
    {
        const string alice="76561198000000001", bob="76561198000000002";
        string root=Path.Combine(Path.GetTempPath(),"dc-listening-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"sound-listening.json");
            string legacy=$"{{\"{alice}\":false,\"{bob}\":true}}";
            File.WriteAllText(path,legacy);
            var store=new SoundPreferenceStore(path);
            check(!store.Get(alice).Enabled && store.Get(bob).Enabled && store.Get(alice).VolumePercent==100,
                "Boolean legacy preferences migrate without unmuting an existing listener");
            check(File.ReadAllText(path)==legacy,"Migration does not rewrite old preferences until a setting changes");
            store.Set(alice,store.Get(alice) with { VolumePercent=35,HearAlive=false,MutedSounds=["gong"] });
            store=new SoundPreferenceStore(path);
            var p=store.Get(alice);
            check(!p.Enabled && p.VolumePercent==35 && !p.HearAlive && p.HearDead && p.MutedSounds.SetEquals(["gong"]),
                "Volume, both state rules and individual mute survive restart together");
            check(store.Get(bob).Enabled && store.Get(bob).VolumePercent==100 && store.Get(bob).MutedSounds.Count==0,
                "Changing one player preserves a second player's legacy choice");
            store.Set(alice,true); p=store.Get(alice);
            check(p.VolumePercent==35 && !p.HearAlive && p.MutedSounds.Contains("gong"),"Master toggle preserves volume and finer filters");
            check(!p.Allows(true,"new_sound") && p.Allows(false,"new_sound"),"Alive and dead hearing use the listener's state; new sounds are allowed");
            check(!p.Allows(false,"gong"),"Per-sound mute also blocks otherwise eligible dead listeners");
            check(!(p with { VolumePercent=0 }).Allows(false,"new_sound"),"Zero volume excludes a recipient even when enabled");
            check(!(p with { HearDead=false }).Allows(false),"Both state filters off never delivers audio");
            check(Math.Abs(p.Volume(.8f)-.28f)<.00001f,"Personal percentage multiplies the catalog volume without amplifying it");
            var standard=new SoundListeningPreference();
            check(standard.Enabled&&standard.HearAlive&&standard.HearDead&&standard.VolumePercent==100&&standard.MutedSounds.Count==0,
                "New listeners retain the previous default playback behavior");
            foreach(string bad in new[]{
                $"{{\"{alice}\":{{\"VolumePercent\":101}}}}",
                $"{{\"{alice}\":{{\"VolumePercent\":-1}}}}",
                $"{{\"{alice}\":{{\"MutedSounds\":null}}}}",
                $"{{\"{alice}\":{{\"MutedSounds\":[\"../bad\"]}}}}",
                $"{{\"{alice}\":false,\"{alice}\":true}}",
                $"{{\"{alice}\":{{\"Enabled\":false,\"enabled\":true}}}}",
                $"{{\"{alice}\":null}}"})
            {
                File.WriteAllText(path,bad); bool rejected=false;
                try { _=new SoundPreferenceStore(path); } catch(Exception ex) when(ex is InvalidDataException or JsonException) { rejected=true; }
                check(rejected&&File.ReadAllText(path)==bad,"Invalid or ambiguous preference file is rejected without erasing it");
            }
            string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);
            var blockedStore=new SoundPreferenceStore(blocked);bool failed=false;
            try { blockedStore.Set(alice,new SoundListeningPreference {VolumePercent=20,MutedSounds=["gong"]}); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {failed=true;}
            check(failed&&blockedStore.Get(alice).VolumePercent==100&&blockedStore.Get(alice).MutedSounds.Count==0,
                "Failed rich-preference save leaves active settings unchanged");
        }
        finally
        {
            string resolved=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("dc-listening-tests-"))throw new Exception("Unsafe cleanup");
            Directory.Delete(resolved,true);
        }
        var first=new SoundListener(1,76561198000000001,true,true,false,true,false,25);
        var second=first with {Slot=2,SteamId=76561198000000002,VolumePercent=100};
        var third=first with {Slot=3,SteamId=76561198000000003};
        var groups=SoundAudience.Groups([first,second,third,first,first with {Slot=4,VolumePercent=0},first with {Slot=5,Enabled=false}]);
        check(groups.Length==2 && groups.Single(g=>g.VolumePercent==25).Recipients.Keys.Order().SequenceEqual([1,3]) &&
            groups.Single(g=>g.VolumePercent==100).Recipients.Keys.SequenceEqual([2]),"Different listeners receive exactly one matching-volume event; zero and muted listeners are excluded");
        check(SoundAudience.Groups([first with {Alive=true},second],true).Single().Recipients.Keys.SequenceEqual([2]),
            "Personal volume cannot bypass the dead-to-dead server rule");
        uint next=40; var stopped=new List<(uint,int,ulong)>();
        var active=SoundBroadcast.Start("gong",100,3,groups,_=>++next,(g,s,id)=>stopped.Add((g,s,id)),true);
        check(active.SoundId=="gong"&&active.DeadOnly&&active.Recipients.Length==3,"Broadcast retains the sound ID, rule and listener identities across volume groups");
        check(active.Remove(1,out var removed)&&removed.Guid==41&&removed.SteamId==first.SteamId&&active.Recipients.Length==2,
            "Single listener removal targets their own volume group's GUID");
        check(active.Recipients.Single(x=>x.Key==2).Value.Guid==42&&active.Recipients.Single(x=>x.Key==3).Value.Guid==41,
            "Other volume groups and listeners are retained after one mute");
        bool emissionFailed=false; int calls=0;
        try { SoundBroadcast.Start("gong",100,3,groups,_=>++calls==2?throw new IOException("failed"):51u,(g,s,id)=>stopped.Add((g,s,id))); }
        catch(IOException) {emissionFailed=true;}
        check(emissionFailed&&stopped.Select(x=>x.Item2).Order().SequenceEqual([1,3])&&stopped.All(x=>x.Item1==51),
            "Failure in a later volume group stops only already-started deliveries");
        var quota=new SoundRoundLimit();
        quota.TryStart(first.SteamId,()=>{_=SoundBroadcast.Start("gong",0,3,groups,_=>1,(_,_,_)=>{});return true;});
        check(quota.Remaining(first.SteamId)==2,"Multiple volume events count as one public sound start");
    }
}
