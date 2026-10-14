using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using LPLK_Weapons;
using LPLK_CombatRules;
using SwiftlyS2.Shared.Players;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
void Invalid(Action action,string message){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}Check(failed,message);}
var w=new WeaponSettings();var c=new CombatSettings();WeaponSettings.Validate(w);CombatSettings.Validate(c);
Check(!w.RestrictionEnabled&&!w.MenuEnabled&&!c.HeadshotOnly&&c.GoldenRule,"Match live runtime, not default HeadshotOnly true");
Check(WeaponCatalog.All.Length==34&&WeaponCatalog.All.Select(w=>w.Id).Distinct().Count()==34,"All firearms include Dual Berettas exactly once");
var restricted=w with{RestrictionEnabled=true,AllowedWeapons=WeaponCatalog.Preset("PistolsOnly")};
foreach(var weapon in WeaponCatalog.All)
{
    Check(WeaponPolicy.Restricted(restricted,weapon.Id)!=weapon.Secondary,"Pistol preset preserves all sidearms");
    Check(!WeaponPolicy.Restricted(w,weapon.Id),"Disabled restrictions allow firearm");
    Check(!WeaponPolicy.MenuAllows(w,weapon.Id),"Disabled give menu cannot grant");
}
Check(!WeaponPolicy.Restricted(restricted,"weapon_knife"),"Default keeps knife/objectives");
Check(WeaponPolicy.Restricted(restricted with{RestrictUnknownWeapons=true},"weapon_unknown"),"Explicit unknown restriction");
Check(!WeaponPolicy.Restricted(restricted with{Enabled=false},"weapon_awp"),"Module disabled");
Check(!WeaponPolicy.MenuAllows(w with{MenuEnabled=true,MenuBlacklist=["weapon_awp"]},"weapon_awp"),"Blacklist");
Check(WeaponPolicy.MenuAllows(w with{MenuEnabled=true},"weapon_awp"),"Enabled menu offers allowed firearm");
Check(!WeaponPolicy.MenuAllows(w with{MenuEnabled=true},"weapon_c4"),"Cannot grant arbitrary entity");
var rule=new WeaponRule{BlockUsing=true,AllowPickupWhenBlocked=true,TeamQuota=new(){[0]=2,[5]=1},MapQuota=new(){["de_dust2"]=new(){[0]=0,[3]=3}}};
Check(WeaponPolicy.Quota(rule,"de_inferno",6)==1,"Decreasing quota uses highest threshold, not max allowance");
Check(WeaponPolicy.Quota(rule,"DE_DUST2",2)==0&&WeaponPolicy.Quota(rule,"de_dust2",4)==3,"Map override");
var special=w with{Rules=new(){["weapon_awp"]=rule}};
Check(WeaponPolicy.Denial(special,"weapon_awp",true,false,"de_inferno",6,0) is null,"Pickup exception");
Check(WeaponPolicy.Denial(special,"weapon_awp",false,false,"de_inferno",6,0) is not null,"Buy remains blocked");
Check(WeaponPolicy.Denial(special,"weapon_awp",false,true,"de_inferno",6,0) is null,"Permission bypass for block");
Check(WeaponPolicy.Denial(special,"weapon_awp",true,true,"de_inferno",6,1) is not null,"Bypass cannot exceed quota");
Check(WeaponPolicy.Quota(new(),"de_inferno",5) is null,"No quota means unlimited");
Invalid(()=>WeaponSettings.Validate(w with{AllowedWeapons=null!}),"Null list rejected");
Invalid(()=>WeaponSettings.Validate(w with{AllowedWeapons=["weapon_awp","weapon_awp"]}),"Duplicates rejected");
Invalid(()=>WeaponSettings.Validate(w with{MenuBlacklist=["weapon_awp;quit"]}),"No injected class name");
Invalid(()=>WeaponSettings.Validate(w with{Rules=new(){["weapon_hegrenade"]=rule}}),"Unsupported grenade quota rejected, not silently miscounted");
Invalid(()=>WeaponSettings.Validate(w with{Rules=new(){["weapon_awp"]=new(){TeamQuota=new(){[-1]=1}}}}),"Negative threshold rejected");
foreach(int team in new[]{0,1,2,3})foreach(int ct in new[]{-1,0,5,6})foreach(int t in new[]{-1,0,5,6})
{
    bool expected=team is 2 or 3&&ct>=0&&t>=0&&ct!=t&&(team==3?ct>t:t>ct);
    Check(CombatPolicy.BlockGolden(c,"weapon_awp",team,ct,t,true,false,false)==expected,"Golden rule leading team only, no unknown/tie");
}
Check(!CombatPolicy.BlockGolden(c,"weapon_ssg08",3,6,5,true,false,false),"Scout excluded");
Check(!CombatPolicy.BlockGolden(c,"weapon_awp",3,6,5,false,false,false),"No-scope allowed");
Check(!CombatPolicy.BlockGolden(c,"weapon_awp",3,6,5,true,true,false),"Warmup allowed");
Check(CombatPolicy.BlockGolden(c with{EnabledInWarmup=true},"weapon_awp",3,6,5,true,true,false),"Explicit warmup");
foreach(bool? ffa in new bool?[]{true,null})Check(!CombatPolicy.BlockGolden(c,"weapon_awp",3,6,5,true,false,ffa),"FFA or unknown FFA not team leader penalty");
Check(!CombatPolicy.BlockGolden(c,"weapon_awp",3,6,5,true,null,false),"Unknown warmup not assumed live");
Check(!CombatPolicy.BlockGolden(c with{MinimumLeadRounds=2},"weapon_awp",3,6,5,true,false,false),"Minimum lead");
Check(!CombatPolicy.BlockGolden(c with{Enabled=false},"weapon_awp",3,6,5,true,false,false),"Disabled combat");
var head=c with{HeadshotOnly=true};
Check(CombatPolicy.BlockHeadshot(head,"weapon_elite",false),"Dualies treated as firearm");
Check(!CombatPolicy.BlockHeadshot(head,"weapon_awp",true),"Head allowed");
Check(!CombatPolicy.BlockHeadshot(head,"weapon_knife",false),"Knife unaffected by firearms-only");
Check(CombatPolicy.BlockHeadshot(head with{FirearmsOnly=false},"weapon_knife",false),"Explicit all-weapons headshot");
Check(CombatPolicy.BlockHeadshot(c with{Rules=new(){["weapon_awp"]=new(){OnlyHeadshot=true}}},"weapon_awp",false),"Per-weapon headshot");
foreach(var (formula,result) in new[]{("=50",50f),("+25",125f),("-150",0f),("*2",200f),("/4",25f),("*100000",100000f)})Check(DamageFormula.Apply(100,formula)==result,"Damage formula "+formula);
foreach(string formula in new[]{"/0","NaN","*Infinity","1000","+1;quit","*100001"})Check(!DamageFormula.Valid(formula),"Reject invalid formula "+formula);
Invalid(()=>CombatSettings.Validate(c with{GoldenWeapons=["weapon_knife"]}),"Golden only firearms");
Invalid(()=>CombatSettings.Validate(c with{MinimumLeadRounds=0}),"Zero minimum rejected");
Invalid(()=>CombatSettings.Validate(c with{Rules=new(){["weapon_awp"]=new(){Damage="/0"}}}),"Invalid damage cannot replace valid settings");
bool unknownRejected=false;try{JsonSerializer.Deserialize<WeaponSettings>("{\"Clip\":999}");}catch(JsonException){unknownRejected=true;}
Check(unknownRejected,"Unsupported legacy options must not silently disappear");
using(var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"state","weapons-reference.json"))))
{
    var source=reference.RootElement;
    Check(c.HeadshotOnly==source.GetProperty("runtime").GetProperty("HeadshotOnlyEnabled").GetBoolean(),"Live runtime headshot transferred");
    Check(w.RestrictionEnabled==source.GetProperty("runtime").GetProperty("WeaponRestrictionEnabled").GetBoolean(),"Live runtime restriction transferred");
    Check(c.GoldenWeapons.SequenceEqual(source.GetProperty("settings").GetProperty("GoldenRule").GetProperty("Weapons").EnumerateArray().Select(x=>x.GetString()!)),"Live Golden weapon list transferred");
    Check(source.GetProperty("aws").GetProperty("unparsedLines").GetInt32()==0,"Live AWS has no unreviewed overrides");
}

// Actual menu options and callbacks, replacing only engine interfaces.
var world=new MenuWorld();world.DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"WeaponsCombatChecks","test-data");
world.Permissions.Add("lplk.admin");
int actions=0;
for(int count=0;count<=34;count++)
{
    var rows=Enumerable.Range(0,count).Select(i=>($"Action {i}",(Action<IPlayer>)(_=>actions++))).ToArray();
    CompactMenu.Open(world.Core,world.Player,"Test",()=>true,_=>true,_=>rows,_=>{});
    int seen=0;
    while(true)
    {
        Check(world.Labels.Count<=5,"Compact menu within five rows");
        seen+=world.Labels.Count(x=>x.Contains("Action"));
        if(!world.Labels.Any(x=>x.Contains("Nächste")))break;
        await world.Click("Nächste");
    }
    Check(seen==count,"Every entry reachable exactly once");
}
bool active=true,allowed=true;
CompactMenu.Open(world.Core,world.Player,"Test",()=>active,_=>allowed,_=>new[]{("Action",(Action<IPlayer>)(_=>actions++))});
var stale=world.Find("Action");allowed=false;await world.Click(stale);Check(actions==0,"Revoked compact menu rights");
allowed=true;active=false;await world.Click(stale);Check(actions==0,"Unloaded compact menu ignored");
active=true;world.Session++;await world.Click(stale);Check(actions==0,"Reused player slot ignored");

void Prepare(object plugin,string id)
{
    var type=plugin.GetType();var open=type.GetMethod("OpenAdmin",BindingFlags.Instance|BindingFlags.NonPublic)!;
    var support=new ModuleSupport(world.Core,id,id,"lplk.admin",p=>open.Invoke(plugin,[p])){Active=true};
    type.GetField("module",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(plugin,support);
}
var weapons=new LPLK_Weapons.Plugin(world.Core);Prepare(weapons,"LPLK_Weapons");weapons.Status(world.Context);
Check(world.Labels.Count==4,"Weapons root compact");await world.Click("Waffenfreigaben");await world.Click("Einzelne Waffen");
Check(world.Labels.Count<=5,"Weapon groups paginated");await world.Click("Pistolen");Check(world.Labels.Count<=5,"Pistol list paginated");
weapons.GunsCommand(world.Context);Check(world.Messages.Last().Contains("ausgeschaltet"),"Disabled guns command refuses access");
var enabled=w with{MenuEnabled=true};
typeof(LPLK_Weapons.Plugin).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(weapons,enabled);
weapons.GunsCommand(world.Context);await world.Click("Pistolen");await world.Click("Glock");
Check(world.Messages.Last().Contains("lebender Spieler"),"Dead player cannot receive weapons");
weapons.MapUnload(null!);await world.Click("Glock");
Check(world.Messages.Last().Contains("Neue Runde"),"Old-life or old-map selection cannot issue weapons");
Directory.CreateDirectory(world.DataDirectory);var configPath=Path.Combine(world.DataDirectory,"settings.json");
File.WriteAllText(configPath,JsonSerializer.Serialize(w));weapons.Tick();
File.WriteAllText(configPath,"{\"AllowedWeapons\":null}");File.SetLastWriteTimeUtc(configPath,DateTime.UtcNow.AddSeconds(1));
typeof(LPLK_Weapons.Plugin).GetField("nextStatus",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(weapons,0L);weapons.Tick();
var retained=(WeaponSettings)typeof(LPLK_Weapons.Plugin).GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(weapons)!;
Check(!retained.MenuEnabled&&retained.AllowedWeapons.Length==34,"Invalid external update retains last validated settings");
Check(typeof(LPLK_Weapons.Plugin).GetField("reloadError",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(weapons) is string,"Reload failure visible");
File.WriteAllText(configPath,JsonSerializer.Serialize(w));
var combat=new LPLK_CombatRules.Plugin(world.Core);Prepare(combat,"LPLK_CombatRules");combat.Status(world.Context);await world.Click("Goldene Regel");await world.Click("Betroffene Waffen");
Check(world.Labels.Count==5&&world.Labels.Any(s=>s.Contains("#FF7070")&&s.Contains("SSG 08")),"Golden weapon switches: Scout disabled, max five rows");
Check(world.Labels.Last()=="<font color='#FFFFFF'>Zurück</font>","Unnumbered white return");
Check(world.AllMenus.All(m=>m.Options.Count<=5),"All new menu pages compact");
Console.WriteLine($"{checks} weapons/combat checks passed (policy + real menu callbacks, no native gameplay simulation).");
