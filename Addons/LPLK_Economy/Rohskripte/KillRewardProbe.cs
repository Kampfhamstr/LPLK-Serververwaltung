using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private void KillRewardProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_Economy killprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        const string a="76561198000000001",b="76561198000000002";
        string path=Path.Combine(Core.PluginDataDirectory,"killreward-probe.sqlite3"),report=Path.Combine(Core.PluginDataDirectory,"killreward-probe.json");
        try
        {
            bool prior=File.Exists(path),completed=false;string seedBoot=boot;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;completed=j.RootElement.GetProperty("restarted").GetBoolean();}
            var rules=settings with{Profile="killreward-probe-89450e53",StartBank=0,ResetMode="Never"};using var probe=new Ledger(path,rules);
            if(!prior)
            {
                foreach(string account in new[]{a,b}){probe.Open(account,"Prüfkonto "+account[^1]);probe.Adjust("seed:"+account,1,a,account,100000,"set");}
                probe.AwardKillReward("open",a,b,"B",16000,false);
                probe.AwardKillReward("pending",a,b,"B",15000,false);probe.PayKillReward("pay","pending",a,"synthetic",15000,1000,500);
            }
            bool restarted=seedBoot!=boot;
            if(!completed&&(probe.KillRewardClaims().Sum(x=>x.Remaining)!=1600||probe.PendingCount!=1||probe.Balance(a)!=100500||probe.Balance(b)!=100000))throw new InvalidDataException("Saved kill reward seed lost");
            if(restarted)
            {
                probe.ResolveCash("pay",true,"synthetic confirmed write");probe.ResolveCash("pay",true,"synthetic confirmed write");
                probe.PayKillReward("open-pay","open",a,"bank",0,0,1600);probe.PayKillReward("open-pay","open",a,"bank",0,0,1600);
                if(probe.AwardKillReward("open",a,b,"B",16000,false)!=0||probe.Balance(a)!=102100||probe.Balance(b)!=100000||probe.PendingCount!=0||probe.KillRewardClaims().Length!=0)throw new InvalidDataException("Kill reward settlement duplicated or lost");
            }
            probe.Verify();
            string[] aliases=["bet","bt","wette","wetten","bets","pool","wettenstatus","bank","konto","deposit","einzahlen","withdraw","abheben","transfer","ueberweisen","überweisen","topbank","banktop","rich","ranking","bounty","kopfgeld","lotto","lotterie","los","titel","title","titles","spionlog","spylog","bankraublog","bankmsg","bbmsg","betbankmsg","help","bbhelp","betbankhelp","bbadmin","bankadmin","betbankadmin","bankset","bankadd","bankremove","bankdel","bankresetplayer","bankresetkonto"];
            string[] commands=aliases.SelectMany(alias=>new[]{alias,"sw_"+alias}).ToArray();int registered=commands.Count(Core.Command.IsCommandRegistered);
            if(registered!=commands.Length)throw new InvalidDataException("Legacy Economy command missing");
            int legacyAdminChecks=LegacyAdminProbe();int webBankChecks=WebBankProbe();
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",restarted,claimPreserved=true,pendingCashPreserved=true,victimBalancePreserved=true,settledOnce=restarted,legacyAliases=aliases.Length,registeredCommands=registered,legacyAdminClampVerified=true,legacyAdminChecks,webBankChecks,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),playerDataChanged=false,productionChanged=false});
            ctx.Reply($"LPLK_Economy killprobe: claim=True; pendingCash=True; victimUnchanged=True; settledOnce={restarted}; restarted={restarted}; commands={registered}/{commands.Length}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy killprobe fehlgeschlagen");ctx.Reply("LPLK_Economy killprobe: fehlgeschlagen");}
    }
}
