<?php
$root=dirname(__DIR__);
$template=file_get_contents($root.'/templates/server_live.php');
$css=file_get_contents($root.'/public/assets/app.css');

function ok155(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok155(str_contains($template,'class="whitelist-current-details"'),'Aktuelle Whitelist steckt in einem Details-Element');
ok155(!str_contains($template,'<details class="whitelist-current-details" open>'),'Liste ist standardmäßig geschlossen');
ok155(str_contains($template,'-Spieler anzeigen'),'Aufklapptext ist vorhanden');
ok155(str_contains($template,'<?=count($whitelistManager[\'entries\']??[])?>'),'Anzahl der Einträge bleibt sichtbar');
ok155(str_contains($template,'data-whitelist-current-search'),'Suche bleibt innerhalb der aufgeklappten Liste verfügbar');
ok155(str_contains($template,'Markierte entfernen'),'Entfernen-Funktion bleibt vorhanden');
ok155(str_contains($css,'.whitelist-current-details>summary'),'CSS für kompakten Aufklappkopf vorhanden');

echo "\nv1.5.5 Whitelist-Collapse-Test erfolgreich.\n";
