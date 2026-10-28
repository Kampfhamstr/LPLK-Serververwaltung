using System.Text.Json;
namespace LPLK_Economy;
public sealed record ModelRentalProduct(string Id,string Name,long DailyPrice,string Requirement="",long Required=0);

// Leases outlive a monthly bank reset. Debit, entitlement and retry receipt share
// the same RAM transaction and the same delayed database snapshot.
public sealed partial class Ledger
{
 private void InitializeModelRentals()=>Atomic(()=>{
  Exec("""
  CREATE TABLE IF NOT EXISTS model_leases(account TEXT NOT NULL,model TEXT NOT NULL,expires INTEGER NOT NULL,PRIMARY KEY(account,model)) STRICT;
  CREATE TABLE IF NOT EXISTS model_purchases(id TEXT PRIMARY KEY,account TEXT NOT NULL,model TEXT NOT NULL,days INTEGER NOT NULL,price INTEGER NOT NULL,period INTEGER NOT NULL,expires INTEGER NOT NULL) STRICT;
  CREATE TABLE IF NOT EXISTS model_progress(account TEXT NOT NULL,metric TEXT NOT NULL,value INTEGER NOT NULL,PRIMARY KEY(account,metric)) STRICT;
  """);
  if(Text("SELECT value FROM meta WHERE key='modelProgressImported'") is null){
   foreach(string metric in new[]{"RobberyOrders","LottoTicketsBought","BountiesSet"})
    Exec("INSERT INTO model_progress SELECT substr(key,8),$p0,sum(CAST(coalesce(json_extract(json,$p1),0) AS INTEGER)) FROM module_state WHERE owner='LPLK_Economy.Titles' AND key GLOB 'player:*' GROUP BY key",metric,"$.Values."+metric);
   Put("modelProgressImported",DateTimeOffset.UtcNow.ToString("O"));
  }return 0;
 });
 private void ModelProgressInside(string account,(string Metric,long Delta)[] changes)
 {
  if(Networked||!BankPolicy.Steam(account))return;
  foreach(var (metric,delta) in changes.Where(c=>c.Metric is "RobberyOrders" or "LottoTicketsBought" or "BountiesSet")){
   if(delta<0)throw new InvalidDataException("Negativer Modellfortschritt.");
   if(delta>0)Exec("INSERT INTO model_progress VALUES($p0,$p1,$p2) ON CONFLICT(account,metric) DO UPDATE SET value=value+excluded.value",account,metric,delta);
  }
 }
 public long ModelProgress(string account,string metric){if(Networked||!BankPolicy.Steam(account))return 0;lock(gate)return Number("SELECT value FROM model_progress WHERE account=$p0 AND metric=$p1",account,metric);}
 public long ModelLease(string account,string model)
 {
  if(!BankPolicy.Steam(account)||!Key(model,96))throw new BankRejected("Ungültige Modellauswahl.");
  lock(gate)return Number("SELECT expires FROM model_leases WHERE account=$p0 AND model=$p1",account,model);
 }
 public long RentModel(string id,long period,string account,string model,int days,long dailyPrice,long now,string requirement="",long required=0)
 {
  if(Networked||!BankPolicy.Steam(account)||!Key(model,96)||!Key(id,110)||days is <1 or >30||dailyPrice is <1 or >1_000_000_000||now<1)throw new BankRejected("Ungültige Modellmiete.");
  return Atomic(()=>{
   using(var c=Command("SELECT account,model,days,price,period,expires FROM model_purchases WHERE id=$p0",id))
   using(var r=c.ExecuteReader())if(r.Read()){
    if(r.GetString(0)!=account||r.GetString(1)!=model||r.GetInt32(2)!=days||r.GetInt64(3)!=dailyPrice||r.GetInt64(4)!=period)throw new BankRejected("Bestätigung bereits für einen anderen Kauf verwendet.");
    return r.GetInt64(5);
   }
   if(requirement is not ("" or "RobberyOrders" or "LottoTicketsBought" or "BountiesSet")||required<0||requirement!=""&&ModelProgress(account,requirement)<required)throw new BankRejected("Voraussetzung für dieses Modell noch nicht erfüllt.");
   long expires=checked(Math.Max(now,ModelLease(account,model))+days*86400L);
   if(expires-now>366*86400L)throw new BankRejected("Höchstens ein Jahr im Voraus mieten.");
   long price=checked(days*dailyPrice);
   BookInside(new("model:"+id,period,"LPLK_Models",$"Modellmiete {model} {days} Tage",[new(account,-price),new("system:models",price)]),false);
   Exec("INSERT INTO model_leases VALUES($p0,$p1,$p2) ON CONFLICT(account,model) DO UPDATE SET expires=excluded.expires",account,model,expires);
   Exec("INSERT INTO model_purchases VALUES($p0,$p1,$p2,$p3,$p4,$p5,$p6)",id,account,model,days,dailyPrice,period,expires);
   return expires;
  });
 }
}
