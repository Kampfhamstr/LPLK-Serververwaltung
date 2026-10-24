using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Statistics;
using LPLK_Demos;
using SwiftlyS2.Shared;
using SwiftlyS2.Core.Menus.OptionsBase;
int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object? v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
void Refresh(object p){Set(p,p is LPLK_Statistics.Plugin?"nextStatus":"nextTick",0L);p.GetType().GetMethod("Tick")!.Invoke(p,null);}
object? Invoke(object p,string n,params object?[] a)=>p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(p,a);
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"RecordingMaintenanceChecks","test-data");
MenuWorld World<T>(string name,T settings){var w=new MenuWorld{DataDirectory=Path.Combine(root,name),RejectEntityAccess=true};Directory.CreateDirectory(w.DataDirectory);AtomicJson.Write(Path.Combine(w.DataDirectory,"settings.json"),settings);w.Permissions.Add("lplk.admin");return w;}
ISwiftlyCore Core(MenuWorld w)
{
 object? Hooks(MethodInfo m,object?[]? a)=>m.ReturnType.IsInterface?Proxy.Make(m.ReturnType,Hooks):Proxy.Empty(m.ReturnType);
 return Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_GameHooks"?Proxy.Make(m.ReturnType,Hooks):m.Invoke(w.Core,a));
}
async Task Click(MenuWorld w,string text){for(int page=0;page<5;page++){try{await w.Click(w.FindExact(text));return;}catch(InvalidOperationException)when(w.Labels.Any(l=>l.Contains("Nächste Seite"))){await w.Click(w.FindExact("Nächste Seite"));}}throw new Exception("Missing menu: "+text);}
void Input(MenuWorld w,string text,bool accepted=true)=>Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,text)==accepted,"Input validation "+text);
string PathFor(MenuWorld w)=>Path.Combine(w.DataDirectory,"settings.json");
var sw=World("statistics",new StatisticsSettings());var data=new StatsData();data.Players[12]=new(){Name="fixture",LastSeen=DateTimeOffset.Parse("2026-10-01T00:00:00Z"),Pvp=new(){Shots=500,Kills=50,Headshots=20},Bots=new(){Shots=200,Hits=100,Kills=20},Weapons=new(){["ak47"]=13},Hud=false,Extra=new(){BombsPlanted=3,BombsDefused=2}};
string statsPath=Path.Combine(sw.DataDirectory,"statistics.json");AtomicJson.Write(statsPath,data);string originalData=File.ReadAllText(statsPath);
var stats=new LPLK_Statistics.Plugin(Core(sw));stats.Load(false);Refresh(stats);Check(sw.EntityAccesses==0,"Cold tick avoids native rules");stats.MapLoad(null!);stats.MapUnload(null!);Refresh(stats);Check(sw.EntityAccesses==0,"Map-unload tick avoids native rules");
StatisticsSettings S()=>Field<StatisticsSettings>(stats,"settings");var shots=Field<ShotLedger>(stats,"shots");shots.Fire("session","ak47",100,true);
File.WriteAllText(PathFor(sw),JsonSerializer.Serialize(S()));Refresh(stats);Check(shots.Hurt("session","ak47",101,false,true).Hit,"Formatting-only edit preserves pending shot");
stats.Status(sw.Context);await Click(sw,"Topplätze: 10");Input(sw,"101",false);Input(sw,"100");Check(S().TopCount==100,"Full ranking range");
await Click(sw,"PvP-Schüsse: 250");Input(sw,"2147483647");Check(S().MinShots==int.MaxValue,"Original positive integer range preserved");
stats.Status(sw.Context);await Click(sw,"Kopfquote: 0.35");Input(sw,"0,45");Check(S().HeadshotWeight==.45,"Comma decimal for ranking weight");
stats.Status(sw.Context);await Click(sw,"Punktegrenze · Kill-Effizienz: 0.2");Input(sw,"0",false);Input(sw,"0.3");Check(S().FullKillEfficiency==.3,"Positive score denominator");
stats.Status(sw.Context);await Click(sw,"Anzeigezeit (ms): 2500");Input(sw,"10000");await Click(sw,"Unbekanntes Feuer: molotov");Check(S().UnknownInferno=="incgrenade","Original fallback choices accessible");
stats.Status(sw.Context);await Click(sw,"Teamschaden: False");Check(S().ShowFriendlyDamage,"Previously config-only damage choice");
stats.Status(sw.Context);AtomicJson.Write(PathFor(sw),S() with{PvpEnabled=false,MinBotKills=333});await Click(sw,"PvP: True");Check(S() is {PvpEnabled:true,MinBotKills:333},"Stale toggle preserves newer independent value");
shots.Fire("another","ak47",100,true);DateTime stamp=File.GetLastWriteTimeUtc(PathFor(sw));AtomicJson.Write(PathFor(sw),S() with{TopCount=87});File.SetLastWriteTimeUtc(PathFor(sw),stamp);Refresh(stats);Check(S().TopCount==87&&!shots.Hurt("another","ak47",101,false,true).Hit,"Same timestamp rule change clears only transient attribution");
Check(File.ReadAllText(statsPath)==originalData,"Durable player counters/preferences remain byte-identical");
Set(stats,"weaponExportTask",Task.FromResult(1L));Set(stats,"weaponCapture",new WeaponRankingCapture(data,S()));stats.Status(sw.Context);await Click(sw,"PvP: True");Check(Field<WeaponRankingCapture?>(stats,"weaponCapture") is null&&Field<long>(stats,"nextSave")==0,"Configuration change discards pending old capture and schedules fresh export");
var dw=World("demos",new DemoSettings());AtomicJson.Write(Path.Combine(dw.DataDirectory,"index.json"),new List<DemoRecord>());var demos=new LPLK_Demos.Plugin(dw.Core);demos.Load(false);Check(dw.EntityAccesses==0,"Demo cold load avoids native world");Set(demos,"mapReady",true);
var archive=Field<DemoArchive>(demos,"archive");var rec=new DemoRecord{File="lplk_20261019_100000_0123456789abcdef0123456789abcdef.dem",State="recording",MinimumSeconds=5,DeleteUnrequested=true,Requested=true,Requests=1};archive.Records.Add(rec);archive.Save();File.WriteAllBytes(archive.PathFor(rec.File),"PBDEMS2\0fixture-data-demo"u8.ToArray());Set(demos,"recording",rec);Set(demos,"startAt",Environment.TickCount64-12000);Field<HashSet<ulong>>(demos,"requesters").Add(12);
DemoSettings D()=>Field<DemoSettings>(demos,"settings");
File.WriteAllText(PathFor(dw),JsonSerializer.Serialize(D()));Refresh(demos);Check(Field<DemoRecord?>(demos,"recording")==rec&&dw.Commands.Count==0,"Formatting-only edit does not stop active recording");
demos.Status(dw.Context);File.WriteAllText(PathFor(dw),"{\"RetentionHours\":0}");await Click(dw,"Bereinigung: False");Check(Field<DemoRecord?>(demos,"recording")==rec&&rec.Requested&&dw.Commands.Count==0&&File.ReadAllText(PathFor(dw)).Contains(":0"),"Invalid admin edit preserves active/requested recording and invalid file");
AtomicJson.Write(PathFor(dw),D() with{PrintRequests=false});await Click(dw,"Mindestlänge (s): 5");Input(dw,"3600");Check(D() is {MinimumSeconds:3600,PrintRequests:false}&&rec.MinimumSeconds==5&&rec.Requested,"Current recording retains start rules; input preserves external flag");
Check(Field<DemoRecord?>(demos,"recording") is null&&dw.Commands.SequenceEqual(new[]{"tv_stoprecord"})&&rec.State=="finalizing","Valid rule change ends recording once through existing engine command");
long now=Environment.TickCount64;Field<Dictionary<string,(long Size,long Stable,long Deadline)>>(demos,"finalizing")[rec.File]=(new FileInfo(archive.PathFor(rec.File)).Length,now-4000,now+30000);Refresh(demos);Check(rec.State=="complete"&&File.Exists(archive.PathFor(rec.File))&&rec.Requests==1,"Requested recording finalizes using original minimum and request data");
await Click(dw,"Aufbewahrung (h): 48");Input(dw,"8760");demos.Status(dw.Context);await Click(dw,"Spielerschwelle: 1");Input(dw,"65",false);Input(dw,"64");await Click(dw,"Wartezeit (s): 300");Input(dw,"86400");Check(D() is {IdleSeconds:86400,IdlePlayerThreshold:64,RetentionHours:8760},"Full existing recorder ranges exposed");
demos.Status(dw.Context);await Click(dw,"Anfragehinweis: False");Check(D().PrintRequests,"Request announcement switch exposed");
foreach(var pair in new (object Plugin,MenuWorld World)[]{(stats,sw),(demos,dw)})
{
 string path=PathFor(pair.World),good=File.ReadAllText(path);
 foreach(string bad in new[]{"null","[]","{\"Unknown\":true}",new string(' ',65537)}){File.WriteAllText(path,bad);Refresh(pair.Plugin);Check(Field<string?>(pair.Plugin,"reloadError") is not null&&File.ReadAllText(path)==bad,"Invalid content rejected without overwrite");}
 File.WriteAllText(path,good);Refresh(pair.Plugin);Check(Field<string?>(pair.Plugin,"reloadError") is null,"Corrected file recovers");
 string status=Path.Combine(pair.World.DataDirectory,"status.json");File.Delete(status);Directory.CreateDirectory(status);Refresh(pair.Plugin);Check(Field<bool>(pair.Plugin,"statusWriteFailed"),"Status failure handled without aborting tick");Directory.Delete(status);Refresh(pair.Plugin);Check(!Field<bool>(pair.Plugin,"statusWriteFailed"),"Status recovery");
 foreach(var menu in pair.World.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"Compact menu");foreach(var back in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Unnumbered back");}
}
demos.Status(dw.Context);await Click(dw,"Mindestlänge (s): 3600");var stale=(InputMenuOption)dw.Current!.Options[0];string configBytes=File.ReadAllText(PathFor(dw));dw.Permissions.Clear();stale.SetValue(dw.Player,"8");Check(File.ReadAllText(PathFor(dw))==configBytes,"Revoked permission blocks input");dw.Permissions.Add("lplk.admin");dw.Session++;stale.SetValue(dw.Player,"9");Check(File.ReadAllText(PathFor(dw))==configBytes,"Changed session blocks stale input");
bool badWeight=false;try{StatisticsSettings.Validate(S() with{HeadshotWeight=double.MaxValue,VolumeWeight=double.MaxValue});}catch(InvalidDataException){badWeight=true;}Check(badWeight,"Overflowing weight sum cannot produce invalid rank");
Check(double.IsFinite(StatPolicy.Rating(new(){Shots=5000,Kills=1000,Headshots=1000},new StatisticsSettings{HeadshotWeight=1e308,KillEfficiencyWeight=0,HeadshotEfficiencyWeight=0,VolumeWeight=0})),"Finite large weights avoid intermediate multiplication overflow");
stats.Unload();demos.Unload();Check(File.ReadAllText(statsPath)==originalData&&archive.Records.Single().Requested&&sw.EntityAccesses==0&&dw.EntityAccesses==0,"Counters and demo request survive unload with no native probes");
Console.WriteLine($"RecordingMaintenanceChecks: {checks} passed; real menu/reload/file paths, preserved counters and requested recording; native engine commands captured only.");
