using System.Text.Json;
using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;

namespace LPLK_Maps;

public sealed partial record MapSettings
{
    public bool LunacyLowGravityVote { get; init; } = true;
}

public sealed record LunacyGravitySaved(int Schema, NominationSession Session, float Original);

public sealed partial class Plugin
{
    private readonly LunacyVote lunacyVote = new();
    private readonly Dictionary<int, (IPlayer Player, IMenuAPI Menu, ulong Session)> lunacyMenus = [];
    private bool lunacyRoundEnded, lunacyDirectVote, lunacyApplyOnLoad, lunacyRuntimeReady;
    private float? lunacyOriginalGravity, lunacyOriginalRestartDelay;
    private NominationSession? lunacySession;
    private string? lunacyError;
    private string LunacyGravityPath => Path.Combine(RoundFileStore.DataPath(Core), "lunacy-gravity.json");

    private bool BeginLunacyVote(MapEntry winner, bool direct = false)
    {
        if (!settings.LunacyLowGravityVote || winner.Id != LunacyVote.MapId) return false;
        var players = Voters();
        if (!lunacyVote.Start(winner.Id, players.Select(p => p.SteamID), Environment.TickCount64)) return false;
        lunacyDirectVote = direct; lunacyRoundEnded = false; lunacyApplyOnLoad = false;
        // Keep CS2's own match-end timer from overtaking this follow-up vote.
        if (Core.ConVar.Find<float>("mp_match_restart_delay") is {} delay && delay.Value < LunacyVote.Seconds + 10)
        { lunacyOriginalRestartDelay = delay.Value; delay.Value = LunacyVote.Seconds + 10; }
        Core.PlayerManager.SendLplkChat("[LPLK] Lunacy hat gewonnen! Mit Low Gravity spielen? 30 Sekunden für Ja/Nein; Mehrheit der Stimmen entscheidet. Bei Gleichstand bleibt normale Schwerkraft. Menü: !mapchooser");
        foreach (var player in players) OpenLunacyVote(player);
        Audit("lunacy_vote_started", new { mapId = winner.Id, seconds = LunacyVote.Seconds });
        return true;
    }
    private void OpenLunacyVote(IPlayer player)
    {
        if (!CanVote(player) || !lunacyVote.MayVote(player.SteamID)) return;
        CloseLunacyMenu(player.PlayerID);
        var serial = lunacyVote.Serial; var session = player.SessionId;
        var builder = Core.MenusAPI.CreateBuilder().SetPlayerFrozen(false).Design
            .SetMenuTitle(MenuColors.Heading("Lunacy mit Low Gravity spielen?"))
            .Design.SetMaxVisibleItems(5);
        var numbers = new MenuNumbers();
        foreach (bool yes in new[] { true, false })
        {
            var option = new ButtonMenuOption(numbers.Next(yes ? "Ja" : "Nein")) { CloseAfterClick = false };
            GameThreadMenu.Bind(Core, option, player, () => module.Active, actor =>
            {
                if (actor.SessionId != session || !CanVote(actor) || nextMap?.Id != LunacyVote.MapId ||
                    !lunacyVote.Cast(actor.SteamID, yes, Environment.TickCount64, serial)) return;
                CloseLunacyMenu(actor.PlayerID);
                actor.SendLplkChat(yes ? "[LPLK] Ja-Stimme gespeichert." : "[LPLK] Nein-Stimme gespeichert.");
            });
            builder.AddOption(option);
        }
        var menu = builder.Build(); MenuPresentation.Apply(menu);
        lunacyMenus[player.PlayerID] = (player, menu, session);
        Core.MenusAPI.OpenMenuForPlayer(player, menu);
    }
    private void CloseLunacyMenu(int id)
    {
        if (lunacyMenus.Remove(id, out var shown) && shown.Player.IsValid && shown.Player.SessionId == shown.Session)
            Core.MenusAPI.CloseMenuForPlayer(shown.Player, shown.Menu);
    }
    private void CloseLunacyMenus() { foreach (var id in lunacyMenus.Keys.ToArray()) CloseLunacyMenu(id); }
    private void RestoreLunacyRestartDelay()
    {
        if (lunacyOriginalRestartDelay is {} original && Core.ConVar.Find<float>("mp_match_restart_delay") is {} delay && delay.Value == LunacyVote.Seconds + 10)
            delay.Value = original;
        lunacyOriginalRestartDelay = null;
    }
    private void CancelLunacyVote()
    {
        CloseLunacyMenus(); RestoreLunacyRestartDelay(); lunacyVote.Reset();
        lunacyApplyOnLoad = false; lunacyRoundEnded = false;
    }
    private void TickLunacy(long now)
    {
        InitializeLunacyGravity();
        if (!lunacyVote.Active) return;
        if (nextMap?.Id != LunacyVote.MapId) { CancelLunacyVote(); return; }
        if (!lunacyVote.Finish(now, Voters().Select(p => p.SteamID))) return;
        CloseLunacyMenus(); RestoreLunacyRestartDelay();
        lunacyApplyOnLoad = lunacyVote.Accepted;
        Core.PlayerManager.SendLplkChat($"[LPLK] Lunacy: {lunacyVote.Yes} Ja / {lunacyVote.No} Nein. " +
            (lunacyApplyOnLoad ? "Low Gravity wird beim Mapstart aktiviert." : "Die Map startet mit normaler Schwerkraft."));
        Audit("lunacy_vote_result", new { yes = lunacyVote.Yes, no = lunacyVote.No, lowGravity = lunacyApplyOnLoad });
        CompleteSelectedVote();
    }
    private void CompleteSelectedVote()
    {
        if (nextMap is not {} selected || lunacyVote.Active) return;
        bool roundEnd = lunacyDirectVote ? settings.DirectVotemapAtRoundEnd : reason == Reason.Rtv ? settings.RtvAtRoundEnd : reason == Reason.Admin;
        bool immediate = lunacyDirectVote || reason is Reason.Rtv or Reason.Admin;
        if (matchEnded) Schedule(selected, settings.MatchEndDelaySeconds, adminSource: selected.Id == adminNextMapId ? "admin_vote_result" : null);
        else if (immediate && roundEnd && !lunacyRoundEnded)
        { changeAtRoundEnd = true; Core.PlayerManager.SendLplkChat("[LPLK] Die gewählte Map startet am Rundenende."); }
        else if (immediate) Schedule(selected, settings.ChangeDelaySeconds, adminSource: selected.Id == adminNextMapId ? "admin_vote_result" : null);
        else Core.PlayerManager.SendLplkChat("[LPLK] Die gewählte Map startet nach dem Match.");
        lunacyDirectVote = lunacyRoundEnded = false;
    }
    private void InitializeLunacyGravity()
    {
        if (lunacyRuntimeReady || nominationMapUnloading) return;
        var session = CurrentNominationSession(); if (session is null) return;
        lunacySession = session; lunacyRuntimeReady = true;
        try
        {
            if (!RamFile.Exists(LunacyGravityPath)) return;
            var saved = JsonSerializer.Deserialize<LunacyGravitySaved>(RamFile.ReadAllText(LunacyGravityPath));
            if (saved is { Schema: 1 } && saved.Session == session && session.Workshop == LunacyVote.MapId &&
                float.IsFinite(saved.Original) && saved.Original > 0 && Core.ConVar.Find<float>("sv_gravity") is {} gravity &&
                (gravity.Value == saved.Original || gravity.Value == LunacyVote.Gravity))
                ApplyLunacyGravity(saved.Original);
        }
        catch (Exception e) { lunacyError = e.GetType().Name; Core.Logger.LogWarning(e, "Lunacy-Schwerkraft konnte nicht wiederhergestellt werden."); }
    }
    private void ApplyLunacyGravity(float? restoredOriginal = null)
    {
        if (Workshop != LunacyVote.MapId || Core.ConVar.Find<float>("sv_gravity") is not {} gravity) return;
        float original = restoredOriginal ?? gravity.Value;
        if (!float.IsFinite(original) || original <= 0) return;
        gravity.Value = LunacyVote.Gravity;
        if (gravity.Value != LunacyVote.Gravity) { lunacyError = "sv_gravity nicht bestätigt"; return; }
        lunacyOriginalGravity = original; lunacyError = null;
        SaveLunacyGravity();
    }
    private void SaveLunacyGravity()
    {
        if (lunacyOriginalGravity is {} original && lunacySession is {} session)
            DeferredJson.Queue(LunacyGravityPath, new LunacyGravitySaved(1, session, original));
    }
    private void RestoreLunacyGravity()
    {
        if (lunacyOriginalGravity is {} original && Core.ConVar.Find<float>("sv_gravity") is {} gravity && gravity.Value == LunacyVote.Gravity)
            gravity.Value = original;
        lunacyOriginalGravity = null;
    }
    private void LunacyMapLoaded()
    {
        bool apply = lunacyApplyOnLoad; CancelLunacyVote();
        lunacyRuntimeReady = false;
        if (apply)
        {
            int serial = generation;
            Core.Scheduler.DelayBySeconds(1, () =>
            {
                if (!module.Active || serial != generation || Workshop != LunacyVote.MapId) return;
                lunacySession = CurrentNominationSession();
                ApplyLunacyGravity();
                if (lunacyOriginalGravity is not null) Core.PlayerManager.SendLplkChat("[LPLK] Abstimmung angenommen: Lunacy läuft mit Low Gravity (400).");
            });
        }
    }
    private object LunacyStatus() => new { active = lunacyVote.Active, accepted = lunacyApplyOnLoad, yes = lunacyVote.Yes, no = lunacyVote.No,
        gravity = Core.ConVar.Find<float>("sv_gravity")?.Value, applied = lunacyOriginalGravity is not null, error = lunacyError };

    // One-shot native lifecycle test, using the same gate pattern as the existing
    // nomination/bot reload checks. No client or production command can seed it.
    private void LunacyProbe(SwiftlyS2.Shared.Commands.ICommandContext ctx, string step)
    {
        if (ctx.IsSentByPlayer || ctx.Sender is not null || AnyHuman() || voteEnds>0 || pending is not null || nextMap is not null)
            throw new InvalidOperationException("Leerer Testserver erforderlich.");
        string path = Path.Combine(RoundFileStore.DataPath(Core), "lunacy-test.json");
        if (!RamFile.Exists(path) || new RamFileInfo(path).Length>2048) throw new InvalidOperationException("Testfreigabe fehlt.");
        using var auth=JsonDocument.Parse(RamFile.ReadAllText(path));
        var expires=auth.RootElement.GetProperty("expires").GetDateTimeOffset();
        if(auth.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b" ||
            expires<DateTimeOffset.UtcNow || expires>DateTimeOffset.UtcNow.AddMinutes(15)) throw new InvalidOperationException("Testfreigabe ungültig.");
        if(step=="seed")
        {
            if(Workshop==LunacyVote.MapId || lunacyApplyOnLoad || lunacyOriginalGravity is not null) throw new InvalidOperationException("Testbasis ungültig.");
            if(!lunacyVote.Start(LunacyVote.MapId,[1,2],Environment.TickCount64)) throw new InvalidOperationException("Testabstimmung läuft bereits.");
            lunacyVote.Cast(1,true,Environment.TickCount64,lunacyVote.Serial);lunacyVote.Cast(2,true,Environment.TickCount64,lunacyVote.Serial);
            lunacyVote.Finish(Environment.TickCount64,[1,2],true);lunacyApplyOnLoad=lunacyVote.Accepted;
        }
        else if(step=="clear")
        {
            if(Workshop==LunacyVote.MapId || lunacyOriginalGravity is not null)throw new InvalidOperationException("Testmap noch aktiv.");
            CancelLunacyVote();RamFile.Delete(path);
        }
        else throw new InvalidOperationException("Unbekannter Testschritt.");
        ctx.ReplyLplk("LPLK_LUNACY "+JsonSerializer.Serialize(LunacyStatus()));
    }
}
