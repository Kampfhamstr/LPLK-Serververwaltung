using System.Text.Json;
using LPLK_Admin;
using SwiftlyS2.Shared.Commands;

public static class LegacyAdminChecks
{
    public static void Run(Action<bool,string> check)
    {
        var w=new MenuWorld {DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminMenuChecks","legacy-admin"),Plugins=[]};
        Directory.CreateDirectory(w.DataDirectory);
        var settings=new AdminSettings{LegacyPlayerMenus=true,LegacyGameMaps=new(){{"de_dust2","Dust 2"}},LegacyWorkshopMaps=new(){{"Inferno Online",3549919360}}};
        File.WriteAllText(Path.Combine(w.DataDirectory,"settings.json"),JsonSerializer.Serialize(settings));
        var p=new Plugin(w.Core);p.Load(false);p.UseSharedInterface(w.Interfaces);
        ICommandContext C(params string[] args)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch {"get_Sender"=>w.Player,"get_IsSentByPlayer"=>true,"get_Args"=>args,"Reply"=>Capture((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
        object? Capture(string text){w.Messages.Add(text);return null;}
        p.LegacyMap(C("de_dust2"));check(w.Delayed.Count==0,"Map command requires original admin.map right");
        w.Permissions.Add("admin.map");p.LegacyMap(C("de_dust2;quit"));check(w.Delayed.Count==0,"Map command injection and unknown maps rejected");
        p.LegacyMap(C("de_dust2"));check(w.Delayed.Count==1,"Reviewed map scheduled with original delay");w.Delayed[0]();check(w.Commands.SequenceEqual(["changelevel de_dust2"]),"Reviewed map reaches fixed native command");
        p.LegacyWorkshop(C("Inferno"));w.Delayed[^1]();check(w.Commands.Last()=="host_workshop_map 3549919360","Legacy named Workshop map retained");
        p.LegacyWorkshop(C("3070192462"));w.Permissions.Clear();int count=w.Commands.Count;w.Delayed[^1]();check(w.Commands.Count==count,"Revoked map right invalidates pending change");
        w.Permissions.Add("admin.map");p.LegacyMap(C("de_dust2"));w.Session++;w.Delayed[^1]();check(w.Commands.Count==count,"Reconnected admin cannot execute previous delayed change");
        p.LegacyMap(C("de_dust2"));w.Plugins.Add("LPLK_Maps");w.Delayed[^1]();check(w.Commands.Count==count,"New map module prevents pending legacy command");
        w.Permissions.Clear();p.AddAdmin(C("any"));check(w.Messages.Last().Contains("Keine Berechtigung"),"Role alias keeps root boundary");
        w.Permissions.Add("admin.root");p.AddAdmin(C("any"));check(w.Messages.Last().Contains("verwaltung.lplk.de")&&w.Commands.Count==count&&w.AdminCalls.Count==0,"Role changes remain central without local permission mutation");
        bool failed=false;try{AdminSettingsFile.Validate(settings with{LegacyGameMaps=new(){{"de_dust2;quit","bad"}}});}catch(InvalidDataException){failed=true;}check(failed,"Unsafe configured map rejected before load");
        var copy=AdminSettingsFile.Copy(settings);copy.LegacyGameMaps["de_dust2"]="changed";check(!AdminSettingsFile.Same(copy,settings)&&settings.LegacyGameMaps["de_dust2"]=="Dust 2","Map config changes detected without aliasing old snapshot");
        p.Unload();
    }
}
