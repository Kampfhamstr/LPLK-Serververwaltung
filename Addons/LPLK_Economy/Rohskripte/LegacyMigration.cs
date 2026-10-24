using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LPLK_Economy;

public sealed record LegacyImportReceipt(string Source,string Sha256,int Accounts,long BalanceTotal,int Titles,int PermanentPlans,int SecurityEntries);

public sealed partial class Ledger
{
    private static readonly string[] LegacyFiles=["bank.json","lotto.json","titles.json","bank_robbery.json","bounties.json","spy_reports.json"];
    public static string LegacyBundleHash(IReadOnlyDictionary<string,string> files)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary()))));
    private static Dictionary<string,JsonElement> LegacyObject(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var value))return [];
        if(value.ValueKind!=JsonValueKind.Object)throw new BankRejected("Ungültiger Altbestand: "+name);
        return value.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value);
    }
    private static long LegacyNumber(JsonElement value,string name,long fallback=0)
    {
        if(!value.TryGetProperty(name,out var v))return fallback;
        if(!v.TryGetInt64(out var n)||n<0||n>9_000_000_000_000_000)throw new BankRejected("Ungültiger Altwert: "+name);return n;
    }
    private static long LegacyDate(string? value,bool periodAllowed=false)
    {
        if(string.IsNullOrWhiteSpace(value))return 0;
        if(periodAllowed&&value.Equals("until_bank_reset",StringComparison.OrdinalIgnoreCase))return long.MaxValue;
        if(!DateTimeOffset.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var at))throw new BankRejected("Ungültiger Altzeitpunkt.");
        return Math.Max(0,at.ToUnixTimeSeconds());
    }
    private static void LegacyEmpty(JsonElement root,string key)
    {
        if(!root.TryGetProperty(key,out var value))return;
        int count=value.ValueKind==JsonValueKind.Array?value.GetArrayLength():value.ValueKind==JsonValueKind.Object?value.EnumerateObject().Count():throw new BankRejected("Ungültiger Altbestand: "+key);
        if(count!=0)throw new BankRejected("Vor Migration offene Vorgänge abschließen: "+key);
    }
    // A stopped source is imported once. Existing shared balances are incremented,
    // never overwritten; the receipt and every imported entitlement commit together.
    public LegacyImportReceipt ImportLegacyBundle(IReadOnlyDictionary<string,string> files,string expectedHash,string mergeRule)
    {
        if(mergeRule is not ("sum" or "maximum")||!LegacyFiles.All(files.ContainsKey)||files.Keys.Except(LegacyFiles).Any()||files.Values.Any(v=>v.Length>32*1024*1024)||!LegacyBundleHash(files).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new BankRejected("Altbestand unvollständig, verändert oder ohne Zusammenführungsregel.");
        var docs=new Dictionary<string,JsonDocument>();
        try
        {
            foreach(var f in files)docs[f.Key]=JsonDocument.Parse(f.Value);
            var bank=docs["bank.json"].RootElement;var lotto=docs["lotto.json"].RootElement;var titles=docs["titles.json"].RootElement;var robbery=docs["bank_robbery.json"].RootElement;
            LegacyEmpty(lotto,"PendingTickets");LegacyEmpty(robbery,"ActiveOrders");LegacyEmpty(docs["bounties.json"].RootElement,"Active");
            var balances=LegacyObject(bank,"Balances");var names=LegacyObject(bank,"Names");var players=LegacyObject(titles,"Players");var plans=LegacyObject(lotto,"PermanentTickets");var bought=LegacyObject(lotto,"TicketsBoughtSinceBankReset");var securities=LegacyObject(robbery,"Security");
            string[] maps=["RobberSkillLevels","SpySkillLevels","TargetProtections","TargetProtectionCreatedAt","HighRollerProtectionBypassCountsByTarget"];
            var extra=maps.ToDictionary(k=>k,k=>LegacyObject(robbery,k));
            var ids=balances.Keys.Concat(names.Keys).Concat(players.Keys).Concat(plans.Keys).Concat(bought.Keys).Concat(securities.Keys).Concat(extra.Values.SelectMany(v=>v.Keys)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if(ids.Any(id=>!BankPolicy.Steam(id)))throw new BankRejected("Ungültige SteamID im Altbestand.");
            string source=Networked?scope:Profile;
            return Atomic(()=>
            {
                Exec("CREATE TABLE IF NOT EXISTS legacy_imports(source TEXT PRIMARY KEY,hash TEXT NOT NULL,rule TEXT NOT NULL,files TEXT NOT NULL,receipt TEXT NOT NULL) STRICT; CREATE TABLE IF NOT EXISTS legacy_balances(source TEXT NOT NULL REFERENCES legacy_imports(source) DEFERRABLE INITIALLY DEFERRED,account TEXT NOT NULL REFERENCES accounts(id),balance INTEGER NOT NULL,PRIMARY KEY(source,account)) STRICT;");
                if(Text("SELECT hash FROM legacy_imports WHERE source=$p0",source) is {} old)
                {
                    if(!old.Equals(expectedHash,StringComparison.OrdinalIgnoreCase)||Text("SELECT rule FROM legacy_imports WHERE source=$p0",source)!=mergeRule)throw new BankRejected("Quelle wurde bereits mit anderem Inhalt importiert.");
                    return JsonSerializer.Deserialize<LegacyImportReceipt>(Text("SELECT receipt FROM legacy_imports WHERE source=$p0",source)!)!;
                }
                if(Networked?Number("SELECT count(*) FROM transactions WHERE id GLOB $p0",scope+":*")!=0:TransactionCount!=0)throw new BankRejected("Quellprofil bereits benutzt; Altimport abgelehnt.");
                if(Number("SELECT count(*) FROM legacy_imports WHERE rule<>$p0",mergeRule)!=0)throw new BankRejected("Abweichende Zusammenführungsregel.");
                foreach(string id in ids)
                {
                    string name=names.TryGetValue(id,out var n)?SafeName(n.GetString()??id):id;
                    Exec("INSERT OR IGNORE INTO accounts VALUES($p0,$p1,0)",id,name);
                    long value=balances.TryGetValue(id,out var v)?v.TryGetInt64(out long amount)&&amount>=0&&amount<=Limit?amount:throw new BankRejected("Ungültiger alter Kontostand."):0;
                    long prior=Number("SELECT coalesce(max(balance),0) FROM legacy_balances WHERE account=$p0",id);
                    long add=mergeRule=="sum"?value:Math.Max(0,value-prior);
                    BookInside(new("legacy:"+id,Period,"LPLK_Economy","BetBank-Migration",[new(id,add),new("system:migration",-add)]),false);
                    Exec("INSERT INTO legacy_balances VALUES($p0,$p1,$p2)",source,id,value);
                }
                foreach(var p in LegacyObject(bank,"BankMessagesDisabled"))
                {
                    if(!BankPolicy.Steam(p.Key)||p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new BankRejected("Ungültige alte Nachrichteneinstellung.");
                    // Preserve an opt-out if any source had optional messages disabled.
                    Exec("INSERT INTO preferences VALUES($p0,$p1) ON CONFLICT(id) DO UPDATE SET muted=max(muted,excluded.muted)",p.Key,p.Value.GetBoolean()?1:0);
                }
                long superpot=LegacyNumber(lotto,"Superpot");
                BookInside(new("legacy:superpot",Period,"lottery","BetBank-Superpot-Migration",[new("system:superpot",superpot),new("system:migration",-superpot)]),false);
                foreach(string id in plans.Keys.Concat(bought.Keys).Distinct())
                {
                    long count=plans.TryGetValue(id,out var p)?p.GetInt64():0,total=bought.TryGetValue(id,out var q)?q.GetInt64():0;
                    if(count<0||count>1000||total<0||total>9_000_000_000_000_000)throw new BankRejected("Ungültige alte Dauerlose.");
                    Exec("INSERT INTO lot_subscriptions VALUES($p0,$p1,$p2,1)",id,count,total);
                }
                foreach(var p in players)
                {
                    var values=new Dictionary<string,long>();var reached=new Dictionary<string,long>();
                    foreach(var metric in p.Value.EnumerateObject().Where(v=>v.Name!="SteamId"&&v.Value.ValueKind==JsonValueKind.Number))
                    {
                        long value=metric.Value.GetInt64();if(value<0)throw new BankRejected("Negativer Titelzähler.");values[metric.Name]=value;
                        string dateName=metric.Name=="RobberyVictimCount"?"RobberyVictimReachedAt":metric.Name+"ReachedAt";
                        long at=p.Value.TryGetProperty(dateName,out var date)?LegacyDate(date.GetString()):0;
                        reached[metric.Name]=players.Values.Count(other=>other.TryGetProperty(dateName,out var d)&&LegacyDate(d.GetString()) is >0 and var otherAt&&(at==0||otherAt<at))+1;
                    }
                    values["CurrentBank"]=Balance(p.Key);values["HighestBank"]=Math.Max(values.GetValueOrDefault("HighestBank"),Balance(p.Key));
                    string active=p.Value.TryGetProperty("ActiveTitleId",out var selected)?selected.GetString()??"":"";
                    if(active!=""&&!TitleCatalog.All.Any(t=>t.Id==active))throw new BankRejected("Unbekannter aktiver Alttitel.");
                    TitleStateInside("player:"+p.Key,new TitleProgress{Values=values,Reached=reached,Active=active});
                    int discount=checked((int)values.GetValueOrDefault("UpgradeDiscountPercent"));if(discount is <0 or >100)throw new BankRejected("Ungültiger alter Rabatt.");BonusStateInside("discount:"+p.Key,discount);
                }
                foreach(var p in LegacyObject(titles,"FirstMilestoneOwners"))
                {
                    var title=TitleCatalog.All.SingleOrDefault(t=>t.Metric=="Pioneer"&&t.Required.ToString(System.Globalization.CultureInfo.InvariantCulture)==p.Key)??throw new BankRejected("Unbekannter alter Pioniermeilenstein.");
                    string owner=p.Value.GetString()??"";if(!BankPolicy.Steam(owner))throw new BankRejected("Ungültiger alter Pionier.");TitleStateInside("pioneer:"+title.Id,owner);
                }
                foreach(string id in securities.Keys.Concat(extra.Values.SelectMany(v=>v.Keys)).Distinct())
                {
                    JsonElement sec=securities.GetValueOrDefault(id);bool present=sec.ValueKind==JsonValueKind.Object;
                    long Numeric(string map)=>extra[map].TryGetValue(id,out var val)?val.GetInt64():0;
                    long Date(string map)=>extra[map].TryGetValue(id,out var val)?LegacyDate(val.GetString()):0;
                    var protections=present?LegacyObject(sec,"Protections").ToDictionary(p=>p.Key,p=>LegacyDate(p.Value.GetString(),true)):[];
                    int vault=present?checked((int)LegacyNumber(sec,"VaultLevel")):0,robber=checked((int)Numeric("RobberSkillLevels")),spy=checked((int)Numeric("SpySkillLevels")),bypasses=checked((int)Numeric("HighRollerProtectionBypassCountsByTarget"));
                    if(vault is <0 or >3||robber is <0 or >100||spy is <0 or >100||bypasses<0||protections.Keys.Except(["alarm","dogs","tower","guard"]).Any())throw new BankRejected("Ungültige alte Schutzmaßnahme.");
                    var state=new RobberySecurity{Vault=vault,VaultUntil=present&&sec.TryGetProperty("VaultExpiresAt",out var expiry)?LegacyDate(expiry.GetString(),true):0,Protections=protections,RobberSkill=robber,SpySkill=spy,ProtectedAt=Date("TargetProtectionCreatedAt"),ProtectedUntil=Date("TargetProtections"),Bypasses=bypasses};
                    // Protect the common balance equally on every standard server.
                    // Preserve the stronger still valid entitlement from each source.
                    var oldSecurity=RobState<RobberySecurity>("security:"+id);
                    var vaultOwner=new[]{state,oldSecurity}.Where(s=>s.VaultUntil>DateTimeOffset.UtcNow.ToUnixTimeSeconds()).OrderByDescending(s=>s.Vault).ThenByDescending(s=>s.VaultUntil).FirstOrDefault()??new();
                    state=state with{Vault=vaultOwner.Vault,VaultUntil=vaultOwner.VaultUntil,RobberSkill=Math.Max(state.RobberSkill,oldSecurity.RobberSkill),SpySkill=Math.Max(state.SpySkill,oldSecurity.SpySkill),ProtectedAt=Math.Max(state.ProtectedAt,oldSecurity.ProtectedAt),ProtectedUntil=Math.Max(state.ProtectedUntil,oldSecurity.ProtectedUntil),Bypasses=Math.Max(state.Bypasses,oldSecurity.Bypasses),Protections=state.Protections.Concat(oldSecurity.Protections).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Max(p=>p.Value))};
                    Exec("INSERT INTO "+StateTable(RobOwner,"security:"+id)+" VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,RobOwner,"security:"+id,JsonSerializer.Serialize(state));
                }
                foreach(var p in LegacyObject(robbery,"PairCooldowns"))
                {
                    var pair=p.Key.Split('_',':');if(pair.Length!=2||pair.Any(id=>!BankPolicy.Steam(id)))throw new BankRejected("Ungültige alte Raubsperre.");
                    string key="pair:"+pair[0]+":"+pair[1];
                    Exec("INSERT INTO "+StateTable(RobOwner,key)+" VALUES($p0,$p1,$p2,1,$p3) ON CONFLICT(period,owner,key) DO UPDATE SET revision=revision+1,json=excluded.json",Period,RobOwner,key,JsonSerializer.Serialize(Math.Max(RobState<long>(key),LegacyDate(p.Value.GetString()))));
                }
                var receipt=new LegacyImportReceipt(source,expectedHash,balances.Count,checked(balances.Values.Sum(v=>v.GetInt64())),players.Count,plans.Count,securities.Count);
                Exec("INSERT INTO legacy_imports VALUES($p0,$p1,$p2,$p3,$p4)",source,expectedHash,mergeRule,JsonSerializer.Serialize(files),JsonSerializer.Serialize(receipt));
                Verify();return receipt;
            });
        }
        finally{foreach(var doc in docs.Values)doc.Dispose();}
    }
}
