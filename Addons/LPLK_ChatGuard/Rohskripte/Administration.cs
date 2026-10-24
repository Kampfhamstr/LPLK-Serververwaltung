using System.Globalization;
using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_ChatGuard;
public sealed partial class Plugin
{
    [Command("lplk_chatguard",permission:"lplk.chatguard.admin")]
    public void Command(ICommandContext context)
    {
        if(context.Sender is {} player){OpenAdmin(player);return;}
        WriteStatus();context.Reply($"LPLK_ChatGuard 0.1.4: enabled={_config.Enabled}; bridge={BridgePresent}; filterContract={_active}; bridgeFilterReady={BridgeFilterReady()}; localData=true; exportedIdentities=0; configError={_configError??"none"}");
    }
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>_active,_module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p) => Page(p,"LPLK · Chat / Werbeschutz",actor => MenuRows.Join(("Allgemein",OpenAdminRows(actor)),("Allgemein",SettingsMenuRows(actor)),("Schutz",ProtectionMenuRows(actor)),("Sanktionen",SanctionsMenuRows(actor)),("Meldung",MessagesMenuRows(actor)),("Regeln",RulesMenuRows(actor))));
    private IReadOnlyList<(string,Action<IPlayer>)> OpenAdminRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
("Status",a=>a.SendChat($"[LPLK] Werbeschutz: {(_config.Enabled?"AN":"AUS")}; Sperrdomains={_config.BlockedDomains.Count}; Freigaben={_config.AllowedDomains.Count}; aktive Gags={_gagUntil.Count}; Konfiguration={_configError??"gültig"}."))}))(p);
    private void SettingsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
}))(p);
    private void ProtectionMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> ProtectionMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Aktiv: {_config.Enabled}",a=>Change(a,c=>c.Enabled=!c.Enabled,ProtectionMenu)),        ($"Namen prüfen: {_config.CheckPlayerNames}",a=>Change(a,c=>c.CheckPlayerNames=!c.CheckPlayerNames,ProtectionMenu)),        ($"Rollenschutz: {_config.HardActionsOnlyForPlayerRole}",a=>Change(a,c=>c.HardActionsOnlyForPlayerRole=!c.HardActionsOnlyForPlayerRole,ProtectionMenu))}))(p);
    private void Numeric(IPlayer p,string title,int initial,int min,int max,Action<AntiAdConfig,int> edit)=>Input(p,title,initial.ToString(CultureInfo.InvariantCulture),v=>Change(p,c=>edit(c,int.Parse(v,CultureInfo.InvariantCulture)),SanctionsMenu),SanctionsMenu,4,v=>int.TryParse(v,out int n)&&n>=min&&n<=max,$"{min} bis {max} eingeben.");
    private void SanctionsMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SanctionsMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Gagschwelle: {_config.GagAtViolation}",a=>Numeric(a,"Gagschwelle",_config.GagAtViolation,2,100,(c,n)=>c.GagAtViolation=n)),        ($"Kickschwelle: {_config.KickAtViolation}",a=>Numeric(a,"Kickschwelle",_config.KickAtViolation,3,100,(c,n)=>c.KickAtViolation=n)),        ($"Zeitfenster: {_config.RepeatWindowMinutes} min",a=>Numeric(a,"Zeitfenster (Minuten)",_config.RepeatWindowMinutes,1,1440,(c,n)=>c.RepeatWindowMinutes=n)),        ($"Gagdauer: {_config.GagMinutes} min",a=>Numeric(a,"Gagdauer (Minuten)",_config.GagMinutes,1,1440,(c,n)=>c.GagMinutes=n)),        ($"Warnabstand: {_config.ElevatedRoleWarningCooldownSeconds}s",a=>Numeric(a,"Warnabstand (Sekunden)",_config.ElevatedRoleWarningCooldownSeconds,1,300,(c,n)=>c.ElevatedRoleWarningCooldownSeconds=n)),        ("Kicktext",a=>Input(a,"Kicknachricht",_config.KickReason,v=>Change(a,c=>c.KickReason=v,SanctionsMenu),SanctionsMenu))}))(p);
    private void MessagesMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> MessagesMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Namen maskieren: {_config.MaskAdvertisingPlayerNames}",a=>Change(a,c=>c.MaskAdvertisingPlayerNames=!c.MaskAdvertisingPlayerNames,MessagesMenu)),        ($"Joinfilter: {_config.CensorAdvertisingSystemMessages}",a=>Change(a,c=>c.CensorAdvertisingSystemMessages=!c.CensorAdvertisingSystemMessages,MessagesMenu)),        ($"Leavefilter: {_config.SuppressAdvertisingDisconnectMessages}",a=>Change(a,c=>c.SuppressAdvertisingDisconnectMessages=!c.SuppressAdvertisingDisconnectMessages,MessagesMenu)),        ($"Serverfilter: {_config.SuppressAdvertisingServerMessages}",a=>Change(a,c=>c.SuppressAdvertisingServerMessages=!c.SuppressAdvertisingServerMessages,MessagesMenu)),        ($"Erfolgsmeldung: {_config.BroadcastSuccess}",a=>Change(a,c=>c.BroadcastSuccess=!c.BroadcastSuccess,MessagesMenu)),        ($"Namensanzeige: {_config.BroadcastOffenderName}",a=>Change(a,c=>c.BroadcastOffenderName=!c.BroadcastOffenderName,MessagesMenu)),        ($"Textprotokoll: {_config.LogBlockedText}",a=>Change(a,c=>c.LogBlockedText=!c.LogBlockedText,MessagesMenu)),        ("Ersatzname",a=>Input(a,"Ersatzname",_config.CensoredPlayerName,v=>Change(a,c=>c.CensoredPlayerName=v,MessagesMenu),MessagesMenu,24))}))(p);
    private void RulesMenu(IPlayer p) => OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> RulesMenuRows(IPlayer p) => ((Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>>)(_=>new (string,Action<IPlayer>)[]{
        ($"Sperrdomains ({_config.BlockedDomains.Count})",a=>ListMenu(a,0)),        ($"Freigaben ({_config.AllowedDomains.Count})",a=>ListMenu(a,1)),        ($"Marketing ({_config.MarketingKeywords.Count})",a=>ListMenu(a,2)),        ("Zentralregeln",a=>a.SendChat($"[LPLK] Testkopie: {_globalBlockedDomains.Count} Sperrdomains, {_globalAllowedDomains.Count} erlaubte Domains, {_globalBlockedPhrases.Count} Sperrphrasen. Keine produktiven Werbeidentitäten übernommen."))}))(p);
    private static List<string> List(AntiAdConfig c,int kind)=>kind==0?c.BlockedDomains:kind==1?c.AllowedDomains:c.MarketingKeywords;
    private void ListMenu(IPlayer p,int kind)=>Page(p,kind==0?"Sperrdomains":kind==1?"Erlaubte Domains":"Marketingbegriffe",_=>
    {
        var actions=new List<(string,Action<IPlayer>)>{("Hinzufügen",a=>Input(a,kind==2?"Marketingbegriff":"Domain","",v=>Change(a,c=>{
            string entry=kind==2?ChatGuardPolicy.Normalize(v):ChatGuardPolicy.NormalizeDomain(v);
            if(entry.Length<2||entry.Any(char.IsControl)||kind!=2&&(entry.Contains(' ')||!entry.Contains('.')))throw new InvalidDataException("Ungültiger Eintrag.");
            var list=List(c,kind);if(!list.Contains(entry,StringComparer.OrdinalIgnoreCase))list.Add(entry);
        },b=>ListMenu(b,kind)),b=>ListMenu(b,kind)))};
        // Dot separation prevents our own HUD from becoming an advertisement.
        foreach(string entry in List(_config,kind).ToArray())actions.Add(("Entfernen: "+System.Net.WebUtility.HtmlEncode(entry.Replace("."," · ")),a=>Page(a,"Eintrag entfernen?",_=>new (string,Action<IPlayer>)[]{("Entfernen bestätigen",b=>Change(b,c=>List(c,kind).Remove(entry),d=>ListMenu(d,kind)))},b=>ListMenu(b,kind))));
        return actions;
    },RulesMenu);
    private void Input(IPlayer p,string title,string initial,Action<string> save,Action<IPlayer> back,int maxLength=200,Func<string,bool>? validator=null,string hint="Wert eingeben.")
    {
        if(!_module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Wert eingeben"),maxLength:maxLength,validator:validator??(v=>!string.IsNullOrWhiteSpace(v)&&!v.Any(char.IsControl)),defaultValue:initial,hintMessage:hint){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>_active,(a,value)=>{if(_module.CanAdmin(a))save(value);});
        var returnButton=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,returnButton,p,()=>_active,a=>{if(_module.CanAdmin(a))back(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(returnButton).Build());
    }
    private void Change(IPlayer actor,Action<AntiAdConfig> edit,Action<IPlayer> back)
    {
        if(!_module.CanAdmin(actor))return;
        try{_settingsStore.Change(edit);_configError=null;Core.Logger.LogInformation("LPLK_ChatGuard: Einstellungen durch berechtigten Admin gespeichert; sofort wirksam.");}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {_configError=_settingsStore.Error??e.GetType().Name;actor.SendChat("[LPLK] Nicht gespeichert. Werte und externe Konfiguration prüfen.");}
        // A valid external update remains authoritative even when this menu edit fails.
        if(!ReferenceEquals(_config,_settingsStore.Current)){_config=_settingsStore.Current;_nameChecked.Clear();}
        WriteStatus();back(actor);
    }
}
