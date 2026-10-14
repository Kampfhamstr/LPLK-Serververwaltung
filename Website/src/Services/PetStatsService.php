<?php
namespace App\Services;

use InvalidArgumentException;
use PDO;

/**
 * Eine zentrale, ausschließlich serverseitige Berechnung für Abenteuer und
 * Arena. Die Tierart wird absichtlich nicht ausgewertet: Alle 24 Arten bleiben
 * spielerisch gleichwertig.
 */
final class PetStatsService
{
    private const SKILL_MAX_RANKS = [
        'sharp_claws' => 3,
        'eagle_eye' => 3,
        'finisher' => 1,
        'thick_fur' => 3,
        'stone_skin' => 3,
        'second_wind' => 1,
        'light_paws' => 3,
        'feint' => 3,
        'treasure_nose' => 3,
    ];

    /**
     * @return array{
     *   strength:int,
     *   agility:int,
     *   vitality:int,
     *   instinct:int,
     *   total:int,
     *   power:int,
     *   combat_hp:int,
     *   breakdown:array<string,array{base:int,gear:int,skills:int,total:int}>,
     *   skill_effects:array<string,int>,
     *   ranks:array<string,int>
     * }
     */
    public static function stats(PDO $pdo, array $pet): array
    {
        $petId = (int)($pet['id'] ?? 0);
        $userId = (int)($pet['user_id'] ?? 0);
        if ($petId <= 0 || $userId <= 0) {
            throw new InvalidArgumentException('Für die Pet-Werte werden Pet-ID und Besitzer benötigt.');
        }

        $level = max(1, min(20, (int)($pet['level'] ?? 1)));
        // Gleichmäßige, bewusst moderate Grundskalierung: +1 auf alle vier
        // Attribute nach jeweils drei vollständigen Leveln.
        $baseValue = 10 + intdiv($level - 1, 3);

        $gearStatement = $pdo->prepare(
            'SELECT COALESCE(SUM(strength_bonus),0) AS strength,
                    COALESCE(SUM(agility_bonus),0) AS agility,
                    COALESCE(SUM(vitality_bonus),0) AS vitality,
                    COALESCE(SUM(instinct_bonus),0) AS instinct
             FROM pet_items
             WHERE pet_id=? AND user_id=? AND equipped=1'
        );
        $gearStatement->execute([$petId, $userId]);
        $gearRow = $gearStatement->fetch(PDO::FETCH_ASSOC) ?: [];
        $gear = [
            'strength' => max(0, (int)($gearRow['strength'] ?? 0)),
            'agility' => max(0, (int)($gearRow['agility'] ?? 0)),
            'vitality' => max(0, (int)($gearRow['vitality'] ?? 0)),
            'instinct' => max(0, (int)($gearRow['instinct'] ?? 0)),
        ];

        $ranks = array_fill_keys(array_keys(self::SKILL_MAX_RANKS), 0);
        $rankStatement = $pdo->prepare('SELECT skill_key,rank FROM pet_skill_ranks WHERE pet_id=? AND user_id=?');
        $rankStatement->execute([$petId, $userId]);
        foreach ($rankStatement->fetchAll(PDO::FETCH_ASSOC) as $row) {
            $key = (string)($row['skill_key'] ?? '');
            if (!array_key_exists($key, self::SKILL_MAX_RANKS)) continue;
            $ranks[$key] = max(0, min(self::SKILL_MAX_RANKS[$key], (int)($row['rank'] ?? 0)));
        }

        $skillStats = [
            'strength' => $ranks['sharp_claws'] * 2,
            'agility' => $ranks['light_paws'] * 2,
            'vitality' => $ranks['thick_fur'] * 2,
            'instinct' => $ranks['eagle_eye'] * 2,
        ];

        $breakdown = [];
        foreach (['strength','agility','vitality','instinct'] as $stat) {
            $breakdown[$stat] = [
                'base' => $baseValue,
                'gear' => $gear[$stat],
                'skills' => $skillStats[$stat],
                'total' => $baseValue + $gear[$stat] + $skillStats[$stat],
            ];
        }

        $strength = $breakdown['strength']['total'];
        $agility = $breakdown['agility']['total'];
        $vitality = $breakdown['vitality']['total'];
        $instinct = $breakdown['instinct']['total'];
        $skillEffects = [
            'armor_bonus' => $ranks['stone_skin'] * 3,
            'crit_percent' => $ranks['eagle_eye'] * 2,
            'finisher_percent' => $ranks['finisher'] > 0 ? 15 : 0,
            'second_wind_percent' => $ranks['second_wind'] > 0 ? 15 : 0,
            'dodge_percent' => $ranks['feint'] * 2,
            'loot_bonus_percent' => $ranks['treasure_nose'] * 5,
        ];

        $combatHp = 80 + ($vitality * 8);
        $power = (int)round(
            ($strength * 2.0)
            + ($agility * 1.5)
            + ($vitality * 1.8)
            + ($instinct * 1.4)
            + ($skillEffects['armor_bonus'] * 1.5)
            + $skillEffects['crit_percent']
            + $skillEffects['dodge_percent']
        );

        return [
            'strength' => $strength,
            'agility' => $agility,
            'vitality' => $vitality,
            'instinct' => $instinct,
            'total' => $strength + $agility + $vitality + $instinct,
            'power' => max(1, $power),
            'combat_hp' => $combatHp,
            'breakdown' => $breakdown,
            'skill_effects' => $skillEffects,
            'ranks' => $ranks,
        ];
    }
}
