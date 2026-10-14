<?php
// Offline read-only planner. Never opens an application or game database.
require dirname(__DIR__).'/src/Services/BankNetworkPlan.php';
if(PHP_SAPI!=='cli'||$argc!==3){fwrite(STDERR,"Aufruf: php bank-migration-plan.php bestand.json pruefplan.json\n");exit(2);}
try {
    if(!is_file($argv[1])||is_link($argv[1])||filesize($argv[1])>67108864)throw new RuntimeException('Ungültige Bestandsdatei.');
    $input=json_decode(file_get_contents($argv[1]),true,32,JSON_THROW_ON_ERROR);
    if(!is_array($input)||!array_is_list($input))throw new RuntimeException('Bestandsliste erwartet.');
    $plan=App\Services\BankNetworkPlan::inventory($input);
    $handle=fopen($argv[2],'x');if(!$handle)throw new RuntimeException('Ausgabe muss eine neue Datei sein.');
    $json=json_encode($plan,JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR)."\n";
    if(fwrite($handle,$json)!==strlen($json))throw new RuntimeException('Ausgabe unvollständig.');fclose($handle);
    echo "Prüfplan erzeugt. Keine Guthaben importiert; Umschaltung gesperrt.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n");exit(1);}
