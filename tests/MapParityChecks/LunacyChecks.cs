using LPLK_Maps;

static class LunacyChecks
{
    public static void Run(Action<bool,string> check, MapSettings settings)
    {
        var choices=Enumerable.Range(1,6).Select(i=>new MapEntry{Id="de_map"+i,Name="Map "+i}).ToArray();
        foreach(bool balanced in new[]{false,true})
        {
            var selected=MapPolicy.CandidateSelection(choices,new HashSet<string>{"de_map1"},3,balanced,new HashSet<string>{"de_map5","de_map6"});
            check(selected.Select(m=>m.Id).SequenceEqual(new[]{"de_map1","de_map5","de_map6"}),"NEW maps fill ballot before ordinary candidates while retaining nominations");
            var markers=new NewMapMarkers(new(){["de_map5"]=DateTimeOffset.UtcNow,["de_map6"]=DateTimeOffset.UtcNow});
            check(markers.OrderNominations(selected,DateTimeOffset.UtcNow).Select(m=>m.Id).SequenceEqual(new[]{"de_map5","de_map6","de_map1"}),"NEW ballot rows appear at the top");
        }
        var vote = new LunacyVote();
        check(!vote.Start("de_dust2", [1,2], 100), "No gravity question for other winners");
        check(!vote.Start(LunacyVote.MapId, [], 100), "Empty server cannot approve low gravity");
        foreach (var pair in new[] { (true,true), (true,false), (false,false) })
        {
            check(vote.Start(LunacyVote.MapId, [1,2], 100), "Lunacy winner starts follow-up");
            int serial = vote.Serial;
            check(!vote.Start(LunacyVote.MapId, [1,2], 200), "Active vote cannot be replaced");
            check(!vote.Cast(3,true,200,serial), "Late join cannot enter original electorate");
            check(vote.Cast(1,pair.Item1,200,serial) && !vote.Cast(1,!pair.Item1,201,serial), "One ballot per voter");
            check(vote.Cast(2,pair.Item2,200,serial), "Second ballot accepted");
            check(!vote.Finish(30099,[1,2]) && vote.Finish(30100,[1,2]), "Thirty-second boundary");
            check(vote.Accepted == (pair.Item1 && pair.Item2), "Strict majority; tie and no rejected");
            check(!vote.Cast(1,true,30100,serial), "Closed ballot rejects stale click");
            vote.Reset();
        }
        vote.Start(LunacyVote.MapId,[1,2,3],0);vote.Cast(1,true,1,vote.Serial);vote.Cast(2,true,1,vote.Serial);vote.Cast(3,false,1,vote.Serial);
        vote.Finish(30000,[2,3]);check(!vote.Accepted && vote.Yes==1 && vote.No==1,"Disconnected voter is excluded");
        vote.Reset();vote.Start(LunacyVote.MapId,[1,2],0);vote.Finish(30000,[1,2]);check(!vote.Accepted,"No ballots means normal gravity");
        vote.Reset();vote.Start(LunacyVote.MapId,[1,2],0);vote.Cast(1,true,1,vote.Serial);vote.Finish(30000,[1,2]);check(vote.Accepted,"Missing answer is abstention");
        int old = vote.Serial;vote.Reset();vote.Start(LunacyVote.MapId,[1],0);check(!vote.Cast(1,true,1,old)&&!vote.Cast(1,true,30000,vote.Serial),"Old and deadline clicks cannot count");

        foreach (bool yes in new[] {true,false})
        using (var world = new World(settings with {RtvAtRoundEnd=false}))
        {
            var map = MapPolicy.CaptureMeasurement(new MapEntry{Id=LunacyVote.MapId,Name="Lunacy"},16,16,0,0);
            map=MapPolicy.ApproveLimits(map,map.LimitProposal!.Signature);
            world.Set("maps",new List<MapEntry>{map});world.Set("installed",new HashSet<string>{map.Id});
            world.Limits["sv_gravity"]=800f;world.Limits["mp_match_restart_delay"]=10f;
            world.Set("lunacyRuntimeReady",true);
            var reason=Enum.Parse(typeof(Plugin).GetNestedType("Reason",System.Reflection.BindingFlags.NonPublic)!,"Rtv");
            check((bool)world.Call("BeginVote",reason,null,null)!,"Actual mapvote starts");
            world.Call("CastBallot",world.Menu.Player,map);world.Call("FinishVote");
            var followup=world.Get<LunacyVote>("lunacyVote");
            check(followup.Active&&world.Get<MapEntry?>("pending") is null,"Lunacy result postpones immediate RTV transition");
            check(world.Menu.Labels.Count==2&&world.Menu.Labels.Any(x=>x.Contains("Ja"))&&world.Menu.Labels.Any(x=>x.Contains("Nein")),"Actual menu offers numbered Ja/Nein");
            var button=world.Menu.FindExact(yes?"Ja":"Nein");world.Menu.Click(button).GetAwaiter().GetResult();
            check(world.Limits["sv_gravity"].Equals(800f),"Voting never changes current map gravity");
            world.Call("MatchEnded");check(world.Get<MapEntry?>("pending") is null,"Match end waits for follow-up");
            world.Call("TickLunacy",followup.Deadline);
            check(world.Get<bool>("lunacyApplyOnLoad")==yes&&world.Get<MapEntry>("pending").Id==map.Id,"Decision carries to selected transition");
            check(world.Limits["mp_match_restart_delay"].Equals(10f),"Temporary match delay restored");
            world.Menu.Click(button).GetAwaiter().GetResult();check(followup.Accepted==yes,"Old menu cannot alter completed result");
        }
        using (var world = new World(settings))
        {
            world.Limits["sv_gravity"]=800f;
            world.Call("ApplyLunacyGravity",new object?[]{null});check(world.Limits["sv_gravity"].Equals(800f),"Other map cannot acquire low gravity");
            world.Workshop=LunacyVote.MapId;world.Call("ApplyLunacyGravity",new object?[]{null});
            check(world.Limits["sv_gravity"].Equals(400f),"Lunacy applies verified gravity 400");
            world.Call("RestoreLunacyGravity");check(world.Limits["sv_gravity"].Equals(800f),"Map exit restores original gravity");
            world.Call("ApplyLunacyGravity",new object?[]{null});world.Limits["sv_gravity"]=600f;
            world.Call("RestoreLunacyGravity");check(world.Limits["sv_gravity"].Equals(600f),"Later admin gravity preserved");
            world.Limits["sv_gravity"]=800f;world.IgnoreSet="sv_gravity";world.Call("ApplyLunacyGravity",new object?[]{null});
            check(world.Get<float?>("lunacyOriginalGravity") is null&&world.Get<string?>("lunacyError") is not null,"Rejected native setter is not reported as success");
        }
    }
}
