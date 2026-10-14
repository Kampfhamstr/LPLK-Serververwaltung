namespace LPLK_Economy;
public sealed partial class Plugin
{
    private int WebBankProbe()
    {
        int checks=0;void Check(bool value){if(!value)throw new InvalidDataException("Webbank-Prüfung fehlgeschlagen.");checks++;}
        const string a="76561198000000001";
        var rules=settings with{Profile="webbank-probe-11110004",ResetMode="Never",StartBank=0,MaxBank=100000};
        using var db=new Ledger(Path.Combine(Core.PluginDataDirectory,"webbank-probe.sqlite3"),rules);db.Open(a,"Prüfkonto");
        string seed="web:"+Guid.NewGuid().ToString("N");DateTimeOffset now=DateTimeOffset.UtcNow;
        var request=new PanelBankRequest(seed,rules.Profile,db.Period,a,db.AccountRevision(a),1,"",100,"set",500,"Isolierte Linux-Prüfung",now.AddMinutes(5));
        var receipt=db.PanelAdjust(request,now,50000);Check(db.Balance(a)==500);Check(db.PanelAdjust(request,now,50000)==receipt);Check(db.PanelReceipt(seed,db.Period)==receipt);
        var stale=request with{Id=seed+"x",Amount=501};bool rejected=false;try{db.PanelAdjust(stale,now,50000);}catch(BankRejected){rejected=true;}Check(rejected);Check(db.Balance(a)==500);db.Verify();return checks;
    }
}
