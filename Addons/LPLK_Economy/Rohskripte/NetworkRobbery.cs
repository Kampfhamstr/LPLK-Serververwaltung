namespace LPLK_Economy;

public sealed record BankPresence(string Account,string Session,string Name,string Server,string ServerName);
public sealed record BankNotice(string Id,string Message);

public sealed partial class Ledger
{
    public const int PresenceSeconds=8;
    public RobberyOrder[] PlayerPendingRobberies(string account){lock(gate)return NetworkPendingRobberies().Where(o=>o.Sponsor==account).ToArray();}
    private void InitializeNetworkRobbery()=>Exec("""
        CREATE TABLE IF NOT EXISTS network_presence(scope TEXT NOT NULL,account TEXT NOT NULL,session TEXT NOT NULL,name TEXT NOT NULL,server_name TEXT NOT NULL,updated INTEGER NOT NULL,PRIMARY KEY(scope,account)) STRICT;
        CREATE TABLE IF NOT EXISTS network_robbery_notices(id TEXT NOT NULL,account TEXT NOT NULL,message TEXT NOT NULL,created INTEGER NOT NULL,delivered INTEGER,PRIMARY KEY(id,account)) STRICT;
        """);
    // This is an additive schema: 0.11 can still read the journal and finish orders on rollback.
    public void PublishBankPresence(IEnumerable<BankPresence> players,long now)
    {
        if(!Networked)return;
        var rows=players.ToArray();
        if(rows.Length>128||rows.Any(p=>!BankPolicy.Steam(p.Account)||p.Server!=scope||string.IsNullOrWhiteSpace(p.Session)||p.Session.Length>128)||rows.Select(p=>p.Account).Distinct().Count()!=rows.Length)throw new BankRejected("Ungültige Bankverbindungen.");
        Atomic(()=>
        {
            Exec("DELETE FROM network_presence WHERE scope=$p0 OR updated<$p1",scope,now-60);
            foreach(var p in rows)Exec("INSERT INTO network_presence VALUES($p0,$p1,$p2,$p3,$p4,$p5)",scope,p.Account,p.Session,SafeName(p.Name),SafeName(p.ServerName),now);
            Exec("DELETE FROM network_robbery_notices WHERE delivered IS NOT NULL AND created<$p0",now-30*86400L);
            return 0;
        });
    }
    public BankPresence[] BankPlayers(long now)
    {
        lock(gate)
        {
            if(!Networked)return [];
            var rows=new List<BankPresence>();
            using var c=Command("SELECT account,session,name,scope,server_name FROM network_presence WHERE updated>=$p0 AND updated<=$p1 ORDER BY name,scope",now-PresenceSeconds,now+1);using var r=c.ExecuteReader();
            while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4)));
            // During reconnects two sessions may briefly overlap; neither may be selected.
            return rows.GroupBy(p=>p.Account).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray();
        }
    }
    public BankPresence RequireBankTarget(string account,string server,string session,long now)
    {
        lock(gate)return BankPlayers(now).SingleOrDefault(p=>p.Account==account&&p.Server==server&&p.Session==session)??throw new BankRejected("Zielverbindung geändert oder nicht mehr aktuell; neu auswählen.");
    }
    public RobberyOrder OrderNetworkRobbery(string id,long period,string sponsor,BankPresence target,RobberySettings rules,long now,long expectedCost)=>Atomic(()=>
    {
        if(!Networked)throw new BankRejected("Kein Bankverbund aktiv.");
        // Replays return the existing receipt, even after the target has left.
        if(GetRobbery(id) is {} old)return OrderRobbery(id,period,sponsor,target.Account,rules,now,expectedCost);
        RequireBankTarget(target.Account,target.Server,target.Session,now);
        if(!BankPlayers(now).Any(p=>p.Account==sponsor&&p.Server==scope))throw new BankRejected("Auftraggeber nicht mehr im Bankverbund aktiv.");
        return OrderRobbery(id,period,sponsor,target.Account,rules,now,expectedCost);
    });
    private void QueueRobberyNotices(RobberyOrder order,long now)
    {
        if(!Networked)return;
        string result=order.Success?$"Erfolg · Beute {order.Stolen:N0} $":"Fehlschlag · Gebühr verbraucht";
        string message=$"[LPLK Bankraub] {order.SponsorName} → {order.TargetName}: {result}.";
        var recipients=new[]{order.Sponsor,order.Target}.Concat(order.Revealed);
        if(order.Rules.AnnouncePublicResult)recipients=recipients.Concat(BankPlayers(now).Select(p=>p.Account));
        foreach(string account in recipients.Distinct().Where(BankPolicy.Steam))
            Exec("INSERT OR IGNORE INTO network_robbery_notices VALUES($p0,$p1,$p2,$p3,NULL)",scope+":"+order.Period+":"+order.Id,account,message+(account==order.Sponsor&&order.Claim>0?$" {order.Claim:N0} $ wartet auf freien Kontoplatz.":""),now);
    }
    public BankNotice[] BankNotices(string account)
    {
        lock(gate)
        {
            if(!Networked||!BankPolicy.Steam(account))return [];
            var rows=new List<BankNotice>();using var c=Command("SELECT id,message FROM network_robbery_notices WHERE account=$p0 AND delivered IS NULL ORDER BY created,id LIMIT 10",account);using var r=c.ExecuteReader();
            while(r.Read())rows.Add(new(r.GetString(0),r.GetString(1)));return rows.ToArray();
        }
    }
    public void AckBankNotice(string account,string id,long now)=>Atomic(()=>Exec("UPDATE network_robbery_notices SET delivered=$p0 WHERE account=$p1 AND id=$p2 AND delivered IS NULL",now,account,id));
}
