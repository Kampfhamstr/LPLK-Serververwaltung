using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Fluch;
using LPLK_NadeEffects;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Menus;
using System.Numerics;
int checks=0;void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"EffectsMaintenanceChecks","test-data");
T Field<T>(object p,string name)=>(T)p.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string name,object value)=>p.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,value);
void Refresh(object p){Set(p,"nextStatus",0L);p.GetType().GetMethod("Tick")!.Invoke(p,null);}
MenuWorld World<T>(string id,T value){var w=new MenuWorld{DataDirectory=Path.Combine(root,id),RejectEntityAccess=true};Directory.CreateDirectory(w.DataDirectory);File.WriteAllText(Path.Combine(w.DataDirectory,"settings.json"),JsonSerializer.Serialize(value));w.Permissions.Add("lplk.admin");return w;}
string PathFor(MenuWorld w)=>Path.Combine(w.DataDirectory,"settings.json");
async Task Choose(MenuWorld w,string text)=>await w.Click(w.FindExact(text));
void Input(MenuWorld w,string value,bool expected=true){Check(((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,value)==expected,"Input validation: "+value);}
var fs=new FluchSettings{StartCenterMessageMs=7100,ExplosionCenterMessageMs=6600,OrbitProgressMessageMs=1700,MenuAutoCloseSeconds=111};var fw=World("fluch",fs);var fluch=new LPLK_Fluch.Plugin(fw.Core);fluch.Load(true);var cw=new CurseTestWorld();var curses=new CurseController(cw,cw.Errors.Add);Set(fluch,"curses",curses);double now=Environment.TickCount64/1000d;curses.Start(cw.Players[1].Owner,cw.Players[2].Owner,fs,now);
fluch.Administration(fw.Context);await Choose(fw,"Hühner: 20");Input(fw,"65",false);Input(fw,"12.5",false);Input(fw,"64");Check(Field<FluchSettings>(fluch,"settings").ChickenCount==64&&curses.ChickenCount==20,"Running curse retains its original immutable count");curses.Tick(true,now+11);Check(cw.Hits.SequenceEqual(new[]{100f}),"Running curse retains original damage and duration");
await Choose(fw,"Dauer: 10 s");Input(fw,"1");await Choose(fw,"Hühnerschaden: 5 (gesamt 320)");Input(fw,"100");Check(Field<FluchSettings>(fluch,"settings").TotalDamage==6400,"Full original allowed damage range accessible");
await Choose(fw,"Menüdauer: 111 s");Input(fw,"120");await Choose(fw,"Anzeigezeit · Start: 7100 ms");Input(fw,"10000");await Choose(fw,"Fortschritt: 1700 ms");Input(fw,"2000");Check(Field<FluchSettings>(fluch,"settings") is {StartCenterMessageMs:10000,ExplosionCenterMessageMs:6600,OrbitProgressMessageMs:2000,MenuAutoCloseSeconds:120},"Previously config-only timing preserved and editable");
fluch.Administration(fw.Context);File.WriteAllText(PathFor(fw),JsonSerializer.Serialize(Field<FluchSettings>(fluch,"settings") with{Enabled=false,HeadOrbitRadius=249}));await Choose(fw,"Aktiv: AN");Check(Field<FluchSettings>(fluch,"settings") is {Enabled:true,HeadOrbitRadius:249},"Fluch toggle uses latest external config");
var ns=new NadeSettings{SelectButton="Space",MoveUpButton="A",MoveDownButton="D",ExitButton="R",FadeAlpha=7,FadeColor="12 34 56",MenuNameMaxLength=63};var nw=World("nades",ns);var nade=new LPLK_NadeEffects.Plugin(nw.Core);nade.Load(true);var ew=new NadeTestWorld();var effects=new NadeController(ew,ew.Errors.Add);Set(nade,"effects",effects);effects.Toggle(ew.Players[1].Owner.Connection,ew.Players[2].Owner.Connection);effects.Equip(2,"flashbang",ns,Environment.TickCount64/1000d+100);
nade.Administration(nw.Context);await Choose(nw,$"Auslösesperre: {1.25:0.##} s");Input(nw,"60");Check(effects.Selected==1&&effects.PendingCount==0,"Valid setting update cancels queued effects but retains selected players");
effects.Equip(2,"flashbang",ns,Environment.TickCount64/1000d+100);File.WriteAllText(PathFor(nw),"{\"FadeAlpha\":999}");await Choose(nw,"Granaten entfernen: AN");Check(effects.PendingCount==1&&effects.Selected==1&&Field<NadeSettings>(nade,"settings").RemoveNades&&File.ReadAllText(PathFor(nw)).Contains("999"),"Rejected config edit preserves queued effect and selection, cannot overwrite bad file");
File.WriteAllText(PathFor(nw),JsonSerializer.Serialize(Field<NadeSettings>(nade,"settings") with{FadeHoldTime=9.75}));DateTime stamp=File.GetLastWriteTimeUtc(PathFor(nw));Refresh(nade);Check(effects.PendingCount==0&&effects.Selected==1,"Valid external reload cancels effects without deleting selection");File.WriteAllText(PathFor(nw),JsonSerializer.Serialize(Field<NadeSettings>(nade,"settings") with{FadeHoldTime=9.25}));File.SetLastWriteTimeUtc(PathFor(nw),stamp);Refresh(nade);Check(Field<NadeSettings>(nade,"settings").FadeHoldTime==9.25,"Content reload detects identical timestamp");
await Choose(nw,"Farbe: 12 34 56");Input(nw,"256 0 0",false);Input(nw,"1 2 3");await Choose(nw,"Deckkraft: 7");Input(nw,"0");await Choose(nw,"Lebensdauer: 3 s");Input(nw,"30");Check(Field<NadeSettings>(nade,"settings") is {FadeColor:"1 2 3",FadeAlpha:0,FadeEntityRemoveDelaySeconds:30,FadeHoldTime:9.25},"Full color/alpha/lifetime values preserve unrelated custom fade timing");
await Choose(nw,"Warntext ändern");Input(nw,"Eigener Hinweis");await Choose(nw,"Hinweisdauer: 3000 ms");Input(nw,"10000");await Choose(nw,"Namenslänge: 63");Input(nw,"64");Check(Field<NadeSettings>(nade,"settings") is {WarnMessage:"Eigener Hinweis",CenterWarningDurationMs:10000,MenuNameMaxLength:64},"Warn text and original display ranges editable");
foreach(var item in new (object Plugin,MenuWorld World)[]{(fluch,fw),(nade,nw)}){
 string path=PathFor(item.World),good=File.ReadAllText(path);
 foreach(string bad in new[]{"null","[]","{\"Unknown\":true}",new string(' ',65537)}){File.WriteAllText(path,bad);Refresh(item.Plugin);Check(Field<string?>(item.Plugin,"reloadError") is not null,"Invalid external file visible and rejected");}
 File.WriteAllText(path,good);Refresh(item.Plugin);Check(Field<string?>(item.Plugin,"reloadError") is null,"Config correction recovers");
 string status=Path.Combine(item.World.DataDirectory,"status.json");File.Delete(status);Directory.CreateDirectory(status);Refresh(item.Plugin);Check(Directory.Exists(status),"Status IO failure cannot abort tick lifecycle");Directory.Delete(status);Refresh(item.Plugin);Check(File.Exists(status),"Status output recovers");
 foreach(var menu in item.World.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"At most five rows");foreach(var back in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Back unnumbered");}
}
foreach(var bindings in nw.MenuKeys.Values)Check(bindings.GetValueOrDefault("SetSelectButton")=="Space"&&bindings.GetValueOrDefault("SetMoveForwardButton")=="A"&&bindings.GetValueOrDefault("SetMoveBackwardButton")=="D"&&bindings.GetValueOrDefault("SetExitButton")=="R","All native menu builders preserve custom keys, including inputs");
Check(effects.Selected==1&&cw.Errors.Count==0&&ew.Errors.Count==0&&fw.EntityAccesses==0&&nw.EntityAccesses==0,"Config tests keep selection without using native entities");
fluch.Unload();nade.Unload();Check(curses.Count==0&&effects.Selected==0,"Established unload cleanup retained");Console.WriteLine($"EffectsMaintenanceChecks: {checks} checks passed; real menu/input/config paths, effect snapshots and selected players preserved; engine calls replaced by test doubles.");

sealed class CurseTestWorld:ICurseWorld
{
    public Dictionary<int,CursePlayer> Players=[];public Dictionary<uint,Vector3> Entities=[];public List<float> Hits=[];public List<Exception> Errors=[];
    public int Moves,Destroyed,FailSpawn;public bool ThrowMove;private uint next;public Action? OnDamage;
    public CurseTestWorld()=>Reset();
    public void Reset(){Players=new(){[1]=new(new(1,1,"s",1,2),true,true,new()),[2]=new(new(2,2,"s",2,2),true,false,new(10,20,30))};Entities.Clear();Hits.Clear();Errors.Clear();Moves=Destroyed=FailSpawn=0;ThrowMove=false;next=0;OnDamage=null;}
    public CursePlayer? Player(int slot)=>Players.GetValueOrDefault(slot);
    public uint Spawn(Vector3 p,float yaw){if(++next==FailSpawn)return uint.MaxValue;Entities[next]=p;return next;}
    public bool Move(uint h,Vector3 p,float yaw){if(ThrowMove)throw new Exception("move");if(!Entities.ContainsKey(h))return false;Entities[h]=p;Moves++;return true;}
    public void Destroy(uint h){Entities.Remove(h);Destroyed++;}
    public void Damage(CurseOwner target,CurseOwner caster,float amount){Hits.Add(amount);OnDamage?.Invoke();}
    public void Notice(CurseOwner owner,string kind,float v,FluchSettings s){}
}

sealed class NadeTestWorld:INadeWorld
{
    public Dictionary<int,NadePlayer> Players=[];public HashSet<uint> Fades=[];public List<string> Inventory=[];public List<Exception> Errors=[];
    public int RemovalCalls,Knife,Destroyed,LastRemoved;public bool FailFade,FailRemove;private uint next;
    public NadeTestWorld()=>Reset();
    public void Reset(){Players=new(){[1]=new(new(new(1,1,1),1,2),true,true,10),[2]=new(new(new(2,2,2),2,2),true,false,0)};Fades.Clear();Inventory=["weapon_flashbang","weapon_flashbang","weapon_hegrenade","weapon_smokegrenade","weapon_molotov","weapon_incgrenade","weapon_decoy","weapon_tagrenade","weapon_awp","weapon_knife"];Errors.Clear();RemovalCalls=Knife=Destroyed=0;FailFade=FailRemove=false;next=0;}
    public NadePlayer? Player(int slot)=>Players.GetValueOrDefault(slot);
    public uint Fade(NadeOwner o,NadeSettings s){if(FailFade)throw new Exception("fade");Fades.Add(++next);return next;}
    public void Destroy(uint h){Fades.Remove(h);Destroyed++;}
    public int Remove(NadeOwner o,string[] weapons,bool knife){if(FailRemove)throw new Exception("remove");RemovalCalls++;if(knife)Knife++;return Inventory.RemoveAll(x=>weapons.Contains(x));}
    public void Notice(NadeOwner o,string item,NadeSettings s,int removed){LastRemoved=removed;}
}
