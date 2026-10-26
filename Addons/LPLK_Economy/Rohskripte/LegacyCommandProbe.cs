namespace LPLK_Economy;
public sealed partial class Plugin
{
    // Called only by the existing empty-test-profile probe; never opens the player database.
    private int LegacyAdminProbe()
    {
        if(!ready||!Active||settings.Profile!="standard-test-89450e53"||Core.PlayerManager.GetAllValidPlayers().Any(p=>!p.IsFakeClient))throw new InvalidOperationException("Isolated test profile required");
        const string a="76561198000000001";
        var rules=settings with{Profile="legacy-commands-probe-89450e53",StartBank=0,ResetMode="Never",MaxBank=200000,MaxManualBankSet=50000};
        using var probe=new Ledger(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"legacy-commands-probe.sqlite3"),rules);
        string id="probe:"+Guid.NewGuid().ToString("N");probe.Open(a,"Befehlsprüfkonto");int checks=0;
        void Balance(long expected){if(probe.Balance(a)!=expected)throw new InvalidDataException("Legacy admin probe balance mismatch");checks++;}
        probe.AdjustLegacy(id+":set",probe.Period,a,a,100000,"set",50000);Balance(50000);
        probe.AdjustLegacy(id+":add",probe.Period,a,a,100000,"add",50000);Balance(150000);
        probe.AdjustLegacy(id+":remove",probe.Period,a,a,100000,"remove",50000);Balance(50000);
        probe.AdjustLegacy(id+":cap",probe.Period,a,a,long.MaxValue,"add",50000);Balance(200000);
        probe.AdjustLegacy(id+":one",probe.Period,a,a,1,"remove",50000);Balance(199999);
        probe.AdjustLegacy(id+":cap",probe.Period,a,a,long.MaxValue,"add",50000);Balance(199999);
        probe.AdjustLegacy(id+":zero",probe.Period,a,a,long.MaxValue,"remove",50000);Balance(0);
        bool rejected=false;try{LegacyAdminAmount("all");}catch(BankRejected){rejected=true;}if(!rejected)throw new InvalidDataException("Legacy admin all accepted");checks++;
        probe.Verify();return checks;
    }
}
