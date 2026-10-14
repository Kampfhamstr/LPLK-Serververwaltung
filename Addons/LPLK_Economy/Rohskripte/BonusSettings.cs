namespace LPLK_Economy;
public sealed record BonusEffect(bool Enabled,int Weight,int Amount);
public sealed record BonusEffects
{
    public BonusEffect CashBonus {get;init;}=new(true,25,2500);
    public BonusEffect BankBonus {get;init;}=new(true,25,5000);
    public BonusEffect FreeTicket {get;init;}=new(true,10,3);
    public BonusEffect NextKillBankBonus {get;init;}=new(true,10,5000);
    // Armor/heal deliberately excluded by the reference, even with legacy settings.
    public IEnumerable<(string Kind,BonusEffect Effect)> Pool(bool pistol)
    {
        if(!pistol)yield return ("CashBonus",CashBonus);
        yield return ("BankBonus",BankBonus);yield return ("FreeTicket",FreeTicket);yield return ("NextKillBankBonus",NextKillBankBonus);
    }
}
public sealed record AbilitySettings
{
    public bool Enabled {get;init;}=true;
    public int ChancePerTicketBasisPoints {get;init;}=2000;
    public int MaxBonusesPerPlayerPerRound {get;init;}=1;
    public int MaxTicketsCountedPerPlayer {get;init;}=-1;
    public bool AllowDuringWarmup {get;init;}
    public bool IgnoreBots {get;init;}=true;
    public bool AnnounceToPlayer {get;init;}=true;
    public bool AnnouncePublic {get;init;}
    public bool ShowCenterMessage {get;init;}=true;
    public BonusEffects Effects {get;init;}=new();
}
public sealed record CashCollectionSettings
{
    public bool Enabled {get;init;}=true;
    public bool BlockIfRoundStartCashAboveBankLimit {get;init;}=true;
    public bool CollectOnLastHalfRound {get;init;}=true;
    public bool CollectOnFinalRound {get;init;}=true;
    public bool SetPlayerCashToZero {get;init;}=true;
    public int MinCashToCollect {get;init;}=1;
    public int MaxCashPerPlayer {get;init;}=-1;
    public bool TrackCashOnPlayerDeath {get;init;}=true;
    public bool UseDeathCashSnapshotIfHigher {get;init;}=true;
    public bool CollectDisconnectedDeathSnapshots {get;init;}=true;
    public bool SendDebugMessageNextRound {get;init;}=true;
    public bool DebugMessageToAdminsOnly {get;init;}=true;
    public bool IgnoreBots {get;init;}=true;
    public bool AnnounceCollection {get;init;}=true;
    public bool NotifyAffectedPlayers {get;init;}=true;
}
public static class BonusPolicy
{
    public static void Validate(AbilitySettings s)
    {
        if(s is null||s.Effects is null||s.ChancePerTicketBasisPoints is <0 or >10000||s.MaxBonusesPerPlayerPerRound is <0 or >10||s.MaxTicketsCountedPerPlayer is < -1 or >1100)throw new InvalidDataException("Ungültige Lottofähigkeiten.");
        foreach(var (kind,e) in s.Effects.Pool(false))if(e is null||e.Weight is <0 or >100000||e.Amount<0||e.Amount>(kind=="FreeTicket"?1000:1_000_000))throw new InvalidDataException("Ungültiger Lottoeffekt.");
    }
    public static void Validate(LotterySettings s)
    {
        var c=s.CashToSuperpot;
        if(s.PermanentTicketUpgradeDiscountChanceBasisPoints is <0 or >10000||s.PermanentTicketUpgradeDiscountMinPercent is <0 or >100||s.PermanentTicketUpgradeDiscountMaxPercent is <0 or >100||s.PermanentTicketUpgradeDiscountMinPercent>s.PermanentTicketUpgradeDiscountMaxPercent||c is null||c.MinCashToCollect is <1 or >1_000_000||c.MaxCashPerPlayer is < -1 or >1_000_000)throw new InvalidDataException("Ungültige Lotto-Rabatt-/Cashregeln.");
    }
    public static string CollectionReason(CashCollectionSettings s,int round,int maxRounds)=>maxRounds>1&&s.CollectOnLastHalfRound&&round==maxRounds/2?"letzter Halbzeitrunde":maxRounds>1&&s.CollectOnFinalRound&&round==maxRounds?"letzter Runde":"";
    public static int CollectAmount(CashCollectionSettings s,int live,int snapshot,bool offline=false)
    {
        if(live<0||snapshot<0)throw new BankRejected("Ungültiger Cashstand.");
        int source=offline?s.CollectDisconnectedDeathSnapshots?snapshot:0:s.UseDeathCashSnapshotIfHigher?Math.Max(live,snapshot):live;
        return source<s.MinCashToCollect?0:s.MaxCashPerPlayer<0?source:Math.Min(source,s.MaxCashPerPlayer);
    }
}
