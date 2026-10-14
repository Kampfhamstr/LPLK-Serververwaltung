using System.Reflection;
using LPLK_Admin;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

public class Proxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Make<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class => (T)Make(typeof(T), handler);
    public static object Make(Type type, Func<MethodInfo, object?[]?, object?> handler)
    { var proxy = Create(type, typeof(Proxy)); ((Proxy)proxy).Handler = handler; return proxy; }
    public static object? Empty(Type type) => type == typeof(void) ? null : type.IsValueType ? Activator.CreateInstance(type) : null;
}

public sealed class MenuWorld
{
    public HashSet<string> Permissions = [];
    public List<string> Plugins = ["LPLK_Maps", "LPLK_Sounds"];
    public List<string> Commands = [], Messages = [];
    public List<IMenuAPI> AllMenus = [];
    public ulong Session = 7;
    public int ModuleOpens;
    public string DataDirectory = Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!, "AdminMenuChecks", "test-data");
    public bool WeaponsEnabled;
    public bool MapsEnabled=true,SoundsEnabled=true;
    public bool StatisticsEnabled=true,DemosEnabled;
    public bool ThirdPersonEnabled;
    public bool EconomyEnabled,BettingEnabled,LotteryEnabled,RobberyEnabled,TitlesEnabled,BountyEnabled;
    public bool RejectEntityAccess;public int EntityAccesses;
    public HashSet<string> FailedPlugins=[];
    private readonly Dictionary<string, IReadOnlyDictionary<string, Delegate>> modules = [];
    public IMenuAPI? Current;
    public IPlayer Player;
    public ISwiftlyCore Core;
    public IInterfaceManager Interfaces;
    public ICommandContext Context;
    public List<string> Labels => Current!.Options.Select(o => o.Text).ToList();
    public IMenuOption Find(string text) => Current!.Options.Single(o => o.Text.Contains(text));
    public async Task Click(string label) => await Click(Find(label));
    public async Task Click(IMenuOption option) { await option.OnClickAsync(Player); }
    public MenuWorld()
    {
        Player = Proxy.Make<IPlayer>((m, a) => m.Name switch
        {
            "get_IsValid" or "get_IsAuthorized" => true, "get_IsFakeClient" => false,
            "get_SteamID" => 76561198000000000UL, "get_SessionId" => Session, "get_PlayerID" or "get_Slot" => 1,
            "SendChat" => Capture(Messages, (string)a![0]!), "ExecuteCommand" => Capture(Commands, (string)a![0]!), _ => Proxy.Empty(m.ReturnType)
        });
        Context = Proxy.Make<ICommandContext>((m, a) => m.Name switch
        { "get_Sender" => Player, "get_IsSentByPlayer" => true, "get_Args" => Array.Empty<string>(), "Reply" => Capture(Messages, (string)a![0]!), _ => Proxy.Empty(m.ReturnType) });
        Interfaces = Proxy.Make<IInterfaceManager>((m, a) =>
        { if (m.Name == "TryGetSharedInterface") { bool found = modules.TryGetValue((string)a![0]!, out var api); a[1] = api; return found; } return Proxy.Empty(m.ReturnType); });
        modules["LPLK.Player.Maps.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>MapsEnabled)};
        modules["LPLK.Player.Sounds.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>SoundsEnabled)};
        modules["LPLK.Player.Statistics.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>StatisticsEnabled)};
        modules["LPLK.Player.Demos.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>DemosEnabled)};
        modules["LPLK.Player.ThirdPerson.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>ThirdPersonEnabled)};
        modules["LPLK.Player.Economy.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,command)=>EconomyEnabled&&command=="sw_bank"||BettingEnabled&&command=="sw_bet"||LotteryEnabled&&command=="sw_lotto"||RobberyEnabled&&command=="sw_bankraub"||TitlesEnabled&&command=="sw_titel"||BountyEnabled&&command=="sw_kopfgeld")};
        var services = new Dictionary<Type, object>();
        Core = Proxy.Make<ISwiftlyCore>((m, a) =>
        {
            if (m.Name == "get_Logger") return NullLogger.Instance;
            if (m.Name == "get_PluginDataDirectory") return DataDirectory;
            if (m.Name == "get_GameFilesDirectory") return DataDirectory;
            if (!services.TryGetValue(m.ReturnType, out var service)) services[m.ReturnType] = service = Proxy.Make(m.ReturnType, Service);
            return service;
        });
    }
    private static object? Capture(List<string> list, string value) { list.Add(value); return null; }
    private object? Service(MethodInfo m, object?[]? a)
    {
        switch (m.Name)
        {
            case "GetGameRules": EntityAccesses++;if(RejectEntityAccess)throw new InvalidOperationException("Entity system not ready");return null;
            case "ExecuteCommand": Commands.Add((string)a![0]!);return null;
            case "GetAllPlugins": return Plugins.ToList();
            case "GetPluginStatus": return FailedPlugins.Contains((string)a![0]!)?PluginStatus.Unloaded:PluginStatus.Loaded;
            case "PlayerHasPermission": return Permissions.Contains((string)a![1]!);
            case "GetPlayerPermissions": return Permissions.ToList();
            case "IsCommandRegistered": return true;
            case "GetPlayer": return Player;
            case "GetAllPlayers": return new List<IPlayer>();
            case "NextWorldUpdate": ((Action)a![0]!)(); return Proxy.Empty(m.ReturnType);
            case "CreateBuilder": return Builder();
            case "GetCurrentMenu": return Current;
            case "OpenMenuForPlayer": Current = (IMenuAPI)a![1]!; return null;
        }
        return Proxy.Empty(m.ReturnType);
    }
    public void AddModule(string id, string title)
    {
        if (!Plugins.Contains(id)) Plugins.Add(id);
        modules["LPLK.Admin." + id + ".v1"] = new Dictionary<string, Delegate>
        {
            ["title"] = (Func<string>)(() => title),
            ["allowed"] = (Func<IPlayer, bool>)(_ => Permissions.Contains("lplk.admin") || Permissions.Contains("lplk." + id[5..].ToLowerInvariant() + ".admin")),
            ["open"] = (Action<IPlayer>)(_ => { ModuleOpens++; Current = Builder().Build(); })
        };
    }
    public void AddWeaponsPlayerApi()
    {
        if(!Plugins.Contains("LPLK_Weapons"))Plugins.Add("LPLK_Weapons");
        modules["LPLK.Player.Weapons.v1"]=new Dictionary<string,Delegate>{["allowed"]=(Func<IPlayer,string,bool>)((_,_)=>WeaponsEnabled)};
    }
    private IMenuBuilderAPI Builder()
    {
        var options = new List<IMenuOption>(); var config = new MenuConfiguration();
        IMenuBuilderAPI builder = null!;
        var menu = Proxy.Make<IMenuAPI>((m, a) => m.Name switch
        {
            "get_Options" => options, "get_Configuration" => config,
            "AddOption" => Add(options, (IMenuOption)a![0]!), _ => Proxy.Empty(m.ReturnType)
        });
        var design = Proxy.Make<IMenuDesignAPI>((m, a) =>
        {
            if (m.Name == "SetMenuTitle") config.Title = (string)a![0]!;
            if (m.Name == "SetMaxVisibleItems") config.MaxVisibleItems = (int)a![0]!;
            return builder;
        });
        builder = Proxy.Make<IMenuBuilderAPI>((m, a) =>
        {
            if (m.Name == "get_Design") return design;
            if (m.Name == "AddOption") options.Add((IMenuOption)a![0]!);
            if (m.Name == "Build") { AllMenus.Add(menu); return menu; }
            return builder;
        });
        return builder;
    }
    private static object? Add(List<IMenuOption> options, IMenuOption option) { options.Add(option); return null; }
}



