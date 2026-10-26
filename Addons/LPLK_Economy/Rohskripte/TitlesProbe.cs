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
    private void TitlesProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.ReplyLplk("LPLK_Economy titleprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        const string a="76561198000000001",b="76561198000000002";
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"titles-probe.sqlite3"),report=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"titles-probe.json");
        try
        {
            string[] aliases=["titel","title","titles","sw_titel","sw_title","sw_titles"];
            if(aliases.Any(c=>!Core.Command.IsCommandRegistered(c)))throw new InvalidDataException("Title alias missing");
            bool prior=File.Exists(path);string seedBoot=boot;long expectedA=0,expectedB=0,expectedTransactions=0;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;expectedA=j.RootElement.GetProperty("accountA").GetInt64();expectedB=j.RootElement.GetProperty("accountB").GetInt64();expectedTransactions=j.RootElement.GetProperty("transactions").GetInt64();}
            using var probe=new Ledger(path,settings with{Profile="titles-probe-89450e53",StartBank=0});
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.Adjust("seed-a",1,a,a,1_000_000,"set");probe.Adjust("seed-b",1,b,b,2_000_000,"set");probe.Transfer("transfer",1,a,b,1000);probe.SelectTitle("title",1,a,"wealth_1000000");
                var tax=probe.ApplyTitleRound("tax",1,[a,b],true);if(tax.Length!=1||tax[0].Amount!=400||probe.Superpot!=40)throw new InvalidDataException("Native tax mismatch");
                if(probe.ApplyTitleRound("interest",1,[a,b],false).Length!=2)throw new InvalidDataException("Native interest missing");
                expectedA=probe.Balance(a);expectedB=probe.Balance(b);expectedTransactions=probe.TransactionCount;
            }
            if(probe.ActiveTitle(a)!="Millionär"||probe.TitlesProgress(a).Values.GetValueOrDefault("TransfersSent")!=1||!probe.PioneerOwners().Values.Contains(a)||probe.RulerSlots(b)<1)throw new InvalidDataException("Title progress not preserved");
            if(probe.ApplyTitleRound("tax",1,[a,b],true).Length!=0||probe.ApplyTitleRound("interest",1,[a,b],false).Length!=0||probe.Balance(a)!=expectedA||probe.Balance(b)!=expectedB||probe.TransactionCount!=expectedTransactions)throw new InvalidDataException("Round paid or collected twice");probe.Verify();
            bool restarted=seedBoot!=boot;
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",restarted,accountA=expectedA,accountB=expectedB,transactions=expectedTransactions,progressPreserved=true,activeTitlePreserved=true,pioneerPreserved=true,rulerSlotsPreserved=true,interestAndTaxOnce=true,legacyCommands=aliases,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),playerDataChanged=false,productionChanged=false});
            ctx.ReplyLplk($"LPLK_Economy titleprobe: progress=True; activeTitle=True; pioneer=True; rulerSlots=True; interestAndTaxOnce=True; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy titleprobe fehlgeschlagen");ctx.ReplyLplk("LPLK_Economy titleprobe: fehlgeschlagen");}
    }
}
