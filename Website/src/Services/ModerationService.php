<?php
namespace App\Services;

use App\Core\Audit;
use App\Core\Database;

final class ModerationService
{
    public const SANCTION_TYPES = [
        'mute' => 'Globaler Voice-Mute',
        'gag' => 'Globaler Chat-Gag',
        'silence' => 'Globaler Silence (Voice + Chat)',
    ];

    public static function reasons(): array { return GlobalBanService::reasons(); }
    public static function durations(): array { return GlobalBanService::durations(); }
    public static function sanctionTypes(): array { return self::SANCTION_TYPES; }

    public static function context(string $steamId64): array
    {
        self::assertSteamId($steamId64);
        self::expireDue();
        return [
            'warnings' => self::warningsForSteam($steamId64, 8),
            'active_warning_count' => self::activeWarningCount($steamId64),
            'notes' => self::notesForSteam($steamId64, 8),
            'evidence' => self::evidenceForSteam($steamId64, 8),
            'sanctions' => self::sanctionsForSteam($steamId64, true),
            'appeals' => self::appealsForSteam($steamId64, 8),
            'ban_history' => GlobalBanService::search(['status'=>'all','q'=>$steamId64,'limit'=>25]),
            'advertising_events' => AntiAdvertisementService::events(['q'=>$steamId64,'limit'=>25]),
        ];
    }

    public static function createWarning(string $steamId64, string $playerName, string $reasonCode, string $detail, ?int $createdBy, ?int $chatId = null, int $expiresDays = 90, bool $autoEscalate = true): array
    {
        self::assertSteamId($steamId64);
        $reasonCode = isset(GlobalBanService::reasons()[$reasonCode]) ? $reasonCode : 'other';
        $label = GlobalBanService::reasons()[$reasonCode];
        $detail = self::cut(trim($detail), 700);
        $playerName = self::playerName($playerName, $steamId64);
        $expiresDays = max(0, min(3650, $expiresDays));
        $now = date('c');
        $expiresAt = $expiresDays === 0 ? null : date('c', time() + ($expiresDays * 86400));
        $evidence = self::chatSnapshot($chatId, $steamId64);

        $stmt = Database::connection()->prepare(
            'INSERT INTO moderation_warnings(steam_id64,player_name,reason_code,reason_label,detail,created_by,created_at,expires_at,evidence_chat_message_id,evidence_message,evidence_sent_at) VALUES(?,?,?,?,?,?,?,?,?,?,?)'
        );
        $stmt->execute([$steamId64,$playerName,$reasonCode,$label,$detail,$createdBy,$now,$expiresAt,$evidence['id']??null,$evidence['message']??null,$evidence['sent_at']??null]);
        $id=(int)Database::connection()->lastInsertId();
        Audit::log('moderation.warning.create','steam_player',$steamId64,['warning_id'=>$id,'reason_code'=>$reasonCode,'expires_at'=>$expiresAt,'chat_message_id'=>$chatId]);

        $active = self::activeWarningCount($steamId64);
        $banId = null;
        if ($autoEscalate && $active >= 3 && GlobalBanService::activeForSteam($steamId64) === null) {
            $banId = GlobalBanService::create(
                $steamId64,
                $playerName,
                'server_rules',
                'Automatische Eskalation nach ' . $active . ' aktiven Verwarnungen.',
                1440,
                $createdBy,
                $chatId
            );
            Audit::log('moderation.warning.auto_escalation','steam_player',$steamId64,['warning_id'=>$id,'active_warnings'=>$active,'global_ban_id'=>$banId]);
        }
        return ['id'=>$id,'active_count'=>$active,'global_ban_id'=>$banId];
    }

    public static function resolveWarning(int $id, string $reason, ?int $userId): void
    {
        $reason=self::cut(trim($reason),300); if($reason==='')$reason='Erledigt';
        $stmt=Database::connection()->prepare('UPDATE moderation_warnings SET resolved_at=?,resolved_by=?,resolution=? WHERE id=? AND resolved_at IS NULL');
        $stmt->execute([date('c'),$userId,$reason,$id]);
        Audit::log('moderation.warning.resolve','moderation_warning',(string)$id,['reason'=>$reason]);
    }

    public static function addNote(string $steamId64, string $playerName, string $note, ?int $createdBy): int
    {
        self::assertSteamId($steamId64); $note=self::cut(trim($note),2000);
        if($note==='')throw new \InvalidArgumentException('Notiz darf nicht leer sein.');
        $stmt=Database::connection()->prepare('INSERT INTO moderation_notes(steam_id64,player_name,note,created_by,created_at) VALUES(?,?,?,?,?)');
        $stmt->execute([$steamId64,self::playerName($playerName,$steamId64),$note,$createdBy,date('c')]);
        $id=(int)Database::connection()->lastInsertId();
        Audit::log('moderation.note.create','steam_player',$steamId64,['note_id'=>$id]);
        return $id;
    }

    public static function addEvidence(string $steamId64, string $playerName, string $type, string $label, string $value, ?int $createdBy, ?int $chatId = null, ?int $banId = null, ?int $warningId = null): int
    {
        self::assertSteamId($steamId64);
        $type=in_array($type,['note','url','chat'],true)?$type:'note';
        $label=self::cut(trim($label),160); $value=self::cut(trim($value),4000);
        if($type==='url'){
            if(!filter_var($value,FILTER_VALIDATE_URL) || !in_array(strtolower((string)parse_url($value,PHP_URL_SCHEME)),['https','http'],true)) throw new \InvalidArgumentException('Ungültige Beweis-URL.');
        }
        if($type==='chat'){
            $snap=self::chatSnapshot($chatId,$steamId64);
            if(!$snap)throw new \InvalidArgumentException('Chatnachricht nicht gefunden oder gehört nicht zum Spieler.');
            $label=$label!==''?$label:'Serverchat'; $value=(string)$snap['message'];
        }
        if($value==='')throw new \InvalidArgumentException('Beweisinhalt darf nicht leer sein.');
        $stmt=Database::connection()->prepare('INSERT INTO moderation_evidence(steam_id64,player_name,evidence_type,label,value_text,chat_message_id,global_ban_id,warning_id,created_by,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)');
        $stmt->execute([$steamId64,self::playerName($playerName,$steamId64),$type,$label,$value,$chatId,$banId,$warningId,$createdBy,date('c')]);
        $id=(int)Database::connection()->lastInsertId();
        Audit::log('moderation.evidence.create','steam_player',$steamId64,['evidence_id'=>$id,'type'=>$type,'ban_id'=>$banId,'warning_id'=>$warningId]);
        return $id;
    }

    public static function createSanction(string $steamId64, string $playerName, string $type, string $reasonCode, string $detail, int $minutes, ?int $createdBy, ?int $chatId = null): int
    {
        self::assertSteamId($steamId64);
        if(!isset(self::SANCTION_TYPES[$type]))throw new \InvalidArgumentException('Ungültige Kommunikationssperre.');
        $reasons=GlobalBanService::reasons(); $reasonCode=isset($reasons[$reasonCode])?$reasonCode:'other';
        $minutes=max(0,min(5256000,$minutes)); $now=date('c'); $expires=$minutes===0?null:date('c',time()+$minutes*60);
        $detail=self::cut(trim($detail),500); $playerName=self::playerName($playerName,$steamId64);
        $evidence=self::chatSnapshot($chatId,$steamId64);
        $pdo=Database::connection(); $pdo->beginTransaction();
        try{
            $pdo->prepare("UPDATE global_communication_sanctions SET active=0,revoked_at=?,revoke_reason='Durch neue globale Kommunikationssperre ersetzt',updated_at=? WHERE steam_id64=? AND sanction_type=? AND active=1")->execute([$now,$now,$steamId64,$type]);
            $stmt=$pdo->prepare('INSERT INTO global_communication_sanctions(steam_id64,player_name,sanction_type,reason_code,reason_label,reason_detail,starts_at,expires_at,permanent,active,created_by,created_at,updated_at,evidence_chat_message_id,evidence_message,evidence_sent_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
            $stmt->execute([$steamId64,$playerName,$type,$reasonCode,$reasons[$reasonCode],$detail,$now,$expires,$minutes===0?1:0,1,$createdBy,$now,$now,$evidence['id']??null,$evidence['message']??null,$evidence['sent_at']??null]);
            $id=(int)$pdo->lastInsertId(); $pdo->commit();
            Audit::log('moderation.global_comm.create','global_comm_sanction',(string)$id,['steam_id64'=>$steamId64,'type'=>$type,'reason_code'=>$reasonCode,'minutes'=>$minutes]);
            return $id;
        }catch(\Throwable $e){if($pdo->inTransaction())$pdo->rollBack();throw $e;}
    }

    public static function revokeSanction(int $id, string $reason, ?int $userId): void
    {
        $reason=self::cut(trim($reason),300); if($reason==='')$reason='Global aufgehoben';
        Database::connection()->prepare('UPDATE global_communication_sanctions SET active=0,revoked_by=?,revoked_at=?,revoke_reason=?,updated_at=? WHERE id=? AND active=1')->execute([$userId,date('c'),$reason,date('c'),$id]);
        Audit::log('moderation.global_comm.revoke','global_comm_sanction',(string)$id,['reason'=>$reason]);
    }

    public static function createAppeal(int $banId, string $steamId64, string $playerName, string $statement, ?int $createdBy): int
    {
        self::assertSteamId($steamId64); $statement=self::cut(trim($statement),5000);
        if($statement==='')throw new \InvalidArgumentException('Appeal-Text darf nicht leer sein.');
        $stmt=Database::connection()->prepare('INSERT INTO moderation_appeals(global_ban_id,steam_id64,player_name,statement,status,created_by,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?)');
        $stmt->execute([$banId>0?$banId:null,$steamId64,self::playerName($playerName,$steamId64),$statement,'open',$createdBy,date('c'),date('c')]);
        $id=(int)Database::connection()->lastInsertId();
        Audit::log('moderation.appeal.create','moderation_appeal',(string)$id,['steam_id64'=>$steamId64,'global_ban_id'=>$banId]);
        return $id;
    }

    public static function updateAppeal(int $id, string $status, string $decision, ?int $reviewedBy): void
    {
        $status=in_array($status,['open','reviewing','approved','denied'],true)?$status:'reviewing';
        $decision=self::cut(trim($decision),3000); $now=date('c');
        Database::connection()->prepare('UPDATE moderation_appeals SET status=?,decision=?,reviewed_by=?,reviewed_at=?,updated_at=? WHERE id=?')->execute([$status,$decision,$reviewedBy,$status==='open'?null:$now,$now,$id]);
        Audit::log('moderation.appeal.update','moderation_appeal',(string)$id,['status'=>$status]);
    }

    public static function statusMap(array $steamIds): array
    {
        $steamIds = array_values(array_unique(array_filter(array_map(
            static fn($value): string => trim((string)$value),
            $steamIds
        ), static fn(string $steam): bool => preg_match('/^7656119\d{10}$/', $steam) === 1)));
        if ($steamIds === []) return [];
        if (count($steamIds) > 250) $steamIds = array_slice($steamIds, 0, 250);

        self::expireDue();
        $pdo = Database::connection();
        $now = date('c');
        $placeholders = implode(',', array_fill(0, count($steamIds), '?'));
        $out = [];
        foreach ($steamIds as $steam) {
            $out[$steam] = [
                'steam_id64' => $steam,
                'banned' => false,
                'ban_id' => null,
                'ban_reason' => null,
                'ban_expires_at' => null,
                'ban_permanent' => false,
                'warnings' => 0,
                'mute' => false,
                'gag' => false,
                'silence' => false,
                'sanctions' => [],
            ];
        }

        $banSql = 'SELECT id,steam_id64,reason_label,reason_detail,expires_at,permanent FROM global_bans '
            . 'WHERE active=1 AND steam_id64 IN (' . $placeholders . ') '
            . 'AND (permanent=1 OR expires_at IS NULL OR expires_at>?) ORDER BY id DESC';
        $stmt = $pdo->prepare($banSql);
        $stmt->execute([...$steamIds, $now]);
        foreach ($stmt->fetchAll() as $row) {
            $steam = (string)$row['steam_id64'];
            if (!isset($out[$steam]) || $out[$steam]['banned']) continue;
            $reason = trim((string)($row['reason_label'] ?? ''));
            $detail = trim((string)($row['reason_detail'] ?? ''));
            if ($detail !== '') $reason .= ($reason !== '' ? ': ' : '') . $detail;
            $out[$steam]['banned'] = true;
            $out[$steam]['ban_id'] = (int)$row['id'];
            $out[$steam]['ban_reason'] = $reason !== '' ? $reason : 'Globaler Ban';
            $out[$steam]['ban_expires_at'] = $row['expires_at'] !== null ? (string)$row['expires_at'] : null;
            $out[$steam]['ban_permanent'] = (int)$row['permanent'] === 1;
        }

        $warningSql = 'SELECT steam_id64,COUNT(*) AS c FROM moderation_warnings '
            . 'WHERE steam_id64 IN (' . $placeholders . ') AND resolved_at IS NULL '
            . 'AND (expires_at IS NULL OR expires_at>?) GROUP BY steam_id64';
        $stmt = $pdo->prepare($warningSql);
        $stmt->execute([...$steamIds, $now]);
        foreach ($stmt->fetchAll() as $row) {
            $steam = (string)$row['steam_id64'];
            if (isset($out[$steam])) $out[$steam]['warnings'] = (int)$row['c'];
        }

        $sanctionSql = 'SELECT id,steam_id64,sanction_type,reason_label,reason_detail,expires_at,permanent '
            . 'FROM global_communication_sanctions WHERE active=1 AND steam_id64 IN (' . $placeholders . ') '
            . 'AND (permanent=1 OR expires_at IS NULL OR expires_at>?) ORDER BY id DESC';
        $stmt = $pdo->prepare($sanctionSql);
        $stmt->execute([...$steamIds, $now]);
        foreach ($stmt->fetchAll() as $row) {
            $steam = (string)$row['steam_id64'];
            if (!isset($out[$steam])) continue;
            $type = strtolower((string)$row['sanction_type']);
            if (isset($out[$steam][$type])) $out[$steam][$type] = true;
            if ($type === 'silence') {
                $out[$steam]['mute'] = true;
                $out[$steam]['gag'] = true;
            }
            $reason = trim((string)($row['reason_label'] ?? ''));
            $detail = trim((string)($row['reason_detail'] ?? ''));
            if ($detail !== '') $reason .= ($reason !== '' ? ': ' : '') . $detail;
            $out[$steam]['sanctions'][] = [
                'id' => (int)$row['id'],
                'type' => $type,
                'reason' => $reason,
                'expires_at' => $row['expires_at'] !== null ? (string)$row['expires_at'] : null,
                'permanent' => (int)$row['permanent'] === 1,
            ];
        }

        foreach ($out as &$status) {
            $status['restricted'] = $status['banned'] || $status['warnings'] > 0 || $status['mute'] || $status['gag'] || $status['silence'];
        }
        unset($status);
        return $out;
    }

    public static function activeCommunicationPayload(): array
    {
        self::expireDue();
        $rows=Database::connection()->query('SELECT id,steam_id64,player_name,sanction_type,reason_code,reason_label,reason_detail,starts_at,expires_at,permanent FROM global_communication_sanctions WHERE active=1 ORDER BY id')->fetchAll();
        return array_map(static function(array $r):array{
            $reason=(string)$r['reason_label']; $detail=trim((string)($r['reason_detail']??'')); if($detail!=='')$reason.=': '.$detail;
            return ['id'=>(int)$r['id'],'steam_id64'=>(string)$r['steam_id64'],'player_name'=>(string)$r['player_name'],'sanction_type'=>(string)$r['sanction_type'],'reason_code'=>(string)$r['reason_code'],'reason'=>$reason,'created_at'=>(string)$r['starts_at'],'expires_at'=>$r['expires_at']!==null?(string)$r['expires_at']:null,'permanent'=>(int)$r['permanent']===1];
        },$rows);
    }

    public static function dashboard(string $q='',string $advertisingStatus=''): array
    {
        self::expireDue(); $q=trim($q); $like='%'.$q.'%';
        $pdo=Database::connection();
        $warnings=$q===''?$pdo->query('SELECT w.*,u.name created_by_name FROM moderation_warnings w LEFT JOIN users u ON u.id=w.created_by ORDER BY w.id DESC LIMIT 100')->fetchAll():self::query('SELECT w.*,u.name created_by_name FROM moderation_warnings w LEFT JOIN users u ON u.id=w.created_by WHERE w.steam_id64 LIKE ? OR w.player_name LIKE ? COLLATE NOCASE OR w.reason_label LIKE ? COLLATE NOCASE ORDER BY w.id DESC LIMIT 100',[$like,$like,$like]);
        $sanctions=$q===''?$pdo->query('SELECT s.*,u.name created_by_name FROM global_communication_sanctions s LEFT JOIN users u ON u.id=s.created_by ORDER BY s.active DESC,s.id DESC LIMIT 100')->fetchAll():self::query('SELECT s.*,u.name created_by_name FROM global_communication_sanctions s LEFT JOIN users u ON u.id=s.created_by WHERE s.steam_id64 LIKE ? OR s.player_name LIKE ? COLLATE NOCASE OR s.reason_label LIKE ? COLLATE NOCASE ORDER BY s.active DESC,s.id DESC LIMIT 100',[$like,$like,$like]);
        $appeals=$q===''?$pdo->query('SELECT a.*,u.name created_by_name,ru.name reviewed_by_name FROM moderation_appeals a LEFT JOIN users u ON u.id=a.created_by LEFT JOIN users ru ON ru.id=a.reviewed_by ORDER BY CASE a.status WHEN "open" THEN 0 WHEN "reviewing" THEN 1 ELSE 2 END,a.id DESC LIMIT 100')->fetchAll():self::query('SELECT a.*,u.name created_by_name,ru.name reviewed_by_name FROM moderation_appeals a LEFT JOIN users u ON u.id=a.created_by LEFT JOIN users ru ON ru.id=a.reviewed_by WHERE a.steam_id64 LIKE ? OR a.player_name LIKE ? COLLATE NOCASE OR a.statement LIKE ? COLLATE NOCASE ORDER BY a.id DESC LIMIT 100',[$like,$like,$like]);
        $notes=$q===''?$pdo->query('SELECT n.*,u.name created_by_name FROM moderation_notes n LEFT JOIN users u ON u.id=n.created_by ORDER BY n.id DESC LIMIT 100')->fetchAll():self::query('SELECT n.*,u.name created_by_name FROM moderation_notes n LEFT JOIN users u ON u.id=n.created_by WHERE n.steam_id64 LIKE ? OR n.player_name LIKE ? COLLATE NOCASE OR n.note LIKE ? COLLATE NOCASE ORDER BY n.id DESC LIMIT 100',[$like,$like,$like]);
        $evidence=$q===''?$pdo->query('SELECT e.*,e.value_text AS value,u.name created_by_name FROM moderation_evidence e LEFT JOIN users u ON u.id=e.created_by ORDER BY e.id DESC LIMIT 100')->fetchAll():self::query('SELECT e.*,e.value_text AS value,u.name created_by_name FROM moderation_evidence e LEFT JOIN users u ON u.id=e.created_by WHERE e.steam_id64 LIKE ? OR e.player_name LIKE ? COLLATE NOCASE OR e.label LIKE ? COLLATE NOCASE OR e.value_text LIKE ? COLLATE NOCASE ORDER BY e.id DESC LIMIT 100',[$like,$like,$like,$like]);
        $bans=GlobalBanService::search(['status'=>'all','q'=>$q,'limit'=>100]);
        $advertisingCases=AntiAdvertisementService::cases(['q'=>$q,'status'=>$advertisingStatus,'limit'=>100]);
        return [
            'warnings'=>$warnings,'sanctions'=>$sanctions,'appeals'=>$appeals,'notes'=>$notes,'evidence'=>$evidence,
            'bans'=>$bans,'advertising_cases'=>$advertisingCases,'advertising_counts'=>AntiAdvertisementService::countCasesByStatus(),
            'advertising_rules'=>AntiAdvertisementService::rules(),
        ];
    }

    public static function warningsForSteam(string $steamId64,int $limit=20):array{self::expireDue();return self::query('SELECT w.*,u.name created_by_name,ru.name resolved_by_name FROM moderation_warnings w LEFT JOIN users u ON u.id=w.created_by LEFT JOIN users ru ON ru.id=w.resolved_by WHERE w.steam_id64=? ORDER BY w.id DESC LIMIT '.max(1,min(100,$limit)),[$steamId64]);}
    public static function notesForSteam(string $steamId64,int $limit=20):array{return self::query('SELECT n.*,u.name created_by_name FROM moderation_notes n LEFT JOIN users u ON u.id=n.created_by WHERE n.steam_id64=? ORDER BY n.id DESC LIMIT '.max(1,min(100,$limit)),[$steamId64]);}
    public static function evidenceForSteam(string $steamId64,int $limit=20):array{return self::query('SELECT e.*,e.value_text AS value,u.name created_by_name FROM moderation_evidence e LEFT JOIN users u ON u.id=e.created_by WHERE e.steam_id64=? ORDER BY e.id DESC LIMIT '.max(1,min(100,$limit)),[$steamId64]);}
    public static function sanctionsForSteam(string $steamId64,bool $activeOnly=false):array{self::expireDue();return self::query('SELECT s.*,u.name created_by_name FROM global_communication_sanctions s LEFT JOIN users u ON u.id=s.created_by WHERE s.steam_id64=?'.($activeOnly?' AND s.active=1':'').' ORDER BY s.active DESC,s.id DESC',[$steamId64]);}
    public static function appealsForSteam(string $steamId64,int $limit=20):array{return self::query('SELECT a.*,u.name created_by_name,ru.name reviewed_by_name FROM moderation_appeals a LEFT JOIN users u ON u.id=a.created_by LEFT JOIN users ru ON ru.id=a.reviewed_by WHERE a.steam_id64=? ORDER BY a.id DESC LIMIT '.max(1,min(100,$limit)),[$steamId64]);}
    public static function activeWarningCount(string $steamId64):int{self::expireDue();$s=Database::connection()->prepare('SELECT COUNT(*) FROM moderation_warnings WHERE steam_id64=? AND resolved_at IS NULL AND (expires_at IS NULL OR expires_at>?)');$s->execute([$steamId64,date('c')]);return(int)$s->fetchColumn();}

    private static function expireDue():void{$now=date('c');Database::connection()->prepare("UPDATE global_communication_sanctions SET active=0,revoked_at=COALESCE(revoked_at,?),revoke_reason=COALESCE(revoke_reason,'Automatisch abgelaufen'),updated_at=? WHERE active=1 AND permanent=0 AND expires_at IS NOT NULL AND expires_at<=?")->execute([$now,$now,$now]);}
    private static function chatSnapshot(?int $chatId,string $steamId64):?array{if(!$chatId)return null;$s=Database::connection()->prepare('SELECT id,steam_id64,message,sent_at FROM server_chat_messages WHERE id=? LIMIT 1');$s->execute([$chatId]);$r=$s->fetch();return $r&&((string)$r['steam_id64']===$steamId64)?$r:null;}
    private static function query(string $sql,array $params):array{$s=Database::connection()->prepare($sql);$s->execute($params);return$s->fetchAll();}
    private static function assertSteamId(string $steam):void{if(!preg_match('/^7656119\d{10}$/',$steam))throw new \InvalidArgumentException('Ungültige SteamID64.');}
    private static function playerName(string $name,string $steam):string{$name=self::cut(trim($name),80);return$name!==''?$name:$steam;}
    private static function cut(string $v,int $max):string{return function_exists('mb_substr')?mb_substr($v,0,$max):substr($v,0,$max);}
}
