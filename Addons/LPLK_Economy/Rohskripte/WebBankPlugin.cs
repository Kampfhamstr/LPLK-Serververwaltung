using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
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
    private readonly ExportWriter exportWriter=new();
    private DateTimeOffset? lastWebExportAt;
    private string? webError;
    private Task<(DateTimeOffset At,long Milliseconds)>? webExportTask;
    private bool webExportRequested;
    private long webExportMs;
    private void ExportWeb()
    {
        if(!Active||!settings.WebBank.ExportEnabled)return;
        webExportRequested=true;CompleteWebExport();
    }
    private void CompleteWebExport(bool wait=false)
    {
        if(webExportTask is not null&&(wait||webExportTask.IsCompleted))
        {
            try{var result=webExportTask.GetAwaiter().GetResult();lastWebExportAt=result.At;webExportMs=result.Milliseconds;webError=exportWriter.Error;}
            catch{webError="Webexport nicht verfügbar; Kontobuchungen bleiben unabhängig davon aktiv.";}
            webExportTask=null;
        }
        if(webExportTask is not null||!webExportRequested)return;
        webExportRequested=false;
        try
        {
            var read=bank!.CreateWebSnapshotReader();var persisted=bank.WaitForRoundPersistence();string directory=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"web");
            // Separate read-only connection: no shared connection/lock or engine
            // access on the worker. Both database reads and serialization stay
            // outside the last-kill/round-end game callback.
            webExportTask=Task.Run(async()=>{
                await persisted.ConfigureAwait(false);
                var timer=System.Diagnostics.Stopwatch.StartNew();var at=DateTimeOffset.UtcNow;
                foreach(var item in read(at))exportWriter.Queue(Path.Combine(directory,item.Key),item.Value);
                exportWriter.Drain();if(exportWriter.Error is not null)throw new IOException("Round export incomplete");
                return (at,timer.ElapsedMilliseconds);
            });
        }
        catch {webError="Webexport nicht verfügbar; Kontobuchungen bleiben unabhängig davon aktiv.";}
        if(wait&&webExportTask is not null)CompleteWebExport(true);
    }
    [Command("lplk_bank_request")][CommandAlias("lplk_bank_request",registerRaw:true)]
    public void WebBankCommand(ICommandContext ctx)
    {
        if(ctx.Sender is not null){ctx.ReplyLplk("Dieser Befehl ist nur für die authentifizierte Serververwaltung vorgesehen.");return;}
        if(ctx.Args.Length!=1||ctx.Args[0].Length!=32||!ctx.Args[0].All(char.IsAsciiHexDigit)){ctx.ReplyLplk("Ungültige Auftragsnummer.");return;}
        string id=ctx.Args[0];string root=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"web-requests");
        string path=Path.Combine(root,id+".json"),result=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"web-results",id+".json");
        try
        {
            if(!Active||!settings.BankEnabled||!settings.WebBank.AdminChangesEnabled||!settings.AllowEditPlayerBank||reloadError is not null)throw new BankRejected("Webbank-Änderungen derzeit gesperrt.");
            // This release deliberately cannot accept requests on a production bank.
            if(settings.Profile!="standard-test-89450e53")throw new BankRejected("Webbank noch nicht für dieses Profil freigegeben.");
            if(new DirectoryInfo(root).LinkTarget is not null||new FileInfo(path).LinkTarget is not null||new FileInfo(path).Length>4096)throw new BankRejected("Ungültige Auftragsdatei.");
            var options=new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
            var request=JsonSerializer.Deserialize<PanelBankRequest>(File.ReadAllText(path),options)??throw new BankRejected("Auftrag fehlt.");
            if(request.Id!="web:"+id||request.Account is null||!BankPolicy.Steam(request.Account))throw new BankRejected("Auftragsnummer oder Konto stimmt nicht.");
            ulong target=ulong.Parse(request.Account);
            int targetRank=Math.Max(Core.Permission.PlayerHasPermission(target,settings.AdminPermission)||Core.Permission.PlayerHasPermission(target,"lplk.economy.admin")?10:0,AdminActionPolicy.Rank(p=>Core.Permission.PlayerHasPermission(target,p)));
            if(request.ActorSteamId!=request.Account&&targetRank>0&&request.ActorRank<=targetRank)throw new BankRejected("Geschütztes Zielkonto.");
            var receipt=bank!.PanelAdjust(request,DateTimeOffset.UtcNow,settings.MaxManualBankSet);
            AtomicJson.Write(result,new{schema=1,id,state="applied",receipt});
            ctx.ReplyLplk("Bankauftrag verbucht; dauerhafter Beleg vorhanden.");
        }
        catch(BankRejected e){AtomicJson.Write(result,new{schema=1,id,state="rejected",message=e.Message});ctx.ReplyLplk(e.Message);}
        catch {ctx.ReplyLplk("Auftragsstatus unklar. Dieselbe Auftragsnummer prüfen; keinen Ersatzauftrag erzeugen.");}
    }
}
