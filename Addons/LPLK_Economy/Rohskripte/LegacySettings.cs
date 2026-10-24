using System.Text.Json;
using System.Text.Json.Nodes;

namespace LPLK_Economy;

public static class LegacySettings
{
    // Input is the explicit allowlisted reference export; unknown rules block a rollout.
    public static EconomySettings Convert(string reference,string profile,bool standard)
    {
        var document=JsonNode.Parse(reference)!.AsObject();var source=document["settings"]!.AsObject();
        var target=JsonSerializer.SerializeToNode(new EconomySettings())!.AsObject();
        var unsupported=new List<string>();
        void Set(JsonObject dest,string key,JsonNode? value,string path)
        {
            if(!dest.ContainsKey(key)){unsupported.Add(path);return;}
            if(value is JsonValue number&&number.TryGetValue<int>(out int enumValue)&&key is "RewardMode" or "ResetMode" or "OpenMode"){
                string[] names=key=="RewardMode"?["Cash","Bank","CashThenBankOverflow"]:key=="ResetMode"?["Never","Daily","Weekly","Monthly"]:["Time","PlayersPerTeam"];
                if(enumValue<0||enumValue>=names.Length)throw new InvalidDataException("Unbekannter Altmodus: "+path);
                value=JsonValue.Create(names[enumValue]);
            }
            if(value is JsonObject obj&&dest[key] is JsonObject nested){foreach(var p in obj)Set(nested,p.Key,p.Value,path+"."+p.Key);}
            else dest[key]=value?.DeepClone();
        }
        var rename=new Dictionary<string,string>{["Bank.Enabled"]="BankEnabled",["Bank.MaxSafeBankBalanceWhenUnlimited"]="UnlimitedLimit",["Bank.BlockBankSystemForMapIfRoundStartCashAboveLimit"]="BlockMapAboveCashLimit",["Bank.MapCashBlockMaxPlausibleCash"]="MaxPlausibleCash",["Bank.WithdrawBuyTimeSecondsAfterRoundStart"]="BuyTimeSeconds",["Match.DetectPistolRoundsFromMaxRounds"]="DetectPistolRounds",["Transfer.Enabled"]="TransfersEnabled",["Transfer.OnlyAlivePlayers"]="OnlyAliveTransfers",["Transfer.MaxStoredTransferTargetsInMenu"]="MaxStoredTransferTargets",["AntiBotTakeoverExploit.BlockCashActionsAfterDeathThisRound"]="BlockCashAfterDeath",["Admin.Permission"]="AdminPermission",["Menu.AutoCloseSeconds"]="MenuAutoCloseSeconds",["Menu.DepositAmountButtons"]="DepositButtons",["Menu.WithdrawAmountButtons"]="WithdrawButtons",["Menu.TransferAmountButtons"]="TransferButtons",["Menu.AdminBankAddButtons"]="AdminAddButtons",["Menu.AdminBankRemoveButtons"]="AdminRemoveButtons",["Menu.AdminBankSetButtons"]="AdminSetButtons"};
        string[] nestedSections=["KillReward","Bounty","BountyMarker","TitleSystem","InterestSystem","LuxuryTax","BankRobbery","Lotto","LottoAbilityBonus","Betting"];
        var fixedRules=new Dictionary<string,bool>{["Betting.RequireAlivePlayersForBetting"]=true,["Betting.AllowBetsWhenRoundStateUnknown"]=false,["Betting.AllowCancelBet"]=false,["Betting.AllowCancelWhileBettingOpen"]=false,["Betting.ReplaceExistingBet"]=false,["Betting.LockBetAfterPlaced"]=true,["Betting.ClearBetsOnRoundPrestart"]=true};
        foreach(var section in source)
        {
            if(section.Key=="Enabled"){Set(target,"Enabled",section.Value,"Enabled");continue;}
            if(section.Key=="Commands")continue; // Fixed compatible aliases are checked separately before deployment.
            if(section.Value is not JsonObject fields)throw new InvalidDataException("Ungültiger Regelabschnitt: "+section.Key);
            foreach(var p in fields)
            {
                string path=section.Key+"."+p.Key;
                if(fixedRules.TryGetValue(path,out bool fixedValue)){if(p.Value!.GetValue<bool>()!=fixedValue)unsupported.Add(path);continue;}
                if(path=="Admin.AdminSteamIdCount"){if(p.Value!.GetValue<int>()!=0)unsupported.Add(path);continue;}
                if(path=="Menu.MaxVisibleItems")continue; // Shared compact menu implements the requested layout.
                if(path=="AntiBotTakeoverExploit.BlockCashActionsWhileControllingBot"){
                    if(p.Value!.GetValue<bool>()!=true)unsupported.Add(path);continue;
                }
                if(path=="Bank.OverflowToBank"){
                    if(p.Value!.GetValue<bool>()!=true)unsupported.Add(path);continue;
                }
                if(section.Key=="Match"&&p.Key is "ProcessEconomyOnRoundEndWithoutWinner" or "MinActivePlayersForEconomyRoundEnd"){
                    foreach(string name in new[]{"Lotto","BankRobbery"})Set(target[name]!.AsObject(),p.Key,p.Value,path);continue;
                }
                if(section.Key=="Messages"){Set(target["Betting"]!.AsObject(),p.Key,p.Value,path);continue;}
                if(path=="Menu.BetAmountButtons"){Set(target["Betting"]!.AsObject(),"AmountButtons",p.Value,path);continue;}
                if(nestedSections.Contains(section.Key)){
                    var value=p.Value?.DeepClone();
                    if(path=="LottoAbilityBonus.Effects"&&value is JsonObject effects){
                        foreach(string obsolete in new[]{"ArmorBonus","HealBonus"})effects.Remove(obsolete); // Deliberately unused in the verified old implementation.
                        foreach(var effect in effects.Select(v=>v.Value).OfType<JsonObject>()){effect.Remove("MaxHp");effect.Remove("MaxArmor");}
                    }
                    Set(target[section.Key]!.AsObject(),p.Key,value,path);
                }
                else Set(target,rename.GetValueOrDefault(path,p.Key),p.Value,path);
            }
        }
        if(unsupported.Count>0)throw new InvalidDataException("Regeln ohne geprüfte Zuordnung: "+string.Join(", ",unsupported));
        var overrides=document["overrides"]!.AsObject();
        foreach(var p in overrides.Where(p=>p.Value is not null)){
            switch(p.Key){
                case "MaxBankOverride":target["MaxBank"]=p.Value!.DeepClone();break;
                case "ResetModeOverride":target["ResetMode"]=p.Value!.DeepClone();break;
                case "KillRewardEnabledOverride":target["KillReward"]!["Enabled"]=p.Value!.DeepClone();break;
                case "BountyEnabledOverride":target["Bounty"]!["Enabled"]=p.Value!.DeepClone();break;
                case "BountyMarkerEnabledOverride":target["BountyMarker"]!["Enabled"]=p.Value!.DeepClone();break;
                default:throw new InvalidDataException("Unbekannter wirksamer Override.");
            }
        }
        target["Profile"]=profile;if(standard){target["ResetMode"]="Monthly";target["AllowChangeMaxBank"]=false;target["AllowChangeResetMode"]=false;}
        var result=target.Deserialize<EconomySettings>()!;EconomySettings.Validate(result);return result;
    }
}
