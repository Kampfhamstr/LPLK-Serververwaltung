using CS2ServerPortal;
using System.Net;
using System.Text.Json;

await GitHubChecks.RunAsync();
await GitHubImportChecks.RunAsync();

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
const string fixtureKey = "pt" + "lc_LOCAL_FIXTURE_NOT_A_REAL_KEY";
string ipBody = JsonSerializer.Serialize(new { errors = new[] { new { code = "AccessDeniedHttpException", detail = "This IP address (203.0.113.42) does not have permission to access the API using these credentials. " + fixtureKey } } });
string denied = PanelAccessErrors.Describe(HttpStatusCode.Forbidden, ipBody, "application/json");
Check(denied.Contains("IP-Beschränkung") && denied.Contains("203.0.113.42") && !denied.Contains(fixtureKey), "IP block detection or response redaction failed.");
string twoFactor = PanelAccessErrors.Describe(HttpStatusCode.Forbidden, "{\"errors\":[{\"code\":\"TwoFactorAuthRequiredException\"}]}", "application/json");
Check(twoFactor.Contains("Zwei-Faktor"), "2FA diagnosis failed.");
foreach (var body in new[] { "not-json " + fixtureKey, "{\"errors\":[null,2,{}]}", "[]", "{\"errors\":false}", "{\"errors\":[{\"detail\":\"" + fixtureKey + "\"}]}" })
    Check(!PanelAccessErrors.Describe(HttpStatusCode.Forbidden, body, "application/json").Contains(fixtureKey), "Untrusted response was exposed.");
Check(PanelAccessErrors.Describe(HttpStatusCode.Forbidden, "<html>" + fixtureKey, "text/html").Contains("HTML-Seite"), "Proxy response detection failed.");
var blockedHandler = new FixtureHandler(HttpStatusCode.OK, "{\"data\":[]}");
bool rejected = false;
try { using var unused = new PanelClient(new("https://panel.example.test", "pt" + "la_LOCAL_FIXTURE_NOT_A_REAL_KEY"), blockedHandler); }
catch (ArgumentException ex) { rejected = ex.Message.Contains("Application-API"); }
Check(rejected && blockedHandler.Requests == 0, "Application key was not rejected before sending.");
using (var client = new PanelClient(new("https://panel.example.test", fixtureKey), new FixtureHandler(HttpStatusCode.Forbidden, ipBody)))
{
    try { await client.GetServersAsync(false); throw new Exception("Forbidden response was accepted."); }
    catch (PanelException ex) { Check(ex.Message.Contains("203.0.113.42") && !ex.Message.Contains(fixtureKey), "HTTP response was not safely classified."); }
}
using (var client = new PanelClient(new("https://panel.example.test", fixtureKey), new FixtureHandler(HttpStatusCode.OK, "{\"data\":[]}")))
    Check((await client.GetServersAsync(false)).Count == 0, "Successful API response regressed.");
using (var client = new PanelClient(new("https://panel.example.test", fixtureKey), new FixtureHandler(HttpStatusCode.Forbidden, new string('X', 70000) + fixtureKey)))
{
    try { await client.GetServersAsync(false); throw new Exception("Oversized forbidden response was accepted."); }
    catch (PanelException ex) { Check(ex.Message.Contains("403") && !ex.Message.Contains(fixtureKey), "Oversized response handling failed."); }
}
var filesHandler = new FixtureHandler(HttpStatusCode.OK, """
{"data":[{"attributes":{"name":"plugin.cs","size":12,"is_file":true,"is_symlink":false,"modified_at":null}},{"attributes":{"name":"outside","size":0,"is_file":false,"is_symlink":true,"modified_at":null}}]}
""");
using (var client = new PanelClient(new("https://panel.example.test", fixtureKey), filesHandler))
{
    foreach (string badPath in new[] { "relative", "/addons/../private", "/addons/./test", "/addons\\test", "/addons/\n" })
    {
        try { await client.ListFilesAsync("12345678", badPath); throw new Exception("Invalid path accepted."); }
        catch (ArgumentException) { }
    }
    Check(filesHandler.Requests == 0, "Invalid paths sent to server.");
    var files = await client.ListFilesAsync("12345678", "/game/csgo/addons/a & b");
    Check(files.Count == 2 && files[1].IsSymlink, "Link metadata lost.");
    Check(filesHandler.LastMethod == HttpMethod.Get && filesHandler.LastUri!.Query.Contains("%26"), "File listing must use encoded GET.");
    await client.ReadFileAsync("12345678", "/game/csgo/addons/a & b/plugin.cs");
    Check(filesHandler.LastMethod == HttpMethod.Get, "File read must use GET.");
}
using (var client = new PanelClient(new("https://panel.example.test", fixtureKey), new FixtureHandler(HttpStatusCode.OK,
    "{\"data\":[{\"attributes\":{\"name\":\"../escape\",\"size\":0,\"is_file\":false}}]}")))
{
    try { await client.ListFilesAsync("12345678", "/addons"); throw new Exception("Traversal filename accepted."); }
    catch (PanelException) { }
}
string shape = string.Join("\n", AddonAnalysis.JsonShape("""
{ // comments supported
 "Enabled": true, "Menu": {"Title": "DO_NOT_EXPORT_VALUE"}, "ApiKey":"DO_NOT_EXPORT_SECRET",
 "Password": {"NestedSecret":"DO_NOT_EXPORT_NESTED"}, "76561198000000000":{"Owner":true},
 "Options":[{"Count":5}],
}
"""));
Check(shape.Contains("Menu.Title : String") && shape.Contains("Options[].Count : Number"), "Nested JSONC shape missing.");
Check(shape.Contains("Enabled : Boolean") && !shape.Contains(": True") && !shape.Contains(": False"), "Boolean values must not be exported.");
Check(!shape.Contains("DO_NOT_EXPORT") && !shape.Contains("ApiKey") && !shape.Contains("Password") && !shape.Contains("76561198"), "Config values/identities leaked.");
Check(AddonInventory.SkipDirectory("/game/csgo/addons/swiftlys2/logs") && !AddonInventory.SkipDirectory("/game/csgo/addons/swiftlys2/plugins/Test/src"), "Inventory exclusions regressed.");
Check(!AddonAnalysis.Eligible(new("/game/csgo/addons/swiftlys2/configs/database.jsonc", 1, true, false, null)), "Database config must be excluded.");
Check(!AddonAnalysis.Eligible(new("/game/csgo/addons/swiftlys2/plugins/Test/src/Test.cs", 1, true, true, null)), "Symlink must be excluded.");
string snapshot = """
{"updated_at":"2026-10-11T09:00:00Z","plugins":[{"id":"test","name":"Test","version":"1.0","status":"Loaded"}],"players":["PRIVATE_PLAYER"],"chat_messages":["PRIVATE_CHAT"],"bans":["PRIVATE_BAN"]}
""";
var runtime = AddonRuntime.Parse("12345678", snapshot, DateTimeOffset.Parse("2026-10-11T09:00:20Z"));
Check(runtime.FreshAtRead && runtime.Plugins.Count == 1, "Fresh snapshot misclassified.");
Check(!JsonSerializer.Serialize(runtime).Contains("PRIVATE_"), "Runtime export leaked unrelated user data.");
Check(!AddonRuntime.Parse("12345678", snapshot, DateTimeOffset.Parse("2026-10-11T10:00:00Z")).FreshAtRead, "Stale snapshot presented as live.");
Check(!AddonRuntime.Parse("12345678", snapshot, DateTimeOffset.Parse("2026-10-11T08:00:00Z")).FreshAtRead, "Future snapshot presented as live.");
await DashboardChecks.Run();
var review=MapReview.Parse("""
{"updatedAt":"2026-10-11T12:07:00Z","measurementOnly":true,"catalog":3,"measured":1,"approved":0,"poolMessage":"ok","transition":"ok","limitReview":[{"id":"map","name":"Map","inPool":true},{"id":"sound","name":"Sound","inPool":true},{"id":"unknown","name":"Unknown","inPool":true}]}
""");
review=MapReview.WithContentKinds(review,"""[{"Id":"map","IsMap":true},{"Id":"sound","IsMap":false}]""");
Check(review.LimitReview[0].IsMap is true && review.LimitReview[1].IsMap is false && review.LimitReview[2].IsMap is null,"Content classification must distinguish maps, assets and unknown entries.");
Check(review.LimitReview[1].Result.Contains("Kein Map-Inhalt") && review.LimitReview[2].Result=="Messung offen","Assets must not remain pending; missing evidence must remain pending.");
Check(review.MeasurementOnly && review.Approved==0 && review.LimitReview.All(m=>!m.Approved),"Content metadata must never grant limits.");
var excludedReview=MapReview.WithExclusions(review,"""{"LPLK_Maps":{"ExcludedWorkshopIds":["map"]}}""");
Check(excludedReview.LimitReview[0].Excluded && !excludedReview.LimitReview[1].Excluded && excludedReview.LimitReview[0].Result.Contains("ausgeschlossen"),"Configured exclusions must apply even to an entry tagged as a Map by Steam.");
Check(!excludedReview.LimitReview[0].Approved && excludedReview.MeasurementOnly,"Exclusions must not approve or activate limits.");
var approvalRows=Enumerable.Range(100,90).Select(n=>n.ToString()).Concat(TestMapApproval.Withheld).Select(id=>review.LimitReview[0] with
    {Id=id,IsMap=true,InPool=true,MeasuredAt=DateTimeOffset.UtcNow,ProbeError=null,MinPlayers=1,MaxPlayers=8,LimitProposal=new(1,6,2,"reviewed-"+id)}).ToList();
var approvalReview=review with{LimitReview=approvalRows,Approved=0,MeasurementOnly=true};
var approvalPlan=TestMapApproval.Prepare(approvalReview);
Check(approvalPlan.Count==90&&!approvalPlan.Any(m=>TestMapApproval.Withheld.Contains(m.Id)),"Batch approval must exclude both specifically withheld maps.");
Check(approvalPlan.All(m=>m.BeforeMax==8)&&approvalRows.All(m=>m.MaxPlayers==8&&!m.Approved),"Preparing approval must preserve original bounds and never mutate the snapshot.");
foreach(var invalid in new[]{approvalReview with{MeasurementOnly=false},approvalReview with{Approved=1},approvalReview with{LimitReview=approvalRows.Skip(1).ToList()},approvalReview with{LimitReview=approvalRows.Select(m=>m.Id=="100"?m with{ProbeError="changed"}:m).ToList()}})
{try{TestMapApproval.Prepare(invalid);throw new Exception("Invalid approval snapshot accepted.");}catch(InvalidDataException){}}
try { MapReview.WithContentKinds(review,"""[{"Id":"map","IsMap":true},{"Id":"map","IsMap":false}]""");throw new Exception("Ambiguous content kinds accepted."); } catch(InvalidDataException) {}
foreach(var (uuid,ip,port,allowed) in new[] {
    (PanelClient.TestServerUuid,"203.0.113.10",27020,true),
    ("11110003-0000-4000-8000-000000000003","203.0.113.10",27020,false),
    (PanelClient.TestServerUuid,"203.0.113.10",27019,false),
    (PanelClient.TestServerUuid,"203.0.113.1",27020,false) })
{
    var body=JsonSerializer.Serialize(new {attributes=new {uuid,relationships=new {allocations=new {data=new[] {new {attributes=new {is_default=true,ip,port}}}}}}});
    var handler=new FixtureHandler(HttpStatusCode.OK,body);
    using var client=new PanelClient(new("https://panel.example.test",fixtureKey),handler);
    bool powerRejected=false;
    try { await client.TestServerPowerAsync("start"); } catch(InvalidOperationException) { powerRejected=true; }
    Check(powerRejected!=allowed,"Test power identity guard failed.");
    Check(handler.Requests==(allowed?2:1) && (allowed?handler.LastMethod==HttpMethod.Post:handler.LastMethod==HttpMethod.Get),"Foreign server must never receive power POST.");
    if(allowed)Check(handler.LastUri!.AbsolutePath=="/api/client/servers/11110004/power","Power POST escaped fixed test target.");
}
var unsafePower=new FixtureHandler(HttpStatusCode.OK,"{}");
using(var client=new PanelClient(new("https://panel.example.test",fixtureKey),unsafePower))
{
    try { await client.TestServerPowerAsync("kill"); throw new Exception("Unsupported power signal accepted."); } catch(ArgumentException) {}
    Check(unsafePower.Requests==0,"Unsupported signal must not contact server.");
}
Console.WriteLine("PASS: panel diagnostics; bounded error handling; secret suppression; read-only encoded file requests; traversal rejection; symlink metadata; JSONC field extraction without values; secret/data exclusions; runtime freshness and privacy.");

Check(SshService.CleanOutput(new string('x',25000),"secret",32000).Length==25000,"Extended fixed status envelope preserves full JSON payload");
Check(SshService.CleanOutput("secret"+new string('x',33000),"secret",32000).StartsWith("[ausgeblendet]")&&SshService.CleanOutput(new string('x',33000),"",32000).EndsWith("[Ausgabe gekürzt]"),"Extended status envelope remains bounded and redacted");
Check(SshService.CleanOutput(new string('x',21000),"").EndsWith("[Ausgabe gekürzt]"),"Default SSH envelope unchanged");

sealed class FixtureHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public int Requests { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public Uri? LastUri { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        LastMethod = request.Method;
        LastUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
