using System.Text.Json;
using LPLK_AntiTeamFlash;

int checks=0;
void Check(string name,bool condition){if(!condition)throw new Exception(name);checks++;}
var s=new FlashSettings();
Check("T teammate",FlashPolicy.Suppress(s,2,2,false,false,false));
Check("CT teammate",FlashPolicy.Suppress(s,3,3,false,false,false));
Check("T enemy",!FlashPolicy.Suppress(s,2,3,false,true,false));
Check("CT enemy",!FlashPolicy.Suppress(s,3,2,false,true,false));
Check("self default",!FlashPolicy.Suppress(s,2,2,true,true,false));
Check("self optional",FlashPolicy.Suppress(s with{FlashOwner=false},2,2,true,true,false));
Check("self optional FFA",FlashPolicy.Suppress(s with{FlashOwner=false},2,2,true,true,true));
Check("FFA enemy",!FlashPolicy.Suppress(s,2,2,false,true,true));
Check("unknown FFA",!FlashPolicy.Suppress(s,2,2,false,true,null));
Check("disabled",!FlashPolicy.Suppress(s with{Enabled=false},2,2,false,true,false));
Check("spectator",!FlashPolicy.Suppress(s,1,1,false,true,false));
Check("unassigned",!FlashPolicy.Suppress(s,0,0,false,true,false));
Check("unknown team",!FlashPolicy.Suppress(s,4,4,false,true,false));
Check("missing attacker",!FlashPolicy.Suppress(s,2,0,false,true,false));
Check("permission denied",!FlashPolicy.Suppress(s with{AccessPermission="antiteamflash.vip"},2,2,false,false,false));
Check("permission granted",FlashPolicy.Suppress(s with{AccessPermission="antiteamflash.vip"},2,2,false,true,false));
Check("permission not enemy immunity",!FlashPolicy.Suppress(s with{AccessPermission="antiteamflash.vip"},2,3,false,true,false));
Check("permission applies to self",!FlashPolicy.Suppress(s with{FlashOwner=false,AccessPermission="vip"},2,2,true,false,false));
Check("empty permission",FlashSettings.ValidPermission(""));
Check("legacy permission",FlashSettings.ValidPermission("antiteamflash.vip"));
Check("null permission",!FlashSettings.ValidPermission(null));
Check("markup rejected",!FlashSettings.ValidPermission("<font>"));
Check("control rejected",!FlashSettings.ValidPermission("vip\n"));
Check("whitespace rejected",!FlashSettings.ValidPermission(" vip "));
Check("permission length",!FlashSettings.ValidPermission(new string('a',129)));

var h=new FlashHistory();var id=new FlashIdentity(3,10,44,2);
Check("first friendly clear",h.ShouldClear(id,true,10,5));
Check("friendly followed by friendly",h.ShouldClear(id,true,11,4));
Check("enemy retains effect",!h.ShouldClear(id,false,12,6));
Check("friendly must not clear enemy",!h.ShouldClear(id,true,13,1));
Check("enemy still protected at final tick",!h.ShouldClear(id,true,17.99,0));
Check("expiry boundary",h.ShouldClear(id,true,18,3));
Check("new allowed self",!h.ShouldClear(id,false,20,4));
Check("config change retains allowed self",!h.ShouldClear(id,true,21,1));
Check("new session cannot inherit window",h.ShouldClear(id with{Session=11},true,22,3));
h.ShouldClear(id,false,30,5);
Check("respawn cannot inherit window",h.ShouldClear(id with{Pawn=45},true,31,3));
h.ShouldClear(id,false,40,5);
Check("team change cannot inherit window",h.ShouldClear(id with{Team=3},true,41,3));
h.ShouldClear(id,false,50,5);h.Remove(id.Slot);
Check("disconnect clears window",h.ShouldClear(id,true,51,3));
h.ShouldClear(id,false,60,5);h.Clear();
Check("round/map/unload clears history",h.Count==0 && h.ShouldClear(id,true,1,3));
h.ShouldClear(id,false,10,3);
Check("clock reversal conservative",!h.ShouldClear(id,true,1,3));
h.Clear();Check("unknown duration conservative",!h.ShouldClear(id,true,10,double.NaN));
Check("later event cannot erase unknown flash",!h.ShouldClear(id,true,11,1));
h.Clear();Check("infinite duration conservative",!h.ShouldClear(id,true,10,double.PositiveInfinity));
h.Clear();Check("invalid clock conservative",!h.ShouldClear(id,true,double.NaN,1));
h.Clear();Check("negative duration conservative",!h.ShouldClear(id,true,10,-1));
h.Clear();h.ShouldClear(id,false,10,5);
Check("overlap left to engine",!h.ShouldClear(id,true,11,8));
Check("subsequent friendly cannot erase retained engine effect",!h.ShouldClear(id,true,16,1));
Check("overlap expires",h.ShouldClear(id,true,19,3));

if(args.Length==1)
{
    using var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0],"state/reference-antiteamflash.json")));
    var live=reference.RootElement.GetProperty("settings").Deserialize<FlashSettings>()!;
    Check("live enabled migration",live.Enabled==s.Enabled);
    Check("live self flash migration",live.FlashOwner==s.FlashOwner);
    Check("live permission migration",live.AccessPermission==s.AccessPermission);
}
Console.WriteLine($"PASS: {checks} AntiTeamFlash checks");
