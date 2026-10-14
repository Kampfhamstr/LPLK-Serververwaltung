using LPLK_NadeEffects;
using System.Text.Json;
using System.Reflection;
using SwiftlyS2.Shared.Commands;

int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
var s=new NadeSettings();NadeSettings.Validate(s);
foreach(var invalid in new[]{s with{FadeDuration=double.NaN},s with{FadeHoldTime=double.PositiveInfinity},s with{FadeAlpha=256},s with{TriggerCooldownSeconds=0},s with{FadeColor="255 255 256"},s with{FadeColor="255 255"},s with{EnvFadeActivatorOnly=false},s with{AdminPermission=""},s with{WarnMessage="x\ny"},s with{SelectButton="Tab"}})
{bool failed=false;try{NadeSettings.Validate(invalid);}catch(InvalidDataException){failed=true;}Check(failed,"Invalid setting rejected");}
Check(!NadePolicy.Configured(s,"weapon_awp")&&!NadePolicy.Configured(s,"knife")&&!NadePolicy.Configured(s,"hegrenade_projectile"),"Guns/knives/projectiles excluded");
Check(NadePolicy.Configured(s," Weapon_HEGrenade "),"Normalize item event prefixes/case");
var w=new World();var c=new NadeController(w,w.Errors.Add);var a=w.Players[1].Owner.Connection;var b=w.Players[2].Owner.Connection;
Check(!c.Equip(2,"hegrenade",s,0),"No effect without explicit selection");Check(c.Toggle(a,b)=="AN","Admin selects current player");
Check(c.Equip(2,"hegrenade",s,0)&&!c.Equip(2,"flashbang",s,.01),"Duplicate event cannot enqueue twice");c.Tick(true,.02);Check(w.RemovalCalls==0,"Wait for safe post-equip time");c.Tick(true,.04);
Check(w.RemovalCalls==1&&w.Fades.Count==1&&w.Knife==1&&w.Inventory.SequenceEqual(["weapon_awp","weapon_knife"]),"Configured nades removed, other weapons retained, knife requested");
Check(!c.Equip(2,"hegrenade",s,1),"Cooldown stops repeated equip");c.Tick(true,3.1);Check(w.Fades.Count==0,"Fade expires and is destroyed");c.Clear();
string[] names=["flashbang","hegrenade","smokegrenade","molotov","incgrenade","decoy","tagrenade"];
string[] properties=["EnableFlashbang","EnableHEGrenade","EnableSmoke","EnableMolotov","EnableIncendiary","EnableDecoy","EnableTAGrenade"];
for(int mask=0;mask<128;mask++)
{
    var config=s with{};for(int bit=0;bit<7;bit++)typeof(NadeSettings).GetProperty(properties[bit])!.SetValue(config,(mask&(1<<bit))!=0);
    w.Reset();c.Clear();c.Toggle(a,b);int first=Enumerable.Range(0,7).FirstOrDefault(i=>(mask&(1<<i))!=0,-1);
    if(first<0){Check(!c.Equip(2,"hegrenade",config,0),"No enabled types => no effect");continue;}
    Check(c.Equip(2,names[first],config,0),"Enabled grenade triggers");c.Tick(true,.04);
    for(int bit=0;bit<7;bit++)Check(w.Inventory.Contains("weapon_"+names[bit])==((mask&(1<<bit))==0),"Only configured grenade types removed");
    Check(w.Inventory.Contains("weapon_awp")&&w.Inventory.Contains("weapon_knife"),"Unrelated inventory preserved");
}
c.Clear();
foreach(string change in new[]{"target-steam","target-session","pawn","team","dead","actor-session","rights","immunity","disabled","deselected","round","config"})
{
    w.Reset();c.Clear();c.Toggle(a,b);c.Equip(2,"hegrenade",s,0);var target=w.Players[2];var actor=w.Players[1];
    if(change=="target-steam")w.Players[2]=target with{Owner=target.Owner with{Connection=b with{Steam=99}}};
    if(change=="target-session")w.Players[2]=target with{Owner=target.Owner with{Connection=b with{Session=99}}};
    if(change=="pawn")w.Players[2]=target with{Owner=target.Owner with{Pawn=99}};
    if(change=="team")w.Players[2]=target with{Owner=target.Owner with{Team=3}};
    if(change=="dead")w.Players[2]=target with{Alive=false};
    if(change=="actor-session")w.Players[1]=actor with{Owner=actor.Owner with{Connection=a with{Session=99}}};
    if(change=="rights")w.Players[1]=actor with{Admin=false};
    if(change=="immunity")w.Players[2]=target with{Rank=100};
    if(change=="deselected")c.Toggle(a,b);
    if(change is "round" or "config")c.CancelEffects();
    c.Tick(change!="disabled",1);Check(w.RemovalCalls==0&&w.Fades.Count==0&&c.PendingCount==0,"Cancelled before mutation: "+change);
}
w.Reset();c.Clear();Check(c.Toggle(a,b with{Session=99}).Contains("geändert"),"Stale target selection rejected");
w.Players[1]=w.Players[1] with{Admin=false};Check(c.Toggle(a,b).Contains("geändert"),"Ordinary player cannot mark");
w.Reset();w.Players[2]=w.Players[2] with{Rank=100};Check(c.Toggle(a,b).Contains("geändert"),"Equal/higher immunity rejected");
w.Reset();c.Clear();c.Toggle(a,b);c.Equip(2,"flashbang",s with{RemoveNades=false},0);c.Tick(true,.04);Check(w.RemovalCalls==0&&w.Knife==0&&w.Fades.Count==1,"Warning/fade without inventory changes");c.Clear();
w.Reset();c.Toggle(a,b);c.Equip(2,"flashbang",s with{UseEnvFade=false,SwitchToKnifeAfterRemove=false},0);c.Tick(true,.04);Check(w.Fades.Count==0&&w.RemovalCalls==1&&w.Knife==0,"Independent effect controls");c.Clear();
w.Reset();c.Toggle(a,b);w.FailFade=true;c.Equip(2,"flashbang",s,0);c.Tick(true,.04);Check(w.Errors.Count==1&&w.RemovalCalls==1,"Fade failure does not hide removal outcome");c.Clear();
w.Reset();c.Toggle(a,b);w.FailRemove=true;c.Equip(2,"flashbang",s,0);c.Tick(true,.04);Check(w.Errors.Count==1&&c.PendingCount==0&&w.LastRemoved==-1,"Failed/partial removal reported as unknown, not zero/success, and not retried forever");c.Tick(true,5);Check(w.Fades.Count==0,"Fade cleaned after removal failure");c.Clear();
w.Reset();c.Toggle(a,b);for(int i=0;i<8;i++){c.Equip(2,"flashbang",s,i*1.5);c.Tick(true,i*1.5+.04);Check(c.FadeCount==1&&w.Fades.Count==1,"One fade per target despite rapid repeats");}c.Clear();
w.Reset();c.Toggle(a,b);c.Equip(2,"flashbang",s,0);c.Tick(true,.04);c.Disconnect(1);Check(c.Selected==0&&w.Fades.Count==0,"Selecting admin disconnect clears selection and resources");
w.Reset();c.Toggle(a,b);c.Equip(2,"flashbang",s,0);c.Tick(true,.04);int destroys=w.Destroyed;c.ForgetMap();Check(c.FadeCount==0&&w.Destroyed==destroys,"Map teardown avoids old native handles");c.Clear();
for(int count=0;count<=64;count++)
{var seen=new List<int>();int pages=NadePolicy.Page(count,0).Pages;for(int page=0;page<pages;page++){var p=NadePolicy.Page(count,page);var chunk=Enumerable.Range(0,count).Skip(p.Offset).Take(p.Size);seen.AddRange(chunk);Check(chunk.Count()+(page>0?1:0)+(page+1<pages?1:0)+1<=5,"Page row limit");}Check(seen.SequenceEqual(Enumerable.Range(0,count)),"Pagination preserves every player");}
Check(NativeNadeWorld.Html("<b>&") == "&lt;b&gt;&amp;"&&NativeNadeWorld.Chat("[red]x\ny")=="(red)xy","Untrusted display text escaped");
var menu=new MenuWorld();Directory.CreateDirectory(menu.DataDirectory);File.WriteAllText(Path.Combine(menu.DataDirectory,"settings.json"),JsonSerializer.Serialize(s));var plugin=new Plugin(menu.Core);plugin.Load(false);plugin.Administration(menu.Context);Check(menu.Current is null,"Non-admin menu denied");
menu.Permissions.Add("lplk.nadeeffects.admin");plugin.Administration(menu.Context);Check(menu.Labels.Count==4,"Root reserves central return");await menu.Click("Einstellungen");await menu.Click("Auslöser");await menu.Click("Granatentypen");
int visited=0;do{visited++;if(!menu.Labels.Any(x=>x.Contains("Nächste")))break;await menu.Click("Nächste");}while(visited<8);Check(visited==4,"Seven grenade types on four small pages");await menu.Click("Zurück");
var toggle=menu.Find("Granaten entfernen");menu.Permissions.Clear();await menu.Click(toggle);Check(JsonSerializer.Deserialize<NadeSettings>(File.ReadAllText(Path.Combine(menu.DataDirectory,"settings.json")))!.RemoveNades,"Revoked permission cannot write");menu.Permissions.Add("@css/root");await menu.Click(toggle);Check(!JsonSerializer.Deserialize<NadeSettings>(File.ReadAllText(Path.Combine(menu.DataDirectory,"settings.json")))!.RemoveNades,"Legacy root permission preserved");
await menu.Click("Zurück");await menu.Click("Bildschirm-Fade");await menu.Click("Farbe,");await menu.Click("Zurück");await menu.Click("Zurück");await menu.Click("Warnung");await menu.Click("Adminhinweise");await menu.Click("Zurück");await menu.Click("Warntext");
Check(menu.AllMenus.All(m=>m.Options.Count<=5&&m.Configuration.MaxVisibleItems==5),"All menus fit five rows");Check(menu.SelectKeys.Count==menu.AllMenus.Count&&menu.SelectKeys.All(k=>k=="E"),"E selection retained");
Check(typeof(Plugin).GetMethod(nameof(Plugin.NadeBoom))!.GetCustomAttributes<CommandAlias>().Any(x=>x.Alias=="nb"),"Legacy short command retained");plugin.Unload();Console.WriteLine($"{checks} NadeEffects checks passed (inventory policy, cancellation, ownership, fade lifecycle, real menu callbacks).");

sealed class World:INadeWorld
{
    public Dictionary<int,NadePlayer> Players=[];public HashSet<uint> Fades=[];public List<string> Inventory=[];public List<Exception> Errors=[];
    public int RemovalCalls,Knife,Destroyed,LastRemoved;public bool FailFade,FailRemove;private uint next;
    public World()=>Reset();
    public void Reset(){Players=new(){[1]=new(new(new(1,1,1),1,2),true,true,10),[2]=new(new(new(2,2,2),2,2),true,false,0)};Fades.Clear();Inventory=["weapon_flashbang","weapon_flashbang","weapon_hegrenade","weapon_smokegrenade","weapon_molotov","weapon_incgrenade","weapon_decoy","weapon_tagrenade","weapon_awp","weapon_knife"];Errors.Clear();RemovalCalls=Knife=Destroyed=0;FailFade=FailRemove=false;next=0;}
    public NadePlayer? Player(int slot)=>Players.GetValueOrDefault(slot);
    public uint Fade(NadeOwner o,NadeSettings s){if(FailFade)throw new Exception("fade");Fades.Add(++next);return next;}
    public void Destroy(uint h){Fades.Remove(h);Destroyed++;}
    public int Remove(NadeOwner o,string[] weapons,bool knife){if(FailRemove)throw new Exception("remove");RemovalCalls++;if(knife)Knife++;return Inventory.RemoveAll(x=>weapons.Contains(x));}
    public void Notice(NadeOwner o,string item,NadeSettings s,int removed){LastRemoved=removed;}
}
