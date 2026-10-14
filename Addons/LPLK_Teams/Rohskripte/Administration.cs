using System.Globalization;
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
    [CommandAlias("tsw")]
    public void AdminCommand(ICommandContext c)
    {
        if(c.Sender is {} p){OpenAdmin(p);return;}
        Refresh();WriteStatus();
        c.Reply($"LPLK_Teams 0.1.1: enabled={settings.Enabled}; ready={ready}; bridgeReady={BridgeReady()}; CT={score?.Ct}; T={score?.T}; source={tracker.Source}; rounds={rounds}; threshold={TeamPolicy.Threshold(settings,rounds)}; selected={selected.Count}; errors={errors}; reloadError={reloadError??"none"}");
    }
    [Command("tsbscore")]
    public void ScoreCommand(ICommandContext c)
    {
        if(c.Sender is not null&&!settings.AllowChatStatusCommand)return;
        Refresh();
        c.Reply($"[LPLK] Teams {(settings.Enabled?"AN":"AUS")}: CT {score?.Ct.ToString()??"?"} : {score?.T.ToString()??"?"} T; geschützt: {ProtectedText()}; Grenze {TeamPolicy.Threshold(settings,rounds)} / {rounds}; Halbzeit {Halftime}; Quelle {tracker.Source}.");
    }
    private void OpenAdmin(IPlayer p)=>module.Menu(p,"LPLK · Teamwechsel",
        ($"Aktiv: {settings.Enabled}",a=>Set(a,nameof(TeamSettings.Enabled),!settings.Enabled)),
        ("Spielstand",a=>{Refresh();a.SendChat($"[LPLK] CT {score?.Ct} : {score?.T} T | {ProtectedText()} | Grenze {TeamPolicy.Threshold(settings,rounds)} / {rounds} | {roundsSource} | {tracker.Source}");OpenAdmin(a);}),
        ("Sperrregeln",a=>SettingsMenu(a,0)),
        ("Matchende",a=>SettingsMenu(a,1)),
        ("Meldungen",a=>SettingsMenu(a,2)),
        ($"Verliererteam ({selected.Count}) …",a=>PlayersMenu(a,0)));
    private static readonly (string Name,string Label)[][] Groups=
    [
        [(nameof(TeamSettings.AutoDetectMaxRounds),"Autorunden"),(nameof(TeamSettings.FallbackMaxRounds),"Ersatz-Rundenzahl"),(nameof(TeamSettings.BlockAtMaxRoundsPercent),"Sperranteil"),
         (nameof(TeamSettings.BlockOnlyIfTeamIsLeading),"Führungsschutz"),(nameof(TeamSettings.BlockBothTeamsIfBothReachedLimit),"Doppelsperre"),(nameof(TeamSettings.AllowSpectator),"Zuschauer erlauben"),(nameof(TeamSettings.ForceBackIfSwitchStillHappened),"Rückversetzen")],
        [(nameof(TeamSettings.AllowHalftimeTeamSwitchException),"Halbzeitausnahme"),(nameof(TeamSettings.HalftimeSwitchGraceSeconds),"Halbzeitdauer"),(nameof(TeamSettings.TreatWinScoreAsMatchEnd),"Siegpunkt"),
         (nameof(TeamSettings.BlockConfiguredSteamIdsFromWinningTeamDuringMatchEnd),"Verliererzuordnung"),(nameof(TeamSettings.MatchEndGuardWindowSeconds),"Sperrdauer")],
        [(nameof(TeamSettings.UseRoundEndFallback),"Rundenersatz"),(nameof(TeamSettings.AnnounceWhenTeamBecomesProtected),"Sperrteam ankündigen"),(nameof(TeamSettings.ShowBlockedMessageInChat),"Chathinweis"),
         (nameof(TeamSettings.ShowBlockedMessageInCenter),"Bildschirmhinweis"),(nameof(TeamSettings.ShowForceToLosingTeamMessage),"Verliererteam-Hinweis"),(nameof(TeamSettings.AllowChatStatusCommand),"Spielstandbefehl")]
    ];
    private void SettingsMenu(IPlayer p,int group)
    {
        var rows=new List<(string,Action<IPlayer>)>();
        foreach(var (name,label) in Groups[group])
        {
            var property=typeof(TeamSettings).GetProperty(name)!;object value=property.GetValue(settings)!;
            rows.Add(($"{label}: {value}",a=>{if(value is bool)Set(a,name,!(bool)property.GetValue(settings)!,group);else InputNumber(a,name,label,group);}));
        }
        rows.Add(("Zurück",OpenAdmin));module.Menu(p,"Teams · Einstellungen",1,rows.ToArray());
    }
    private void Set(IPlayer actor,string name,object value,int? group=null)
    {
        if(!module.CanAdmin(actor))return;
        var next=settings with{};typeof(TeamSettings).GetProperty(name)!.SetValue(next,value);TeamSettings.Validate(next);
        module.SaveSettings(next);settings=next;stamp=File.GetLastWriteTimeUtc(module.SettingsPath);revision++;permits.Clear();reloadError=null;
        Core.Logger.LogInformation("LPLK_Teams: Admin {Actor}, {Setting}={Value}",actor.SteamID,name,value);
        Refresh();WriteStatus();if(group is {} g)SettingsMenu(actor,g);else OpenAdmin(actor);
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
        Core.MenusAPI.OpenMenuForPlayer(p,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teams · Wert ändern")).Design.SetMaxVisibleItems(5).AddOption(input).AddOption(back).Build());
    }
    private void PlayersMenu(IPlayer p,int page)
    {
        if(!module.CanAdmin(p))return;
        var ids=Core.PlayerManager.GetAllValidPlayers().Where(IsHuman).Select(x=>x.SteamID).Concat(selected.Keys).Distinct().Order().ToArray();
        page=Math.Clamp(page,0,Math.Max(0,(ids.Length-1)/8));
        var builder=Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("Teams · Verliererteam-Auswahl")).Design.SetMaxVisibleItems(5);
        var numbers=new MenuNumbers();
        foreach(var id in ids.Skip(page*8).Take(8))
        {
            var target=Core.PlayerManager.GetAllValidPlayers().FirstOrDefault(x=>x.SteamID==id&&IsHuman(x));
            ConnectionKey? identity=target is null?null:Key(target);
            string label=WebUtility.HtmlEncode(target?.Controller.PlayerName??$"Offline {id}");bool active=selected.ContainsKey(id);
            var button=new ButtonMenuOption(numbers.Next($"{label}: {(active?"AN":"AUS")}",enabled:active)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>loaded,a=>
            {
                if(!module.CanAdmin(a)||!CanTarget(a.SteamID,id)){a.SendChat("[LPLK] Ziel hat gleiche/höhere Rechte.");return;}
                if(identity is {} expected)
                {
                    var fresh=Core.PlayerManager.GetPlayer(expected.Slot);
                    if(!IsHuman(fresh)||Key(fresh!)!=expected){a.SendChat("[LPLK] Spielerverbindung hat sich geändert.");PlayersMenu(a,page);return;}
                }
                if(selected.ContainsKey(id))
                {
                    if(selected[id]!=a.SteamID&&!CanTarget(a.SteamID,selected[id])){a.SendChat("[LPLK] Auswahl eines gleich/höher berechtigten Admins.");return;}
                    SaveSelection(id,null);
                }
                else
                {
                    if(identity is null)return;
                    ConfirmSelection(a,id,identity.Value,page);return;
                }
                PlayersMenu(a,page);
            });builder.AddOption(button);
        }
        void Navigation(string label,Action<IPlayer> action,bool back=false)
        {
            var button=new ButtonMenuOption(back?MenuColors.Back(label):numbers.Next(label)){CloseAfterClick=false};
            GameThreadMenu.Bind(Core,button,p,()=>loaded,a=>{if(module.CanAdmin(a))action(a);});builder.AddOption(button);
        }
        if(page>0)Navigation("Vorherige Seite",a=>PlayersMenu(a,page-1));
        if((page+1)*8<ids.Length)Navigation("Nächste Seite",a=>PlayersMenu(a,page+1));
        Navigation("Zurück",OpenAdmin,true);Core.MenusAPI.OpenMenuForPlayer(p,builder.Build());
    }
    private void ConfirmSelection(IPlayer actor,ulong id,ConnectionKey identity,int page)=>module.Menu(actor,"Matchende-Zuordnung bestätigen",1,
        ("Bestätigen",a=>
        {
            var p=Core.PlayerManager.GetPlayer(identity.Slot);
            if(!module.CanAdmin(a)||!IsHuman(p)||Key(p!)!=identity||!CanTarget(a.SteamID,id)){a.SendChat("[LPLK] Ziel/Berechtigung nicht mehr gültig.");return;}
            SaveSelection(id,a.SteamID);
            Core.Logger.LogInformation("LPLK_Teams: Admin {Actor} wählt Ziel {Target} für Matchende",a.SteamID,id);
            if(Effective&&!Halftime&&p!.Controller.TeamNum==GuardWinner)QueueMove(p,GuardWinner==3?2:3,true);
            PlayersMenu(a,page);
        }),("Zurück",a=>PlayersMenu(a,page)));
    private void SaveSelection(ulong target,ulong? actor)
    {
        var next=new Dictionary<ulong,ulong>(selected);
        if(actor is {} id)next[target]=id;else next.Remove(target);
        if(next.Count>256)throw new InvalidOperationException("Maximal 256 Auswahlen.");
        AtomicJson.Write(SelectionPath,next);selected=next;revision++;WriteStatus();
    }
}
