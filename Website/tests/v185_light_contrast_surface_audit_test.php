<?php
$root=dirname(__DIR__);
$css=(string)file_get_contents($root.'/public/assets/app.css');
$layout=(string)file_get_contents($root.'/templates/layout.php');
$version=trim((string)file_get_contents($root.'/VERSION'));
$checks=[];
$checks['version']=version_compare($version,'1.8.5','>=');
$checks['cache css']=str_contains($layout,'app.css?v='.$version);
$checks['cache js']=str_contains($layout,'app.js?v='.$version);
$checks['cache theme']=str_contains($layout,'theme-init.js?v='.$version);
$checks['chat text dark']=str_contains($css,'html[data-theme-resolved="light"] .server-chat-text{color:#344054}');
$checks['chat meta']=str_contains($css,'html[data-theme-resolved="light"] .server-chat-meta time');
$checks['chat role founder']=str_contains($css,'html[data-theme-resolved="light"] .game-role-text-founder{color:#991b3a}');
$checks['chat role member']=str_contains($css,'html[data-theme-resolved="light"] .game-role-text-member{color:#245b96}');
$checks['day separator']=str_contains($css,'html[data-theme-resolved="light"] .server-chat-day-separator span{background:#fff');
$checks['active button light']=str_contains($css,'html[data-theme-resolved="light"] .btn-active{background:#eaf8ef!important');
$checks['emergency light']=str_contains($css,'html[data-theme-resolved="light"] .ptero-emergency-control{background:#fff7f7');
$checks['team spec light']=str_contains($css,'html[data-theme-resolved="light"] .team-spec')&&str_contains($css,'background:#f0f2f5');
$checks['permissions dangerous light']=str_contains($css,'.permission-check.is-dangerous')&&str_contains($css,'background:linear-gradient(90deg,#fff1f2,#fff 44%)');
$checks['activity hover light']=str_contains($css,'.activity-digest-link-row:hover')&&str_contains($css,'background:#eef3f8');
$checks['ban evidence light']=str_contains($css,'.ban-evidence-card blockquote')&&str_contains($css,'background:#fffdf5');
$failed=array_keys(array_filter($checks,static fn($ok)=>!$ok));
foreach($checks as $name=>$ok) echo ($ok?'OK  ':'FAIL')." $name\n";
if($failed){fwrite(STDERR,"Fehlgeschlagen: ".implode(', ',$failed)."\n");exit(1);} 
echo "v1.8.5+ Light Theme Contrast & Surface Audit Test bestanden.\n";
