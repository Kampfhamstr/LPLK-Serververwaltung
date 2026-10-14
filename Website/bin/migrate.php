#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);
$root = dirname(__DIR__);
require $root . '/src/bootstrap.php';
use App\Core\Database;
use App\Services\FastLiveService;

$pdo = Database::connection();
$avatarDir = $root . '/storage/steam-avatars';
if (!is_dir($avatarDir)) @mkdir($avatarDir, 0770, true);
$additions = [
    'community_role' => "TEXT NOT NULL DEFAULT 'spieler'",
    'status' => "TEXT NOT NULL DEFAULT 'active'",
    'first_name' => 'TEXT NULL',
    'last_name' => 'TEXT NULL',
    'email' => 'TEXT NULL',
    'phone' => 'TEXT NULL',
    'discord_name' => 'TEXT NULL',
    'discord_id' => 'TEXT NULL',
    'notes' => 'TEXT NULL',
    'joined_at' => 'TEXT NULL',
    'left_at' => 'TEXT NULL',
    'steam_personaname' => 'TEXT NULL',
    'steam_profile_url' => 'TEXT NULL',
    'steam_avatar_url' => 'TEXT NULL',
    'steam_avatar_medium_url' => 'TEXT NULL',
    'steam_avatar_full_url' => 'TEXT NULL',
    'steam_visibility' => 'INTEGER NULL',
    'steam_personastate' => 'INTEGER NULL',
    'steam_lastlogoff' => 'INTEGER NULL',
    'steam_last_synced_at' => 'TEXT NULL',
    'steam_sync_error' => 'TEXT NULL',
    'main_player_id' => 'INTEGER NULL REFERENCES game_players(id) ON DELETE SET NULL',
];

$communityRoleAdded = false;
$tableExists = (bool)$pdo->query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='game_players'")->fetchColumn();
if ($tableExists) {
    $columns = [];
    foreach ($pdo->query("PRAGMA table_info(game_players)")->fetchAll() as $column) $columns[(string)$column['name']] = true;
    foreach ($additions as $name => $definition) {
        if (!isset($columns[$name])) {
            $pdo->exec('ALTER TABLE game_players ADD COLUMN ' . $name . ' ' . $definition);
            if ($name === 'community_role') $communityRoleAdded = true;
            echo "Spalte ergänzt: game_players.$name\n";
        }
    }
}

// v1.6.8: Fast-Live-Schalter und pro Server verschlüsseltes Bridge-Geheimnis.
// Auf einer frischen Installation existiert game_servers vor dem Schema-Import noch nicht;
// ALTER TABLE darf daher nur bei einem echten Upgrade ausgeführt werden.
$gameServersExists168 = (bool)$pdo->query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='game_servers'")->fetchColumn();
$fastLiveEnabledAdded168 = false;
if ($gameServersExists168) {
    $serverColumns168 = [];
    foreach ($pdo->query("PRAGMA table_info(game_servers)")->fetchAll() as $column) {
        $serverColumns168[(string)$column['name']] = true;
    }
    if (!isset($serverColumns168['bridge_fastlive_enabled'])) {
        $pdo->exec("ALTER TABLE game_servers ADD COLUMN bridge_fastlive_enabled INTEGER NOT NULL DEFAULT 1");
        $fastLiveEnabledAdded168 = true;
        echo "Spalte ergänzt: game_servers.bridge_fastlive_enabled\n";
    }
    if (!isset($serverColumns168['bridge_fastlive_secret_enc'])) {
        $pdo->exec("ALTER TABLE game_servers ADD COLUMN bridge_fastlive_secret_enc TEXT NULL");
        echo "Spalte ergänzt: game_servers.bridge_fastlive_secret_enc\n";
    }
}

$schema = file_get_contents($root . '/migrations/schema.sql');

$pdo->exec($schema);

// v1.9.3: Moderationsworkflow für automatische Werbeereignisse und manuelle
// Chatmeldungen. CREATE TABLE IF NOT EXISTS ergänzt bei SQLite keine Spalten,
// deshalb werden bestehende Installationen hier idempotent erweitert.
$advertisingEventColumns193 = [];
foreach ($pdo->query("PRAGMA table_info(global_advertising_events)")->fetchAll() as $column) {
    $advertisingEventColumns193[(string)$column['name']] = true;
}
foreach ([
    'origin' => "TEXT NOT NULL DEFAULT 'automatic'",
    'review_status' => "TEXT NOT NULL DEFAULT 'detected'",
    'chat_message_id' => 'INTEGER NULL',
    'global_ban_id' => 'INTEGER NULL',
    'reported_by' => 'INTEGER NULL',
    'reviewed_by' => 'INTEGER NULL',
    'reviewed_at' => 'TEXT NULL',
    'review_note' => 'TEXT NULL',
] as $column193 => $definition193) {
    if (!isset($advertisingEventColumns193[$column193])) {
        $pdo->exec('ALTER TABLE global_advertising_events ADD COLUMN ' . $column193 . ' ' . $definition193);
        echo "Spalte ergänzt: global_advertising_events.$column193\n";
    }
}
$pdo->exec('CREATE INDEX IF NOT EXISTS idx_global_ad_events_review ON global_advertising_events(review_status,received_at DESC,id DESC)');
$pdo->exec('CREATE INDEX IF NOT EXISTS idx_global_ad_events_ban ON global_advertising_events(global_ban_id,id DESC)');

// v1.7.9: zusätzliche Spalten für 2FA-Pflicht und zeitlich begrenzte Serverrechte.
$roleTemplateColumns179 = [];
foreach ($pdo->query("PRAGMA table_info(web_role_templates)")->fetchAll() as $column) $roleTemplateColumns179[(string)$column['name']] = true;
if (!isset($roleTemplateColumns179['require_2fa'])) {
    $pdo->exec("ALTER TABLE web_role_templates ADD COLUMN require_2fa INTEGER NOT NULL DEFAULT 0");
    echo "Spalte ergänzt: web_role_templates.require_2fa\n";
}
$usaColumns179 = [];
foreach ($pdo->query("PRAGMA table_info(user_server_access)")->fetchAll() as $column) $usaColumns179[(string)$column['name']] = true;
foreach ([
    'expires_at'=>'TEXT NULL',
    'reason'=>'TEXT NULL',
    'updated_by'=>'INTEGER NULL',
] as $column179=>$definition179) {
    if (!isset($usaColumns179[$column179])) {
        $pdo->exec('ALTER TABLE user_server_access ADD COLUMN '.$column179.' '.$definition179);
        echo "Spalte ergänzt: user_server_access.$column179\n";
    }
}

// v1.7.9: editierbare Webpanel-Rollenvorlagen. Bestehende Vorlagen und
// individuelle Nutzer-/Serverrechte werden bei Updates ausdrücklich erhalten.
$roleTemplateDefaults179 = [
    'founder' => [
        'permissions'=>['*'],
        'server_caps'=>['view'=>true,'players'=>true,'permissions'=>true,'sync'=>true,'live'=>true],
        'max_game_role'=>'founder',
        'require_2fa'=>1,
    ],
    'admin' => [
        'permissions'=>[
            'dashboard.view','dashboard.announce','community.chat.view','community.chat.post','community.chat.moderate','community.forum.view','community.forum.post','community.forum.moderate','users.view','users.invite','users.manage','users.permissions','users.bulk','users.preview','roles.templates.manage','roles.packages.manage','roles.export_import',
            'players.view','players.manage','players.import','players.export','players.steam.sync','players.contacts.view','players.contacts.manage',
            'servers.view','servers.manage','servers.access_groups.manage','servers.sync','server.resources.view','server.power.start','server.power.restart','server.power.stop','server.power.kill','server.console.view','server.console.execute','plugins.view','plugins.manage','plugins.repository.manage','plugins.update','plugins.rollback','plugins.rescan',
            'live.view','live.kick','live.ban','live.mute','live.gag','live.silence','live.slay','live.freeze','live.respawn','live.health','live.armor','live.weapons','live.whitelist','live.team','live.noclip','live.teleport','live.nickname','live.message','live.map','live.restart','live.console','live.cvar','live.bots','live.bot.kick','live.bot.slay','live.bot.team','live.rules','live.speed','live.gravity','live.godmode','live.weapon_mode','live.presets',
            'moderation.view','moderation.advertising.rules','moderation.warn','moderation.notes','moderation.evidence','moderation.appeals','moderation.global_comms','moderation.ban.temporary','moderation.ban.permanent','moderation.unban','audit.view','settings.view','settings.manage',
        ],
        'server_caps'=>['view'=>true,'players'=>true,'permissions'=>true,'sync'=>true,'live'=>true],
        'max_game_role'=>'member',
        'require_2fa'=>0,
    ],
    'member' => [
        'permissions'=>['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post','players.view','servers.view','live.view','moderation.view'],
        'server_caps'=>['view'=>true,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
        'max_game_role'=>'vip',
        'require_2fa'=>0,
    ],
    'vip' => [
        'permissions'=>['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post','players.view'],
        'server_caps'=>['view'=>false,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
        'max_game_role'=>'spieler',
        'require_2fa'=>0,
    ],
    'spieler' => [
        'permissions'=>['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post'],
        'server_caps'=>['view'=>false,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
        'max_game_role'=>'spieler',
        'require_2fa'=>0,
    ],
];
$insertTemplate179 = $pdo->prepare(
    'INSERT OR IGNORE INTO web_role_templates(role_name,permissions_json,server_caps_json,max_game_role,require_2fa,updated_at) VALUES(?,?,?,?,?,?)'
);
foreach ($roleTemplateDefaults179 as $role179 => $template179) {
    $insertTemplate179->execute([
        $role179,
        json_encode($template179['permissions'], JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),
        json_encode($template179['server_caps'], JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),
        $template179['max_game_role'],
        (int)($template179['require_2fa'] ?? 0),
        date('c'),
    ]);
}

// v1.8.0: neue Pterodactyl-Rechte konservativ in bestehende editierte
// Rollenvorlagen uebernehmen. Bestehende Anpassungen bleiben erhalten:
// Ressourcen folgen live.view, Neustart folgt live.restart, Konsole folgt
// live.console. Nur die Admin-Vorlage erhaelt Start/Stop/Kill, wenn sie
// zuvor bereits den vollen Gameserver-Neustart durfte.
try {
    $templates180 = $pdo->query('SELECT role_name,permissions_json FROM web_role_templates')->fetchAll();
    $update180 = $pdo->prepare('UPDATE web_role_templates SET permissions_json=?,updated_at=? WHERE role_name=?');
    foreach ($templates180 as $row180) {
        $role180 = strtolower(trim((string)($row180['role_name'] ?? '')));
        if ($role180 === 'founder') continue;
        $permissions180 = json_decode((string)($row180['permissions_json'] ?? '[]'), true);
        if (!is_array($permissions180)) $permissions180 = [];
        $permissions180 = array_values(array_unique(array_filter($permissions180, 'is_string')));
        $before180 = $permissions180;

        $add180 = static function(string $permission) use (&$permissions180): void {
            if (!in_array($permission, $permissions180, true)) $permissions180[] = $permission;
        };

        if (in_array('live.view', $permissions180, true)) {
            $add180('server.resources.view');
        }
        if (in_array('live.restart', $permissions180, true)) {
            $add180('server.power.restart');
            if ($role180 === 'admin') {
                $add180('server.power.start');
                $add180('server.power.stop');
                $add180('server.power.kill');
            }
        }
        if (in_array('live.console', $permissions180, true)) {
            $add180('server.console.view');
            $add180('server.console.execute');
        }

        if ($permissions180 !== $before180) {
            $update180->execute([
                json_encode($permissions180, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),
                date('c'),
                $role180,
            ]);
            echo "v1.8.0-Rechte ergaenzt: web_role_templates.$role180\n";
        }
    }
} catch (Throwable $e) {
    echo "Hinweis: v1.8.0-Rollenvorlagen konnten nicht automatisch ergaenzt werden: {$e->getMessage()}\n";
}


// v1.8.2: Einzel-Bot-Rechte aus dem bisherigen Sammelrecht ableiten.
// Eigene, bereits gesetzte neue Einzelrechte bleiben unverändert.
try {
    $templates182 = $pdo->query('SELECT role_name,permissions_json FROM web_role_templates')->fetchAll();
    $update182 = $pdo->prepare('UPDATE web_role_templates SET permissions_json=?,updated_at=? WHERE role_name=?');
    foreach ($templates182 as $row182) {
        $role182 = strtolower(trim((string)($row182['role_name'] ?? '')));
        if ($role182 === 'founder') continue;
        $permissions182 = json_decode((string)($row182['permissions_json'] ?? '[]'), true);
        if (!is_array($permissions182)) $permissions182 = [];
        $permissions182 = array_values(array_unique(array_filter($permissions182, 'is_string')));
        $before182 = $permissions182;
        if (in_array('live.bots', $permissions182, true)) {
            foreach (['live.bot.kick','live.bot.slay','live.bot.team'] as $permission182) {
                if (!in_array($permission182, $permissions182, true)) $permissions182[] = $permission182;
            }
        }
        if ($permissions182 !== $before182) {
            $update182->execute([
                json_encode($permissions182, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),
                date('c'),
                $role182,
            ]);
            echo "v1.8.2-Bot-Rechte ergaenzt: web_role_templates.$role182\n";
        }
    }
} catch (Throwable $e) {
    echo "Hinweis: v1.8.2-Bot-Rechte konnten nicht automatisch ergaenzt werden: {$e->getMessage()}\n";
}

// v1.6.9: Fast Live ist für vorhandene Server beim erstmaligen Upgrade standardmäßig aktiv.
// Das Geheimnis wird einmalig serverbezogen erzeugt. Bereits bewusst deaktivierte
// v1.6.8-Installationen werden bei einem erneuten migrate nicht zwangsweise reaktiviert.
if ($gameServersExists168) {
    $serverRows169 = $pdo->query('SELECT id,bridge_fastlive_enabled,bridge_fastlive_secret_enc FROM game_servers ORDER BY id')->fetchAll();
    foreach ($serverRows169 as $serverRow169) {
        $needsSecret169 = trim((string)($serverRow169['bridge_fastlive_secret_enc'] ?? '')) === '';
        if ($fastLiveEnabledAdded168 || ($needsSecret169 && !empty($serverRow169['bridge_fastlive_enabled']))) {
            FastLiveService::ensureEnabled((int)$serverRow169['id']);
            echo "Fast Live vorbereitet: Server #" . (int)$serverRow169['id'] . "\n";
        }
    }
}

// v1.3.3: Sitzungs-Version für globale Session-Ungültigmachung nach Passwort-Reset.
$userColumns = [];
foreach ($pdo->query("PRAGMA table_info(users)")->fetchAll() as $column) {
    $userColumns[(string)$column['name']] = true;
}
if (!isset($userColumns['auth_version'])) {
    $pdo->exec("ALTER TABLE users ADD COLUMN auth_version INTEGER NOT NULL DEFAULT 1");
    echo "Spalte ergänzt: users.auth_version\n";
}
$pdo->exec("UPDATE users SET auth_version=1 WHERE auth_version IS NULL OR auth_version<1");

// v1.8.1: einmal verwendbare 2FA-Recovery-Codes (nur gehashte Werte in der Datenbank).
$userColumns181 = [];
foreach ($pdo->query("PRAGMA table_info(users)")->fetchAll() as $column) {
    $userColumns181[(string)$column['name']] = true;
}
if (!isset($userColumns181['totp_recovery_codes_json'])) {
    $pdo->exec("ALTER TABLE users ADD COLUMN totp_recovery_codes_json TEXT NULL");
    echo "Spalte ergänzt: users.totp_recovery_codes_json\n";
}

// v1.8.4: persönliche Darstellung (Hell / Dunkel / System) pro Panelkonto.
$userColumns184 = [];
foreach ($pdo->query("PRAGMA table_info(users)")->fetchAll() as $column) {
    $userColumns184[(string)$column['name']] = true;
}
if (!isset($userColumns184['ui_theme'])) {
    $pdo->exec("ALTER TABLE users ADD COLUMN ui_theme TEXT NOT NULL DEFAULT 'system'");
    echo "Spalte ergänzt: users.ui_theme\n";
}
$pdo->exec("UPDATE users SET ui_theme='system' WHERE ui_theme IS NULL OR lower(ui_theme) NOT IN ('light','dark','system')");

// v1.4.0/v1.15.0: Steam-Verifizierung, Webpanel-Präsenz und Abrufdrosselung.
$userColumns140 = [];
foreach ($pdo->query("PRAGMA table_info(users)")->fetchAll() as $column) {
    $userColumns140[(string)$column['name']] = true;
}
$userAddColumns140 = [
    'steam_id64' => 'TEXT NULL',
    'steam_personaname' => 'TEXT NULL',
    'steam_profile_url' => 'TEXT NULL',
    'steam_avatar_url' => 'TEXT NULL',
    'steam_verified_at' => 'TEXT NULL',
    'steam_profile_synced_at' => 'TEXT NULL',
    'steam_profile_refresh_attempted_epoch' => 'INTEGER NULL',
    'last_seen_at' => 'TEXT NULL',
];
foreach ($userAddColumns140 as $column => $definition) {
    if (!isset($userColumns140[$column])) {
        $pdo->exec('ALTER TABLE users ADD COLUMN ' . $column . ' ' . $definition);
        echo "Spalte ergänzt: users.{$column}\n";
    }
}
$pdo->exec("CREATE UNIQUE INDEX IF NOT EXISTS idx_users_steam_id64_unique ON users(steam_id64) WHERE steam_id64 IS NOT NULL AND trim(steam_id64)<>''");

// v1.15.0: v1.14.0 konnte bei fehlenden Profildaten einen künstlichen Namen
// "Steam <letzte 6 Ziffern>" speichern. Dieser Platzhalter darf nie als
// öffentlicher Besitzername gelten und wird bis zum echten Profilabruf geleert.
$cleanupLegacySteamNames1150 = $pdo->prepare(
    "UPDATE users
     SET steam_personaname=NULL,steam_profile_synced_at=NULL,updated_at=?
     WHERE length(trim(COALESCE(steam_id64,'')))=17
       AND trim(steam_id64) GLOB '7656119[0-9]*'
       AND trim(steam_id64) NOT GLOB '*[^0-9]*'
       AND trim(COALESCE(steam_personaname,'')) COLLATE BINARY = ('Steam ' || substr(trim(steam_id64),-6))
       AND trim(COALESCE(steam_profile_url,''))=''
       AND trim(COALESCE(steam_avatar_url,''))=''"
);
$cleanupLegacySteamNames1150->execute([date('c')]);
if ($cleanupLegacySteamNames1150->rowCount() > 0) {
    echo "Veraltete künstliche Steam-Namen entfernt: " . $cleanupLegacySteamNames1150->rowCount() . "\n";
}


// v1.2.0: getrennte Freigabe für Live-Administration pro Gameserver.
$usaExists = (bool)$pdo->query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='user_server_access'")->fetchColumn();
if ($usaExists) {
    $usaColumns = [];
    foreach ($pdo->query("PRAGMA table_info(user_server_access)")->fetchAll() as $column) $usaColumns[(string)$column['name']] = true;
    if (!isset($usaColumns['can_live_admin'])) {
        $pdo->exec("ALTER TABLE user_server_access ADD COLUMN can_live_admin INTEGER NOT NULL DEFAULT 0");
        echo "Spalte ergänzt: user_server_access.can_live_admin
";
    }
}
$pdo->exec("UPDATE game_players SET community_role='spieler' WHERE community_role IS NULL OR community_role='' ");
if ($communityRoleAdded) {
    // Bei einem Upgrade übernimmt die zentrale Rolle einmalig die höchste bereits bekannte Serverrolle.
    $pdo->exec("UPDATE game_players SET community_role = COALESCE((SELECT spg.role_name FROM server_player_groups spg WHERE spg.player_id=game_players.id ORDER BY CASE spg.role_name WHEN 'founder' THEN 5 WHEN 'admin' THEN 4 WHEN 'member' THEN 3 WHEN 'vip' THEN 2 ELSE 1 END DESC LIMIT 1), community_role)");
}
$pdo->exec("UPDATE game_players SET status='active' WHERE status IS NULL OR status='' ");

$pdo->exec("UPDATE game_players SET display_name=steam_personaname WHERE steam_personaname IS NOT NULL AND trim(steam_personaname)<>''");

// v1.7.9: fünf Webpanel-Rollen und granulare Rechte. Anders als die alte
// v1.3.2-Migration werden permissions_json und user_server_access NICHT gelöscht.
$pdo->exec("UPDATE users SET web_role='spieler' WHERE lower(web_role) NOT IN ('spieler','vip','member','admin','founder')");
$pdo->exec("UPDATE invitations SET web_role='spieler' WHERE accepted_at IS NULL AND lower(web_role) NOT IN ('spieler','vip','member','admin','founder')");

// v1.9.5: Admins erhalten die neue granulare Werbeschutz-Regelverwaltung.
// Bestehende individuelle Änderungen anderer Rollen bleiben unangetastet.
try {
    $stmt195=$pdo->prepare("SELECT permissions_json FROM web_role_templates WHERE role_name='admin' LIMIT 1");
    $stmt195->execute();$raw195=$stmt195->fetchColumn();
    if($raw195!==false){
        $permissions195=json_decode((string)$raw195,true);
        if(!is_array($permissions195))$permissions195=[];
        if(!in_array('moderation.advertising.rules',$permissions195,true)){
            $permissions195[]='moderation.advertising.rules';
            $pdo->prepare("UPDATE web_role_templates SET permissions_json=?,updated_at=? WHERE role_name='admin'")
                ->execute([json_encode(array_values(array_unique($permissions195)),JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),date('c')]);
            echo "v1.9.5-Recht ergänzt: moderation.advertising.rules für Admin\n";
        }
    }
}catch(Throwable $e){echo "Hinweis: v1.9.5-Werbeschutzrecht konnte nicht automatisch ergänzt werden: {$e->getMessage()}\n";}

// v1.10.0: Pets für alle bisherigen Community-Rollenvorlagen freischalten;
// die Verwaltung bleibt auf Admin beschränkt. Individuelle Denys bleiben wirksam.
try {
    $templates1100=$pdo->query('SELECT role_name,permissions_json FROM web_role_templates')->fetchAll();
    $update1100=$pdo->prepare('UPDATE web_role_templates SET permissions_json=?,updated_at=? WHERE role_name=?');
    foreach($templates1100 as $row1100){
        $role1100=strtolower((string)$row1100['role_name']);
        if($role1100==='founder')continue;
        $permissions1100=json_decode((string)$row1100['permissions_json'],true);
        if(!is_array($permissions1100))$permissions1100=[];
        $before1100=$permissions1100;
        if(!in_array('pets.use',$permissions1100,true))$permissions1100[]='pets.use';
        if($role1100==='admin'&&!in_array('pets.manage',$permissions1100,true))$permissions1100[]='pets.manage';
        if($permissions1100!==$before1100)$update1100->execute([json_encode(array_values(array_unique($permissions1100)),JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE),date('c'),$role1100]);
    }
}catch(Throwable $e){echo "Hinweis: v1.10.0-Pets-Rechte konnten nicht automatisch ergänzt werden: {$e->getMessage()}\n";}

// v1.13.0: Jedes bereits vorhandene Pet erhält genau ein persönliches
// Starter-Ausrüstungsteil. Bei einer erneut ausgeführten Migration bleibt eine
// inzwischen vom Spieler gewählte Charm-Ausrüstung unangetastet.
$starterCreatedAt1130=date('c');
$seedStarter1130=$pdo->prepare(<<<'SQL'
INSERT OR IGNORE INTO pet_items(
    user_id,pet_id,template_key,item_name,slot,rarity,item_level,level_required,
    strength_bonus,agility_bonus,vitality_bonus,instinct_bonus,equipped,
    source_type,source_id,source_index,created_at
)
SELECT
    p.user_id,p.id,'adventurer_scarf','Abenteurer-Halstuch','charm','common',1,1,
    0,1,0,1,
    CASE WHEN EXISTS(
        SELECT 1 FROM pet_items equipped_charm
        WHERE equipped_charm.pet_id=p.id
          AND equipped_charm.user_id=p.user_id
          AND equipped_charm.slot='charm'
          AND equipped_charm.equipped=1
    ) THEN 0 ELSE 1 END,
    'starter',p.id,0,?
FROM pets p
WHERE NOT EXISTS(
    SELECT 1 FROM pet_items starter
    WHERE starter.source_type='starter'
      AND starter.source_id=p.id
      AND starter.source_index=0
)
SQL);
$seedStarter1130->execute([$starterCreatedAt1130]);
echo "Pet-Abenteuer v1.13.0: ".(int)$seedStarter1130->rowCount()." bestehende Pets mit Abenteurer-Halstuch vorbereitet.\n";

echo "Granulare Webpanel-Rollen/Rechte v1.7.9 sind aktiv; bestehende Nutzer- und Serverfreigaben wurden erhalten.\n";
echo "Passwort-Wiederherstellung und Sitzungs-Versionierung v1.3.3 sind aktiv.\n";
echo "Steam-Verifizierung, Webpanel-Präsenz und Announcements v1.4.0 sind aktiv.\n";
echo "Dashboard-Chat und internes Forum v1.4.1 sind aktiv.\n";
echo "Plugin Manager & GitHub Updates v1.5.0 sind aktiv.\n";
echo "Webpanel-Nutzerleiste und Direktnachrichten v1.6.0 sind aktiv.\n";
echo "Fast-Live-Push, lokale Command-Queue und dauerhaftes Serverchat-Archiv sind aktiv.\n";
echo "Fast Live Auto-Provisioning v1.6.9 ist aktiv.\n";
echo "Globale Banverwaltung v1.7.3 ist aktiv.\n";
echo "Moderationszentrum, globale Kommunikationssperren und Beweisablage v1.7.4 sind aktiv.\n";
echo "2FA-QR-Code, Recovery-Codes und Founder-2FA-Sicherheitsmodus v1.8.1 sind aktiv.\n";
echo "Modernes Hell-/Dunkel-/System-Design v1.8.4 ist pro Panelkonto gespeichert.\n";
echo "Zentrale Werbeschutz-Regelverwaltung v1.9.5 ist aktiv.\n";
echo "Dashboard-Power-Schnelltasten v1.9.6 sind aktiv; eine neue Datenbankmigration ist nicht erforderlich.\n";
echo "Pets-MVP v1.10.0 mit Pflege, XP, Wallet und Tagesaufgaben ist aktiv.\n";
echo "Pet-Abenteuer, Ausrüstung, Skillbaum und PvE-Arena v1.13.0 sind aktiv.\n";
echo "Pet-Spielwiese mit Namen, Leveln, Besitzern und Paaranimationen v1.14.0 ist aktiv.\n";
echo "Steam-geschützte Spielwiese und Pet-Aktionsanimationen v1.15.2.8 sind aktiv.\n";
echo "Datenbankschema ist mit Serververwaltung v1.15.2.8 kompatibel.\n";
