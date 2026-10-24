using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private AdminSettingsFile settingsFile=null!;
    private string? settingsError;
    private long nextSettingsCheck;
    private bool settingsStatusFailed;
    private readonly ConditionalWeakTable<IMenuAPI,object> ownedMenus=new();
    private bool CanConfigure(IPlayer p)=>Has(p,"lplk.admin")||Has(p,"admin.root");
    private void LoadSettings(){settingsFile=new(SettingsPath);settings=AdminSettingsFile.Copy(settingsFile.Current);}
    private static bool SettingsFailure(Exception e)=>e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;
    private void OpenOwnedMenu(IPlayer p,IMenuAPI menu){moduleReturns.Remove(p.PlayerID);ownedMenus.Add(menu,new object());MenuPresentation.Apply(menu);Core.MenusAPI.OpenMenuForPlayer(p,menu);}
    private void InvalidateMenus()
    {
        generation++;
        foreach(var p in Core.PlayerManager.GetAllValidPlayers())
        {
            if(!p.IsValid||p.IsFakeClient)continue;
            try{var menu=Core.MenusAPI.GetCurrentMenu(p);if(menu is not null&&ownedMenus.TryGetValue(menu,out _))Core.MenusAPI.CloseMenuForPlayer(p,menu);}
            catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Admin: Altes Menü konnte nicht geschlossen werden; Aktionen sind ungültig.");}
        }
    }
    private void SyncSettings()
    {
        var next=settingsFile.Current;bool changed=!AdminSettingsFile.Same(settings,next);
        settings=AdminSettingsFile.Copy(next);settingsError=null;ReconcileAdminCommands();if(changed)InvalidateMenus();
    }
    private void ReloadSettings()
    {
        if(settingsFile is null)return;
        try{settingsFile.Reload();SyncSettings();}
        catch(Exception e)when(SettingsFailure(e)){if(settingsError is null)Core.Logger.LogWarning(e,"LPLK_Admin: Letzte gültige Gründe/Dauern bleiben aktiv.");settingsError="Ungültige Admin-Konfiguration; letzter gültiger Stand bleibt aktiv.";}
    }
    // World updates continue while an empty server hibernates; game ticks do not.
    // Settings and command registration still run on the game thread, at most every two seconds.
    [EventListener<EventDelegates.OnWorldUpdate>]
    public void SettingsTick()
    {
        long now=Stopwatch.GetTimestamp();if(!active||now<nextSettingsCheck)return;nextSettingsCheck=now+Stopwatch.Frequency*2;
        ReloadSettings();WriteSettingsStatus();
    }
    private void WriteSettingsStatus()
    {
        try{AtomicJson.Write(Path.Combine(Core.PluginDataDirectory,"status.json"),new{updatedAt=DateTimeOffset.UtcNow,version="0.10.5",reasons=settings.Reasons.Length,durations=settings.Durations.Length,reloadError=settingsError,commands=aliasRegistrations.Count,commandError});settingsStatusFailed=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){if(!settingsStatusFailed)Core.Logger.LogWarning(e,"LPLK_Admin: Status nicht schreibbar.");settingsStatusFailed=true;}
    }
    private void SettingsMenu(IPlayer p)=>Menu(p,"Admin · Einstellungen",_=>new (string,Action<IPlayer>)[]{
        ("Gründe",a=>ReasonsSettings(a,0)),("Dauern",a=>DurationsSettings(a,0)),("Befehle",a=>CommandGroupsMenu(a,0)),("Status",a=>a.SendChat("[LPLK] Konfiguration: "+(settingsError??"gültig")+"; Befehle: "+(commandError??"bereit")))},ServerManagement,CanConfigure);
    private void ChangeSettings(IPlayer p,Func<AdminSettings,AdminSettings> edit,Action<IPlayer> back)
    {
        if(!CanConfigure(p))return;
        try{settingsFile.Change(edit);SyncSettings();p.SendChat("[LPLK] Einstellung gespeichert.");}
        catch(Exception e)when(SettingsFailure(e)){Core.Logger.LogWarning(e,"LPLK_Admin: Einstellung nicht gespeichert.");p.SendChat("[LPLK] Nicht gespeichert. Konfiguration und Auswahl prüfen.");ReloadSettings();}
        WriteSettingsStatus();back(p);
    }
    private void SettingInput(IPlayer p,string title,int length,Func<string,bool> validate,Action<IPlayer,string> change,Action<IPlayer> back,string initial="")
    {
        if(!CanConfigure(p))return;int serial=generation;
        // The framework truncates at maxLength before validation; one extra character
        // lets our validator reject overlong input instead of silently changing it.
        var input=new InputMenuOption(new MenuNumbers().Next(title),maxLength:length+1,defaultValue:initial,validator:validate,hintMessage:"Wert in den Chat eingeben.");
        GameThreadMenu.BindInput(Core,input,p,()=>active&&serial==generation,(a,value)=>{ReloadSettings();if(serial!=generation||!CanConfigure(a))return;if(validate(value))change(a,value);});
        var cancel=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,cancel,p,()=>active&&serial==generation,a=>{if(CanConfigure(a))back(a);});
        OpenOwnedMenu(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(cancel).Build());
    }
    private static bool ReasonInput(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=150&&!value.Any(char.IsControl);
    private static bool DurationInput(string value)=>int.TryParse(value,out int n)&&n is >=0 and <=525600;
    private static string DurationLabel(int n)=>n==0?"Dauerhaft":n+" Minuten";
    private void ReasonsSettings(IPlayer p,int page)=>Menu(p,"Admin · Gründe",_=>
    {
        var rows=settings.Reasons.Select((reason,index)=>(reason,(Action<IPlayer>)(a=>ReasonSettings(a,index,page)))).ToList();
        if(settings.Reasons.Length<30)rows.Add(("Hinzufügen",a=>SettingInput(a,"Grund",150,ReasonInput,(b,value)=>ChangeSettings(b,s=>s with{Reasons=[..s.Reasons,value.Trim()]},c=>ReasonsSettings(c,page)),b=>ReasonsSettings(b,page))));
        return rows;
    },SettingsMenu,CanConfigure,page,ReasonsSettings);
    private void ReasonSettings(IPlayer p,int index,int page)
    {
        string[] snapshot=[..settings.Reasons];if(index<0||index>=snapshot.Length)return;
        AdminSettings Edit(AdminSettings s,string? value)
        {
            if(!s.Reasons.SequenceEqual(snapshot))throw new InvalidOperationException("Gründeliste geändert.");
            return s with{Reasons=value is null?s.Reasons.Where((_,i)=>i!=index).ToArray():s.Reasons.Select((r,i)=>i==index?value.Trim():r).ToArray()};
        }
        Menu(p,"Grund · "+snapshot[index],_=>new (string,Action<IPlayer>)[]{
            ("Ändern",a=>SettingInput(a,"Grund",150,ReasonInput,(b,value)=>ChangeSettings(b,s=>Edit(s,value),c=>ReasonsSettings(c,page)),b=>ReasonsSettings(b,page),snapshot[index])),
            ("Entfernen",a=>SettingsConfirm(a,b=>ChangeSettings(b,s=>Edit(s,null),c=>ReasonsSettings(c,page)),b=>ReasonsSettings(b,page)))},a=>ReasonsSettings(a,page),CanConfigure);
    }
    private void DurationsSettings(IPlayer p,int page)=>Menu(p,"Admin · Dauern",_=>
    {
        var rows=settings.Durations.Select((duration,index)=>(DurationLabel(duration),(Action<IPlayer>)(a=>DurationSettings(a,index,page)))).ToList();
        if(settings.Durations.Length<20)rows.Add(("Hinzufügen",a=>SettingInput(a,"Minuten",6,DurationInput,(b,value)=>ChangeSettings(b,s=>s with{Durations=[..s.Durations,int.Parse(value)]},c=>DurationsSettings(c,page)),b=>DurationsSettings(b,page))));
        return rows;
    },SettingsMenu,CanConfigure,page,DurationsSettings);
    private void DurationSettings(IPlayer p,int index,int page)
    {
        int[] snapshot=[..settings.Durations];if(index<0||index>=snapshot.Length)return;
        AdminSettings Edit(AdminSettings s,int? value)
        {
            if(!s.Durations.SequenceEqual(snapshot))throw new InvalidOperationException("Dauernliste geändert.");
            return s with{Durations=value is null?s.Durations.Where((_,i)=>i!=index).ToArray():s.Durations.Select((d,i)=>i==index?value.Value:d).ToArray()};
        }
        Menu(p,"Dauer · "+DurationLabel(snapshot[index]),_=>new (string,Action<IPlayer>)[]{
            ("Ändern",a=>SettingInput(a,"Minuten",6,DurationInput,(b,value)=>ChangeSettings(b,s=>Edit(s,int.Parse(value)),c=>DurationsSettings(c,page)),b=>DurationsSettings(b,page),snapshot[index].ToString())),
            ("Entfernen",a=>SettingsConfirm(a,b=>ChangeSettings(b,s=>Edit(s,null),c=>DurationsSettings(c,page)),b=>DurationsSettings(b,page)))},a=>DurationsSettings(a,page),CanConfigure);
    }
    private void SettingsConfirm(IPlayer p,Action<IPlayer> action,Action<IPlayer> back)=>Menu(p,"Eintrag entfernen",_=>new (string,Action<IPlayer>)[]{("Bestätigen",action)},back,CanConfigure);
}
