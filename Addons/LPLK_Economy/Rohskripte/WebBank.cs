using System.Text.Json;
namespace LPLK_Economy;

// A web snapshot is never an alternative writer. All adjustments return here.
public sealed record PanelBankRequest(string Id,string Profile,long Period,string Account,long Revision,long UserId,string ActorSteamId,int ActorRank,
    string Operation,long Amount,string Reason,DateTimeOffset ExpiresAt);
public sealed record PanelBankReceipt(PanelBankRequest Request,long Before,long After,DateTimeOffset AppliedAt);
public sealed partial class Ledger
{
    private Ledger(string path,string server,EconomySettings settings)
    {
        scope=server;Profile=settings.Profile;Limit=settings.Limit;StartBank=settings.StartBank;
        titleRules=settings;robberyRules=settings.BankRobbery;
        db=new(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=1}.ToString());
        try{db.Open();}catch{db.Dispose();throw;}
    }
    public Func<DateTimeOffset,Dictionary<string,object>> CreateWebSnapshotReader()
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            string path=SnapshotReadPath,server=scope;var rules=titleRules;
            return at=>{using var reader=new Ledger(path,server,rules);return reader.WebRoundSnapshot(at);};
        }
    }
    public long AccountRevision(string account){lock(gate)return Number("SELECT coalesce(max(seq),0) FROM entries WHERE account=$p0",account);}
    public PanelBankReceipt? PanelReceipt(string id,long period)
    {
        lock(gate)
        {
            var json=Text("SELECT json FROM module_state WHERE period=$p0 AND owner='LPLK_WebBank' AND key=$p1",period,id);
            return json is null?null:JsonSerializer.Deserialize<PanelBankReceipt>(json)??throw new InvalidDataException("Ungültiger Webbank-Beleg.");
        }
    }
    public PanelBankReceipt PanelAdjust(PanelBankRequest r,DateTimeOffset now,long maxSet)
    {
        if(r is null||r.Id is null||!Key(r.Id,64)||!r.Id.StartsWith("web:",StringComparison.Ordinal)||r.Profile!=Profile||r.Account is null||!BankPolicy.Steam(r.Account)||r.UserId<1||r.ActorSteamId is null||r.ActorSteamId!=""&&!BankPolicy.Steam(r.ActorSteamId)||r.ActorRank is not (10 or 100)||r.Revision<1||r.Period<1||r.Amount is <0 or >9_000_000_000_000_000||r.Operation is not ("set" or "add" or "remove")||r.Reason is null||r.Reason.Trim().Length is <5 or >120||r.Reason.Any(char.IsControl))throw new BankRejected("Ungültiger Webbank-Auftrag.");
        return Atomic(()=>
        {
            // Retries retrieve the original durable receipt even after other bookings/reset.
            if(PanelReceipt(r.Id,r.Period) is {} old)
            {
                if(old.Request!=r)throw new BankRejected("Auftragsnummer mit anderem Inhalt bereits verwendet.");
                return old;
            }
            if(r.ExpiresAt<=now||r.ExpiresAt>now.AddMinutes(10))throw new BankRejected("Webbank-Auftrag abgelaufen oder ungültig datiert.");
            if(r.Period!=Period||!Exists(r.Account)||AccountRevision(r.Account)!=r.Revision)throw new BankRejected("Kontostand inzwischen geändert. Neu laden und erneut bestätigen.");
            if(r.Operation=="set"&&maxSet>=0&&r.Amount>maxSet)throw new BankRejected("Betrag überschreitet das Adminlimit.");
            long before=Balance(r.Account),delta=r.Operation=="set"?r.Amount-before:r.Operation=="add"?r.Amount:-r.Amount;
            var receipt=new PanelBankReceipt(r,before,checked(before+delta),now);
            BookInside(new(r.Id,r.Period,"LPLK_WebBank",$"Webadmin #{r.UserId}: {r.Operation} {r.Amount}",
                [new(r.Account,delta),new("system:admin",-delta)],
                [new(r.Id,0,JsonSerializer.Serialize(receipt))]),false);
            return receipt;
        });
    }
    public Dictionary<string,object> WebRoundSnapshot(DateTimeOffset now)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            Dictionary<string,object> Capture()
            {
                var accounts=Accounts(int.MaxValue);
                fault?.Invoke("web-snapshot-read");
                var files=new Dictionary<string,object>();
                foreach(var account in accounts)files[account.Id+".json"]=WebPlayer(account.Id,now);
                files["ranking.json"]=WebRanking(now);return files;
            }
            if(transaction is not null)return Capture();
            // A process-local lock cannot prevent another game server booking.
            // A read-only WAL snapshot keeps all exported files consistent while
            // allowing other connections to commit without waiting for this read.
            using var tx=db.BeginTransaction(deferred:true);transaction=tx;
            try{var files=Capture();tx.Commit();return files;}
            finally{transaction=null;}
        }
    }
    public object WebPlayer(string account,DateTimeOffset now)
    {
        lock(gate)
        {
            if(!BankPolicy.Steam(account)||!Exists(account))throw new BankRejected("Konto nicht vorhanden.");
            return new{schema=1,profile=Profile,steamId=account,exportPolicy="RoundEnd",generatedAt=now,period=Period,revision=AccountRevision(account),
                balance=Balance(account),pendingCash=Blocked(account),betClaims=PlayerBetClaims(account),
                permanentTickets=LotteryPlan(account).Count,purchasedTickets=LotteryPlan(account).Bought,
                vaultLevel=Security(account,robberyRules,now.ToUnixTimeSeconds()).Vault,
                robberLevel=Security(account,robberyRules,now.ToUnixTimeSeconds()).RobberSkill,
                spyLevel=Security(account,robberyRules,now.ToUnixTimeSeconds()).SpySkill,
                activeTitle=TitlesProgress(account).Active,history=History(account,20)};
        }
    }
    public object WebRanking(DateTimeOffset now)
    {
        lock(gate)return new{schema=1,profile=Profile,exportPolicy="RoundEnd",generatedAt=now,period=Period,scope=Networked?"network":"server",databaseIdentity=NetworkIdentity,totalAccounts=AccountCount,
            rows=Accounts(100).Select(a=>new{steamId=a.Id,name=a.Name,balance=a.Balance}).ToArray()};
    }
}
