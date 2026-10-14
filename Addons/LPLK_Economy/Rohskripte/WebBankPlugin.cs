using System.Text.Json;
using System.Text.Json.Serialization;
using LPLK.Common;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Economy;

public sealed record WebBankSettings
{
    public bool ExportEnabled {get;init;}=true;
    public bool AdminChangesEnabled {get;init;}=false;
}
public sealed partial class Plugin
{
    private long nextWebExport;
    private readonly Queue<string> webAccounts=[];
    private string? webError;
    private void ExportWeb()
    {
        if(!Active||!settings.WebBank.ExportEnabled)return;
        try
        {
            if(Environment.TickCount64>=nextWebExport&&webAccounts.Count==0)
            {
                nextWebExport=Environment.TickCount64+30000;
                foreach(var a in bank!.Accounts(int.MaxValue))webAccounts.Enqueue(a.Id);
                AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"web","ranking.json"),bank.WebRanking(DateTimeOffset.UtcNow));
            }
            for(int i=0;i<32&&webAccounts.TryPeek(out var account);i++)
            {
                AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"web",account+".json"),bank!.WebPlayer(account,DateTimeOffset.UtcNow));webAccounts.Dequeue();
            }
            webError=null;
        }
        catch {webError="Webexport nicht verfügbar; Kontobuchungen bleiben unabhängig davon aktiv.";}
    }
    [Command("lplk_bank_request")][CommandAlias("lplk_bank_request",registerRaw:true)]
    public void WebBankCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not null){ctx.Reply("Dieser Befehl ist nur für die authentifizierte Serververwaltung vorgesehen.");return;}
        if(ctx.Args.Length!=1||ctx.Args[0].Length!=32||!ctx.Args[0].All(char.IsAsciiHexDigit)){ctx.Reply("Ungültige Auftragsnummer.");return;}
        string id=ctx.Args[0];string root=Path.Combine(Core.PluginDataDirectory,"web-requests");
        string path=Path.Combine(root,id+".json"),result=Path.Combine(Core.PluginDataDirectory,"web-results",id+".json");
        try
        {
            if(!Active||!settings.BankEnabled||!settings.WebBank.AdminChangesEnabled||!settings.AllowEditPlayerBank||reloadError is not null)throw new BankRejected("Webbank-Änderungen derzeit gesperrt.");
            // This release deliberately cannot accept requests on a production bank.
            if(settings.Profile!="standard-test-11110004")throw new BankRejected("Webbank noch nicht für dieses Profil freigegeben.");
            if(new DirectoryInfo(root).LinkTarget is not null||new FileInfo(path).LinkTarget is not null||new FileInfo(path).Length>4096)throw new BankRejected("Ungültige Auftragsdatei.");
            var options=new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
            var request=JsonSerializer.Deserialize<PanelBankRequest>(File.ReadAllText(path),options)??throw new BankRejected("Auftrag fehlt.");
            if(request.Id!="web:"+id||request.Account is null||!BankPolicy.Steam(request.Account))throw new BankRejected("Auftragsnummer oder Konto stimmt nicht.");
            ulong target=ulong.Parse(request.Account);
            int targetRank=Math.Max(Core.Permission.PlayerHasPermission(target,settings.AdminPermission)||Core.Permission.PlayerHasPermission(target,"lplk.economy.admin")?10:0,AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(target,p)));
            if(request.ActorSteamId!=request.Account&&targetRank>0&&request.ActorRank<=targetRank)throw new BankRejected("Geschütztes Zielkonto.");
            var receipt=bank!.PanelAdjust(request,DateTimeOffset.UtcNow,settings.MaxManualBankSet);
            AtomicJson.Write(result,new{schema=1,id,state="applied",receipt});
            AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"web",request.Account+".json"),bank.WebPlayer(request.Account,DateTimeOffset.UtcNow));
            nextWebExport=0;ctx.Reply("Bankauftrag verbucht; dauerhafter Beleg vorhanden.");
        }
        catch(BankRejected e){AtomicJson.Write(result,new{schema=1,id,state="rejected",message=e.Message});ctx.Reply(e.Message);}
        catch {ctx.Reply("Auftragsstatus unklar. Dieselbe Auftragsnummer prüfen; keinen Ersatzauftrag erzeugen.");}
    }
}
