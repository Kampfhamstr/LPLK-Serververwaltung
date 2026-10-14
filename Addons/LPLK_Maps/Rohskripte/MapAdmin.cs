using System.Reflection;
using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Players;

namespace LPLK_Maps;

public sealed partial class Plugin
{
    private void OpenAdmin(IPlayer player)
    {
        var actions=new List<(string,Action<IPlayer>)>();
        if(settings.Enabled&&!settings.MeasurementOnly)
        {
            actions.Add(("RTV",p=>{if(!StartVote(Reason.Admin))p.SendChat("[LPLK] Abstimmung momentan nicht verfügbar.");}));
            actions.Add(("Nominieren",p=>p.ExecuteCommand("sw_nominate")));
        }
        actions.Add(("Mapwechsel",p=>Selection(p,"Mapwechsel",maps.Where(m=>m.IsMap&&!m.Removed&&!settings.ExcludedWorkshopIds.Contains(m.Id)).OrderBy(m=>m.Name).ToList(),ConfirmMapChange)));
        actions.Add(("Maptest",MapTestMenu));
        actions.Add(("Einstellungen",MapSettingsMenu));
        module.Menu(player,"Maps",actions.ToArray());
    }
    private void ConfirmMapChange(IPlayer p,MapEntry selected)=>module.Menu(p,"Wechsel zu "+MapPolicy.Escape(selected.Name)+"?",1,
        ("Ja",a=>{var current=maps.FirstOrDefault(m=>m.Id==selected.Id);if(current is not null&&!current.Removed&&!settings.ExcludedWorkshopIds.Contains(current.Id))Schedule(current,settings.ChangeDelaySeconds);}),
        ("Nein",OpenAdmin));
    private void MapTestMenu(IPlayer p)=>module.Menu(p,"Maptest",1,
        ("Vermessen",a=>MeasureCurrent(a)),
        ("Grenzen",OpenLimitReview),
        ("Zurück",OpenAdmin));
    private void MapSettingsMenu(IPlayer player)=>module.Menu(player,"Einstellungen",1,
        ("Grenzen",OpenLimitReview),
        ("Mapliste",p=>Selection(p,"LPLK · Mapverwaltung",maps.Where(m=>m.IsMap&&!settings.ExcludedWorkshopIds.Contains(m.Id)).OrderBy(m=>m.Name).ToList(),OpenMap)),
        ("Workshop",OpenWorkshop),
        ("Regeln",OpenRules),
        ("Abgleichen",p=>{Scan();RefreshCollection();p.SendChat("[LPLK] "+poolMessage);WriteStatus();}),
        ("Zurück",OpenAdmin));
    private void OpenLimitReview(IPlayer player)=>Selection(player,"LPLK · Grenzen: Freigabe offen",maps.Where(m=>MapPolicy.Measured(m)&&!MapPolicy.LimitsApproved(m)).OrderBy(m=>m.Name).ToList(),OpenLimitProposal);
    private void OpenLimitProposal(IPlayer player,MapEntry selected)
    {
        if(!module.CanAdmin(player))return;var map=maps.FirstOrDefault(m=>m.Id==selected.Id);
        if(map is null||!MapPolicy.Measured(map)||map.LimitProposal is not {} proposal){player.SendChat("[LPLK] Noch keine bestätigte Messung verfügbar.");return;}
        player.SendChat($"[LPLK] {map.Name}: bisher {map.MinPlayers}–{map.MaxPlayers}, Vorschlag {proposal.MinPlayers}–{proposal.MaxPlayers}. T/CT-Spawns {map.TSpawns}/{map.CtSpawns}; Bombenplätze {map.BombSites}; Geiseln {map.Hostages}; Reserve {proposal.Reserve}.");
        module.Menu(player,"LPLK · "+MapPolicy.Escape(map.Name),
            ("Messwerte",a=>OpenLimitProposal(a,map)),
            ("Übernehmen",a=>
            {
                var current=maps.First(m=>m.Id==map.Id);
                try{ReplaceMap(MapPolicy.ApproveLimits(current,proposal.Signature));WriteStatus();a.SendChat("[LPLK] Genau diese Grenzen freigegeben. Manuelle Sperren und Prüfmodus bleiben unverändert.");OpenLimitReview(a);}
                catch(InvalidDataException e){a.SendChat("[LPLK] "+e.Message);OpenLimitProposal(a,current);}
            }),
            ("Abbrechen",OpenLimitReview));
    }
    private void OpenMap(IPlayer p,MapEntry selected)
    {
        if(!module.CanAdmin(p))return;var map=maps.FirstOrDefault(m=>m.Id==selected.Id);if(map is null)return;
        module.Menu(p,"LPLK · "+MapPolicy.Escape(map.Name),
            ("Grenzen",a=>OpenLimitProposal(a,map)),
            ("Starten",a=>ConfirmMapChange(a,map)),
            ($"Pool: {(map.Enabled?"Ja":"Nein")}",a=>{var current=maps.First(m=>m.Id==map.Id);ReplaceMap(current with{Enabled=!current.Enabled});OpenMap(a,current);}),
            ($"Gesperrt: {(map.Removed?"Ja":"Nein")}",a=>{var current=maps.First(m=>m.Id==map.Id);ReplaceMap(current with{Removed=!current.Removed});OpenMap(a,current);}),
            ($"Spielergrenze: {map.MinPlayers}–{map.MaxPlayers}",a=>Input(a,"Maximale Spielerzahl (0 = automatisch)",3,v=>int.TryParse(v,out int n)&&n is >=0 and <=128,(actor,v)=>
            {int value=int.Parse(v);var current=maps.First(m=>m.Id==map.Id);ReplaceMap(current with{ConfiguredMaxPlayers=value==0?null:value,MeasuredAt=null,LastProbeAt=null,ProbeError=null});if(MapPolicy.Confirmed(current,CurrentMap,Workshop))MeasureCurrent(actor);else actor.SendChat("[LPLK] Grenze gespeichert; automatische Neuprüfung vorgemerkt.");})),
            ("Neu prüfen",a=>{var current=maps.First(m=>m.Id==map.Id);ReplaceMap(current with{MeasuredAt=null,LastProbeAt=null,ProbeError=null});a.SendChat("[LPLK] Automatische Prüfung vorgemerkt.");}),
            ("Messwerte",a=>a.SendChat($"[LPLK] T-Spawns: {map.TSpawns?.ToString()??"?"}; CT: {map.CtSpawns?.ToString()??"?"}; Bombenplätze: {map.BombSites?.ToString()??"?"}; Geiseln: {map.Hostages?.ToString()??"?"}. {map.ProbeError}")));
    }
    private void OpenWorkshop(IPlayer p)=>module.Menu(p,"LPLK · Workshop-Pool",
        ($"Kollektion: {(settings.CollectionId.Length>0?settings.CollectionId:"nicht gesetzt")}",a=>Input(a,"Workshop-Kollektions-ID (0 = aus)",20,v=>v=="0"||MapPolicy.WorkshopId(v) is not null,(actor,v)=>Apply(settings with{CollectionId=v=="0"?"":MapPolicy.WorkshopId(v)!},actor))),
        ($"Hinzufügen ({settings.WorkshopIds.Length})",a=>Input(a,"Workshop-Map-ID",20,v=>MapPolicy.WorkshopId(v) is not null,(actor,v)=>Apply(settings with{WorkshopIds=settings.WorkshopIds.Append(MapPolicy.WorkshopId(v)!).Distinct().ToArray()},actor))),
        ("Entfernen",a=>Input(a,"Workshop-Map-ID",20,v=>MapPolicy.WorkshopId(v) is not null,(actor,v)=>Apply(settings with{WorkshopIds=settings.WorkshopIds.Where(id=>id!=MapPolicy.WorkshopId(v)).ToArray()},actor))),
        ($"Automessung: {(settings.AutoMeasureEmptyServer?"Ja":"Nein")}",a=>Apply(settings with{AutoMeasureEmptyServer=!settings.AutoMeasureEmptyServer},a)),
        ("Abgleichen",a=>{RefreshCollection();a.SendChat("[LPLK] Workshop-Abgleich gestartet.");}));
    private void Apply(MapSettings next,IPlayer p)
    {
        MapSettings.Validate(next);AtomicJson.Write(ConfigPath,new {LPLK_Maps=next});settings=next;configStamp=File.GetLastWriteTimeUtc(ConfigPath);nextPoolRefresh=0;
        CancelVote();rtv.Clear();Scan();p.SendChat("[LPLK] Regeln gespeichert.");OpenAdmin(p);
    }
    private static readonly Dictionary<string,string> Labels=new()
    {
        [nameof(MapSettings.MeasurementOnly)]="Prüfmodus",
        [nameof(MapSettings.Enabled)]="Aktiv",[nameof(MapSettings.RtvEnabled)]="RTV aktiv",[nameof(MapSettings.RtvInWarmup)]="Warmup-RTV",[nameof(MapSettings.RtvPercent)]="RTV-Prozent",
        [nameof(MapSettings.RtvMinPlayers)]="RTV-Mindestspieler",[nameof(MapSettings.RtvMinRounds)]="RTV-Mindestrunden",[nameof(MapSettings.RtvAtRoundEnd)]="Rundenende",
        [nameof(MapSettings.RtvDelaySeconds)]="RTV-Wartezeit",[nameof(MapSettings.VoteSeconds)]="Votedauer",[nameof(MapSettings.MapsToShow)]="Mapanzahl",
        [nameof(MapSettings.CooldownMaps)]="Map-Cooldown",[nameof(MapSettings.AllowSpectatorsToVote)]="Zuschauer",[nameof(MapSettings.AllowCurrentMap)]="Wiederholung",
        [nameof(MapSettings.EndOfMapVote)]="Endvote",[nameof(MapSettings.TriggerRoundsBeforeEnd)]="Votevorlauf",[nameof(MapSettings.ForceDifferentMapAtMatchEnd)]="Mapwechsel",
        [nameof(MapSettings.FallbackRandomMap)]="Ersatzmap",[nameof(MapSettings.ChangeDelaySeconds)]="RTV-Wechselverzögerung",[nameof(MapSettings.MatchEndDelaySeconds)]="Endverzögerung",
        [nameof(MapSettings.VerifySeconds)]="Mapwechsel-Prüfintervall",[nameof(MapSettings.MaxAttempts)]="Wechselversuche",[nameof(MapSettings.SetNextLevel)]="Nextlevel",
        [nameof(MapSettings.RequireVpkFile)]="Dateiprüfung",[nameof(MapSettings.RescanOnMapLoad)]="Startprüfung",[nameof(MapSettings.RescanBeforeVote)]="Voteprüfung",
        [nameof(MapSettings.AutoVoteMinutes)]="Zeitvote",[nameof(MapSettings.PoolRefreshMinutes)]="Abgleichintervall",[nameof(MapSettings.EmptySecondsBeforeProbe)]="Prüfwartezeit",
        [nameof(MapSettings.MaxWorkshopGiB)]="Speicherlimit",[nameof(MapSettings.MinimumFreeDiskGiB)]="Speicherreserve"
    };
    private void OpenRules(IPlayer p)
    {
        var actions=Labels.Select(item=>
        {
            var property=typeof(MapSettings).GetProperty(item.Key)!;object value=property.GetValue(settings)!;
            Action<IPlayer> action=actor=>
            {
                if(property.PropertyType==typeof(bool)){var next=settings with{};property.SetValue(next,!(bool)property.GetValue(settings)!);Apply(next,actor);}
                else Input(actor,item.Value,4,v=>int.TryParse(v,out _),(player,v)=>{var next=settings with{};property.SetValue(next,int.Parse(v));Apply(next,player);});
            };
            return(item.Value+": "+(value is bool b?(b?"Ja":"Nein"):value),action);
        }).ToArray();module.Menu(p,"LPLK · Mapregeln",actions);
    }
    private void Input(IPlayer player,string label,int length,Func<string,bool> validator,Action<IPlayer,string> change)
    {
        if(!module.CanAdmin(player))return;int serial=generation;ulong owner=player.SteamID;
        var input=new InputMenuOption(new MenuNumbers().Next(label),maxLength:length,validator:validator,hintMessage:"Wert in den Chat eingeben.");
        input.ValueChanged+=(_,a)=>
        {
            if(serial==generation&&a.Player.SteamID==owner&&module.CanAdmin(a.Player))
            {try{change(a.Player,a.NewValue);}catch(InvalidDataException e){a.Player.SendChat("[LPLK] "+e.Message);}}
        };
        Core.MenusAPI.OpenMenuForPlayer(player,Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design.SetMenuTitle(MenuColors.Heading("LPLK · "+label)).AddOption(input).Build());
    }
}

