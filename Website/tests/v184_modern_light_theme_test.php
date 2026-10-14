<?php
$root=dirname(__DIR__);
$checks=[];
$checks['version']=version_compare(trim((string)file_get_contents($root.'/VERSION')),'1.8.4','>=');
$schema=(string)file_get_contents($root.'/migrations/schema.sql');
$migrate=(string)file_get_contents($root.'/bin/migrate.php');
$layout=(string)file_get_contents($root.'/templates/layout.php');
$controller=(string)file_get_contents($root.'/src/Controllers/AuthController.php');
$router=(string)file_get_contents($root.'/public/index.php');
$css=(string)file_get_contents($root.'/public/assets/app.css');
$js=(string)file_get_contents($root.'/public/assets/app.js');
$themeInit=(string)file_get_contents($root.'/public/assets/theme-init.js');
$live=(string)file_get_contents($root.'/templates/server_live.php');

$checks['schema ui_theme']=str_contains($schema,"ui_theme TEXT NOT NULL DEFAULT 'system'");
$checks['migration ui_theme']=str_contains($migrate,"ALTER TABLE users ADD COLUMN ui_theme TEXT NOT NULL DEFAULT 'system'");
$checks['route']=str_contains($router,"/profile/theme")&&str_contains($router,"changeTheme");
$checks['controller whitelist']=str_contains($controller,"['light','dark','system']")&&str_contains($controller,"UPDATE users SET ui_theme=?");
$checks['controller csrf']=str_contains($controller,"public function changeTheme():void")&&str_contains($controller,"Security::verifyCsrf()");
$checks['audit']=str_contains($controller,"auth.ui_theme.changed");
$checks['layout switch']=str_contains($layout,'data-ui-theme-mode="light"')&&str_contains($layout,'data-ui-theme-mode="dark"')&&str_contains($layout,'data-ui-theme-mode="system"');
$checks['early theme init']=strpos($layout,'theme-init.js')<strpos($layout,'app.css');
$checks['light css']=str_contains($css,'html[data-theme-resolved="light"]')&&str_contains($css,'--bg:#f4f6f9');
$checks['system init']=str_contains($themeInit,'prefers-color-scheme: light')&&str_contains($js,"media.addEventListener('change',syncSystem)");
$checks['network labels']=str_contains($js,'Download ${formatBytes(data.network_rx_bytes)} · Upload ${formatBytes(data.network_tx_bytes)}')&&str_contains($live,'Download / Upload seit Start');
$checks['no visible RX/TX']=!preg_match('/\bRX\b|\bTX\b/',$live.$js);

$failed=array_keys(array_filter($checks,fn($ok)=>!$ok));
foreach($checks as $name=>$ok) echo ($ok?'OK  ':'FAIL')." $name\n";
if($failed){fwrite(STDERR,"Fehlgeschlagen: ".implode(', ',$failed)."\n");exit(1);} 
echo "v1.8.4 Modern Light Theme Test bestanden.\n";
