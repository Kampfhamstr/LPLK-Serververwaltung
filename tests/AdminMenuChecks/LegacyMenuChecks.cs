using System.Text.Json;
using LPLK_Admin;
using SwiftlyS2.Shared.Commands;

public static class LegacyMenuChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var w=new MenuWorld {DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminMenuChecks","legacy-fixture")};
        Directory.CreateDirectory(w.DataDirectory);string path=Path.Combine(w.DataDirectory,"settings.json");
        void Settings(bool enabled)=>File.WriteAllText(path,JsonSerializer.Serialize(new AdminSettings{LegacyPlayerMenus=enabled}));
        Settings(false);w.Plugins=["headshotonly","ThirdPerson","betbank"];
        w.OwnedCommands["headshotonly"]=[new(){CommandName="rtv",RegisterRaw=true,Permission="",HelpText=""},new(){CommandName="nominate",RegisterRaw=true,Permission="",HelpText=""}];
        w.OwnedCommands["ThirdPerson"]=[new(){CommandName="thirdperson",RegisterRaw=false,Permission="camera.use",HelpText=""}];
        w.OwnedCommands["betbank"]=[new(){CommandName="bank",RegisterRaw=true,Permission="",HelpText=""}];
        var p=new Plugin(w.Core);p.Load(false);p.UseSharedInterface(w.Interfaces);
        p.PlayerMenuCommand(w.Context);check(w.Labels.Count==1,"Legacy menus require explicit per-server opt-in");
        Settings(true);p.PlayerMenuCommand(w.Context);check(w.Labels[0].Contains("Mapmenü")&&w.Labels[1].Contains("Konto"),"Legacy maps and bank preserve requested order");
        w.Plugins.Add("MapChooser");w.OwnedCommands["MapChooser"]=[new(){CommandName="rtv",RegisterRaw=false,Permission="",HelpText=""},new(){CommandName="nominate",RegisterRaw=false,Permission="",HelpText=""}];p.PlayerMenuCommand(w.Context);check(w.Labels[0].Contains("Mapmenü"),"Existing HeadshotOnly raw routes remain authoritative beside prefixed MapChooser");
        check(!w.Labels.Any(x=>x.Contains("Kamera")),"Legacy registered permission checked before showing link");
        await w.Click("Mapmenü");await w.Click("Nominieren");check(w.Commands.SequenceEqual(["nominate"]),"Legacy raw command executes as player");
        var stale=w.Find("RTV");w.FailedPlugins.Add("headshotonly");await w.Click(stale);check(w.Commands.Count==1,"Unloaded legacy owner invalidates stale link");w.FailedPlugins.Clear();
        w.Permissions.Add("camera.use");p.PlayerMenuCommand(w.Context);await w.Click("Kamera");check(w.Commands.Last()=="sw_thirdperson","Prefixed camera handler keeps its own permission and player context");
        w.Permissions.Clear();await w.Click("Kamera");check(w.Commands.Count==2,"Revoked legacy permission rechecked on click");
        w.Plugins.Add("LPLK_Maps");w.MapsEnabled=false;p.PlayerMenuCommand(w.Context);await w.Click("Mapmenü");await w.Click("RTV");check(w.Commands.Count==2,"New disabled module cannot be bypassed through old handler");
        w.Plugins.Remove("LPLK_Maps");w.OwnedCommands["conflicting"]=[new(){CommandName="rtv",RegisterRaw=true,Permission="",HelpText=""},new(){CommandName="nominate",RegisterRaw=true,Permission="",HelpText=""}];p.PlayerMenuCommand(w.Context);check(!w.Labels.Any(x=>x.Contains("Mapmenü")),"Ambiguous command ownership hides legacy routes");
        w.OwnedCommands.Remove("conflicting");p.PlayerMenuCommand(w.Context);var bank=w.Find("Konto");Settings(false);await w.Click(bank);check(w.Commands.Count==2,"Changed opt-in invalidates already-open menu");
        Settings(true);w.Plugins.AddRange(["GunsMenuSW2","k4.gotv"]);w.OwnedCommands["GunsMenuSW2"]=[new(){CommandName="guns",RegisterRaw=true,Permission="",HelpText=""}];w.OwnedCommands["k4.gotv"]=[new(){CommandName="demo",RegisterRaw=false,Permission="",HelpText=""}];
        p.PlayerMenuCommand(w.Context);check(w.Labels.FindIndex(x=>x.Contains("Konto"))<w.Labels.FindIndex(x=>x.Contains("Waffen")),"Legacy weapons stay below bank");await w.Click("Waffen");check(w.Commands.Last()=="guns","Original guns entry retained");
        p.PlayerMenuCommand(w.Context);await w.Click("Demo sichern");check(w.Commands.Last()=="sw_demo","Original requested recording stays reachable");
        w.Plugins.Add("LPLK_Demos");w.DemosEnabled=false;p.PlayerMenuCommand(w.Context);check(!w.Labels.Any(x=>x.Contains("Demo sichern")),"New disabled recorder does not fall back to old GOTV");
        p.Unload();
    }
}
