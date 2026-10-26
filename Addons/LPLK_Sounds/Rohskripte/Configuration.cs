using System.Globalization;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;

namespace LPLK_Sounds;
public sealed partial class Plugin
{
    private SoundConfigFile configFile=null!;
    private AssetCatalog localCatalog=null!;
    private long nextConfig;
    private string assetKey="";
    private bool assetReloadPending,statusWriteFailed;
    private string? reloadError;
    private void ActivateCatalogSettings()
    {
        // Called only at initial load and the real precache hook, never by a menu/tick.
        var next=config.AutoCatalogFromAddon?new WorkshopCatalogSync(config.WorkshopIds,Path.Combine(LPLK.Common.RoundFileStore.DataPath(Core),"workshop-catalog-cache.json"),localCatalog):null;
        workshopCatalog=next;catalog=next?.Current??localCatalog;
        assetKey=SoundConfigFile.AssetKey(config);assetReloadPending=false;
    }
    private void SyncConfiguration()
    {
        var next=configFile.Current;bool changed=!SoundConfigFile.Same(config,next);
        config=SoundConfigFile.Clone(next);assetReloadPending=SoundConfigFile.AssetKey(config)!=assetKey;
        if(changed){StopBroadcast();CloseAllOwnedMenus();}
    }
    private void ReloadConfiguration()
    {
        try{configFile.Reload();SyncConfiguration();reloadError=null;}
        catch(Exception e)when(ConfigFailure(e))
        {if(reloadError is null)Logger.LogWarning(e,"LPLK_Sounds: Konfiguration nicht übernommen; letzte gültige Regeln bleiben aktiv.");reloadError="Ungültige Soundkonfiguration; letzter gültiger Stand bleibt aktiv.";}
    }
    private static bool ConfigFailure(Exception e)=>e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;
    private void ChangeConfiguration(Action<SoundsConfig> change)
    {
        configFile.Change(change);SyncConfiguration();reloadError=null;WriteStatus();
    }
    private void Apply(IPlayer p,Action<SoundsConfig> change)
    {
        if(!module.CanAdmin(p))return;
        try{ChangeConfiguration(change);}
        catch(Exception e)when(ConfigFailure(e)){Logger.LogWarning(e,"LPLK_Sounds: Änderung nicht gespeichert.");p.SendLplkChat("[LPLK Sounds] Nicht gespeichert. Aktuelle Konfiguration prüfen.");}
        SoundAdminSettings(p);
    }
    private void SoundPauseInput(IPlayer player)
    {
        if(!module.CanAdmin(player))return;
        static bool Parse(string value,out double number)=>double.TryParse(value.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&double.IsFinite(number)&&number is >=1 and <=60;
        var input=new InputMenuOption(new MenuNumbers().Next("Sekunden 1–60"),maxLength:24,validator:s=>Parse(s,out _),hintMessage:"Soundpause in Sekunden, z.B. 2,5.");
        GameThreadMenu.BindInput(Core,input,player,()=>module.Active&&menusEnabled,(p,value)=>{if(module.CanAdmin(p)&&Parse(value,out var seconds))Apply(p,c=>c.BroadcastCooldownSeconds=seconds);});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};GameThreadMenu.Bind(Core,back,player,()=>module.Active&&menusEnabled,p=>{if(module.CanAdmin(p))SoundAdminSettings(p);});
        Core.MenusAPI.OpenMenuForPlayer(player,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Soundpause")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
}
