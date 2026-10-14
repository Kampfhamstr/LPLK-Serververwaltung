<?php
$root=dirname(__DIR__);
$template=file_get_contents($root.'/templates/server_live.php');
$js=file_get_contents($root.'/public/assets/app.js');

function ok142(bool $ok,string $message):void{
    if(!$ok) throw new RuntimeException($message);
    echo "OK  {$message}\n";
}

ok142(str_contains($template,'data-map-search-reset'),'Reset-Button ist im Mapformular vorhanden');
ok142(str_contains($template,'type="button" data-map-search-reset'),'Reset-Button kann das Formular nicht absenden');
ok142(str_contains($template,'Enter filtert nur und startet keinen Mapwechsel'),'Oberfläche erklärt Enter-Verhalten');
ok142(str_contains($js,"if(event.key==='Enter')"),'Enter wird im Suchfeld abgefangen');
ok142(str_contains($js,'event.preventDefault()'),'Impliziter Submit wird verhindert');
ok142(str_contains($js,"if(event.key==='Escape')"),'Esc-Reset ist implementiert');
ok142(str_contains($js,'resetButton?.addEventListener'),'Reset-Button ist verdrahtet');
ok142(str_contains($js,'document.activeElement===input'),'Zusätzlicher Formular-Submit-Guard vorhanden');
ok142(str_contains($js,"if(select.selectedOptions[0]?.disabled) select.value=''"),'Unsichtbare alte Mapauswahl wird entfernt');

echo "\nv1.4.2 Map-Suche-Test erfolgreich.\n";
