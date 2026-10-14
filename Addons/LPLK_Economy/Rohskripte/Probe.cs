using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace LPLK_Economy;

public sealed partial class Plugin
{
    private readonly string boot=Guid.NewGuid().ToString("N");
    private void BettingProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-11110004"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_Economy betprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        string path=Path.Combine(Core.PluginDataDirectory,"betting-probe.sqlite3"),report=Path.Combine(Core.PluginDataDirectory,"betting-probe.json");
        const string a="76561198000000001",b="76561198000000002";
        try
        {
            bool prior=File.Exists(path);string seedBoot=boot;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;}
            using var probe=new Ledger(path,settings with{Profile="bet-probe-11110004",StartBank=0});
            var rules=new BettingSettings();
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.StartBetRound("resolved",rules);
                probe.PrepareBetStake("a","resolved",a,"synthetic",2,1000,500);probe.ResolveCash("a",true,"probe");
                probe.PrepareBetStake("b","resolved",b,"synthetic",3,1000,500);probe.ResolveCash("b",true,"probe");probe.CloseBetRound("resolved",2);
                probe.PayBetClaim("pay","resolved",a,"synthetic",1000,500,500);
                probe.StartBetRound("interrupted",rules);probe.PrepareBetStake("b2","interrupted",b,"synthetic",3,1000,700);probe.ResolveCash("b2",true,"probe");
            }
            bool restarted=seedBoot!=boot;
            if(restarted)
            {
                probe.RecoverBetRounds();
                if(probe.PlayerBetClaims(b)>0)probe.PayBetClaim("restart-refund","interrupted",b,"bank",0,0,700);
            }
            long transactions=probe.TransactionCount;probe.CloseBetRound("resolved",3);probe.ResolveCash("a",true,"duplicate");
            if(probe.TransactionCount!=transactions||probe.Balance(a)!=500||probe.Balance(b)!=(restarted?700:0)||probe.PendingCount!=1||!probe.Blocked(a)||probe.Intent("pay")?.Status!="pending"||probe.OpenBetRounds!=(restarted?0:1))throw new InvalidDataException("Wettprobe widersprüchlich.");
            probe.Verify();
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),restarted,payoutNotRepeated=true,pendingPayoutPreserved=true,interruptedRoundRefunded=restarted,playerDataChanged=false,productionChanged=false});
            ctx.Reply($"LPLK_Economy betprobe: poolVerified=True; pendingPayoutPreserved=True; payoutNotRepeated=True; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy betprobe fehlgeschlagen");ctx.Reply("LPLK_Economy betprobe: fehlgeschlagen");}
    }
    private void Probe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-11110004"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_Economy probe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        string path=Path.Combine(Core.PluginDataDirectory,"probe.sqlite3"),report=Path.Combine(Core.PluginDataDirectory,"probe.json");
        const string a="76561198000000001",b="76561198000000002";
        try
        {
            bool prior=File.Exists(path);string? previousBoot=null;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));previousBoot=j.RootElement.GetProperty("boot").GetString();}
            var config=settings with{Profile="probe-11110004",StartBank=0};
            using var probe=new Ledger(path,config);
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.Adjust("seed",1,a,a,12500,"set");
                probe.Transfer("transfer",1,a,b,2500);probe.PrepareCash("withdraw",1,a,"synthetic-session",10000,11000);
            }
            if(probe.Balance(a)!=9000||probe.Balance(b)!=2500||probe.PendingCount!=1||!probe.Blocked(a))throw new InvalidDataException("Prüfkonten unerwartet.");
            long transactions=probe.TransactionCount;
            var duplicate=probe.Transfer("transfer",1,a,b,2500);
            if(!duplicate.Duplicate||transactions!=probe.TransactionCount)throw new InvalidDataException("Wiederholung nicht erkannt.");
            bool rejected=false;try{probe.Transfer("pending-reject",1,a,b,100);}catch(BankRejected){rejected=true;}
            if(!rejected)throw new InvalidDataException("Offenes Konto nicht gesperrt.");probe.Verify();
            bool restarted=prior&&previousBoot is not null&&previousBoot!=boot;
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,phase=restarted?"restart-verified":"seed-verified",sqlite=true,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),durableReplay=duplicate.Duplicate,pendingRecovered=rejected,restarted,playerDataChanged=false,productionChanged=false});
            ctx.Reply($"LPLK_Economy probe: SQLite-Journal und Wiederholung geprüft; pendingRecovered={rejected}; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy probe fehlgeschlagen");ctx.Reply("LPLK_Economy probe: fehlgeschlagen");}
    }
}
