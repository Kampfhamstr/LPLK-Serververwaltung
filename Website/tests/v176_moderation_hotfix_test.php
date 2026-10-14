<?php
$root=dirname(__DIR__);
function ok176(bool $c,string $m):void{if(!$c){fwrite(STDERR,"FEHLER: {$m}\n");exit(1);}echo "OK  {$m}\n";}
$version=trim((string)file_get_contents($root.'/VERSION'));
$view=(string)file_get_contents($root.'/src/Core/View.php');
$controller=(string)file_get_contents($root.'/src/Controllers/ModerationController.php');
$service=(string)file_get_contents($root.'/src/Services/ModerationService.php');
$tpl=(string)file_get_contents($root.'/templates/moderation.php');
$css=(string)file_get_contents($root.'/public/assets/app.css');
ok176(version_compare($version,'1.7.6','>='),'Webpanel-Version ist 1.7.6 oder neuer');
ok176(str_contains($view,'array $viewData = []') && str_contains($view,'extract($viewData, EXTR_SKIP)'), 'View-Renderer hat keine data-Namenskollision mehr');
ok176(str_contains($controller,"'moderationData'=>\$moderationData"),'Moderationscontroller verwendet eindeutigen Datenvertrag');
ok176(str_contains($tpl,"\$moderationData['warnings'] ?? null") && str_contains($tpl,"\$moderationData['sanctions'] ?? null"),'Moderationstemplate besitzt defensive Defaults');
ok176(str_contains($service,"'notes'=>\$notes") && str_contains($service,"'evidence'=>\$evidence"),'Moderationsdashboard liefert Notizen und Beweise');
ok176(str_contains($tpl,'Interne Moderator-Notizen') && str_contains($tpl,'<h2>Beweise</h2>'),'Notizen und Beweise werden im Zentrum angezeigt');
ok176(str_contains($css,'.moderation-summary-card.state-warning') && str_contains($css,'.moderation-section-head'),'Moderationsdesign ist farbcodiert und kachelbasiert');
echo "\nv1.7.6 Moderations-Hotfix-Test erfolgreich.\n";
