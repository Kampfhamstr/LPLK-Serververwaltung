<?php
namespace App\Services;

use App\Core\Audit;
use App\Core\Database;

final class GlobalBanService
{
    public const REASONS = [
        'advertising' => 'Werbung / Fremdwerbung / Spam',
        'cheating' => 'Cheating / Hacking / unerlaubte Hilfsmittel',
        'insult' => 'Beleidigung / Flaming / toxisches Verhalten',
        'griefing' => 'Spielbehinderung / Griefing',
        'server_rules' => 'Missachtung der Serverregeln',
        'admin_instruction' => 'Missachtung von Admin-Anweisungen',
        'hate_speech' => 'Rassismus / Hassrede / diskriminierende Inhalte',
        'exploit' => 'Exploit- / Bug-Abuse',
        'team_abuse' => 'Absichtliches Teamkilling / Teamflash / Teamschaden',
        'chat_spam' => 'Chat- / Voice-Spam',
        'name_abuse' => 'Unzulässiger Name / Clan-Tag / Profilwerbung',
        'ban_evasion' => 'Bannumgehung / Zweitaccount',
        'threats' => 'Drohungen / massive Belästigung',
        'other' => 'Sonstiger Regelverstoß',
    ];

    public const DURATIONS = [
        60 => '1 Stunde',
        360 => '6 Stunden',
        1440 => '1 Tag',
        4320 => '3 Tage',
        10080 => '7 Tage',
        20160 => '14 Tage',
        43200 => '30 Tage',
        129600 => '90 Tage',
        525600 => '1 Jahr',
        0 => 'Permanent',
        -1 => 'Benutzerdefiniert',
    ];

    public static function reasons(): array { return self::REASONS; }
    public static function durations(): array { return self::DURATIONS; }

    public static function create(
        string $steamId64,
        string $playerName,
        string $reasonCode,
        string $reasonDetail,
        int $minutes,
        ?int $createdBy,
        ?int $chatMessageId = null,
        ?int $advertisingEventId = null
    ): int {
        self::assertSteamId($steamId64);
        self::expireDue();
        $reasonCode = array_key_exists($reasonCode, self::REASONS) ? $reasonCode : 'other';
        $reasonLabel = self::REASONS[$reasonCode];
        $playerName = self::cut(trim($playerName), 80);
        if ($playerName === '') $playerName = $steamId64;
        $reasonDetail = self::cut(trim($reasonDetail), 500);
        $minutes = max(0, min(5256000, $minutes));
        $now = date('c');
        $expiresAt = $minutes === 0 ? null : date('c', time() + ($minutes * 60));

        $evidence = null;
        if ($chatMessageId !== null && $chatMessageId > 0) {
            $evidence = self::chatEvidence($chatMessageId, $steamId64);
        }
        $advertisingEvent = null;
        if ($advertisingEventId !== null && $advertisingEventId > 0) {
            $advertisingEvent = AntiAdvertisementService::getEvent($advertisingEventId);
            if (!$advertisingEvent) throw new \InvalidArgumentException('Werbeereignis wurde nicht gefunden.');
            if (trim((string)($advertisingEvent['steam_id64']??'')) !== $steamId64) {
                throw new \InvalidArgumentException('Werbeereignis und SteamID64 passen nicht zusammen.');
            }
            if (!$evidence) {
                $evidence = [
                    'id' => $advertisingEvent['chat_message_id']??null,
                    'server_id' => $advertisingEvent['server_id']??null,
                    'player_name' => $advertisingEvent['player_name']??$playerName,
                    'message' => $advertisingEvent['message_excerpt']??null,
                    'sent_at' => $advertisingEvent['observed_at']??$now,
                ];
            }
        }

        $pdo = Database::connection();
        $pdo->beginTransaction();
        try {
            // Ein alter oder parallel geöffneter Moderationsdialog darf keinen
            // zweiten aktiven Ban derselben SteamID erzeugen oder den vorhandenen
            // Ban stillschweigend durch einen neuen Datensatz ersetzen.
            $existing=$pdo->prepare('SELECT id FROM global_bans WHERE steam_id64=? AND active=1 ORDER BY id DESC LIMIT 1');
            $existing->execute([$steamId64]);
            $existingId=(int)$existing->fetchColumn();
            if($existingId>0)throw new \RuntimeException('Diese SteamID ist bereits global gebannt (Ban #'.$existingId.').');

            $stmt = $pdo->prepare(
                'INSERT INTO global_bans(' .
                'steam_id64,player_name,reason_code,reason_label,reason_detail,starts_at,expires_at,permanent,active,' .
                'created_by,created_at,updated_at,evidence_chat_message_id,evidence_server_id,evidence_player_name,evidence_message,evidence_sent_at,source' .
                ') VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)'
            );
            $stmt->execute([
                $steamId64,$playerName,$reasonCode,$reasonLabel,$reasonDetail,$now,$expiresAt,$minutes===0?1:0,1,
                $createdBy,$now,$now,
                $evidence['id']??null,$evidence['server_id']??null,$evidence['player_name']??null,$evidence['message']??null,$evidence['sent_at']??null,
                $advertisingEvent ? 'anti_advertisement' : ($evidence ? 'server_chat' : 'manual')
            ]);
            $id = (int)$pdo->lastInsertId();
            if ($advertisingEventId !== null && $advertisingEventId > 0) {
                AntiAdvertisementService::linkBan($advertisingEventId,$id,$createdBy);
            }
            $pdo->commit();

            Audit::log('global_ban.create','global_ban',(string)$id,[
                'steam_id64'=>$steamId64,'player_name'=>$playerName,'reason_code'=>$reasonCode,'reason'=>$reasonLabel,
                'detail'=>$reasonDetail,'minutes'=>$minutes,'expires_at'=>$expiresAt,'chat_message_id'=>$chatMessageId,
                'advertising_event_id'=>$advertisingEventId
            ]);
            return $id;
        } catch (\Throwable $e) {
            if ($pdo->inTransaction()) $pdo->rollBack();
            throw $e;
        }
    }

    public static function update(int $id, string $reasonCode, string $reasonDetail, int $minutes): void
    {
        $row = self::get($id);
        if (!$row) throw new \RuntimeException('Ban nicht gefunden.');
        if (empty($row['active'])) throw new \RuntimeException('Nur aktive Bans können bearbeitet werden.');
        $reasonCode = array_key_exists($reasonCode, self::REASONS) ? $reasonCode : 'other';
        $minutes = max(0, min(5256000, $minutes));
        $now = date('c');
        $expiresAt = $minutes === 0 ? null : date('c', time() + ($minutes * 60));
        Database::connection()->prepare(
            'UPDATE global_bans SET reason_code=?,reason_label=?,reason_detail=?,expires_at=?,permanent=?,updated_at=? WHERE id=?'
        )->execute([$reasonCode,self::REASONS[$reasonCode],self::cut(trim($reasonDetail),500),$expiresAt,$minutes===0?1:0,$now,$id]);
        Audit::log('global_ban.update','global_ban',(string)$id,[
            'steam_id64'=>(string)$row['steam_id64'],'reason_code'=>$reasonCode,'minutes'=>$minutes,'expires_at'=>$expiresAt
        ]);
    }

    public static function unban(int $id, string $reason, ?int $userId): void
    {
        $row = self::get($id);
        if (!$row) throw new \RuntimeException('Ban nicht gefunden.');
        if (empty($row['active'])) return;
        $now = date('c');
        $reason = self::cut(trim($reason), 300);
        if ($reason === '') $reason = 'Global entbannt';
        Database::connection()->prepare(
            'UPDATE global_bans SET active=0,unbanned_by=?,unbanned_at=?,unban_reason=?,updated_at=? WHERE id=?'
        )->execute([$userId,$now,$reason,$now,$id]);
        if ((string)($row['source']??'') === 'anti_advertisement' || (string)($row['reason_code']??'') === 'advertising') {
            AntiAdvertisementService::releaseAfterUnban($id,(string)$row['steam_id64'],$reason,$userId);
        }
        Audit::log('global_ban.unban','global_ban',(string)$id,[
            'steam_id64'=>(string)$row['steam_id64'],'reason'=>$reason
        ]);
    }

    public static function get(int $id): ?array
    {
        $stmt = Database::connection()->prepare(
            "SELECT b.*,CASE WHEN u.steam_verified_at IS NOT NULL AND TRIM(COALESCE(u.steam_personaname,''))<>'' THEN u.steam_personaname ELSE u.name END AS created_by_name,u.name AS created_by_panel_name,u.steam_id64 AS created_by_steam_id64,u.steam_verified_at AS created_by_steam_verified_at,CASE WHEN uu.steam_verified_at IS NOT NULL AND TRIM(COALESCE(uu.steam_personaname,''))<>'' THEN uu.steam_personaname ELSE uu.name END AS unbanned_by_name,uu.name AS unbanned_by_panel_name,uu.steam_id64 AS unbanned_by_steam_id64 FROM global_bans b " .
            'LEFT JOIN users u ON u.id=b.created_by LEFT JOIN users uu ON uu.id=b.unbanned_by WHERE b.id=? LIMIT 1'
        );
        $stmt->execute([$id]);
        $row = $stmt->fetch();
        return $row ?: null;
    }

    public static function search(array $filters = []): array
    {
        self::expireDue();
        $where = ['1=1']; $params = [];
        $status = strtolower(trim((string)($filters['status'] ?? 'active')));
        if ($status === 'active') $where[] = 'b.active=1';
        elseif ($status === 'history') $where[] = 'b.active=0';
        $reason = trim((string)($filters['reason'] ?? ''));
        if ($reason !== '' && isset(self::REASONS[$reason])) { $where[]='b.reason_code=?'; $params[]=$reason; }
        $q = trim((string)($filters['q'] ?? ''));
        if ($q !== '') {
            $where[] = '(b.steam_id64 LIKE ? OR b.player_name LIKE ? COLLATE NOCASE OR b.reason_label LIKE ? COLLATE NOCASE OR b.reason_detail LIKE ? COLLATE NOCASE OR CAST(b.id AS TEXT)=?)';
            $like = '%' . $q . '%'; array_push($params,$like,$like,$like,$like,$q);
        }
        $limit = max(25,min(500,(int)($filters['limit']??200)));
        $stmt = Database::connection()->prepare(
            "SELECT b.*,CASE WHEN u.steam_verified_at IS NOT NULL AND TRIM(COALESCE(u.steam_personaname,''))<>'' THEN u.steam_personaname ELSE u.name END AS created_by_name,u.name AS created_by_panel_name,u.steam_id64 AS created_by_steam_id64,u.steam_verified_at AS created_by_steam_verified_at,CASE WHEN uu.steam_verified_at IS NOT NULL AND TRIM(COALESCE(uu.steam_personaname,''))<>'' THEN uu.steam_personaname ELSE uu.name END AS unbanned_by_name,uu.name AS unbanned_by_panel_name,uu.steam_id64 AS unbanned_by_steam_id64 FROM global_bans b " .
            'LEFT JOIN users u ON u.id=b.created_by LEFT JOIN users uu ON uu.id=b.unbanned_by ' .
            'WHERE '.implode(' AND ',$where).' ORDER BY b.active DESC,b.id DESC LIMIT '.$limit
        );
        $stmt->execute($params);
        return $stmt->fetchAll();
    }

    public static function activeForSteam(string $steamId64): ?array
    {
        if (!preg_match('/^7656119\d{10}$/',$steamId64)) return null;
        self::expireDue();
        $stmt=Database::connection()->prepare(
            "SELECT b.*,CASE WHEN u.steam_verified_at IS NOT NULL AND TRIM(COALESCE(u.steam_personaname,''))<>'' THEN u.steam_personaname ELSE u.name END AS created_by_name,u.name AS created_by_panel_name,u.steam_id64 AS created_by_steam_id64,u.steam_verified_at AS created_by_steam_verified_at FROM global_bans b LEFT JOIN users u ON u.id=b.created_by WHERE b.steam_id64=? AND b.active=1 ORDER BY b.id DESC LIMIT 1"
        );
        $stmt->execute([$steamId64]);
        $row=$stmt->fetch();
        return $row?:null;
    }

    public static function countActive(): int
    {
        self::expireDue();
        return (int)Database::connection()->query('SELECT COUNT(*) FROM global_bans WHERE active=1')->fetchColumn();
    }

    public static function activePayload(): array
    {
        self::expireDue();
        $rows = Database::connection()->query(
            'SELECT id,steam_id64,player_name,reason_code,reason_label,reason_detail,starts_at,expires_at,permanent FROM global_bans WHERE active=1 ORDER BY id'
        )->fetchAll();
        return array_map(static function(array $row): array {
            $detail = trim((string)($row['reason_detail']??''));
            $reason = (string)$row['reason_label'];
            if ($detail !== '') $reason .= ': ' . $detail;
            return [
                'id'=>(int)$row['id'],
                'steam_id64'=>(string)$row['steam_id64'],
                'player_name'=>(string)$row['player_name'],
                'reason_code'=>(string)$row['reason_code'],
                'reason'=>$reason,
                'created_at'=>(string)$row['starts_at'],
                'expires_at'=>$row['expires_at']!==null?(string)$row['expires_at']:null,
                'permanent'=>(int)$row['permanent']===1,
            ];
        },$rows);
    }

    public static function chatMessage(int $id): ?array
    {
        $stmt=Database::connection()->prepare(
            'SELECT m.*,gs.name AS server_name FROM server_chat_messages m LEFT JOIN game_servers gs ON gs.id=m.server_id WHERE m.id=? LIMIT 1'
        );
        $stmt->execute([$id]); $row=$stmt->fetch(); return $row?:null;
    }

    private static function chatEvidence(int $id, string $steamId64): ?array
    {
        $row=self::chatMessage($id);
        if(!$row) return null;
        if(trim((string)($row['steam_id64']??''))!==$steamId64) return null;
        return $row;
    }

    private static function expireDue(): void
    {
        $now=date('c');
        $pdo=Database::connection();
        $due=$pdo->prepare(
            "SELECT id,steam_id64 FROM global_bans WHERE active=1 AND permanent=0 AND expires_at IS NOT NULL AND expires_at<=? AND (source='anti_advertisement' OR reason_code='advertising')"
        );
        $due->execute([$now]);$advertisingBans=$due->fetchAll();
        $pdo->prepare(
            "UPDATE global_bans SET active=0,unbanned_at=COALESCE(unbanned_at,?),unban_reason=COALESCE(unban_reason,'Automatisch abgelaufen'),updated_at=? WHERE active=1 AND permanent=0 AND expires_at IS NOT NULL AND expires_at<=?"
        )->execute([$now,$now,$now]);
        foreach($advertisingBans as $ban){
            AntiAdvertisementService::releaseAfterUnban((int)$ban['id'],(string)$ban['steam_id64'],'Automatisch abgelaufen',null);
        }
    }

    private static function assertSteamId(string $steamId64): void
    {
        if (!preg_match('/^7656119\d{10}$/',$steamId64)) throw new \InvalidArgumentException('Ungültige SteamID64.');
    }

    private static function cut(string $value,int $max): string
    {
        return function_exists('mb_substr') ? mb_substr($value,0,$max) : substr($value,0,$max);
    }
}
