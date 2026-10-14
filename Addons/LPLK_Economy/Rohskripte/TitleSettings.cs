namespace LPLK_Economy;
public sealed record TitleSettings
{
    public bool Enabled {get;init;}=true;
    public bool AnnounceTitlesOnJoin {get;init;}=true;
    public bool AnnounceJoinWithoutTitles {get;init;}=true;
    public int MaxTitlesPerJoinLine {get;init;}=4; // Legacy reference; only the selected title is announced.
    public bool AnnouncePioneerMilestones {get;init;}=true;
    public int PermanentTicketsPerRulerTitle {get;init;}=1;
}
public sealed record InterestSettings
{
    public bool Enabled {get;init;}=true;
    public bool PayOnValidRoundEnd {get;init;}=true;
    public int MinActivePlayersForInterest {get;init;}=2;
    public int InterestBasisPointsPerTitle {get;init;}=10;
    public int InterestBasisPointsPerRulerTitle {get;init;}=100;
    public int InterestRateDivisor {get;init;}=10;
    public int MaxTotalInterestBasisPoints {get;init;}=1000;
    public long MinBankForInterest {get;init;}=1;
    public long MaxInterestPerPlayer {get;init;}=-1;
    public bool AnnounceToPlayer {get;init;}=true;
    public long AnnounceOnlyIfInterestAtLeast {get;init;}=1;
}
public sealed record TaxTier(long Threshold,int TaxBasisPoints);
public sealed record LuxuryTaxSettings
{
    public bool Enabled {get;init;}=true;
    public bool CollectOnRoundStart {get;init;}=true;
    public int MinActivePlayersForLuxuryTax {get;init;}=2;
    public int SuperpotShareBasisPoints {get;init;}=1000;
    public bool AnnounceToPlayer {get;init;}=true;
    public bool AnnounceSummaryToChat {get;init;}=true;
    public TaxTier[] TaxTiers {get;init;}=[new(1_000_000,2),new(10_000_000,20),new(20_000_000,40),new(30_000_000,60),new(40_000_000,80),new(50_000_000,100)];
}
public static class TitlePolicy
{
    public static void Validate(EconomySettings s)
    {
        var t=s.TitleSystem;var i=s.InterestSystem;var x=s.LuxuryTax;
        if(t is null||i is null||x is null||t.MaxTitlesPerJoinLine is <1 or >20||t.PermanentTicketsPerRulerTitle is <0 or >100||i.MinActivePlayersForInterest is <0 or >64||i.InterestBasisPointsPerTitle is <0 or >10000||i.InterestBasisPointsPerRulerTitle is <0 or >10000||i.InterestRateDivisor is <1 or >100000||i.MaxTotalInterestBasisPoints is < -1 or >10000||i.MinBankForInterest is <0 or >9_000_000_000_000_000||i.MaxInterestPerPlayer is < -1 or >9_000_000_000_000_000||i.AnnounceOnlyIfInterestAtLeast<0||x.MinActivePlayersForLuxuryTax is <0 or >64||x.SuperpotShareBasisPoints is <0 or >10000||x.TaxTiers is null||x.TaxTiers.Length is <1 or >100||x.TaxTiers.Any(v=>v is null||v.Threshold is <1 or >9_000_000_000_000_000||v.TaxBasisPoints is <1 or >10000)||x.TaxTiers.Select(v=>v.Threshold).Distinct().Count()!=x.TaxTiers.Length)throw new InvalidDataException("Ungültige Titel-, Zins- oder Steuerregeln.");
    }
    public static int InterestRate(InterestSettings s,int normal,int rulers)
    {
        long raw=checked((long)normal*s.InterestBasisPointsPerTitle+(long)rulers*s.InterestBasisPointsPerRulerTitle);
        return (int)((s.MaxTotalInterestBasisPoints<0?raw:Math.Min(raw,s.MaxTotalInterestBasisPoints))/s.InterestRateDivisor);
    }
    public static int TaxRate(LuxuryTaxSettings s,long balance)=>s.TaxTiers.Where(t=>t.Threshold<=balance).OrderBy(t=>t.Threshold).LastOrDefault()?.TaxBasisPoints??0;
    public static string Group(EconomyTitle t)=>t.Id.StartsWith("bounty_",StringComparison.Ordinal)||t.Id=="ruler_bounty_claim"?"Kopfgeld":t.Category switch
    {
        "Vermögen" or "Spaß" or "Episch"=>"Vermögen", "Spion" or "Auftrag" or "Erfolg" or "Fehlschlag" or "Opfer" or "Sicherheit" or "Räuber-Level"=>"Raub & Spionage",
        "Lotto" or "Dauerlos" or "Superpot"=>"Lotto", "Wetten" or "All-In"=>"Wetten", "Bank" or "Transfer"=>"Bankaktivität", "Pionier" or "Herrscher"=>"Pionier & Herrscher", _=>"Kopfgeld"
    };
}
