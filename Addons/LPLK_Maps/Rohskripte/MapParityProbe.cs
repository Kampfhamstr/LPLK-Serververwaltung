using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Shared.Commands;

namespace LPLK_Maps;
public sealed partial class Plugin
{
    // One-shot authorization is written only by the fixed SSH test adapter on
    // server 89450e53. This never runs automatically or from a player command.
    private void ParityProbe(ICommandContext ctx)
    {
        if(ctx.IsSentByPlayer||ctx.Sender is not null||AnyHuman()||voteEnds>0||pending is not null||nextMap is not null||probeId is not null||extensions!=0||originalLimits is not null){ctx.ReplyLplk("[LPLK] Isolierter, leerer Testzustand erforderlich.");return;}
        string path=Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"parity-test-authorization.json");
        if(!File.Exists(path)||new FileInfo(path).Length>2048){ctx.ReplyLplk("[LPLK] Einmalige Testfreigabe fehlt.");return;}
        using var authorization=JsonDocument.Parse(File.ReadAllText(path));
        if(authorization.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b"||authorization.RootElement.GetProperty("expires").GetDateTimeOffset()<DateTimeOffset.UtcNow||authorization.RootElement.GetProperty("expires").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddMinutes(5)){ctx.ReplyLplk("[LPLK] Testfreigabe ungültig.");return;}
        File.Delete(path);var oldSettings=settings;bool oldWarmup=warmup,oldEnd=endVoteStarted;long oldWait=nextVoteAllowed,oldAutoWait=autoVotePostponedUntil;int oldRound=earliestEndRound;
        var before=Limits();var seed=new MapExtension(30,24,13);MapExtension? extended=null;bool success=false;string? error=null;
        try
        {
            PutLimits(seed);if(Limits()!=seed)throw new InvalidOperationException("Native seed rejected");
            settings=settings with{Enabled=true,MeasurementOnly=false,ExtendEnabled=true,ExtendMinPlayers=0,ExtendMinRounds=0,ExtendMinutes=15,ExtendRounds=5,ExtendLimit=1};warmup=false;
            if(!ExtendMap())throw new InvalidOperationException("Native extension rejected");extended=Limits();
            if(extended!=new MapExtension(45,29,16)||extensions!=1||ExtendMap())throw new InvalidOperationException("Native extension mismatch/limit");
            ResetParity();if(Limits()!=seed)throw new InvalidOperationException("Native owned rollback mismatch");success=true;
        }
        catch(Exception e){error=e.GetType().Name;}
        finally
        {
            try{ResetParity();PutLimits(before);if(Limits()!=before){success=false;error="Original limits not restored";}}
            catch{success=false;error="Original limit restore failed";}
            settings=oldSettings;warmup=oldWarmup;endVoteStarted=oldEnd;nextVoteAllowed=oldWait;autoVotePostponedUntil=oldAutoWait;earliestEndRound=oldRound;
        }
        string assemblySha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(LPLK.Common.RoundFileStore.PluginPath(Core),"LPLK_Maps.dll"))));
        AtomicJson.Write(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"parity-probe.json"),new{at=DateTimeOffset.UtcNow,version="0.4.4",assemblySha256,success,error,before,seed,extended,restored=Limits(),measurementOnly=settings.MeasurementOnly,humans=Humans().Count,clientGameplayTested=false});
        WriteStatus();ctx.ReplyLplk(success?"[LPLK] Native Mapverlängerung und Rücksetzung bestätigt.":"[LPLK] Mapprüfung fehlgeschlagen; Prüfbericht beachten.");
    }
}

