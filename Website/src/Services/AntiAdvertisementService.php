<?php
namespace App\Services;

use App\Core\Audit;
use App\Core\Database;

/** Zentrale serverübergreifende Werbeschutz-Synchronisation und Moderationsakte. */
final class AntiAdvertisementService
{
    private const DEFAULT_DOMAINS = ['cs2commends.com','jaegerservice.xyz','sinlyxe.cc','clash.gg'];
    private const DEFAULT_ALLOWED_DOMAINS = ['community.example.invalid'];
    private const DEFAULT_KEYWORDS = [
        'free commends','free commend','free cs2','cs2 commends','commends','commend boost',
        'cheap skins','free skins','free cases','deposit bonus','bonus code','promo code','discord.gg',
    ];
    private const RULE_TYPES = ['blocked_domain','allowed_domain','blocked_phrase'];
    private const REVIEW_STATUSES = ['detected','reported','protected','banned','unbanned','false_positive','resolved'];

    public static function ingestEvents(int $serverId, array $events): void
    {
        if ($serverId <= 0 || !$events) return;
        $events = array_slice($events, 0, 100);
        Database::withBusyRetry(static function () use ($serverId,$events): void {
            $insert=Database::connection()->prepare(
                'INSERT OR IGNORE INTO global_advertising_events(' .
                'event_id,server_id,event_type,steam_id64,player_name,match_value,action,message_excerpt,observed_at,received_at,origin,review_status' .
                ") VALUES(?,?,?,?,?,?,?,?,?,?,'automatic','detected')"
            );
            $identity=self::identityUpsertStatement();
            $domainStmt=self::domainUpsertStatement();
            $now=date('c');
            foreach($events as $event){
                if(!is_array($event)) continue;
                $eventId=trim((string)($event['event_id']??$event['id']??''));
                if($eventId==='' || strlen($eventId)>128) continue;
                $type=substr(trim((string)($event['event_type']??$event['type']??'unknown')),0,64);
                $steam=trim((string)($event['steam_id64']??''));
                if($steam!=='' && !preg_match('/^7656119\d{10}$/',$steam)) $steam='';
                $name=self::cut(trim((string)($event['player_name']??'')),128);
                $match=self::cut(trim((string)($event['match_value']??$event['match']??'')),255);
                $action=self::cut(trim((string)($event['action']??'')),64);
                $excerpt=self::cut(trim((string)($event['message_excerpt']??'')),500);
                $observed=trim((string)($event['observed_at']??''));
                if($observed==='' || strtotime($observed)===false) $observed=$now;
                $insert->execute([$eventId,$serverId,$type,$steam!==''?$steam:null,$name,$match,$action,$excerpt!==''?$excerpt:null,$observed,$now]);
                if($insert->rowCount()===0) continue;
                if($steam!=='') $identity->execute([$steam,$name,$match,$serverId,$serverId,$observed,$observed,1]);
                $domain=self::extractDomain($match);
                if($domain!==null) $domainStmt->execute([$domain,$serverId,$serverId,$observed,$observed]);
            }
        }, 5);
    }

    /** Meldet eine archivierte Chatnachricht zur Prüfung, ohne vor Bestätigung automatisch zu sperren. */
    public static function reportChatMessage(int $chatMessageId, ?int $reportedBy): int
    {
        if($chatMessageId<=0) throw new \InvalidArgumentException('Chatnachricht fehlt.');
        $pdo=Database::connection();
        $stmt=$pdo->prepare('SELECT m.*,gs.name AS server_name FROM server_chat_messages m LEFT JOIN game_servers gs ON gs.id=m.server_id WHERE m.id=? LIMIT 1');
        $stmt->execute([$chatMessageId]);
        $chat=$stmt->fetch();
        if(!$chat) throw new \RuntimeException('Chatnachricht wurde nicht gefunden.');
        $steam=trim((string)($chat['steam_id64']??''));
        self::assertSteamId($steam);
        $message=self::cut(trim((string)($chat['message']??'')),500);
        if($message==='') throw new \InvalidArgumentException('Leere Chatnachrichten können nicht gemeldet werden.');
        $name=self::cut(trim((string)($chat['player_name']??'')),128);
        $match=self::extractDomain($message)??'Manuelle Spam-/Werbemeldung';
        $observed=trim((string)($chat['sent_at']??''));
        if($observed==='' || strtotime($observed)===false) $observed=date('c');
        $now=date('c');
        $eventKey='manual-chat-'.$chatMessageId;

        Database::withBusyRetry(static function() use($pdo,$eventKey,$chat,$steam,$name,$match,$message,$observed,$now,$chatMessageId,$reportedBy):void{
            $pdo->beginTransaction();
            try{
                $insert=$pdo->prepare(
                    'INSERT OR IGNORE INTO global_advertising_events(' .
                    'event_id,server_id,event_type,steam_id64,player_name,match_value,action,message_excerpt,observed_at,received_at,origin,review_status,chat_message_id,reported_by' .
                    ") VALUES(?,?,?,?,?,?,?,?,?,?,'manual_chat','reported',?,?)"
                );
                $insert->execute([$eventKey,(int)$chat['server_id'],'manual_chat_report',$steam,$name,$match,'pending_review',$message,$observed,$now,$chatMessageId,$reportedBy]);
                if($insert->rowCount()===0) throw new \RuntimeException('Diese Chatnachricht wurde bereits als Werbung gemeldet.');
                // Ein vorhandener aktiver Schutzstatus darf durch die neue Meldung nicht deaktiviert werden.
                self::identityUpsertStatement()->execute([$steam,$name,$match,(int)$chat['server_id'],(int)$chat['server_id'],$observed,$observed,0]);
                $pdo->commit();
            }catch(\Throwable $e){if($pdo->inTransaction())$pdo->rollBack();throw $e;}
        },5);
        $find=$pdo->prepare('SELECT id FROM global_advertising_events WHERE event_id=? LIMIT 1');$find->execute([$eventKey]);
        $id=(int)$find->fetchColumn();
        Audit::log('anti_advertisement.report','advertising_event',(string)$id,['steam_id64'=>$steam,'chat_message_id'=>$chatMessageId,'server_id'=>(int)$chat['server_id']]);
        return $id;
    }

    public static function reviewEvent(int $eventId, string $status, string $note, ?int $reviewedBy): void
    {
        if(!in_array($status,['protected','false_positive','resolved'],true)) throw new \InvalidArgumentException('Ungültiger Prüfstatus.');
        $event=self::getEvent($eventId);
        if(!$event) throw new \RuntimeException('Werbeereignis wurde nicht gefunden.');
        $note=self::cut(trim($note),500);
        $steam=trim((string)($event['steam_id64']??''));
        $now=date('c');
        $pdo=Database::connection();
        Database::withBusyRetry(static function() use($pdo,$eventId,$status,$note,$reviewedBy,$now,$event,$steam):void{
            $pdo->beginTransaction();
            try{
                $pdo->prepare('UPDATE global_advertising_events SET review_status=?,review_note=?,reviewed_by=?,reviewed_at=? WHERE id=?')
                    ->execute([$status,$note!==''?$note:null,$reviewedBy,$now,$eventId]);
                if($status==='protected' && preg_match('/^7656119\d{10}$/',$steam)){
                    self::identityUpsertStatement()->execute([
                        $steam,(string)$event['player_name'],(string)$event['match_value'],(int)$event['server_id'],(int)$event['server_id'],
                        (string)$event['observed_at'],$now,1
                    ]);
                    $domain=self::extractDomain((string)$event['match_value']);
                    if($domain!==null) self::domainUpsertStatement()->execute([$domain,(int)$event['server_id'],(int)$event['server_id'],(string)$event['observed_at'],$now]);
                }elseif($status==='false_positive' && preg_match('/^7656119\d{10}$/',$steam)){
                    self::refreshIdentityActivity($steam);
                    $domain=self::extractDomain((string)$event['match_value']);
                    if($domain!==null) self::refreshDomainActivity($domain);
                }
                $pdo->commit();
            }catch(\Throwable $e){if($pdo->inTransaction())$pdo->rollBack();throw $e;}
        },5);
        Audit::log('anti_advertisement.review','advertising_event',(string)$eventId,['steam_id64'=>$steam,'status'=>$status,'note'=>$note]);
    }

    public static function getEvent(int $id): ?array
    {
        $stmt=Database::connection()->prepare(
            'SELECT e.*,gs.name AS server_name,ru.name AS reported_by_name,vu.name AS reviewed_by_name ' .
            'FROM global_advertising_events e LEFT JOIN game_servers gs ON gs.id=e.server_id ' .
            'LEFT JOIN users ru ON ru.id=e.reported_by LEFT JOIN users vu ON vu.id=e.reviewed_by WHERE e.id=? LIMIT 1'
        );
        $stmt->execute([$id]);$row=$stmt->fetch();return$row?:null;
    }

    public static function events(array $filters=[]): array
    {
        $where=['1=1'];$params=[];
        $status=trim((string)($filters['status']??''));
        if($status!=='' && in_array($status,self::REVIEW_STATUSES,true)){$where[]='e.review_status=?';$params[]=$status;}
        $q=trim((string)($filters['q']??''));
        if($q!==''){
            $like='%'.$q.'%';
            $where[]='(e.steam_id64 LIKE ? OR e.player_name LIKE ? COLLATE NOCASE OR e.match_value LIKE ? COLLATE NOCASE OR e.message_excerpt LIKE ? COLLATE NOCASE OR gs.name LIKE ? COLLATE NOCASE)';
            array_push($params,$like,$like,$like,$like,$like);
        }
        $limit=max(25,min(500,(int)($filters['limit']??100)));
        $stmt=Database::connection()->prepare(
            'SELECT e.*,gs.name AS server_name,ru.name AS reported_by_name,vu.name AS reviewed_by_name,' .
            'b.active AS ban_active,b.unbanned_at,b.unban_reason,bu.name AS unbanned_by_name ' .
            'FROM global_advertising_events e LEFT JOIN game_servers gs ON gs.id=e.server_id ' .
            'LEFT JOIN users ru ON ru.id=e.reported_by LEFT JOIN users vu ON vu.id=e.reviewed_by ' .
            'LEFT JOIN global_bans b ON b.id=e.global_ban_id LEFT JOIN users bu ON bu.id=b.unbanned_by ' .
            'WHERE '.implode(' AND ',$where).' ORDER BY e.received_at DESC,e.id DESC LIMIT '.$limit
        );
        $stmt->execute($params);return$stmt->fetchAll();
    }

    /** Eine aktuelle Moderationsakte pro SteamID mit zusammengeführter Ereignis- und Ban-Historie. */
    public static function cases(array $filters=[]): array
    {
        $pdo=Database::connection();
        $where=[
            "e.steam_id64 IS NOT NULL",
            "TRIM(e.steam_id64)<>''",
            'e.id=(SELECT e2.id FROM global_advertising_events e2 WHERE e2.steam_id64=e.steam_id64 ORDER BY e2.received_at DESC,e2.id DESC LIMIT 1)'
        ];
        $params=[];
        $status=trim((string)($filters['status']??''));
        $activeBanExists='EXISTS(SELECT 1 FROM global_bans bx WHERE bx.steam_id64=e.steam_id64 AND bx.active=1)';
        if($status==='banned'){
            $where[]=$activeBanExists;
        }elseif($status!=='' && in_array($status,self::REVIEW_STATUSES,true)){
            $where[]='NOT '.$activeBanExists;
            $where[]='e.review_status=?';
            $params[]=$status;
        }
        $q=trim((string)($filters['q']??''));
        if($q!==''){
            $like='%'.$q.'%';
            $where[]='EXISTS(SELECT 1 FROM global_advertising_events eq LEFT JOIN game_servers gsq ON gsq.id=eq.server_id WHERE eq.steam_id64=e.steam_id64 AND (eq.steam_id64 LIKE ? OR eq.player_name LIKE ? COLLATE NOCASE OR eq.match_value LIKE ? COLLATE NOCASE OR eq.message_excerpt LIKE ? COLLATE NOCASE OR gsq.name LIKE ? COLLATE NOCASE))';
            array_push($params,$like,$like,$like,$like,$like);
        }
        $limit=max(25,min(200,(int)($filters['limit']??100)));
        $stmt=$pdo->prepare(
            'SELECT e.*,gs.name AS server_name,ru.name AS reported_by_name,vu.name AS reviewed_by_name,' .
            'ai.active AS advertising_identity_active,ai.hit_count AS identity_hit_count,ai.first_seen_at AS identity_first_seen_at,ai.last_seen_at AS identity_last_seen_at,' .
            'ab.id AS active_ban_id,ab.starts_at AS active_ban_starts_at,ab.expires_at AS active_ban_expires_at,ab.permanent AS active_ban_permanent,' .
            'ab.reason_label AS active_ban_reason_label,ab.reason_detail AS active_ban_reason_detail,abu.name AS active_ban_created_by_name,' .
            'CASE WHEN ab.id IS NOT NULL THEN \'banned\' ELSE e.review_status END AS case_status,' .
            '(SELECT COUNT(*) FROM global_advertising_events ec WHERE ec.steam_id64=e.steam_id64) AS event_count,' .
            '(SELECT COUNT(DISTINCT ec.server_id) FROM global_advertising_events ec WHERE ec.steam_id64=e.steam_id64) AS server_count ' .
            'FROM global_advertising_events e LEFT JOIN game_servers gs ON gs.id=e.server_id ' .
            'LEFT JOIN users ru ON ru.id=e.reported_by LEFT JOIN users vu ON vu.id=e.reviewed_by ' .
            'LEFT JOIN global_advertising_identities ai ON ai.steam_id64=e.steam_id64 ' .
            'LEFT JOIN global_bans ab ON ab.id=(SELECT ab2.id FROM global_bans ab2 WHERE ab2.steam_id64=e.steam_id64 AND ab2.active=1 ORDER BY ab2.id DESC LIMIT 1) ' .
            'LEFT JOIN users abu ON abu.id=ab.created_by ' .
            'WHERE '.implode(' AND ',$where).' ORDER BY (ab.id IS NOT NULL) DESC,e.received_at DESC,e.id DESC LIMIT '.$limit
        );
        $stmt->execute($params);
        $cases=$stmt->fetchAll();
        if(!$cases)return[];

        $steamIds=array_values(array_unique(array_map(static fn(array $row):string=>(string)$row['steam_id64'],$cases)));
        $placeholders=implode(',',array_fill(0,count($steamIds),'?'));
        $eventHistory=self::eventHistoryForCases($steamIds,$placeholders);
        $banHistory=self::banHistoryForCases($steamIds,$placeholders);
        $eventLabels=['detected'=>'Automatisch erkannt','reported'=>'Zur Prüfung gemeldet','protected'=>'Werbeschutz aktiviert','banned'=>'Global gebannt','unbanned'=>'Global entbannt','false_positive'=>'Als Fehlalarm bewertet','resolved'=>'Prüfung abgeschlossen'];

        foreach($cases as &$case){
            $steam=(string)$case['steam_id64'];
            $timeline=[];
            foreach($eventHistory[$steam]??[] as $event){
                $origin=(string)($event['origin']??'automatic');
                $timeline[]=[
                    'kind'=>'detection','at'=>(string)$event['observed_at'],
                    'label'=>$origin==='manual_chat'?'Aus Chat gemeldet':'Werbung erkannt',
                    'detail'=>trim((string)($event['match_value']??'')),
                    'server_name'=>(string)($event['server_name']??''),'message'=>(string)($event['message_excerpt']??'')
                ];
                if(!empty($event['reviewed_at'])){
                    $eventStatus=(string)($event['review_status']??'detected');
                    $reviewer=trim((string)($event['reviewed_by_name']??''));
                    $note=trim((string)($event['review_note']??''));
                    $timeline[]=[
                        'kind'=>'review','at'=>(string)$event['reviewed_at'],
                        'label'=>$eventLabels[$eventStatus]??$eventStatus,
                        'detail'=>trim(($reviewer!==''?'von '.$reviewer:'').($reviewer!==''&&$note!==''?' · ':'').$note),
                        'server_name'=>'','message'=>''
                    ];
                }
            }
            foreach($banHistory[$steam]??[] as $ban){
                $banId=(int)$ban['id'];
                $actor=trim((string)($ban['created_by_name']??''));
                $reason=trim((string)($ban['reason_label']??'Globaler Ban'));
                $timeline[]=[
                    'kind'=>'ban','at'=>(string)$ban['starts_at'],'label'=>'Global gebannt · Ban #'.$banId,
                    'detail'=>$reason.($actor!==''?' · von '.$actor:''),'server_name'=>'','message'=>''
                ];
                if(empty($ban['active']) && !empty($ban['unbanned_at'])){
                    $unbanner=trim((string)($ban['unbanned_by_name']??''));
                    $unbanReason=trim((string)($ban['unban_reason']??''));
                    $timeline[]=[
                        'kind'=>'unban','at'=>(string)$ban['unbanned_at'],'label'=>'Global entbannt · Ban #'.$banId,
                        'detail'=>trim(($unbanner!==''?'von '.$unbanner:'').($unbanner!==''&&$unbanReason!==''?' · ':'').$unbanReason),
                        'server_name'=>'','message'=>''
                    ];
                }
            }
            usort($timeline,static function(array $a,array $b):int{
                $at=strtotime((string)($a['at']??''))?:0;$bt=strtotime((string)($b['at']??''))?:0;
                return$bt<=>$at;
            });
            $case['recent_actions']=array_slice($timeline,0,8);
            $case['event_history']=$eventHistory[$steam]??[];
            $case['ban_history']=$banHistory[$steam]??[];
        }
        unset($case);
        return$cases;
    }

    public static function countCasesByStatus(): array
    {
        $counts=array_fill_keys(self::REVIEW_STATUSES,0);
        $sql="SELECT CASE WHEN EXISTS(SELECT 1 FROM global_bans b WHERE b.steam_id64=e.steam_id64 AND b.active=1) THEN 'banned' ELSE e.review_status END AS case_status,COUNT(*) AS c " .
            "FROM global_advertising_events e WHERE e.steam_id64 IS NOT NULL AND TRIM(e.steam_id64)<>'' " .
            'AND e.id=(SELECT e2.id FROM global_advertising_events e2 WHERE e2.steam_id64=e.steam_id64 ORDER BY e2.received_at DESC,e2.id DESC LIMIT 1) GROUP BY case_status';
        foreach(Database::connection()->query($sql)->fetchAll() as $row){
            $key=(string)$row['case_status'];if(isset($counts[$key]))$counts[$key]=(int)$row['c'];
        }
        return$counts;
    }

    private static function eventHistoryForCases(array $steamIds,string $placeholders): array
    {
        $stmt=Database::connection()->prepare(
            'SELECT * FROM (SELECT e.*,gs.name AS server_name,vu.name AS reviewed_by_name,' .
            'ROW_NUMBER() OVER(PARTITION BY e.steam_id64 ORDER BY e.received_at DESC,e.id DESC) AS history_rank ' .
            'FROM global_advertising_events e LEFT JOIN game_servers gs ON gs.id=e.server_id LEFT JOIN users vu ON vu.id=e.reviewed_by ' .
            'WHERE e.steam_id64 IN ('.$placeholders.')) ranked WHERE history_rank<=8 ORDER BY received_at DESC,id DESC'
        );
        $stmt->execute($steamIds);$grouped=[];
        foreach($stmt->fetchAll() as $row)$grouped[(string)$row['steam_id64']][]=$row;
        return$grouped;
    }

    private static function banHistoryForCases(array $steamIds,string $placeholders): array
    {
        $stmt=Database::connection()->prepare(
            'SELECT * FROM (SELECT b.*,u.name AS created_by_name,uu.name AS unbanned_by_name,' .
            'ROW_NUMBER() OVER(PARTITION BY b.steam_id64 ORDER BY b.id DESC) AS history_rank ' .
            'FROM global_bans b LEFT JOIN users u ON u.id=b.created_by LEFT JOIN users uu ON uu.id=b.unbanned_by ' .
            'WHERE b.steam_id64 IN ('.$placeholders.')) ranked WHERE history_rank<=8 ORDER BY id DESC'
        );
        $stmt->execute($steamIds);$grouped=[];
        foreach($stmt->fetchAll() as $row)$grouped[(string)$row['steam_id64']][]=$row;
        return$grouped;
    }

    public static function countByStatus(): array
    {
        $counts=array_fill_keys(self::REVIEW_STATUSES,0);
        foreach(Database::connection()->query('SELECT review_status,COUNT(*) AS c FROM global_advertising_events GROUP BY review_status')->fetchAll() as $row){
            $key=(string)$row['review_status'];if(isset($counts[$key]))$counts[$key]=(int)$row['c'];
        }
        return $counts;
    }

    public static function linkBan(int $eventId, int $banId, ?int $reviewedBy): void
    {
        if($eventId<=0 || $banId<=0)return;
        $now=date('c');
        Database::connection()->prepare("UPDATE global_advertising_events SET global_ban_id=?,review_status='banned',reviewed_by=?,reviewed_at=? WHERE id=?")
            ->execute([$banId,$reviewedBy,$now,$eventId]);
    }

    /** Entbannt auch die zentrale Werbeschutz-Identität; eine neue Erkennung aktiviert sie erneut. */
    public static function releaseAfterUnban(int $banId, string $steamId64, string $reason, ?int $userId): void
    {
        if(!preg_match('/^7656119\d{10}$/',$steamId64))return;
        $now=date('c');$note=self::cut('Entbannt: '.trim($reason),500);
        $pdo=Database::connection();
        $pdo->prepare("UPDATE global_advertising_events SET review_status='unbanned',reviewed_by=?,reviewed_at=?,review_note=? WHERE global_ban_id=?")
            ->execute([$userId,$now,$note,$banId]);
        $pdo->prepare('UPDATE global_advertising_identities SET active=0 WHERE steam_id64=?')->execute([$steamId64]);
    }

    /** Sichtbare System-, manuelle und gelernte Regeln für die Moderationsoberfläche. */
    public static function rules(): array
    {
        $result=['blocked_domains'=>[],'allowed_domains'=>[],'blocked_phrases'=>[],'marketing_indicators'=>[]];
        $fixed=[
            'blocked_domain'=>self::DEFAULT_DOMAINS,
            'allowed_domain'=>self::DEFAULT_ALLOWED_DOMAINS,
        ];
        $bucket=['blocked_domain'=>'blocked_domains','allowed_domain'=>'allowed_domains','blocked_phrase'=>'blocked_phrases'];
        foreach($fixed as $type=>$values){
            foreach($values as $value){
                $result[$bucket[$type]][]=['rule_type'=>$type,'rule_value'=>$value,'source'=>'system','active'=>1,'fixed'=>1,'hit_count'=>0,'updated_at'=>null];
            }
        }
        foreach(self::DEFAULT_KEYWORDS as $value){
            $result['marketing_indicators'][]=['rule_type'=>'marketing_indicator','rule_value'=>$value,'source'=>'system','active'=>1,'fixed'=>1,'hit_count'=>0,'updated_at'=>null];
        }

        $fixedLookup=[];
        foreach($fixed as $type=>$values)foreach($values as $value)$fixedLookup[$type.'|'.$value]=true;
        try{
            $rows=Database::connection()->query('SELECT domain AS rule_value,source,active,hit_count,last_seen_at AS updated_at FROM global_advertising_domains ORDER BY active DESC,domain')->fetchAll();
            foreach($rows as $row){
                $value=self::normalizeDomain((string)$row['rule_value']);
                if($value===''||isset($fixedLookup['blocked_domain|'.$value]))continue;
                $result['blocked_domains'][]=['rule_type'=>'blocked_domain','rule_value'=>$value,'source'=>(string)$row['source'],'active'=>(int)$row['active'],'fixed'=>0,'hit_count'=>(int)$row['hit_count'],'updated_at'=>$row['updated_at']];
            }
        }catch(\Throwable){}
        try{
            $rows=Database::connection()->query('SELECT rule_type,rule_value,source,active,updated_at FROM global_advertising_rules ORDER BY active DESC,rule_type,rule_value')->fetchAll();
            foreach($rows as $row){
                $type=(string)$row['rule_type'];$value=(string)$row['rule_value'];
                if(!isset($bucket[$type])||isset($fixedLookup[$type.'|'.$value]))continue;
                $result[$bucket[$type]][]=['rule_type'=>$type,'rule_value'=>$value,'source'=>(string)$row['source'],'active'=>(int)$row['active'],'fixed'=>0,'hit_count'=>0,'updated_at'=>$row['updated_at']];
            }
        }catch(\Throwable){}
        return$result;
    }

    public static function addRule(string $type,string $value,?int $userId): void
    {
        $type=self::assertRuleType($type);$value=self::normalizeRuleValue($type,$value);
        if(self::isFixedRule($type,$value))throw new \RuntimeException('Diese Systemregel ist bereits dauerhaft vorhanden.');
        $pdo=Database::connection();$now=date('c');
        if($type==='blocked_domain'){
            $pdo->prepare(
                "INSERT INTO global_advertising_domains(domain,source,first_server_id,last_server_id,first_seen_at,last_seen_at,hit_count,active) VALUES(?,'manual',NULL,NULL,?,?,0,1) " .
                "ON CONFLICT(domain) DO UPDATE SET source='manual',active=1,last_seen_at=excluded.last_seen_at"
            )->execute([$value,$now,$now]);
        }else{
            $pdo->prepare(
                "INSERT INTO global_advertising_rules(rule_type,rule_value,source,active,created_by,updated_by,created_at,updated_at) VALUES(?,?,'manual',1,?,?,?,?) " .
                "ON CONFLICT(rule_type,rule_value) DO UPDATE SET source='manual',active=1,updated_by=excluded.updated_by,updated_at=excluded.updated_at"
            )->execute([$type,$value,$userId,$userId,$now,$now]);
        }
        Audit::log('anti_advertisement.rule.add','advertising_rule',$type.':'.$value,['rule_type'=>$type,'rule_value'=>$value]);
    }

    public static function setRuleActive(string $type,string $value,bool $active,?int $userId): void
    {
        $type=self::assertRuleType($type);$value=self::normalizeRuleValue($type,$value);
        if(self::isFixedRule($type,$value))throw new \RuntimeException('Feste Systemregeln können nicht deaktiviert werden.');
        $pdo=Database::connection();
        if($type==='blocked_domain'){
            $stmt=$pdo->prepare("UPDATE global_advertising_domains SET active=?,source='manual',last_seen_at=? WHERE domain=?");
            $stmt->execute([$active?1:0,date('c'),$value]);
        }else{
            $stmt=$pdo->prepare("UPDATE global_advertising_rules SET active=?,updated_by=?,updated_at=? WHERE rule_type=? AND rule_value=?");
            $stmt->execute([$active?1:0,$userId,date('c'),$type,$value]);
        }
        if($stmt->rowCount()===0)throw new \RuntimeException('Werbeschutzregel wurde nicht gefunden.');
        Audit::log('anti_advertisement.rule.toggle','advertising_rule',$type.':'.$value,['rule_type'=>$type,'rule_value'=>$value,'active'=>$active]);
    }

    public static function activeSnapshot(): array
    {
        $pdo=Database::connection();
        $ids=[];
        try{
            $q=$pdo->query("SELECT steam_id64 FROM global_advertising_identities WHERE active=1 ORDER BY last_seen_at DESC LIMIT 5000");
            foreach($q->fetchAll() as $r){$v=(string)$r['steam_id64'];if($v!=='')$ids[]=$v;}
        }catch(\Throwable){}
        $domains=self::DEFAULT_DOMAINS;
        try{
            $q=$pdo->query("SELECT domain FROM global_advertising_domains WHERE active=1 ORDER BY domain LIMIT 2000");
            foreach($q->fetchAll() as $r){$v=strtolower(trim((string)$r['domain']));if($v!=='')$domains[]=$v;}
        }catch(\Throwable){}
        $allowed=self::DEFAULT_ALLOWED_DOMAINS;$blockedPhrases=[];
        try{
            $q=$pdo->query("SELECT rule_type,rule_value FROM global_advertising_rules WHERE active=1 ORDER BY rule_type,rule_value LIMIT 4000");
            foreach($q->fetchAll() as $r){
                $type=(string)$r['rule_type'];$value=trim((string)$r['rule_value']);if($value==='')continue;
                if($type==='allowed_domain')$allowed[]=$value;
                elseif($type==='blocked_phrase')$blockedPhrases[]=$value;
            }
        }catch(\Throwable){}
        return [
            'version'=>2,'generated_at'=>gmdate('c'),'blocked_steam_ids'=>array_values(array_unique($ids)),
            'domains'=>array_values(array_unique($domains)),'allowed_domains'=>array_values(array_unique($allowed)),
            'blocked_phrases'=>array_values(array_unique($blockedPhrases)),
            'marketing_keywords'=>self::DEFAULT_KEYWORDS,
        ];
    }

    private static function identityUpsertStatement(): \PDOStatement
    {
        return Database::connection()->prepare(
            'INSERT INTO global_advertising_identities(steam_id64,player_name,match_value,first_server_id,last_server_id,first_seen_at,last_seen_at,hit_count,active) VALUES(?,?,?,?,?,?,?,1,?) ' .
            'ON CONFLICT(steam_id64) DO UPDATE SET player_name=excluded.player_name,match_value=excluded.match_value,last_server_id=excluded.last_server_id,last_seen_at=excluded.last_seen_at,hit_count=global_advertising_identities.hit_count+1,active=MAX(global_advertising_identities.active,excluded.active)'
        );
    }

    private static function domainUpsertStatement(): \PDOStatement
    {
        return Database::connection()->prepare(
            "INSERT INTO global_advertising_domains(domain,source,first_server_id,last_server_id,first_seen_at,last_seen_at,hit_count,active) VALUES(?,'learned',?,?,?,?,1,1) " .
            "ON CONFLICT(domain) DO UPDATE SET last_server_id=excluded.last_server_id,last_seen_at=excluded.last_seen_at,hit_count=global_advertising_domains.hit_count+1,active=CASE WHEN global_advertising_domains.source='manual' THEN global_advertising_domains.active ELSE 1 END"
        );
    }

    private static function refreshIdentityActivity(string $steamId64): void
    {
        $stmt=Database::connection()->prepare("SELECT COUNT(*) FROM global_advertising_events WHERE steam_id64=? AND review_status IN ('detected','protected','banned')");
        $stmt->execute([$steamId64]);$active=(int)$stmt->fetchColumn()>0?1:0;
        Database::connection()->prepare('UPDATE global_advertising_identities SET active=? WHERE steam_id64=?')->execute([$active,$steamId64]);
    }

    private static function refreshDomainActivity(string $domain): void
    {
        $active=0;
        $rows=Database::connection()->query("SELECT match_value FROM global_advertising_events WHERE review_status IN ('detected','protected','banned')")->fetchAll();
        foreach($rows as $row){
            if(self::extractDomain((string)($row['match_value']??''))===$domain){$active=1;break;}
        }
        Database::connection()->prepare("UPDATE global_advertising_domains SET active=? WHERE domain=? AND source<>'manual'")->execute([$active,$domain]);
    }

    private static function assertRuleType(string $type): string
    {
        $type=strtolower(trim($type));
        if(!in_array($type,self::RULE_TYPES,true))throw new \InvalidArgumentException('Ungültiger Werbeschutz-Regeltyp.');
        return$type;
    }

    private static function normalizeRuleValue(string $type,string $value): string
    {
        if($type==='blocked_domain'||$type==='allowed_domain'){
            $value=self::normalizeDomain($value);
            if($value===''||strlen($value)>253||!preg_match('/^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$/',$value))
                throw new \InvalidArgumentException('Bitte eine gültige Domain ohne Pfad eingeben.');
            return$value;
        }
        $value=strtolower(trim(preg_replace('/\s+/u',' ',$value)??''));
        $length=function_exists('mb_strlen')?mb_strlen($value):strlen($value);
        if($length<3||$length>120)throw new \InvalidArgumentException('Die Werbephrase muss zwischen 3 und 120 Zeichen lang sein.');
        return$value;
    }

    private static function normalizeDomain(string $value): string
    {
        $value=strtolower(trim($value));
        $value=preg_replace('#^https?://#','',$value)??$value;
        if(str_starts_with($value,'www.'))$value=substr($value,4);
        $value=preg_split('~[/?#\s]~',$value,2)[0]??'';
        return rtrim($value,'.');
    }

    private static function isFixedRule(string $type,string $value): bool
    {
        $fixed=$type==='blocked_domain'?self::DEFAULT_DOMAINS:($type==='allowed_domain'?self::DEFAULT_ALLOWED_DOMAINS:[]);
        return in_array($value,$fixed,true);
    }

    private static function extractDomain(string $value): ?string
    {
        $v=strtolower(trim($value));$v=str_replace(['[.]','(.)',' . ',' dot '],'.',$v);
        if(preg_match('/(?:^|[^a-z0-9-])((?:[a-z0-9-]+\.)+[a-z]{2,24})(?:$|[^a-z0-9-])/i',' '.$v.' ',$m))return rtrim(strtolower($m[1]),'.');
        if(preg_match('/^(?:[a-z0-9-]+\.)+[a-z]{2,24}$/i',$v))return rtrim($v,'.');
        return null;
    }

    private static function assertSteamId(string $steamId64): void
    {
        if(!preg_match('/^7656119\d{10}$/',$steamId64))throw new \InvalidArgumentException('Für die Meldung fehlt eine gültige SteamID64.');
    }

    private static function cut(string $value,int $max): string
    {
        return function_exists('mb_substr')?mb_substr($value,0,$max):substr($value,0,$max);
    }
}
