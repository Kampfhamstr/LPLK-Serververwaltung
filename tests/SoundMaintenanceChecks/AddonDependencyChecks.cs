using System.Reflection;
using System.Text;
using System.Text.Json;
using LPLK.Common;
using LPLK_Sounds;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;

public static class AddonDependencyChecks
{
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,
            "SoundMaintenanceChecks","addon-dependency-fixture");
        string game=Path.Combine(root,"game");
        string configs=Path.Combine(game,"addons","swiftlys2","configs","plugins");
        string ownConfig=Path.Combine(configs,"LPLK_Sounds");
        string managerConfig=Path.Combine(configs,"AddonsManager","config.jsonc");
        var world=new MenuWorld{DataDirectory=Path.Combine(root,"data")};
        Directory.CreateDirectory(ownConfig);
        Directory.CreateDirectory(Path.GetDirectoryName(managerConfig)!);
        Directory.CreateDirectory(world.DataDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory,"catalog.json"),Path.Combine(world.DataDirectory,"catalog.json"),true);

        // Keep the manager's config outside every tree RoundFileStore normally primes.
        var core=Proxy.Make<ISwiftlyCore>((m,a)=>m.Name switch
        {
            "get_GameFilesDirectory"=>game,
            "get_Configuration"=>Proxy.Make(m.ReturnType,(op,v)=>op.Name=="GetConfigPath"
                ?Path.Combine(ownConfig,(string)v![0]!)
                :Proxy.Empty(op.ReturnType)),
            "get_GameFileSystem"=>Proxy.Make(m.ReturnType,(op,_)=>op.Name=="FileExists"?true:Proxy.Empty(op.ReturnType)),
            _=>m.Invoke(world.Core,a)
        });
        ICommandContext Context(bool player,Action<string> reply)=>Proxy.Make<ICommandContext>((m,a)=>
        {
            if(m.Name=="Reply"){reply((string)a![0]!);return null;}
            return m.Name switch
            {
                "get_Sender"=>player?world.Player:null,
                "get_IsSentByPlayer"=>player,
                "get_Args"=>player?new[]{"1"}:Array.Empty<string>(),
                _=>Proxy.Empty(m.ReturnType)
            };
        });
        string ConsoleStatus(Plugin plugin)
        {
            string result="";
            plugin.Status(Context(false,value=>result=value));
            return result;
        }
        var manifest=new List<string>();
        var precache=Proxy.Make<IOnPrecacheResourceEvent>((m,a)=>
        {
            if(m.Name=="AddItem")manifest.Add((string)a![0]!);
            return Proxy.Empty(m.ReturnType);
        });
        var precacheMethod=typeof(Plugin).GetMethod("OnPrecache",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var onTickMethod=typeof(Plugin).GetMethod("OnTick",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var cases=new(string Name,string? Document,string Expected,bool RequiredAtLoad)[]
        {
            ("valid late-load dependency","{ // valid JSONC\n \"Main\":{\"Addons\":[\"3797047889\",\"3803459186\",],}, }","bereit",true),
            ("initially optional dependency","{\"main\":{\"aDdOnS\":[\"3797047889\"]}}","bereit",false),
            ("configured but missing sound ID","{\"Main\":{\"Addons\":[\"3803459186\"]}}","Sound-Workshop-ID fehlt im AddonsManager.",true),
            ("malformed dependency","{\"Main\":{\"Addons\":false}}","AddonsManager-Konfiguration nicht lesbar.",true),
            ("ambiguous dependency","{\"Main\":{\"Addons\":[\"3797047889\"]},\"main\":{\"Addons\":[]}}","AddonsManager-Konfiguration nicht lesbar.",true),
            ("oversized dependency",new string(' ',4*1024*1024+1),"AddonsManager-Konfiguration nicht lesbar.",true),
            ("missing dependency",null,"AddonsManager-Konfiguration nicht lesbar.",true)
        };
        foreach(var scenario in cases)
        {
            RoundFileStore.ResetForChecks();
            File.WriteAllText(Path.Combine(ownConfig,"config.json"),JsonSerializer.Serialize(new SoundsConfig
            {
                AutoCatalogFromAddon=false,RequireAddonsManager=scenario.RequiredAtLoad
            }));
            if(scenario.Document is null)File.Delete(managerConfig);
            else File.WriteAllText(managerConfig,scenario.Document,new UTF8Encoding(true));
            world.FailedPlugins.Add("AddonsManager");
            var plugin=new Plugin(core);
            plugin.Load(false);
            try
            {
                if(scenario.RequiredAtLoad)
                    check(ConsoleStatus(plugin).Contains("AddonsManager ist nicht geladen."),scenario.Name+": initial unloaded manager is rejected");
                else
                    check(ConsoleStatus(plugin).Contains("bereit"),scenario.Name+": optional manager does not block");

                world.FailedPlugins.Remove("AddonsManager");
                manifest.Clear();
                precacheMethod.Invoke(plugin,[precache]);
                onTickMethod.Invoke(plugin,null);
                if(!scenario.RequiredAtLoad)
                {
                    var config=(SoundsConfig)typeof(Plugin).GetField("config",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plugin)!;
                    config.RequireAddonsManager=true;
                }
                string state=ConsoleStatus(plugin);
                check(manifest.Count==89&&state.Contains("88/88 vorgeladen"),scenario.Name+": genuine precache still registers bank and 88 sounds");
                check(state.Contains(scenario.Expected),scenario.Name+": expected dependency status, got "+state);
                if(scenario.Expected=="bereit")
                {
                    File.WriteAllText(managerConfig,"{invalid after plugin load");
                    check(ConsoleStatus(plugin).Contains("bereit"),scenario.Name+": playback reuses the physical load snapshot");
                    string rejection="";
                    plugin.SoundMenuCommand(Context(true,value=>rejection=value));
                    check(rejection.Contains("Keine passenden Zuhoerer"),scenario.Name+": normal numbered command passes readiness instead of rejecting a valid config");
                    world.FailedPlugins.Add("AddonsManager");
                    check(ConsoleStatus(plugin).Contains("AddonsManager ist nicht geladen."),scenario.Name+": live manager unload still closes playback");
                }
                check(RoundFileStore.GameThreadDiskReads==0,scenario.Name+": no gameplay disk reads");
            }
            finally
            {
                plugin.Unload();
                RoundFileStore.ResetForChecks();
            }
        }
    }
}
