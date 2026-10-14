namespace LPLK_Statistics;
public sealed record RankingRow(int rank,string steamId,string name,double value,long kills,long shots);
public static class RankingExport
{
    public static IReadOnlyDictionary<string,RankingRow[]> Boards(StatsData data,StatisticsSettings s)
    {
        IEnumerable<(ulong Id,PlayerStats Player,double Value)> Values(string metric)=>data.Players
            .Where(p=>p.Key>=76561197960265728&&p.Key<=76561202255233023)
            .Select(p=>(Id:p.Key,Player:p.Value,Value:metric switch
            {
                "rating"=>s.PvpEnabled&&StatPolicy.Eligible(p.Value.Pvp,s.MinShots,s.MinKills)?StatPolicy.Rating(p.Value.Pvp,s):-1,
                "kills"=>s.PvpEnabled?(double)p.Value.Pvp.Kills:-1,
                "headshots"=>s.PvpEnabled?(double)p.Value.Pvp.Headshots:-1,
                "pickup"=>s.PvpEnabled?(double)(p.Value.Extra.EnemyWeapon+p.Value.Extra.TeamWeapon):-1,
                "playtime"=>s.PvpEnabled?(double)p.Value.Extra.PlaySeconds:-1,
                "botAccuracy"=>s.BotTrainingEnabled&&StatPolicy.Eligible(p.Value.Bots,s.MinBotShots,s.MinBotKills)?100*StatPolicy.Ratio(p.Value.Bots.Hits,p.Value.Bots.Shots):-1,
                _=>-1
            })).Where(p=>p.Value>=0&&(metric is "rating" or "botAccuracy"||p.Value>0));
        return new[]{"rating","kills","headshots","pickup","playtime","botAccuracy"}.ToDictionary(metric=>metric,metric=>
        {
            var values=Values(metric).OrderByDescending(p=>p.Value).ThenBy(p=>p.Id).Take(100).ToArray();
            var result=new List<RankingRow>();int rank=0;double previous=-1;
            for(int i=0;i<values.Length;i++){var p=values[i];if(i==0||p.Value!=previous)rank=i+1;previous=p.Value;var c=metric=="botAccuracy"?p.Player.Bots:p.Player.Pvp;result.Add(new(rank,p.Id.ToString(),StatPolicy.Clean(p.Player.Name),p.Value,c.Kills,c.Shots));}
            return result.ToArray();
        });
    }
    public static object Create(StatsData data,StatisticsSettings settings,DateTimeOffset now)=>new
    {
        schema=1,profile="standard",generatedAt=now,boards=Boards(data,settings),
        collection=new{settings.PvpEnabled,settings.BotTrainingEnabled,settings.MinShots,settings.MinKills,settings.MinBotShots,settings.MinBotKills,settings.CountPvpWarmup,settings.CountTeamKills}
    };
}
