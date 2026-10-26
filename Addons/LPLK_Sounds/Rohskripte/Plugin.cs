using File = LPLK.Common.RamFile;
using Directory = LPLK.Common.RamDirectory;
using FileInfo = LPLK.Common.RamFileInfo;
using DirectoryInfo = LPLK.Common.RamDirectoryInfo;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Core.Menus.OptionsBase;

namespace LPLK_Sounds;

[PluginMetadata(Id="LPLK_Sounds",Name="LPLK_Sounds",Version="0.1.16",Author="LPLK",Description="Sounds mit Empfängerwahl, Hörfiltern und !admin",MinimumAPIVersion="1.4.13")]
public sealed partial class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    private const string PluginVersion="0.1.16";
    private ILogger Logger=>Core.Logger;
    private ModuleSupport module=null!;
    private SoundsConfig config=new();
    private AssetCatalog catalog=new();
    private WorkshopCatalogSync? workshopCatalog;
    private readonly HashSet<string> precachedSounds=new(StringComparer.Ordinal);
    private string ConfigPath=>Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"addons","swiftlys2","configs","plugins","LPLK_Sounds","config.json");
    private string CatalogSource=>workshopCatalog?.Source??"Lokaler Soundkatalog";
    private string CatalogStatus=>workshopCatalog?.Status??"Workshop-Abgleich ausgeschaltet";
    private long nextStatus;

    public override void Load(bool hotReload)
    {
        LPLK.Common.RoundFileStore.BeginLoad(Core,this);
        try { LoadCore(hotReload); LPLK.Common.RoundFileStore.EndLoad(); }
        catch { LPLK.Common.RoundFileStore.AbortLoad(); throw; }
    }
    private void LoadCore(bool hotReload)
    { ChatTheme.Initialize("LPLK_Sounds",()=>Core.PlayerManager.GetAllPlayers().Where(p=>p.IsValid&&!p.IsFakeClient).Select(p=>p.Name));
        module=new(Core,"LPLK_Sounds","Sounds","lplk.sounds.admin",OpenAdmin,CanOverrideSound);
        if(!File.Exists(ConfigPath))DataFiles.WriteAtomic(ConfigPath,JsonSerializer.Serialize(config,SoundsConfig.Json));
        configFile=new(ConfigPath);config=SoundConfigFile.Clone(configFile.Current);
        localCatalog=AssetCatalog.Read(Path.Combine(LPLK.Common.RoundFileStore.PluginPath(Core),"catalog.json"));ActivateCatalogSettings();
        soundPreferences=new(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"sound-listening.json"));RefreshSoundNumbers();
        module.Active=true;menusEnabled=true;
        Core.Event.OnPrecacheResource+=OnPrecache;Core.Event.OnTick+=OnTick;Core.Event.OnClientConnected+=OnConnected;Core.Event.OnClientDisconnected+=OnDisconnected;Core.Event.OnMapUnload+=OnMapUnload;
        Core.GameEvent.HookPre<EventRoundPrestart>(_=>{StopAll();soundRoundLimit.Reset();soundSendLimit.Reset();RefreshSoundLimitTitles();return HookResult.Continue;});
        Core.GameEvent.HookPost<EventPlayerSpawn>(e=>{if(e.UserIdPlayer is {} p){if(broadcast?.DeadOnly==true)RemoveBroadcastListener(p.Slot);if(directedSounds.TryGetValue(p.Slot,out var s)&&s.Sound.DeadOnly)StopDirectedSound(p.Slot);}return HookResult.Continue;});
        WriteStatus();Logger.LogInformation("LPLK_Sounds {Version} geladen: {Count} Sounds, keine Kartenfunktionen. Ressourcenprüfung beim Mapstart.",PluginVersion,catalog.Sounds.Count);
    }
    public override void ConfigureSharedInterface(IInterfaceManager manager)
    {
        module.ConfigureSharedInterface(manager);
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Target.LPLK_Sounds.v1",new(){["actions"]=(Func<IPlayer,IPlayer,IReadOnlyDictionary<string,Action<IPlayer>>>)TargetActions});
        manager.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player.Sounds.v1",new()
        {
            ["allowed"]=(Func<IPlayer,string,bool>)((p,command)=>module.Active&&menusEnabled&&config.Enabled&&RealSoundPlayer(p)&&command is "sound" or "soundsend")
        });
    }
    public override void Unload()
    {
        try { UnloadCore(); }
        finally { LPLK.Common.RoundData.Drain(); LPLK.Common.DeferredJson.Drain(); LPLK.Common.RoundFileStore.Drain(); LPLK.Common.RoundFileStore.EndUnload(); }
    }
    private void UnloadCore()
    {
        menusEnabled=false;if(module is not null)module.Active=false;CloseAllOwnedMenus();
        Core.Event.OnPrecacheResource-=OnPrecache;Core.Event.OnTick-=OnTick;Core.Event.OnClientConnected-=OnConnected;Core.Event.OnClientDisconnected-=OnDisconnected;Core.Event.OnMapUnload-=OnMapUnload;
        StopAll();precachedSounds.Clear();lastMenuOpen.Clear();
     LPLK.Common.DeferredJson.Drain(); }
    private void OnPrecache(IOnPrecacheResourceEvent e)
    {
        ReloadConfiguration();if(assetReloadPending)ActivateCatalogSettings();
        precachedSounds.Clear();
        if(!config.Enabled){WriteStatus();return;}
        if(workshopCatalog is not null)
        {
            bool changed=workshopCatalog.Refresh(path=>!Core.GameFileSystem.FileExists(path,"GAME")?null:Core.GameFileSystem.GetFileSize(path,"GAME")>WorkshopCatalogDocument.MaxBytes?throw new InvalidDataException("Workshop-Katalog zu groß."):Core.GameFileSystem.ReadFile(path,"GAME"),path=>Core.GameFileSystem.FileExists(path,"GAME"));
            if(changed)CloseAllOwnedMenus();catalog=workshopCatalog.Current;
        }
        RefreshSoundNumbers();var added=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (id,sound) in catalog.Sounds)
        {
            // Reject the historical map-colliding event library; only sound resources are precached.
            if(sound.Resources.Contains(SoundResources.LegacyLibrary)||sound.Resources.Any(path=>!Core.GameFileSystem.FileExists(path+"_c","GAME")))continue;
            // Register both the event library and each compiled audio resource once.
            foreach(var path in sound.Resources.Distinct(StringComparer.Ordinal))if(added.Add(path))e.AddItem(path);
            precachedSounds.Add(id);
        }
        WriteStatus();Logger.LogInformation("LPLK_Sounds: {Ready}/{Total} Sounds vorgeladen; {Source}; {Status}",precachedSounds.Count,catalog.Sounds.Count,CatalogSource,CatalogStatus);
    }
    private string? CommonUnavailable()
    {
        if(!config.Enabled)return "Sounds sind deaktiviert.";
        if(assetReloadPending)return "Soundkatalog geändert; Ressourcen werden beim nächsten Mapstart vorgeladen.";
        if(!config.RequireAddonsManager)return null;
        if(config.WorkshopIds.Length==0)return "Sound-Workshop-ID fehlt.";
        if(Core.PluginManager.GetPluginStatus("AddonsManager")!=PluginStatus.Loaded)return "AddonsManager ist nicht geladen.";
        try
        {
            string path=Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"addons","swiftlys2","configs","plugins","AddonsManager","config.jsonc");
            using var doc=JsonDocument.Parse(DataFiles.ReadLimited(path),new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});
            var ids=doc.RootElement.GetProperty("Main").GetProperty("Addons").EnumerateArray().Select(x=>x.GetString()??"");
            return PlaybackPolicy.MissingAddons(config.WorkshopIds,ids).Length>0?"Sound-Workshop-ID fehlt im AddonsManager.":null;
        }
        catch(Exception e)when(e is IOException or JsonException or KeyNotFoundException or InvalidOperationException){return "AddonsManager-Konfiguration nicht lesbar.";}
    }
    private string? Unavailable(string id)=>CommonUnavailable()??(!catalog.Sounds.ContainsKey(id)?"Unbekannter Sound.":!precachedSounds.Contains(id)?"Sound noch nicht vorgeladen; Workshop-Assets und nächsten Mapstart prüfen.":null);
    private static IPlayer? AuthenticatedPlayer(ICommandContext c)
    {
        if(c.Sender is {IsValid:true,IsAuthorized:true,IsFakeClient:false} p&&p.SteamID!=0)return p;
        c.ReplyLplk("[LPLK Sounds] Dieser Befehl benötigt einen angemeldeten Spieler.");return null;
    }
    private static bool ConsoleOnly(ICommandContext c){if(!c.IsSentByPlayer)return true;c.ReplyLplk("[LPLK Sounds] Serverbefehl; Einstellungen unter !admin > Sounds.");return false;}
    private void CommandAction(ICommandContext c,Action action){try{action();}catch(Exception e){Logger.LogWarning(e,"Soundbefehl fehlgeschlagen");c.ReplyLplk("[LPLK Sounds] "+e.Message);}}
    private void OnTick(){if(!menusEnabled)return;if(Environment.TickCount64>=nextConfig){nextConfig=Environment.TickCount64+5000;ReloadConfiguration();}SweepMenus();SweepBroadcast();if(Environment.TickCount64>=nextStatus){nextStatus=Environment.TickCount64+30000;WriteStatus();}}
    private void OnConnected(IOnClientConnectedEvent e)=>Cleanup(e.PlayerId);
    private void OnDisconnected(IOnClientDisconnectedEvent e)=>Cleanup(e.PlayerId);
    private void Cleanup(int slot){CloseOwnedMenu(slot);lastMenuOpen.Remove(slot);RemoveBroadcastListener(slot);StopDirectedSound(slot);}
    private void OnMapUnload(IOnMapUnloadEvent e){CloseAllOwnedMenus();lastMenuOpen.Clear();StopAll();soundRoundLimit.Reset();soundSendLimit.Reset();precachedSounds.Clear();}
    private void StopAll(){StopBroadcast();lastBroadcast=null;}
    private void WriteStatus()
    {
        try{LPLK.Common.DeferredJson.Queue(Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version=PluginVersion,enabled=config.Enabled,sounds=catalog.Sounds.Count,precached=precachedSounds.Count,catalogSource=CatalogSource,catalogStatus=CatalogStatus,workshopIds=config.WorkshopIds,unavailable=CommonUnavailable(),cardsEnabled=false,publicLimit=SoundRoundLimit.PerPlayer,privateLimit=1,cooldownSeconds=config.BroadcastCooldownSeconds,reloadError,assetReloadPending});statusWriteFailed=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!statusWriteFailed)Logger.LogWarning(e,"LPLK_Sounds: Status nicht schreibbar.");statusWriteFailed=true;}
    }
    [Command("lplk_sounds")]
    public void Status(ICommandContext c){if(c.Sender is {} p)OpenAdmin(p);else{WriteStatus();c.ReplyLplk($"LPLK_Sounds {PluginVersion}: {precachedSounds.Count}/{catalog.Sounds.Count} vorgeladen; {CatalogSource}; {CommonUnavailable()??"bereit"}; Karten=aus");}}
    private void OpenAdmin(IPlayer p) => module.Menu(p,"Sounds",((Func<IPlayer,(string,Action<IPlayer>)[]>)(actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Sound",SoundAdminSettingsRows(actor)))))(p));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{("Abspielen",a=>ShowSoundMenu(a)),("Ansagen",a=>{if(RequireSoundOverride(a,a.SendLplkChat))ShowSoundMenu(a,adminOnly:true);}),("Stoppen",a=>{StopBroadcast();a.SendLplkChat("[LPLK Sounds] Gestoppt; Cooldown und Rundenlimit bleiben erhalten.");})}))(p);
    private void SoundAdminSettings(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SoundAdminSettingsRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_ => new (string,Action<IPlayer>)[]{($"Aktiv: {(config.Enabled?"Ja":"Nein")}",a=>Apply(a,c=>c.Enabled=!c.Enabled)),($"Totenregel: {(config.DeadPlayersOnlyToDead?"Ja":"Nein")}",a=>Apply(a,c=>c.DeadPlayersOnlyToDead=!c.DeadPlayersOnlyToDead)),($"Soundpause: {config.BroadcastCooldownSeconds:0.###}s",SoundPauseInput),("Katalog",a=>ShowCatalogMenu(a,SoundAdminSettings))}))(p);
    private void ShowCatalogMenu(IPlayer p,Action<IPlayer> back)
    {
        var owner=new OwnedMenu(p.Slot,p.SteamID);var menu=MenuBuilder("LPLK · Soundkatalog");
        menu.AddOption(new TextMenuOption($"{precachedSounds.Count}/{catalog.Sounds.Count} Sounds vorgeladen"));
        Button(menu,owner,"Details",a=>{a.SendLplkChat("[LPLK Sounds] "+CatalogSource+"; "+CatalogStatus);a.SendLplkChat("[LPLK Sounds] "+(CommonUnavailable()??"Asset-Manager bereit. Client-Hörtest erforderlich."));});
        BackButton(menu,owner,"Zurück",back);OpenOwnedMenu(p,owner,menu);
    }
}

