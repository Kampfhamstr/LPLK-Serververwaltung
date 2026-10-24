using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using LPLK.Common;
using LPLK_Economy;
using LPLK_Maps;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using Economy=LPLK_Economy.Plugin;
using Maps=LPLK_Maps.Plugin;
using Admin=LPLK_Admin.Plugin;

int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
var flags=BindingFlags.NonPublic|BindingFlags.Instance;
object? Call(object p,string name,params object?[] a){try{return p.GetType().GetMethod(name,flags)!.Invoke(p,a);}catch(TargetInvocationException e){ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
void Set(object p,string name,object? value)=>p.GetType().GetField(name,flags)!.SetValue(p,value);
T Get<T>(object p,string name)=>(T)p.GetType().GetField(name,flags)!.GetValue(p)!;
void Reject(Action a,string message){bool rejected=false;try{a();}catch(Exception e)when(e is BankRejected or InvalidDataException){rejected=true;}Check(rejected,message);}
string build=Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!;
string suite=Assembly.GetExecutingAssembly().GetName().Name!;
string root=Path.Combine(build,suite,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);

foreach(var (text,value) in new[]{("2500",2500L),("2.500",2500),("2,5k",2500),("1M",1000000),("1.234.567",1234567),(" 10 ",10),("1,000",1),("9000000000000000",9000000000000000)})
    Check(MenuAmount.Parse(text)==value,"Exact amount: "+text);
foreach(string text in new[]{"", "0","-1","+1","1.5","1,5","1e3","1 000","1.23.456","nan","1k; quit","99999999999999999999999999999999","9000000000000001","999999999999b"})Reject(()=>MenuAmount.Parse(text),"Invalid amount: "+text);
Check(MenuAmount.Parse("0",0,100)==0,"Admin set can use zero");Reject(()=>MenuAmount.Parse("101",0,100),"Caller maximum enforced");
Check(MenuPresentation.AlignAction("Zurück")=="Zurück"&&MenuPresentation.AlignAction("Weiter")=="Weiter","Navigation remains centered");
Check(MenuPresentation.AlignAction("1. Kurz").EndsWith('\u00a0')&&!MenuPresentation.AlignAction("1. Kurz").StartsWith('\u00a0'),"Action padding is right-only");
var grouped=MenuRows.Join(("Wetten",new (string,Action<IPlayer>)[]{("Aktiv: Ja",_=>{})}),("Kopfgeld",new (string,Action<IPlayer>)[]{("Aktiv: Nein",_=>{})}));
Check(grouped[0].Item1=="Wetten · Aktiv: Ja"&&grouped[1].Item1=="Kopfgeld · Aktiv: Nein","Flattened duplicate labels retain context");

var ew=new MenuWorld{DataDirectory=Path.Combine(root,"economy"),RejectEntityAccess=true};
Delegate? chat=null;var queue=new Queue<Action>();bool defer=false;Guid hook=Guid.NewGuid();bool unhooked=false;
ew.ServiceOverride=(m,a)=>m.Name switch
{
    "HookClientChat"=>(true,CaptureChat((Delegate)a![0]!)),
    "UnhookClientChat"=>(true,Unhook((Guid)a![0]!)),
    "NextWorldUpdate" when defer=>(true,Queue((Action)a![0]!)),_ =>(false,null)
};
Guid CaptureChat(Delegate d){chat=d;return hook;}
object? Unhook(Guid g){unhooked=g==hook;return null;}
object? Queue(Action a){queue.Enqueue(a);return null;}
var economy=new Economy(ew.Core);economy.Load(false);Check(chat is not null,"Chat handler registered");
long accepted=-1;int acceptedCount=0,returned=0;
void Prompt(bool admin=false)=>Call(economy,"AmountInput",ew.Player,"Prüfbetrag",(Action<IPlayer,long>)((p,n)=>{accepted=n;acceptedCount++;ew.Current=null;}),(Action<IPlayer>)(_=>{returned++;ew.Current=null;}),admin,1L,1000000L);
HookResult Say(string text,bool team=false)=>(HookResult)chat!.DynamicInvoke(1,text,team)!;
Prompt();Check(ew.Current!.OptionScrollStyle==MenuOptionScrollStyle.WaitingCenter,"Input uses nonwrapping first page");
Check(Say("2,5k",true)==HookResult.Stop&&accepted==2500&&acceptedCount==1,"Team chat input privately accepted once");
Check(Say("2,5k")==HookResult.Continue&&acceptedCount==1,"Repeated chat cannot repeat amount");
Prompt();Check(Say("2,5")==HookResult.Stop&&acceptedCount==1,"Fractional dollars rejected privately");Check(Say("100")==HookResult.Stop&&accepted==100,"Invalid input can be corrected");
Prompt();Check(Say("abbrechen")==HookResult.Stop&&returned==1,"Cancellation returns without action");
Prompt();Check(Say("!rtv")==HookResult.Continue&&Say("200")==HookResult.Continue,"Other commands release capture");
Prompt();ew.Session++;Check(Say("200")==HookResult.Continue,"Reconnect cannot use previous input");
Prompt();ew.Current=null;Check(Say("200")==HookResult.Continue,"Closed menu releases chat");
Prompt();defer=true;int count=acceptedCount;Check(Say("300")==HookResult.Stop&&Say("400")==HookResult.Stop,"Concurrent chat stays private");Check(queue.Count==1,"Only one queued acceptance");ew.Session++;queue.Dequeue()();defer=false;Check(acceptedCount==count,"Session rechecked on game thread");
ew.Permissions.Add("lplk.economy.admin");Prompt(true);defer=true;Say("500");ew.Permissions.Clear();queue.Dequeue()();defer=false;Check(acceptedCount==count,"Admin permission rechecked after scheduling");
Prompt();var requests=Get<object>(economy,"amountRequests");var request=requests.GetType().GetProperty("Item")!.GetValue(requests,[1])!;
request.GetType().GetField("Expires")!.SetValue(request,Environment.TickCount64-1);Check(Say("600")==HookResult.Continue,"Expired capture releases ordinary chat");
Prompt();request=requests.GetType().GetProperty("Item")!.GetValue(requests,[1])!;request.GetType().GetField("Period")!.SetValue(request,-1L);Check(Say("600")==HookResult.Stop&&acceptedCount==count,"Changed bank period cannot use old amount prompt");

economy.BankCommand(ew.Context);Check(ew.Labels.Any(l=>l.Contains("Einzahlen"))&&ew.Labels.Any(l=>l.Contains("Bankraub")),"Bank actions and robbery are direct");
await ew.Click("Einzahlen");Check(ew.Labels.Any(l=>l.Contains("Eigener Betrag")),"Cash menu exposes chat entry");await ew.Click("Eigener Betrag");Check(Say("abc")==HookResult.Stop,"Wired amount capture suppresses invalid private text");
economy.Unload();Check(unhooked&&Say("42")==HookResult.Continue,"Unload releases chat capture");

var aw=new MenuWorld{DataDirectory=Path.Combine(root,"admin")};var adminPlugin=new Admin(aw.Core);adminPlugin.Load(false);adminPlugin.UseSharedInterface(aw.Interfaces);
adminPlugin.PlayerMenuCommand(aw.Context);Check(aw.Labels.Any(l=>l.Contains("Mapmenü")),"Normal menu links mapmenu");await aw.Click("Mapmenü");
Check(aw.Labels.Any(l=>l.Contains("RTV"))&&aw.Labels.Any(l=>l.Contains("Nominieren")),"Player map menu exposes both actions");await aw.Click("RTV");Check(aw.Commands.Last()=="sw_rtv","RTV executes with player authority");
aw.MapsEnabled=false;int commands=aw.Commands.Count;await aw.Click("Nominieren");Check(aw.Commands.Count==commands&&aw.Messages.Last().Contains("nicht verfügbar"),"Unavailable player function cannot execute");
adminPlugin.MapMenuCommand(aw.Context);Check(aw.Labels.Any(l=>l.Contains("RTV")),"Direct mapmenu remains informative while voting disabled");
Check(aw.Current!.OptionScrollStyle==MenuOptionScrollStyle.WaitingCenter&&aw.Current.Configuration.MaxVisibleItems==5,"Public layout uses five nonwrapping rows");
adminPlugin.Unload();

var mw=new MenuWorld{DataDirectory=Path.Combine(root,"maps"),RejectEntityAccess=true};mw.Permissions.Add("lplk.maps.admin");
mw.ServiceOverride=(m,a)=>m.Name switch{"get_GlobalVars"=>throw new NotSupportedException(),"get_WorkshopId"=>(true,""),"CloseMenuForPlayer"=>(true,null),_=>(false,null)};
var underlying=mw.Core;mw.Core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name=="get_PluginPath"?mw.DataDirectory:m.Invoke(underlying,a));
Directory.CreateDirectory(mw.DataDirectory);string configPath=Path.Combine(mw.DataDirectory,"addons/swiftlys2/configs/plugins/LPLK_Maps/config.jsonc");Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
string workshop=Path.Combine(root,"workshop");Directory.CreateDirectory(workshop);
File.WriteAllText(configPath,JsonSerializer.Serialize(new{LPLK_Maps=new MapSettings{AutoMeasureEmptyServer=false,WorkshopPath=workshop,MinimumFreeDiskGiB=5,MeasurementOnly=true}}));
File.WriteAllText(Path.Combine(mw.DataDirectory,"maps.json"),JsonSerializer.Serialize(new[]{new MapEntry{Id="de_fixture",Name="Fixture"}}));var mapsPlugin=new Maps(mw.Core);mapsPlugin.Load(false);Set(mapsPlugin,"collectionLoading",true);
MapEntry Measured(string id,int sites){var m=MapPolicy.CaptureMeasurement(new MapEntry{Id=id,Name=id},16,16,sites,0);return MapPolicy.ApproveLimits(m,m.LimitProposal!.Signature);}
var catalog=Enumerable.Range(0,23).Select(i=>Measured("de_small"+i.ToString("00"),1)).Concat(Enumerable.Range(0,11).Select(i=>Measured("de_large"+i.ToString("00"),2))).Append(Measured("de_blocked",1) with{Removed=true}).ToList();Set(mapsPlugin,"maps",catalog);
Call(mapsPlugin,"MapChangeGroups",mw.Player);await mw.Click("1–max. Slots");
Check(mw.Labels.Count==12&&mw.Labels.Count(l=>l.Contains("de_small"))==10,"First map page: ten maps plus next/back");Check(!mw.Labels.Any(l=>l.Contains("de_large")||l.Contains("de_blocked")),"Min1 group excludes min6 and blocked maps");
await mw.Click("Weiter");Check(mw.Labels.Count(l=>l.Contains("de_small"))==10&&mw.Labels[0].Contains("11."),"Second page keeps numbering");await mw.Click("Weiter");Check(mw.Labels.Count(l=>l.Contains("de_small"))==3&&!mw.Labels.Any(l=>MenuPresentation.Navigation(l)&&l.Contains("Weiter")),"Final map page has no phantom next");
await mw.Click("Zurück");await mw.Click("6–max. Slots");Check(mw.Labels.Count(l=>l.Contains("de_large"))==10&&!mw.Labels.Any(l=>l.Contains("de_small")),"Min6 group independently paged");
Set(mapsPlugin,"maps",new List<MapEntry>{new(){Id="123",Name="Eigener Kartenname",Enabled=false,InPool=false}});var backend=new FakeWorkshop();Set(mapsPlugin,"workshopBackend",backend);
async Task Begin(string id)
{
    backend.Request=new(TaskCreationOptions.RunContinuationsAsynchronously);Call(mapsPlugin,"StartWorkshopTest",mw.Player,id);
    for(int n=0;n<200&&!backend.FetchCalled;n++)await Task.Delay(5);
}
async Task FinishMetadata(WorkshopItem item)
{
    backend.FetchCalled=false;backend.Request.SetResult(item);
    for(int n=0;n<200;n++){var job=Get<object>(mapsPlugin,"testDownload");string status=(string)job.GetType().GetField("Status")!.GetValue(job)!;if(status!="Workshop wird geprüft")return;await Task.Delay(5);}
    throw new Exception("Metadata callback timed out");
}
bool Job(string field){var job=Get<object>(mapsPlugin,"testDownload");return (bool)job.GetType().GetField(field)!.GetValue(job)!;}
await Begin("123");await FinishMetadata(new("123","Testmap",true,1024));Check(backend.Downloads==1&&!Job("Ready")&&Get<object?>(mapsPlugin,"pending") is null,"Download starts without changing map");
Check(Get<List<MapEntry>>(mapsPlugin,"maps").Single(m=>m.Id=="123") is {Name:"Eigener Kartenname",Enabled:false,InPool:false},"Map test preserves custom catalog name and pool flags");
Check(!mw.Labels.Any(l=>l.Contains("Starten")),"Start hidden during download");
backend.State=new(true,false,false,1024,1024);Call(mapsPlugin,"PollWorkshopTest",Environment.TickCount64,true);Check(!Job("Ready"),"Installed flag alone cannot complete download");
backend.Complete!("123",true,"OK");Check(!Job("Ready"),"Completion callback without map file cannot complete download");
WriteMap(Path.Combine(workshop,"123","test.vpk"));Call(mapsPlugin,"PollWorkshopTest",Environment.TickCount64,true);Check(Job("Ready")&&mw.Labels.Any(l=>l.Contains("Starten")),"Callback plus state plus VPK enables explicit start");
Check(Get<object?>(mapsPlugin,"pending") is null,"Completed download still does not auto-start");await mw.Click("Starten");var confirm=mw.Find("Bestätigen");mw.Permissions.Clear();await mw.Click(confirm);Check(Get<object?>(mapsPlugin,"pending") is null,"Start confirmation rechecks admin rights");mw.Permissions.Add("lplk.maps.admin");
backend.State=new(true,true,true,500,1024);await mw.Click(confirm);Check(Get<object?>(mapsPlugin,"pending") is null,"New update invalidates stale start confirmation");backend.State=new(true,false,false,1024,1024);await mw.Click(confirm);Check(Get<object?>(mapsPlugin,"pending") is not null,"Explicit fresh confirmation schedules map change");Set(mapsPlugin,"pending",null);
await Begin("456");mw.Permissions.Clear();await FinishMetadata(new("456","Unauthorized",true,1));Check(Job("Failed")&&backend.Downloads==1,"Rights revoked during metadata query prevent download");mw.Permissions.Add("lplk.maps.admin");
await Begin("789");await FinishMetadata(new("789","Not a map",false,1));Check(Job("Failed")&&backend.Downloads==1,"Non-map Workshop item refused");
await Begin("321");backend.State=new(false,false,false,0,100);await FinishMetadata(new("321","Failure",true,100));backend.Complete!("321",false,"Timeout");Check(Job("Failed")&&!Job("Ready"),"Failed Steam transfer cannot start");
mapsPlugin.Unload();Check(backend.Disposed,"Unload disposes download callbacks");
Check(ew.Errors.Count==0&&mw.Errors.Count==0&&aw.Errors.Count==0,"No swallowed game-thread exceptions");
var report=new{checkedAt=DateTimeOffset.UtcNow,checks,scope="Real plugin menus, private chat hooks, asynchronous metadata/Steam adapter, catalog and VPK; no native client rendering"};
File.WriteAllText(Path.Combine(build,suite,"result.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));

void WriteMap(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var tree=new MemoryStream();using(var w=new BinaryWriter(tree,Encoding.UTF8,true)){w.Write(Encoding.UTF8.GetBytes("vmap_c\0maps\0de_workshop_test\0"));w.Write(0u);w.Write((ushort)0);w.Write((ushort)0x7fff);w.Write(0u);w.Write(0u);w.Write((ushort)0xffff);w.Write(new byte[3]);}
    using var file=File.Create(path);using var bw=new BinaryWriter(file);bw.Write(0x55aa1234u);bw.Write(1u);bw.Write((uint)tree.Length);bw.Write(tree.ToArray());
}
sealed class FakeWorkshop:IWorkshopTestBackend
{
    public bool Connected=>true;public bool FetchCalled,Disposed;public int Downloads;public WorkshopTransfer State=new(false,false,true,0,1024);
    public TaskCompletionSource<WorkshopItem> Request=new();public Action<string,bool,string>? Complete;
    public Task<WorkshopItem> Fetch(string id,CancellationToken token){FetchCalled=true;return Request.Task.WaitAsync(token);}
    public WorkshopTransfer Read(string id)=>State;
    public bool Download(string id,Action<string,bool,string> completed){Downloads++;Complete=completed;return true;}
    public void Dispose()=>Disposed=true;
}
