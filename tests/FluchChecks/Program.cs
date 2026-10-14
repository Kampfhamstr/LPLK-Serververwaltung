using LPLK_Fluch;
using System.Numerics;
using System.Text.Json;

int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
var s=new FluchSettings();FluchSettings.Validate(s);
foreach(var invalid in new[]{s with{ChickenCount=0},s with{ChickenCount=65},s with{MaxActiveCurses=5},s with{OrbitSpeed=float.NaN},s with{HeadOrbitRadius=float.PositiveInfinity},s with{OrbitTickSeconds=.001f},s with{OrbitDurationSeconds=0},s with{ChickenExplosionDamagePerChicken=-1},s with{AdminPermission=""}})
{bool threw=false;try{FluchSettings.Validate(invalid);}catch(InvalidDataException){threw=true;}Check(threw,"Invalid settings rejected");}
var w=new World();var c=new CurseController(w,w.Errors.Add);var a=w.Players[1].Owner;var b=w.Players[2].Owner;
Check(c.Start(a,b,s,100).StartsWith("Fluch gestartet"),"Start succeeds");Check(c.Count==1&&w.Entities.Count==20,"Exactly requested chickens spawned");
c.Tick(true,105);Check(w.Moves==20&&w.Hits.Count==0,"Orbit before deadline without damage");
var expected=CurseMath.Position(w.Players[2].Origin,CurseMath.Angle(0,s,5),s);Check(Vector3.Distance(w.Entities[1],expected)<.001,"Orbit follows target and elapsed time");
c.Tick(true,105.01);Check(w.Moves==20,"Movement interval honored");
c.Tick(true,110);c.Tick(true,120);Check(w.Hits.Count==1&&w.Hits[0]==100&&c.Count==0&&w.Entities.Count==0,"One damage event and complete cleanup at deadline");
Check(c.Start(a,a,s,0).Contains("Selbst"),"Self target denied");Check(c.Start(a,b,s with{Enabled=false},0).Contains("ausgeschaltet"),"Disabled start denied");
// A target selected from an old menu must not refer to a new connection/pawn/team.
foreach(var owner in new[]{b with{Steam=99},b with{Session="new"},b with{Pawn=99},b with{Team=3}})
{w.Reset();w.Players[2]=w.Players[2] with{Owner=owner};Check(!c.Start(a,b,s,0).StartsWith("Fluch gestartet")&&w.Entities.Count==0,"Stale selection rejected");}
foreach(var reason in new[]{"steam","session","pawn","team","dead","caster-session","permission","disabled"})
{
    w.Reset();c.Start(a,b,s,0);
    if(reason=="steam")w.Players[2]=w.Players[2] with{Owner=b with{Steam=999}};
    if(reason=="session")w.Players[2]=w.Players[2] with{Owner=b with{Session="next"}};
    if(reason=="pawn")w.Players[2]=w.Players[2] with{Owner=b with{Pawn=999}};
    if(reason=="team")w.Players[2]=w.Players[2] with{Owner=b with{Team=3}};
    if(reason=="dead")w.Players[2]=w.Players[2] with{Alive=false};
    if(reason=="caster-session")w.Players[1]=w.Players[1] with{Owner=a with{Session="next"}};
    if(reason=="permission")w.Players[1]=w.Players[1] with{Admin=false};
    c.Tick(reason!="disabled",11);Check(w.Hits.Count==0&&c.Count==0&&w.Entities.Count==0,"Cancel without damage: "+reason);
}
w.Reset();c.Start(a,b,s,0);c.Start(a,b,s with{ChickenExplosionDamagePerChicken=2},5);c.Tick(true,10);
Check(w.Hits.Count==0&&w.Entities.Count==20,"Replacement cannot finish at old deadline");c.Tick(true,15);Check(w.Hits.SequenceEqual([40f]),"Replacement uses own settings and deadline");
w.Reset();c.Start(a,b,s,0);c.Tick(true,1);w.ThrowMove=true;c.Tick(true,2);Check(w.Errors.Count==1&&c.Count==0&&w.Entities.Count==0,"Native movement failure cleans entire lease");
w.Reset();w.FailSpawn=7;c.Start(a,b,s,0);Check(c.Count==0&&w.Entities.Count==0&&w.Errors.Count==1,"Partial spawn failure rolls back all prior entities");
w.Reset();c.Start(a,b,s,0);w.Entities.Clear();c.Tick(true,1);c.Tick(true,11);Check(c.Count==0&&w.Hits.Count==0,"All disappeared chickens cancel curse");
w.Reset();c.Start(a,b,s,0);c.ReleasePlayer(1);c.Tick(true,11);Check(c.Count==0&&w.Entities.Count==0&&w.Hits.Count==0,"Caster disconnect cancels");
w.Reset();c.Start(a,b,s,0);c.Clear();c.Clear();Check(w.Entities.Count==0&&w.Hits.Count==0,"Repeated stop/round cleanup idempotent");
w.Reset();c.Start(a,b,s,0);int destroyed=w.Destroyed;c.Forget();Check(c.Count==0&&w.Destroyed==destroyed,"Map teardown never dereferences old entities");
w.Reset();for(int i=2;i<=6;i++){w.Players[i]=new(new(i,(ulong)i,"s",(uint)i,2),true,false,new());c.Start(a,w.Players[i].Owner,s,0);}Check(c.Count==4&&w.Entities.Count==80,"Global resource bound enforced");c.Clear();
w.Reset();c.Start(a,b,s,0);w.OnDamage=c.Clear;c.Tick(true,10);Check(w.Hits.Count==1&&w.Entities.Count==0&&c.Count==0,"Reentrant damage/death hook safe");
for(int count=0;count<=64;count++)
{
    var seen=new List<int>();int pages=TargetPages.Make(count,0).Pages;
    for(int page=0;page<pages;page++){var p=TargetPages.Make(count,page);var take=Enumerable.Range(0,count).Skip(p.Offset).Take(p.Size).ToArray();seen.AddRange(take);Check(take.Length+(page>0?1:0)+(page+1<pages?1:0)+1<=5,"Target pages fit including navigation");}
    Check(seen.SequenceEqual(Enumerable.Range(0,count)),"Every target appears once");
}
// Actual module menu callbacks, with native engine boundaries replaced by test doubles.
var menu=new MenuWorld();Directory.CreateDirectory(menu.DataDirectory);File.WriteAllText(Path.Combine(menu.DataDirectory,"settings.json"),JsonSerializer.Serialize(s));
var plugin=new Plugin(menu.Core);plugin.Load(false);plugin.Administration(menu.Context);Check(menu.Current is null,"Ordinary player denied");
menu.Permissions.Add("lplk.fluch.admin");plugin.Administration(menu.Context);Check(menu.Labels.Count==4,"Module root leaves room for central return");
await menu.Click("Einstellungen");await menu.Click("Hühner, Dauer");Check(menu.Labels.Count==5,"Effect submenu fits");
var stale=menu.Find("Hühner:");menu.Permissions.Clear();await menu.Click(stale);Check(JsonSerializer.Deserialize<FluchSettings>(File.ReadAllText(Path.Combine(menu.DataDirectory,"settings.json")))!.ChickenCount==20,"Revoked rights cannot mutate settings");
menu.Permissions.Add("@css/root");await menu.Click(stale);Check(JsonSerializer.Deserialize<FluchSettings>(File.ReadAllText(Path.Combine(menu.DataDirectory,"settings.json")))!.ChickenCount==32,"Legacy root permission retained");
await menu.Click("Zurück");await menu.Click("Kreisbahn");await menu.Click("Wellen");await menu.Click("Zurück");await menu.Click("Zurück");await menu.Click("Ziele &");await menu.Click("Bildschirm");
Check(menu.AllMenus.All(m=>m.Options.Count<=5),"All module menus max five rows");Check(menu.Labels.Last()=="<font color='#FFFFFF'>Zurück</font>","White unnumbered return");
var staleToggle=menu.Find("Countdown");menu.Session++;await menu.Click(staleToggle);Check(!JsonSerializer.Deserialize<FluchSettings>(File.ReadAllText(Path.Combine(menu.DataDirectory,"settings.json")))!.ShowOrbitProgressMessage,"Reconnect invalidates old callbacks");
plugin.Unload();Console.WriteLine($"{checks} Fluch checks passed (lifecycle, damage, identity, failure cleanup, real menu callbacks).");

sealed class World:ICurseWorld
{
    public Dictionary<int,CursePlayer> Players=[];public Dictionary<uint,Vector3> Entities=[];public List<float> Hits=[];public List<Exception> Errors=[];
    public int Moves,Destroyed,FailSpawn;public bool ThrowMove;private uint next;public Action? OnDamage;
    public World()=>Reset();
    public void Reset(){Players=new(){[1]=new(new(1,1,"s",1,2),true,true,new()),[2]=new(new(2,2,"s",2,2),true,false,new(10,20,30))};Entities.Clear();Hits.Clear();Errors.Clear();Moves=Destroyed=FailSpawn=0;ThrowMove=false;next=0;OnDamage=null;}
    public CursePlayer? Player(int slot)=>Players.GetValueOrDefault(slot);
    public uint Spawn(Vector3 p,float yaw){if(++next==FailSpawn)return uint.MaxValue;Entities[next]=p;return next;}
    public bool Move(uint h,Vector3 p,float yaw){if(ThrowMove)throw new Exception("move");if(!Entities.ContainsKey(h))return false;Entities[h]=p;Moves++;return true;}
    public void Destroy(uint h){Entities.Remove(h);Destroyed++;}
    public void Damage(CurseOwner target,CurseOwner caster,float amount){Hits.Add(amount);OnDamage?.Invoke();}
    public void Notice(CurseOwner owner,string kind,float v,FluchSettings s){}
}
