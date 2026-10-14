<?php
namespace App\Core;

final class Auth
{
    public const PERMISSIONS = [
        'dashboard.view' => 'Dashboard ansehen',
        'dashboard.announce' => 'Webpanel-Mitteilungen veröffentlichen',
        'community.chat.view' => 'Internen Dashboard-Chat lesen',
        'community.chat.post' => 'Im internen Dashboard-Chat schreiben',
        'community.chat.moderate' => 'Internen Dashboard-Chat moderieren',
        'community.forum.view' => 'Internes Forum lesen',
        'community.forum.post' => 'Im internen Forum schreiben',
        'community.forum.moderate' => 'Internes Forum moderieren',
        'pets.use' => 'Eigenes Pet adoptieren und pflegen',
        'pets.manage' => 'Pets-Modul verwalten und moderieren',
        'users.view' => 'Webpanel-Nutzer ansehen',
        'users.invite' => 'Nutzer per E-Mail einladen',
        'users.manage' => 'Webpanel-Nutzer verwalten',
        'users.permissions' => 'Individuelle Panel- und Serverrechte verwalten',
        'users.bulk' => 'Mehrere Panel-Nutzer gemeinsam bearbeiten',
        'users.preview' => 'Effektive Rechte anderer Nutzer simulieren',
        'roles.templates.manage' => 'Rollenvorlagen unterhalb der eigenen Rolle bearbeiten',
        'roles.packages.manage' => 'Rechtepakete erstellen und zuweisen',
        'roles.export_import' => 'Rollenvorlagen exportieren und importieren',
        'economy.manage' => 'Bankguthaben verwalten (mit Spieler- und Serverrechten)',
        'players.view' => 'SteamID-Spieler ansehen',
        'players.manage' => 'Spieler, Rollen und Servergruppen verwalten',
        'players.import' => 'Whitelist/SteamIDs importieren',
        'players.export' => 'Spielerlisten exportieren',
        'players.steam.sync' => 'Steam-Profile synchronisieren',
        'players.contacts.view' => 'Kontaktdaten von Personen ansehen',
        'players.contacts.manage' => 'Kontaktdaten von Personen bearbeiten',
        'servers.view' => 'Server ansehen',
        'servers.manage' => 'Server, Verbindungen und Gruppenrechte verwalten',
        'servers.access_groups.manage' => 'Servergruppen für Panelzugriffe verwalten',
        'servers.sync' => 'Berechtigungen synchronisieren',
        'server.resources.view' => 'CPU, RAM, Speicher, Netzwerk und Laufzeit eines Servers live ansehen',
        'server.power.start' => 'Gameserver über Pterodactyl starten',
        'server.power.restart' => 'Gameserver über Pterodactyl neu starten',
        'server.power.stop' => 'Gameserver über Pterodactyl sauber stoppen',
        'server.power.kill' => 'Gameserver über Pterodactyl hart beenden (Kill)',
        'server.console.view' => 'Serverkonsole bzw. Konsolenbereich ansehen',
        'server.console.execute' => 'Serverbefehle über die Serverkonsole ausführen',
        'plugins.view' => 'SwiftlyS2 Plugins und Versionen ansehen',
        'plugins.manage' => 'Alle Plugin-Verwaltungsrechte (Kompatibilität)',
        'plugins.repository.manage' => 'Plugin-Repositorys und Updatequellen verwalten',
        'plugins.update' => 'Plugin-Updates installieren',
        'plugins.rollback' => 'Plugin-Rollbacks ausführen',
        'plugins.rescan' => 'Plugin-Inventar neu einlesen',
        'live.view' => 'Live-Spieler und Serverstatus ansehen',
        'live.kick' => 'Spieler live kicken',
        'live.ban' => 'Spieler live bannen/entbannen',
        'live.mute' => 'Voicechat live muten/unmuten',
        'live.gag' => 'Textchat live sperren/freigeben',
        'live.silence' => 'Voice- und Textchat gemeinsam sperren',
        'live.slay' => 'Spieler live töten',
        'live.freeze' => 'Spieler freezen/unfreezen',
        'live.respawn' => 'Spieler respawnen',
        'live.health' => 'Spieler-Health setzen',
        'live.armor' => 'Spieler-Armor setzen',
        'live.weapons' => 'Waffen geben/entfernen',
        'live.whitelist' => 'Whitelist/Blacklist des laufenden Servers verwalten',
        'live.team' => 'Spieler Team zuordnen',
        'live.noclip' => 'Noclip live ein-/ausschalten',
        'live.teleport' => 'Spieler live teleportieren/holen',
        'live.nickname' => 'Server-Nickname setzen/zurücksetzen',
        'live.message' => 'Server- und Spielernachrichten senden',
        'live.map' => 'Map-/Rundenwechsel ausführen',
        'live.restart' => 'Gameserver neu starten',
        'live.console' => 'Serverkonsole/RCON-ähnliche Befehle ausführen',
        'live.cvar' => 'Server-ConVars setzen',
        'live.bots' => 'Bots live als Gruppe verwalten',
        'live.bot.kick' => 'Einzelnen Bot live kicken',
        'live.bot.slay' => 'Einzelnen Bot live töten',
        'live.bot.team' => 'Einzelnen Bot live ins Team verschieben',
        'live.rules' => 'Server-Spielregeln live verwalten',
        'live.speed' => 'Spieler- und Serverspeed live verwalten',
        'live.gravity' => 'Spieler- und Servergravity live verwalten',
        'live.godmode' => 'Godmode live ein-/ausschalten',
        'live.weapon_mode' => 'Weapon-Restricted-Modi live verwalten',
        'live.presets' => 'Live-Regelpresets anwenden und speichern',
        'moderation.view' => 'Moderationszentrum ansehen',
        'moderation.advertising.rules' => 'Globale Werbeschutzregeln verwalten',
        'moderation.warn' => 'Spieler verwarnen und Verwarnungen erledigen',
        'moderation.notes' => 'Interne Moderator-Notizen verwalten',
        'moderation.evidence' => 'Moderationsbeweise hinterlegen',
        'moderation.appeals' => 'Appeals prüfen und bearbeiten',
        'moderation.global_comms' => 'Globale Mutes/Gags/Silence verwalten',
        'moderation.ban.temporary' => 'Globale zeitliche Bans erstellen/bearbeiten',
        'moderation.ban.permanent' => 'Globale permanente Bans erstellen/bearbeiten',
        'moderation.unban' => 'Globale Bans aufheben',
        'audit.view' => 'Audit-Log ansehen',
        'settings.view' => 'Systemeinstellungen ansehen',
        'settings.manage' => 'Systemeinstellungen und API-/SMTP-Konfiguration ändern',
    ];

    public const WEB_ROLES = [
        'spieler' => 'Spieler',
        'vip' => 'VIP',
        'member' => 'Member',
        'admin' => 'Admin',
        'founder' => 'Founder',
    ];

    public const ROLE_LEVELS = [
        'spieler'=>100,
        'vip'=>200,
        'member'=>300,
        'admin'=>400,
        'founder'=>500,
    ];

    public const GAME_ROLE_LEVELS = [
        'spieler'=>1,
        'vip'=>2,
        'member'=>3,
        'admin'=>4,
        'founder'=>5,
    ];

    /**
     * Sichere Defaults, die nur verwendet werden, solange noch keine editierbare
     * Vorlage in web_role_templates gespeichert wurde. Founder bleibt technisch
     * immer Vollzugriff und kann nicht durch eine Vorlage eingeschränkt werden.
     */
    private const ROLE_DEFAULTS = [
        'founder' => ['*'],
        'admin' => [
            'dashboard.view','dashboard.announce','community.chat.view','community.chat.post','community.chat.moderate','community.forum.view','community.forum.post','community.forum.moderate','pets.use','pets.manage',
            'users.view','users.invite','users.manage','users.permissions','users.bulk','users.preview','roles.templates.manage','roles.packages.manage','roles.export_import',
            'players.view','players.manage','players.import','players.export','players.steam.sync','players.contacts.view','players.contacts.manage',
            'servers.view','servers.manage','servers.access_groups.manage','servers.sync','server.resources.view','server.power.start','server.power.restart','server.power.stop','server.power.kill','server.console.view','server.console.execute',
            'plugins.view','plugins.manage','plugins.repository.manage','plugins.update','plugins.rollback','plugins.rescan',
            'live.view','live.kick','live.ban','live.mute','live.gag','live.silence','live.slay','live.freeze','live.respawn','live.health','live.armor','live.weapons','live.whitelist','live.team','live.noclip','live.teleport','live.nickname','live.message','live.map','live.restart','live.console','live.cvar','live.bots','live.bot.kick','live.bot.slay','live.bot.team','live.rules','live.speed','live.gravity','live.godmode','live.weapon_mode','live.presets',
            'moderation.view','moderation.advertising.rules','moderation.warn','moderation.notes','moderation.evidence','moderation.appeals','moderation.global_comms','moderation.ban.temporary','moderation.ban.permanent','moderation.unban',
            'audit.view','settings.view','settings.manage',
        ],
        'member' => ['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post','pets.use','players.view','servers.view','live.view','moderation.view'],
        'vip' => ['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post','pets.use','players.view'],
        'spieler' => ['dashboard.view','community.chat.view','community.chat.post','community.forum.view','community.forum.post','pets.use'],
    ];

    private const ROLE_SERVER_DEFAULTS = [
        'founder' => ['view'=>true,'players'=>true,'permissions'=>true,'sync'=>true,'live'=>true],
        'admin' => ['view'=>true,'players'=>true,'permissions'=>true,'sync'=>true,'live'=>true],
        'member' => ['view'=>true,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
        'vip' => ['view'=>false,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
        'spieler' => ['view'=>false,'players'=>false,'permissions'=>false,'sync'=>false,'live'=>false],
    ];

    private const ROLE_MAX_GAME_ROLE = [
        'founder'=>'founder',
        'admin'=>'member',
        'member'=>'vip',
        'vip'=>'spieler',
        'spieler'=>'spieler',
    ];

    public static function user(): ?array
    {
        static $cached = false;
        static $user = null;
        if ($cached) return $user;
        $cached = true;
        $id = $_SESSION['user_id'] ?? null;
        if (!$id) return null;
        $stmt = Database::connection()->prepare('SELECT * FROM users WHERE id = ? AND active = 1');
        $stmt->execute([(int)$id]);
        $row = $stmt->fetch();
        if (!$row) {
            unset($_SESSION['user_id'], $_SESSION['auth_version']);
            return null;
        }

        $accountVersion = max(1, (int)($row['auth_version'] ?? 1));
        $sessionVersion = isset($_SESSION['auth_version']) ? (int)$_SESSION['auth_version'] : 0;
        if ($sessionVersion !== $accountVersion) {
            unset($_SESSION['user_id'], $_SESSION['auth_version'], $_SESSION['_csrf']);
            return null;
        }

        // Presence ist nur eine Komfortfunktion. Ein paralleler SQLite-Writer
        // darf daher niemals die eigentliche Benutzeranfrage mit "database is locked"
        // abbrechen. 60 s reichen bei einem 180-s-Onlinefenster vollständig aus.
        if (!isset($_SESSION['_presence_touch']) || time() - (int)$_SESSION['_presence_touch'] >= 60) {
            $seenAt = date('c');
            try {
                Database::withBusyRetry(static function () use ($seenAt, $row): void {
                    Database::connection()->prepare('UPDATE users SET last_seen_at=? WHERE id=?')
                        ->execute([$seenAt, (int)$row['id']]);
                }, 3);
                $row['last_seen_at'] = $seenAt;
            } catch (\Throwable) {
                // Best effort: Bei hoher Live-Last wird der Presence-Touch beim
                // nächsten Intervall nachgeholt, die Seite bleibt aber benutzbar.
            }
            $_SESSION['_presence_touch'] = time();
        }

        $user = $row;
        return $user;
    }

    public static function check(): bool { return self::user() !== null; }

    public static function requireLogin(): void
    {
        if (!self::check()) {
            header('Location: /login');
            exit;
        }
    }

    public static function can(string $permission): bool
    {
        $user = self::user();
        if (!$user) return false;
        return self::userCan($user, $permission);
    }

    public static function canAny(array $permissions): bool
    {
        foreach ($permissions as $permission) {
            if (is_string($permission) && self::can($permission)) return true;
        }
        return false;
    }

    public static function userCan(array $user, string $permission): bool
    {
        return (bool)self::permissionDecisionForUser($user, $permission)['allowed'];
    }

    /**
     * Ermittelt nicht nur Ja/Nein, sondern auch die Herkunft des effektiven Rechts.
     * Konfliktregel: Ein ausdrückliches Verbot gewinnt vor jeder Erlaubnis.
     */
    public static function permissionDecisionForUser(array $user, string $permission): array
    {
        $storedRole = self::normalizeWebRole((string)($user['web_role'] ?? 'spieler'));
        $founderLocked = self::founderTwoFactorLocked($user);
        $role = self::effectiveWebRoleForUser($user);
        if (array_key_exists('active',$user) && (int)$user['active'] !== 1) {
            return ['allowed'=>false,'source'=>'Benutzerkonto ist gesperrt','source_type'=>'account_disabled'];
        }
        if (!array_key_exists($permission, self::PERMISSIONS)) {
            return ['allowed'=>false,'source'=>'Unbekanntes/verwaistes Recht','source_type'=>'orphan'];
        }

        // Founder ohne 2FA bleibt arbeitsfähig, wird aber hart auf die editierbare
        // Admin-Vorlage begrenzt. Erst nach erfolgreicher 2FA gilt Founder-Vollzugriff.
        if ($founderLocked) {
            $template = self::roleTemplate('admin');
            $adminAllowed = in_array('*', $template['permissions'], true)
                || in_array($permission, $template['permissions'], true);
            if (!$adminAllowed) {
                return [
                    'allowed'=>false,
                    'source'=>'Founder-Sicherheitsmodus: Admin-Vorlage enthält dieses Recht nicht',
                    'source_type'=>'founder_2fa_admin_cap',
                ];
            }
            // Auch im Sicherheitsmodus bleibt die zentrale Konfliktregel erhalten:
            // ein ausdrückliches Deny schlägt die geerbte Admin-Erlaubnis.
            $overrides = self::permissionOverrides($user['permissions_json'] ?? null);
            if (array_key_exists($permission, $overrides) && $overrides[$permission] === false) {
                return ['allowed'=>false,'source'=>'Individuell ausdrücklich verboten · Founder-Sicherheitsmodus','source_type'=>'user_deny'];
            }
            $temporary = self::activeTemporaryPermissionDecisions((int)($user['id'] ?? 0));
            if (isset($temporary[$permission]['deny'])) {
                return ['allowed'=>false,'source'=>'Temporär verboten bis '.$temporary[$permission]['deny'].' · Founder-Sicherheitsmodus','source_type'=>'temporary_deny'];
            }
            return [
                'allowed'=>true,
                'source'=>'Founder-Sicherheitsmodus: Admin-Vorlage bis 2FA aktiviert ist',
                'source_type'=>'founder_2fa_admin_cap',
            ];
        }

        if (self::twoFactorSetupRequired($user) && $permission !== 'dashboard.view') {
            return ['allowed'=>false,'source'=>'2FA für diese Rollenvorlage erforderlich','source_type'=>'security'];
        }

        if ($storedRole === 'founder') {
            return ['allowed'=>true,'source'=>'Founder-Vollzugriff · 2FA bestätigt','source_type'=>'founder'];
        }

        $overrides = self::permissionOverrides($user['permissions_json'] ?? null);
        if (array_key_exists($permission, $overrides) && $overrides[$permission] === false) {
            return ['allowed'=>false,'source'=>'Individuell ausdrücklich verboten','source_type'=>'user_deny'];
        }

        $temporary = self::activeTemporaryPermissionDecisions((int)($user['id'] ?? 0));
        if (isset($temporary[$permission]['deny'])) {
            return ['allowed'=>false,'source'=>'Temporär verboten bis '.$temporary[$permission]['deny'],'source_type'=>'temporary_deny'];
        }

        if (array_key_exists($permission, $overrides) && $overrides[$permission] === true) {
            return ['allowed'=>true,'source'=>'Individuell ausdrücklich erlaubt','source_type'=>'user_allow'];
        }

        if (isset($temporary[$permission]['allow'])) {
            return ['allowed'=>true,'source'=>'Temporär erlaubt bis '.$temporary[$permission]['allow'],'source_type'=>'temporary_allow'];
        }

        $package = self::packageGrantSource((int)($user['id'] ?? 0), $permission);
        if ($package !== null) {
            return ['allowed'=>true,'source'=>'Rechtepaket: '.$package,'source_type'=>'package'];
        }

        // Abwärtskompatibilität zu Installationen vor v1.7.9: alte Sammelrechte
        // bleiben wirksam. Ein explizites Verbot des neuen Einzelrechts wurde
        // bereits weiter oben ausgewertet und hat daher weiterhin Vorrang.
        $legacyUmbrellas = [
            'plugins.repository.manage'=>'plugins.manage',
            'plugins.update'=>'plugins.manage',
            'plugins.rollback'=>'plugins.manage',
            'plugins.rescan'=>'plugins.manage',
            'moderation.ban.temporary'=>'live.ban',
            'moderation.ban.permanent'=>'live.ban',
            'moderation.unban'=>'live.ban',
            'server.resources.view'=>'live.view',
            'server.power.restart'=>'live.restart',
            'server.console.view'=>'live.console',
            'server.console.execute'=>'live.console',
            'live.bot.kick'=>'live.bots',
            'live.bot.slay'=>'live.bots',
            'live.bot.team'=>'live.bots',
        ];
        if (isset($legacyUmbrellas[$permission])) {
            $legacy = self::permissionDecisionForUser($user,$legacyUmbrellas[$permission]);
            if (!empty($legacy['allowed'])) {
                return ['allowed'=>true,'source'=>'Kompatibilitätsrecht: '.self::PERMISSIONS[$legacyUmbrellas[$permission]],'source_type'=>'legacy_umbrella'];
            }
        }

        $template = self::roleTemplate($role);
        $allowed = in_array('*', $template['permissions'], true)
            || in_array($permission, $template['permissions'], true);
        return [
            'allowed'=>$allowed,
            'source'=>$allowed ? Auth::webRoleLabel($role).'-Rollenvorlage' : Auth::webRoleLabel($role).'-Rollenvorlage enthält dieses Recht nicht',
            'source_type'=>'role_template',
        ];
    }

    public static function effectivePermissionsForUser(array $user): array
    {
        $result = [];
        foreach (self::PERMISSIONS as $permission => $_label) {
            $result[$permission] = self::userCan($user, $permission);
        }
        return $result;
    }

    public static function effectivePermissionDecisionsForUser(array $user): array
    {
        $result = [];
        foreach (self::PERMISSIONS as $permission => $_label) {
            $result[$permission] = self::permissionDecisionForUser($user, $permission);
        }
        return $result;
    }

    public static function rawPermissionOverrides(mixed $json): array
    {
        if (!is_string($json) || trim($json) === '') return [];
        $decoded = json_decode($json, true);
        if (!is_array($decoded)) return [];
        $result = [];
        if (array_is_list($decoded)) {
            foreach ($decoded as $permission) if (is_string($permission) && trim($permission) !== '') $result[$permission] = true;
            return $result;
        }
        foreach ($decoded as $permission => $allowed) {
            if (!is_string($permission) || trim($permission) === '') continue;
            $result[$permission] = filter_var($allowed, FILTER_VALIDATE_BOOL, FILTER_NULL_ON_FAILURE) ?? false;
        }
        return $result;
    }

    public static function permissionOverrides(mixed $json): array
    {
        return array_filter(
            self::rawPermissionOverrides($json),
            static fn($allowed, $permission): bool => array_key_exists((string)$permission, self::PERMISSIONS),
            ARRAY_FILTER_USE_BOTH
        );
    }

    public static function orphanedPermissionOverrides(mixed $json): array
    {
        return array_filter(
            self::rawPermissionOverrides($json),
            static fn($allowed, $permission): bool => !array_key_exists((string)$permission, self::PERMISSIONS),
            ARRAY_FILTER_USE_BOTH
        );
    }

    public static function isDangerousPermission(string $permission): bool
    {
        return in_array($permission, [
            'users.manage','users.permissions','roles.templates.manage','roles.packages.manage','roles.export_import',
            'servers.manage','servers.access_groups.manage','plugins.manage','plugins.repository.manage','plugins.update','plugins.rollback',
            'server.power.restart','server.power.stop','server.power.kill','server.console.execute',
            'live.ban','live.restart','live.console','live.cvar','live.godmode','live.presets',
            'moderation.global_comms','moderation.ban.permanent','moderation.unban','settings.manage',
        ], true);
    }

    public static function secondFactorEnabledForUser(?array $user = null): bool
    {
        $user ??= self::user();
        if (!$user) return false;
        if ((int)($user['totp_enabled'] ?? 0) === 1) return true;
        return WebAuthn::credentialCount((int)($user['id'] ?? 0)) > 0;
    }

    public static function twoFactorSetupRequired(?array $user = null): bool
    {
        $user ??= self::user();
        if (!$user) return false;
        $role = self::normalizeWebRole((string)($user['web_role'] ?? 'spieler'));
        $template = self::roleTemplate($role);
        return !empty($template['require_2fa']) && !self::secondFactorEnabledForUser($user);
    }

    public static function founderTwoFactorLocked(?array $user = null): bool
    {
        $user ??= self::user();
        if (!$user) return false;
        return self::normalizeWebRole((string)($user['web_role'] ?? 'spieler')) === 'founder'
            && !self::secondFactorEnabledForUser($user);
    }

    public static function effectiveWebRoleForUser(array $user): string
    {
        $role = self::normalizeWebRole((string)($user['web_role'] ?? 'spieler'));
        return self::founderTwoFactorLocked($user) ? 'admin' : $role;
    }

    public static function effectiveWebRole(): string
    {
        $user = self::user();
        return $user ? self::effectiveWebRoleForUser($user) : 'spieler';
    }

    private static function activeTemporaryPermissionDecisions(int $userId): array
    {
        static $cache = [];
        if ($userId <= 0) return [];
        if (array_key_exists($userId, $cache)) return $cache[$userId];
        try {
            $stmt = Database::connection()->prepare(
                "SELECT permission_key,effect,expires_at FROM user_temporary_permissions
                 WHERE user_id=? AND (expires_at IS NULL OR expires_at='' OR expires_at>?) ORDER BY id"
            );
            $stmt->execute([$userId,date('c')]);
            $out=[];
            foreach ($stmt->fetchAll() as $row) {
                $key=(string)$row['permission_key'];
                if (!array_key_exists($key,self::PERMISSIONS)) continue;
                $effect=strtolower((string)$row['effect'])==='deny'?'deny':'allow';
                $until=trim((string)($row['expires_at']??''));
                $out[$key][$effect]=$until!==''?$until:'ohne Ablauf';
            }
            return $cache[$userId]=$out;
        } catch (\Throwable) {
            return $cache[$userId]=[];
        }
    }

    private static function packageGrantSource(int $userId, string $permission): ?string
    {
        static $cache=[];
        if ($userId<=0) return null;
        if (!isset($cache[$userId])) {
            $cache[$userId]=[];
            try {
                $stmt=Database::connection()->prepare(
                    "SELECT p.name,p.permissions_json,a.expires_at
                     FROM user_permission_packages a JOIN permission_packages p ON p.id=a.package_id
                     WHERE a.user_id=? AND p.active=1 AND (a.expires_at IS NULL OR a.expires_at='' OR a.expires_at>?)
                     ORDER BY lower(p.name),p.id"
                );
                $stmt->execute([$userId,date('c')]);
                foreach($stmt->fetchAll() as $row){
                    $perms=json_decode((string)$row['permissions_json'],true);
                    if(!is_array($perms))continue;
                    foreach($perms as $key){
                        if(is_string($key)&&array_key_exists($key,self::PERMISSIONS)&&!isset($cache[$userId][$key])){
                            $cache[$userId][$key]=(string)$row['name'];
                        }
                    }
                }
            } catch (\Throwable) {}
        }
        return $cache[$userId][$permission]??null;
    }

    public static function roleTemplate(string $role): array
    {
        static $cache = [];
        $role = self::normalizeWebRole($role);
        if (isset($cache[$role])) return $cache[$role];

        $fallback = [
            'role'=>$role,
            'permissions'=>self::ROLE_DEFAULTS[$role] ?? [],
            'server_caps'=>self::ROLE_SERVER_DEFAULTS[$role] ?? self::ROLE_SERVER_DEFAULTS['spieler'],
            'max_game_role'=>self::ROLE_MAX_GAME_ROLE[$role] ?? 'spieler',
            'require_2fa'=>$role === 'founder',
        ];

        if ($role === 'founder') return $cache[$role] = $fallback;

        try {
            $stmt = Database::connection()->prepare(
                'SELECT permissions_json,server_caps_json,max_game_role,require_2fa FROM web_role_templates WHERE role_name=?'
            );
            $stmt->execute([$role]);
            $row = $stmt->fetch();
            if (!$row) return $cache[$role] = $fallback;

            $permissions = json_decode((string)$row['permissions_json'], true);
            $serverCaps = json_decode((string)$row['server_caps_json'], true);
            if (!is_array($permissions)) $permissions = $fallback['permissions'];
            if (!is_array($serverCaps)) $serverCaps = $fallback['server_caps'];

            $cleanPermissions = [];
            foreach ($permissions as $permission) {
                if ($permission === '*' || (is_string($permission) && array_key_exists($permission, self::PERMISSIONS))) {
                    if (!in_array($permission, $cleanPermissions, true)) $cleanPermissions[] = $permission;
                }
            }
            $cleanCaps = [];
            foreach (array_keys(self::ROLE_SERVER_DEFAULTS['spieler']) as $capability) {
                $cleanCaps[$capability] = !empty($serverCaps[$capability]);
            }
            $maxGameRole = strtolower(trim((string)($row['max_game_role'] ?? 'spieler')));
            if (!array_key_exists($maxGameRole, self::GAME_ROLE_LEVELS)) $maxGameRole = $fallback['max_game_role'];

            return $cache[$role] = [
                'role'=>$role,
                'permissions'=>$cleanPermissions,
                'server_caps'=>$cleanCaps,
                'max_game_role'=>$maxGameRole,
                'require_2fa'=>(int)($row['require_2fa'] ?? 0) === 1,
            ];
        } catch (\Throwable) {
            return $cache[$role] = $fallback;
        }
    }

    public static function allRoleTemplates(): array
    {
        $out = [];
        foreach (self::WEB_ROLES as $role => $label) {
            $out[$role] = self::roleTemplate($role) + ['label'=>$label,'level'=>self::ROLE_LEVELS[$role]];
        }
        return $out;
    }

    public static function requirePermission(string $permission): void
    {
        self::requireLogin();
        if (!self::can($permission)) {
            http_response_code(403);
            View::render('error', ['title' => 'Zugriff verweigert', 'message' => 'Für diese Funktion fehlen die erforderlichen Rechte.']);
            exit;
        }
    }

    public static function isFounderAccount(?array $user = null): bool
    {
        $user ??= self::user();
        return $user !== null && self::normalizeWebRole((string)($user['web_role'] ?? 'spieler')) === 'founder';
    }

    public static function isFounder(): bool
    {
        $user = self::user();
        return $user !== null && self::isFounderAccount($user) && !self::founderTwoFactorLocked($user);
    }

    public static function requireFounder(): void
    {
        self::requireLogin();
        $user = self::user();
        if ($user && self::founderTwoFactorLocked($user)) {
            View::flash('warning','Founder-Rechte sind bis zur 2FA-Einrichtung gesperrt. Aktuell gelten für dieses Konto ausschließlich die Rechte der Admin-Vorlage.');
            header('Location: /profile');
            exit;
        }
        if (!self::isFounder()) {
            http_response_code(403);
            View::render('error', ['title' => 'Zugriff verweigert', 'message' => 'Diese Funktion ist ausschließlich Foundern mit aktivierter Zwei-Faktor-Authentifizierung vorbehalten.']);
            exit;
        }
    }

    public static function webRoleLevel(string $role): int
    {
        return self::ROLE_LEVELS[self::normalizeWebRole($role)] ?? 0;
    }

    public static function canManageWebRoleTarget(string $targetRole): bool
    {
        $user = self::user();
        if (!$user || !self::can('users.manage')) return false;
        if (self::isFounder()) return true;
        return self::webRoleLevel(self::effectiveWebRoleForUser($user)) > self::webRoleLevel($targetRole);
    }

    public static function canAssignWebRole(string $role): bool
    {
        $user = self::user();
        if (!$user || !array_key_exists(self::normalizeWebRole($role), self::WEB_ROLES)) return false;
        if (self::isFounder()) return true;
        return self::webRoleLevel(self::effectiveWebRoleForUser($user)) > self::webRoleLevel($role);
    }

    public static function canInviteWebRole(string $role): bool
    {
        return self::can('users.invite') && self::canAssignWebRole($role);
    }

    public static function invitableWebRoles(): array
    {
        $result = [];
        foreach (self::WEB_ROLES as $role => $label) {
            if (self::canInviteWebRole($role)) $result[$role] = $label;
        }
        return $result;
    }

    public static function canEditRoleTemplate(string $role): bool
    {
        $user = self::user();
        if (!$user || !self::can('roles.templates.manage')) return false;
        $role = self::normalizeWebRole($role);
        if (self::isFounder()) return true;
        return self::webRoleLevel(self::effectiveWebRoleForUser($user)) > self::webRoleLevel($role);
    }

    public static function canServer(int $serverId, string $capability = 'view'): bool
    {
        $user = self::user();
        return $user ? self::userCanServer($user,$serverId,$capability) : false;
    }

    public static function userCanServer(array $user, int $serverId, string $capability='view'): bool
    {
        $storedRole=self::normalizeWebRole((string)($user['web_role']??'spieler'));
        $role=self::effectiveWebRoleForUser($user);
        if (array_key_exists('active',$user) && (int)$user['active'] !== 1) return false;
        if (self::twoFactorSetupRequired($user) && !self::founderTwoFactorLocked($user)) return false;
        if ($storedRole==='founder' && !self::founderTwoFactorLocked($user)) return true;
        $columns=['view'=>'can_view','players'=>'can_manage_players','permissions'=>'can_manage_permissions','sync'=>'can_sync','live'=>'can_live_admin'];
        $column=$columns[$capability]??null;
        if($column===null)return false;
        $now=date('c');
        try{
            $stmt=Database::connection()->prepare(
                'SELECT '.$column.' FROM user_server_access WHERE user_id=? AND server_id=? AND (expires_at IS NULL OR expires_at="" OR expires_at>?)'
            );
            $stmt->execute([(int)$user['id'],$serverId,$now]);
            $directValue=$stmt->fetchColumn();

            $stmt=Database::connection()->prepare(
                'SELECT uga.'.$column.' value FROM user_server_group_access uga
                 JOIN server_group_members gm ON gm.group_id=uga.group_id
                 WHERE uga.user_id=? AND gm.server_id=? AND (uga.expires_at IS NULL OR uga.expires_at="" OR uga.expires_at>?)'
            );
            $stmt->execute([(int)$user['id'],$serverId,$now]);
            $groupValues=$stmt->fetchAll(\PDO::FETCH_COLUMN);

            // Explizites Verbot gewinnt immer – unabhängig davon, ob es direkt
            // am Benutzer oder über eine Servergruppe gesetzt wurde.
            if($directValue!==false && (int)$directValue===0)return false;
            foreach($groupValues as $v)if((int)$v===0)return false;
            if($directValue!==false && (int)$directValue===1)return true;
            foreach($groupValues as $v)if((int)$v===1)return true;
        }catch(\Throwable){}
        $template=self::roleTemplate($role);
        return !empty($template['server_caps'][$capability]);
    }

    public static function serverAccessDecisionForUser(array $user,int $serverId,string $capability='view'): array
    {
        $storedRole=self::normalizeWebRole((string)($user['web_role']??'spieler'));
        $role=self::effectiveWebRoleForUser($user);
        if (array_key_exists('active',$user) && (int)$user['active'] !== 1) return ['allowed'=>false,'source'=>'Benutzerkonto ist gesperrt'];
        if(self::twoFactorSetupRequired($user) && !self::founderTwoFactorLocked($user))return ['allowed'=>false,'source'=>'2FA für die Rolle erforderlich'];
        if($storedRole==='founder' && !self::founderTwoFactorLocked($user))return ['allowed'=>true,'source'=>'Founder-Vollzugriff · 2FA bestätigt'];
        $columns=['view'=>'can_view','players'=>'can_manage_players','permissions'=>'can_manage_permissions','sync'=>'can_sync','live'=>'can_live_admin'];
        $column=$columns[$capability]??null;
        if(!$column)return ['allowed'=>false,'source'=>'Unbekannte Serverfreigabe'];
        $now=date('c');
        try{
            $stmt=Database::connection()->prepare('SELECT '.$column.',expires_at FROM user_server_access WHERE user_id=? AND server_id=? AND (expires_at IS NULL OR expires_at="" OR expires_at>?)');
            $stmt->execute([(int)$user['id'],$serverId,$now]);$row=$stmt->fetch();
            $stmt=Database::connection()->prepare(
                'SELECT g.name,uga.'.$column.' value,uga.expires_at FROM user_server_group_access uga JOIN server_access_groups g ON g.id=uga.group_id JOIN server_group_members gm ON gm.group_id=g.id WHERE uga.user_id=? AND gm.server_id=? AND (uga.expires_at IS NULL OR uga.expires_at="" OR uga.expires_at>?) ORDER BY lower(g.name)'
            );
            $stmt->execute([(int)$user['id'],$serverId,$now]);$rows=$stmt->fetchAll();

            if($row && (int)$row[$column]===0)return ['allowed'=>false,'source'=>'Individuelle Serverfreigabe verbietet dieses Recht'.(!empty($row['expires_at'])?' bis '.$row['expires_at']:'')];
            foreach($rows as $r)if((int)$r['value']===0)return ['allowed'=>false,'source'=>'Servergruppe '.$r['name'].' verbietet dieses Recht'];
            if($row && (int)$row[$column]===1)return ['allowed'=>true,'source'=>'Individuelle Serverfreigabe'.(!empty($row['expires_at'])?' bis '.$row['expires_at']:'')];
            foreach($rows as $r)if((int)$r['value']===1)return ['allowed'=>true,'source'=>'Servergruppe '.$r['name']];
        }catch(\Throwable){}
        $allowed=!empty(self::roleTemplate($role)['server_caps'][$capability]);
        return ['allowed'=>$allowed,'source'=>self::webRoleLabel($role).'-Rollenvorlage'];
    }

    public static function requireServer(int $serverId, string $capability = 'view'): void
    {
        self::requireLogin();
        if (!self::canServer($serverId, $capability)) {
            http_response_code(403);
            View::render('error', ['title' => 'Serverzugriff verweigert', 'message' => 'Für diesen Gameserver wurde Ihnen die erforderliche Freigabe nicht erteilt.']);
            exit;
        }
    }

    public static function serverMaxGameRole(int $serverId): string
    {
        $user=self::user();
        return $user?self::serverMaxGameRoleForUser($user,$serverId):'spieler';
    }

    public static function serverMaxGameRoleForUser(array $user,int $serverId): string
    {
        $storedRole=self::normalizeWebRole((string)($user['web_role']??'spieler'));
        $role=self::effectiveWebRoleForUser($user);
        if(self::twoFactorSetupRequired($user) && !self::founderTwoFactorLocked($user))return 'spieler';
        if($storedRole==='founder' && !self::founderTwoFactorLocked($user))return 'founder';
        $now=date('c');
        try{
            $stmt=Database::connection()->prepare('SELECT max_game_role FROM user_server_access WHERE user_id=? AND server_id=? AND (expires_at IS NULL OR expires_at="" OR expires_at>?)');
            $stmt->execute([(int)$user['id'],$serverId,$now]);$value=$stmt->fetchColumn();
            if($value!==false){$candidate=strtolower(trim((string)$value));if(isset(self::GAME_ROLE_LEVELS[$candidate]))return $candidate;}
            $stmt=Database::connection()->prepare(
                'SELECT uga.max_game_role FROM user_server_group_access uga JOIN server_group_members gm ON gm.group_id=uga.group_id WHERE uga.user_id=? AND gm.server_id=? AND (uga.expires_at IS NULL OR uga.expires_at="" OR uga.expires_at>?)'
            );
            $stmt->execute([(int)$user['id'],$serverId,$now]);$roles=$stmt->fetchAll(\PDO::FETCH_COLUMN);
            if($roles){
                $valid=array_values(array_filter(array_map(static fn($r)=>strtolower(trim((string)$r)),$roles),static fn($r)=>isset(self::GAME_ROLE_LEVELS[$r])));
                if($valid){usort($valid,static fn($a,$b)=>self::GAME_ROLE_LEVELS[$a]<=>self::GAME_ROLE_LEVELS[$b]);return $valid[0];}
            }
        }catch(\Throwable){}
        return self::roleTemplate($role)['max_game_role']??'spieler';
    }

    public static function canAssignGameRole(int $serverId, string $role): bool
    {
        if (!self::can('players.manage') || !self::canServer($serverId, 'players')) return false;
        $requested = self::GAME_ROLE_LEVELS[$role] ?? 0;
        $max = self::GAME_ROLE_LEVELS[self::serverMaxGameRole($serverId)] ?? 1;
        return $requested > 0 && $requested <= $max;
    }

    public static function canAssignCommunityRole(string $role): bool
    {
        if (!self::can('players.manage')) return false;
        $requested = self::GAME_ROLE_LEVELS[$role] ?? 0;
        $max = self::GAME_ROLE_LEVELS[self::maxCommunityRole()] ?? 1;
        return $requested > 0 && $requested <= $max;
    }

    public static function canManageGameRoleTarget(string $currentRole): bool
    {
        if (!self::can('players.manage')) return false;
        if (self::isFounder()) return array_key_exists($currentRole, self::GAME_ROLE_LEVELS);
        $current = self::GAME_ROLE_LEVELS[$currentRole] ?? 0;
        $max = self::GAME_ROLE_LEVELS[self::maxCommunityRole()] ?? 1;
        return $current > 0 && $current <= $max;
    }

    public static function maxCommunityRole(): string
    {
        $user = self::user();
        if (!$user) return 'spieler';
        $storedRole = self::normalizeWebRole((string)($user['web_role'] ?? 'spieler'));
        if ($storedRole === 'founder' && !self::founderTwoFactorLocked($user)) return 'founder';
        $role = self::effectiveWebRoleForUser($user);
        return self::roleTemplate($role)['max_game_role'] ?? 'spieler';
    }

    public static function normalizeGameRoleForImport(string $role): string
    {
        $role = strtolower(trim($role));
        return array_key_exists($role, self::GAME_ROLE_LEVELS) ? $role : 'spieler';
    }

    public static function normalizeWebRole(string $role): string
    {
        $role = strtolower(trim($role));
        return array_key_exists($role, self::WEB_ROLES) ? $role : 'spieler';
    }

    public static function webRoleLabel(string $role): string
    {
        $role = self::normalizeWebRole($role);
        return self::WEB_ROLES[$role] ?? 'Spieler';
    }

    public static function beginLogin(string $email, string $password): string
    {
        self::clearExpiredTwoFactorChallenge();

        $pdo = Database::connection();
        $email = strtolower(trim($email));
        $ip = Security::clientIp();

        if (self::isLoginRateLimited($email, $ip)) {
            self::cancelPendingTwoFactor();
            return 'failed';
        }

        $stmt = $pdo->prepare(
            'SELECT * FROM users WHERE lower(email)=lower(?) AND active=1 LIMIT 1'
        );
        $stmt->execute([$email]);
        $user = $stmt->fetch();

        if (!$user || !password_verify($password, (string)$user['password_hash'])) {
            self::recordLoginAttempt($email, $ip, false);
            self::cancelPendingTwoFactor();
            return 'failed';
        }

        if (self::secondFactorEnabledForUser($user)) {
            // Passwort ist korrekt, die Sitzung ist aber noch NICHT angemeldet.
            session_regenerate_id(true);
            unset($_SESSION['user_id'], $_SESSION['auth_version'], $_SESSION['_csrf']);

            $_SESSION['_2fa_pending'] = [
                'user_id'=>(int)$user['id'],
                'email'=>$email,
                'auth_version'=>max(1,(int)($user['auth_version'] ?? 1)),
                'started_at'=>time(),
                'expires_at'=>time()+300,
                'attempts'=>0,
            ];

            Audit::log('auth.2fa.challenge_started','user',(string)$user['id']);
            return '2fa_required';
        }

        self::recordLoginAttempt($email, $ip, true);
        self::finalizeLogin($user, 'password');
        return 'authenticated';
    }

    public static function completeTwoFactor(string $code): bool
    {
        self::clearExpiredTwoFactorChallenge();

        $pending = $_SESSION['_2fa_pending'] ?? null;
        if (!is_array($pending)) return false;

        $userId = (int)($pending['user_id'] ?? 0);
        $email = strtolower(trim((string)($pending['email'] ?? '')));
        $ip = Security::clientIp();

        if ($userId <= 0 || $email === '' || self::isLoginRateLimited($email, $ip)) {
            self::cancelPendingTwoFactor();
            return false;
        }

        $stmt = Database::connection()->prepare(
            'SELECT * FROM users WHERE id=? AND active=1 LIMIT 1'
        );
        $stmt->execute([$userId]);
        $user = $stmt->fetch();

        if (!$user
            || !self::secondFactorEnabledForUser($user)
            || max(1,(int)($user['auth_version'] ?? 1)) !== (int)($pending['auth_version'] ?? 0)
        ) {
            self::cancelPendingTwoFactor();
            return false;
        }

        $rawCode = trim($code);
        $digits = preg_replace('/\D+/', '', $rawCode) ?? '';
        $ok = false;
        $usedRecoveryCode = false;
        $remainingRecoveryCodes = null;

        try {
            $looksLikeTotp = preg_match('/^\d{6}$/', $digits)
                && strlen(preg_replace('/\s+/', '', $rawCode) ?? '') <= 7;
            if ($looksLikeTotp && (int)($user['totp_enabled'] ?? 0) === 1 && !empty($user['totp_secret_enc'])) {
                $secret = Crypto::decrypt((string)$user['totp_secret_enc']);
                $ok = TOTP::verify($secret, $digits);
            }
            if (!$ok) {
                $recovery = TOTP::consumeRecoveryCode(
                    isset($user['totp_recovery_codes_json']) ? (string)$user['totp_recovery_codes_json'] : null,
                    $rawCode
                );
                $ok = !empty($recovery['valid']);
                if ($ok) {
                    $usedRecoveryCode = true;
                    $remainingRecoveryCodes = (int)$recovery['remaining'];
                    Database::connection()->prepare(
                        'UPDATE users SET totp_recovery_codes_json=?,updated_at=? WHERE id=?'
                    )->execute([(string)$recovery['json'],date('c'),$userId]);
                    $user['totp_recovery_codes_json'] = (string)$recovery['json'];
                }
            }
        } catch (\Throwable) {
            self::cancelPendingTwoFactor();
            return false;
        }

        if (!$ok) {
            $_SESSION['_2fa_pending']['attempts'] =
                (int)($_SESSION['_2fa_pending']['attempts'] ?? 0) + 1;

            self::recordLoginAttempt($email, $ip, false);
            Audit::log('auth.2fa.failed','user',(string)$userId,[
                'attempt'=>(int)$_SESSION['_2fa_pending']['attempts'],
            ]);

            if ((int)$_SESSION['_2fa_pending']['attempts'] >= 6) {
                self::cancelPendingTwoFactor();
            }

            return false;
        }

        self::recordLoginAttempt($email, $ip, true);
        if ($usedRecoveryCode) {
            Audit::log('auth.2fa.recovery_used','user',(string)$userId,[
                'remaining'=>$remainingRecoveryCodes,
            ]);
        }
        self::cancelPendingTwoFactor();
        self::finalizeLogin($user, $usedRecoveryCode ? 'recovery' : 'totp');
        if ($usedRecoveryCode && $remainingRecoveryCodes !== null) {
            $_SESSION['_2fa_recovery_notice'] = $remainingRecoveryCodes;
        }
        return true;
    }

    public static function pendingTwoFactorUser(): ?array
    {
        self::clearExpiredTwoFactorChallenge();
        $pending = $_SESSION['_2fa_pending'] ?? null;
        if (!is_array($pending)) return null;
        $userId = (int)($pending['user_id'] ?? 0);
        if ($userId <= 0) return null;
        $stmt = Database::connection()->prepare('SELECT * FROM users WHERE id=? AND active=1 LIMIT 1');
        $stmt->execute([$userId]);
        $user = $stmt->fetch();
        if (!$user || max(1,(int)($user['auth_version'] ?? 1)) !== (int)($pending['auth_version'] ?? 0)) {
            self::cancelPendingTwoFactor();
            return null;
        }
        return $user;
    }

    public static function completePasskeyTwoFactor(int $userId): bool
    {
        self::clearExpiredTwoFactorChallenge();
        $pending = $_SESSION['_2fa_pending'] ?? null;
        if (!is_array($pending) || (int)($pending['user_id'] ?? 0) !== $userId) return false;
        $user = self::pendingTwoFactorUser();
        if (!$user || WebAuthn::credentialCount($userId) < 1) return false;
        $email = strtolower(trim((string)($pending['email'] ?? '')));
        $ip = Security::clientIp();
        self::recordLoginAttempt($email, $ip, true);
        self::cancelPendingTwoFactor();
        self::finalizeLogin($user, 'passkey');
        return true;
    }

    public static function failPendingTwoFactor(string $method='unknown'): void
    {
        self::clearExpiredTwoFactorChallenge();
        $pending = $_SESSION['_2fa_pending'] ?? null;
        if (!is_array($pending)) return;
        $userId=(int)($pending['user_id']??0);
        $email=strtolower(trim((string)($pending['email']??'')));
        $_SESSION['_2fa_pending']['attempts']=(int)($pending['attempts']??0)+1;
        if ($email!=='') self::recordLoginAttempt($email, Security::clientIp(), false);
        Audit::log('auth.2fa.failed','user',(string)$userId,[
            'attempt'=>(int)$_SESSION['_2fa_pending']['attempts'],
            'method'=>$method,
        ]);
        if ((int)$_SESSION['_2fa_pending']['attempts'] >= 6) self::cancelPendingTwoFactor();
    }

    public static function pendingTwoFactor(): bool
    {
        self::clearExpiredTwoFactorChallenge();
        return is_array($_SESSION['_2fa_pending'] ?? null);
    }

    public static function cancelPendingTwoFactor(): void
    {
        unset($_SESSION['_2fa_pending']);
    }

    // Rückwärtskompatibel für ältere interne Aufrufer.
    public static function login(string $email, string $password, ?string $totp = null): bool
    {
        $result = self::beginLogin($email, $password);

        if ($result === 'authenticated') return true;
        if ($result !== '2fa_required' || $totp === null || trim($totp) === '') return false;

        return self::completeTwoFactor($totp);
    }

    private static function clearExpiredTwoFactorChallenge(): void
    {
        $pending = $_SESSION['_2fa_pending'] ?? null;
        if (!is_array($pending)) return;

        if ((int)($pending['expires_at'] ?? 0) < time()) {
            unset($_SESSION['_2fa_pending']);
        }
    }

    private static function isLoginRateLimited(string $email, string $ip): bool
    {
        $cutoff = date('c', time() - 900);
        $stmt = Database::connection()->prepare(
            'SELECT COUNT(*) FROM login_attempts '
            . 'WHERE email=? AND ip_address=? AND success=0 AND created_at>=?'
        );
        $stmt->execute([$email,$ip,$cutoff]);
        return (int)$stmt->fetchColumn() >= 8;
    }

    private static function recordLoginAttempt(string $email, string $ip, bool $success): void
    {
        Database::connection()->prepare(
            'INSERT INTO login_attempts(email,ip_address,success,created_at) VALUES(?,?,?,?)'
        )->execute([$email,$ip,$success?1:0,date('c')]);
    }

    private static function finalizeLogin(array $user, string $method='password'): void
    {
        $pdo = Database::connection();

        session_regenerate_id(true);
        $_SESSION['user_id'] = (int)$user['id'];
        $_SESSION['auth_version'] = max(1, (int)($user['auth_version'] ?? 1));
        $_SESSION['_created'] = time();
        $_SESSION['_last_activity'] = time();

        // last_login_at und Activity Digest erst nach vollständig erfolgreichem
        // Login. Das korrekte Passwort allein genügt bei aktiver 2FA nicht.
        $previousLogin = trim((string)($user['last_login_at'] ?? ''));
        $_SESSION['_activity_digest_since'] = $previousLogin !== ''
            ? $previousLogin
            : date('c', time() - 7 * 86400);
        $_SESSION['_activity_digest_first_login'] = $previousLogin === '';
        $_SESSION['_activity_digest_pending'] = 1;

        unset($_SESSION['_csrf'], $_SESSION['_2fa_pending']);

        $now = date('c');
        $pdo->prepare(
            'UPDATE users SET last_login_at=?,last_seen_at=?,updated_at=? WHERE id=?'
        )->execute([$now,$now,$now,(int)$user['id']]);

        $_SESSION['_presence_touch'] = time();

        $pdo->prepare('DELETE FROM login_attempts WHERE created_at < ?')
            ->execute([date('c', time() - 2592000)]);

        Audit::log('auth.login','user',(string)$user['id'],[
            'two_factor'=>$method !== 'password',
            'method'=>$method,
        ]);
    }

    public static function logout(): void
    {
        self::cancelPendingTwoFactor();
        if (self::check()) Audit::log('auth.logout', 'user', (string)self::user()['id']);
        $_SESSION = [];
        if (ini_get('session.use_cookies')) {
            $params = session_get_cookie_params();
            setcookie(session_name(), '', time() - 42000, $params['path'], $params['domain'] ?? '', $params['secure'], $params['httponly']);
        }
        session_destroy();
    }

    public static function displayName(?array $user = null): string
    {
        $user ??= self::user();
        if (!$user) return '';

        $steamName = trim((string)($user['steam_personaname'] ?? ''));
        $verified = trim((string)($user['steam_verified_at'] ?? '')) !== '';
        if ($verified && $steamName !== '') return $steamName;

        return trim((string)($user['name'] ?? '')) ?: ('Nutzer ' . (int)($user['id'] ?? 0));
    }

    public static function unreadAnnouncementCount(): int
    {
        $user = self::user();
        if (!$user) return 0;

        $stmt = Database::connection()->prepare(
            "SELECT COUNT(*) FROM webpanel_announcements a
             LEFT JOIN webpanel_announcement_reads r
               ON r.announcement_id=a.id AND r.user_id=?
             WHERE a.revoked_at IS NULL
               AND (a.expires_at IS NULL OR a.expires_at>?)
               AND r.announcement_id IS NULL"
        );
        $stmt->execute([(int)$user['id'], date('c')]);
        return (int)$stmt->fetchColumn();
    }

    public static function roleDefaults(string $role): array
    {
        $role = self::normalizeWebRole($role);
        if ($role === 'founder') return array_keys(self::PERMISSIONS);
        return self::ROLE_DEFAULTS[$role] ?? [];
    }
}
