using System.Reflection;
using System.Text.Json;
using LPLK_Admin;
using SwiftlyS2.Shared.Events;

public static class IdleSettingsChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world=new MenuWorld {DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminMenuChecks","idle-fixture")};
        Directory.CreateDirectory(world.DataDirectory);
        string settings=Path.Combine(world.DataDirectory,"settings.json"),status=Path.Combine(world.DataDirectory,"status.json");
        File.WriteAllText(settings,JsonSerializer.Serialize(new AdminSettings {Reasons=["Vorher"]}));
        var plugin=new Plugin(world.Core);plugin.Load(false);
        var worldHandlers=typeof(Plugin).GetMethods().Where(m=>m.GetCustomAttributes().Any(a=>a.GetType()==typeof(EventListener<EventDelegates.OnWorldUpdate>))).ToArray();
        check(worldHandlers.Length==1,"Idle server has a world-update settings handler");
        File.WriteAllText(settings,JsonSerializer.Serialize(new AdminSettings {Reasons=["Neu","Zwei"]}));
        // Simulate a hibernating server: dispatch only world updates, never game ticks.
        foreach(var handler in worldHandlers)handler.Invoke(plugin,[]);
        using(var json=JsonDocument.Parse(File.ReadAllText(status)))check(json.RootElement.GetProperty("reasons").GetInt32()==2&&json.RootElement.GetProperty("reloadError").ValueKind==JsonValueKind.Null,"Settings refresh while game ticks are stopped");
        string first=File.ReadAllText(status);
        File.WriteAllText(settings,JsonSerializer.Serialize(new AdminSettings {Reasons=["Drei","Vier","Fuenf"]}));
        foreach(var handler in worldHandlers)handler.Invoke(plugin,[]);
        check(File.ReadAllText(status)==first,"World updates retain two-second I/O throttle");
        typeof(Plugin).GetField("nextSettingsCheck",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(plugin,0L);
        File.WriteAllText(settings,"{invalid");
        foreach(var handler in worldHandlers)handler.Invoke(plugin,[]);
        using(var json=JsonDocument.Parse(File.ReadAllText(status)))check(json.RootElement.GetProperty("reasons").GetInt32()==2&&json.RootElement.GetProperty("reloadError").ValueKind==JsonValueKind.String,"Bad idle configuration keeps last valid values and reports error");
        first=File.ReadAllText(status);plugin.Unload();
        typeof(Plugin).GetField("nextSettingsCheck",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(plugin,0L);
        foreach(var handler in worldHandlers)handler.Invoke(plugin,[]);
        check(File.ReadAllText(status)==first,"Unloaded plugin ignores later world updates");
    }
}
