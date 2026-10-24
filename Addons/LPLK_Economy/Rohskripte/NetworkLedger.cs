using System.Text.Json;
using System.Text.RegularExpressions;

namespace LPLK_Economy;

// One physical SQLite database on the game host. Accounts and the balanced journal
// are global; rounds, claims and recoveries belong to the originating server.
// This must never be placed on a NAS or copied into individual server volumes.
public sealed partial class Ledger
{
    private string scope="";
    private FileStream? networkLease;
    public bool Networked=>scope.Length>0;
    public string ServerScope=>scope;
    public string? NetworkIdentity {get {lock(gate)return Networked?Text("SELECT value FROM meta WHERE key='networkIdentity'"):null;}}
    private void AcquireScopeLease(string path)
    {
        string lease=path+"."+scope+".lock";
        if(new FileInfo(lease).LinkTarget is not null)throw new InvalidDataException("Verlinkte Bank-Sperrdatei.");
        networkLease=new FileStream(lease,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    private static readonly Regex ScopedTables=new(@"\b(module_state|cash|cash_pending|bet_rounds|bet_tickets|bet_cash|lot_rounds|lot_tickets|lot_tickets_round|lot_subscriptions|lot_refunds)\b",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    private static void ValidateScope(string value)
    {
        if(value.Length!=0&&(value.Length!=8||value.Any(c=>!char.IsAsciiHexDigit(c)||char.IsUpper(c))))throw new InvalidDataException("Ungültige Bank-Serverkennung.");
    }
    private string ScopeSql(string sql)=>Networked?ScopedTables.Replace(sql,m=>"s_"+scope+"_"+m.Value):sql;
    private string StateTable(string owner,string key)=>Networked&&owner==RobOwner&&(key.StartsWith("security:",StringComparison.Ordinal)||key.StartsWith("pair:",StringComparison.Ordinal))?"network_protection":"module_state";
    private string ScopeAccount(string value)=>!Networked?value:value.StartsWith("system:",StringComparison.Ordinal)?"system:"+scope+":"+value[7..]:value=="pool:lottery"?"system:"+scope+":lottery":value;
    private string TransactionId(string id)=>Networked?scope+":"+id:id;
    private static string NetworkRules(EconomySettings s)=>JsonSerializer.Serialize(new{s.StartBank,Limit=s.Limit,s.ResetMode,s.UseUtcForResetKey});
    private void CheckNetworkRules(EconomySettings settings)
    {
        if(Text("SELECT value FROM meta WHERE key='networkRules'")!=NetworkRules(settings))throw new BankRejected("Gemeinsame Bankregeln stimmen nicht überein; Änderung zentral durchführen.");
    }
    private void InitializeNetwork(EconomySettings settings,bool existed)=>Atomic(()=>
    {
        // Check again while holding the write transaction: two first connections
        // may have observed a missing file before either acquired the SQLite lock.
        long version=Number("PRAGMA user_version");
        if(version==0&&!existed)
        {
            Exec("""
                CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT NOT NULL) STRICT;
                CREATE TABLE accounts(id TEXT PRIMARY KEY,name TEXT NOT NULL,balance INTEGER NOT NULL) STRICT;
                CREATE TABLE preferences(id TEXT PRIMARY KEY,muted INTEGER NOT NULL CHECK(muted IN (0,1))) STRICT;
                CREATE TABLE transactions(id TEXT PRIMARY KEY,period INTEGER NOT NULL,owner TEXT NOT NULL,reason TEXT NOT NULL,fingerprint TEXT NOT NULL,at TEXT NOT NULL) STRICT;
                CREATE TABLE entries(seq INTEGER PRIMARY KEY,tx TEXT NOT NULL REFERENCES transactions(id),account TEXT NOT NULL REFERENCES accounts(id),delta INTEGER NOT NULL,balance INTEGER NOT NULL) STRICT;
                CREATE INDEX entries_account ON entries(account,seq DESC);
                CREATE TABLE network_scopes(id TEXT PRIMARY KEY,settings TEXT NOT NULL) STRICT;
                CREATE TABLE network_cash_locks(account TEXT PRIMARY KEY REFERENCES accounts(id),scope TEXT NOT NULL,id TEXT NOT NULL,UNIQUE(scope,id)) STRICT;
                CREATE TABLE network_protection(period INTEGER NOT NULL,owner TEXT NOT NULL,key TEXT NOT NULL,revision INTEGER NOT NULL,json TEXT NOT NULL,PRIMARY KEY(period,owner,key)) STRICT;
                """);
            Put("profile",Profile);Put("networkIdentity",Guid.NewGuid().ToString("N"));Put("networkRules",NetworkRules(settings));Put("period","1");Put("resetMode","Never");Put("resetKey","never");Put("resetAt",DateTimeOffset.UtcNow.ToString("O"));
        }
        else if(version!=4)throw new InvalidDataException("Gemeinsame Bank benötigt Schema 4; lokale Daten bleiben erhalten.");
        if(Text("SELECT value FROM meta WHERE key='profile'")!=Profile)throw new InvalidDataException("Gemeinsame Bank gehört zu anderem Profil.");
        CheckNetworkRules(settings);
        if(Number("SELECT count(*) FROM network_scopes WHERE id=$p0",scope)==0)
        {
            Exec("""
                CREATE TABLE module_state(period INTEGER NOT NULL,owner TEXT NOT NULL,key TEXT NOT NULL,revision INTEGER NOT NULL,json TEXT NOT NULL,PRIMARY KEY(period,owner,key)) STRICT;
                CREATE TABLE cash(id TEXT PRIMARY KEY,account TEXT NOT NULL REFERENCES accounts(id),session TEXT NOT NULL,period INTEGER NOT NULL,before_cash INTEGER NOT NULL,after_cash INTEGER NOT NULL,delta INTEGER NOT NULL,status TEXT NOT NULL CHECK(status IN ('pending','committed','cancelled'))) STRICT;
                CREATE INDEX cash_pending ON cash(account,status);
                """);
            UpgradeBettingSchema();UpgradeLotterySchema();
            // A pending native cash change locks the player on every server.
            // Locks commit/roll back together with the intent and bank reservation.
            Exec($"""
                CREATE TRIGGER s_{scope}_cash_insert AFTER INSERT ON cash WHEN NEW.status='pending' BEGIN
                  INSERT INTO network_cash_locks VALUES(NEW.account,'{scope}',NEW.id);
                END;
                CREATE TRIGGER s_{scope}_cash_resolve AFTER UPDATE OF status ON cash WHEN OLD.status='pending' AND NEW.status<>'pending' BEGIN
                  DELETE FROM network_cash_locks WHERE account=OLD.account AND scope='{scope}' AND id=OLD.id;
                END;
                CREATE TRIGGER s_{scope}_cash_reopen BEFORE UPDATE OF status ON cash WHEN OLD.status<>'pending' AND NEW.status='pending' BEGIN
                  SELECT RAISE(ABORT,'Resolved cash cannot be reopened');
                END;
                """);
        }
        Exec("INSERT INTO network_scopes VALUES($p0,$p1) ON CONFLICT(id) DO UPDATE SET settings=excluded.settings",scope,JsonSerializer.Serialize(settings));
        Exec("PRAGMA user_version=4");return 0;
    });

    private (string Id,EconomySettings Settings)[] NetworkScopes()
    {
        var rows=new List<(string,EconomySettings)>();
        using var c=Command("SELECT id,settings FROM network_scopes ORDER BY id");using var r=c.ExecuteReader();
        while(r.Read())rows.Add((r.GetString(0),JsonSerializer.Deserialize<EconomySettings>(r.GetString(1))??throw new InvalidDataException("Bankprofil fehlt.")));
        return rows.ToArray();
    }
    private void VerifyNetworkLocks()
    {
        var expected=new Dictionary<string,(string Scope,string Id)>();
        foreach(var s in NetworkScopes())
        {
            ValidateScope(s.Id);
            using var c=Command("SELECT account,id FROM s_"+s.Id+"_cash WHERE status='pending'");using var r=c.ExecuteReader();
            while(r.Read())if(!expected.TryAdd(r.GetString(0),(s.Id,r.GetString(1))))throw new InvalidDataException("Mehrere offene Cash-Buchungen im Verbund.");
        }
        using var command=Command("SELECT account,scope,id FROM network_cash_locks");using var rows=command.ExecuteReader();
        while(rows.Read())if(!expected.Remove(rows.GetString(0),out var found)||found!=(rows.GetString(1),rows.GetString(2)))throw new InvalidDataException("Gemeinsame Cash-Sperre stimmt nicht.");
        if(expected.Count!=0)throw new InvalidDataException("Gemeinsame Cash-Sperre fehlt.");
    }
    private void InScope(string server,EconomySettings settings,Action work)
    {
        ValidateScope(server);string oldScope=scope;var oldRules=titleRules;var oldRobbery=robberyRules;
        try{scope=server;titleRules=settings;robberyRules=settings.BankRobbery;work();}
        finally{scope=oldScope;titleRules=oldRules;robberyRules=oldRobbery;}
    }
    private RobberyOrder[] NetworkPendingRobberies()
    {
        if(!Networked)return RobberyOrders(true,10000);
        var all=new List<RobberyOrder>();
        foreach(var s in NetworkScopes())InScope(s.Id,s.Settings,()=>all.AddRange(RobberyOrders(true,10000)));
        return all.ToArray();
    }
    private void ResetNetworkInside(string id,long expectedPeriod,string reason,string mode,string key)
    {
        if(expectedPeriod!=Period)throw new BankRejected("Reset-Ansicht veraltet; nicht erneut ausgeführt.");
        var scopes=NetworkScopes();
        if(Number("SELECT count(*) FROM network_cash_locks")>0)throw new BankRejected("Vor dem gemeinsamen Reset offene Cash-Buchungen auf allen Servern prüfen.");
        foreach(var s in scopes)InScope(s.Id,s.Settings,()=>
        {
            if(Number("SELECT count(*) FROM bet_tickets WHERE status='accepted'")>0||BetClaims>0||RobberyClaims>0||KillRewardClaims().Length>0)throw new BankRejected("Offene Ansprüche auf Server "+scope+" verhindern den gemeinsamen Reset.");
        });
        foreach(var s in scopes)InScope(s.Id,s.Settings,()=>
        {
            CarryBountiesInside(checked(expectedPeriod+1));ResetLotteryInside(id,s.Settings.Lotto);
            Exec("UPDATE bet_rounds SET status='settled' WHERE status<>'settled'");
            if(Exists("pool:lottery")&&Balance("pool:lottery")!=0)BookInside(new(id+":pool",expectedPeriod,"LPLK_Economy",reason,[new("pool:lottery",-Balance("pool:lottery")),new("system:reset",Balance("pool:lottery"))]),false);
        });
        int chunk=0;
        foreach(var group in Accounts(int.MaxValue).Select(a=>new Posting(a.Id,checked(StartBank-a.Balance))).Chunk(100))
        {
            long sum=checked((long)group.Sum(p=>(decimal)p.Delta));
            BookInside(new(id+":"+chunk++,expectedPeriod,"LPLK_Economy",reason,group.Append(new Posting("system:reset",-sum)).ToArray()),false);
        }
        BookInside(new(id,expectedPeriod,"LPLK_Economy",reason,[]),false);
        Put("period",checked(expectedPeriod+1).ToString(System.Globalization.CultureInfo.InvariantCulture));Put("resetMode",mode);Put("resetKey",key);Put("resetAt",DateTimeOffset.UtcNow.ToString("O"));
    }
}
