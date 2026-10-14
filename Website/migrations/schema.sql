PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    web_role TEXT NOT NULL DEFAULT 'member',
    permissions_json TEXT NULL,
    totp_secret_enc TEXT NULL,
    totp_recovery_codes_json TEXT NULL,
    totp_enabled INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    auth_version INTEGER NOT NULL DEFAULT 1,
    steam_id64 TEXT NULL UNIQUE,
    steam_personaname TEXT NULL,
    steam_profile_url TEXT NULL,
    steam_avatar_url TEXT NULL,
    steam_verified_at TEXT NULL,
    steam_profile_synced_at TEXT NULL,
    steam_profile_refresh_attempted_epoch INTEGER NULL,
    last_seen_at TEXT NULL,
    ui_theme TEXT NOT NULL DEFAULT 'system',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_login_at TEXT NULL
);

CREATE TABLE IF NOT EXISTS invitations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    email TEXT NOT NULL,
    web_role TEXT NOT NULL,
    permissions_json TEXT NULL,
    server_access_json TEXT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    invited_by INTEGER NOT NULL,
    expires_at TEXT NOT NULL,
    accepted_at TEXT NULL,
    revoked_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (invited_by) REFERENCES users(id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS web_role_templates (
    role_name TEXT PRIMARY KEY,
    permissions_json TEXT NOT NULL,
    server_caps_json TEXT NOT NULL,
    max_game_role TEXT NOT NULL DEFAULT 'spieler',
    require_2fa INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS user_live_layouts (
    user_id INTEGER NOT NULL,
    scope_key TEXT NOT NULL,
    layout_json TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (user_id, scope_key),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_user_live_layouts_user ON user_live_layouts(user_id);

CREATE TABLE IF NOT EXISTS app_settings (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NULL,
    encrypted INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS game_players (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    display_name TEXT NOT NULL,
    steam_id64 TEXT NOT NULL UNIQUE,
    community_role TEXT NOT NULL DEFAULT 'spieler',
    status TEXT NOT NULL DEFAULT 'active',
    first_name TEXT NULL,
    last_name TEXT NULL,
    email TEXT NULL,
    phone TEXT NULL,
    discord_name TEXT NULL,
    discord_id TEXT NULL,
    notes TEXT NULL,
    joined_at TEXT NULL,
    left_at TEXT NULL,
    steam_personaname TEXT NULL,
    steam_profile_url TEXT NULL,
    steam_avatar_url TEXT NULL,
    steam_avatar_medium_url TEXT NULL,
    steam_avatar_full_url TEXT NULL,
    steam_visibility INTEGER NULL,
    steam_personastate INTEGER NULL,
    steam_lastlogoff INTEGER NULL,
    steam_last_synced_at TEXT NULL,
    steam_sync_error TEXT NULL,
    main_player_id INTEGER NULL,
    active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (main_player_id) REFERENCES game_players(id) ON DELETE SET NULL
);


CREATE TABLE IF NOT EXISTS steam_name_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id INTEGER NOT NULL,
    steam_name TEXT NOT NULL,
    first_seen_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    UNIQUE(player_id, steam_name),
    FOREIGN KEY (player_id) REFERENCES game_players(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_game_players_role ON game_players(community_role);
CREATE INDEX IF NOT EXISTS idx_game_players_steam_name ON game_players(steam_personaname);
CREATE INDEX IF NOT EXISTS idx_game_players_name ON game_players(display_name);
CREATE INDEX IF NOT EXISTS idx_game_players_main_player ON game_players(main_player_id);
CREATE INDEX IF NOT EXISTS idx_steam_name_history_player ON steam_name_history(player_id, last_seen_at);

CREATE TABLE IF NOT EXISTS game_servers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    connector_type TEXT NOT NULL,
    base_url TEXT NULL,
    server_identifier TEXT NULL,
    host TEXT NULL,
    port INTEGER NULL,
    username TEXT NULL,
    secret_enc TEXT NULL,
    root_path TEXT NOT NULL DEFAULT '/',
    bridge_fastlive_enabled INTEGER NOT NULL DEFAULT 1,
    bridge_fastlive_secret_enc TEXT NULL,
    framework TEXT NULL,
    last_scan_at TEXT NULL,
    last_sync_at TEXT NULL,
    last_error TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);


CREATE TABLE IF NOT EXISTS user_server_access (
    user_id INTEGER NOT NULL,
    server_id INTEGER NOT NULL,
    can_view INTEGER NOT NULL DEFAULT 0,
    can_manage_players INTEGER NOT NULL DEFAULT 0,
    can_manage_permissions INTEGER NOT NULL DEFAULT 0,
    can_sync INTEGER NOT NULL DEFAULT 0,
    can_live_admin INTEGER NOT NULL DEFAULT 0,
    max_game_role TEXT NOT NULL DEFAULT 'spieler',
    expires_at TEXT NULL,
    reason TEXT NULL,
    updated_by INTEGER NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (user_id, server_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS permission_packages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    permissions_json TEXT NOT NULL DEFAULT '[]',
    active INTEGER NOT NULL DEFAULT 1,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS user_permission_packages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    package_id INTEGER NOT NULL,
    expires_at TEXT NULL,
    reason TEXT NULL,
    assigned_by INTEGER NULL,
    created_at TEXT NOT NULL,
    UNIQUE(user_id, package_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (package_id) REFERENCES permission_packages(id) ON DELETE CASCADE,
    FOREIGN KEY (assigned_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS user_temporary_permissions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    permission_key TEXT NOT NULL,
    effect TEXT NOT NULL CHECK(effect IN ('allow','deny')),
    expires_at TEXT NULL,
    reason TEXT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_user_temporary_permissions_active ON user_temporary_permissions(user_id, expires_at);
CREATE INDEX IF NOT EXISTS idx_user_permission_packages_active ON user_permission_packages(user_id, expires_at);

CREATE TABLE IF NOT EXISTS server_access_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS server_group_members (
    group_id INTEGER NOT NULL,
    server_id INTEGER NOT NULL,
    PRIMARY KEY (group_id, server_id),
    FOREIGN KEY (group_id) REFERENCES server_access_groups(id) ON DELETE CASCADE,
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS user_server_group_access (
    user_id INTEGER NOT NULL,
    group_id INTEGER NOT NULL,
    can_view INTEGER NOT NULL DEFAULT 0,
    can_manage_players INTEGER NOT NULL DEFAULT 0,
    can_manage_permissions INTEGER NOT NULL DEFAULT 0,
    can_sync INTEGER NOT NULL DEFAULT 0,
    can_live_admin INTEGER NOT NULL DEFAULT 0,
    max_game_role TEXT NOT NULL DEFAULT 'spieler',
    expires_at TEXT NULL,
    reason TEXT NULL,
    assigned_by INTEGER NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (user_id, group_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (group_id) REFERENCES server_access_groups(id) ON DELETE CASCADE,
    FOREIGN KEY (assigned_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_server_group_members_server ON server_group_members(server_id, group_id);
CREATE INDEX IF NOT EXISTS idx_user_server_group_access_active ON user_server_group_access(user_id, expires_at);

CREATE TABLE IF NOT EXISTS server_managed_players (
    server_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    PRIMARY KEY (server_id, player_id),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES game_players(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS server_player_groups (
    server_id INTEGER NOT NULL,
    player_id INTEGER NOT NULL,
    role_name TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (server_id, player_id),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE,
    FOREIGN KEY (player_id) REFERENCES game_players(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS server_role_permissions (
    server_id INTEGER NOT NULL,
    role_name TEXT NOT NULL,
    permissions_json TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (server_id, role_name),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS detected_plugins (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    framework TEXT NOT NULL,
    plugin_name TEXT NOT NULL,
    plugin_path TEXT NOT NULL,
    detected_at TEXT NOT NULL,
    UNIQUE(server_id, framework, plugin_name, plugin_path),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS plugin_permission_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    framework TEXT NOT NULL,
    plugin_name TEXT NOT NULL,
    permission_key TEXT NOT NULL,
    min_role TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE(server_id, framework, plugin_name, permission_key),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS sync_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    framework TEXT NOT NULL,
    remote_path TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    content TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);


CREATE TABLE IF NOT EXISTS server_file_state (
    server_id INTEGER NOT NULL,
    remote_path TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (server_id, remote_path),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NULL,
    action TEXT NOT NULL,
    target_type TEXT NULL,
    target_id TEXT NULL,
    details_json TEXT NULL,
    ip_address TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS login_attempts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    email TEXT NOT NULL,
    ip_address TEXT NOT NULL,
    success INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL
);


CREATE TABLE IF NOT EXISTS password_reset_requests (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NULL,
    email_hash TEXT NOT NULL,
    token_hash TEXT NULL UNIQUE,
    ip_address TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    used_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_audit_created_at ON audit_log(created_at);
CREATE INDEX IF NOT EXISTS idx_login_attempts_lookup ON login_attempts(email, ip_address, created_at);
CREATE INDEX IF NOT EXISTS idx_password_reset_email ON password_reset_requests(email_hash, created_at);
CREATE INDEX IF NOT EXISTS idx_password_reset_ip ON password_reset_requests(ip_address, created_at);
CREATE INDEX IF NOT EXISTS idx_password_reset_user ON password_reset_requests(user_id, created_at);


CREATE TABLE IF NOT EXISTS webpanel_announcements (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    message TEXT NOT NULL,
    level TEXT NOT NULL DEFAULT 'info',
    created_by INTEGER NOT NULL,
    expires_at TEXT NULL,
    revoked_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS webpanel_announcement_reads (
    announcement_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    read_at TEXT NOT NULL,
    PRIMARY KEY (announcement_id, user_id),
    FOREIGN KEY (announcement_id) REFERENCES webpanel_announcements(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_webpanel_announcements_active ON webpanel_announcements(revoked_at, expires_at, created_at);
CREATE INDEX IF NOT EXISTS idx_webpanel_announcement_reads_user ON webpanel_announcement_reads(user_id, read_at);


CREATE TABLE IF NOT EXISTS dashboard_chat_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    message TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_dashboard_chat_created ON dashboard_chat_messages(created_at, id);

CREATE TABLE IF NOT EXISTS forum_threads (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    category TEXT NOT NULL DEFAULT 'allgemein',
    title TEXT NOT NULL,
    is_pinned INTEGER NOT NULL DEFAULT 0,
    is_locked INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS forum_posts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    thread_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    body TEXT NOT NULL,
    deleted_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (thread_id) REFERENCES forum_threads(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_forum_threads_list ON forum_threads(deleted_at, is_pinned, updated_at);
CREATE INDEX IF NOT EXISTS idx_forum_posts_thread ON forum_posts(thread_id, deleted_at, created_at, id);

CREATE TABLE IF NOT EXISTS webpanel_direct_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sender_id INTEGER NOT NULL,
    recipient_id INTEGER NOT NULL,
    message TEXT NOT NULL,
    created_at TEXT NOT NULL,
    read_at TEXT NULL,
    FOREIGN KEY (sender_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (recipient_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_webpanel_dm_conversation
ON webpanel_direct_messages(sender_id, recipient_id, id);

CREATE INDEX IF NOT EXISTS idx_webpanel_dm_unread
ON webpanel_direct_messages(recipient_id, read_at, id);


CREATE TABLE IF NOT EXISTS plugin_update_sources (
    plugin_id TEXT PRIMARY KEY,
    repository TEXT NOT NULL,
    asset_pattern TEXT NOT NULL DEFAULT '*.zip',
    install_subpath TEXT NOT NULL DEFAULT '',
    channel TEXT NOT NULL DEFAULT 'stable',
    update_mode TEXT NOT NULL DEFAULT 'check_only',
    auto_update INTEGER NOT NULL DEFAULT 0,
    latest_version TEXT NULL,
    latest_tag TEXT NULL,
    latest_release_url TEXT NULL,
    latest_asset_name TEXT NULL,
    latest_asset_url TEXT NULL,
    latest_asset_sha256 TEXT NULL,
    last_checked_at TEXT NULL,
    last_error TEXT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS plugin_update_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    plugin_id TEXT NOT NULL,
    from_version TEXT NULL,
    to_version TEXT NULL,
    command_id TEXT NULL,
    mode TEXT NOT NULL DEFAULT 'manual',
    status TEXT NOT NULL DEFAULT 'queued',
    message TEXT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    observed_at TEXT NULL,
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_plugin_update_runs_lookup
ON plugin_update_runs(server_id, plugin_id, created_at);

CREATE INDEX IF NOT EXISTS idx_plugin_update_sources_auto
ON plugin_update_sources(auto_update, update_mode, last_checked_at);


-- v1.6.8: Fast-Live-Push, lokale Command-Queue und dauerhaftes Gameserver-Chatarchiv.
CREATE TABLE IF NOT EXISTS bridge_command_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    command_id TEXT NOT NULL,
    command_json TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'queued',
    delivery_attempts INTEGER NOT NULL DEFAULT 0,
    requested_at TEXT NOT NULL,
    delivered_at TEXT NULL,
    completed_at TEXT NULL,
    result_json TEXT NULL,
    UNIQUE(server_id,command_id),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_bridge_command_delivery ON bridge_command_queue(server_id,status,delivered_at,id);

CREATE TABLE IF NOT EXISTS server_chat_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id INTEGER NOT NULL,
    message_uid TEXT NOT NULL,
    steam_id64 TEXT NULL,
    player_name TEXT NOT NULL,
    community_role_snapshot TEXT NOT NULL DEFAULT 'spieler',
    team TEXT NOT NULL DEFAULT 'Unassigned',
    team_only INTEGER NOT NULL DEFAULT 0,
    message TEXT NOT NULL,
    map_name TEXT NULL,
    sent_at TEXT NOT NULL,
    received_at TEXT NOT NULL,
    UNIQUE(server_id,message_uid),
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_server_chat_time ON server_chat_messages(server_id,sent_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_server_chat_steam_time ON server_chat_messages(server_id,steam_id64,sent_at DESC);
CREATE INDEX IF NOT EXISTS idx_server_chat_player_name ON server_chat_messages(server_id,player_name);

-- v1.7.3: zentrale globale Banverwaltung mit Chat-Belegen und Historie.
CREATE TABLE IF NOT EXISTS global_bans (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    reason_code TEXT NOT NULL DEFAULT 'other',
    reason_label TEXT NOT NULL,
    reason_detail TEXT NULL,
    starts_at TEXT NOT NULL,
    expires_at TEXT NULL,
    permanent INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    unbanned_by INTEGER NULL,
    unbanned_at TEXT NULL,
    unban_reason TEXT NULL,
    evidence_chat_message_id INTEGER NULL,
    evidence_server_id INTEGER NULL,
    evidence_player_name TEXT NULL,
    evidence_message TEXT NULL,
    evidence_sent_at TEXT NULL,
    source TEXT NOT NULL DEFAULT 'manual',
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (unbanned_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (evidence_chat_message_id) REFERENCES server_chat_messages(id) ON DELETE SET NULL,
    FOREIGN KEY (evidence_server_id) REFERENCES game_servers(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_bans_active_steam ON global_bans(active,steam_id64);
CREATE INDEX IF NOT EXISTS idx_global_bans_expiry ON global_bans(active,permanent,expires_at);
CREATE INDEX IF NOT EXISTS idx_global_bans_reason ON global_bans(reason_code,active);
CREATE INDEX IF NOT EXISTS idx_global_bans_created ON global_bans(created_at DESC,id DESC);

-- v1.7.4: Moderationszentrum, Verwarnungen, Notizen, Beweise, Appeals und globale Kommunikationssperren.
CREATE TABLE IF NOT EXISTS moderation_warnings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    reason_code TEXT NOT NULL DEFAULT 'other',
    reason_label TEXT NOT NULL,
    detail TEXT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NULL,
    resolved_by INTEGER NULL,
    resolved_at TEXT NULL,
    resolution TEXT NULL,
    evidence_chat_message_id INTEGER NULL,
    evidence_message TEXT NULL,
    evidence_sent_at TEXT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (resolved_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (evidence_chat_message_id) REFERENCES server_chat_messages(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_moderation_warnings_steam ON moderation_warnings(steam_id64,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_moderation_warnings_active ON moderation_warnings(resolved_at,expires_at,steam_id64);

CREATE TABLE IF NOT EXISTS moderation_notes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    note TEXT NOT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_moderation_notes_steam ON moderation_notes(steam_id64,id DESC);

CREATE TABLE IF NOT EXISTS moderation_evidence (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    evidence_type TEXT NOT NULL DEFAULT 'note',
    label TEXT NULL,
    value_text TEXT NOT NULL,
    chat_message_id INTEGER NULL,
    global_ban_id INTEGER NULL,
    warning_id INTEGER NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (chat_message_id) REFERENCES server_chat_messages(id) ON DELETE SET NULL,
    FOREIGN KEY (global_ban_id) REFERENCES global_bans(id) ON DELETE SET NULL,
    FOREIGN KEY (warning_id) REFERENCES moderation_warnings(id) ON DELETE SET NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_moderation_evidence_steam ON moderation_evidence(steam_id64,id DESC);

CREATE TABLE IF NOT EXISTS moderation_appeals (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    global_ban_id INTEGER NULL,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    statement TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'open',
    decision TEXT NULL,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    reviewed_by INTEGER NULL,
    reviewed_at TEXT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY (global_ban_id) REFERENCES global_bans(id) ON DELETE SET NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (reviewed_by) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_moderation_appeals_status ON moderation_appeals(status,id DESC);
CREATE INDEX IF NOT EXISTS idx_moderation_appeals_steam ON moderation_appeals(steam_id64,id DESC);

CREATE TABLE IF NOT EXISTS global_communication_sanctions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    steam_id64 TEXT NOT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    sanction_type TEXT NOT NULL,
    reason_code TEXT NOT NULL DEFAULT 'other',
    reason_label TEXT NOT NULL,
    reason_detail TEXT NULL,
    starts_at TEXT NOT NULL,
    expires_at TEXT NULL,
    permanent INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    created_by INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    revoked_by INTEGER NULL,
    revoked_at TEXT NULL,
    revoke_reason TEXT NULL,
    evidence_chat_message_id INTEGER NULL,
    evidence_message TEXT NULL,
    evidence_sent_at TEXT NULL,
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (revoked_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (evidence_chat_message_id) REFERENCES server_chat_messages(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_comm_active_steam ON global_communication_sanctions(active,steam_id64,sanction_type);
CREATE INDEX IF NOT EXISTS idx_global_comm_expiry ON global_communication_sanctions(active,permanent,expires_at);



-- v1.9.2: Passkeys / WebAuthn als vollwertiger zweiter Faktor.
CREATE TABLE IF NOT EXISTS webauthn_credentials (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    credential_id_b64 TEXT NOT NULL UNIQUE,
    public_key_pem TEXT NOT NULL,
    algorithm INTEGER NOT NULL,
    sign_count INTEGER NOT NULL DEFAULT 0,
    label TEXT NOT NULL DEFAULT 'Passkey',
    transports_json TEXT NOT NULL DEFAULT '[]',
    aaguid TEXT NULL,
    created_at TEXT NOT NULL,
    last_used_at TEXT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_webauthn_credentials_user ON webauthn_credentials(user_id,id);

-- v1.9.2: zentraler serverübergreifender Werbeschutz.
CREATE TABLE IF NOT EXISTS global_advertising_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT NOT NULL UNIQUE,
    server_id INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    steam_id64 TEXT NULL,
    player_name TEXT NOT NULL DEFAULT '',
    match_value TEXT NOT NULL DEFAULT '',
    action TEXT NOT NULL DEFAULT '',
    message_excerpt TEXT NULL,
    observed_at TEXT NOT NULL,
    received_at TEXT NOT NULL,
    origin TEXT NOT NULL DEFAULT 'automatic',
    review_status TEXT NOT NULL DEFAULT 'detected',
    chat_message_id INTEGER NULL,
    global_ban_id INTEGER NULL,
    reported_by INTEGER NULL,
    reviewed_by INTEGER NULL,
    reviewed_at TEXT NULL,
    review_note TEXT NULL,
    FOREIGN KEY (server_id) REFERENCES game_servers(id) ON DELETE CASCADE,
    FOREIGN KEY (chat_message_id) REFERENCES server_chat_messages(id) ON DELETE SET NULL,
    FOREIGN KEY (global_ban_id) REFERENCES global_bans(id) ON DELETE SET NULL,
    FOREIGN KEY (reported_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (reviewed_by) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_ad_events_server_time ON global_advertising_events(server_id,received_at DESC);
CREATE INDEX IF NOT EXISTS idx_global_ad_events_steam ON global_advertising_events(steam_id64,received_at DESC);

CREATE TABLE IF NOT EXISTS global_advertising_identities (
    steam_id64 TEXT PRIMARY KEY,
    player_name TEXT NOT NULL DEFAULT '',
    match_value TEXT NOT NULL DEFAULT '',
    first_server_id INTEGER NULL,
    last_server_id INTEGER NULL,
    first_seen_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    hit_count INTEGER NOT NULL DEFAULT 1,
    active INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (first_server_id) REFERENCES game_servers(id) ON DELETE SET NULL,
    FOREIGN KEY (last_server_id) REFERENCES game_servers(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_ad_identities_active ON global_advertising_identities(active,last_seen_at DESC);

CREATE TABLE IF NOT EXISTS global_advertising_domains (
    domain TEXT PRIMARY KEY,
    source TEXT NOT NULL DEFAULT 'learned',
    first_server_id INTEGER NULL,
    last_server_id INTEGER NULL,
    first_seen_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    hit_count INTEGER NOT NULL DEFAULT 1,
    active INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (first_server_id) REFERENCES game_servers(id) ON DELETE SET NULL,
    FOREIGN KEY (last_server_id) REFERENCES game_servers(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_ad_domains_active ON global_advertising_domains(active,domain);

-- v1.9.5: manuell verwaltete globale Werbeschutzregeln.
CREATE TABLE IF NOT EXISTS global_advertising_rules (
    rule_type TEXT NOT NULL CHECK(rule_type IN ('allowed_domain','blocked_phrase')),
    rule_value TEXT NOT NULL,
    source TEXT NOT NULL DEFAULT 'manual',
    active INTEGER NOT NULL DEFAULT 1,
    created_by INTEGER NULL,
    updated_by INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (rule_type,rule_value),
    FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (updated_by) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_global_ad_rules_active ON global_advertising_rules(rule_type,active,rule_value);

-- v1.10.0: serverseitiges Pets-MVP mit Pflege, Fortschritt, Wallet und Tagesaufgaben.
CREATE TABLE IF NOT EXISTS pets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    species_key TEXT NOT NULL,
    name TEXT NOT NULL,
    level INTEGER NOT NULL DEFAULT 1 CHECK(level BETWEEN 1 AND 20),
    xp INTEGER NOT NULL DEFAULT 0 CHECK(xp >= 0),
    health INTEGER NOT NULL DEFAULT 100 CHECK(health BETWEEN 0 AND 100),
    hunger INTEGER NOT NULL DEFAULT 85 CHECK(hunger BETWEEN 0 AND 100),
    energy INTEGER NOT NULL DEFAULT 85 CHECK(energy BETWEEN 0 AND 100),
    happiness INTEGER NOT NULL DEFAULT 80 CHECK(happiness BETWEEN 0 AND 100),
    bond INTEGER NOT NULL DEFAULT 10 CHECK(bond BETWEEN 0 AND 100),
    last_state_at TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pets_one_active_user ON pets(user_id) WHERE active=1;
CREATE INDEX IF NOT EXISTS idx_pets_ranking ON pets(active,level DESC,xp DESC,bond DESC);

CREATE TABLE IF NOT EXISTS pet_actions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pet_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    action_type TEXT NOT NULL,
    idempotency_key TEXT NOT NULL,
    effects_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE(user_id,idempotency_key),
    FOREIGN KEY(pet_id) REFERENCES pets(id) ON DELETE CASCADE,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_actions_pet_time ON pet_actions(pet_id,created_at DESC);

CREATE TABLE IF NOT EXISTS pet_action_cooldowns (
    pet_id INTEGER NOT NULL,
    action_type TEXT NOT NULL,
    available_at TEXT NOT NULL,
    PRIMARY KEY(pet_id,action_type),
    FOREIGN KEY(pet_id) REFERENCES pets(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS pet_wallets (
    user_id INTEGER PRIMARY KEY,
    coins INTEGER NOT NULL DEFAULT 0 CHECK(coins >= 0),
    training_points INTEGER NOT NULL DEFAULT 0 CHECK(training_points >= 0),
    updated_at TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS pet_wallet_ledger (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    currency TEXT NOT NULL CHECK(currency IN ('coins','training_points')),
    amount INTEGER NOT NULL,
    reason TEXT NOT NULL,
    reference_key TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE(user_id,currency,reference_key),
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS pet_tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    pet_id INTEGER NOT NULL,
    period_key TEXT NOT NULL,
    action_type TEXT NOT NULL,
    title TEXT NOT NULL,
    target INTEGER NOT NULL CHECK(target > 0),
    progress INTEGER NOT NULL DEFAULT 0 CHECK(progress >= 0),
    reward_coins INTEGER NOT NULL DEFAULT 0,
    reward_xp INTEGER NOT NULL DEFAULT 0,
    claimed_at TEXT NULL,
    created_at TEXT NOT NULL,
    UNIQUE(user_id,period_key,action_type),
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY(pet_id) REFERENCES pets(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_tasks_daily ON pet_tasks(user_id,period_key,claimed_at);

CREATE TABLE IF NOT EXISTS pet_inventory (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    item_key TEXT NOT NULL,
    item_name TEXT NOT NULL,
    item_type TEXT NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 1 CHECK(quantity > 0),
    equipped INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    UNIQUE(user_id,item_key),
    FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- v1.13.0: Abenteuer, individuelle Ausrüstung, Skillbaum und PvE-Arena.
-- Die zusätzliche Eindeutigkeit ermöglicht zusammengesetzte Fremdschlüssel, die
-- nicht nur die Pet-ID, sondern auch den Besitzer auf Datenbankebene absichern.
CREATE UNIQUE INDEX IF NOT EXISTS idx_pets_id_user_unique ON pets(id,user_id);

CREATE TABLE IF NOT EXISTS pet_adventures (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    pet_id INTEGER NOT NULL,
    adventure_key TEXT NOT NULL CHECK(length(adventure_key) BETWEEN 2 AND 64),
    status TEXT NOT NULL DEFAULT 'running' CHECK(status IN ('running','claimed')),
    difficulty INTEGER NOT NULL CHECK(difficulty BETWEEN 1 AND 5),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 16 AND 80),
    started_at_epoch INTEGER NOT NULL CHECK(started_at_epoch > 0),
    ready_at_epoch INTEGER NOT NULL CHECK(ready_at_epoch >= started_at_epoch),
    result_json TEXT NOT NULL CHECK(length(result_json) > 1),
    claim_idempotency_key TEXT NULL CHECK(claim_idempotency_key IS NULL OR length(claim_idempotency_key) BETWEEN 16 AND 80),
    claimed_at_epoch INTEGER NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE(user_id,idempotency_key),
    UNIQUE(user_id,claim_idempotency_key),
    CHECK(
        (status='running' AND claim_idempotency_key IS NULL AND claimed_at_epoch IS NULL)
        OR
        (status='claimed' AND claim_idempotency_key IS NOT NULL AND claimed_at_epoch IS NOT NULL AND claimed_at_epoch >= ready_at_epoch)
    ),
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pet_adventures_one_running ON pet_adventures(pet_id) WHERE status='running';
CREATE INDEX IF NOT EXISTS idx_pet_adventures_user_history ON pet_adventures(user_id,started_at_epoch DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_pet_adventures_ready ON pet_adventures(status,ready_at_epoch);

-- Zufallsfunde sind Einzelstücke. Feste Attributspalten halten die vom Server
-- berechneten Kampfwerte prüfbar und verhindern frei einschleusbare JSON-Werte.
CREATE TABLE IF NOT EXISTS pet_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    pet_id INTEGER NOT NULL,
    template_key TEXT NOT NULL CHECK(length(template_key) BETWEEN 2 AND 64),
    item_name TEXT NOT NULL CHECK(length(item_name) BETWEEN 2 AND 100),
    slot TEXT NOT NULL CHECK(slot IN ('head','body','paws','charm')),
    rarity TEXT NOT NULL CHECK(rarity IN ('common','uncommon','rare','epic','legendary')),
    item_level INTEGER NOT NULL CHECK(item_level BETWEEN 1 AND 20),
    level_required INTEGER NOT NULL DEFAULT 1 CHECK(level_required BETWEEN 1 AND 20),
    strength_bonus INTEGER NOT NULL DEFAULT 0 CHECK(strength_bonus BETWEEN 0 AND 50),
    agility_bonus INTEGER NOT NULL DEFAULT 0 CHECK(agility_bonus BETWEEN 0 AND 50),
    vitality_bonus INTEGER NOT NULL DEFAULT 0 CHECK(vitality_bonus BETWEEN 0 AND 50),
    instinct_bonus INTEGER NOT NULL DEFAULT 0 CHECK(instinct_bonus BETWEEN 0 AND 50),
    equipped INTEGER NOT NULL DEFAULT 0 CHECK(equipped IN (0,1)),
    source_type TEXT NOT NULL CHECK(source_type IN ('starter','adventure','arena')),
    source_id INTEGER NOT NULL CHECK(source_id > 0),
    source_index INTEGER NOT NULL DEFAULT 0 CHECK(source_index BETWEEN 0 AND 20),
    created_at TEXT NOT NULL,
    UNIQUE(source_type,source_id,source_index),
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pet_items_one_equipped_slot ON pet_items(pet_id,slot) WHERE equipped=1;
CREATE UNIQUE INDEX IF NOT EXISTS idx_pet_items_one_starter ON pet_items(pet_id) WHERE source_type='starter';
CREATE INDEX IF NOT EXISTS idx_pet_items_inventory ON pet_items(user_id,pet_id,equipped DESC,rarity,item_level DESC,id DESC);

CREATE TABLE IF NOT EXISTS pet_skill_ranks (
    pet_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL,
    skill_key TEXT NOT NULL CHECK(length(skill_key) BETWEEN 2 AND 64),
    rank INTEGER NOT NULL CHECK(rank BETWEEN 1 AND 3),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY(pet_id,skill_key),
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_skill_ranks_user ON pet_skill_ranks(user_id,pet_id);

-- Unlocks und Resets teilen sich ein Ereignisjournal. Damit lässt sich ein
-- Reset exakt einmal verbuchen und die tatsächlich investierte Punktzahl
-- nachvollziehbar erstatten.
CREATE TABLE IF NOT EXISTS pet_skill_spends (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    pet_id INTEGER NOT NULL,
    event_type TEXT NOT NULL DEFAULT 'unlock' CHECK(event_type IN ('unlock','reset')),
    skill_key TEXT NOT NULL CHECK(length(skill_key) BETWEEN 2 AND 64),
    rank_before INTEGER NOT NULL DEFAULT 0 CHECK(rank_before BETWEEN 0 AND 3),
    new_rank INTEGER NOT NULL CHECK(new_rank BETWEEN 0 AND 3),
    cost_points INTEGER NOT NULL DEFAULT 0 CHECK(cost_points >= 0),
    refunded_points INTEGER NOT NULL DEFAULT 0 CHECK(refunded_points >= 0),
    cost_coins INTEGER NOT NULL DEFAULT 0 CHECK(cost_coins >= 0),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 16 AND 80),
    created_at_epoch INTEGER NOT NULL CHECK(created_at_epoch > 0),
    created_at TEXT NOT NULL,
    UNIQUE(user_id,idempotency_key),
    CHECK(
        (event_type='unlock' AND skill_key<>'__reset__' AND new_rank=rank_before+1 AND cost_points>0 AND refunded_points=0 AND cost_coins=0)
        OR
        (event_type='reset' AND skill_key='__reset__' AND rank_before=0 AND new_rank=0 AND cost_points=0 AND cost_coins>0)
    ),
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_skill_spends_history ON pet_skill_spends(pet_id,created_at_epoch DESC,id DESC);

CREATE TABLE IF NOT EXISTS pet_arena_profiles (
    pet_id INTEGER PRIMARY KEY,
    user_id INTEGER NOT NULL,
    rating INTEGER NOT NULL DEFAULT 1000 CHECK(rating BETWEEN 0 AND 5000),
    wins INTEGER NOT NULL DEFAULT 0 CHECK(wins >= 0),
    losses INTEGER NOT NULL DEFAULT 0 CHECK(losses >= 0),
    draws INTEGER NOT NULL DEFAULT 0 CHECK(draws >= 0),
    season_key TEXT NOT NULL CHECK(length(season_key) BETWEEN 2 AND 32),
    last_battle_epoch INTEGER NULL CHECK(last_battle_epoch IS NULL OR last_battle_epoch > 0),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_arena_profiles_ranking ON pet_arena_profiles(season_key,rating DESC,wins DESC,pet_id);

CREATE TABLE IF NOT EXISTS pet_arena_battles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    pet_id INTEGER NOT NULL,
    opponent_key TEXT NOT NULL CHECK(length(opponent_key) BETWEEN 2 AND 64),
    tactic TEXT NOT NULL CHECK(tactic IN ('aggressive','defensive','tricky')),
    result TEXT NOT NULL CHECK(result IN ('win','loss','draw')),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 16 AND 80),
    period_key TEXT NOT NULL CHECK(length(period_key)=10),
    stats_snapshot_json TEXT NOT NULL CHECK(length(stats_snapshot_json) > 1),
    battle_log_json TEXT NOT NULL CHECK(length(battle_log_json) > 1),
    rounds INTEGER NOT NULL CHECK(rounds BETWEEN 1 AND 12),
    energy_cost INTEGER NOT NULL CHECK(energy_cost >= 0),
    rating_before INTEGER NOT NULL CHECK(rating_before BETWEEN 0 AND 5000),
    rating_after INTEGER NOT NULL CHECK(rating_after BETWEEN 0 AND 5000),
    reward_coins INTEGER NOT NULL DEFAULT 0 CHECK(reward_coins >= 0),
    reward_xp INTEGER NOT NULL DEFAULT 0 CHECK(reward_xp >= 0),
    reward_training_points INTEGER NOT NULL DEFAULT 0 CHECK(reward_training_points >= 0),
    created_at_epoch INTEGER NOT NULL CHECK(created_at_epoch > 0),
    created_at TEXT NOT NULL,
    UNIQUE(user_id,idempotency_key),
    FOREIGN KEY(pet_id,user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_arena_battles_daily ON pet_arena_battles(user_id,period_key,created_at_epoch DESC);
CREATE INDEX IF NOT EXISTS idx_pet_arena_battles_pet_history ON pet_arena_battles(pet_id,created_at_epoch DESC,id DESC);

-- v1.14.0: kurzlebige, synchronisierte Spielmomente zwischen Community-Pets.
-- Die Tabelle protokolliert ausschließlich Animationen. Sie verändert keine
-- Werte des Ziel-Pets und bindet beide Besitzer über zusammengesetzte FKs.
CREATE TABLE IF NOT EXISTS pet_playground_interactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    actor_user_id INTEGER NOT NULL,
    actor_pet_id INTEGER NOT NULL,
    target_user_id INTEGER NOT NULL,
    target_pet_id INTEGER NOT NULL,
    interaction_type TEXT NOT NULL CHECK(interaction_type IN ('greet','sniff','chase','ball')),
    period_key TEXT NOT NULL CHECK(length(period_key)=10),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 16 AND 80),
    created_at_epoch INTEGER NOT NULL CHECK(created_at_epoch > 0),
    expires_at_epoch INTEGER NOT NULL CHECK(expires_at_epoch > created_at_epoch),
    created_at TEXT NOT NULL,
    UNIQUE(actor_user_id,idempotency_key),
    CHECK(actor_user_id<>target_user_id),
    CHECK(actor_pet_id<>target_pet_id),
    FOREIGN KEY(actor_pet_id,actor_user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE,
    FOREIGN KEY(target_pet_id,target_user_id) REFERENCES pets(id,user_id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_pet_playground_actor_day ON pet_playground_interactions(actor_user_id,actor_pet_id,period_key,created_at_epoch DESC);
CREATE INDEX IF NOT EXISTS idx_pet_playground_pair ON pet_playground_interactions(actor_pet_id,target_pet_id,created_at_epoch DESC);
CREATE INDEX IF NOT EXISTS idx_pet_playground_target_day ON pet_playground_interactions(target_pet_id,period_key,created_at_epoch DESC);
CREATE INDEX IF NOT EXISTS idx_pet_playground_expiry ON pet_playground_interactions(expires_at_epoch,id);
