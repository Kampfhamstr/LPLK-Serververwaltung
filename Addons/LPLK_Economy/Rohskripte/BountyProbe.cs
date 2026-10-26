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
    private void BountyProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.ReplyLplk("LPLK_Economy bountyprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        const string a="76561198000000001",b="76561198000000002",c="76561198000000003";
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"bounty-probe.sqlite3"),report=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"bounty-probe.json");
        try
        {
            bool prior=File.Exists(path),completed=false;string seedBoot=boot;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;completed=j.RootElement.GetProperty("restarted").GetBoolean();}
            var rules=settings with{Profile="bounty-probe-89450e53",StartBank=0,ResetMode="Never"};using var probe=new Ledger(path,rules);
            if(!prior)
            {
                foreach(string account in new[]{a,b,c}){probe.Open(account,"Prüfkonto "+account[^1]);probe.Adjust("seed:"+account,1,a,account,100000,"set");}
                probe.PlaceBounty("bank",1,a,b,5000,"Bank",rules.Bounty);probe.PlaceBounty("active",1,a,c,4000,"Bank",rules.Bounty);
                probe.PlaceBounty("cash",1,a,b,2000,"Cash",rules.Bounty,false,"synthetic",5000);
                probe.PlaceBounty("reward",1,c,a,3000,"Bank",rules.Bounty);probe.ClaimBounty("kill",c,a,false);probe.RefundBounties(b,"probe disconnect");
            }
            bool restarted=seedBoot!=boot;
            if(!completed&&(probe.BountyTotal(c)!=4000||probe.PendingCount!=1||probe.BountyClaims().Sum(x=>x.Remaining)!=8000||probe.GetBounty("cash") is not {Status:"pending",RefundReason:"probe disconnect"}))throw new InvalidDataException("Saved bounty seed lost");
            if(restarted)
            {
                probe.ResolveCash("cash",true,"synthetic confirmed write");probe.ResolveCash("cash",true,"synthetic confirmed write");
                probe.PayBountyClaim("refund-bank","refund:bank",a,"bank",0,0,5000);probe.PayBountyClaim("refund-bank","refund:bank",a,"bank",0,0,5000);
                probe.PayBountyClaim("refund-cash","refund:cash",a,"synthetic",10000,2000,0);probe.ResolveCash("refund-cash",true,"synthetic confirmed write");probe.ResolveCash("refund-cash",true,"synthetic confirmed write");
                probe.PayBountyClaim("reward-pay","kill:kill",c,"synthetic",15000,1000,2000);probe.ResolveCash("reward-pay",true,"synthetic confirmed write");probe.ResolveCash("reward-pay",true,"synthetic confirmed write");
                if(probe.Balance(a)!=96000||probe.Balance(c)!=99000||probe.PendingCount!=0||probe.BountyClaims().Length!=0||probe.BountyTotal(c)!=4000||probe.TitlesProgress(c).Values.GetValueOrDefault("BountiesClaimed")!=1||!probe.OwnedTitles(c).Any(t=>t.Id=="ruler_bounty_claim"))throw new InvalidDataException("Bounty settlement duplicated or lost");
            }
            probe.Verify();AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",restarted,activeBountyPreserved=true,pendingRefundPreserved=true,rewardClaimPreserved=true,titlesVerified=restarted,settledOnce=restarted,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),playerDataChanged=false,productionChanged=false});
            string[] commands=["kopfgeld","sw_kopfgeld","bounty","sw_bounty"];
            ctx.ReplyLplk($"LPLK_Economy bountyprobe: active=True; pendingRefund=True; rewardClaim=True; settledOnce={restarted}; restarted={restarted}; commands={commands.Count(Core.Command.IsCommandRegistered)}/4; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy bountyprobe fehlgeschlagen");ctx.ReplyLplk("LPLK_Economy bountyprobe: fehlgeschlagen");}
    }
}
