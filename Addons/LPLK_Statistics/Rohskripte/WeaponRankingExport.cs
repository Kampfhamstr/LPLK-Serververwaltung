using LPLK.Common;
namespace LPLK_Statistics;

public sealed record WeaponRankingRow(int rank,string steamId,string name,long value);
public sealed record WeaponRankingPlayer(ulong Id,string Name,KeyValuePair<string,long>[] Weapons);
public sealed record WeaponRankingSnapshot(WeaponRankingPlayer[] Players,StatisticsSettings Settings,DateTimeOffset GeneratedAt);
public sealed class WeaponRankingCapture
{
    private readonly ulong[] ids;private readonly StatisticsSettings settings;private readonly List<WeaponRankingPlayer> players=[];private int index;
    public WeaponRankingCapture(StatsData data,StatisticsSettings settings){this.settings=settings;ids=settings.Enabled&&settings.WeaponTracking?data.Players.Keys.ToArray():[];}
    public bool Step(StatsData data,int limit=32)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int copied=0;index<ids.Length&&copied<limit;copied++)
        {
            ulong id=ids[index++];
            if(id>=76561197960265728&&id<=76561202255233023&&data.Players.TryGetValue(id,out var p))
                players.Add(new(id,p.Name,p.Weapons.Where(w=>w.Value>0).ToArray()));
            if(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds>=0.5)break;
        }
        return index==ids.Length;
    }
    public WeaponRankingSnapshot Finish(DateTimeOffset now)=>index==ids.Length?new(players.ToArray(),settings,now):throw new InvalidOperationException("Snapshot incomplete");
}
public static class WeaponRankingExport
{
    public static readonly string[] Weapons = new[]{"knife","taser"}
        .Concat(WeaponCatalog.All.Select(w=>w.Id[7..])).Concat(StatPolicy.Grenades).Append("c4").ToArray();

    public static WeaponRankingSnapshot Capture(StatsData data,StatisticsSettings settings,DateTimeOffset now)=>new(
        settings.Enabled&&settings.WeaponTracking?data.Players.Where(p=>p.Key>=76561197960265728&&p.Key<=76561202255233023)
            .Select(p=>new WeaponRankingPlayer(p.Key,p.Value.Name,p.Value.Weapons.Where(w=>w.Value>0).ToArray())).ToArray():[],settings,now);

    public static IReadOnlyDictionary<string,WeaponRankingRow[]> Boards(StatsData data,StatisticsSettings settings)=>Boards(Capture(data,settings,DateTimeOffset.UtcNow));
    public static IReadOnlyDictionary<string,WeaponRankingRow[]> Boards(WeaponRankingSnapshot snapshot)
    {
        var groups=Weapons.ToDictionary(w=>w,_=>new List<(ulong Id,string Name,long Kills)>());
        foreach(var player in snapshot.Players)foreach(var weapon in player.Weapons)
            if(groups.TryGetValue(weapon.Key,out var group))group.Add((player.Id,player.Name,weapon.Value));
        return Weapons.ToDictionary(weapon=>weapon,weapon=>
        {
            var sorted=groups[weapon].OrderByDescending(p=>p.Kills).ThenBy(p=>p.Id).Take(100).ToArray();
            var rows=new WeaponRankingRow[sorted.Length];int rank=0;long previous=-1;
            for(int i=0;i<sorted.Length;i++)
            {
                var p=sorted[i];if(i==0||p.Kills!=previous)rank=i+1;previous=p.Kills;
                rows[i]=new(rank,p.Id.ToString(),StatPolicy.Clean(p.Name),p.Kills);
            }
            return rows;
        });
    }

    public static object Create(StatsData data,StatisticsSettings settings,DateTimeOffset now)=>Create(Capture(data,settings,now));
    public static object Create(WeaponRankingSnapshot snapshot)=>new
    {
        schema=1,profile="standard",generatedAt=snapshot.GeneratedAt,boards=Boards(snapshot),
        collection=new{snapshot.Settings.Enabled,snapshot.Settings.WeaponTracking,snapshot.Settings.CountWeaponWarmup,snapshot.Settings.CountTeamKills}
    };
}
