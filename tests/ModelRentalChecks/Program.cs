using LPLK_Economy;
using LPLK_Models;
using Microsoft.Data.Sqlite;
const string a="76561198000000001",b="76561198000000002";
int checks=0;
void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
void Reject(Action action,string why){try{action();}catch(BankRejected){checks++;return;}throw new Exception("Expected rejection: "+why);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"ModelRentalChecks","fixture");Directory.CreateDirectory(root);
string path=Path.Combine(root,"current-"+Guid.NewGuid().ToString("N")+".sqlite3");
var settings=new EconomySettings{Profile="model-test",ResetMode="Monthly"};bool fault=false;
long now=1_798_000_000,until;
using(var bank=new Ledger(path,settings,p=>{if(fault&&p=="after-posting")throw new IOException("Injected failure");},roundMemory:true)){
 bank.Open(a,"A");bank.Open(b,"B");bank.Adjust("seed",bank.Period,a,a,2_000_000,"set");
 bank.FlushPersistence();
 until=bank.RentModel("one",bank.Period,a,"skeleton",1,100000,now);
 Check(until==now+86400&&bank.Balance(a)==1_900_000,"one day bank debit");
 Check(bank.RentModel("one",bank.Period,a,"skeleton",1,100000,now+100)==until&&bank.Balance(a)==1_900_000,"duplicate no second debit or extension");
 Reject(()=>bank.RentModel("one",bank.Period,b,"skeleton",1,100000,now),"receipt cannot move owner");
 Reject(()=>bank.RentModel("one",bank.Period,a,"skeleton",2,100000,now),"receipt cannot change duration");
 Reject(()=>bank.RentModel("poor",bank.Period,b,"skeleton",1,100000,now),"insufficient balance");
 Check(bank.ModelLease(b,"skeleton")==0,"no entitlement on failed payment");
 Reject(()=>bank.RentModel("zero",bank.Period,a,"skeleton",0,100000,now),"duration zero");
 Reject(()=>bank.RentModel("long",bank.Period,a,"skeleton",31,100000,now),"duration too long");
 Reject(()=>bank.RentModel("period",bank.Period+1,a,"skeleton",1,100000,now),"stale bank period");
 fault=true;try{bank.RentModel("failure",bank.Period,a,"ghost",1,100000,now);throw new Exception("Fault not injected");}catch(IOException){}fault=false;
 Check(bank.Balance(a)==1_900_000&&bank.ModelLease(a,"ghost")==0,"rollback includes debit and lease");
 using(var disk=new SqliteConnection("Data Source="+path)){disk.Open();using var c=disk.CreateCommand();c.CommandText="SELECT COUNT(*) FROM model_leases";Check((long)c.ExecuteScalar()! ==0,"live purchases only in RAM before boundary");}
 until=bank.RentModel("extend",bank.Period,a,"skeleton",3,100000,now+10);
 Check(until==now+4*86400&&bank.Balance(a)==1_600_000,"extension starts at prior expiration");
 Reject(()=>bank.RentModel("locked",bank.Period,a,"famous",1,250000,now,"LottoTicketsBought",100),"locked model cannot be rented");
 Check(bank.ModelLease(a,"famous")==0&&bank.Balance(a)==1600000,"locked model does not debit");
 var lotto=new LotterySettings{MaxSingleTicketsPerPlayerPerDraw=100};
 bank.StartLotteryRound("unlock",lotto);bank.BuyLottery("hundred","unlock",a,100);
 bank.BuyLottery("hundred","unlock",a,100);
 Check(bank.ModelProgress(a,"LottoTicketsBought")==100,"100 actually purchased tickets, duplicate counts once");
 bank.CloseLotteryRound("unlock",false);bank.ResumeLottery();
 bank.RentModel("unlocked",bank.Period,a,"famous",1,250000,now,"LottoTicketsBought",100);
 Check(bank.ModelLease(a,"famous")==now+86400,"earned unlock permits rental");
 var bounty=new BountySettings{AllowStacking=true};
 for(int i=0;i<3;i++)bank.PlaceBounty("goal-"+i,bank.Period,a,b,1000,"Bank",bounty);
 bank.PlaceBounty("goal-2",bank.Period,a,b,1000,"Bank",bounty);
 Check(bank.ModelProgress(a,"BountiesSet")==3,"three funded bounties, duplicate counts once");
 bank.Adjust("target",bank.Period,a,b,1000000,"set");
 var quote=bank.QuoteRobbery(a,b,settings.BankRobbery,now);
 bank.OrderRobbery("goal-rob",bank.Period,a,b,settings.BankRobbery,now,quote.Cost);
 bank.OrderRobbery("goal-rob",bank.Period,a,b,settings.BankRobbery,now,quote.Cost);
 Check(bank.ModelProgress(a,"RobberyOrders")==1,"real robbery order unlocks once");
 bank.FlushPersistence();
 bank.CheckReset(settings,new DateTime(2026,10,27));bank.CheckReset(settings,new DateTime(2026,11,1));
 Check(bank.ModelProgress(a,"LottoTicketsBought")==100&&bank.ModelProgress(a,"BountiesSet")==3&&bank.ModelProgress(a,"RobberyOrders")==1,"earned progress survives bank reset");
 Check(bank.ModelLease(a,"skeleton")==until,"lease survives bank reset");bank.FlushPersistence();
}
using(var restored=new Ledger(path,settings,roundMemory:true))Check(restored.ModelLease(a,"skeleton")==until,"lease survives restart with bank snapshot");
var entries=Enumerable.Range(0,30).Select(i=>{string p=$"characters/models/nozb1/{(i<12?"skeleton":"cat")}_{i}/player.vmdl";return new ModelEntry(ModelPolicy.Id(p),"Model "+i,p,"3163629484","Test");}).ToArray();
var s=new ModelSettings{Models=entries};ModelSettings.Validate(s);
var free=ModelPolicy.Free(entries,"2026-10-27",10,true);
Check(free.Count==10,"ten free models");Check(entries.Where(m=>free.Contains(m.Id)).All(ModelPolicy.HalloweenModel),"Halloween pool");
Check(free.SetEquals(ModelPolicy.Free(entries.Reverse(),"2026-10-27",10,true)),"rotation independent of order/restart");
Check(!free.SetEquals(ModelPolicy.Free(entries,"2026-10-28",10,true)),"daily rotation");
Check(ModelPolicy.Id(entries[0].Path)==ModelPolicy.Id(entries[0].Path.ToUpperInvariant()),"duplicate package model stable identity");
try{ModelSettings.Validate(s with{Models=[entries[0],entries[0]]});throw new Exception("duplicate accepted");}catch(InvalidDataException){checks++;}
 foreach(string vehicle in new[]{"t34","lada2107","bombardino"}){
  string p=$"characters/models/nozb1/{vehicle}_player_model/{vehicle}_player_model.vmdl";
  try{ModelSettings.Validate(s with{Models=[new(ModelPolicy.Id(p),vehicle,p,"3163629484","Test")]});throw new Exception("vehicle accepted");}catch(InvalidDataException){checks++;}
 }
 var gated=entries.Select((m,i)=>i<12?m with{DailyPrice=500000,Requirement="BountiesSet",Required=3}:m).ToArray();
 Check(ModelPolicy.Free(gated,"2026-10-27",10,true).All(id=>gated.Single(m=>m.Id==id).Requirement==""),"free rotation never bypasses unlocks");
 var catalog=System.Text.Json.JsonSerializer.Deserialize<ModelSettings>(System.IO.File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"Addons/LPLK_Models/test-settings.json")))!;
 ModelSettings.Validate(catalog);Check(ModelPolicy.Free(catalog.Models,"2026-10-27",10,true).Count==10,"actual installed catalogue validates and has ten free models");
Console.WriteLine($"PASS {checks} model rental, RAM, restart and Halloween checks");
