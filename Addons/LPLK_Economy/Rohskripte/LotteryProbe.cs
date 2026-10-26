using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Economy;

public sealed partial class Plugin
{
    private void LotteryProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.ReplyLplk("LPLK_Economy lottoprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"lottery-probe.sqlite3"),report=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"lottery-probe.json");
        const string a="76561198000000001",b="76561198000000002";
        try
        {
            bool prior=File.Exists(path);string seedBoot=boot;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;}
            using var probe=new Ledger(path,settings with{Profile="lottery-probe-89450e53",StartBank=0});var rules=new LotterySettings();
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.Adjust("seed-a",1,a,a,10000,"set");probe.Adjust("seed-b",1,b,b,10000,"set");
                probe.SetLotteryPlan("plan",1,a,3,0,rules);
                probe.StartLotteryRound("resolved",rules);probe.BuyLottery("a","resolved",a,2);probe.BuyLottery("b","resolved",b,1);probe.CloseLotteryRound("resolved",true,_=>0);
                probe.StartLotteryRound("pending",rules);probe.BuyLottery("c","pending",b,1);probe.PrepareCash("uncertain",1,b,"synthetic",1000,1100);probe.CloseLotteryRound("pending",true,_=>0);
                probe.StartLotteryRound("interrupted",rules);probe.BuyLottery("d","interrupted",a,2);
            }
            bool restarted=seedBoot!=boot;
            if(restarted)
            {
                probe.ResumeLottery(true);
                if(probe.Intent("uncertain")?.Status=="pending")
                {
                    if(probe.GetLotteryRound("pending")?.Status!="closing"||probe.GetLotteryRound("pending")?.Roll!=0)throw new InvalidDataException("Ziehungsentscheidung verloren.");
                    probe.ResolveCash("uncertain",false,"synthetic-probe-review");
                }
                probe.ResumeLottery();
            }
            long count=probe.TransactionCount;probe.CloseLotteryRound("resolved",false);probe.BuyLottery("a","resolved",a,2);
            if(count!=probe.TransactionCount||probe.Balance(a)!=(restarted?10200:9200)||probe.Balance(b)!=(restarted?9400:8900)||probe.Superpot!=(restarted?200:150)||probe.LotteryPlan(a).Count!=3||probe.OpenLotteryRounds!=(restarted?0:2)||probe.PendingCount!=(restarted?0:1))throw new InvalidDataException("Lottoprobe widersprüchlich.");
            probe.Verify();
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),restarted,drawNotRepeated=true,superpotPreserved=true,subscriptionPreserved=true,interruptedRoundRefunded=restarted,savedDrawResumed=restarted,playerDataChanged=false,productionChanged=false});
            ctx.ReplyLplk($"LPLK_Economy lottoprobe: drawNotRepeated=True; superpotPreserved=True; savedDrawResumed={restarted}; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy lottoprobe fehlgeschlagen");ctx.ReplyLplk("LPLK_Economy lottoprobe: fehlgeschlagen");}
    }
}
