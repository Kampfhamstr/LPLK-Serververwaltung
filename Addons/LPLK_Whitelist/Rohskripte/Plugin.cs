using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
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

[PluginMetadata(Id="LPLK_Whitelist",Name="LPLK_Whitelist",Version="0.2.13",Author="LPLK",Description="Zugangsliste, Webpanel-Befehle und !admin",MinimumAPIVersion="1.4.14")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private ModuleSupport module=null!;
    private AccessSettings settings=new();
    private HashSet<string> ids=[];
    private AccessSnapshot snapshot=null!;
    private bool statusWriteFailed;
    private long nextPoll;
    private int generation;
    private string? reloadError;
    private string ListPath=>Path.Combine(LPLK.Common.RoundFileStore.PluginPath(Core),"whitelist.txt");
    private string ConfigPath=>Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"addons","swiftlys2","configs","plugins","LPLK_Whitelist","config.jsonc");
    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Whitelist",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Whitelist","Zugang","lplk.whitelist.admin",OpenAdmin,
            p=>Core.Permission.PlayerHasPermission(p.SteamID,settings.PermissionForCommands));
        if(!File.Exists(ConfigPath))WriteSettings(new());
        if(!File.Exists(ListPath))SaveIds([]);
        // Prime contents at load; metadata alone is insufficient for later RAM streams.
        _=File.ReadAllText(ListPath);
        Reload();module.Active=true;
        Core.GameEvent.HookPost<EventPlayerConnectFull>(e=>{if(e.UserIdPlayer is {} p)Enforce(p);return HookResult.Continue;});
        Core.Scheduler.NextWorldUpdate(()=>{if(module.Active)EnforceAll();});
        Core.Logger.LogInformation("LPLK_Whitelist 0.2.13 geladen: Modus {Mode}, {Count} Einträge, AdminBypass={Bypass}",settings.Mode,ids.Count,settings.AdminBypass);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)=>module.ConfigureSharedInterface(manager);
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore(){generation++;if(module is not null)module.Active=false;RemoveConfiguredCommands(); LPLK.Common.DeferredJson.Drain(); }
    private bool Bypass(IPlayer p)=>Core.Permission.PlayerHasPermission(p.SteamID,"lplk.admin")||Core.Permission.PlayerHasPermission(p.SteamID,"lplk.whitelist.bypass");
    private bool Reload()
    {
        var next=AccessFiles.Read(ConfigPath,ListPath);
        bool changed=settings!=next.Settings||!ids.SetEquals(next.Ids);
        snapshot=next;settings=next.Settings;ids=next.Ids;reloadError=null;ReconcileCommands();WriteStatus();return changed;
    }
    [EventListener<EventDelegates.OnTick>]
    public void Tick()
    {
        if(!module.Active||Environment.TickCount64<nextPoll)return;nextPoll=Environment.TickCount64+5000;
        try{if(Reload())EnforceAll();}
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
        ids=next;
    }
    private void Change(string id,bool add,Action<string> reply,IPlayer? actor=null,Func<bool>? stillCurrent=null)
    {
        if(!AccessPolicy.ValidId(id)){reply("[LPLK] Bitte eine gültige SteamID64 angeben.");return;}
        // Merge with current persisted data; invalid external edits cannot be silently overwritten.
        bool rulesChanged=Reload();if(rulesChanged)EnforceAll();
        if(stillCurrent is not null&&!stillCurrent()){reply("[LPLK] Befehl inzwischen geändert; aktuellen Namen verwenden.");return;}
        if(actor is not null&&!module.CanAdmin(actor)){reply("[LPLK] Keine Berechtigung nach Aktualisierung der Zugangsregeln.");return;}
        var next=new HashSet<string>(ids,StringComparer.Ordinal);bool changed=add?next.Add(id):next.Remove(id);
        if(changed){AccessFiles.WriteIds(ConfigPath,ListPath,snapshot,next);Reload();}
        WriteStatus();reply(changed?(add?"[LPLK] Eintrag dauerhaft gespeichert.":"[LPLK] Eintrag dauerhaft entfernt."):"[LPLK] Eintrag bereits im gewünschten Zustand.");
        if(changed)EnforceAll();
    }
    [Command("wl")][CommandAlias("lplk_wl_add")]
    public void Add(ICommandContext c)=>CommandChange(c,true);
    [Command("uwl")][CommandAlias("lplk_wl_remove")]
    public void Remove(ICommandContext c)=>CommandChange(c,false);
    private void CommandChange(ICommandContext c,bool add,Func<bool>? stillCurrent=null)
    {
        if(!module.Active)return;
        if(c.Sender is {} p&&!module.CanAdmin(p)){c.ReplyLplk("[LPLK] Keine Berechtigung.");return;}
        if(c.Args.Length!=1){c.ReplyLplk((add?activeCommandNames.Add:activeCommandNames.Remove)+" <SteamID64>");return;}
        try{Change(c.Args[0],add,c.ReplyLplk,c.Sender,stillCurrent);}catch(Exception e){Core.Logger.LogError(e,"Whitelist-Dateiänderung fehlgeschlagen");c.ReplyLplk("[LPLK] Änderung nicht gespeichert; bisherige Liste bleibt aktiv.");}
    }
    [Command("lplk_whitelist")]
    public void Status(ICommandContext c)
    {
        if(c.Sender is {} p){if(module.CanAdmin(p))OpenAdmin(p);else c.ReplyLplk("[LPLK] Keine Berechtigung.");return;}
        if(c.Args.Length==2&&c.Args[0]=="check"&&AccessPolicy.ValidId(c.Args[1]))
        {c.ReplyLplk($"LPLK_Whitelist: listed={ids.Contains(c.Args[1])}; allowedWithoutBypass={AccessPolicy.Allows(settings,ids.Contains(c.Args[1]),false)}");return;}
        c.ReplyLplk($"LPLK_Whitelist 0.2.13: enabled={settings.Enabled}; mode={settings.Mode}; entries={ids.Count}; adminBypass={settings.AdminBypass}; persisted={File.Exists(ListPath)}; reloadError={reloadError??"none"}; addCommand=sw_{activeCommandNames.Add}; removeCommand=sw_{activeCommandNames.Remove}; commandError={commandError??"none"}");
    }
    private void WriteSettings(AccessSettings value)=>AtomicJson.Write(ConfigPath,new{Whitelist=new{value.Enabled,value.Mode,value.AdminBypass,value.KickMessage,value.PermissionForCommands,AddCommand="wl",RemoveCommand="uwl"}});
    private void WriteStatus()
    {
        try{LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.2.13",settings.Enabled,settings.Mode,settings.AdminBypass,entries=ids.Count,reloadError,addCommand="sw_"+activeCommandNames.Add,removeCommand="sw_"+activeCommandNames.Remove,commandError});statusWriteFailed=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!statusWriteFailed)Core.Logger.LogWarning(e,"LPLK_Whitelist: Status nicht schreibbar.");statusWriteFailed=true;}
    }
    private void Apply(Func<AccessSettings,AccessSettings> edit,IPlayer p)
    {
        if(!module.CanAdmin(p))return;
        try
        {
            if(Reload())EnforceAll();if(!module.CanAdmin(p)){p.SendLplkChat("[LPLK] Keine Berechtigung nach Aktualisierung der Zugangsregeln.");return;}
            var next=edit(settings);AccessFiles.WriteSettings(ConfigPath,ListPath,snapshot,next);Reload();EnforceAll();
        }
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Whitelist: Änderung nicht gespeichert");p.SendLplkChat("[LPLK] Nicht gespeichert. Aktuelle Zugangsdateien prüfen.");}
        OpenRules(p);
    }
    private void OpenAdmin(IPlayer p) => module.Menu(p,"LPLK · Zugang",((Func<IPlayer,(string,Action<IPlayer>)[]>)(actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("OpenRules",OpenRulesRows(actor)))))(p));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{("Einträge",OpenEntries)}))(p);
    private void OpenRules(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> OpenRulesRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Aktiv: {(settings.Enabled?"Ja":"Nein")}",a=>Apply(s=>s with{Enabled=!s.Enabled},a)),($"Modus: {(settings.Mode==1?"Whitelist":"Blacklist")}",a=>Apply(s=>s with{Mode=s.Mode==1?2:1},a)),($"Admin-Ausnahme: {(settings.AdminBypass?"Ja":"Nein")}",a=>Apply(s=>s with{AdminBypass=!s.AdminBypass},a)),("Neuladen",a=>{if(Reload())EnforceAll();OpenRules(a);})}))(p);
    private void OpenEntries(IPlayer p)=>module.Menu(p,"Einträge",1,
        ("Hinzufügen",a=>Input(a,true)),("Entfernen",a=>Input(a,false)),($"Liste ({ids.Count})",a=>ShowEntries(a,0)),("Zurück",OpenAdmin));
    private void ShowEntries(IPlayer p,int page)
    {
        var actions=ids.Order().Select(id=>(id,(Action<IPlayer>)(a=>module.Menu(a,"LPLK · "+id,1,("Eintrag entfernen",b=>{Change(id,false,b.SendLplkChat,b);ShowEntries(b,0);}),("Zurück",b=>ShowEntries(b,0)))))).ToList();
        actions.Add(("Zurück",OpenEntries));module.Menu(p,"LPLK · Einträge",1,actions.ToArray());
    }
    private void Input(IPlayer player,bool add)
    {
        if(!module.CanAdmin(player))return;int serial=generation;
        var input=new InputMenuOption(new MenuNumbers().Next("SteamID64"),maxLength:18,validator:AccessPolicy.ValidId,hintMessage:"SteamID64 in den Chat eingeben.");
        GameThreadMenu.BindInput(Core,input,player,()=>module.Active&&serial==generation,(a,value)=>{if(module.CanAdmin(a)){try{Change(value,add,a.SendLplkChat,a);}catch(Exception e){Core.Logger.LogError(e,"Whitelist-Eingabe fehlgeschlagen");a.SendLplkChat("[LPLK] Änderung nicht gespeichert.");}}});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,player,()=>module.Active&&serial==generation,a=>{if(module.CanAdmin(a))OpenEntries(a);});
        Core.MenusAPI.OpenMenuForPlayer(player,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(add?"LPLK · Eintrag hinzufügen":"LPLK · Eintrag entfernen")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
}
