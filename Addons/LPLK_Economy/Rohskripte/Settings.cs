using System.Globalization;
namespace LPLK_Economy;

public sealed record EconomySettings
{
    public WebBankSettings WebBank {get;init;}=new();
    public int MenuAutoCloseSeconds {get;init;}=25;
    public long[] AdminAddButtons {get;init;}=[1000,5000,10000];
    public long[] AdminRemoveButtons {get;init;}=[1000,5000,10000];
    public long[] AdminSetButtons {get;init;}=[0,1000,5000,10000,25000,50000];
    public KillRewardSettings KillReward {get;init;}=new();
    public bool AllowToggleKillReward {get;init;}=true;
    public BountySettings Bounty {get;init;}=new();
    public BountyMarkerSettings BountyMarker {get;init;}=new();
    public long[] BountyAmountButtons {get;init;}=[1000,2500,5000,7500,10000];
    public AbilitySettings LottoAbilityBonus { get; init; } = new();
    public TitleSettings TitleSystem { get; init; } = new();
    public InterestSettings InterestSystem { get; init; } = new();
    public LuxuryTaxSettings LuxuryTax { get; init; } = new();
    public RobberySettings BankRobbery { get; init; } = new();
    public LotterySettings Lotto { get; init; } = new();
    public BettingSettings Betting { get; init; } = new();
    public bool BankEnabled { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public string Profile { get; init; } = "standard-test-11110004";
    public long StartBank { get; init; }
    public long MaxBank { get; init; } = -1;
    public long UnlimitedLimit { get; init; } = 9_000_000_000_000_000;
    public string ResetMode { get; init; } = "Monthly";
    public bool UseUtcForResetKey { get; init; }
    public int MaxPlayerCash { get; init; } = 16000;
    public bool BlockMapAboveCashLimit { get; init; } = true;
    public bool MapCashBlockAllPlayers { get; init; } = true;
    public int MapCashBlockLimit { get; init; } = 16000;
    public int MaxPlausibleCash { get; init; } = 1000000;
    public int MinDeposit { get; init; } = 100;
    public int MinWithdraw { get; init; } = 100;
    public bool DepositOnlyDuringFreezeTime { get; init; }
    public bool WithdrawOnlyDuringFreezeTime { get; init; } = true;
    public bool AllowWithdrawDuringBuyTime { get; init; } = true;
    public int BuyTimeSeconds { get; init; } = 25;
    public bool BlockDepositDuringWarmup { get; init; } = true;
    public bool BlockWithdrawDuringWarmup { get; init; }
    public string[] BlockDepositOnMapPrefixes { get; init; } = ["ar_"];
    public bool BlockWithdrawDuringPistolRounds { get; init; } = true;
    public bool AllowWithdrawDuringPistolRound { get; init; }
    public int MaxWithdrawPistolRound { get; init; } = 800;
    public bool AllowWithdrawDuringPistolRoundBuyTime { get; init; }
    public int MaxWithdrawPistolRoundBuyTime { get; init; }
    public int[] PistolRoundNumbers { get; init; } = [1,11];
    public int MaxRounds { get; init; } = 20;
    public bool DetectPistolRounds { get; init; } = true;
    public bool BlockCashAfterDeath { get; init; } = true;
    public bool TransfersEnabled { get; init; } = true;
    public long MinTransfer { get; init; } = 100;
    public long MaxTransfer { get; init; } = -1;
    public bool OnlySameTeam { get; init; } = true;
    public bool AllowForeignPlayers { get; init; } = true;
    public bool AllowStoredPlayerTransfers { get; init; } = true;
    public bool AllowUnknownSteamIdTransfers { get; init; } = true;
    public int MaxStoredTransferTargets { get; init; } = 40;
    public bool OnlyAliveTransfers { get; init; }
    public bool BlockTransferDuringWarmup { get; init; }
    public bool TransferOnlyDuringFreezeTime { get; init; }
    public string AdminPermission { get; init; } = "betbank.admin";
    public bool AllowChangeMaxBank { get; init; } = true;
    public bool AllowChangeResetMode { get; init; } = true;
    public bool AllowManualBankReset { get; init; } = true;
    public bool AllowEditPlayerBank { get; init; } = true;
    public long MaxManualBankSet { get; init; } = 50000;
    public long[] DepositButtons { get; init; } = [500,1000,2500,5000];
    public long[] WithdrawButtons { get; init; } = [500,800,1000,2500,5000];
    public long[] TransferButtons { get; init; } = [500,1000,2500,5000];
    public long Limit => MaxBank < 0 ? UnlimitedLimit : MaxBank;
    public static void Validate(EconomySettings s)
    {
        if(s.WebBank is null||s.MenuAutoCloseSeconds is <0 or >600)throw new InvalidDataException("Ungültige Web-/Menüeinstellungen.");
        foreach(var values in new[]{s.AdminAddButtons,s.AdminRemoveButtons,s.AdminSetButtons})if(values is null||values.Length is <1 or >20||values.Any(v=>v<0||v>s.UnlimitedLimit))throw new InvalidDataException("Ungültige Adminbeträge.");
        KillRewardPolicy.Validate(s.KillReward);
        BountyPolicy.Validate(s.Bounty,s.BountyMarker,s.BountyAmountButtons);
        BonusPolicy.Validate(s.LottoAbilityBonus);
        if(s.BankRobbery is null)throw new InvalidDataException("Bankraub-Einstellungen fehlen."); RobberyPolicy.Validate(s.BankRobbery);TitlePolicy.Validate(s);
        if(s.Lotto is null)throw new InvalidDataException("Lotto-Einstellungen fehlen."); LotterySettings.Validate(s.Lotto);
        if(s.Betting is null)throw new InvalidDataException("Wetteinstellungen fehlen."); BettingSettings.Validate(s.Betting);
        if(s.Profile.Length is <1 or >64 || !s.Profile.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_') || s.MaxBank < -1 || s.UnlimitedLimit is <1 or >9_000_000_000_000_000 || s.Limit > 9_000_000_000_000_000 || s.StartBank <0 || s.StartBank>s.Limit || s.ResetMode is not ("Never" or "Daily" or "Weekly" or "Monthly") || s.MaxPlayerCash is <1 or >16000 || s.MapCashBlockLimit<1 || s.MaxPlausibleCash<s.MapCashBlockLimit || s.MinDeposit is <1 or >16000 || s.MinWithdraw is <1 or >16000 || s.BuyTimeSeconds is <0 or >600 || s.MaxRounds is <2 or >100 || s.MinTransfer<1 || s.MaxTransfer< -1 || s.MaxManualBankSet< -1 || s.MaxStoredTransferTargets is <1 or >100 || string.IsNullOrWhiteSpace(s.AdminPermission) || s.AdminPermission.Length>128 || s.PistolRoundNumbers is null || s.PistolRoundNumbers.Any(n=>n<1||n>200) || s.BlockDepositOnMapPrefixes is null || s.BlockDepositOnMapPrefixes.Any(p=>string.IsNullOrWhiteSpace(p)||p.Length>32) || s.MaxWithdrawPistolRound is <0 or >16000 || s.MaxWithdrawPistolRoundBuyTime is <0 or >16000)
            throw new InvalidDataException("Ungültige Bankeinstellungen.");
        foreach(var buttons in new[]{s.DepositButtons,s.WithdrawButtons,s.TransferButtons})if(buttons is null || buttons.Length is <1 or >20 || buttons.Any(v=>v<1||v>s.UnlimitedLimit))throw new InvalidDataException("Ungültige Betragsauswahl.");
    }
}

public readonly record struct CashContext(bool Known,bool Warmup,bool Freeze,bool ActiveRound,int Round,double Seconds,bool Alive,bool ControllingBot,bool Died,bool MapBlocked,string Map);
public static class BankPolicy
{
    public static long[] MenuAmounts(IEnumerable<long> values,long minimum,long maximum)
    {
        if(maximum<minimum)return [];
        var choices=values.Where(v=>v>=minimum&&v<=maximum).Distinct().Order().ToArray();
        return choices.Length>0?choices:[minimum];
    }
    public static bool Steam(string id)=>id.Length==17&&id.All(char.IsAsciiDigit)&&ulong.TryParse(id,out var n)&&n>=76561197960265728&&n<=76561202255233023;
    public static bool Pistol(EconomySettings s,int round)=>s.PistolRoundNumbers.Contains(round)||(s.DetectPistolRounds&&(round==1||round==s.MaxRounds/2+1));
    public static string? CashDenied(EconomySettings s,CashContext c,bool deposit)
    {
        if(!s.Enabled)return "Bank ist ausgeschaltet.";
        if(!c.Known||(!c.Warmup&&c.Round<1))return "Rundenstatus noch unbekannt.";
        if(c.MapBlocked)return "Bank auf dieser Map wegen erhöhtem Startgeld gesperrt.";
        if(!c.Alive||c.ControllingBot||(s.BlockCashAfterDeath&&c.Died))return "Cash-Aktionen nur mit der eigenen lebenden Spielfigur.";
        if(deposit)
        {
            if(s.BlockDepositDuringWarmup&&c.Warmup)return "Einzahlen im Warmup gesperrt.";
            if(s.DepositOnlyDuringFreezeTime&&!c.Freeze)return "Einzahlen nur in der Freezetime.";
            if(s.BlockDepositOnMapPrefixes.Any(x=>c.Map.StartsWith(x,StringComparison.OrdinalIgnoreCase)))return "Einzahlen auf diesem Maptyp gesperrt.";
        }
        else
        {
            if(s.BlockWithdrawDuringWarmup&&c.Warmup)return "Abheben im Warmup gesperrt.";
            if(!c.Warmup&&Pistol(s,c.Round))
            {
                if(s.BlockWithdrawDuringPistolRounds)return "Abheben in Pistolenrunden gesperrt.";
                if(!(c.Freeze?s.AllowWithdrawDuringPistolRound:s.AllowWithdrawDuringPistolRoundBuyTime&&c.ActiveRound&&c.Seconds>=0&&c.Seconds<=s.BuyTimeSeconds))return "Abheben in dieser Pistolenrunde gesperrt.";
            }
            if(s.WithdrawOnlyDuringFreezeTime&&!c.Freeze&&!(s.AllowWithdrawDuringBuyTime&&c.ActiveRound&&c.Seconds>=0&&c.Seconds<=s.BuyTimeSeconds))return "Abheben nur während Freeze-/Buytime.";
        }
        return null;
    }
    public static long Amount(string text,long all,long min,long max)
    {
        long value=text.Equals("all",StringComparison.OrdinalIgnoreCase)?all:long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:-1;
        if(value<min||value>max)throw new BankRejected($"Betrag muss zwischen {min:N0} und {max:N0} liegen.");return value;
    }
    public static string ResetKey(string mode,DateTime now)=>mode switch{"Daily"=>now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),"Weekly"=>$"{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now):00}","Monthly"=>now.ToString("yyyy-MM",CultureInfo.InvariantCulture),_=>"never"};
}
public sealed class BankRejected(string message):Exception(message);


