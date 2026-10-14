<?php
$root=dirname(__DIR__);
function ok175(bool $condition,string $label):void{if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}echo "OK  {$label}\n";}
$version=trim((string)file_get_contents($root.'/VERSION'));
$routes=(string)file_get_contents($root.'/public/index.php');
$live=(string)file_get_contents($root.'/src/Controllers/LiveController.php');
$moderation=(string)file_get_contents($root.'/src/Services/ModerationService.php');
$modController=(string)file_get_contents($root.'/src/Controllers/ModerationController.php');
$tpl=(string)file_get_contents($root.'/templates/server_live.php');
$layout=(string)file_get_contents($root.'/templates/layout.php');
$js=(string)file_get_contents($root.'/public/assets/app.js');
$css=(string)file_get_contents($root.'/public/assets/app.css');

ok175(version_compare($version,'1.7.5','>='),'Webpanel-Version ist 1.7.5 oder neuer');
ok175(str_contains($routes,"/live/rule-quick"),'Schnellregel-Route vorhanden');
ok175(str_contains($live,'public function quickRule'),'Schnellregel-Controller vorhanden');
ok175(str_contains($live,"'friendly_fire'=>\$value === '1'") && str_contains($live,"'global_speed'=>"),'Schnellregeln ändern einzelne Werte ohne Komplettreset');
ok175(str_contains($tpl,'data-rule-dashboard') && str_contains($tpl,'data-rule-card'),'Live-Istzustand nutzt interaktive Regelkacheln');
ok175(str_contains($tpl,'$bridgeVersion'),'Bridge-Version wird dynamisch aus Status gelesen');
ok175(!str_contains($tpl,'Von Bridge v1.5.3 direkt'),'Veralteter harter Bridge-v1.5.3-Hinweis entfernt');
ok175(str_contains($css,'.rule-state-active') && str_contains($css,'.rule-state-custom'),'Aktiv-/Sonderzustände sind farbcodiert');
ok175(str_contains($js,"contextmenu") && str_contains($js,'[data-rule-card]'),'Links-/Rechtsklick-Schnellwahl ist implementiert');
ok175(str_contains($moderation,'public static function statusMap'),'Moderationsstatus wird serverseitig gebündelt');
ok175(str_contains($routes,"/moderation/statuses"),'Batch-Route für Moderationsmarker vorhanden');
ok175(str_contains($modController,'public function statuses'),'Moderationsstatus-API vorhanden');
ok175(str_contains($js,'player-restriction-ban') && str_contains($js,'player-restriction-warning') && str_contains($js,'player-restriction-silence'),'BAN/WARN/MUTE/GAG/SILENCE-Marker werden im UI erzeugt');
ok175(str_contains($layout,'data-player-action-restrictions'),'Spieleraktionsdialog besitzt Restriktionsübersicht');
ok175(str_contains($css,'.player-action-dialog{width:min(1080px,96vw)'),'Spieleraktionsdialog wurde für fehlerhafte/enge Layouts korrigiert');

echo "\nv1.7.5 Active-State/Moderation-Badges-Test erfolgreich.\n";
