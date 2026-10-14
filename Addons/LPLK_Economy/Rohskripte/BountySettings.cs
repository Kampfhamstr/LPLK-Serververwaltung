namespace LPLK_Economy;

public sealed record BountySettings
{
    public bool Enabled {get;init;}=true;
    public int MinBounty {get;init;}=1000;
    public int MaxBounty {get;init;}=10000;
    public bool LargeBountyAmountsEnabled {get;init;}=true;
    public bool PreferBankForDirectBountyCommands {get;init;}=true;
    public bool AllowBankFunding {get;init;}=true;
    public bool AllowCashFunding {get;init;}=true;
    public bool AllowDuringWarmup {get;init;}=true;
    public bool AllowCashFundingDuringWarmup {get;init;}
    public bool OnlyEnemyTeam {get;init;}
    public bool AllowBankPercentBounty {get;init;}=true;
    public int[] BankPercentButtons {get;init;}=[10,20,30,40,50];
    public bool OnlyAliveTargets {get;init;}
    public bool BlockBots {get;init;}=true;
    public bool BlockSelfBounty {get;init;}=true;
    public bool TakeMoneyImmediately {get;init;}=true;
    public bool AllowStacking {get;init;}=true;
    public bool PersistAcrossRounds {get;init;}=true;
    public bool RefundIfTargetDisconnects {get;init;}=true;
    public bool IgnoreTeamKills {get;init;}=true;
    public string RewardMode {get;init;}="CashThenBankOverflow";
    public bool AnnounceBountyPlaced {get;init;}=true;
    public bool AnnounceBountyClaimed {get;init;}=true;
}
public sealed record BountyMarkerSettings
{
    public bool Enabled {get;init;}=true;
    public bool AnnounceBountiesOnRoundStart {get;init;}=true;
    public bool ShowCenterMessageOnRoundStart {get;init;}=true;
    public bool ShowCenterMessageOnBountyPlaced {get;init;}=true;
    public bool OnlyEnemyTeamCanSeeHud {get;init;}=true;
    public int MaxBountiesAnnounced {get;init;}=5;
}
public static class BountyPolicy
{
    public static void Validate(BountySettings s,BountyMarkerSettings marker,long[] buttons)
    {
        if(s is null||marker is null||s.MinBounty<1||s.MaxBounty< -1||!s.LargeBountyAmountsEnabled&&s.MaxBounty>=0&&s.MaxBounty<s.MinBounty||s.RewardMode is not ("Bank" or "Cash" or "CashThenBankOverflow")||s.BankPercentButtons is null||s.BankPercentButtons.Length is <1 or >20||s.BankPercentButtons.Any(v=>v is <1 or >100)||marker.MaxBountiesAnnounced is <1 or >20||buttons is null||buttons.Length is <1 or >20||buttons.Any(v=>v is <1 or >int.MaxValue))throw new InvalidDataException("Ungültige Kopfgeldregeln.");
        // Persistent accounts identify human Steam players. Bot economy was never
        // imported; a bot-enabled configuration must not silently pretend to work.
        if(!s.BlockBots)throw new InvalidDataException("Kopfgeld auf Bots benötigt ein eigenes Botkonten-Konzept; BlockBots muss aktiv bleiben.");
    }
    public static bool Funding(BountySettings s,string source,bool warmup)=>source=="Bank"?s.AllowBankFunding:source=="Cash"&&s.AllowCashFunding&&(!warmup||s.AllowCashFundingDuringWarmup);
    public static long Maximum(BountySettings s,long available,bool percent=false)=>Math.Max(0,Math.Min(int.MaxValue,!percent&&!s.LargeBountyAmountsEnabled&&s.MaxBounty>=0?Math.Min(available,s.MaxBounty):available));
    public static long Percent(long balance,int percent)=>percent is <1 or >100?0:Math.Min(int.MaxValue,(long)decimal.Floor(balance*(decimal)percent/100));
    public static string Choose(BountySettings s,long bank,long cash,long amount,bool all,bool warmup)
    {
        string first=s.PreferBankForDirectBountyCommands?"Bank":"Cash",second=first=="Bank"?"Cash":"Bank";
        bool Fits(string source)=>Funding(s,source,warmup)&&Maximum(s,source=="Bank"?bank:cash)>=Math.Max(s.MinBounty,all?0:amount);
        return Fits(first)?first:Fits(second)?second:throw new BankRejected("Keine freigegebene Quelle reicht für dieses Kopfgeld. Bank und Cash werden nicht gemischt.");
    }
}
