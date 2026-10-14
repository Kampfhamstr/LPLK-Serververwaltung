#!/usr/bin/env php
<?php
if (PHP_SAPI !== 'cli') exit(1);
require dirname(__DIR__) . '/tests/adapter_smoke.php';

require dirname(__DIR__) . '/tests/live_static_smoke.php';
require dirname(__DIR__) . '/tests/v193_advertising_moderation_history_test.php';
require dirname(__DIR__) . '/tests/v193_advertising_workflow_integration_test.php';
require dirname(__DIR__) . '/tests/v194_advertising_identity_cards_integration_test.php';
require dirname(__DIR__) . '/tests/v195_advertising_rule_management_integration_test.php';
require dirname(__DIR__) . '/tests/v196_dashboard_power_shortcuts_test.php';
require dirname(__DIR__) . '/tests/v1100_pets_module_test.php';
require dirname(__DIR__) . '/tests/v1110_species_catalog_test.php';
require dirname(__DIR__) . '/tests/v1120_pet_animations_test.php';
require dirname(__DIR__) . '/tests/v1130_pet_adventure_arena_integration_test.php';
require dirname(__DIR__) . '/tests/v1140_pet_playground_integration_test.php';
require dirname(__DIR__) . '/tests/v1150_pet_playground_steam_gate_test.php';
require dirname(__DIR__) . '/tests/v1150_pet_action_animations_test.php';
require dirname(__DIR__) . '/tests/v1151_pet_roster_sqlite_binding_hotfix_test.php';
require dirname(__DIR__) . '/tests/v1152_fastlive_command_roundtrip_test.php';
passthru(escapeshellarg(PHP_BINARY).' '.escapeshellarg(dirname(__DIR__).'/tests/v1152_quick_manage_result_test.php'),$result1152);
if($result1152!==0)exit($result1152);
