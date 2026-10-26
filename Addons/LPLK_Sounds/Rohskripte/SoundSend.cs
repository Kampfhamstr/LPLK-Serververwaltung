using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Sounds;

namespace LPLK_Sounds;

public sealed partial class Plugin
{
    private sealed record DirectedSound(ulong SenderSteamId, SoundBroadcast Sound);
    private readonly Dictionary<int, DirectedSound> directedSounds = [];
    private readonly SoundSendLimit soundSendLimit = new();
    private static bool RealSoundPlayer(IPlayer p) => p is { IsValid:true, IsAuthorized:true, IsFakeClient:false }
        && p.SteamID != 0 && p.Slot is >= 0 and < 64;
    private static string SoundPlayerName(IPlayer p) => new string((p.Controller?.PlayerName ?? $"Spieler #{p.Slot + 1}")
        .Where(c=>!char.IsControl(c)).Take(64).ToArray());
    private string PrivateSoundLimitTitle(IPlayer player,string title) => title + " | " +
        (CanOverrideSound(player)?"unbegrenzt (Admin)":"1 je Empfänger/Runde");

    [Command("soundsend", registerRaw:true, helpText:"Sound an einen Spieler senden: soundsend [Soundnummer]")]
    public void SendSoundCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player) return;
        if (context.Args.Length == 0) { OpenPlayerMenu(player,()=>ShowSendSoundMenu(player)); return; }
        if (context.Args.Length != 1 || !SoundMenuPolicy.TryNumber(context.Args[0],out int number))
        { context.ReplyLplk("[LPLK Sounds] !soundsend <Soundnummer> oder !soundsend fuer die Liste."); return; }
        CommandAction(context,()=>
        {
            var entry = NumberedSounds.List(catalog).FirstOrDefault(s=>s.Number == number);
            if (entry is null) { context.ReplyLplk("[LPLK Sounds] Unbekannte Soundnummer."); return; }
            if (!RequireSoundAccess(player,entry.Id,context.ReplyLplk)) return;
            OpenPlayerMenu(player,()=>ShowSoundRecipients(player,entry,0,p=>ShowSendSoundMenu(p)));
        });
    }

    private void ShowSendSoundMenu(IPlayer player,int pageIndex=0,SoundRecipient? recipient=null,string? recipientSession=null)
    {
        var page=SoundMenuPolicy.Page(SoundList(includeAll:CanOverrideSound(player)),pageIndex);
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder(PrivateSoundLimitTitle(player,$"Sound senden {page.Index+1}/{page.Count}"));
        foreach (var entry in page.Items)
            Button(builder,owner,MenuPolicy.Label(entry.Title),p=>
            {
                if (!RequireSoundAccess(p,entry.Id,m=>Reply(p,m)))return;
                if(recipient is null)ShowSoundRecipients(p,entry,0,q=>ShowSendSoundMenu(q,page.Index));
                else
                {
                    var target=Core.PlayerManager.GetPlayer(recipient.Slot);
                    if(!module.CanAdmin(p)||target is not {IsValid:true}||target.SteamID!=recipient.SteamId||target.SessionId.ToString()!=recipientSession){Reply(p,"Spieler oder Berechtigung nicht mehr verfügbar.");return;}
                    SendSound(p,entry.Id,recipient,m=>Reply(p,m));
                }
            },soundNumber:entry.Number);
        if (page.Items.Count==0) builder.AddOption(new TextMenuOption("Noch keine Sounds vorhanden."));
        if (page.Index>0) BackButton(builder,owner,"Zurück - Seite "+page.Index,p=>ShowSendSoundMenu(p,page.Index-1,recipient,recipientSession));
        if (page.Index+1<page.Count) Button(builder,owner,"Weiter - Seite "+(page.Index+2),p=>ShowSendSoundMenu(p,page.Index+1,recipient,recipientSession));
        BackButton(builder,owner,"Soundliste",p=>ShowSoundMenu(p));
        OpenOwnedMenu(player,owner,builder);
    }

    private void ShowSoundRecipients(IPlayer player,NumberedSound entry,int pageIndex,Action<IPlayer> back)
    {
        if (!RequireSoundAccess(player,entry.Id,m=>Reply(player,m))) return;
        var targets=Core.PlayerManager.GetAllPlayers().Where(p=>RealSoundPlayer(p) && p.SteamID!=player.SteamID)
            .OrderBy(p=>p.Slot).ToArray();
        int pages=1,page=0;
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder(PrivateSoundLimitTitle(player,$"Empfänger {page+1}/{pages}: {MenuPolicy.Label(entry.Title)}"));
        foreach (var target in targets)
        {
            var identity=new SoundRecipient(target.Slot,target.SteamID);
            string label=MenuPolicy.Label(SoundPlayerName(target))+" · "+(AliveForSounds(target)?"lebend":"tot/Zuschauer");
            Button(builder,owner,label,p=>
            {
                if (SendSound(p,entry.Id,identity,m=>Reply(p,m))) back(p);
            },"Nur an diesen Spieler senden. Sounds AUS und persönliche Hörfilter werden respektiert.");
        }
        if (targets.Length==0) builder.AddOption(new TextMenuOption("Keine anderen Spieler verbunden."));
        if (page>0) BackButton(builder,owner,"Zurück - Seite "+page,p=>ShowSoundRecipients(p,entry,page-1,back));
        if (page+1<pages) Button(builder,owner,"Weiter - Seite "+(page+2),p=>ShowSoundRecipients(p,entry,page+1,back));
        Button(builder,owner,"Aktualisieren",p=>ShowSoundRecipients(p,entry,page,back));
        BackButton(builder,owner,"Zurück",back);
        OpenOwnedMenu(player,owner,builder);
    }

    private bool SendSound(IPlayer source,string id,SoundRecipient identity,Action<string> reply)
    {
        if (!RealSoundPlayer(source)) { reply("[LPLK Sounds] Soundstart nur mit bestaetigter Spieler-ID moeglich."); return false; }
        if (!RequireSoundAccess(source,id,reply)) return false;
        var target=Core.PlayerManager.GetPlayer(identity.Slot);
        if (target is null || !RealSoundPlayer(target) || target.SteamID!=identity.SteamId)
        { reply("[LPLK Sounds] Dieser Spieler ist nicht mehr verfuegbar. Spielerliste aktualisieren."); return false; }
        if (source.SteamID==target.SteamID) { reply("[LPLK Sounds] Bitte einen anderen Spieler auswaehlen."); return false; }
        if (Listening(target).BlockedSoundSenders.Contains(source.SteamID.ToString()))
        { reply("[LPLK Sounds] Dieser Spieler hat private Sounds von dir blockiert."); return false; }
        if (SoundSendPolicy.Rejection(AliveForSounds(source),config.DeadPlayersOnlyToDead,Listener(target,id),Listening(target),id) is string rejection)
        { reply("[LPLK Sounds] "+rejection); return false; }
        var result=soundSendLimit.TryStart(source.SteamID,target.SteamID,()=>EmitDirectedSound(source,target,id,reply),bypassLimit:CanOverrideSound(source));
        if (result==SoundStartResult.LimitReached)
        { reply("[LPLK Sounds] Du hast diesem Spieler in dieser Runde bereits einen privaten Sound gesendet. Naechste Runde wieder moeglich."); return false; }
        if (result!=SoundStartResult.Started) return false;
        RefreshSoundLimitTitles();
        reply($"[LPLK Sounds] {catalog.Sounds[id].Title} nur an {SoundPlayerName(target)} gesendet.");
        AnnounceSoundPlayback(source,id,target);
        return true;
    }

    private bool EmitDirectedSound(IPlayer source,IPlayer target,string id,Action<string> reply)
    {
        if (Unavailable(id) is string reason) { reply("[LPLK Sounds] "+reason); return false; }
        long now=Environment.TickCount64;
        if (broadcast is { AdminOverride:true } priority && !priority.Expired(now))
        { reply("[LPLK Sounds] Ein Adminsound laeuft noch. Danach erneut versuchen."); return false; }
        if (lastBroadcast is long last && !PlaybackPolicy.Expired(last,now,config.BroadcastCooldownSeconds))
        { reply($"[LPLK Sounds] Gemeinsamer Sound-Cooldown: noch {Math.Ceiling(config.BroadcastCooldownSeconds-(now-last)/1000.0):0} s."); return false; }
        bool deadOnly=SoundAudience.RestrictToDead(AliveForSounds(source),config.DeadPlayersOnlyToDead);
        var groups=SoundAudience.Groups([Listener(target,id)],deadOnly);
        if (groups.Length!=1) { reply("[LPLK Sounds] Dieser Spieler kann den Sound aktuell nicht hoeren."); return false; }
        try
        {
            var sound=catalog.Sounds[id];
            StopDirectedSound(target.Slot);
            RemoveBroadcastListener(target.Slot);
            directedSounds[target.Slot]=new(source.SteamID,SoundBroadcast.Start(id,now,sound.DurationSeconds,groups,group=>
            {
                using var ev=new SoundEvent(sound.Event,sound.Volume*group.VolumePercent/100f,1.0f);
                ev.Recipients.RemoveAllPlayers();
                ev.Recipients.AddRecipient(target.Slot);
                return ev.Emit();
            },StopSoundFor,deadOnly));
            lastBroadcast=now;
            return true;
        }
        catch (Exception ex)
        { Logger.LogError(ex,"Gezielter Soundstart fehlgeschlagen."); reply("[LPLK Sounds] Soundstart fehlgeschlagen; Serverlog pruefen."); return false; }
    }

    private void StopDirectedSound(int slot)
    {
        if (!directedSounds.Remove(slot,out var directed)) return;
        foreach (var (recipient,delivery) in directed.Sound.Recipients) StopSoundFor(delivery.Guid,recipient,delivery.SteamId);
    }
    private void SweepDirectedSounds()
    {
        foreach (var (slot,directed) in directedSounds.ToArray())
        {
            var sound=directed.Sound;
            var target=Core.PlayerManager.GetPlayer(slot);
            if (sound.Expired(Environment.TickCount64) || target is null || Listening(target).BlockedSoundSenders.Contains(directed.SenderSteamId.ToString()) || sound.Recipients.Any(r=>
                !SoundAudience.SameConnection(r.Key,r.Value.SteamId,Listener(target)) ||
                !SoundAudience.Eligible(Listener(target,sound.SoundId),sound.DeadOnly))) StopDirectedSound(slot);
        }
    }

    private void ShowSoundSenderBlocks(IPlayer player,int pageIndex=0)
    {
        var blocked=Listening(player).BlockedSoundSenders;
        var connected=Core.PlayerManager.GetAllPlayers().Where(p=>RealSoundPlayer(p) && p.SteamID!=player.SteamID)
            .DistinctBy(p=>p.SteamID).ToDictionary(p=>p.SteamID.ToString(),SoundPlayerName);
        var ids=connected.Keys.Concat(blocked).Distinct().OrderBy(id=>connected.GetValueOrDefault(id)??id,StringComparer.OrdinalIgnoreCase).ToArray();
        int pages=1,page=0;
        var owner=new OwnedMenu(player.Slot,player.SteamID);
        var builder=MenuBuilder($"Private Sounds blockieren {page+1}/{pages}");
        foreach(string id in ids)
        {
            string name=connected.GetValueOrDefault(id)??("Offline: "+id);
            Button(builder,owner,(blocked.Contains(id)?"[BLOCKIERT] ":"[ERLAUBT] ")+MenuPolicy.Label(name),p=>
            {
                ToggleSoundSenderBlock(p,id);
                ShowSoundSenderBlocks(p,page);
            },enabled:!blocked.Contains(id));
        }
        if(ids.Length==0)builder.AddOption(new TextMenuOption("Keine anderen Spieler oder gespeicherten Sperren."));
        if(page>0)BackButton(builder,owner,"Zurück - Seite "+page,p=>ShowSoundSenderBlocks(p,page-1));
        if(page+1<pages)Button(builder,owner,"Weiter - Seite "+(page+2),p=>ShowSoundSenderBlocks(p,page+1));
        Button(builder,owner,"Aktualisieren",p=>ShowSoundSenderBlocks(p,page));
        BackButton(builder,owner,"Einstellungen",p=>ShowSoundSettings(p,0,0));
        OpenOwnedMenu(player,owner,builder);
    }

    private void ToggleSoundSenderBlock(IPlayer player,string senderId)
    {
        if(!SelectionPolicy.ValidSteamId(senderId) || senderId==player.SteamID.ToString()) return;
        var preference=Listening(player);bool block=!preference.BlockedSoundSenders.Contains(senderId);
        soundPreferences.Set(player.SteamID.ToString(),preference with {
            BlockedSoundSenders=block?preference.BlockedSoundSenders.Add(senderId):preference.BlockedSoundSenders.Remove(senderId)
        });
        if(block && directedSounds.TryGetValue(player.Slot,out var active) && active.SenderSteamId.ToString()==senderId) StopDirectedSound(player.Slot);
        Reply(player,block?"[LPLK Sounds] Private Sounds dieses Spielers blockiert (gespeichert).":"[LPLK Sounds] Private Sounds dieses Spielers wieder erlaubt (gespeichert).");
    }

    private void AnnounceSoundPlayback(IPlayer source,string id,IPlayer? target=null,bool adminOverride=false)
    {
        string number=soundNumbers?.List(catalog).FirstOrDefault(s=>s.Id==id)?.Number.ToString()??id;
        string message=target is null
            ? $"[LPLK Sounds] {SoundPlayerName(source)} spielt öffentlich: {number}. {catalog.Sounds[id].Title}"+(adminOverride?" (Admin-Override).":".")
            : $"[LPLK Sounds ADMIN] Privat: {SoundPlayerName(source)} -> {SoundPlayerName(target)} | {number}. {catalog.Sounds[id].Title}";
        foreach(var observer in Core.PlayerManager.GetAllPlayers().Where(RealSoundPlayer))
        {
            try { if(target is null || CanOverrideSound(observer)) Reply(observer,message); }
            catch(Exception ex) { Logger.LogWarning(ex,"Sound-Debug konnte nicht an Slot {Slot} gesendet werden.",observer.Slot); }
        }
        if(target is not null) Logger.LogInformation("Privater Sound: Absender {SenderSteamID}, Empfaenger {RecipientSteamID}, Sound {SoundId}.",source.SteamID,target.SteamID,id);
    }
}
