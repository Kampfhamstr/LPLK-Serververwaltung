using System.Text.Json;
using LPLK_Maps;

static class PromotionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var start=DateTimeOffset.Parse("2026-11-26T18:25:05Z");
        var markers=new NewMapMarkers(new(){["de_fresh"]=start});
        var maps=new[]{new MapEntry{Id="de_old",Name="Old"},new MapEntry{Id="de_fresh",Name="Fresh"}};
        foreach(var at in new[]{start,start.AddDays(3).AddTicks(-1)})
        {
            check(markers.IsPromoted("de_fresh",at)&&markers.IsNew("de_fresh",at),"Promotion and NEW start together and last through day three");
            check(markers.OrderNominations(maps,at)[0].Id=="de_fresh","Young map takes priority");
            check(markers.Label("de_fresh","name",at)=="<font color='#90EE90'><b>NEW</b></font> name","Promotion uses bold bright-green NEW");
        }
        foreach(var at in new[]{start.AddDays(3),start.AddDays(13),start.AddDays(14).AddTicks(-1)})
        {
            check(!markers.IsPromoted("de_fresh",at)&&markers.IsNew("de_fresh",at),"Promotion expires at exactly 72 hours while NEW remains");
            check(markers.OrderNominations(maps,at).SequenceEqual(maps),"Ordinary menu order resumes after promotion");
            check(markers.Label("de_fresh","name",at)=="<font color='#90EE90'>NEW</font> name","Only plain green NEW remains after day three");
            var promoted=maps.Where(m=>markers.IsPromoted(m.Id,at)).Select(m=>m.Id).ToHashSet();
            check(MapPolicy.CandidateSelection(maps,new HashSet<string>(),1,false,promoted)[0].Id=="de_old","Expired promotion cannot reserve a mapvote place");
        }
        check(!markers.IsNew("de_fresh",start.AddDays(14))&&markers.Label("de_fresh","name",start.AddDays(14))=="name","NEW expires after fourteen total days, not seventeen");
        check(!markers.IsPromoted("de_fresh",start.AddTicks(-1))&&!markers.IsPromoted("unknown",start),"Future and unknown activation cannot promote");
        markers.Observe(["de_fresh"],start.AddDays(2));
        var restored=NewMapMarkers.Parse(JsonSerializer.Serialize(markers.Snapshot()));
        check(!restored.IsPromoted("de_fresh",start.AddDays(3))&&restored.IsNew("de_fresh",start.AddDays(3)),"Reload and observation cannot restart either deadline");
        check(restored.Snapshot()["de_fresh"]==start,"Original activation timestamp retained");
        check(restored.OrderNominations(maps.Take(1),start).Count==1,"Priority cannot add an excluded map");
    }
}
