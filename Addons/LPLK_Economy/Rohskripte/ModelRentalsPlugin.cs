using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;
public sealed partial class Plugin
{
 private Dictionary<string,ModelRentalProduct> modelProducts=[];
 private void RegisterModelProducts(string json)
 {
  var entries=System.Text.Json.JsonSerializer.Deserialize<ModelRentalProduct[]>(json)??throw new BankRejected("Modellkatalog fehlt.");
  if(entries.Length>512||entries.Select(x=>x.Id).Distinct().Count()!=entries.Length||entries.Any(x=>!Ledger.Key(x.Id,96)||x.DailyPrice is <1 or >1_000_000_000||x.Requirement is not ("" or "RobberyOrders" or "LottoTicketsBought" or "BountiesSet")||x.Required is <0 or >1000000||x.Requirement!=""&&x.Required<1))throw new BankRejected("Ungültiger Mietkatalog.");
  modelProducts=entries.ToDictionary(x=>x.Id);
 }
 private long RentPlayerModel(IPlayer player,string model,int days,string confirmation,long period,long quotedPrice)
 {
  Require(player);
  if(!modelProducts.TryGetValue(model,out var product)||quotedPrice!=product.DailyPrice)throw new BankRejected("Preis oder Katalog geändert; Menü erneut öffnen.");
  if(!settings.ModelRentalsEnabled||bank!.Networked||!bank.InMemory)throw new BankRejected("Modellmiete derzeit nicht verfügbar.");
  return bank.RentModel(confirmation,period,Id(player),model,days,product.DailyPrice,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),product.Requirement,product.Required);
 }
}
