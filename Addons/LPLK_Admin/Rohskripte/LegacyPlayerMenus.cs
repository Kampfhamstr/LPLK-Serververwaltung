using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    // Transitional links to reviewed, existing handlers. The owning plugin keeps
    // all feature/configuration checks; these links never execute as the console.
    private static readonly Dictionary<string,(string Owner,string Name)[]> LegacyRoutes = new()
    {
        ["sw_rtv"] = [("headshotonly","rtv"),("MapChooser","rtv"),("swiftsurf","rtv")],
        ["sw_nominate"] = [("headshotonly","nominate"),("MapChooser","nominate"),("swiftsurf","nominate")],
        ["sw_revote"] = [("headshotonly","revote"),("MapChooser","revote"),("swiftsurf","revote")],
        ["sw_nextmap"] = [("headshotonly","nextmap"),("MapChooser","nextmap"),("swiftsurf","nextmap")],
        ["sw_tp"] = [("ThirdPerson","thirdperson")],
        ["sw_guns"] = [("GunsMenuSW2","guns")],
        ["sw_demo"] = [("k4.gotv","demo")],
        ["sw_stats"] = [("swiftsurf","profile")],
        ["sound"] = [("LPLK_Sounds","sound")],
        ["sw_bank"] = [("betbank","bank")],
        ["sw_bet"] = [("betbank","bet")],
        ["sw_kopfgeld"] = [("betbank","kopfgeld")],
        ["sw_bankraub"] = [("betbank","bankraub")],
        ["sw_lotto"] = [("betbank","lotto")],
        ["sw_titel"] = [("betbank","titel")]
    };
    private string? LegacyCommand(IPlayer player,string modern,string feature)
    {
        if(!settings.LegacyPlayerMenus||!Connected(player)||!LegacyRoutes.TryGetValue(feature,out var routes))return null;
        // Never bypass the new module's disabled state, missing capability or unload.
        if(Core.PluginManager.GetAllPlugins().Contains(modern) && modern!="LPLK_Sounds")return null;
        if(modern=="LPLK_Sounds"&&interfaces is not null&&interfaces.TryGetSharedInterface<IReadOnlyDictionary<string,Delegate>>("LPLK.Player.Sounds.v1",out _))return null;
        string Exact(CommandInfo c)=>c.RegisterRaw?c.CommandName:"sw_"+c.CommandName;
        var candidates=new List<string>();
        foreach(var route in routes)
        {
            // These production profiles already use HeadshotOnly's raw chat
            // commands. A second prefixed MapChooser handler must not hide them.
            if(route.Owner=="MapChooser"&&Core.PluginManager.GetAllPlugins().Contains("headshotonly"))continue;
            if(!Loaded(route.Owner))continue;
            foreach(var info in Core.Command.GetCommandsByPlugin(route.Owner))
            {
                string exact=Exact(info);
                if(info.CommandName!=route.Name||!Core.Command.IsCommandRegistered(exact))continue;
                if(!string.IsNullOrEmpty(info.Permission)&&!Has(player,info.Permission))continue;
                // Refuse ambiguous ownership instead of selecting a handler by load order.
                if(Core.Command.GetAllCommandsInfo().Count(c=>Exact(c)==exact)!=1)continue;
                candidates.Add(exact);
            }
        }
        return candidates.Distinct().Count()==1?candidates[0]:null;
    }
    private bool PublicFeatureAllowed(IPlayer p,string id,string command)=>PlayerFeatureAllowed(p,id,command)||LegacyCommand(p,id,command) is not null;
    private void PublicCommand(IPlayer p,string id,string command)
    {
        if(PlayerFeatureAllowed(p,id,command)){PlayerCommand(p,id,command);return;}
        if(LegacyCommand(p,id,command) is {} old){p.ExecuteCommand(old);return;}
        p.SendLplkChat("[LPLK] Funktion derzeit nicht verfügbar.");
    }
    private bool PublicMapsAvailable(IPlayer p)=>Loaded("LPLK_Surf")?ModeApi(p,"Surf","maps") is not null:Loaded("LPLK_Maps")||new[]{"sw_rtv","sw_nominate","sw_revote","sw_nextmap"}.Any(c=>LegacyCommand(p,"LPLK_Maps",c) is not null);
}
