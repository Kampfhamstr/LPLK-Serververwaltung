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
        if(context.Sender is {} player) { OpenAdmin(player);return; }
        WriteStatus();
        context.Reply($"LPLK_ChatGuard 0.1.0: enabled={_config.Enabled}; bridge={BridgePresent}; filterContract={_active}; bridgeFilterReady={BridgeFilterReady()}; localData=true; exportedIdentities=0; configError={_configError??"none"}");
    }
    private void OpenAdmin(IPlayer p)=>_module.Menu(p,"LPLK · Chat / Werbeschutz",
        ($"Aktiv: {_config.Enabled}",a=>Change(a,c=>c.Enabled=!c.Enabled)),
        ($"Spielernamen prüfen: {_config.CheckPlayerNames}",a=>Change(a,c=>c.CheckPlayerNames=!c.CheckPlayerNames)),
        ($"Höhere Rollen nur warnen: {_config.HardActionsOnlyForPlayerRole}",a=>Change(a,c=>c.HardActionsOnlyForPlayerRole=!c.HardActionsOnlyForPlayerRole)),
        ("Warnungen / Sanktionen …",SanctionsMenu),("Meldungen / Datenschutz …",MessagesMenu),("Domains / Filterregeln …",RulesMenu));
    private void SanctionsMenu(IPlayer p)=>_module.Menu(p,"ChatGuard · Warnungen / Sanktionen",1,
        ($"Gag ab Treffer: {_config.GagAtViolation} (+1 / nach 10: 2)",a=>Change(a,c=>c.GagAtViolation=c.GagAtViolation>=10?2:c.GagAtViolation+1)),
        ($"Kick ab Treffer: {_config.KickAtViolation} (+1 / nach 20: 3)",a=>Change(a,c=>c.KickAtViolation=c.KickAtViolation>=20?3:c.KickAtViolation+1)),
        ($"Zeitfenster: {_config.RepeatWindowMinutes} min (+5 / nach 60: 5)",a=>Change(a,c=>c.RepeatWindowMinutes=c.RepeatWindowMinutes>=60?5:c.RepeatWindowMinutes+5)),
        ($"Gagdauer: {_config.GagMinutes} min (+5 / nach 120: 5)",a=>Change(a,c=>c.GagMinutes=c.GagMinutes>=120?5:c.GagMinutes+5)),
        ($"Warnabstand höhere Rollen: {_config.ElevatedRoleWarningCooldownSeconds}s (+5 / nach 60: 5)",a=>Change(a,c=>c.ElevatedRoleWarningCooldownSeconds=c.ElevatedRoleWarningCooldownSeconds>=60?5:c.ElevatedRoleWarningCooldownSeconds+5)),
        ("Kicknachricht ändern …",a=>Input(a,"Kicknachricht",_config.KickReason,v=>Change(a,c=>c.KickReason=v))),
        ("Zurück",OpenAdmin));
    private void MessagesMenu(IPlayer p)=>_module.Menu(p,"ChatGuard · Meldungen",1,
        ($"Werbenamen maskieren: {_config.MaskAdvertisingPlayerNames}",a=>Change(a,c=>c.MaskAdvertisingPlayerNames=!c.MaskAdvertisingPlayerNames)),
        ($"Joinwerbung verbergen: {_config.CensorAdvertisingSystemMessages}",a=>Change(a,c=>c.CensorAdvertisingSystemMessages=!c.CensorAdvertisingSystemMessages)),
        ($"Leave nach Werbetreffer verbergen: {_config.SuppressAdvertisingDisconnectMessages}",a=>Change(a,c=>c.SuppressAdvertisingDisconnectMessages=!c.SuppressAdvertisingDisconnectMessages)),
        ($"Map-/Serverwerbung filtern: {_config.SuppressAdvertisingServerMessages}",a=>Change(a,c=>c.SuppressAdvertisingServerMessages=!c.SuppressAdvertisingServerMessages)),
        ($"Erfolg im Chat melden: {_config.BroadcastSuccess}",a=>Change(a,c=>c.BroadcastSuccess=!c.BroadcastSuccess)),
        ($"Spielername in Erfolgsmeldung: {_config.BroadcastOffenderName}",a=>Change(a,c=>c.BroadcastOffenderName=!c.BroadcastOffenderName)),
        ($"Blockierten Text protokollieren: {_config.LogBlockedText}",a=>Change(a,c=>c.LogBlockedText=!c.LogBlockedText)),
        ("Ersatzname ändern …",a=>Input(a,"Ersatzname",_config.CensoredPlayerName,v=>Change(a,c=>c.CensoredPlayerName=v))),
        ("Zurück",OpenAdmin));
    private void RulesMenu(IPlayer p)=>_module.Menu(p,"ChatGuard · Regeln",1,
        ($"Lokale Sperrdomains ({_config.BlockedDomains.Count}) …",a=>ListMenu(a,0)),
        ($"Erlaubte Domains ({_config.AllowedDomains.Count}) …",a=>ListMenu(a,1)),
        ($"Marketingbegriffe ({_config.MarketingKeywords.Count}) …",a=>ListMenu(a,2)),
        ("Übernommene zentrale Regeln anzeigen",a=>{a.SendChat($"[LPLK] Testkopie: {_globalBlockedDomains.Count} Sperrdomains, {_globalAllowedDomains.Count} erlaubte Domains, {_globalBlockedPhrases.Count} Sperrphrasen. Keine produktiven Werbeidentitäten übernommen.");RulesMenu(a);}),
        ("Zurück",OpenAdmin));
    private static List<string> List(AntiAdConfig c,int kind)=>kind==0?c.BlockedDomains:kind==1?c.AllowedDomains:c.MarketingKeywords;
    private void ListMenu(IPlayer p,int kind)
    {
        var actions=new List<(string,Action<IPlayer>)>{("Eintrag hinzufügen …",a=>Input(a,kind==2?"Marketingbegriff":"Domain","",v=>Change(a,c=>{
            string entry=kind==2?ChatGuardPolicy.Normalize(v):ChatGuardPolicy.NormalizeDomain(v);
            if(entry.Length<2||entry.Any(char.IsControl)||kind!=2&&(entry.Contains(' ')||!entry.Contains('.')))throw new InvalidDataException("Ungültiger Eintrag.");
            var list=List(c,kind);if(!list.Contains(entry,StringComparer.OrdinalIgnoreCase))list.Add(entry);
        })))};
        // Display dots as separators so blocked domain strings are not emitted as
        // fresh advertisements by our own protected HUD/text channels.
        foreach(string entry in List(_config,kind).ToArray())actions.Add(("Entfernen: "+entry.Replace("."," · "),a=>_module.Menu(a,"Eintrag entfernen?",1,("Entfernen bestätigen",b=>Change(b,c=>List(c,kind).Remove(entry))),("Zurück",b=>ListMenu(b,kind)))));
        actions.Add(("Zurück",RulesMenu));_module.Menu(p,kind==0?"Sperrdomains":kind==1?"Erlaubte Domains":"Marketingbegriffe",1,actions.ToArray());
    }
    private void Input(IPlayer p,string title,string initial,Action<string> save)
    {
        if(!_module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next("Neuen Wert im Chat eingeben"),maxLength:200,validator:v=>!string.IsNullOrWhiteSpace(v)&&!v.Any(char.IsControl),defaultValue:initial,hintMessage:"Neuen Wert im Chat eingeben."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>_active,(a,value)=>{if(_module.CanAdmin(a))save(value);});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>_active,a=>{if(_module.CanAdmin(a))OpenAdmin(a);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading(title)).Design.SetMaxVisibleItems(5).AddOption(input).AddOption(back).Build());
    }
    private void Change(IPlayer actor,Action<AntiAdConfig> edit)
    {
        if(!_module.CanAdmin(actor))return;
        var next=JsonSerializer.Deserialize<AntiAdConfig>(JsonSerializer.Serialize(_config,_json),_json)!;
        edit(next);ChatGuardPolicy.Validate(next);SaveConfig(next);_config=next;_configError=null;_nameChecked.Clear();
        Core.Logger.LogInformation("LPLK_ChatGuard: Einstellungen durch berechtigten Admin gespeichert; sofort wirksam.");WriteStatus();OpenAdmin(actor);
    }
}
