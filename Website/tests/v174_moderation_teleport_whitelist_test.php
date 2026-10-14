<?php
$root=dirname(__DIR__);
function ok174(bool $ok,string $msg):void{if(!$ok){fwrite(STDERR,"FEHLER: $msg\n");exit(1);}echo "OK  $msg\n";}
$version=trim((string)file_get_contents($root.'/VERSION'));
ok174(version_compare($version,'1.7.4','>='),'Webpanel-Version ist 1.7.4 oder neuer');
$schema=(string)file_get_contents($root.'/migrations/schema.sql');
foreach(['moderation_warnings','moderation_notes','moderation_evidence','moderation_appeals','global_communication_sanctions'] as $table) ok174(str_contains($schema,'CREATE TABLE IF NOT EXISTS '.$table),'Schema enthält '.$table);
$fast=(string)file_get_contents($root.'/src/Services/FastLiveService.php');
ok174(str_contains($fast,"'global_comm_sanctions'=>ModerationService::activeCommunicationPayload()"),'Fast Live liefert globale Kommunikationssperren');
ok174(str_contains($fast,'recentCommands'),'Fast Live stellt echtes Ausführungsfeedback bereit');
$live=(string)file_get_contents($root.'/src/Controllers/LiveController.php');
ok174(str_contains($live,"'teleport_player' => 'live.teleport'")||str_contains($live,"'goto_player','bring_player','teleport_player' => 'live.teleport'"),'Teleport Player ist berechtigt');
ok174(str_contains($live, "\$payload['source_steamid']") && str_contains($live, "\$payload['destination_steamid']"),'Teleport validiert Quelle und Ziel');
$tpl=(string)file_get_contents($root.'/templates/server_live_player.php');
ok174(str_contains($tpl,'name="source_steamid"')&&str_contains($tpl,'name="destination_steamid"'),'Teleport-UI hat freie Quelle und Ziel');
ok174(str_contains($tpl,'$ownSteamId'),'Eigenes Steam-Konto wird für die Vorauswahl berücksichtigt');
$pac=(string)file_get_contents($root.'/src/Controllers/PlayerActionsController.php');
ok174(str_contains($pac,"'whitelist_add' => 'live.whitelist'"),'Globale Spieleraktionskachel darf serverbezogen whitelisten');
ok174(str_contains($pac,'WhitelistLiveService::addOne($serverId, $steamId64)'),'Whitelist-Zugriff verwendet explizite Server-ID');
$layout=(string)file_get_contents($root.'/templates/layout.php');
ok174(str_contains($layout,'data-player-whitelist-server'),'Whitelist-Serverauswahl ist in der Spieleraktionskachel vorhanden');
ok174(str_contains($layout,'/moderation/warnings')&&str_contains($layout,'/moderation/sanctions')&&str_contains($layout,'/moderation/notes'),'Moderationsaktionen sind aus der Spieleraktionskachel erreichbar');
$auth=(string)file_get_contents($root.'/src/Core/Auth.php');
foreach(['moderation.view','moderation.warn','moderation.notes','moderation.evidence','moderation.appeals','moderation.global_comms'] as $perm) ok174(str_contains($auth,$perm),'Berechtigung vorhanden: '.$perm);
echo "\nv1.7.4 Moderation/Teleport/Whitelist-Test erfolgreich.\n";
