using System.Reflection;
using LPLK_Admin;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
for (int count = 0; count <= 64; count++)
{
    var original = Enumerable.Range(0, count).ToArray(); var seen = new List<int>();
    int pages = MenuLayout.Page(original, 0).Count;
    for (int i = 0; i < pages; i++)
    {
        var page = MenuLayout.Page(original, i); seen.AddRange(page.Items);
        int rows = page.Items.Length + (i > 0 ? 1 : 0) + (i + 1 < pages ? 1 : 0) + 1;
        Check(rows <= 5, $"Page overflow for {count} items, page {i}");
    }
    Check(seen.SequenceEqual(original), "Pagination must neither lose nor repeat entries");
    Check(MenuLayout.Page(original, -1).Index == 0 && MenuLayout.Page(original, int.MaxValue).Index == pages - 1, "Stale page clamps");
}
Check(MenuLayout.Label("ÄÖß <b>&") == "ÄÖß &lt;b&gt;&amp;", "Safe names preserve Unicode");

var world = new MenuWorld();
var plugin = new Plugin(world.Core); plugin.Load(false); plugin.UseSharedInterface(world.Interfaces);
var ctx = world.Context;
world.AddWeaponsPlayerApi();
plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Count == 3 && !world.Labels.Any(s => s.Contains("Adminmenü")), "Public menu hides admin");
await world.Click("Waffen, Kamera &amp; Sounds");
Check(!world.Labels.Any(s=>s.Contains("Waffenauswahl")),"Disabled weapons menu hidden");
world.WeaponsEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Waffen, Kamera &amp; Sounds");
var weaponLink=world.Find("Waffenauswahl");world.WeaponsEnabled=false;await world.Click(weaponLink);
Check(world.Commands.Count==0,"Stale weapons link rechecks availability");
world.WeaponsEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Waffen, Kamera &amp; Sounds");await world.Click("Waffenauswahl");
Check(world.Commands.SequenceEqual(["sw_guns"]),"Weapons command retains player authority");
world.Commands.Clear();plugin.PlayerMenuCommand(ctx);
world.MapsEnabled=false;world.SoundsEnabled=false;world.WeaponsEnabled=false;plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Count==1&&world.Labels[0].Contains("Hilfe"),"Inactive addon capabilities hide empty categories");
world.MapsEnabled=true;world.FailedPlugins.Add("LPLK_Maps");plugin.PlayerMenuCommand(ctx);
Check(!world.Labels.Any(s=>s.Contains("Maps")),"Installed but unloaded addon hidden");
world.FailedPlugins.Clear();world.SoundsEnabled=true;plugin.PlayerMenuCommand(ctx);
await world.Click("Maps &amp; Abstimmungen");
Check(world.Labels.Count == 4, "Map submenu plus back");
await world.Click("Map nominieren");
Check(world.Commands.SequenceEqual(["sw_nominate"]), "Nomination runs as player");
world.Plugins.Remove("LPLK_Maps");
await world.Click("RTV-Stimme");
Check(world.Commands.Count == 1 && world.Messages.Last().Contains("nicht verfügbar"), "Unloaded public feature does not execute");
await world.Click("Zurück");
Check(!world.Labels.Any(s => s.Contains("Maps")), "Return rebuilds availability");

world.Plugins.Add("LPLK_Statistics");world.Plugins.Add("LPLK_Demos");plugin.PlayerMenuCommand(ctx);await world.Click("Statistik &amp; Hilfe");
Check(world.Labels.Any(x=>x.Contains("Statistik &amp; Ranglisten"))&&!world.Labels.Any(x=>x.Contains("Demo aufbewahren")),"Stats available, inactive demo hidden");
var statsLink=world.Find("Statistik &amp; Ranglisten");world.StatisticsEnabled=false;var beforeStats=world.Commands.Count;await world.Click(statsLink);Check(world.Commands.Count==beforeStats,"Disabled stale stats link cannot execute");
world.DemosEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Statistik &amp; Hilfe");await world.Click("Demo aufbewahren");Check(world.Commands.Last()=="sw_demo","Demo command uses player context");
world.FailedPlugins.Add("LPLK_Demos");plugin.PlayerMenuCommand(ctx);await world.Click("Statistik &amp; Hilfe");Check(!world.Labels.Any(x=>x.Contains("Demo aufbewahren")),"Unloaded demos hidden despite stale capability");world.FailedPlugins.Clear();
world.Plugins.Add("LPLK_ThirdPerson");world.ThirdPersonEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Waffen, Kamera &amp; Sounds");
Check(world.Labels.Any(x=>x.Contains("ThirdPerson")),"Active permitted camera appears");
var cameraLink=world.Find("ThirdPerson");int cameraCommands=world.Commands.Count;world.ThirdPersonEnabled=false;await world.Click(cameraLink);
Check(world.Commands.Count==cameraCommands,"Revoked/disabled camera capability rechecked at click");
world.ThirdPersonEnabled=true;await world.Click(cameraLink);Check(world.Commands.Last()=="sw_tp","Camera toggle executes as player");
world.FailedPlugins.Add("LPLK_ThirdPerson");plugin.PlayerMenuCommand(ctx);await world.Click("Waffen, Kamera &amp; Sounds");Check(!world.Labels.Any(x=>x.Contains("ThirdPerson")),"Unloaded camera hidden despite retained API");
world.FailedPlugins.Clear();world.ThirdPersonEnabled=false;plugin.PlayerMenuCommand(ctx);await world.Click("Waffen, Kamera &amp; Sounds");Check(!world.Labels.Any(x=>x.Contains("ThirdPerson")),"Unavailable camera not displayed");
plugin.Admin(ctx);
Check(world.Messages.Last().Contains("nicht mehr freigegeben"), "Direct admin command denied to ordinary player");
world.Permissions.Add("admin.menu"); plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Admin entry appears");
var staleAdmin = world.Find("Adminmenü"); world.Permissions.Clear(); await world.Click(staleAdmin);
Check(world.Messages.Last().Contains("nicht mehr freigegeben"), "Old public admin link cannot bypass revocation");

world.AddModule("LPLK_Whitelist", "Whitelist");
world.Permissions.Add("lplk.whitelist.admin"); plugin.Admin(ctx);
Check(world.Labels.Count == 2 && world.Labels.Any(s => s.Contains("Zugriff")), "Module-only admin can navigate without admin.menu");
await world.Click("Zugriff"); await world.Click("Whitelist");
Check(world.ModuleOpens == 1, "Permitted module opens");
await world.Click("Zurück zum Adminmenü");
Check(world.Labels.Any(s => s.Contains("Whitelist")), "Module returns to owning category");
var staleModule = world.Find("Whitelist"); world.Permissions.Clear(); await world.Click(staleModule);
Check(world.ModuleOpens == 1, "Revoked module cannot reopen");

world.Permissions.Add("lplk.admin"); world.Plugins.Add("LPLK_Maps");
foreach (var id in new[] { "LPLK_Maps", "LPLK_Parachute", "LPLK_Teams", "LPLK_AntiCamp", "LPLK_BombWires", "LPLK_AntiTeamFlash", "LPLK_Weapons", "LPLK_CombatRules", "LPLK_Sounds", "LPLK_ChatGuard", "LPLK_Future" }) world.AddModule(id, id);
plugin.Admin(ctx); Check(world.Labels.Count == 5, "Admin home has four groups plus back");
await world.Click("Spielregeln"); Check(world.Labels.Count == 5, "Gameplay home has four groups plus back");
await world.Click("Weitere Module"); Check(world.Labels.Any(s => s.Contains("LPLK_Future")), "Unknown registered module remains reachable");
await world.Click("Zurück"); await world.Click("Bewegung");
Check(world.Labels.Count == 4, "Three movement modules plus back");
Check(world.Labels.Last() == "<font color='#FFFFFF'>Zurück</font>", "Back is white and unnumbered");
Check(world.Labels.First().StartsWith("<font color='#FFD966'>1. "), "Action numbering and yellow retained");
var staleMovement = world.Find("LPLK_Parachute"); plugin.PlayerMenuCommand(ctx); await world.Click(staleMovement);
Check(world.ModuleOpens == 1, "Superseded menu cannot execute");
world.AddModule("LPLK_Fluch","Hühnerfluch");plugin.Admin(ctx);await world.Click("Spielregeln");await world.Click("Kampf &amp;");
while(!world.Labels.Any(x=>x.Contains("Hühnerfluch")))await world.Click("Nächste");
var fluchLink=world.Find("Hühnerfluch");world.FailedPlugins.Add("LPLK_Fluch");int opens=world.ModuleOpens;await world.Click(fluchLink);
Check(world.ModuleOpens==opens,"Unloaded curse cannot open from stale menu");world.FailedPlugins.Clear();
plugin.Admin(ctx);await world.Click("Spielregeln");await world.Click("Kampf &amp;");while(!world.Labels.Any(x=>x.Contains("Hühnerfluch")))await world.Click("Nächste");await world.Click("Hühnerfluch");
Check(world.ModuleOpens==opens+1,"Loaded Fluch available under combat");
world.AddModule("LPLK_NadeEffects","Granateneffekte");plugin.Admin(ctx);await world.Click("Spielregeln");await world.Click("Kampf &amp;");
for(int page=0;page<8&&!world.Labels.Any(x=>x.Contains("Granateneffekte"));page++)await world.Click("Nächste");
var nadeLink=world.Find("Granateneffekte");world.FailedPlugins.Add("LPLK_NadeEffects");opens=world.ModuleOpens;await world.Click(nadeLink);Check(world.ModuleOpens==opens,"Unloaded nade module cannot open from stale menu");world.FailedPlugins.Clear();
plugin.Admin(ctx);await world.Click("Spielregeln");await world.Click("Kampf &amp;");for(int page=0;page<8&&!world.Labels.Any(x=>x.Contains("Granateneffekte"));page++)await world.Click("Nächste");await world.Click("Granateneffekte");Check(world.ModuleOpens==opens+1,"Nade module belongs to combat group");plugin.PlayerMenuCommand(ctx);
world.AddModule("LPLK_Economy","Bank & Konten");world.EconomyEnabled=true;plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Any(x=>x.Contains("Bank &amp; Wirtschaft")),"Enabled Economy category visible");
await world.Click("Bank &amp; Wirtschaft");Check(!world.Labels.Any(x=>x.Contains("Wetten")||x.Contains("Lotterie")||x.Contains("Bankraub")||x.Contains("Titel")||x.Contains("Kopfgeld")),"Unimplemented internal Economy features hidden despite registered commands");var economyLink=world.Find("Bankkonto");int oldCommands=world.Commands.Count;world.EconomyEnabled=false;await world.Click(economyLink);
Check(world.Commands.Count==oldCommands,"Disabled Economy rechecked before command");world.EconomyEnabled=true;await world.Click(economyLink);Check(world.Commands.Last()=="sw_bank","Bank retains player authority");
world.BettingEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Bank &amp; Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Wetten")),"Internal betting shown through same Economy addon");var wagerLink=world.Find("Wetten");int wagerCommands=world.Commands.Count;world.BettingEnabled=false;await world.Click(wagerLink);Check(world.Commands.Count==wagerCommands,"Disabled betting rechecked without disabling bank");world.BettingEnabled=true;await world.Click(wagerLink);Check(world.Commands.Last()=="sw_bet","Betting keeps player authority");world.BettingEnabled=false;
world.LotteryEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Bank &amp; Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Lotterie")),"Internal lottery visible through Economy");var lottoLink=world.Find("Lotterie");int lottoCommands=world.Commands.Count;world.LotteryEnabled=false;await world.Click(lottoLink);Check(world.Commands.Count==lottoCommands,"Disabled lottery rechecked at click");world.LotteryEnabled=true;await world.Click(lottoLink);Check(world.Commands.Last()=="sw_lotto","Lottery keeps player authority");world.LotteryEnabled=false;
world.RobberyEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Bank &amp; Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Bankraub")),"Internal robbery visible through same Economy addon");var robberyLink=world.Find("Bankraub");int robberyCommands=world.Commands.Count;world.RobberyEnabled=false;await world.Click(robberyLink);Check(world.Commands.Count==robberyCommands,"Robbery disabled at click blocks old link");world.RobberyEnabled=true;await world.Click(robberyLink);Check(world.Commands.Last()=="sw_bankraub","Robbery command retains player authority");world.RobberyEnabled=false;
world.TitlesEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Bank &amp; Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Titel")),"Internal titles visible through Economy");var titleLink=world.Find("Titel");int titleCommands=world.Commands.Count;world.TitlesEnabled=false;await world.Click(titleLink);Check(world.Commands.Count==titleCommands,"Disabled titles rechecked on click");world.TitlesEnabled=true;await world.Click(titleLink);Check(world.Commands.Last()=="sw_titel","Title command keeps player authority");world.TitlesEnabled=false;
world.BountyEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Bank &amp; Wirtschaft");while(!world.Labels.Any(x=>x.Contains("Kopfgeld")))await world.Click("Nächste");Check(world.Labels.Any(x=>x.Contains("Kopfgeld")),"Enabled bounty belongs to Economy");var bountyLink=world.Find("Kopfgeld");int bountyCommands=world.Commands.Count;world.BountyEnabled=false;await world.Click(bountyLink);Check(world.Commands.Count==bountyCommands,"Disabled bounty rechecked at click");world.BountyEnabled=true;await world.Click(bountyLink);Check(world.Commands.Last()=="sw_kopfgeld","Bounty keeps player authority");world.BountyEnabled=false;
world.FailedPlugins.Add("LPLK_Economy");plugin.PlayerMenuCommand(ctx);Check(!world.Labels.Any(x=>x.Contains("Bank &amp; Wirtschaft")),"Unloaded Economy hides group");world.FailedPlugins.Clear();
plugin.Admin(ctx);await world.Click("Spielregeln");for(int page=0;page<4&&!world.Labels.Any(x=>x.Contains("Bank &amp; Wirtschaft"));page++)await world.Click("Nächste");await world.Click("Bank &amp; Wirtschaft");await world.Click("Bank &amp; Konten");
Check(world.ModuleOpens==opens+2,"Economy grouped under gameplay economy");
world.EconomyEnabled=false;plugin.PlayerMenuCommand(ctx);plugin.Admin(ctx);await world.Click("Spielregeln");for(int page=0;page<4&&!world.Labels.Any(x=>x.Contains("Bank &amp; Wirtschaft"));page++)await world.Click("Nächste");
Check(world.Labels.Any(x=>x.Contains("Bank &amp; Wirtschaft")),"Loaded disabled Economy administration remains reachable");
world.EconomyEnabled=true;world.MapsEnabled=true;world.Plugins.Add("LPLK_Maps");plugin.PlayerMenuCommand(ctx);Check(world.Labels.Count==5&&!world.Labels.Any(x=>x.Contains("Nächste Seite")),"Five public root actions fit one page without back row");
var oldHelp = world.Find("Hilfe"); world.Session++; await world.Click(oldHelp);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Old connection cannot navigate");
plugin.PlayerMenuCommand(ctx); oldHelp = world.Find("Hilfe"); plugin.Unload(); await world.Click(oldHelp);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Unloaded plugin callbacks ignored");
Check(typeof(Plugin).GetMethod(nameof(Plugin.Admin))!.GetCustomAttributes<CommandAlias>().Any(a => a.Alias == "adminmenu"), "Admin alias uses same permission-protected handler");
Check(world.AllMenus.Where(m => m.Configuration.Title?.StartsWith("<font") == true).All(m => m.Options.Count <= 5 && m.Configuration.MaxVisibleItems == 5), "All central menus fit five rows");
Console.WriteLine($"{checks} Admin menu checks passed (real option callbacks; fake engine boundaries).");




