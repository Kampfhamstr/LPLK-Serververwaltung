using System.Text;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace LPLK_Whitelist;

[PluginMetadata(Id="LPLK_Whitelist",Name="LPLK_Whitelist",Version="0.2.2",Author="LPLK",Description="Zugangsliste, Webpanel-Befehle und !admin",MinimumAPIVersion="1.4.14")]
public sealed class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private AccessSettings settings=new();
    private HashSet<string> ids=[];
    private DateTime configStamp,listStamp;
    private long nextPoll;
    private int generation;
    private string? reloadError;
    private string ListPath=>Path.Combine(Core.PluginPath,"whitelist.txt");
    private string ConfigPath=>Path.Combine(Core.GameFilesDirectory,"addons","swiftlys2","configs","plugins","LPLK_Whitelist","config.jsonc");
    public override void Load(bool hotReload)
    {
        module=new(Core,"LPLK_Whitelist","Whitelist / Zugang","lplk.whitelist.admin",OpenAdmin,
            p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.PermissionForCommands));
        if(!File.Exists(ConfigPath))WriteSettings(new());
        if(!File.Exists(ListPath))SaveIds([]);
        Reload();module.Active=true;
        Core.GameEvent.HookPost<EventPlayerConnectFull>(e=>{if(e.UserIdPlayer is {} p)Enforce(p);return HookResult.Continue;});
        Core.Scheduler.NextWorldUpdate(()=>{if(module.Active)EnforceAll();});
        Core.Logger.LogInformation("LPLK_Whitelist 0.2.2 geladen: Modus {Mode}, {Count} Einträge, AdminBypass={Bypass}",settings.Mode,ids.Count,settings.AdminBypass);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload(){generation++;if(module is not null)module.Active=false;}
    private bool Bypass(IPlayer p)=>Core.Permission.PlayerHasPermission(p.SteamID,"lplk.admin")||Core.Permission.PlayerHasPermission(p.SteamID,"lplk.whitelist.bypass");
    private static string ReadBounded(string path)
    {
        var file=new FileInfo(path);
        if(!file.Exists||file.Length>4*1024*1024||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Zugangsdatei fehlt, ist zu groß oder verknüpft.");
        return File.ReadAllText(path);
    }
    private void Reload()
    {
        using var doc=JsonDocument.Parse(ReadBounded(ConfigPath),new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        var root=doc.RootElement.TryGetProperty("Whitelist",out var section)?section:doc.RootElement;
        var next=root.Deserialize<AccessSettings>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Konfiguration leer.");
        AccessSettings.Validate(next);var entries=AccessPolicy.Parse(ReadBounded(ListPath));
        settings=next;ids=entries;configStamp=File.GetLastWriteTimeUtc(ConfigPath);listStamp=File.GetLastWriteTimeUtc(ListPath);reloadError=null;
        WriteStatus();
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active||Environment.TickCount64<nextPoll)return;nextPoll=Environment.TickCount64+5000;
        if(configStamp==File.GetLastWriteTimeUtc(ConfigPath)&&listStamp==File.GetLastWriteTimeUtc(ListPath))return;
        try{Reload();EnforceAll();}
        catch(Exception e){if(reloadError is null)Core.Logger.LogError(e,"LPLK_Whitelist: ungültige Änderung; letzter gültiger Stand bleibt aktiv");reloadError="Ungültige Dateiänderung; letzter gültiger Stand bleibt aktiv.";WriteStatus();}
    }
    [EventListener<EventDelegates.OnClientSteamAuthorize>]
    public void Authorized(IOnClientSteamAuthorizeEvent e)
    {
        int slot=e.PlayerId,serial=generation;var player=Core.PlayerManager.GetPlayer(slot);if(player is null)return;ulong steamId=player.SteamID;
        Core.Scheduler.NextWorldUpdate(()=>{var current=Core.PlayerManager.GetPlayer(slot);if(module.Active&&serial==generation&&current is not null&&current.SteamID==steamId)Enforce(current);});
    }
    private void Enforce(IPlayer p)
    {
        if(!module.Active||!p.IsValid||p.IsFakeClient||!p.IsAuthorized)return;
        if(!AccessPolicy.Allows(settings,ids.Contains(p.SteamID.ToString()),Bypass(p)))p.Kick(settings.KickMessage,ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
    }
    private void EnforceAll(){foreach(var p in Core.PlayerManager.GetAllPlayers())Enforce(p);}
    private void SaveIds(HashSet<string> next)
    {
        var tmp=ListPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(tmp,string.Join('\n',next.Order())+"\n",new UTF8Encoding(false));File.Move(tmp,ListPath,true);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
        ids=next;listStamp=File.GetLastWriteTimeUtc(ListPath);
    }
    private void Change(string id,bool add,Action<string> reply)
    {
        if(!AccessPolicy.ValidId(id)){reply("[LPLK] Bitte eine gültige SteamID64 angeben.");return;}
        // Merge with current persisted data; invalid external edits cannot be silently overwritten.
        Reload();var next=new HashSet<string>(ids,StringComparer.Ordinal);bool changed=add?next.Add(id):next.Remove(id);
        if(changed)SaveIds(next);
        WriteStatus();reply(changed?(add?"[LPLK] Eintrag dauerhaft gespeichert.":"[LPLK] Eintrag dauerhaft entfernt."):"[LPLK] Eintrag bereits im gewünschten Zustand.");
        if(changed)EnforceAll();
    }
    [Command("wl")][CommandAlias("lplk_wl_add")]
    public void Add(ICommandContext c)=>CommandChange(c,true);
    [Command("uwl")][CommandAlias("lplk_wl_remove")]
    public void Remove(ICommandContext c)=>CommandChange(c,false);
    private void CommandChange(ICommandContext c,bool add)
    {
        if(c.Sender is {} p&&!module.CanAdmin(p)){c.Reply("[LPLK] Keine Berechtigung.");return;}
        if(c.Args.Length!=1){c.Reply(add?"wl <SteamID64>":"uwl <SteamID64>");return;}
        try{Change(c.Args[0],add,c.Reply);}catch(Exception e){Core.Logger.LogError(e,"Whitelist-Dateiänderung fehlgeschlagen");c.Reply("[LPLK] Änderung nicht gespeichert; bisherige Liste bleibt aktiv.");}
    }
    [Command("lplk_whitelist")]
    public void Status(ICommandContext c)
    {
        if(c.Sender is {} p){if(module.CanAdmin(p))OpenAdmin(p);else c.Reply("[LPLK] Keine Berechtigung.");return;}
        if(c.Args.Length==2&&c.Args[0]=="check"&&AccessPolicy.ValidId(c.Args[1]))
        {c.Reply($"LPLK_Whitelist: listed={ids.Contains(c.Args[1])}; allowedWithoutBypass={AccessPolicy.Allows(settings,ids.Contains(c.Args[1]),false)}");return;}
        c.Reply($"LPLK_Whitelist: enabled={settings.Enabled}; mode={settings.Mode}; entries={ids.Count}; adminBypass={settings.AdminBypass}; persisted={File.Exists(ListPath)}; reloadError={reloadError??"none"}");
    }
    private void WriteSettings(AccessSettings value)=>AtomicJson.Write(ConfigPath,new{Whitelist=new{value.Enabled,value.Mode,value.AdminBypass,value.KickMessage,value.PermissionForCommands,AddCommand="wl",RemoveCommand="uwl"}});
    private void WriteStatus()=>AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,settings.Enabled,settings.Mode,settings.AdminBypass,entries=ids.Count,reloadError});
    private void Apply(AccessSettings next,IPlayer p)
    {
        if(!module.CanAdmin(p))return;AccessSettings.Validate(next);WriteSettings(next);Reload();EnforceAll();OpenAdmin(p);
    }
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · Whitelist / Zugang",
        ($"Aktiv: {(settings.Enabled?"Ja":"Nein")}",a=>Apply(settings with{Enabled=!settings.Enabled},a)),
        ($"Modus: {(settings.Mode==1?"Whitelist":"Blacklist")}",a=>Apply(settings with{Mode=settings.Mode==1?2:1},a)),
        ($"Admin-Ausnahme: {(settings.AdminBypass?"Ja":"Nein")}",a=>Apply(settings with{AdminBypass=!settings.AdminBypass},a)),
        ("SteamID hinzufügen",a=>Input(a,true)),("SteamID entfernen",a=>Input(a,false)),
        ($"Einträge ansehen ({ids.Count})",a=>ShowEntries(a,0)),
        ("Dateien neu laden",a=>{Reload();EnforceAll();OpenAdmin(a);}));
    private void ShowEntries(IPlayer p,int page)
    {
        var ordered=ids.Order().ToArray();page=Math.Clamp(page,0,Math.Max(0,(ordered.Length-1)/10));
        var actions=ordered.Skip(page*10).Take(10).Select(id=>(id,(Action<IPlayer>)(a=>module.Menu(a,"LPLK · "+id,1,("Eintrag entfernen",b=>{Change(id,false,b.SendChat);ShowEntries(b,page);}),("Zurück",b=>ShowEntries(b,page)))))).ToList();
        if(page>0)actions.Add(("Vorherige Seite",a=>ShowEntries(a,page-1)));
        if((page+1)*10<ordered.Length)actions.Add(("Nächste Seite",a=>ShowEntries(a,page+1)));
        actions.Add(("Zurück",OpenAdmin));module.Menu(p,"LPLK · Einträge / Seite "+(page+1),1,actions.ToArray());
    }
    private void Input(IPlayer player,bool add)
    {
        if(!module.CanAdmin(player))return;ulong owner=player.SteamID;int serial=generation;
        var input=new InputMenuOption(new MenuNumbers().Next("SteamID64"),maxLength:17,validator:AccessPolicy.ValidId,hintMessage:"SteamID64 in den Chat eingeben.");
        GameThreadMenu.BindInput(Core,input,player,()=>module.Active&&serial==generation,(a,value)=>{if(module.CanAdmin(a)){try{Change(value,add,a.SendChat);}catch(Exception e){Core.Logger.LogError(e,"Whitelist-Eingabe fehlgeschlagen");a.SendChat("[LPLK] Änderung nicht gespeichert.");}}});
        Core.MenusAPI.OpenMenuForPlayer(player,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(add?"LPLK · Eintrag hinzufügen":"LPLK · Eintrag entfernen")).Design.SetMaxVisibleItems(5).AddOption(input).Build());
    }
}
