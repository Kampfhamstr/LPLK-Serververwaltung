<?php
$root=dirname(__DIR__);
$settings=file_get_contents($root.'/templates/settings.php');
$github=file_get_contents($root.'/src/Services/GitHubReleaseService.php');
$plugins=file_get_contents($root.'/templates/plugins.php');

function ok151(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok151(str_starts_with(ltrim($settings),'<?php use App\\Core\\Security;')&&str_contains($settings,'use App\\Core\\Auth;'),'Security- und Auth-Import stehen vor dem GitHub-Block');
ok151(substr_count($settings,'use App\\Core\\Security;')===1,'Security-Import ist genau einmal vorhanden');
ok151(str_contains($settings,'id="github"'),'GitHub-Einstellungsbereich vorhanden');
ok151(str_contains($github,'parse_url($value)'),'Vollständige GitHub-Release-Links werden robust verarbeitet');
ok151(str_contains($plugins,'Nicht geprüft'),'Plugin ohne Quelle wird als Nicht geprüft angezeigt');

echo "\nv1.5.1 GitHub-Settings-Hotfix-Test erfolgreich.\n";
