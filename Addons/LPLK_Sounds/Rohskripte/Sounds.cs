using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.Sounds;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LPLK_Sounds;

public sealed partial class Plugin
{
    private SoundPreferenceStore soundPreferences = null!;
    private SoundBroadcast? broadcast;
    private long? lastBroadcast;
    private readonly SoundRoundLimit soundRoundLimit = new();

    private SoundListeningPreference Listening(IPlayer player) => soundPreferences.Get(player.SteamID.ToString());
    private SoundListener Listener(IPlayer player, string? soundId = null)
    {
        var preference = Listening(player);
        bool alive = AliveForSounds(player);
        return new(player.Slot, player.SteamID, player.IsAuthorized, player.IsValid, player.IsFakeClient,
            preference.Allows(alive, soundId), alive, preference.VolumePercent);
    }
    private static bool AliveForSounds(IPlayer player) => player.IsValid &&
        SoundAudience.IsPlayingAlive(player.IsAlive, player.Controller?.TeamNum ?? 0);
    private bool HearsSounds(IPlayer player, string? soundId = null) => SoundAudience.CanHear(Listener(player, soundId));

    [Command("lplk_sound_deadonly", registerRaw:true, helpText:"Server: Sounds toter Spieler nur fuer Tote: lplk_sound_deadonly <on|off>")]
    public void DeadOnlyCommand(ICommandContext context)
    {
        if (!ConsoleOnly(context)) return;
        if (context.Args.Length == 0)
        { context.Reply("[LPLK Sounds] Tote spielen nur fuer Tote: " + (config.DeadPlayersOnlyToDead ? "AN" : "AUS") + ". Aendern: lplk_sound_deadonly on/off"); return; }
        if (context.Args.Length != 1 || context.Args[0] is not ("on" or "off"))
        { context.Reply("lplk_sound_deadonly <on|off>"); return; }
        CommandAction(context, () =>
        {
            bool enabled = context.Args[0] == "on";
            var node = JsonNode.Parse(DataFiles.ReadLimited(ConfigPath), documentOptions: new JsonDocumentOptions {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            })?.AsObject() ?? throw new InvalidDataException("Leere Konfiguration.");
            string key = node.Select(x => x.Key).FirstOrDefault(x => x.Equals(nameof(SoundsConfig.DeadPlayersOnlyToDead), StringComparison.OrdinalIgnoreCase))
                ?? nameof(SoundsConfig.DeadPlayersOnlyToDead);
            node[key] = enabled;
            DataFiles.WriteAtomic(ConfigPath, node.ToJsonString(SoundsConfig.Json));
            if (config.DeadPlayersOnlyToDead != enabled) StopBroadcast();
            config.DeadPlayersOnlyToDead = enabled;
            // Close stale rule/status views; reopening reads the persisted setting.
            CloseAllOwnedMenus();
            context.Reply("[LPLK Sounds] Tote spielen nur fuer Tote: " + (enabled ? "AN" : "AUS") + " (gespeichert). Das Rundenlimit bleibt bei drei Sounds pro Spieler; Admins sind ausgenommen.");
        });
    }

    [Command("lplk_sound_listen", registerRaw:true, helpText:"LPLK-Sounds-Sounds fuer dich: lplk_sound_listen <on|off>")]
    public void ListeningCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player) return;
        if (context.Args.Length == 0)
        {
            context.Reply("[LPLK Sounds] " + ListeningSummary(player) + " Einstellungen: !sound.");
            return;
        }
        if (context.Args.Length != 1 || context.Args[0].ToLowerInvariant() is not ("on" or "off"))
        { context.Reply("[LPLK Sounds] lplk_sound_listen <on|off>"); return; }
        CommandAction(context, () => SetListening(player, context.Args[0].Equals("on", StringComparison.OrdinalIgnoreCase), context.Reply));
    }

    private void SetListening(IPlayer player, bool enabled, Action<string> reply)
    {
        soundPreferences.Set(player.SteamID.ToString(), enabled);
        if (!enabled) StopLocalSounds(player.Slot);
        reply("[LPLK Sounds] Sounds hoeren: " + (enabled ? "AN" : "AUS") + " (gespeichert). Admin-Override ist ausgenommen.");
    }

    [Command("soundvolume", registerRaw:true, helpText:"Persoenliche LPLK-Sounds-Lautstaerke: soundvolume <0-100>")]
    public void SoundVolumeCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player) return;
        if (context.Args.Length == 0)
        { context.Reply("[LPLK Sounds] " + ListeningSummary(player) + " Lautstaerke: !soundvolume 0-100"); return; }
        if (context.Args.Length != 1 || !int.TryParse(context.Args[0], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int volume) || volume is < 0 or > 100)
        { context.Reply("[LPLK Sounds] !soundvolume <0-100>, zum Beispiel !soundvolume 40"); return; }
        CommandAction(context, () => SetSoundVolume(player, volume, context.Reply));
    }

    private string ListeningSummary(IPlayer player)
    {
        var p = Listening(player);
        return $"Sounds: {(p.Enabled ? "AN" : "AUS")}; Lautstaerke {p.VolumePercent}%; lebend {(p.HearAlive ? "AN" : "AUS")}; tot/Zuschauer {(p.HearDead ? "AN" : "AUS")}; {p.MutedSounds.Count} einzeln stumm.";
    }
    private void SetSoundVolume(IPlayer player, int volume, Action<string> reply)
    {
        soundPreferences.Set(player.SteamID.ToString(), Listening(player) with { VolumePercent = volume });
        if (volume == 0) StopLocalSounds(player.Slot);
        reply($"[LPLK Sounds] Deine Lautstaerke: {volume}% (gespeichert; {(volume == 0 ? "sofort stumm" : "ab dem naechsten Sound")}). Admin-Override ist ausgenommen.");
    }
    private void SetSoundState(IPlayer player, bool alive, bool enabled)
    {
        var previous = Listening(player);
        soundPreferences.Set(player.SteamID.ToString(), alive ? previous with { HearAlive = enabled } : previous with { HearDead = enabled });
        if (!enabled && AliveForSounds(player) == alive) StopLocalSounds(player.Slot);
        Reply(player, "[LPLK Sounds] " + ListeningSummary(player) + " Gespeichert; Admin-Override ist ausgenommen.");
    }
    private void ToggleSoundMute(IPlayer player, string id)
    {
        var previous = Listening(player);
        bool mute = !previous.MutedSounds.Contains(id);
        soundPreferences.Set(player.SteamID.ToString(), previous with {
            MutedSounds = mute ? previous.MutedSounds.Add(id) : previous.MutedSounds.Remove(id)
        });
        if (mute)
        {
            if (directedSounds.TryGetValue(player.Slot,out var directed) && directed.Sound.SoundId == id) StopDirectedSound(player.Slot);
            if (broadcast?.SoundId == id && !broadcast.AdminOverride) RemoveBroadcastListener(player.Slot);

        }
        Reply(player, "[LPLK Sounds] " + catalog.Sounds[id].Title + (mute ? " fuer dich stumm. Admin-Override ist ausgenommen." : " fuer dich freigegeben; deine weiteren Hoerfilter gelten."));
    }

    private bool StopLocalSounds(int slot)
    {
        StopDirectedSound(slot);

        if (broadcast?.AdminOverride == true) return false;
        RemoveBroadcastListener(slot);
        return true;
    }

    [Command("lplk_sound_play", registerRaw:true, helpText:"Sound oeffentlich nach den Serverregeln abspielen: lplk_sound_play <id>")]
    public void PlaySoundCommand(ICommandContext context)
    {
        if (AuthenticatedPlayer(context) is not { } player) return;
        if (context.Args.Length != 1) { context.Reply("[LPLK Sounds] lplk_sound_play <id>"); return; }
        CommandAction(context, () => BroadcastSound(player, context.Args[0].ToLowerInvariant(), context.Reply));
    }

    private bool BroadcastSound(IPlayer source, string id, Action<string>? reply = null, bool adminOverride = false)
    {
        if (source is not { IsValid: true, IsAuthorized: true, IsFakeClient: false } || source.SteamID == 0)
        { reply?.Invoke("[LPLK Sounds] Soundstart nur mit bestaetigter Spieler-ID moeglich."); return false; }
        // Recheck here, not only when the menu/command was opened.
        if (adminOverride && !RequireSoundOverride(source, reply)) return false;
        if (!RequireSoundAccess(source,id,reply)) return false;
        int listeners = 0;
        var result = soundRoundLimit.TryStart(source.SteamID, () => EmitBroadcastSound(source, id, reply, out listeners, adminOverride), bypassLimit: CanOverrideSound(source));
        if (result == SoundStartResult.LimitReached)
        {
            reply?.Invoke("[LPLK Sounds] Rundenlimit erreicht: 3/3 Sounds ausgespielt. Ab der naechsten Runde wieder moeglich.");
            return false;
        }
        if (result != SoundStartResult.Started) return false;
        RefreshSoundLimitTitles();
        string audience = broadcast?.DeadOnly == true ? "tote Zuhoerer" : "Zuhoerer";
        reply?.Invoke($"[LPLK Sounds] {(adminOverride ? "ADMIN-OVERRIDE: " : "")}{catalog.Sounds[id].Title} fuer {listeners} {audience} gestartet. {SoundLimitStatus(source)}");
        if (adminOverride) Logger.LogInformation("Admin-Sound-Override: SteamID {SteamID}, Sound {SoundId}, {Listeners} Empfaenger bei 100 Prozent Kataloglautstaerke.", source.SteamID, id, listeners);
        AnnounceSoundPlayback(source,id,adminOverride:adminOverride);
        return true;
    }

    private bool EmitBroadcastSound(IPlayer source, string id, Action<string>? reply, out int listeners, bool adminOverride = false)
    {
        listeners = 0;
        if (Unavailable(id) is string reason)
        { reply?.Invoke("[LPLK Sounds] " + reason); return false; }
        long now = Environment.TickCount64;
        if (!adminOverride && broadcast is { AdminOverride:true } priority && !priority.Expired(now))
        { reply?.Invoke("[LPLK Sounds] Ein Adminsound laeuft noch. Danach erneut versuchen."); return false; }
        if (lastBroadcast is long last && !PlaybackPolicy.Expired(last, now, config.BroadcastCooldownSeconds))
        {
            double remaining = Math.Ceiling(config.BroadcastCooldownSeconds - (now - last) / 1000.0);
            reply?.Invoke($"[LPLK Sounds] Gemeinsamer Sound-Cooldown: noch {remaining:0} s. Danach im !sound-Menue erneut auswaehlen.");
            return false;
        }
        bool deadOnly = !adminOverride && SoundAudience.RestrictToDead(AliveForSounds(source), config.DeadPlayersOnlyToDead);
        var groups = SoundAudience.Groups(Core.PlayerManager.GetAllPlayers().Select(p => Listener(p, id)), deadOnly, adminOverride);
        if (groups.Length == 0)
        { reply?.Invoke("[LPLK Sounds] Keine passenden Zuhoerer: persoenliche Hoerfilter, Lautstaerke und Totenregel pruefen."); return false; }
        try
        {
            var sound = catalog.Sounds[id];
            StopBroadcast();
            broadcast = SoundBroadcast.Start(id, now, sound.DurationSeconds, groups, group =>
            {
                using var soundEvent = new SoundEvent(sound.Event, sound.Volume * group.VolumePercent / 100f, 1.0f);
                soundEvent.Recipients.RemoveAllPlayers();
                foreach (int slot in group.Recipients.Keys) soundEvent.Recipients.AddRecipient(slot);
                return soundEvent.Emit();
            }, StopSoundFor, deadOnly, adminOverride);
            lastBroadcast = now;
            listeners = groups.Sum(g => g.Recipients.Count);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "LPLK-Sounds-Sound fuer alle konnte nicht gestartet werden.");
            reply?.Invoke("[LPLK Sounds] Soundstart fehlgeschlagen; Serverlog pruefen.");
            return false;
        }
    }

    private void StopSoundFor(uint guid, int slot, ulong steamId)
    {
        var player = Core.PlayerManager.GetPlayer(slot);
        if (player is null || !SoundAudience.SameConnection(slot, steamId, Listener(player))) return;
        try
        {
            using var message = Core.NetMessage.Create<CMsgSosStopSoundEvent>();
            message.SoundeventGuid = unchecked((int)guid);
            message.Recipients.RemoveAllPlayers();
            message.Recipients.AddRecipient(slot);
            message.Send();
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Sound-Stop fuer Slot {Slot} fehlgeschlagen.", slot); }
    }

    private void RemoveBroadcastListener(int slot)
    {
        if (broadcast is { } active && active.Remove(slot, out var delivery))
            StopSoundFor(delivery.Guid, slot, delivery.SteamId);
    }
    private void StopBroadcast()
    {
        foreach (int slot in directedSounds.Keys.ToArray()) StopDirectedSound(slot);
        var active = broadcast;
        broadcast = null;
        if (active is null) return;
        foreach (var (slot, delivery) in active.Recipients) StopSoundFor(delivery.Guid, slot, delivery.SteamId);
    }
    private void SweepBroadcast()
    {
        SweepDirectedSounds();
        if (broadcast is not { } active) return;
        if (active.Expired(Environment.TickCount64)) { StopBroadcast(); return; }
        foreach (var (slot, delivery) in active.Recipients)
        {
            var player = Core.PlayerManager.GetPlayer(slot);
            if (player is null || !SoundAudience.SameConnection(slot, delivery.SteamId, Listener(player)) || !SoundAudience.Eligible(Listener(player, active.SoundId), active.DeadOnly, active.AdminOverride))
                RemoveBroadcastListener(slot);
        }
    }
}
