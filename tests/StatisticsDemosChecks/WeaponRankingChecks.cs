using System.Text.Json;
using LPLK_Statistics;
public static class WeaponRankingChecks
{
    public static void Run(Action<bool,string> check,string fixturePath)
    {
        var data=new StatsData();var settings=new StatisticsSettings();
        long[] knife=[30,30,12,8,4,0];long[] taser=[2,10,2,1,0,0];
        for(int i=0;i<6;i++)data.Players[76561198000000000UL+(ulong)i]=new PlayerStats{Name=i==0?"<script>Spieler</script>":"Spieler "+i,Weapons=new(){["knife"]=knife[i],["taser"]=taser[i],["ak47"]=i+1}};
        data.Players[12]=new PlayerStats{Name="Invalid ID",Weapons=new(){["knife"]=999}};
        var boards=WeaponRankingExport.Boards(data,settings);
        check(boards.Count==44,"Complete weapon catalog, including knife, Zeus and grenades");
        check(boards["knife"].Select(r=>r.rank).SequenceEqual([1,1,3,4,5]),"Competition ranks preserve ties and gaps");
        check(boards["knife"][0].steamId=="76561198000000000","Ties deterministically ordered by Steam ID");
        check(boards["knife"].Length==5&&boards["knife"][0].value==30,"Invalid identities and zero kills excluded");
        check(boards["taser"].Select(r=>r.rank).SequenceEqual([1,2,2,4]),"Zeus ranks independent from knife ranks");
        check(boards["hegrenade"].Length==0,"Unused weapons exported as empty lists");
        check(WeaponRankingExport.Boards(data,settings with{PvpEnabled=false})["knife"].Length==5,"Weapon tracking independent of PvP toggle");
        check(WeaponRankingExport.Boards(data,settings with{WeaponTracking=false}).Values.All(b=>b.Length==0),"Disabled weapon tracking exports no leaderboard");
        check(WeaponRankingExport.Boards(data,settings with{Enabled=false}).Values.All(b=>b.Length==0),"Disabled addon exports no leaderboard");
        File.WriteAllText(fixturePath,JsonSerializer.Serialize(WeaponRankingExport.Create(data,settings,DateTimeOffset.UtcNow)));
        var large=new StatsData();for(int i=0;i<120;i++)large.Players[76561198000000100UL+(ulong)i]=new PlayerStats{Weapons=new(){["knife"]=i<3?200:120-i}};
        var top=WeaponRankingExport.Boards(large,settings)["knife"];
        check(top.Length==100&&top[0].rank==1&&top[2].rank==1&&top[3].rank==4,"Top 100 preserves ties and bounds");
        check(data.Players[76561198000000000UL].Weapons["knife"]==30,"Export never mutates source counters");
        var captured=WeaponRankingExport.Capture(data,settings,DateTimeOffset.UtcNow);
        data.Players[76561198000000000UL].Weapons["knife"]=9999;
        check(Task.Run(()=>WeaponRankingExport.Boards(captured)["knife"][0].value).GetAwaiter().GetResult()==30,"Detached background snapshot cannot observe later game-thread mutations");
        var load=new StatsData();for(int i=0;i<10000;i++)load.Players[76561198000100000UL+(ulong)i]=new PlayerStats{Name="Benchmark",Weapons=WeaponRankingExport.Weapons.ToDictionary(w=>w,w=>(long)(i%100+1))};
        var timer=System.Diagnostics.Stopwatch.StartNew();var capture=new WeaponRankingCapture(load,settings);double maxBatchMs=0;int batches=0;bool done;do{var batch=System.Diagnostics.Stopwatch.StartNew();done=capture.Step(load);maxBatchMs=Math.Max(maxBatchMs,batch.Elapsed.TotalMilliseconds);batches++;}while(!done);var snapshot=capture.Finish(DateTimeOffset.UtcNow);double captureMs=timer.Elapsed.TotalMilliseconds;check(snapshot.Players.Length==10000&&batches>=313,"Incremental capture includes all players without one full-scan tick");
        timer.Restart();string serialized=Task.Run(()=>JsonSerializer.Serialize(WeaponRankingExport.Create(snapshot))).GetAwaiter().GetResult();double workerMs=timer.Elapsed.TotalMilliseconds;
        check(System.Text.Encoding.UTF8.GetByteCount(serialized)<4194304,"Maximum-length leaderboard stays within web parser limit");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"StatisticsDemosChecks","weapon-ranking-benchmark.json"),JsonSerializer.Serialize(new{players=10000,weapons=44,captureMs,maxBatchMs,batches,workerMs,bytes=System.Text.Encoding.UTF8.GetByteCount(serialized),synthetic=true,livePerformanceClaim=false}));
    }
}
