<?php
$root=dirname(__DIR__);
function ok193(bool $condition,string $label):void{
    if(!$condition){fwrite(STDERR,"FEHLER: {$label}\n");exit(1);}
    echo "OK  {$label}\n";
}
function source193(string $root,string $file):string{
    $value=@file_get_contents($root.'/'.$file);
    if($value===false){fwrite(STDERR,"FEHLER: Datei fehlt: {$file}\n");exit(1);}
    return$value;
}

$routes=source193($root,'public/index.php');
$schema=source193($root,'migrations/schema.sql');
$migration=source193($root,'bin/migrate.php');
$antiAd=source193($root,'src/Services/AntiAdvertisementService.php');
$banService=source193($root,'src/Services/GlobalBanService.php');
$moderationService=source193($root,'src/Services/ModerationService.php');
$moderation=source193($root,'templates/moderation.php');
$chat=source193($root,'templates/server_chat.php');
$banForm=source193($root,'templates/ban_form.php');

foreach(['origin','review_status','chat_message_id','global_ban_id','reported_by','reviewed_by','reviewed_at','review_note'] as $column){
    ok193(str_contains($schema,$column),"Werbeereignis-Spalte {$column} im Schema");
    ok193(str_contains($migration,"'{$column}'"),"Upgrade ergänzt {$column} idempotent");
}
ok193(str_contains($routes,'/moderation/advertising/report'),'Route für manuelle Chatmeldung vorhanden');
ok193(str_contains($routes,'/moderation/advertising/{id}/review'),'Route für Werbefall-Prüfung vorhanden');
ok193(str_contains($chat,'Werbung melden')&&str_contains($chat,'chat_message_id'),'Chat kann mit unverändertem Beleg gemeldet werden');
ok193(str_contains($antiAd,'reportChatMessage')&&str_contains($antiAd,"'reported'"),'Manuelle Meldung landet zunächst in der Prüfwarteschlange');
ok193(str_contains($antiAd,'reviewEvent')&&str_contains($antiAd,"'false_positive'"),'Fehlalarm-Workflow vorhanden');
ok193(str_contains($antiAd,'linkBan')&&str_contains($banService,'advertisingEventId'),'Werbeereignis und globaler Ban werden verknüpft');
ok193(str_contains($banForm,'Werbeschutz-Ereignis'),'Banformular zeigt den Werbeschutz-Beleg');
ok193(str_contains($moderation,'Werbeschutz-Fälle'),'Werbefälle sind in Moderation sichtbar');
ok193(str_contains($moderation,'Ban- und Entbannungshistorie'),'Ban-/Unban-Historie ist in Moderation sichtbar');
ok193(str_contains($banService,'releaseAfterUnban'),'Entbannung löst verknüpfte Werbeschutz-Identität');
ok193(str_contains($moderationService,"'bans'=>\$bans")&&str_contains($moderationService,"'advertising_cases'=>\$advertisingCases"),'Moderationsdashboard lädt Bans und zusammengeführte Werbefälle gemeinsam');
ok193(str_contains($moderationService,'e.value_text AS value'),'Beweistext verwendet den tatsächlichen Schema-Spaltennamen');

echo "\nv1.9.3 Werbeschutz-Moderation und Historie erfolgreich geprüft.\n";
