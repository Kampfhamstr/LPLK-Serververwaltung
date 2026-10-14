namespace LPLK_Economy;

public sealed record RobberySettings
{
    public bool Enabled { get; init; } = true;
    public int RobberyCost { get; init; } = 100000;
    public int SpyCost { get; init; } = 20000;
    public int SpySkillUpgradeCost { get; init; } = 200000;
    public int MaxSpySkillLevel { get; init; } = 5;
    public int PassiveSpyReportChancePerLevelPercent { get; init; } = 20;
    public bool SpyLogEnabled { get; init; } = true;
    public bool SpyLogRecordUnknownEvents { get; init; } = true;
    public int SpyLogMaxEntriesPerPlayer { get; init; } = 100;
    public int SpyLogMaxGlobalEvents { get; init; } = 500;
    public bool SpyScanAllOnlineTargets { get; init; } = true;
    public int SpyMaxTargetsShown { get; init; } = 20;
    public bool SpyShowTargetsBelowRobberyMinimum { get; init; } = true;
    public int RobberSkillUpgradeCost { get; init; } = 1000000;
    public int MaxRobberSkillLevel { get; init; } = 5;
    public int SuccessChanceBonusPerRobberSkillLevel { get; init; } = 8;
    public int BaseSuccessChancePercent { get; init; } = 50;
    public int MinSuccessChancePercent { get; init; } = 5;
    public int MinTargetBank { get; init; } = 250000;
    public bool ExecuteOnNextValidRoundEnd { get; init; } = true;
    public int MaxActiveOrdersPerSponsor { get; init; } = 1;
    public int MaxActiveOrdersPerTarget { get; init; } = 1;
    public int SameTargetCooldownHours { get; init; } = 1;
    public bool DisableProtectionAboveTargetBank { get; init; } = false;
    public long ProtectionDisabledTargetBankThreshold { get; init; } = 50_000_000;

    public bool HighRollerProtectionBypassEnabled { get; init; } = true;
    public long HighRollerProtectionBypassTargetBankThreshold { get; init; } = 50_000_000;
    public int HighRollerProtectionAbsoluteMinutes { get; init; } = 15;
    public long HighRollerProtectionBypassBaseCost { get; init; } = 1_000_000;
    public int HighRollerProtectionBypassCostMultiplier { get; init; } = 2;
    public long HighRollerProtectionBypassMaxCost { get; init; } = 50_000_000;
    public int HighRollerProtectionBypassSuperpotShareBasisPoints { get; init; } = 1000;
    public bool HighRollerProtectionBypassResetWhenTargetBelowThreshold { get; init; } = true;

    public bool DisableVaultAboveTargetBank { get; init; } = false;

    public bool RobinHoodBonusEnabled { get; init; } = true;
    public long RobinHoodTargetBankThreshold { get; init; } = 50_000_000;
    public long RobinHoodPoorAttackerBankThreshold { get; init; } = 1_000_000;
    public long RobinHoodVeryPoorAttackerBankThreshold { get; init; } = 250_000;
    public int RobinHoodPoorAttackerBonusPercentPoints { get; init; } = 10;
    public int RobinHoodVeryPoorAttackerBonusPercentPoints { get; init; } = 15;
    public int RobinHoodMaxBonusPercentPoints { get; init; } = 15;

    public int ProtectionHoursAfterSuccess { get; init; } = 24;
    public int ProtectionHoursAfterFail { get; init; } = 1;
    public bool UpgradesValidUntilBankReset { get; init; } = true;
    public int VaultDurationDays { get; init; } = 30;
    public int SecurityDurationDays { get; init; } = 30;
    public int SecuritySuccessReductionPercentPoints { get; init; } = 10;
    public int MinLootPercent { get; init; } = 10;
    public int NoVaultMaxLootPercent { get; init; } = 80;
    public int OldSteelVaultMaxLootPercent { get; init; } = 60;
    public int ModernSteelVaultMaxLootPercent { get; init; } = 40;
    public int TitanVaultMaxLootPercent { get; init; } = 25;
    public int OldSteelVaultPrice { get; init; } = 500000;
    public int ModernSteelVaultPrice { get; init; } = 1000000;
    public int TitanVaultPrice { get; init; } = 1500000;
    public int AlarmPrice { get; init; } = 200000;
    public int DogsPrice { get; init; } = 250000;
    public int TowerPrice { get; init; } = 300000;
    public int GuardPrice { get; init; } = 400000;
    public bool AnnouncePublicResult { get; init; } = false;
    public bool ProcessEconomyOnRoundEndWithoutWinner { get; init; } = true;
    public int MinActivePlayersForEconomyRoundEnd { get; init; } = 2;
}