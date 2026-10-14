using System.Globalization;
using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;

public sealed partial class Plugin
{
    // BetBank 1.4.44 accepted compact amounts and these three "all" spellings.
    // Normalize only at the legacy command boundary; the ledger keeps strict integer validation.
    public static string LegacyAmount(string value)
    {
        string text=value.Trim().Replace(" ","");
        if(new[]{"all","alles","max"}.Contains(text,StringComparer.OrdinalIgnoreCase))return "all";
        decimal multiplier=1;
        if(text.EndsWith("k",StringComparison.OrdinalIgnoreCase)){multiplier=1000;text=text[..^1];}
        else if(text.EndsWith("m",StringComparison.OrdinalIgnoreCase)){multiplier=1_000_000;text=text[..^1];}
        else if(text.EndsWith("b",StringComparison.OrdinalIgnoreCase)){multiplier=1_000_000_000;text=text[..^1];}
        if(!decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var number)&&!decimal.TryParse(text,NumberStyles.Number,CultureInfo.GetCultureInfo("de-DE"),out number))throw new BankRejected("Ungültiger Betrag.");
        if(number<0||number>long.MaxValue/multiplier)throw new BankRejected("Betrag außerhalb des gültigen Bereichs.");
        return ((long)decimal.Floor(number*multiplier)).ToString(CultureInfo.InvariantCulture);
    }
    private string LegacyTarget(string query)
    {
        query=query.Trim().Trim('"');if(BankPolicy.Steam(query))return query;
        var candidates=Core.PlayerManager.GetAllValidPlayers().Where(Human).Select(p=>(Id:Id(p),Name:p.Name))
            .Concat(bank!.Accounts(int.MaxValue).Select(p=>(p.Id,p.Name))).GroupBy(x=>x.Id).Select(g=>g.First()).ToArray();
        var exact=candidates.Where(p=>p.Name.Equals(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches=exact.Length>0?exact:candidates.Where(p=>p.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(query.Length==0||matches.Length!=1)throw new BankRejected("Spielername nicht eindeutig gefunden; SteamID64 verwenden.");return matches[0].Id;
    }
    public static long LegacyAdminAmount(string value)
    {
        string amount=LegacyAmount(value);
        if(amount=="all")throw new BankRejected("Für eine Adminbuchung einen konkreten Betrag angeben; Konto zurücksetzen mit !bankresetplayer.");
        return BankPolicy.Amount(amount,0,0,long.MaxValue);
    }
}
