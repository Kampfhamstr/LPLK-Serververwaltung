using System.Globalization;
using System.Text.Json;
using System.Net;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK_Teams;

public sealed partial class Plugin
{
    [Command("lplk_teams")]
    public void AdminCommand(ICommandContext c)
    {
        if(c.Sender is {} p){OpenAdmin(p);return;}
        Refresh();WriteStatus();
        c.Reply($"LPLK_Teams 0.2.2: enabled={settings.Enabled}; ready={ready}; bridgeReady={BridgeReady()}; CT={score?.Ct}; T={score?.T}; source={tracker.Source}; rounds={rounds}; threshold={TeamPolicy.Threshold(settings,rounds)}; selected={SelectionIds.Count()}; errors={errors}; reloadError={reloadError??"none"}");
    }
    [Command("tsbscore")]
    public void ScoreCommand(ICommandContext c)
    {
        if(c.Sender is not null&&!settings.AllowChatStatusCommand)return;
        Refresh();
        c.Reply($"[LPLK] Teams {(settings.Enabled?"AN":"AUS")}: CT {score?.Ct.ToString()??"?"} : {score?.T.ToString()??"?"} T; geschützt: {ProtectedText()}; Grenze {TeamPolicy.Threshold(settings,rounds)} / {rounds}; Halbzeit {Halftime}; Quelle {tracker.Source}.");
    }
    private void Page(IPlayer p,string title,Func<IPlayer,IReadOnlyList<(string,Action<IPlayer>)>> rows,Action<IPlayer>? back=null)=>CompactMenu.Open(Core,p,title,()=>loaded,module.CanAdmin,rows,back);
    private void OpenAdmin(IPlayer p)=>Page(p,"LPLK · Teamwechsel",_=>new (string,Action<IPlayer>)[]{
        ("Spielstand",a=>{Refresh();a.SendChat($"[LPLK] CT {score?.Ct} : {score?.T} T | {ProtectedText()} | Grenze {TeamPolicy.Threshold(settings,rounds)} / {rounds} | {roundsSource} | {tracker.Source} | Konfiguration: {reloadError??"gültig"}");}),
        ($"Verliererteam ({SelectionIds.Count()})",PlayersRoot),
        ($"Aktiv: {settings.Enabled}",a=>Set(a,nameof(TeamSettings.Enabled),false,toggle:true))}.Concat(Groups.SelectMany((g,index)=>SettingRows(index))).Concat(TextSettingRows()).ToArray());
    private void SettingsRoot(IPlayer p)=>OpenAdmin(p);
    private static readonly (string Name,string Label)[][] Groups=
    [
        [(nameof(TeamSettings.AutoDetectMaxRounds),"Autorunden"),(nameof(TeamSettings.FallbackMaxRounds),"Ersatz-Rundenzahl"),(nameof(TeamSettings.BlockAtMaxRoundsPercent),"Sperranteil"),
         (nameof(TeamSettings.BlockOnlyIfTeamIsLeading),"Führungsschutz"),(nameof(TeamSettings.BlockBothTeamsIfBothReachedLimit),"Doppelsperre"),(nameof(TeamSettings.AllowSpectator),"Zuschauer erlauben"),(nameof(TeamSettings.ForceBackIfSwitchStillHappened),"Rückversetzen")],
        [(nameof(TeamSettings.AllowHalftimeTeamSwitchException),"Halbzeitausnahme"),(nameof(TeamSettings.HalftimeSwitchGraceSeconds),"Halbzeitdauer"),(nameof(TeamSettings.TreatWinScoreAsMatchEnd),"Siegpunkt"),
         (nameof(TeamSettings.BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd),"Verliererzuordnung"),(nameof(TeamSettings.MatchEndGuardWindowSeconds),"Sperrdauer"),(nameof(TeamSettings.ResetScoreOnNewMap),"Map-Rücksetzung"),(nameof(TeamSettings.ResetScoreOnNewMatch),"Match-Rücksetzung")],
        [(nameof(TeamSettings.UseRoundEndFallback),"Rundenersatz"),(nameof(TeamSettings.AnnounceWhenTeamBecomesProtected),"Sperrteam ankündigen"),(nameof(TeamSettings.ShowBlockedMessageInChat),"Chathinweis"),
         (nameof(TeamSettings.ShowBlockedMessageInCenter),"Bildschirmhinweis"),(nameof(TeamSettings.ShowForceToLosingTeamMessage),"Verliererteam-Hinweis"),(nameof(TeamSettings.AllowChatStatusCommand),"Spielstandbefehl"),(nameof(TeamSettings.EnableLosingTeamMenu),"Verlierermenü"),(nameof(TeamSettings.Debug),"Diagnose")]
    ];
    private void SettingsMenu(IPlayer p,int group)=>OpenAdmin(p);
    private IReadOnlyList<(string,Action<IPlayer>)> SettingRows(int group)
    {
        var rows=new List<(string,Action<IPlayer>)>();
        foreach(var (name,label) in Groups[group])
        {
            var property=typeof(TeamSettings).GetProperty(name)!;object value=property.GetValue(settings)!;
            rows.Add(($"{label}: {value}",a=>{if(value is bool)Set(a,name,false,group,toggle:true);else InputNumber(a,name,label,group);}));
        }
        return rows;
    }
    private void Set(IPlayer actor,string name,object value,int? group=null,bool toggle=false)
    {
        if(!module.CanAdmin(actor))return;
        try
        {
            ReloadSettings();
            settings=config.Change(current=>{var next=TeamSettings.Copy(current);var property=typeof(TeamSettings).GetProperty(name)!;property.SetValue(next,toggle?!(bool)property.GetValue(current)!:value);return next;});
            revision++;permits.Clear();intents.Clear();reloadError=null;
            Core.Logger.LogInformation("LPLK_Teams: Admin {Actor}, {Setting} geändert",actor.SteamID,name);
            Refresh();
        }
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {reloadError=config.Error??e.GetType().Name;actor.SendChat("[LPLK] Einstellung nicht gespeichert. Externe Konfiguration prüfen und Menü erneut öffnen.");}
        if(!ReferenceEquals(settings,config.Current)){settings=config.Current;revision++;permits.Clear();intents.Clear();}
        WriteStatus();if(group is {} g)SettingsMenu(actor,g);else SettingsRoot(actor);
    }
    private static bool Number(string name,string text,out object value)
    {
        bool fraction=name==nameof(TeamSettings.BlockAtMaxRoundsPercent);value=0;
        if(fraction){if(!double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double d)||!double.IsFinite(d)||d is <=0 or >1)return false;value=d;return true;}
        if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))return false;
        if(name==nameof(TeamSettings.FallbackMaxRounds)?n is <2 or >1000:n is <1 or >120)return false;value=n;return true;
    }
    private void InputNumber(IPlayer p,string name,string label,int group)
    {
        if(!module.CanAdmin(p))return;
        var input=new InputMenuOption(new MenuNumbers().Next(label),maxLength:12,validator:v=>Number(name,v,out _),
            defaultValue:Convert.ToString(typeof(TeamSettings).GetProperty(name)!.GetValue(settings),CultureInfo.InvariantCulture)??"",
            hintMessage:name==nameof(TeamSettings.BlockAtMaxRoundsPercent)?"Anteil >0 bis 1 eingeben, z. B. 0,5.":name==nameof(TeamSettings.FallbackMaxRounds)?"2 bis 1000 Runden eingeben.":"1 bis 120 Sekunden eingeben."){CloseAfterClick=false};
        GameThreadMenu.BindInput(Core,input,p,()=>loaded,(a,text)=>{if(Number(name,text,out var value))Set(a,name,value,group);});
        var back=new ButtonMenuOption(MenuColors.Back("Zurück")){CloseAfterClick=false};
        GameThreadMenu.Bind(Core,back,p,()=>loaded,a=>{if(module.CanAdmin(a))SettingsMenu(a,group);});
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teams · Wert ändern")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter).AddOption(input).AddOption(back).Build());
    }
    private void PlayersMenu(IPlayer p,int page,SelectionFilter filter=SelectionFilter.All)
    {
        if(!CanUseSelection(p))return;
        var ids=Core.PlayerManager.GetAllValidPlayers().Where(IsHuman).Select(x=>x.SteamID).Concat(SelectionIds).Distinct().Where(id=>SelectionMatches(filter,HasSelection(id))).Order().ToArray();
        page=Math.Clamp(page,0,Math.Max(0,(ids.Length-1)/2));
        var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teams · Verliererteam-Auswahl")).Design.SetMaxVisibleItems(5).Design.SetGlobalScrollStyle(SwiftlyS2.Shared.Menus.MenuOptionScrollStyle.WaitingCenter);
        var numbers=new MenuNumbers();
        foreach(var id in ids.Skip(page*2).Take(2))
        {
            var target=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>x.SteamID==id&&IsHuman(x));
            ConnectionKey? identity=target is null?null:Key(target);
            string label=WebUtility.HtmlEncode(target?.Controller.PlayerName??$"Offline {id}");bool active=HasSelection(id);
            var button=new ButtonMenuOption(numbers.Next($"{label}: {(active?"AN":"AUS")}",enabled:active)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>loaded,a=>
            {
                if(!CanUseSelection(a)||!CanTarget(a.SteamID,id)){a.SendChat("[LPLK] Ziel hat gleiche/höhere Rechte.");return;}
                if(identity is {} expected)
                {
                    var fresh=Core.PlayerManager.GetPlayer(expected.Slot);
                    if(!IsHuman(fresh)||Key(fresh!)!=expected){a.SendChat("[LPLK] Spielerverbindung hat sich geändert.");PlayersMenu(a,page,filter);return;}
                }
                if(HasSelection(id))
                {
                    if(selected.TryGetValue(id,out var owner)&&owner!=a.SteamID&&!CanTarget(a.SteamID,owner)){a.SendChat("[LPLK] Auswahl eines gleich/höher berechtigten Admins.");return;}
                    SaveSelection(id,null);
                }
                else
                {
                    if(identity is null)return;
                    ConfirmSelection(a,id,identity.Value,page,filter);return;
                }
                PlayersMenu(a,page,filter);
            });builder.AddOption(button);
        }
        void Navigation(string label,Action<IPlayer> action,bool back=false)
        {
            var button=new ButtonMenuOption(back?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>loaded,a=>{if(CanUseSelection(a))action(a);});builder.AddOption(button);
        }
        if(page>0)Navigation("Vorherige Seite",a=>PlayersMenu(a,page-1,filter));
        if((page+1)*2<ids.Length)Navigation("Nächste Seite",a=>PlayersMenu(a,page+1,filter));
        Navigation("Zurück",PlayersRoot,true);Core.MenusAPI.OpenMenuForPlayer(p,builder.Build());
    }
    private void ConfirmSelection(IPlayer actor,ulong id,ConnectionKey identity,int page,SelectionFilter filter=SelectionFilter.All)=>module.Menu(actor,"Matchende-Zuordnung bestätigen",1,
        ("Bestätigen",a=>
        {
            var p=Core.PlayerManager.GetPlayer(identity.Slot);
            if(!CanUseSelection(a)||!IsHuman(p)||Key(p!)!=identity||!CanTarget(a.SteamID,id)){a.SendChat("[LPLK] Ziel/Berechtigung nicht mehr gültig.");return;}
            SaveSelection(id,a.SteamID);
            Core.Logger.LogInformation("LPLK_Teams: Admin {Actor} wählt Ziel {Target} für Matchende",a.SteamID,id);
            if(Effective&&!Halftime&&p!.Controller.TeamNum==GuardWinner)QueueMove(p,GuardWinner==3?2:3,true);
            PlayersMenu(a,page,filter);
        }),("Zurück",a=>PlayersMenu(a,page,filter)));
    private void SaveSelection(ulong target,ulong? actor)
    {
        if(actor is null && settings.ConfiguredSteamIds.Contains(target)){settings=config.Change(current=>current with{ConfiguredSteamIds=current.ConfiguredSteamIds.Where(id=>id!=target).ToArray()});}
        var next=new Dictionary<ulong,ulong>(selected);
        if(actor is {} id)next[target]=id;else next.Remove(target);
        if(next.Count>256)throw new InvalidOperationException("Maximal 256 Auswahlen.");
        AtomicJson.Write(SelectionPath,next);selected=next;revision++;WriteStatus();
    }
}
