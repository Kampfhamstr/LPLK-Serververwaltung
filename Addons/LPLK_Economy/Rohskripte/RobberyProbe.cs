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
    private void RobberyProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.ReplyLplk("LPLK_Economy robprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"robbery-probe.sqlite3"),report=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"robbery-probe.json");
        const string a="76561198000000001",b="76561198000000002";const long now=1_800_000_000;
        try
        {
            var legacyCommands=new[]{"bank","konto","abheben","withdraw","einzahlen","deposit","transfer","ueberweisen","bet","bt","wette","wetten","lotto","los","lotterie","ranking","rich","bbhelp","betbankhelp","spionlog","spylog","bankraublog","bankraub"};
            if(legacyCommands.Any(c=>!Core.Command.IsCommandRegistered(c)))throw new InvalidDataException("Legacy console alias missing");
            bool prior=File.Exists(path),armed=false;string seedBoot=boot;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;}
            using var probe=new Ledger(path,settings with{Profile="robbery-probe-89450e53",StartBank=0},phase=>{if(armed&&phase=="after-posting")throw new InvalidOperationException("synthetic-interruption");});var rules=new RobberySettings();
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.Adjust("a",1,a,a,5_000_000,"set");probe.Adjust("b",1,a,b,60_000_000,"set");
                probe.BuyRobberyUpgrade("vault",1,b,"vault",3,1_500_000,rules,now);
                probe.OrderRobbery("order",1,a,b,rules,now,100000);probe.MarkRobberyRound(rules,[]);
                armed=true;try{probe.ResumeRobbery(now,_=>0);throw new InvalidDataException("Probe failed to interrupt");}catch(InvalidOperationException e)when(e.Message=="synthetic-interruption"){}finally{armed=false;}
            }
            bool restarted=seedBoot!=boot;
            if(restarted)probe.ResumeRobbery(now,_=>throw new InvalidDataException("Saved robbery must not reroll"));
            var o=probe.GetRobbery("order")??throw new InvalidDataException("Probe order missing");
            if(o.Status!=(restarted?"done":"rolled")||probe.Security(b,rules,now).Vault!=3||probe.Balance(a)!=(restarted?10_750_000:4_900_000)||probe.Balance(b)!=(restarted?52_650_000:58_500_000))throw new InvalidDataException("Robbery probe inconsistent");
            long count=probe.TransactionCount;if(restarted)probe.ResumeRobbery(now);if(count!=probe.TransactionCount)throw new InvalidDataException("Duplicate robbery settlement");probe.Verify();
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),restarted,savedOutcomePreserved=true,upgradePreserved=true,settledOnce=restarted,legacyConsoleCommands=legacyCommands,playerDataChanged=false,productionChanged=false});
            ctx.ReplyLplk($"LPLK_Economy robprobe: savedOutcomePreserved=True; upgradePreserved=True; settledOnce={restarted}; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy robprobe fehlgeschlagen");ctx.ReplyLplk("LPLK_Economy robprobe: fehlgeschlagen");}
    }
}
