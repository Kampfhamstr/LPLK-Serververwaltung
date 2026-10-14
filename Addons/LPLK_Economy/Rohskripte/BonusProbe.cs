using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private void BonusProbe(ICommandContext ctx)
    {
        if(!ready||!Active||settings.Profile!="standard-test-11110004"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient)){ctx.Reply("LPLK_Economy bonusprobe: nur auf leerem, bereitem Standard-Testprofil.");return;}
        const string a="76561198000000001",b="76561198000000002";
        string path=Path.Combine(Core.PluginDataDirectory,"bonus-probe.sqlite3"),report=Path.Combine(Core.PluginDataDirectory,"bonus-probe.json");
        try
        {
            bool prior=File.Exists(path);string seedBoot=boot;bool alreadyCompleted=false;
            if(File.Exists(report)){using var j=JsonDocument.Parse(File.ReadAllText(report));seedBoot=j.RootElement.GetProperty("seedBoot").GetString()!;alreadyCompleted=j.RootElement.GetProperty("restarted").GetBoolean();}
            var rules=settings with{Profile="bonus-probe-11110004",StartBank=0,ResetMode="Never"};
            using var probe=new Ledger(path,rules);
            if(!prior)
            {
                probe.Open(a,"Prüfkonto A");probe.Open(b,"Prüfkonto B");probe.Adjust("seed-a",1,a,a,10_000_000,"set");probe.Adjust("seed-b",1,b,b,10_000_000,"set");
                var lotto=rules.Lotto with{PermanentTicketUpgradeDiscountChanceBasisPoints=10000,PermanentTicketUpgradeDiscountMinPercent=25,PermanentTicketUpgradeDiscountMaxPercent=25};
                probe.StartLotteryRound("discount",lotto);probe.SetLotteryPlan("plan",1,a,1,0,lotto);probe.BuyLottery("auto","discount",a,0,true,_=>0);probe.CloseLotteryRound("discount",false);probe.ResumeLottery();
                probe.StartLotteryRound("bonus",rules.Lotto);probe.BuyLottery("ticket","bonus",b,1);
                var ability=rules.LottoAbilityBonus with{ChancePerTicketBasisPoints=10000,Effects=new(){CashBonus=new(false,0,0),BankBonus=new(true,1,5000),FreeTicket=new(false,0,0),NextKillBankBonus=new(false,0,0)}};
                probe.RollLotteryBonuses("bonus",b,false,ability,_=>0);probe.PrepareCashCollection("collection",1,a,"synthetic",5000,7000,true);
            }
            bool restarted=seedBoot!=boot;
            if(!alreadyCompleted&&(probe.UpgradeDiscount(a)!=25||probe.PendingCount!=1||probe.Superpot!=0||probe.LotteryBonuses("bonus",b) is not [{Kind:"BankBonus",Status:"ready"}]))throw new InvalidDataException("Saved bonus seed lost");
            if(restarted)
            {
                probe.ResolveCash("collection",true,"synthetic confirmed write");probe.ResolveCash("collection",true,"synthetic confirmed write");
                probe.ApplyLotteryBonus("bonus",b,0);probe.ApplyLotteryBonus("bonus",b,0);
                probe.BuyRobberyUpgrade("upgrade",1,a,"vault",1,375000,rules.BankRobbery,1800000000);probe.BuyRobberyUpgrade("upgrade",1,a,"vault",1,375000,rules.BankRobbery,1800000000);
                if(probe.UpgradeDiscount(a)!=0||probe.Security(a,rules.BankRobbery,1800000000).Vault!=1||probe.Superpot!=7000||probe.Balance(a)!=9_625_000||probe.Balance(b)!=10_004_500||probe.PendingCount!=0)throw new InvalidDataException("Bonus settlement duplicated or lost");
            }
            probe.Verify();
            AtomicJson.Write(report,new{updatedAt=DateTimeOffset.UtcNow,boot,seedBoot,phase=restarted?"restart-verified":"seed-verified",restarted,discountPreserved=true,savedChoicePreserved=true,pendingCollectionPreserved=true,settledOnce=restarted,sqliteVersion=SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),playerDataChanged=false,productionChanged=false});
            ctx.Reply($"LPLK_Economy bonusprobe: discount=True; savedChoice=True; pendingCollection=True; settledOnce={restarted}; restarted={restarted}; Spielerkonten unverändert.");
        }
        catch(Exception e){Core.Logger.LogError(e,"LPLK_Economy bonusprobe fehlgeschlagen");ctx.Reply("LPLK_Economy bonusprobe: fehlgeschlagen");}
    }
}
