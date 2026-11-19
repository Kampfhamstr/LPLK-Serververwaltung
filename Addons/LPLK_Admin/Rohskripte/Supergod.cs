using LPLK.Common;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private const float SupergodSpeed = 3.0f;
    private sealed record SupergodLife(ulong SteamId, string Session, uint Pawn, int Team, float PreviousSpeed);
    private readonly Dictionary<int, SupergodLife> supergod = [];
    private readonly List<Guid> supergodEvents = [];
    private readonly Dictionary<int, SupergodRespawn> supergodRespawns = [];
    private sealed record SupergodRespawn(ulong SteamId, ulong Session, int Team, long ExpiresAt, bool SpawnObserved = false);
    private Func<IPlayer, int> supergodTeam = player => player.Controller is { IsValid: true } controller ? controller.TeamNum : 0;

    private bool CanUseSupergod(IPlayer player) => Can(player, "noclip") || Has(player, "admin.root");

    private CCSPlayerPawn? LiveAdminPawn(IPlayer player) =>
        CanUseSupergod(player) && player.IsAlive &&
        supergodTeam(player) is 2 or 3 &&
        player.PlayerPawn is { IsValid: true, Health: > 0 } pawn ? pawn : null;

    private bool SameSupergodPawn(IPlayer player, CCSPlayerPawn pawn, SupergodLife life) =>
        player.SteamID == life.SteamId && player.SessionId.ToString() == life.Session &&
        Core.EntitySystem.GetRefEHandle(pawn).Raw == life.Pawn;

    private CCSPlayerPawn? ActiveSupergodPawn(IPlayer player)
    {
        if (!supergod.TryGetValue(player.PlayerID, out var life) || LiveAdminPawn(player) is not { } pawn)
            return null;
        return SameSupergodPawn(player, pawn, life) && supergodTeam(player) == life.Team ? pawn : null;
    }

    private static void SetSupergodSpeed(CCSPlayerPawn pawn, float speed)
    {
        if (pawn.VelocityModifier == speed) return;
        pawn.VelocityModifier = speed;
        pawn.VelocityModifierUpdated();
    }

    private void SetSupergod(IPlayer player, bool enable)
    {
        if (!CanUseSupergod(player)) throw new InvalidOperationException("Keine Berechtigung für den Supergodmodus.");
        if (!enable)
        {
            if (PendingSupergodRespawn(player) is not null)
            {
                supergodRespawns.Remove(player.PlayerID);
                player.SendLplkChat("[LPLK] Supergodmodus bleibt AUS.");
                return;
            }
            if (ActiveSupergodPawn(player) is null) return;
            StopSupergod(player.PlayerID);
            AnnounceSupergod(player, false);
            return;
        }

        if (ActiveSupergodPawn(player) is not null || PendingSupergodRespawn(player) is not null) return;
        int team = supergodTeam(player);
        if (team is not (2 or 3)) throw new InvalidOperationException("Du musst in T oder CT sein.");
        if (!player.IsAlive)
        {
            StopSupergod(player.PlayerID);
            supergodRespawns[player.PlayerID] = new(player.SteamID, player.SessionId, team, Environment.TickCount64 + 3000);
            try
            {
                player.Respawn();
            }
            catch
            {
                supergodRespawns.Remove(player.PlayerID);
                throw;
            }
            return;
        }

        var pawn = LiveAdminPawn(player) ?? throw new InvalidOperationException("Dein Spieler ist noch nicht bereit.");
        ActivateSupergod(player, pawn, false);
    }

    private void ActivateSupergod(IPlayer player, CCSPlayerPawn pawn, bool respawned)
    {
        StopSupergod(player.PlayerID);
        var life = new SupergodLife(player.SteamID, player.SessionId.ToString(),
            Core.EntitySystem.GetRefEHandle(pawn).Raw, supergodTeam(player), pawn.VelocityModifier);
        supergod.Add(player.PlayerID, life);
        try
        {
            SetSupergodSpeed(pawn, SupergodSpeed);
            if (pawn.WeaponServices?.ActiveWeapon.Value is { IsValid: true } weapon)
                RefillSupergodMagazine(weapon);
        }
        catch
        {
            StopSupergod(player.PlayerID);
            throw;
        }
        AnnounceSupergod(player, true, respawned);
    }

    private void AnnounceSupergod(IPlayer player, bool enable, bool respawned = false)
    {
        Core.Logger.LogInformation("Supergodmodus {State} für Admin {SteamId}", enable ? "AN" : "AUS", player.SteamID);
        NotifyAdminAction(player, respawned ? "Respawn · Supergodmodus AN" : enable ? "Supergodmodus AN" : "Supergodmodus AUS");
        player.SendLplkChat(enable
            ? "[LPLK] Supergodmodus AN · Unverwundbar, volle Magazine, Tempo 3×. Waffen: !superguns"
            : "[LPLK] Supergodmodus AUS.");
    }

    private void StopSupergod(int slot)
    {
        if (!supergod.Remove(slot, out var life)) return;
        if (Core.PlayerManager.GetPlayer(slot) is { IsValid: true } player &&
            player.PlayerPawn is { IsValid: true } pawn && SameSupergodPawn(player, pawn, life) &&
            pawn.VelocityModifier == SupergodSpeed)
            SetSupergodSpeed(pawn, life.PreviousSpeed);
    }

    private void StopAllSupergod()
    {
        supergodRespawns.Clear();
        foreach (int slot in supergod.Keys.ToArray()) StopSupergod(slot);
    }

    private bool MatchesRespawn(IPlayer player, SupergodRespawn request) =>
        CanUseSupergod(player) && player.SteamID == request.SteamId &&
        player.SessionId == request.Session && supergodTeam(player) == request.Team;

    private SupergodRespawn? PendingSupergodRespawn(IPlayer player)
    {
        if (!supergodRespawns.TryGetValue(player.PlayerID, out var request) || !MatchesRespawn(player, request))
            return null;
        return Environment.TickCount64 < request.ExpiresAt ? request : null;
    }

    private HookResult OnSupergodSpawn(EventPlayerSpawn e)
    {
        SupergodPlayerSpawned(e.UserIdPlayer);
        return HookResult.Continue;
    }

    private void SupergodPlayerSpawned(IPlayer? player)
    {
        if (player is null) return;
        StopSupergod(player.PlayerID);
        if (PendingSupergodRespawn(player) is { } request)
            supergodRespawns[player.PlayerID] = request with { SpawnObserved = true };
    }

    private void FinishSupergodRespawns()
    {
        foreach (var (slot, request) in supergodRespawns.ToArray())
        {
            var player = Core.PlayerManager.GetPlayer(slot);
            if (player is null || !MatchesRespawn(player, request))
            {
                supergodRespawns.Remove(slot);
                continue;
            }
            if (Environment.TickCount64 >= request.ExpiresAt)
            {
                supergodRespawns.Remove(slot);
                player.SendLplkChat("[LPLK] Respawn konnte nicht bestätigt werden. Supergodmodus bleibt AUS.");
                continue;
            }
            if (!request.SpawnObserved || LiveAdminPawn(player) is not { } pawn) continue;
            supergodRespawns.Remove(slot);
            Guard(() => ActivateSupergod(player, pawn, true), player.SendLplkChat);
        }
    }

    private void LoadSupergod()
    {
        if (Core.GameHooks?.Entities?.TakeDamage is { } damage) damage.Pre += ProtectSupergod;
        supergodEvents.Add(Core.GameEvent.HookPost<EventWeaponFire>(OnSupergodFire));
        supergodEvents.Add(Core.GameEvent.HookPost<EventItemEquip>(OnSupergodEquip));
        supergodEvents.Add(Core.GameEvent.HookPost<EventPlayerDeath>(e => EndSupergodLife(e.UserIdPlayer)));
        supergodEvents.Add(Core.GameEvent.HookPost<EventPlayerSpawn>(OnSupergodSpawn));
        supergodEvents.Add(Core.GameEvent.HookPost<EventPlayerTeam>(e => EndSupergodLife(e.UserIdPlayer)));
        supergodEvents.Add(Core.GameEvent.HookPost<EventRoundEnd>(_ =>
        {
            StopAllSupergod();
            return HookResult.Continue;
        }));
    }

    private void UnloadSupergod()
    {
        if (Core.GameHooks?.Entities?.TakeDamage is { } damage) damage.Pre -= ProtectSupergod;
        foreach (Guid handle in supergodEvents)
            if (handle != Guid.Empty) Core.GameEvent.Unhook(handle);
        supergodEvents.Clear();
        StopAllSupergod();
    }

    private HookResult EndSupergodLife(IPlayer? player)
    {
        if (player is not null) CancelSupergodLife(player.PlayerID);
        return HookResult.Continue;
    }

    private void CancelSupergodLife(int slot)
    {
        supergodRespawns.Remove(slot);
        StopSupergod(slot);
    }

    [EventListener<EventDelegates.OnClientDisconnected>]
    public void SupergodDisconnect(IOnClientDisconnectedEvent e) => CancelSupergodLife(e.PlayerId);

    [EventListener<EventDelegates.OnMapUnload>]
    public void SupergodMapUnload(IOnMapUnloadEvent e) => StopAllSupergod();

    [EventListener<EventDelegates.OnTick>]
    public void SupergodTick()
    {
        if (!active) return;
        if (supergodRespawns.Count != 0) FinishSupergodRespawns();
        if (supergod.Count == 0) return;
        foreach (int slot in supergod.Keys.ToArray())
        {
            var player = Core.PlayerManager.GetPlayer(slot);
            if (player is null || ActiveSupergodPawn(player) is not { } pawn)
            {
                StopSupergod(slot);
                continue;
            }
            if (pawn.VelocityModifier == SupergodSpeed) continue;
            // If another server action sets a speed, keep that value for deactivation.
            supergod[slot] = supergod[slot] with { PreviousSpeed = pawn.VelocityModifier };
            SetSupergodSpeed(pawn, SupergodSpeed);
        }
    }

    private void ProtectSupergod(ref TakeDamageEntityPreContext context)
    {
        if (!active || supergod.Count == 0 ||
            context.Params.Entity is not { IsValid: true, DesignerName: "player" } target) return;
        var player = Core.PlayerManager.GetPlayerFromPawn(target.As<CBasePlayerPawn>());
        if (player is not null && ActiveSupergodPawn(player) is { } pawn && pawn.Address == target.Address)
            context.SetHookResult(HookResult.Stop);
    }

    private string SupergodWeaponId(CBasePlayerWeapon weapon) =>
        Core.Helpers.GetClassnameByDefinitionIndex(weapon.AttributeManager.Item.ItemDefinitionIndex) ?? weapon.DesignerName;

    private void RefillSupergodMagazine(CBasePlayerWeapon weapon)
    {
        if (WeaponCatalog.Find(SupergodWeaponId(weapon)) is null || weapon.Clip1 < 0) return;
        var data = weapon.As<CCSWeaponBase>().WeaponBaseVData;
        if (!data.IsValid || data.MaxClip1 <= weapon.Clip1) return;
        // Definition limits are shared; update this weapon's live clip only.
        weapon.Clip1 = data.MaxClip1;
        weapon.Clip1Updated();
    }

    private HookResult OnSupergodFire(EventWeaponFire e)
    {
        if (!active || supergod.Count == 0 || e.UserIdPlayer is not { } player ||
            ActiveSupergodPawn(player)?.WeaponServices?.ActiveWeapon.Value is not { IsValid: true } weapon)
            return HookResult.Continue;
        if (e.Weapon is not { Length: > 0 } fired) return HookResult.Continue;
        string id = fired.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? fired : "weapon_" + fired;
        if (id.Equals(SupergodWeaponId(weapon), StringComparison.OrdinalIgnoreCase)) RefillSupergodMagazine(weapon);
        return HookResult.Continue;
    }

    private HookResult OnSupergodEquip(EventItemEquip e)
    {
        if (active && supergod.Count != 0 && e.UserIdPlayer is { } player &&
            ActiveSupergodPawn(player)?.WeaponServices?.ActiveWeapon.Value is { IsValid: true } weapon)
            RefillSupergodMagazine(weapon);
        return HookResult.Continue;
    }

    [Command("supergod")]
    [CommandAlias("sgm")]
    public void SupergodCommand(ICommandContext context)
    {
        if (context.Sender is not { } player)
        {
            context.ReplyLplk("[LPLK] Supergodmodus nur im Spiel.");
            return;
        }
        if (context.Args.Length != 0)
        {
            context.ReplyLplk("[LPLK] Verwendung: !sgm");
            return;
        }
        Guard(() => SetSupergod(player, ActiveSupergodPawn(player) is null && PendingSupergodRespawn(player) is null), context.ReplyLplk);
    }

    private void SupergodMenu(IPlayer player) => Menu(player, "Supergodmodus", admin =>
    {
        bool enabled = ActiveSupergodPawn(admin) is not null;
        bool respawning = PendingSupergodRespawn(admin) is not null;
        string label = enabled ? "Supergodmodus: AN" : respawning ? "Start abbrechen" : "Supergodmodus: AUS";
        var entries = new List<(string, Action<IPlayer>)>
        {
            (label, a =>
            {
                SetSupergod(a, !enabled && !respawning);
                SupergodMenu(a);
            })
        };
        if (enabled) entries.Add(("Waffen holen", SupergodWeapons));
        return entries;
    }, PlayerManagement, CanUseSupergod, colorSwitches: true);
}
