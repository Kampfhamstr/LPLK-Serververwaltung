using LPLK.Common;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private static readonly WeaponEntry[] SupergodEquipment =
    [
        new("weapon_taser", "Zeus x27", "Ausrüstung"),
        new("weapon_flashbang", "Blendgranate", "Ausrüstung"),
        new("weapon_hegrenade", "HE-Granate", "Ausrüstung"),
        new("weapon_smokegrenade", "Rauchgranate", "Ausrüstung"),
        new("weapon_molotov", "Molotow", "Ausrüstung"),
        new("weapon_incgrenade", "Brandgranate", "Ausrüstung"),
        new("weapon_decoy", "Ködergranate", "Ausrüstung"),
    ];
    private bool CanTakeSupergodWeapon(IPlayer player) => ActiveSupergodPawn(player) is not null;

    [Command("superguns")]
    public void SupergodWeaponsCommand(ICommandContext context)
    {
        if (context.Sender is not { } player || !CanTakeSupergodWeapon(player))
        {
            context.ReplyLplk("[LPLK] Zuerst Supergodmodus im Spiel einschalten.");
            return;
        }
        if (context.Args.Length != 0)
        {
            context.ReplyLplk("[LPLK] Verwendung: !superguns");
            return;
        }
        Guard(() => SupergodWeapons(player), context.ReplyLplk);
    }

    private void SupergodWeapons(IPlayer player) => Menu(player, "Waffen holen", _ =>
        WeaponCatalog.Groups.Append("Ausrüstung").Select(group =>
            (group, (Action<IPlayer>)(admin => SupergodWeaponGroup(admin, group)))),
        SupergodMenu, CanTakeSupergodWeapon);

    private void SupergodWeaponGroup(IPlayer player, string group) => Menu(player, group, _ =>
        WeaponCatalog.All.Concat(SupergodEquipment).Where(weapon => weapon.Group == group)
            .Select(weapon => (weapon.Name, (Action<IPlayer>)(admin => GiveSupergodWeapon(admin, weapon)))),
        SupergodWeapons, CanTakeSupergodWeapon);

    private void GiveSupergodWeapon(IPlayer player, WeaponEntry entry)
    {
        var pawn = ActiveSupergodPawn(player);
        if (pawn?.WeaponServices is not { } weapons || pawn.ItemServices is not { } items)
            throw new InvalidOperationException("Waffenausgabe gerade nicht möglich.");

        var oldWeapons = weapons.MyWeapons.Select(handle => handle.Value)
            .OfType<CBasePlayerWeapon>().Where(weapon => weapon.IsValid).ToArray();
        if (oldWeapons.FirstOrDefault(w => SupergodWeaponId(w) == entry.Id) is { } existing)
        {
            RefillSupergodMagazine(existing);
            weapons.SelectWeapon(existing);
            player.SendLplkChat("[LPLK] Bereits ausgerüstet: " + entry.Name);
            return;
        }

        // GiveNamedItem does not use the client purchase command or debit cash.
        var given = items.GiveItem<CBasePlayerWeapon>(entry.Id);
        if (given is not { IsValid: true })
            throw new InvalidOperationException("Das Spiel hat die Ausgabe abgelehnt.");
        uint givenHandle = Core.EntitySystem.GetRefEHandle(given).Raw;
        if (!weapons.MyWeapons.Any(handle => handle.Raw == givenHandle))
            throw new InvalidOperationException("Das Spiel hat die Ausgabe abgelehnt.");

        if (WeaponCatalog.Find(entry.Id) is not null)
        {
            foreach (var old in oldWeapons)
            {
                if (!old.IsValid || WeaponCatalog.Find(SupergodWeaponId(old))?.Secondary != entry.Secondary) continue;
                var handle = Core.EntitySystem.GetRefEHandle(old).Raw;
                if (weapons.MyWeapons.Any(owned => owned.Raw == handle)) weapons.RemoveWeapon(old);
            }
            RefillSupergodMagazine(given);
        }
        weapons.SelectWeapon(given);
        NotifyAdminAction(player, "Supergodmodus: Waffe erhalten", entry.Name);
        player.SendLplkChat("[LPLK] Erhalten: " + entry.Name);
    }
}
