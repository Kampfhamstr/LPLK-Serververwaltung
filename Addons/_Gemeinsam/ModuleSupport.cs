using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;

namespace LPLK.Common;

// Numbers identify visible actions. Navigation is explicitly left unnumbered by callers.
public sealed class MenuNumbers
{
    private int last;
    public string Next(string label,int? existingNumber=null,bool? enabled=null)
    {
        int number=existingNumber??(last+1);last=Math.Max(last,number);
        return MenuColors.Paint($"{number}. {label}",enabled is true?MenuColors.Green:enabled is false?MenuColors.Red:MenuColors.Yellow);
    }
}

public static class MenuColors
{
    public const string White="#FFFFFF",Yellow="#FFD966",Green="#70E080",Red="#FF7070";
    public static string Paint(string text,string color)=>$"<font color='{color}'>{text}</font>";
    public static string Heading(string text)=>Paint(text,White);
    public static string Back(string text)=>Paint(text,White);
    // Config-menu labels are controlled by us. Never apply this inference to player, map or sound names.
    public static bool? SwitchValue(string label)
    {
        foreach(string suffix in new[]{": Ja",": True",": AN"})if(label.EndsWith(suffix,StringComparison.Ordinal))return true;
        foreach(string suffix in new[]{": Nein",": False",": AUS"})if(label.EndsWith(suffix,StringComparison.Ordinal))return false;
        return null;
    }
}

// The shared contract uses BCL delegates and the framework's IPlayer only.
// Each plugin can be rebuilt independently without sharing implementation assemblies.
public sealed class ModuleSupport(ISwiftlyCore core, string id, string title, string permission, Action<IPlayer> openAdmin, Func<IPlayer,bool>? additionalAdmin = null)
{
    private ISwiftlyCore Core => core;
    public bool Active;
    public static string AdminKey(string moduleId) => "LPLK.Admin." + moduleId + ".v1";
    public bool CanAdmin(IPlayer player) => Active && player.IsValid && !player.IsFakeClient &&
        (Core.Permission.PlayerHasPermission(player.SteamID, "lplk.admin") || Core.Permission.PlayerHasPermission(player.SteamID, permission) || additionalAdmin?.Invoke(player)==true);

    public void ConfigureSharedInterface(IInterfaceManager manager) =>
        manager.AddSharedInterface<IReadOnlyDictionary<string, Delegate>, Dictionary<string, Delegate>>(AdminKey(id), new()
        {
            ["title"] = (Func<string>)(() => title),
            ["allowed"] = (Func<IPlayer, bool>)CanAdmin,
            ["open"] = (Action<IPlayer>)(p => { if (CanAdmin(p)) openAdmin(p); })
        });

    public void Menu(IPlayer player, string caption, params (string Label, Action<IPlayer> Action)[] actions)
        => Menu(player,caption,0,actions);

    public void Menu(IPlayer player, string caption, int unnumberedLast, params (string Label, Action<IPlayer> Action)[] actions)
    {
        if (!CanAdmin(player)) return;
        var menu = Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false)
            .Design.SetMenuTitle(MenuColors.Heading(caption)).Design.SetMaxVisibleItems(5);
        var numbers=new MenuNumbers();int index=0;
        foreach (var (label, action) in actions)
        {
            var button = new ButtonMenuOption(index++<actions.Length-unnumberedLast?numbers.Next(label,enabled:MenuColors.SwitchValue(label)):MenuColors.Back(label)) { CloseAfterClick=false };
            GameThreadMenu.Bind(Core,button,player,()=>Active,args =>
            {
                if (CanAdmin(args))
                {
                    try { action(args); }
                    catch (Exception e)
                    {
                        Core.Logger.LogError(e, "{Module}: Einstellung konnte nicht übernommen werden", id);
                        args.SendChat("[LPLK] Änderung fehlgeschlagen; bitte Serverprotokoll prüfen.");
                    }
                }
            });
            menu.AddOption(button);
        }
        Core.MenusAPI.OpenMenuForPlayer(player, menu.Build());
    }

    public string SettingsPath => Path.Combine(Core.PluginDataDirectory, "settings.json");
    public T ReadSettings<T>(Action<T> validate) where T : new()
    {
        T settings;
        if (File.Exists(SettingsPath))
            settings = JsonSerializer.Deserialize<T>(File.ReadAllText(SettingsPath)) ?? throw new InvalidDataException("Ungültige Einstellungen");
        else settings = new T();
        validate(settings);
        if (!File.Exists(SettingsPath)) SaveSettings(settings);
        return settings;
    }

    public void SaveSettings<T>(T settings) => AtomicJson.Write(SettingsPath, settings);
}

public static class GameThreadMenu
{
    public static void Bind(ISwiftlyCore core,ButtonMenuOption option,IPlayer owner,Func<bool> ready,Action<IPlayer> action)
    {
        int slot=owner.PlayerID;ulong steam=owner.SteamID;string session=owner.SessionId.ToString();int pending=0;

        option.Click+=(_,_)=>
        {
            if(Interlocked.Exchange(ref pending,1)!=0)return ValueTask.CompletedTask;
            try { core.Scheduler.NextWorldUpdate(()=>
            {
                try { var p=core.PlayerManager.GetPlayer(slot);if(ready()&&p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&p.SteamID==steam&&p.SessionId.ToString()==session&&core.MenusAPI.GetCurrentMenu(p)?.Options.Contains(option)==true)action(p); }
                catch(Exception e){core.Logger.LogError(e,"LPLK-Menüaktion fehlgeschlagen");}
                finally{Interlocked.Exchange(ref pending,0);}
            }); } catch(Exception e){Interlocked.Exchange(ref pending,0);core.Logger.LogWarning(e,"LPLK-Menüaktion konnte nicht eingeplant werden");}
            return ValueTask.CompletedTask;
        };
    }
    public static void BindInput(ISwiftlyCore core,InputMenuOption option,IPlayer owner,Func<bool> ready,Action<IPlayer,string> action)
    {
        int slot=owner.PlayerID;ulong steam=owner.SteamID;string session=owner.SessionId.ToString();int pending=0;
        option.ValueChanged+=(_,a)=>
        {
            string value=a.NewValue;if(Interlocked.Exchange(ref pending,1)!=0)return;
            try { core.Scheduler.NextWorldUpdate(()=>
            {
                try {var p=core.PlayerManager.GetPlayer(slot);if(ready()&&p is {IsValid:true,IsAuthorized:true,IsFakeClient:false}&&p.SteamID==steam&&p.SessionId.ToString()==session&&core.MenusAPI.GetCurrentMenu(p)?.Options.Contains(option)==true)action(p,value);}
                catch(Exception e){core.Logger.LogError(e,"LPLK-Menüeingabe fehlgeschlagen");}
                finally{Interlocked.Exchange(ref pending,0);}
            }); } catch(Exception e){Interlocked.Exchange(ref pending,0);core.Logger.LogWarning(e,"LPLK-Menüeingabe konnte nicht eingeplant werden");}
        };
    }
}

public static class AtomicJson
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
