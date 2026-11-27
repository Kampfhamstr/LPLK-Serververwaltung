using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Maps;

int checks=0;
void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
void Reject(Action action,string label){bool bad=false;try{action();}catch(InvalidDataException){bad=true;}Check(bad,label);}
foreach(string name in new[]{"surf_test","Workshop Surf_Test","kz_checkmate","xc_test","bkz_test","de_dust2"})Check(MapPolicy.Category(name)==(name.Contains("surf",StringComparison.OrdinalIgnoreCase)?MapCategory.Surf:name.Contains("_test")||name.StartsWith("kz")?MapCategory.Kz:MapCategory.Other),"category "+name);
var settings=new MapSettings{MeasurementOnly=false,RescanBeforeVote=false,AllowSpectatorsToVote=true,ExtendEnabled=true,ExtendMinPlayers=0,EndVoteAllowExtend=true};
var adminMap=new MapEntry{Id="de_admin_test",Name="Admin",InPool=false,Enabled=false,Removed=true,MinPlayers=90,MaxPlayers=128};
Check(MapPolicy.AdminSelectable(adminMap,true,true),"Admin map choice ignores pool, disable, removal and player-limit approval");
Check(!MapPolicy.AdminSelectable(adminMap,false,true),"Unavailable admin map remains unavailable technically");
Check(!MapPolicy.AdminSelectable(adminMap with{IsMap=false},true,true),"Sound package cannot become admin map");
Check(!MapPolicy.AdminSelectable(adminMap with{Id="de_test;quit"},true,true),"Unsafe native map token rejected");
Check(!MapPolicy.AdminSelectable(adminMap,true,false),"Missing required crash protection remains a technical block");
using(var overrideWorld=new World(settings with{Enabled=false,MeasurementOnly=true,BuiltinMapsOnly=true,ExcludedWorkshopIds=["12345"]}))
{
 var chosen=adminMap with{Id="12345",RemoteMapVerified=true};var old=adminMap with{Id="de_old"};
 overrideWorld.Set("maps",new List<MapEntry>{chosen});overrideWorld.Set("pending",old);overrideWorld.Set("nextMap",old);
 Check(((List<MapEntry>)overrideWorld.Call("MapChangeEntries",0)!).Any(m=>m.Id==chosen.Id),"Actual admin list includes disallowed/unapproved workshop map");
 overrideWorld.Call("Schedule",chosen,3,false,false,null,"map_command");
 Check(overrideWorld.Get<MapEntry>("pending").Id==chosen.Id&&overrideWorld.Get<MapEntry>("nextMap").Id==chosen.Id,"Actual admin dispatch replaces previously selected map despite disabled voting and builtin-only rules");
 Check(!overrideWorld.Get<bool>("changeAtRoundEnd"),"Direct admin change is not held by old round-end request");
 Check(!(bool)overrideWorld.Call("CanExtend",false)!,"Player extension cannot remove admin next-map priority");
 overrideWorld.Set("pending",null);overrideWorld.Set("nextMap",chosen);overrideWorld.Call("MatchEnded");
 Check(overrideWorld.Get<MapEntry>("pending").Id==chosen.Id,"Admin next-map priority survives match end even when vote module is disabled");
 overrideWorld.Call("ResetParity");Check(overrideWorld.Get<string?>("adminNextMapId") is null,"New map removes previous admin priority");
 overrideWorld.Set("pending",null);overrideWorld.Set("transitionIssued",true);
 overrideWorld.Call("Schedule",chosen,3,false,false,null,"map_command");
 Check(overrideWorld.Get<MapEntry>("pending").Id==chosen.Id&&!overrideWorld.Get<bool>("transitionIssued"),"Completed old engine transition cannot block a subsequent admin map change");
 overrideWorld.Set("transitionIssued",true);overrideWorld.Call("Schedule",chosen with{Id="12346"},3,false,false,null,"map_command");
 Check(overrideWorld.Get<MapEntry>("pending").Id==chosen.Id,"Already issued active engine transition is not replaced");
 overrideWorld.Set("transitionIssued",false);
 overrideWorld.Set("pending",null);overrideWorld.Set("nextMap",null);overrideWorld.Set("matchEnded",false);
 var adminReason=Enum.Parse(typeof(Plugin).GetNestedType("Reason",BindingFlags.NonPublic)!,"Admin");
 Check((bool)overrideWorld.Call("BeginVote",adminReason,new HashSet<string>{chosen.Id},null)!,"Admin-selected ballot can include restricted map while automatic voting is disabled");
 overrideWorld.Call("CastBallot",overrideWorld.Menu.Player,chosen);
 Check(overrideWorld.Get<Dictionary<ulong,string>>("ballots").GetValueOrDefault(overrideWorld.Menu.Player.SteamID)==chosen.Id,"Actual admin-selected ballot accepts vote despite ordinary eligibility rules");
 overrideWorld.Call("FinishVote");Check(overrideWorld.Get<MapEntry>("nextMap").Id==chosen.Id&&overrideWorld.Get<string?>("adminNextMapId")==chosen.Id,"Admin ballot result keeps override through round or match end");
}
var configured=new MapEntry{Id="3618493746",Name="Zombie Sweden",MinPlayers=1,MaxPlayers=30,ConfiguredMaxPlayers=30};
Check(!MapPolicy.LimitsUsable(configured,settings),"PvP still requires measured approved limits");
Check(MapPolicy.LimitsUsable(configured,settings with{UseConfiguredZombieLimits=true})&&!MapPolicy.Measured(configured)&&!MapPolicy.LimitsApproved(configured),"Zombie configured limits are usable without fabricating measured approval");
Check(!MapPolicy.LimitsUsable(configured with{MaxPlayers=64},settings with{UseConfiguredZombieLimits=true}),"Configured Zombie ceiling cannot be raised implicitly");
Check(MapPolicy.LimitsUsable(configured,settings with{UseConfiguredMapLimits=true})&&!MapPolicy.LimitsApproved(configured),"Imported explicit map bounds preserve eligibility without measured approval");
Check(!MapPolicy.LimitsUsable(configured with{ConfiguredMaxPlayers=null},settings with{UseConfiguredMapLimits=true}),"Unconfigured discovered maps do not acquire migrated eligibility");
foreach(int minute in new[]{0,15,30})foreach(int rounds in new[]{0,20,24})foreach(int wins in new[]{0,10,16})
{
 if(minute+rounds+wins==0){Reject(()=>MapPolicy.Extension(settings,0,minute,rounds,wins),"no active limits cannot extend");continue;}
 var result=MapPolicy.Extension(settings,0,minute,rounds,wins);Check(result==new MapExtension(minute>0?minute+15:0,rounds>0?rounds+5:0,wins>0?wins+3:0),"extend only active limits");
}
Reject(()=>MapPolicy.Extension(settings,3,30,24,13),"bounded extensions");Reject(()=>MapPolicy.Extension(settings,0,float.NaN,24,13),"unknown time rejected");
Check(MapPolicy.Extension(settings with{ExtendInitializeRoundLimits=true},0,0,0,0)==new MapExtension(0,29,18),"explicit old MapChooser zero-limit behavior preserved");
Reject(()=>MapSettings.Validate(settings with{ExtendPercent=101}),"invalid threshold");Reject(()=>MapSettings.Validate(settings with{SetNextMapPermission=""}),"empty permission rejected");
MapSettings.Validate(settings with{RtvMinPlayers=0,ExtendMinPlayers=0,DirectVotemapMinPlayers=0});checks++;
var pool=new[]{new MapEntry{Id="surf_one",Name="Surf One"},new MapEntry{Id="surf_two",Name="Surf Two"},new MapEntry{Id="kz_one",Name="KZ One"},new MapEntry{Id="de_one",Name="Dust One"}};
Check(MapPolicy.CandidateSelection(pool,new HashSet<string>{"surf_two"},3,true).Select(m=>m.Id).SequenceEqual(new[]{"surf_two","surf_one","kz_one"}),"nomination first, category round robin");
Check(MapPolicy.Resolve(pool,"One")==null,"ambiguous partial selection rejected");Check(MapPolicy.Resolve(pool,"surf_two")==pool[1],"exact ID wins");
Check(MapPolicy.NextCycle(pool,new HashSet<string>{"surf_one","de_one"},"surf_one")==pool[3],"cycle skips disallowed maps");Check(MapPolicy.NextCycle(pool,new HashSet<string>{"surf_one"},"de_one")==pool[0],"cycle wraps");
// Idle rotation must never cut into a human session or use a remote/unapproved map.
var clock=new EmptyMapClock();Check(!clock.Due(1000,false,40)&&!clock.Due(2400999,false,40)&&clock.Due(2401000,false,40),"exact forty minute idle boundary");
Check(!clock.Due(2401001,true,40)&&!clock.Due(2401002,false,40)&&!clock.Due(4801001,false,40)&&clock.Due(4801002,false,40),"human presence resets entire idle interval");
clock.Reset();Check(!clock.Due(9000000,false,40)&&!clock.Due(19000000,false,0),"new map and disabled idle rotation reset");
var idleSettings=settings with{UseConfiguredMapLimits=true,IdleRotationMinutes=40};var installed=new HashSet<string>{configured.Id};
Check(MapPolicy.EmptyMapEligible(configured,idleSettings,installed),"installed explicit one-player map permitted");
foreach(var bad in new[]{configured with{MinPlayers=6},configured with{Enabled=false},configured with{InPool=false},configured with{Removed=true},configured with{ConfiguredMaxPlayers=null}})Check(!MapPolicy.EmptyMapEligible(bad,idleSettings,installed),"ineligible idle map rejected");
Check(!MapPolicy.EmptyMapEligible(configured,idleSettings,new HashSet<string>()),"idle rotation never starts a download");
var secondIdle=configured with{Id="3070192462"};var thirdIdle=configured with{Id="3070378842"};
Check(MapPolicy.NextEmptyMap(new[]{configured,secondIdle,thirdIdle},"",configured.Id,new[]{secondIdle.Id,thirdIdle.Id,configured.Id})==secondIdle,"current excluded and least recent chosen");
Check(MapPolicy.NextEmptyMap(new[]{configured},"",configured.Id,Array.Empty<string>()) is null,"single current map never loops");
Check(MapPolicy.StartupMapCommand(configured)=="host_workshop_map "+configured.Id&&MapPolicy.StartupMapCommand(configured with{Id="de_dust2"})=="map de_dust2","safe startup command by map type");
Reject(()=>MapPolicy.StartupMapCommand(configured with{Id="de_dust2;quit"}),"startup command injection rejected");
Reject(()=>MapSettings.Validate(idleSettings with{IdleRotationMinutes=-1}),"idle interval validated");
using(var w=new World(settings))
{
 w.Limits["mp_timelimit"]=30f;w.Limits["mp_maxrounds"]=24;w.Limits["mp_winlimit"]=13;
 Check((bool)w.Call("ExtendMap",false)!,"native boundary setters extend");Check(w.Get<int>("extensions")==1&&w.Limits["mp_timelimit"].Equals(45f)&&w.Limits["mp_maxrounds"].Equals(29)&&w.Limits["mp_winlimit"].Equals(16),"all limits verified");
 Check(w.Get<long>("autoVotePostponedUntil")>=Environment.TickCount64+14*60000L,"extension postpones elapsed-time automatic vote");
 w.Limits["mp_maxrounds"]=99;w.Call("ResetParity");Check(w.Limits["mp_timelimit"].Equals(30f)&&w.Limits["mp_maxrounds"].Equals(99)&&w.Limits["mp_winlimit"].Equals(13),"restore only owned values preserves external change");
}
using(var w=new World(settings))
{
 w.Limits["mp_timelimit"]=30f;w.Limits["mp_maxrounds"]=24;w.IgnoreSet="mp_maxrounds";
 Check(!(bool)w.Call("ExtendMap",false)!,"failed native setter never confirms extension");Check(w.Get<int>("extensions")==0&&w.Limits["mp_timelimit"].Equals(30f)&&w.Limits["mp_maxrounds"].Equals(24),"partial write rolled back");
}
using(var w=new World(settings with{MeasurementOnly=true}))
{
 w.Limits["mp_maxrounds"]=24;Check(!(bool)w.Call("ExtendMap",false)!,"measurement mode cannot extend");Check(w.Limits["mp_maxrounds"].Equals(24),"measurement mode untouched");
}
using(var w=new World(settings))
{
 var map=MapPolicy.CaptureMeasurement(new MapEntry{Id="de_one",Name="One"},16,16,0,0);map=MapPolicy.ApproveLimits(map,map.LimitProposal!.Signature);
 w.Set("maps",new List<MapEntry>{map});w.Set("installed",new HashSet<string>{map.Id});w.Set("candidates",new List<MapEntry>{map});w.Set("voteEnds",Environment.TickCount64+60000);
 w.Call("ShowVote",w.Menu.Player);Check(w.Menu.Labels.Any(x=>x.Contains("One")),"actual vote opens");
 var button=w.Menu.Find("One");w.Menu.Session++;await w.Menu.Click(button);Check(w.Get<Dictionary<ulong,string>>("ballots").Count==0,"old session cannot vote");
 w.Call("ShowVote",w.Menu.Player);w.Set("maps",new List<MapEntry>{map with{Enabled=false}});await w.Menu.Click("One");Check(w.Get<Dictionary<ulong,string>>("ballots").Count==0,"click rechecks changed map eligibility");
 Check(w.Broadcasts.Count==0,"stale or invalid vote has no public announcement");
 w.Set("maps",new List<MapEntry>{map});w.Call("ShowVote",w.Menu.Player);await w.Menu.Click("One");Check(w.Get<Dictionary<ulong,string>>("ballots")[w.Menu.Player.SteamID]==map.Id,"eligible current player ballot saved");
 Check(w.Broadcasts.Count==1&&w.Broadcasts[0].Contains("Prüfspieler")&&w.Broadcasts[0].Contains("stimmt für")&&w.Broadcasts[0].Contains("One[yellow] (1)")&&w.Broadcasts[0].Contains("[blue]"),"accepted vote announced to the whole server with voter and choice");
 w.Call("CastBallot",w.Menu.Player,map);Check(w.Broadcasts.Count==1&&w.Get<Dictionary<ulong,string>>("ballots").Count==1,"same choice never adds a vote or public spam");
 var second=map with{Id="de_two",Name="Two"};w.Set("maps",new List<MapEntry>{map,second});w.Set("installed",new HashSet<string>{map.Id,second.Id});w.Set("candidates",new List<MapEntry>{map,second});w.Call("CastBallot",w.Menu.Player,second);
 Check(w.Broadcasts.Count==2&&w.Broadcasts[1].Contains("ändert seine Stimme")&&w.Broadcasts[1].Contains("Two[yellow] (1)")&&w.Get<Dictionary<ulong,string>>("ballots").Count==1&&w.Get<Dictionary<ulong,string>>("ballots")[w.Menu.Player.SteamID]==second.Id,"revote replaces the ballot and announces the new choice once");
 w.Set("voteEnds",0L);w.Call("CastBallot",w.Menu.Player,map);Check(w.Broadcasts.Count==2,"closed vote cannot announce another choice");
}
using(var w=new World(settings))
{
 var maps=new[]{"de_one","de_two","de_three"}.Select(id=>{var m=MapPolicy.CaptureMeasurement(new MapEntry{Id=id,Name=id},16,16,0,0);return MapPolicy.ApproveLimits(m,m.LimitProposal!.Signature);}).ToList();
 w.Set("maps",maps);w.Set("installed",maps.Select(m=>m.Id).ToHashSet());w.Menu.Permissions.Add("admin.mapsvote");
 var selected=new HashSet<string>{"de_one","de_three"};w.Call("CustomVoteMenu",w.Menu.Player,selected);var start=w.Menu.Find("Abstimmung starten");
 w.Menu.Permissions.Clear();await w.Menu.Click(start);Check(w.Get<long>("voteEnds")==0,"custom admin vote rechecks permission");
 w.Menu.Permissions.Add("admin.mapsvote");w.Call("CustomVoteMenu",w.Menu.Player,selected);w.Set("maps",maps.Select(m=>m.Id=="de_three"?m with{Enabled=false}:m).ToList());
 await w.Menu.Click("Abstimmung starten");Check(w.Get<List<MapEntry>>("candidates").Select(m=>m.Id).SequenceEqual(new[]{"de_one","de_three"}),"custom admin vote preserves explicit selection despite ordinary pool disable");
 Check(w.Get<long>("voteEnds")>Environment.TickCount64,"custom map vote actually starts");
}

// Audit integration: use the real vote methods; changing a ballot replaces only
// the counted choice, while both historical choices retain actor identity.
using(var w=new World(settings with{RtvAtRoundEnd=true}))
{
 var maps=new[]{"de_one","de_two"}.Select(id=>{var m=MapPolicy.CaptureMeasurement(new MapEntry{Id=id,Name=id},16,16,0,0);return MapPolicy.ApproveLimits(m,m.LimitProposal!.Signature);}).ToList();
 w.Set("maps",maps);w.Set("installed",maps.Select(m=>m.Id).ToHashSet());w.Menu.Permissions.Add("admin.mapsvote");
 Check((bool)w.Call("StartSelectedVote",w.Menu.Player,maps.Select(m=>m.Id).ToHashSet())!,"audited admin vote starts");
 w.Call("CastBallot",w.Menu.Player,maps[0]);w.Call("CastBallot",w.Menu.Player,maps[1]);w.Call("CastBallot",w.Menu.Player,maps[1]);
 w.Get<Dictionary<ulong,string>>("ballots")[123UL]=maps[0].Id; // disconnected voter
 w.Call("FinishVote");
 Check(w.Get<MapEntry>("nextMap").Id==maps[1].Id,"logging preserves actual winner and excludes offline voter");
 var last=w.Get<LastVoteHistory>("lastVotes").Last!;
 Check(last.Result.winnerId==maps[1].Id&&last.History.Length==2,"last vote immediately available in RAM before audit persistence");
 w.Menu.Permissions.Clear();w.Plugin.LastVoteCommand(w.Menu.Context);
 Check(w.Menu.Labels.Any(x=>x.Contains("Spieler: 2"))&&w.Menu.Labels.Any(x=>x.Contains("Verlauf: 2")),"lv readable without administration rights");
 Check(w.Menu.Current!.Configuration.MaxVisibleItems==5,"last-vote menu at most five visible rows");
 await w.Menu.Click("Spieler: 2");Check(w.Menu.Labels.Any(x=>x.Contains("Prüfspieler"))&&w.Menu.Labels.Any(x=>x.Contains("nicht gezählt")),"final voter list includes excluded voter");
 var voter=w.Menu.Find("Prüfspieler");w.Menu.Session++;int oldMessages=w.Menu.Messages.Count;await w.Menu.Click(voter);Check(w.Menu.Messages.Count==oldMessages,"old session cannot use last-vote menu");
 w.Plugin.LastVoteCommand(w.Menu.Context);await w.Menu.Click("Maps: 2");Check(w.Menu.Labels.Any(x=>x.Contains("de_two")&&x.Contains("1 Stimmen")),"map counts reflect final vote only");
 await w.Menu.Click("de_two");Check(w.Menu.Labels.Any(x=>x.Contains("Prüfspieler")),"map opens its voters");
 w.Plugin.LastVoteCommand(w.Menu.Context);await w.Menu.Click("Verlauf: 2");Check(w.Menu.Labels.Count(x=>x.Contains("Prüfspieler"))==2,"both original and changed ballot shown");
 string path=Path.Combine(w.Menu.DataDirectory,"map-audit.json");Check(!File.Exists(path),"vote path does not create a file");
 var journal=w.Get<MapAuditJournal>("mapAudit");journal.ReleaseAfterRound();journal.WaitForReleasedWrites();
 using var json=JsonDocument.Parse(File.ReadAllText(path));var events=json.RootElement.GetProperty("Events").EnumerateArray().ToArray();
 var ballots=events.Where(e=>e.GetProperty("Kind").GetString()=="ballot").ToArray();
 Check(ballots.Length==2&&ballots[1].GetProperty("Data").GetProperty("previousMapId").GetString()==maps[0].Id,"journal keeps revote history without duplicate same-choice event");
 Check(ballots.All(e=>e.GetProperty("Data").GetProperty("actor").GetProperty("SteamId").GetString()==w.Menu.Player.SteamID.ToString()),"immutable Steam identity preserved");
 var start=events.Single(e=>e.GetProperty("Kind").GetString()=="vote_started");
 Check(start.GetProperty("Data").GetProperty("actor").GetProperty("Name").GetString()=="Prüfspieler","admin vote initiator recorded");
 var result=events.Single(e=>e.GetProperty("Kind").GetString()=="vote_result");
 Check(result.GetProperty("VoteId").GetString()==start.GetProperty("VoteId").GetString()&&result.GetProperty("Data").GetProperty("winnerId").GetString()==maps[1].Id,"result tied to the actual vote/winner");
 Check(result.GetProperty("Data").GetProperty("ballots")[1].GetProperty("excludedReason").GetString()=="not_online_or_eligible","excluded voter explained in final result");
 w.Call("Schedule",maps[0],3,false,false,w.Menu.Player,"admin_menu");
 Check(w.Get<MapEntry?>("pending") is null,"Map-vote permission alone cannot execute a direct admin map change");
 w.Menu.Permissions.Add("lplk.maps.admin");w.Call("Schedule",maps[0],3,false,false,w.Menu.Player,"admin_menu");
 journal.ReleaseAfterRound();journal.WaitForReleasedWrites();
 using var changed=JsonDocument.Parse(File.ReadAllText(path));var admin=changed.RootElement.GetProperty("Events").EnumerateArray().Single(e=>e.GetProperty("Kind").GetString()=="admin_map_change");
 Check(admin.GetProperty("Data").GetProperty("actor").GetProperty("SteamId").GetString()==w.Menu.Player.SteamID.ToString()&&admin.GetProperty("Data").GetProperty("mapId").GetString()==maps[0].Id,"accepted admin change records actor and target");
}
using(var w=new World(settings))
{
 var maps=new[]{"de_one","de_two"}.Select(id=>{var m=MapPolicy.CaptureMeasurement(new MapEntry{Id=id,Name=id},16,16,0,0);return MapPolicy.ApproveLimits(m,m.LimitProposal!.Signature);}).ToList();
 w.Set("maps",maps);w.Set("installed",maps.Select(m=>m.Id).ToHashSet());w.Menu.Permissions.Add("admin.mapsvote");
 w.Get<Dictionary<ulong,string>>("nominations")[w.Menu.Player.SteamID]=maps[1].Id;
 w.Call("StartSelectedVote",w.Menu.Player,maps.Select(m=>m.Id).ToHashSet());w.Call("FinishVote");
 Check(w.Get<MapEntry>("nextMap").Id==maps[1].Id,"zero-vote nomination tie rule unchanged");
 var j=w.Get<MapAuditJournal>("mapAudit");j.ReleaseAfterRound();j.WaitForReleasedWrites();
 using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(w.Menu.DataDirectory,"map-audit.json")));
 var result=doc.RootElement.GetProperty("Events").EnumerateArray().Single(e=>e.GetProperty("Kind").GetString()=="vote_result").GetProperty("Data");
 Check(result.GetProperty("zeroVotes").GetBoolean()&&result.GetProperty("finalTiePool").GetArrayLength()==1&&!result.GetProperty("randomTieBreak").GetBoolean(),"zero votes and nomination-filtered tie are explicit");
}
foreach(bool categorized in new[]{false,true})using(var w=new World(settings with{CategorizedMenus=categorized}))
{
 var maps=Enumerable.Range(1,20).Select(n=>new MapEntry{Id=n.ToString(),Name="Map "+n,MinPlayers=1,MaxPlayers=30}).ToList();
 var markers=w.Get<NewMapMarkers>("newMapMarkers");markers.Observe(["16","17","18","19","20"],DateTimeOffset.UtcNow.AddDays(-2));
 MapEntry? clicked=null;
 w.Call("Categories",w.Menu.Player,"Map nominieren",(Func<List<MapEntry>>)(()=>maps),(Action<SwiftlyS2.Shared.Players.IPlayer,MapEntry>)((_,m)=>clicked=m),(Func<SwiftlyS2.Shared.Players.IPlayer,bool>)(_=>true),true);
 Check(w.Menu.Labels.Take(5).Select((label,n)=>label.Contains("NEW")&&label.Contains("Map "+(16+n))).All(x=>x),"nomination first page contains all five NEW maps, including categorized config");
 Check(w.Menu.Current!.Configuration.MaxVisibleItems==5,"NEW page retains five visible rows");
 await w.Menu.Click("Weiter");Check(w.Menu.Labels.All(x=>!x.Contains("NEW")),"second page has no duplicate NEW maps");
 await w.Menu.Click("Vorherige Seite");await w.Menu.Click("Map 16");Check(clicked?.Id=="16","priority row selects its matching map ID");
}
var auditRoot=Path.Combine(Path.GetTempPath(),"LplkAuditChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(auditRoot);
try
{
 MapAuditEvent Entry(string id)=>new(id,DateTimeOffset.UtcNow,"test","de_one",1,null,new {name="A",steamId="76561198000000000"});
 string path=Path.Combine(auditRoot,"map-audit.json");var journal=new MapAuditJournal(path);
 journal.Capture(Entry("first"));Check(!File.Exists(path),"capture only RAM");
 journal.ReleaseAfterRound();journal.Capture(Entry("next-round"));journal.WaitForReleasedWrites();
 Check(journal.Pending==1&&JsonSerializer.Deserialize<MapAuditDocument>(File.ReadAllText(path))!.Events.Length==1,"fixed boundary snapshot excludes later events");
 journal.ReleaseAfterRound();journal.WaitForReleasedWrites();Check(journal.Pending==0&&journal.Error is null,"next boundary writes later event");
 // A new instance has no synchronous startup read, then merges/deduplicates on its worker.
 var reload=new MapAuditJournal(path);reload.Capture(Entry("first"));reload.Capture(Entry("third"));reload.ReleaseAfterRound();reload.WaitForReleasedWrites();
 Check(JsonSerializer.Deserialize<MapAuditDocument>(File.ReadAllText(path))!.Events.Length==3,"reload/retry preserves unique records");
 string blocked=Path.Combine(auditRoot,"blocked");File.WriteAllText(blocked,"keep");var failure=new MapAuditJournal(Path.Combine(blocked,"audit.json"));failure.Capture(Entry("retry"));failure.ReleaseAfterRound();failure.WaitForReleasedWrites();
 Check(failure.Pending==1&&failure.Error is not null&&File.ReadAllText(blocked)=="keep","IO failure retains queued batch and existing data");
 File.Delete(blocked);failure.ReleaseAfterRound();failure.WaitForReleasedWrites();Check(failure.Pending==0&&failure.Error is null,"later boundary retries successfully");
 var cap=new MapAuditJournal(Path.Combine(auditRoot,"bounded.json"));for(int n=0;n<=MapAuditJournal.Limit;n++)cap.Capture(Entry("cap"+n));
 Check(cap.Pending==MapAuditJournal.Limit&&cap.Dropped==1&&cap.Error is not null,"RAM cap is explicit, not silent loss");
}
finally {Directory.Delete(auditRoot,true);}

using(var w=new World(settings))
{
 var old=MapPolicy.CaptureMeasurement(new MapEntry{Id="de_old",Name="Old"},16,16,2,0);old=MapPolicy.ApproveLimits(old,old.LimitProposal!.Signature);
 var fresh=MapPolicy.CaptureMeasurement(new MapEntry{Id="de_new",Name="New"},16,16,2,0);
 w.Set("maps",new List<MapEntry>{old,fresh});w.Set("installed",new HashSet<string>{old.Id,fresh.Id});w.Call("LoadNewMapActivations");
 var activation=w.Get<NewMapActivationState>("newMapActivations");w.Call("TrackNewMapActivations");
 Check(activation.Markers.Snapshot().Count==0,"Existing pool grandfathered and unapproved map not marked early");
 var approved=MapPolicy.ApproveLimits(fresh,fresh.LimitProposal!.Signature);
 w.Set("installed",new HashSet<string>{old.Id});w.Call("SaveCatalog",new List<MapEntry>{old,approved});
 Check(activation.Markers.Snapshot().Count==0,"Approval before installation cannot start NEW");
 w.Set("installed",new HashSet<string>{old.Id,fresh.Id});
 foreach(var blocked in new[]{approved with{InPool=false},approved with{Enabled=false},approved with{Removed=true},approved with{IsMap=false}})
 {
  w.Set("maps",new List<MapEntry>{old,blocked});w.Call("TrackNewMapActivations");Check(activation.Markers.Snapshot().Count==0,"Pool membership/manual disable/removal/non-map blocks activation");
 }
 w.Set("maps",new List<MapEntry>{old,approved});
 foreach(var disabled in new[]{settings with{Enabled=false},settings with{MeasurementOnly=true},settings with{ExcludedWorkshopIds=[fresh.Id]}})
 {w.Set("settings",disabled);w.Call("TrackNewMapActivations");Check(activation.Markers.Snapshot().Count==0,"Inactive mode and excluded map block activation");}
 w.Set("settings",settings);w.Call("SaveCatalog",new List<MapEntry>{old,approved});
 var started=activation.Markers.Snapshot()[fresh.Id];
 Check(activation.Markers.IsNew(fresh.Id,DateTimeOffset.UtcNow),"Catalog readiness automatically marks new map despite current low human count");
 Check(w.Get<List<MapEntry>>("maps").SequenceEqual(new[]{old,approved}),"Activation cannot change approval, limits or pool rules");
 w.Call("SaveCatalog",new List<MapEntry>{old});w.Call("SaveCatalog",new List<MapEntry>{old,approved});
 Check(activation.Markers.Snapshot()[fresh.Id]==started,"Removal and re-add preserve original activation");
 typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);w.Call("LoadNewMapActivations");
 Check(w.Get<NewMapActivationState>("newMapActivations").Markers.Snapshot()[fresh.Id]==started,"Actual persisted lifecycle reload preserves deadline");
 Check(!w.Get<NewMapMarkers>("newMapMarkers").IsNew(old.Id,DateTimeOffset.UtcNow),"Reload cannot retroactively label existing pool");
 File.WriteAllText(Path.Combine(w.Menu.DataDirectory,"new-map-activations.json"),"{broken");w.Call("LoadNewMapActivations");
 Check(w.Get<string?>("newMapMarkerError") is not null,"Malformed authoritative state fails closed instead of restarting NEW");
}
var nomSession=new NominationSession("process-1","de_dust2","",12);
var nomSaved=new NominationState(1,nomSession,new(){[1]="de_cbble",[2]="123"});
var nomParsed=NominationState.Parse(JsonSerializer.Serialize(nomSaved));
Check(nomParsed.Restore(nomSession).Count==2,"Nomination snapshot survives serialized plugin update");
foreach(var changed in new[]{nomSession with{Process="process-2"},nomSession with{Map="de_mirage"},nomSession with{Workshop="42"},nomSession with{ServerCount=13}})
 Check(nomParsed.Restore(changed).Count==0,"Nominations never cross process/map/workshop/map-load epoch");
nomParsed.Restore(nomSession).Clear();Check(nomParsed.Entries.Count==2,"Restored dictionary is detached");
foreach(var bad in new[]{nomSaved with{Schema=2},nomSaved with{Entries=new(){[0]="de_cbble"}},nomSaved with{Entries=new(){[1]="../quit"}},nomSaved with{Entries=Enumerable.Range(1,129).ToDictionary(i=>(ulong)i,_=>"de_cbble")}})
 Reject(()=>NominationState.Parse(JsonSerializer.Serialize(bad)),"Malformed nomination file fails closed");
using(var w=new World(settings))
{
 var nominations=w.Get<Dictionary<ulong,string>>("nominations");nominations[w.Menu.Player.SteamID]="de_cbble";
 w.Call("SaveNominationSnapshot",nomSession);
 typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);
 nominations.Clear();w.Call("RestoreNominationSnapshot",nomSession);
 Check(nominations[w.Menu.Player.SteamID]=="de_cbble"&&w.Get<int>("restoredNominations")==1,"Real plugin persistence restores the accepted nomination");
 var cb=MapPolicy.CaptureMeasurement(new MapEntry{Id="de_cbble",Name="Cobblestone"},16,16,0,0);cb=MapPolicy.ApproveLimits(cb,cb.LimitProposal!.Signature);
 var other=cb with{Id="de_other",Name="Other"};w.Set("maps",new List<MapEntry>{other,cb});w.Set("installed",new HashSet<string>{other.Id,cb.Id});
 var reasonType=typeof(Plugin).GetNestedType("Reason",BindingFlags.NonPublic)!;
 Check((bool)w.Call("BeginVote",Enum.Parse(reasonType,"Rtv"),null,null)!,"Vote starts after restoration");
 Check(w.Get<List<MapEntry>>("candidates").First().Id=="de_cbble"&&w.Get<HashSet<string>>("nominatedCandidates").Contains("de_cbble"),"Restored Cobblestone is the first actual vote candidate");
 w.Call("RestoreNominationSnapshot",nomSession with{ServerCount=13});Check(nominations.Count==0,"Same map loaded again clears old nominations");
 w.Set("nominationStateReady",false);
}
using(var w=new World(settings))
{
 w.Set("nominationStateReady",false);
 var path=Path.Combine(w.Menu.DataDirectory,"nominations.json");File.WriteAllText(path,JsonSerializer.Serialize(nomSaved));
 var before=File.ReadAllText(path);w.Call("InitializeNominations",new object?[]{null});
 Check(!w.Get<bool>("nominationStateReady")&&w.Get<string?>("nominationStateError") is null&&File.ReadAllText(path)==before,"Cold start waits without corrupting previous session or reporting an error");
 w.Call("InitializeNominations",nomSession with{Process="new-process"});
 Check(w.Get<bool>("nominationStateReady")&&w.Get<Dictionary<ulong,string>>("nominations").Count==0,"Later map readiness initializes and rejects previous-process nominations");
 var nominations=w.Get<Dictionary<ulong,string>>("nominations");nominations[1]="de_cbble";w.Call("SaveNominations");
 typeof(Plugin).Assembly.GetType("LPLK.Common.DeferredJson")!.GetMethod("Drain")!.Invoke(null,null);
 Check(NominationState.Parse(File.ReadAllText(path)).Entries[1]=="de_cbble","Unload persistence uses cached session even when native globals are unavailable");
 w.Set("nominationStateReady",false);File.WriteAllText(path,"{broken");w.Call("InitializeNominations",nomSession);
 Check(w.Get<bool>("nominationStateBlocked")&&!w.Get<bool>("nominationStateReady"),"Malformed state remains blocked separately from engine readiness");
 w.Call("InitializeNominations",nomSession with{ServerCount=99});Check(File.ReadAllText(path)=="{broken","Map readiness retry cannot overwrite corrupt state");
}
LunacyChecks.Run(Check,settings);
PromotionChecks.Run(Check);
using(var w=new World(settings))
{
 var maps=Enumerable.Range(1,11).Select(n=>new MapEntry{Id="de_menu"+n,Name="Menükarte "+n,MinPlayers=1,MaxPlayers=30}).ToList();
 w.Set("candidates",maps);w.Call("ShowVote",w.Menu.Player);
 Check(w.Menu.Current!.Configuration.Title.Contains("LPLK · Nächste Map"),"Actual mapvote title preserves umlaut and separator");
 w.Call("Selection",w.Menu.Player,"Nominierung",maps,(Action<SwiftlyS2.Shared.Players.IPlayer,MapEntry>)((_,_)=>{}),false,(Action<SwiftlyS2.Shared.Players.IPlayer>)(_=>{}),0);
 Check(w.Menu.Current!.Configuration.Title.Contains("Nominierung · 1/2"),"Actual nomination page separator preserved");
 Check(w.Menu.Labels.Any(label=>label.Contains("Zurück"))&&w.Menu.Labels.Any(label=>label.Contains("Menükarte")),"Actual nomination rows preserve Unicode and back label");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,result="passed",nativeEngineMocked=true,clientAcceptancePending=true}));

sealed class World:IDisposable
{
 public MenuWorld Menu=new(){DataDirectory=Path.Combine(Path.GetTempPath(),"LplkMapParity-"+Guid.NewGuid().ToString("N"))};
 public Plugin Plugin;public List<string> Broadcasts=[];public Dictionary<string,object> Limits=new(){["mp_timelimit"]=0f,["mp_maxrounds"]=0,["mp_winlimit"]=0};public string? IgnoreSet;public string Workshop="";
 public World(MapSettings settings)
 {
  Directory.CreateDirectory(Menu.DataDirectory);Menu.OnlinePlayers=[Menu.Player];
  Menu.ServiceOverride=(m,a)=>
  {
   if(m.Name=="Find"&&a?[0] is string name&&Limits.ContainsKey(name))return(true,Proxy.Make(m.ReturnType,(op,values)=>op.Name switch{"get_Value"=>Limits[name],"set_Value"=>SetLimit(name,values![0]!),_=>Proxy.Empty(op.ReturnType)}));
   if(m.Name=="SendChat"){Broadcasts.Add((string)a![0]!);return(true,null);}
   if(m.Name=="get_GlobalVars")throw new InvalidOperationException("No native globals in fixture");
   if(m.Name=="get_WorkshopId")return(true,Workshop);
   if(m.Name=="CloseMenuForPlayer"){if(Menu.Current==a![1])Menu.Current=null;return(true,null);}
   return(false,null);
  };
  Plugin=new(Menu.Core);Set("settings",settings);Set("nominationStateReady",true);Set("module",new ModuleSupport(Menu.Core,"LPLK_Maps","Maps","lplk.maps.admin",_=>{}){Active=true});Set("warmup",false);Call("StartAudit");
 }
 private object? SetLimit(string name,object value){if(name!=IgnoreSet)Limits[name]=value;return null;}
 public void Set(string name,object? value)=>typeof(Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Plugin,value);
 public T Get<T>(string name)=>(T)typeof(Plugin).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Plugin)!;
 public object? Call(string name,params object?[] args)=>typeof(Plugin).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Plugin,args);
 public void Dispose(){Get<MapAuditJournal>("mapAudit").WaitForReleasedWrites();Plugin.Unload();Directory.Delete(Menu.DataDirectory,true);}
}
