#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);

$root=dirname(__DIR__);
$ok=true;

foreach(['pdo_sqlite','curl','sodium','openssl'] as $extension){
    $state=extension_loaded($extension);
    echo str_pad($extension,28).($state?'OK':'FEHLT').PHP_EOL;
    $ok=$ok&&$state;
}
echo str_pad('ssh2 (optional)',28).(extension_loaded('ssh2')?'OK':'nicht installiert').PHP_EOL;
echo str_pad('ftp (optional)',28).(extension_loaded('ftp')?'OK':'nicht installiert').PHP_EOL;

foreach(['config/app.php','storage/database','storage/backups','storage/logs','storage/steam-avatars'] as $relative){
    $path=$root.'/'.$relative;
    $state=file_exists($path);
    echo str_pad($relative,28).($state?'OK':'FEHLT').PHP_EOL;
    $ok=$ok&&$state;
}

if($ok){
    require $root.'/src/bootstrap.php';
    $pdo=\App\Core\Database::connection();
    $petTables=[
        'pets','pet_actions','pet_action_cooldowns','pet_wallets',
        'pet_wallet_ledger','pet_tasks','pet_inventory',
        'pet_adventures','pet_items','pet_skill_ranks','pet_skill_spends',
        'pet_arena_profiles','pet_arena_battles','pet_playground_interactions',
    ];
    $tableStates=[];
    $tableStatement=$pdo->prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?");
    foreach($petTables as $table){
        $tableStatement->execute([$table]);
        $state=(bool)$tableStatement->fetchColumn();
        $tableStates[$table]=$state;
        echo str_pad('pets:'.$table,28).($state?'OK':'FEHLT').PHP_EOL;
        $ok=$ok&&$state;
    }

    if($tableStates['pet_wallets']){
        $invalidWallets=(int)$pdo->query('SELECT COUNT(*) FROM pet_wallets WHERE coins<0 OR training_points<0')->fetchColumn();
        echo str_pad('pets wallet',28).($invalidWallets===0?'OK':'UNGÜLTIG ('.$invalidWallets.')').PHP_EOL;
        $ok=$ok&&$invalidWallets===0;
    }

    $ownershipTables=['pet_adventures','pet_items','pet_skill_ranks','pet_skill_spends','pet_arena_profiles','pet_arena_battles'];
    $ownerMismatches=0;
    foreach($ownershipTables as $table){
        if(!$tableStates[$table]) continue;
        $ownerMismatches+=(int)$pdo->query(
            "SELECT COUNT(*) FROM {$table} child
             LEFT JOIN pets owner_pet ON owner_pet.id=child.pet_id AND owner_pet.user_id=child.user_id
             WHERE owner_pet.id IS NULL"
        )->fetchColumn();
    }
    echo str_pad('pets ownership',28).($ownerMismatches===0?'OK':'UNGÜLTIG ('.$ownerMismatches.')').PHP_EOL;
    $ok=$ok&&$ownerMismatches===0;

    if($tableStates['pet_playground_interactions']){
        $playgroundMismatches=(int)$pdo->query(
            "SELECT COUNT(*) FROM pet_playground_interactions event
             LEFT JOIN pets actor ON actor.id=event.actor_pet_id AND actor.user_id=event.actor_user_id
             LEFT JOIN pets target ON target.id=event.target_pet_id AND target.user_id=event.target_user_id
             WHERE actor.id IS NULL OR target.id IS NULL
                OR event.actor_pet_id=event.target_pet_id
                OR event.actor_user_id=event.target_user_id
                OR event.expires_at_epoch<=event.created_at_epoch"
        )->fetchColumn();
        echo str_pad('pets playground',28).($playgroundMismatches===0?'OK':'UNGÜLTIG ('.$playgroundMismatches.')').PHP_EOL;
        $ok=$ok&&$playgroundMismatches===0;
    }

    if($tableStates['pet_items']){
        $duplicateSlots=(int)$pdo->query(
            "SELECT COUNT(*) FROM (
                SELECT pet_id,slot
                FROM pet_items
                WHERE equipped=1
                GROUP BY pet_id,slot
                HAVING COUNT(*)>1
            ) duplicate_equipment"
        )->fetchColumn();
        echo str_pad('pets equipped slots',28).($duplicateSlots===0?'OK':'DOPPELT ('.$duplicateSlots.')').PHP_EOL;
        $ok=$ok&&$duplicateSlots===0;
    }
}

exit($ok?0:1);
