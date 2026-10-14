using System.Text.Json;

namespace CS2ServerPortal;

internal sealed record ReviewedMapApproval(string Id,string Signature,int BeforeMin,int BeforeMax);
internal static class TestMapApproval
{
    internal static readonly string[] Withheld=["3354923062","3733075447"];
    internal static string BaselinePath(string workspace)=>Path.Combine(workspace,"state","map-limit-review-before-approval.json");
    internal static List<ReviewedMapApproval> Prepare(MapReviewSnapshot review)
    {
        if(!review.MeasurementOnly||review.Approved!=0)throw new InvalidDataException("Ursprüngliche Übersicht ohne Freigaben erforderlich.");
        var maps=review.LimitReview.Where(m=>m.InPool&&!m.Excluded&&m.IsMap is not false).ToList();
        if(maps.Count!=92||Withheld.Any(id=>!maps.Any(m=>m.Id==id))||maps.Any(m=>m.MeasuredAt is null||m.ProbeError is not null||m.LimitProposal is null))
            throw new InvalidDataException("Vollständige geprüfte 92-Map-Übersicht erforderlich.");
        return maps.Where(m=>!Withheld.Contains(m.Id)).Select(m=>new ReviewedMapApproval(m.Id,m.LimitProposal!.Signature,m.MinPlayers,m.MaxPlayers)).ToList();
    }
    internal static List<ReviewedMapApproval> ReadOrFreeze(string workspace)
    {
        string path=BaselinePath(workspace);
        if(!File.Exists(path))
        {
            string json=File.ReadAllText(MapReview.PathFor(workspace));Prepare(MapReview.Parse(json));
            using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            using var writer=new StreamWriter(file);writer.Write(json);
        }
        return Prepare(MapReview.Parse(File.ReadAllText(path)));
    }
    internal static void RecordConfirmed(string workspace)
    {
        var plan=ReadOrFreeze(workspace);var review=MapReview.Read(workspace)??throw new InvalidDataException("Mapübersicht fehlt.");
        if(!review.MeasurementOnly||review.Approved!=90||Withheld.Any(id=>!review.LimitReview.Any(m=>m.Id==id&&!m.Approved))||
            plan.Any(p=>!review.LimitReview.Any(m=>m.Id==p.Id&&m.Approved&&m.LimitProposal?.Signature==p.Signature)))
            throw new InvalidDataException("90 Freigaben und beide zurückgestellten Maps sind noch nicht im Laufzeitstand bestätigt.");
        var now=DateTimeOffset.UtcNow;
        var receipt=new{confirmedAt=now,testServer=PanelClient.TestServerUuid,measurementOnly=true,withheld=Withheld,
            maps=plan.Select(p=>{var m=review.LimitReview.Single(m=>m.Id==p.Id);return new{p.Id,p.Signature,p.BeforeMin,p.BeforeMax,afterMin=m.MinPlayers,afterMax=m.MaxPlayers};})};
        File.WriteAllText(Path.Combine(workspace,"state","map-limit-approval.json"),JsonSerializer.Serialize(receipt,Program.JsonOptions));
        new DashboardStore(workspace).Update(state=>
        {
            var old=state.Plans.FirstOrDefault(p=>p.Id=="LPLK_Maps");if(old is null)return;
            string notes=string.Join("\n",old.Notes.Split('\n').Where(l=>!l.StartsWith("Mapfreigabe 11.10.2026:")));
            notes=(notes.Trim()+"\nMapfreigabe 11.10.2026: Benutzer hat 90 geprüfte Mapgrenzen freigegeben; Laufzeit bestätigt. Manor und de_sewer bleiben ohne Freigabe. Messbetrieb bleibt aktiv, normale Abstimmungen und Ingame-Abnahme offen. Ausgangsstand und Freigabeprotokoll gespeichert.").Trim();
            DashboardChanges.SavePlan(state,old with{Notes=notes},false,now,old);
        });
    }
}

internal static partial class SshService
{
    internal static SshCommandResult ApproveReviewedTestMaps(SshConnection connection,string workspace,bool apply)
    {
        var proposals=TestMapApproval.ReadOrFreeze(workspace);
        string payload=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{apply,proposals},Program.JsonOptions));
        string command=PanelScript("TestMapApproval.php").Replace("__LPLK_REVIEW_BASE64__",payload,StringComparison.Ordinal);
        using var client=Connect(connection);
        return Run(client,connection,apply?"90 geprüfte Mapgrenzen ausschließlich auf Testserver übernehmen":"Geprüfte Mapfreigaben mit Testserver abgleichen (nur lesend)",command,60);
    }
}
