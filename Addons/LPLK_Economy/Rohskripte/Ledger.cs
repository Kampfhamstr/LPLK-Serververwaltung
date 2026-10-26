using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LPLK_Economy;

public sealed record Posting(string Account,long Delta);
public sealed record StateChange(string Key,long ExpectedRevision,string Json);
public sealed record Booking(string Id,long Period,string Owner,string Reason,Posting[] Postings,StateChange[]? Changes=null);
public sealed record Receipt(string Id,long Period,bool Duplicate);
public sealed record BankAccount(string Id,string Name,long Balance);
public sealed record BankEntry(string Id,string Reason,long Delta,long Balance,string At);
public sealed record CashIntent(string Id,string Account,string Session,long Period,int Before,int After,long Delta,string Status);

// Sole writer. All bank postings, receipts, period changes and dependent module state
// commit in one SQLite transaction. No recovery path replaces an unreadable database.
public sealed partial class Ledger : IDisposable
{
    private readonly object gate=new();
    private SqliteConnection db;
    private SqliteTransaction? transaction;
    private readonly Action<string>? fault;
    private bool disposed;
    public long Limit { get; private set; }
    public long StartBank { get; private set; }
    public string Profile { get; }
    public Ledger(string path,EconomySettings settings,Action<string>? fault=null,string? server=null,bool provision=false,bool roundMemory=false)
    {
        EconomySettings.Validate(settings);Limit=settings.Limit;StartBank=settings.StartBank;Profile=settings.Profile;this.fault=fault;robberyRules=settings.BankRobbery;titleRules=settings;
        scope=server??"";ValidateScope(scope);
        path=Path.GetFullPath(path);
        if(Networked&&!provision&&!File.Exists(path))throw new InvalidDataException("Gemeinsame Bank fehlt; kein lokales Ersatzkonto angelegt.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach(var item in new[]{path,path+"-wal",path+"-shm"})if(new FileInfo(item).LinkTarget is not null)throw new InvalidDataException("Verlinkte Bankdatei abgelehnt.");
        bool exists=File.Exists(path);
        if(Networked)AcquireScopeLease(path);
        db=new(new SqliteConnectionStringBuilder{DataSource=path,Mode=exists?SqliteOpenMode.ReadWrite:SqliteOpenMode.ReadWriteCreate,Pooling=false,DefaultTimeout=1}.ToString());
        try
        {
            db.Open();long version=Number("PRAGMA user_version");
            if(exists&&(Networked?version!=4:version is not (1 or 2 or 3)))throw new InvalidDataException("Unbekanntes oder unvollständiges Bankformat; Datei bleibt erhalten.");
            Exec("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=256;");
            if(Networked){InitializeNetwork(settings,exists);Verify();return;}
            if(!exists)Atomic(()=>
            {
                Exec("""
                    CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT NOT NULL) STRICT;
                    CREATE TABLE accounts(id TEXT PRIMARY KEY,name TEXT NOT NULL,balance INTEGER NOT NULL) STRICT;
                    CREATE TABLE preferences(id TEXT PRIMARY KEY,muted INTEGER NOT NULL CHECK(muted IN (0,1))) STRICT;
                    CREATE TABLE transactions(id TEXT PRIMARY KEY,period INTEGER NOT NULL,owner TEXT NOT NULL,reason TEXT NOT NULL,fingerprint TEXT NOT NULL,at TEXT NOT NULL) STRICT;
                    CREATE TABLE entries(seq INTEGER PRIMARY KEY,tx TEXT NOT NULL REFERENCES transactions(id),account TEXT NOT NULL REFERENCES accounts(id),delta INTEGER NOT NULL,balance INTEGER NOT NULL) STRICT;
                    CREATE INDEX entries_account ON entries(account,seq DESC);
                    CREATE TABLE module_state(period INTEGER NOT NULL,owner TEXT NOT NULL,key TEXT NOT NULL,revision INTEGER NOT NULL,json TEXT NOT NULL,PRIMARY KEY(period,owner,key)) STRICT;
                    CREATE TABLE cash(id TEXT PRIMARY KEY,account TEXT NOT NULL REFERENCES accounts(id),session TEXT NOT NULL,period INTEGER NOT NULL,before_cash INTEGER NOT NULL,after_cash INTEGER NOT NULL,delta INTEGER NOT NULL,status TEXT NOT NULL CHECK(status IN ('pending','committed','cancelled'))) STRICT;
                    CREATE INDEX cash_pending ON cash(account,status);
                    PRAGMA user_version=1;
                    """);
                Put("profile",Profile);Put("period","1");Put("resetMode","Never");Put("resetKey","never");Put("resetAt",DateTimeOffset.UtcNow.ToString("O"));return 0;
            });
            if(Text("SELECT value FROM meta WHERE key='profile'")!=Profile)throw new InvalidDataException("Bankdatei gehört zu anderem Profil.");
            if(version<2)UpgradeBettingSchema(); if(version<3)UpgradeLotterySchema();
            Verify();if(roundMemory)EnableRoundMemory(path);
        }
        catch{db.Dispose();networkLease?.Dispose();throw;}
    }
    private SqliteCommand Command(string sql,params object[] values)
    {
        var c=db.CreateCommand();c.Transaction=transaction;c.CommandText=ScopeSql(sql);
        for(int i=0;i<values.Length;i++)c.Parameters.AddWithValue("$p"+i,values[i] is string s?ScopeAccount(s):values[i]);return c;
    }
    private int Exec(string sql,params object[] v){using var c=Command(sql,v);return c.ExecuteNonQuery();}
    private long Number(string sql,params object[] v){using var c=Command(sql,v);var result=c.ExecuteScalar();return result is null or DBNull?0:Convert.ToInt64(result);}
    private string? Text(string sql,params object[] v){using var c=Command(sql,v);return c.ExecuteScalar() as string;}
    private void Put(string key,string value)=>Exec("INSERT INTO meta VALUES($p0,$p1) ON CONFLICT(key) DO UPDATE SET value=excluded.value",key,value);
    private T Atomic<T>(Func<T> work)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(transaction is not null)return work();
            long changes=InMemory?Number("SELECT total_changes()"):0;
            using var tx=db.BeginTransaction(deferred:false);transaction=tx;
            try{T value=work();fault?.Invoke("before-commit");bool changed=InMemory&&Number("SELECT total_changes()")!=changes;tx.Commit();if(changed)memoryRevision++;return value;}
            finally{transaction=null;}
        }
    }
    public long Period { get {lock(gate)return Number("SELECT CAST(value AS INTEGER) FROM meta WHERE key='period'");} }
    public int AccountCount { get {lock(gate)return (int)Number("SELECT count(*) FROM accounts WHERE id GLOB '[0-9]*'");} }
    public long TransactionCount { get {lock(gate)return Number("SELECT count(*) FROM transactions");} }
    public int PendingCount { get {lock(gate)return (int)Number("SELECT count(*) FROM cash WHERE status='pending'");} }
    public bool Blocked(string account){lock(gate)return Number(Networked?"SELECT count(*) FROM network_cash_locks WHERE account=$p0":"SELECT count(*) FROM cash WHERE account=$p0 AND status='pending'",account)>0;}
    public bool Exists(string account){lock(gate)return Number("SELECT count(*) FROM accounts WHERE id=$p0",account)>0;}
    public long Balance(string account){lock(gate)return Number("SELECT balance FROM accounts WHERE id=$p0",account);}
    public bool Muted(string account){lock(gate)return Number("SELECT muted FROM preferences WHERE id=$p0",account)==1;}
    public void SetMuted(string account,bool value)
    {
        if(!BankPolicy.Steam(account))throw new BankRejected("Ungültiges Konto.");
        Atomic(()=>Exec("INSERT INTO preferences VALUES($p0,$p1) ON CONFLICT(id) DO UPDATE SET muted=excluded.muted",account,value?1:0));
    }
    public void Configure(EconomySettings settings)
    {
        ConfigureWithPersistence(settings,()=>{});
    }
    internal void ConfigureWithPersistence(EconomySettings settings,Action persist)
    {
        EconomySettings.Validate(settings);
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(Networked)CheckNetworkRules(settings);
            if(settings.Profile!=Profile)throw new BankRejected("Profilwechsel erfordert einen vollständigen Serverprofilwechsel.");
            if(settings.Limit<Limit&&PendingCount>0)throw new BankRejected("Limit erst nach Prüfung offener Cash-Buchungen senken.");
            // Failed persistence must never require a second, potentially rejected configuration change.
            persist();Limit=settings.Limit;StartBank=settings.StartBank;robberyRules=settings.BankRobbery;titleRules=settings;
            if(Networked)Atomic(()=>Exec("UPDATE network_scopes SET settings=$p1 WHERE id=$p0",scope,JsonSerializer.Serialize(settings)));
        }
    }
    internal static bool Key(string value,int max=128)=>value.Length is >0&&value.Length<=max&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or ':' or '.' or '/');
    private static bool AccountKey(string id)=>BankPolicy.Steam(id)||id=="pool:lottery"||id.StartsWith("system:",StringComparison.Ordinal)&&Key(id);
    public static string SafeName(string name)=>new(name.Where(c=>!char.IsControl(c)&&c is not ('<' or '>' or '[' or ']')).Take(48).ToArray());
    public void Open(string account,string name)
    {
        if(!BankPolicy.Steam(account))throw new BankRejected("Ungültige SteamID64.");
        lock(gate)
        {
            name=SafeName(name);
            if(Exists(account)&&Text("SELECT name FROM accounts WHERE id=$p0",account)==name)return;
            Atomic(()=>
            {
                if(!Exists(account))
                {
                    Exec("INSERT INTO accounts VALUES($p0,$p1,0)",account,name);
                    BookInside(new($"open:{Period}:{account}",Period,"LPLK_Economy","Startguthaben",[new(account,StartBank),new("system:opening",-StartBank)]),true);
                }
                else Exec("UPDATE accounts SET name=$p1 WHERE id=$p0",account,name);
                return 0;
            });
        }
    }
    public Receipt Book(Booking request)=>Atomic(()=>BookInside(request,false));
    private Receipt BookInside(Booking r,bool allowPending)
    {
        if(!Key(r.Id)||!Key(r.Owner,64)||string.IsNullOrWhiteSpace(r.Reason)||r.Reason.Length>160||r.Postings is null||r.Postings.Length>128||r.Changes?.Length>64)throw new BankRejected("Ungültiger Buchungsauftrag.");
        if(r.Period!=Period)throw new BankRejected("Bankperiode hat gewechselt. Menü erneut öffnen.");
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r))));
        var old=Text("SELECT fingerprint FROM transactions WHERE id=$p0",TransactionId(r.Id));
        if(old is not null){if(old!=hash)throw new BankRejected("Transaktionsnummer mit anderem Inhalt bereits verwendet.");return new(r.Id,r.Period,true);}
        if(r.Postings.Any(p=>p is null||p.Account is null||!AccountKey(p.Account)||!p.Account.StartsWith("system:",StringComparison.Ordinal)&&p.Delta is < -9_000_000_000_000_000 or >9_000_000_000_000_000)||r.Postings.Select(p=>p.Account).Distinct(StringComparer.Ordinal).Count()!=r.Postings.Length||r.Postings.Sum(p=>(decimal)p.Delta)!=0)throw new BankRejected("Buchung ist nicht ausgeglichen oder überschreitet das technische Limit.");
        if(r.Changes is not null&&(r.Changes.Select(c=>c.Key).Distinct().Count()!=r.Changes.Length||r.Changes.Any(c=>!Key(c.Key)||c.ExpectedRevision<0||c.Json is null||c.Json.Length>16384)))throw new BankRejected("Ungültige Moduländerung.");
        foreach(var p in r.Postings)
        {
            if(!Exists(p.Account))
            {
                if(BankPolicy.Steam(p.Account))throw new BankRejected("Konto wurde noch nicht eröffnet.");
                Exec("INSERT INTO accounts VALUES($p0,'',0)",p.Account);
            }
            if(!allowPending&&Blocked(p.Account))throw new BankRejected("Konto gesperrt: Cash-Buchung muss geprüft werden.");
            long before=Balance(p.Account),after=checked(before+p.Delta);
            if(!p.Account.StartsWith("system:",StringComparison.Ordinal)&&(after<0||p.Delta>0&&after>Limit))throw new BankRejected(after<0?"Guthaben reicht nicht aus.":"Empfängerkonto hat sein Limit erreicht.");
        }
        Exec("INSERT INTO transactions VALUES($p0,$p1,$p2,$p3,$p4,$p5)",TransactionId(r.Id),r.Period,r.Owner,r.Reason,hash,DateTimeOffset.UtcNow.ToString("O"));
        foreach(var p in r.Postings)
        {
            long after=checked(Balance(p.Account)+p.Delta);
            Exec("UPDATE accounts SET balance=$p1 WHERE id=$p0",p.Account,after);
            Exec("INSERT INTO entries(tx,account,delta,balance) VALUES($p0,$p1,$p2,$p3)",TransactionId(r.Id),p.Account,p.Delta,after);
            fault?.Invoke("after-posting");
        }
        foreach(var s in r.Changes??[])
        {
            using var json=JsonDocument.Parse(s.Json);
            string table=StateTable(r.Owner,s.Key);
            long revision=Number("SELECT revision FROM "+table+" WHERE period=$p0 AND owner=$p1 AND key=$p2",r.Period,r.Owner,s.Key);
            if(revision!=s.ExpectedRevision)throw new BankRejected("Modulstand wurde inzwischen geändert.");
            Exec("INSERT INTO "+table+" VALUES($p0,$p1,$p2,$p3,$p4) ON CONFLICT(period,owner,key) DO UPDATE SET revision=excluded.revision,json=excluded.json",r.Period,r.Owner,s.Key,checked(revision+1),s.Json);
        }
        foreach(var p in r.Postings)ResetRobberyBypassForBalance(p.Account);
        foreach(var p in r.Postings)TitleWealthInside(p.Account,r.Reason!="BetBank-Migration");
        if(r.Owner=="LPLK_Economy"&&r.Reason=="Überweisung")
        {
            var from=r.Postings.Single(p=>p.Delta<0);var to=r.Postings.Single(p=>p.Delta>0);
            TitleAddInside(from.Account,("TransfersSent",1),("TotalTransferredSent",-from.Delta));
            TitleAddInside(to.Account,("TransfersReceived",1),("TotalTransferredReceived",to.Delta));
        }
        return new(r.Id,r.Period,false);
    }
    public (long Revision,string? Json) State(string owner,string key)
    {
        lock(gate){using var c=Command("SELECT revision,json FROM "+StateTable(owner,key)+" WHERE period=$p0 AND owner=$p1 AND key=$p2",Period,owner,key);using var r=c.ExecuteReader();return r.Read()?(r.GetInt64(0),r.GetString(1)):(0,null);}
    }
    public Receipt Transfer(string id,long period,string from,string to,long amount)
    {
        if(!BankPolicy.Steam(from)||!BankPolicy.Steam(to)||from==to||amount<=0)throw new BankRejected("Ungültige Überweisung.");
        return Book(new(id,period,"LPLK_Economy","Überweisung",[new(from,-amount),new(to,amount)]));
    }
    public Receipt Adjust(string id,long period,string actor,string account,long amount,string operation)=>AdjustCore(id,period,actor,account,amount,operation,false,-1);
    // Legacy admin commands clamp amounts; regular ledger callers retain strict limits.
    // Calculate under the same transaction as the booking and preserve the original request for retries.
    public Receipt AdjustLegacy(string id,long period,string actor,string account,long amount,string operation,long maxManualSet)=>AdjustCore(id,period,actor,account,amount,operation,true,maxManualSet);
    private Receipt AdjustCore(string id,long period,string actor,string account,long amount,string operation,bool legacy,long maxManualSet)
    {
        if(!BankPolicy.Steam(actor)||!BankPolicy.Steam(account)||amount<0||operation is not ("set" or "add" or "remove"))throw new BankRejected("Ungültige Adminbuchung.");
        return Atomic(()=>
        {
            // Stable original request, including the actor, is stored separately from its computed delta.
            string reason=$"Admin {actor}: {operation} {amount}"+(legacy?" (legacy)":"");
            var old=Text("SELECT reason FROM transactions WHERE id=$p0",TransactionId(id));
            if(old is not null)
            {
                if(old!=reason||period!=Period||Number("SELECT count(*) FROM entries WHERE tx=$p0 AND account=$p1",TransactionId(id),account)!=1)throw new BankRejected("Abweichende Admin-Wiederholung.");
                return new Receipt(id,period,true);
            }
            long balance=Balance(account);
            long value=legacy?operation switch
            {
                "set"=>Math.Min(amount,Math.Min(Limit,maxManualSet<0?Limit:maxManualSet)),
                "add"=>Math.Min(amount,Math.Max(0,Limit-balance)),
                _=>Math.Min(amount,balance)
            }:amount;
            long delta=operation=="set"?checked(value-balance):operation=="add"?value:-value;
            return BookInside(new(id,period,"LPLK_Economy",reason,[new(account,delta),new("system:admin",-delta)]),false);
        });
    }
    public CashIntent PrepareCash(string id,long period,string account,string session,int before,int after)
    {
        if(!Key(id,110)||!BankPolicy.Steam(account)||session.Length is <1 or >128||before<0||after<0||before==after||before>16000||after>16000)throw new BankRejected("Ungültiger Cash-Auftrag.");
        return Atomic(()=>
        {
            if(period!=Period)throw new BankRejected("Bankperiode inzwischen gewechselt.");
            long delta=(long)before-after;
            if(Intent(id) is {} old)
            {
                if(old.Account!=account||old.Session!=session||old.Period!=period||old.Before!=before||old.After!=after)throw new BankRejected("Abweichender Cash-Auftrag.");return old;
            }
            if(Blocked(account))throw new BankRejected("Vorherige Cash-Buchung muss geprüft werden.");
            if(!Exists(account)||delta>0&&Balance(account)>Limit-delta)throw new BankRejected("Banklimit erreicht.");
            BookInside(new(id+":prepare",period,"LPLK_Economy",delta>0?"Einzahlung vorgemerkt":"Auszahlung reserviert",delta<0?[new(account,delta),new("system:escrow",-delta)]:[]),false);
            Exec("INSERT INTO cash VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6,'pending')",id,account,session,period,before,after,delta);
            return new CashIntent(id,account,session,period,before,after,delta,"pending");
        });
    }
    public CashIntent? Intent(string id)
    {
        lock(gate){using var c=Command("SELECT id,account,session,period,before_cash,after_cash,delta,status FROM cash WHERE id=$p0",id);using var r=c.ExecuteReader();return r.Read()?new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetInt32(4),r.GetInt32(5),r.GetInt64(6),r.GetString(7)):null;}
    }
    // Completion only after exact native read-back, or an explicit audited admin decision.
    public Receipt ResolveCash(string id,bool applied,string actor)
    {
        if(string.IsNullOrWhiteSpace(actor)||actor.Length>100)throw new BankRejected("Prüfender Akteur fehlt.");
        return Atomic(()=>
        {
            var i=Intent(id)??throw new BankRejected("Cash-Auftrag fehlt.");string status=applied?"committed":"cancelled";
            if(i.Status!="pending"){if(i.Status!=status)throw new BankRejected("Cash-Auftrag wurde anders abgeschlossen.");return new Receipt(id,i.Period,true);}
            if(Text("SELECT kind FROM bet_cash WHERE id=$p0",id) is {} kind)return ResolveBetCash(i,applied,actor,Text("SELECT round FROM bet_cash WHERE id=$p0",id)!,kind);
            if(BonusCashSpec(id) is {} bonus)return ResolveBonusCash(i,bonus,applied,actor);
            if(KillCashSpec(id) is {} kill)return ResolveKillCash(i,kill,applied,actor);
            if(BountyCashSpec(id) is {} bounty)return ResolveBountyCash(i,bounty,applied,actor);
            Posting[] postings=applied?(i.Delta>0?[new(i.Account,i.Delta),new("system:cash",-i.Delta)]:[new("system:escrow",i.Delta),new("system:cash",-i.Delta)]):i.Delta<0?[new(i.Account,-i.Delta),new("system:escrow",i.Delta)]:[];
            var result=BookInside(new(id+":resolve",i.Period,"LPLK_Economy",$"Cash {status}: {actor}",postings),true);
            if(applied)TitleAddInside(i.Account,(i.Delta>0?"Deposits":"Withdrawals",1),("BankMovements",1),(i.Delta>0?"TotalDeposited":"TotalWithdrawn",Math.Abs(i.Delta)));
            Exec("UPDATE cash SET status=$p1 WHERE id=$p0",id,status);return result;
        });
    }
    public CashIntent[] Pending()
    {
        lock(gate){var ids=new List<string>();using(var c=Command("SELECT id FROM cash WHERE status='pending' ORDER BY id"))using(var r=c.ExecuteReader())while(r.Read())ids.Add(r.GetString(0));return ids.Select(id=>Intent(id)!).ToArray();}
    }
    public bool CheckReset(EconomySettings s,DateTime now)
    {
        return Atomic(()=>
        {
            if(Networked)CheckNetworkRules(s);
            string key=BankPolicy.ResetKey(s.ResetMode,now),oldMode=Text("SELECT value FROM meta WHERE key='resetMode'")??"",oldKey=Text("SELECT value FROM meta WHERE key='resetKey'")??"";
            if(oldMode!=s.ResetMode||oldKey.Length==0){Put("resetMode",s.ResetMode);Put("resetKey",key);return false;}
            if(s.ResetMode=="Never"||string.CompareOrdinal(key,oldKey)<=0)return false;
            ResetInside($"period:{Period}:{key}",Period,"Kalenderwechsel",s.ResetMode,key,s.Lotto);return true;
        });
    }
    public void Reset(string id,long expectedPeriod,string actor,EconomySettings s,DateTime now)=>Atomic(()=>
    {
        if(Networked)CheckNetworkRules(s);
        ResetInside(id,expectedPeriod,"Admin "+actor,s.ResetMode,BankPolicy.ResetKey(s.ResetMode,now),s.Lotto);return 0;
    });
    private void ResetInside(string id,long expectedPeriod,string reason,string mode,string key,LotterySettings lottery)
    {
        if(QueuedKillCount>0)throw new BankRejected("Bankreset wartet auf die Kill-Abrechnung am Rundenende.");
        if(Networked){ResetNetworkInside(id,expectedPeriod,reason,mode,key);return;}
        if(expectedPeriod!=Period)throw new BankRejected("Reset-Ansicht veraltet; nicht erneut ausgeführt.");
        if(PendingCount>0||Number("SELECT count(*) FROM bet_tickets WHERE status='accepted'")>0||BetClaims>0)throw new BankRejected("Vor dem Reset offene Cash-Buchungen, Wetteinsätze und Auszahlungen abschließen.");
        if(RobberyClaims>0)throw new BankRejected("Vor dem Reset offene Bankraub-Beuteansprüche auszahlen.");
        if(KillRewardClaims().Length>0)throw new BankRejected("Vor Bankreset offene Kill-Belohnungen auszahlen.");
        CarryBountiesInside(checked(expectedPeriod+1));
        ResetLotteryInside(id,lottery);
        Exec("UPDATE bet_rounds SET status='settled' WHERE status<>'settled'");
        // Reset can span more than 128 accounts. One SQL transaction contains individually balanced records.
        var rows=Accounts(int.MaxValue).Select(a=>new Posting(a.Id,checked(StartBank-a.Balance))).ToList();
        if(Exists("pool:lottery"))rows.Add(new("pool:lottery",-Balance("pool:lottery")));
        int chunk=0;
        foreach(var group in rows.Chunk(100))
        {
            long sum=checked((long)group.Sum(p=>(decimal)p.Delta));
            BookInside(new(id+":"+chunk++,expectedPeriod,"LPLK_Economy",reason,group.Append(new Posting("system:reset",-sum)).ToArray()),false);
        }
        BookInside(new(id,expectedPeriod,"LPLK_Economy",reason,[]),false);
        Put("period",checked(expectedPeriod+1).ToString(System.Globalization.CultureInfo.InvariantCulture));Put("resetMode",mode);Put("resetKey",key);Put("resetAt",DateTimeOffset.UtcNow.ToString("O"));
    }
    public BankAccount[] Accounts(int limit=100)
    {
        lock(gate){var result=new List<BankAccount>();using var c=Command("SELECT id,name,balance FROM accounts WHERE id GLOB '[0-9]*' ORDER BY balance DESC,id LIMIT $p0",limit);using var r=c.ExecuteReader();while(r.Read())result.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2)));return result.ToArray();}
    }
    public BankEntry[] History(string account,int limit=12)
    {
        lock(gate){var rows=new List<BankEntry>();using var c=Command("SELECT t.id,t.reason,e.delta,e.balance,t.at FROM entries e JOIN transactions t ON e.tx=t.id WHERE e.account=$p0 ORDER BY e.seq DESC LIMIT $p1",account,Math.Clamp(limit,1,50));using var r=c.ExecuteReader();while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.GetInt64(3),r.GetString(4)));return rows.ToArray();}
    }
    public void Verify()
    {
        Atomic(()=>
        {
            if(Text("PRAGMA quick_check")!="ok"||Period<1)throw new InvalidDataException("Bankprüfung fehlgeschlagen.");
            using(var c=Command("PRAGMA foreign_key_check"))using(var r=c.ExecuteReader())if(r.Read())throw new InvalidDataException("Verwaiste Buchung.");
            var totals=new Dictionary<string,decimal>(StringComparer.Ordinal);var running=new Dictionary<string,long>(StringComparer.Ordinal);var transactions=new Dictionary<string,decimal>(StringComparer.Ordinal);
            using(var c=Command("SELECT tx,account,delta,balance FROM entries ORDER BY seq"))using(var r=c.ExecuteReader())while(r.Read())
            {
                string tx=r.GetString(0),account=r.GetString(1);long delta=r.GetInt64(2),balance=r.GetInt64(3);
                if(checked(running.GetValueOrDefault(account)+delta)!=balance)throw new InvalidDataException("Buchungsfolge stimmt nicht.");
                running[account]=balance;totals[account]=totals.GetValueOrDefault(account)+delta;transactions[tx]=transactions.GetValueOrDefault(tx)+delta;
            }
            if(transactions.Any(t=>t.Value!=0))throw new InvalidDataException("Unausgeglichene Buchung.");
            using(var c=Command("SELECT id,balance FROM accounts"))using(var r=c.ExecuteReader())while(r.Read())
            {
                string id=r.GetString(0);long balance=r.GetInt64(1);
                if(!AccountKey(id)||totals.GetValueOrDefault(id)!=balance||!id.StartsWith("system:",StringComparison.Ordinal)&&balance<0)throw new InvalidDataException("Kontostand stimmt nicht mit Buchungen überein.");
            }
            VerifyBetting(); VerifyLottery(); VerifyTitles(); VerifyBonuses(); VerifyBounties(); VerifyKillRewards();
            if(Networked)VerifyNetworkLocks();
            var pending=Pending();
            if(pending.Any(i=>i.Period!=Period||i.Before<0||i.After<0||i.Before>(BonusCashSpec(i.Id)?.MaxCash??16000)||i.After>(BonusCashSpec(i.Id)?.MaxCash??16000)||i.Delta!=(long)i.Before-i.After)||pending.GroupBy(i=>i.Account).Any(g=>g.Count()>1)||Balance("system:escrow")!=pending.Where(i=>i.Delta<0).Sum(i=>-i.Delta))throw new InvalidDataException("Offene Cash-Buchungen sind inkonsistent.");
            return 0;
        });
    }
    public void ImportLegacy(string json,string expectedHash)
    {
        if(!Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new BankRejected("Migrationsdatei hat sich geändert.");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var names=new Dictionary<string,string>();var balances=new Dictionary<string,long>();var preferences=new Dictionary<string,bool>();
        foreach(var property in root.EnumerateObject().GroupBy(p=>p.Name))if(property.Count()!=1)throw new BankRejected("Doppelte Migrationsfelder.");
        if(root.TryGetProperty("Names",out var n))foreach(var p in n.EnumerateObject()){if(!BankPolicy.Steam(p.Name)||!names.TryAdd(p.Name,SafeName(p.Value.GetString()??"")))throw new BankRejected("Ungültiger Nameintrag.");}
        foreach(var p in root.GetProperty("Balances").EnumerateObject())if(!BankPolicy.Steam(p.Name)||!p.Value.TryGetInt64(out var v)||v<0||v>Limit||!balances.TryAdd(p.Name,v))throw new BankRejected("Ungültiger Kontostand; Migration vollständig abgelehnt.");
        if(root.TryGetProperty("BankMessagesDisabled",out var pref))foreach(var p in pref.EnumerateObject())if(!BankPolicy.Steam(p.Name)||p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||!preferences.TryAdd(p.Name,p.Value.GetBoolean()))throw new BankRejected("Ungültige Nachrichteneinstellung.");
        string migrationId="migration:"+expectedHash.ToLowerInvariant();
        Atomic(()=>
        {
            if(Text("SELECT value FROM meta WHERE key='migration'")==migrationId)return 0;
            if(AccountCount!=0||TransactionCount!=0||PendingCount!=0)throw new BankRejected("Migration nur in eine leere Bank.");
            foreach(var (account,balance) in balances)
            {
                Exec("INSERT INTO accounts VALUES($p0,$p1,0)",account,names.GetValueOrDefault(account,account));
                BookInside(new("import:"+account,Period,"LPLK_Economy","BetBank-Migration",[new(account,balance),new("system:migration",-balance)]),false);
            }
            foreach(var (account,name) in names.Where(p=>!balances.ContainsKey(p.Key)))
            {
                Exec("INSERT INTO accounts VALUES($p0,$p1,0)",account,name);
                BookInside(new("import:"+account,Period,"LPLK_Economy","BetBank-Namenseintrag / Startguthaben",[new(account,StartBank),new("system:migration",-StartBank)]),false);
            }
            foreach(var (account,muted) in preferences)Exec("INSERT INTO preferences VALUES($p0,$p1)",account,muted?1:0);
            if(root.TryGetProperty("LastResetMode",out var mode)&&root.TryGetProperty("LastResetKey",out var key))
            {
                var m=mode.GetString()??"Never";var k=key.GetString()??"never";
                if(m is not ("Never" or "Daily" or "Weekly" or "Monthly")||k.Length>32)throw new BankRejected("Ungültige Resetperiode.");Put("resetMode",m);Put("resetKey",k);
            }
            Put("migration",migrationId);return 0;
        });
    }
    public void Dispose(){lock(gate){if(disposed)return;FlushPersistence();disposed=true;db.Dispose();networkLease?.Dispose();}}
}


