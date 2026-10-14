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
async Task<IMenuOption> FindPaged(string label)
{
    for (int i = 0; i < 40; i++)
    {
        if (world.Labels.Any(x => x.Contains(label))) return world.Find(label);
        if (!world.Labels.Any(x => x.Contains("Nächste Seite"))) break;
        await world.Click("Nächste Seite");
    }
    throw new Exception("Entry missing from flat menu: " + label);
}
async Task OpenAdvanced(string label)
{
    plugin.Admin(ctx); await world.Click("Erweitert"); await world.Click(await FindPaged(label));
}
world.AddWeaponsPlayerApi();
plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Count == 3 && !world.Labels.Any(s => s.Contains("Adminmenü")), "Public menu hides admin");

Check(!world.Labels.Any(s=>s.Contains("Waffen")),"Disabled weapons menu hidden");
world.WeaponsEnabled=true;plugin.PlayerMenuCommand(ctx);
var weaponLink=world.Find("Waffen");world.WeaponsEnabled=false;await world.Click(weaponLink);
Check(world.Commands.Count==0,"Stale weapons link rechecks availability");
world.WeaponsEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Waffen");
Check(world.Commands.SequenceEqual(["sw_guns"]),"Weapons command retains player authority");
world.Commands.Clear();plugin.PlayerMenuCommand(ctx);
world.MapsEnabled=false;world.SoundsEnabled=false;world.WeaponsEnabled=false;plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Count==1&&world.Labels[0].Contains("Info"),"Inactive addon capabilities hide empty categories");
world.MapsEnabled=true;world.FailedPlugins.Add("LPLK_Maps");plugin.PlayerMenuCommand(ctx);
Check(!world.Labels.Any(s=>s.Contains("Maps")),"Installed but unloaded addon hidden");
world.FailedPlugins.Clear();world.SoundsEnabled=true;plugin.PlayerMenuCommand(ctx);
await world.Click("Maps");
Check(world.Labels.Count == 4, "Map submenu plus back");
await world.Click("Nominieren");
Check(world.Commands.SequenceEqual(["sw_nominate"]), "Nomination runs as player");
world.Plugins.Remove("LPLK_Maps");
await world.Click("RTV");
Check(world.Commands.Count == 1 && world.Messages.Last().Contains("nicht verfügbar"), "Unloaded public feature does not execute");
await world.Click("Zurück");
Check(!world.Labels.Any(s => s.Contains("Maps")), "Return rebuilds availability");

world.Plugins.Add("LPLK_Statistics");world.Plugins.Add("LPLK_Demos");plugin.PlayerMenuCommand(ctx);await world.Click("Info");
Check(world.Labels.Any(x=>x.Contains("Statistik"))&&!world.Labels.Any(x=>x.Contains("Demo sichern")),"Stats available, inactive demo hidden");
var statsLink=world.Find("Statistik");world.StatisticsEnabled=false;var beforeStats=world.Commands.Count;await world.Click(statsLink);Check(world.Commands.Count==beforeStats,"Disabled stale stats link cannot execute");
world.DemosEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Info");await world.Click("Demo sichern");Check(world.Commands.Last()=="sw_demo","Demo command uses player context");
world.FailedPlugins.Add("LPLK_Demos");plugin.PlayerMenuCommand(ctx);await world.Click("Info");Check(!world.Labels.Any(x=>x.Contains("Demo sichern")),"Unloaded demos hidden despite stale capability");world.FailedPlugins.Clear();
world.Plugins.Add("LPLK_ThirdPerson");world.ThirdPersonEnabled=true;plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Any(x=>x.Contains("Kamera")),"Active permitted camera appears");
var cameraLink=world.Find("Kamera");int cameraCommands=world.Commands.Count;world.ThirdPersonEnabled=false;await world.Click(cameraLink);
Check(world.Commands.Count==cameraCommands,"Revoked/disabled camera capability rechecked at click");
world.ThirdPersonEnabled=true;await world.Click(cameraLink);Check(world.Commands.Last()=="sw_tp","Camera toggle executes as player");
world.FailedPlugins.Add("LPLK_ThirdPerson");plugin.PlayerMenuCommand(ctx);Check(!world.Labels.Any(x=>x.Contains("Kamera")),"Unloaded camera hidden despite retained API");
world.FailedPlugins.Clear();world.ThirdPersonEnabled=false;plugin.PlayerMenuCommand(ctx);Check(!world.Labels.Any(x=>x.Contains("Kamera")),"Unavailable camera not displayed");
plugin.Admin(ctx);
Check(world.Messages.Last().Contains("nicht mehr freigegeben"), "Direct admin command denied to ordinary player");
world.Permissions.Add("admin.menu"); plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Admin entry appears");
var staleAdmin = world.Find("Adminmenü"); world.Permissions.Clear(); await world.Click(staleAdmin);
Check(world.Messages.Last().Contains("nicht mehr freigegeben"), "Old public admin link cannot bypass revocation");

world.AddModule("LPLK_Whitelist", "Whitelist");
world.Permissions.Add("lplk.whitelist.admin"); plugin.Admin(ctx);
Check(world.Labels.Count == 2 && world.Labels.Any(s => s.Contains("Erweitert")), "Module-only admin can navigate without admin.menu");
await world.Click("Erweitert"); await world.Click("Whitelist");
Check(world.ModuleOpens == 1, "Permitted module opens");
await world.Click("Adminmenü");
Check(world.Labels.Any(s => s.Contains("Whitelist")), "Module returns to owning category");
var staleModule = world.Find("Whitelist"); world.Permissions.Clear(); await world.Click(staleModule);
Check(world.ModuleOpens == 1, "Revoked module cannot reopen");

world.Permissions.Add("lplk.admin"); world.Plugins.Add("LPLK_Maps");
foreach (var id in new[] { "LPLK_Maps", "LPLK_Parachute", "LPLK_Teams", "LPLK_AntiCamp", "LPLK_BombWires", "LPLK_AntiTeamFlash", "LPLK_Weapons", "LPLK_CombatRules", "LPLK_Sounds", "LPLK_ChatGuard", "LPLK_Future" }) world.AddModule(id, id);
plugin.Admin(ctx); Check(world.Labels.Count == 5, "Admin home has four direct actions plus back");
Check(world.Labels.Take(4).Select(x => x.Contains("Spieler") || x.Contains("Maps") || x.Contains("Sounds") || x.Contains("Erweitert")).All(x => x), "Home contains only everyday entry points");
await world.Click("Spieler");
Check(world.Current!.Configuration.Title!.Contains("Spieler auswählen"), "Players opens selection immediately without a management wrapper");
await world.Click("Zurück"); Check(world.Labels.Any(x => x.Contains("Erweitert")), "Player list returns directly home");
int directOpens = world.ModuleOpens; await world.Click("Maps"); Check(world.ModuleOpens == directOpens + 1, "Maps opens its module in one click");
await world.Click("Adminmenü"); await world.Click("Sounds"); Check(world.ModuleOpens == directOpens + 2, "Sounds opens its module in one click");
await OpenAdvanced("LPLK_Future"); Check(world.ModuleOpens == directOpens + 3, "Unknown module remains reachable in flat advanced list");
await world.Click("Adminmenü");
Check(world.Labels.Any(x => x.Contains("LPLK_Future")), "Module return restores its advanced page");
Check(world.Labels.Last() == "<font color='#FFFFFF'>Zurück</font>", "Back is white and unnumbered");
Check(world.Labels.First().StartsWith("<font color='#FFD966'>1. "), "Action numbering and yellow retained");
plugin.Admin(ctx); await world.Click("Erweitert");
var staleMovement = await FindPaged("LPLK_Parachute"); int staleOpens = world.ModuleOpens;
plugin.PlayerMenuCommand(ctx); await world.Click(staleMovement);
Check(world.ModuleOpens == staleOpens, "Superseded menu cannot execute");
world.AddModule("LPLK_Fluch", "Hühnerfluch"); plugin.Admin(ctx); await world.Click("Erweitert");
var fluchLink = await FindPaged("Hühnerfluch"); world.FailedPlugins.Add("LPLK_Fluch"); int opens = world.ModuleOpens; await world.Click(fluchLink);
Check(world.ModuleOpens == opens, "Unloaded curse cannot open from stale menu"); world.FailedPlugins.Clear();
await OpenAdvanced("Hühnerfluch"); Check(world.ModuleOpens == opens + 1, "Fluch opens without combat category wrappers");
world.AddModule("LPLK_NadeEffects", "Granateneffekte"); plugin.Admin(ctx); await world.Click("Erweitert");
var nadeLink = await FindPaged("Granateneffekte"); world.FailedPlugins.Add("LPLK_NadeEffects"); opens = world.ModuleOpens; await world.Click(nadeLink);
Check(world.ModuleOpens == opens, "Unloaded nade module cannot open from stale menu"); world.FailedPlugins.Clear();
await OpenAdvanced("Granateneffekte"); Check(world.ModuleOpens == opens + 1, "Nade module opens directly from advanced list"); plugin.PlayerMenuCommand(ctx);
world.AddModule("LPLK_Economy","Bank & Konten");world.EconomyEnabled=true;plugin.PlayerMenuCommand(ctx);
Check(world.Labels.Any(x=>x.Contains("Wirtschaft")),"Enabled Economy category visible");
await world.Click("Wirtschaft");Check(!world.Labels.Any(x=>x.Contains("Wetten")||x.Contains("Lotterie")||x.Contains("Bankraub")||x.Contains("Titel")||x.Contains("Kopfgeld")),"Unimplemented internal Economy features hidden despite registered commands");var economyLink=world.Find("Konto");int oldCommands=world.Commands.Count;world.EconomyEnabled=false;await world.Click(economyLink);
Check(world.Commands.Count==oldCommands,"Disabled Economy rechecked before command");world.EconomyEnabled=true;await world.Click(economyLink);Check(world.Commands.Last()=="sw_bank","Bank retains player authority");
world.BettingEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Wetten")),"Internal betting shown through same Economy addon");var wagerLink=world.Find("Wetten");int wagerCommands=world.Commands.Count;world.BettingEnabled=false;await world.Click(wagerLink);Check(world.Commands.Count==wagerCommands,"Disabled betting rechecked without disabling bank");world.BettingEnabled=true;await world.Click(wagerLink);Check(world.Commands.Last()=="sw_bet","Betting keeps player authority");world.BettingEnabled=false;
world.LotteryEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Lotterie")),"Internal lottery visible through Economy");var lottoLink=world.Find("Lotterie");int lottoCommands=world.Commands.Count;world.LotteryEnabled=false;await world.Click(lottoLink);Check(world.Commands.Count==lottoCommands,"Disabled lottery rechecked at click");world.LotteryEnabled=true;await world.Click(lottoLink);Check(world.Commands.Last()=="sw_lotto","Lottery keeps player authority");world.LotteryEnabled=false;
world.RobberyEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Bankraub")),"Internal robbery visible through same Economy addon");var robberyLink=world.Find("Bankraub");int robberyCommands=world.Commands.Count;world.RobberyEnabled=false;await world.Click(robberyLink);Check(world.Commands.Count==robberyCommands,"Robbery disabled at click blocks old link");world.RobberyEnabled=true;await world.Click(robberyLink);Check(world.Commands.Last()=="sw_bankraub","Robbery command retains player authority");world.RobberyEnabled=false;
world.TitlesEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Wirtschaft");Check(world.Labels.Any(x=>x.Contains("Titel")),"Internal titles visible through Economy");var titleLink=world.Find("Titel");int titleCommands=world.Commands.Count;world.TitlesEnabled=false;await world.Click(titleLink);Check(world.Commands.Count==titleCommands,"Disabled titles rechecked on click");world.TitlesEnabled=true;await world.Click(titleLink);Check(world.Commands.Last()=="sw_titel","Title command keeps player authority");world.TitlesEnabled=false;
world.BountyEnabled=true;plugin.PlayerMenuCommand(ctx);await world.Click("Wirtschaft");while(!world.Labels.Any(x=>x.Contains("Kopfgeld")))await world.Click("Nächste");Check(world.Labels.Any(x=>x.Contains("Kopfgeld")),"Enabled bounty belongs to Economy");var bountyLink=world.Find("Kopfgeld");int bountyCommands=world.Commands.Count;world.BountyEnabled=false;await world.Click(bountyLink);Check(world.Commands.Count==bountyCommands,"Disabled bounty rechecked at click");world.BountyEnabled=true;await world.Click(bountyLink);Check(world.Commands.Last()=="sw_kopfgeld","Bounty keeps player authority");world.BountyEnabled=false;
world.FailedPlugins.Add("LPLK_Economy");plugin.PlayerMenuCommand(ctx);Check(!world.Labels.Any(x=>x.Contains("Wirtschaft")),"Unloaded Economy hides group");world.FailedPlugins.Clear();
await OpenAdvanced("Bank &amp; Konten");
Check(world.ModuleOpens == opens + 2, "Economy opens without gameplay and economy wrappers");
world.EconomyEnabled=false; plugin.Admin(ctx); await world.Click("Erweitert");
Check(await FindPaged("Bank &amp; Konten") is not null, "Loaded disabled Economy administration remains reachable");
world.EconomyEnabled=true;world.MapsEnabled=true;world.Plugins.Add("LPLK_Maps");plugin.PlayerMenuCommand(ctx);Check(world.Labels.Count>=5&&!world.Labels.Any(x=>x.Contains("Nächste Seite")),"Five public root actions fit one page without back row");
world.IncludePlayer=true;plugin.Admin(ctx);await world.Click("Spieler");await world.Click("Testspieler");
foreach(var action in new[]{"Kicken","Bannen","Chat","Team","Töten","Respawn","Hinteleportieren","Herholen","Einfrieren","Freigeben","Heilen","Rüsten","Noclip an","Noclip aus","Nachricht"}) Check(world.Labels.Any(x=>x.Contains(action)),"All player actions share selected player: "+action);
Check(!world.Labels.Any(x=>x.Contains("Weitere")||x.Contains("Nächste Seite")),"One scrolling action list without secondary branch");
await world.Click("Heilen");await world.Click("Bestätigen");Check(world.AdminCalls.Last().StartsWith("health:")&&world.AdminCalls.Last().Contains("100"),"Heal executes existing backend with 100 HP");
plugin.Admin(ctx);await world.Click("Spieler");await world.Click("Testspieler");var oldHeal=world.Find("Heilen");world.Permissions.Clear();int adminCalls=world.AdminCalls.Count;await world.Click(oldHeal);Check(world.AdminCalls.Count==adminCalls,"Revoked rights cannot execute old player action");
world.Permissions.Add("admin.respawn");plugin.Admin(ctx);await world.Click("Spieler");await world.Click("Testspieler");Check(world.Labels.Any(x=>x.Contains("Respawn"))&&!world.Labels.Any(x=>x.Contains("Kicken")),"Single special right opens player action directly");
world.Permissions.Clear();world.Permissions.Add("lplk.fluch.admin");world.TargetEnabled=true;world.AddTargetModule("LPLK_Fluch","Fluch");
plugin.Admin(ctx);await world.Click("Spieler");await world.Click("Testspieler");await world.Click("Fluch");
Check(world.TargetCalls==1,"Addon-only admin can reach target action from player without core admin rights");
world.TargetEnabled=false;await world.Click("Fluch");Check(world.TargetCalls==1,"Disabled addon action is rechecked before execution");
world.TargetEnabled=true;world.FailedPlugins.Add("LPLK_Fluch");await world.Click("Fluch");Check(world.TargetCalls==1,"Unloaded target addon cannot execute stale action");world.FailedPlugins.Clear();
world.Permissions.Clear();await world.Click("Fluch");Check(world.TargetCalls==1,"Target addon permissions rechecked");
world.Permissions.Add("lplk.fluch.admin");world.Session++;await world.Click("Fluch");Check(world.TargetCalls==1,"Reconnected target cannot receive old addon action");
world.Permissions.Clear();world.Permissions.Add("lplk.admin");world.IncludePlayer=false;plugin.PlayerMenuCommand(ctx);
var oldHelp = world.Find("Info"); world.Session++; await world.Click(oldHelp);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Old connection cannot navigate");
plugin.PlayerMenuCommand(ctx); oldHelp = world.Find("Info"); plugin.Unload(); await world.Click(oldHelp);
Check(world.Labels.Any(s => s.Contains("Adminmenü")), "Unloaded plugin callbacks ignored");
Check(typeof(Plugin).GetMethod(nameof(Plugin.Admin))!.GetCustomAttributes<CommandAlias>().Any(a => a.Alias == "adminmenu"), "Admin alias uses same permission-protected handler");
Check(world.AllMenus.Where(m => m.Configuration.Title?.StartsWith("<font") == true).All(m => m.Configuration.MaxVisibleItems == 5), "All central menus fit five rows");
await SoundFlowChecks.Run(Check);
Console.WriteLine($"{checks} Admin and sound menu checks passed (real option callbacks; fake engine boundaries).");




