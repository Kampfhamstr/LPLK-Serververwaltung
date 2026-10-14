<?php
// Local-only contract tests: execute the real recovery with synthetic files and a fake Panel DB.
$source=file_get_contents(__DIR__.'/../portal/Diagnostics/InnerCircleRecovery.php');
$base=str_replace('\\','/',realpath($argv[1]??''));if(!$base||!is_dir($base))throw new RuntimeException('Explicit local fixture directory required');
$root=$base.'/recovery-fixture';if(file_exists($root))throw new RuntimeException('Fixture occupied');mkdir($root);
$sw=$root.'/game/csgo/addons/swiftlys2';
foreach(['bin/linuxsteamrt64','bin/managed','gamedata/cs2/core','configs','plugins','data/serververwaltung_bridge','translations'] as $dir)mkdir($sw.'/'.$dir,0777,true);
mkdir($root.'/egg');file_put_contents($root.'/egg/versions.txt',"Swiftly=v1.5.0-beta.11\nOther=unchanged\n");
file_put_contents($sw.'/bin/linuxsteamrt64/swiftlys2.so','old-native');
file_put_contents($sw.'/bin/managed/SwiftlyS2.CS2.deps.json','{"libraries":{"SwiftlyS2.CS2/1.5.0-beta.11":{}}}');
file_put_contents($sw.'/gamedata/cs2/core/signatures.jsonc','old-gamedata');
file_put_contents($sw.'/data/serververwaltung_bridge/status.json',json_encode(['updated_at'=>date('c'),'players'=>[],'plugins'=>[]]));
foreach(['configs','plugins','data','translations'] as $n)file_put_contents($sw.'/'.$n.'/preserve.txt','user-content');
$zip=new ZipArchive();$zip->open($base.'/fixture.zip',ZipArchive::CREATE|ZipArchive::OVERWRITE);$prefix='swiftlys2-linux-v1.4.14-with-runtimes/addons/swiftlys2/';
foreach(['bin/linuxsteamrt64/swiftlys2.so'=>'new-native','bin/managed/SwiftlyS2.CS2.deps.json'=>'{"libraries":{"SwiftlyS2.CS2/1.4.14":{}}}','gamedata/cs2/core/signatures.jsonc'=>'new-gamedata','configs/preserve.txt'=>'MUST-NOT-OVERWRITE'] as $n=>$v)$zip->addFromString($prefix.$n,$v);$zip->close();
$stub=<<<'PHP'
class FakeDb {private string $table='';function table($t){$this->table=$t;return $this;}function where(...$x){return $this;}function join(...$x){return $this;}function whereIn(...$x){return $this;}function pluck(...$x){return $this;}function all(){return ['PRERELEASE'=>'0','INSTALL_SWIFTLY'=>'1'];}function first(...$x){return $this->table==='servers'?(object)['id'=>1,'allocation_id'=>1]:(object)['ip'=>'203.0.113.10','port'=>27019];}} $db=new FakeDb();
PHP;
$source=preg_replace('~ require getcwd\(\).*?\$db=\$app->make\(\x27db\x27\);~',$stub,$source);
$source=str_replace("\$root='/var/lib/pterodactyl/volumes/'.\$uuid;","\$root=".var_export($root,true).';',$source);
$source=str_replace('c51c4a7962fee5efabbaff2789c247cf64897d5d69e22f30bc98b53d1cc34594',hash('sha256','old-native'),$source);
$source=str_replace('d3b9a459f5a5dc984348b6033882f05c07ba62c90b4caa1cdcaa85ac46a865cc',hash_file('sha256',$base.'/fixture.zip'),$source);
// Windows fixture adapter: canonical separators and ownership have no Unix semantics here.
$source=str_replace('realpath($p)!==$p',"str_replace('\\\\','/',realpath(\$p))!==str_replace('\\\\','/',\$p)",$source);
$source=str_replace('$f->getPathname()',"str_replace('\\\\','/',\$f->getPathname())",$source);
$source=str_replace(['chown(','chgrp('],['testOwn(','testOwn('],$source);
$source=str_replace('try {',"function testOwn(...\$args):bool{return true;}\ntry {",$source, $replacements);
// Only insert adapter once, not the nested transaction's try.
$source=preg_replace('/function testOwn\(\.\.\.\$args\):bool\{return true;\}\n(?=try \{)/','',$source,1);
$source=str_replace('<?php',"<?php\nfunction testOwn(...\$args):bool{return true;}", $source);
// Remove duplicate adapter before inner try.
$first=strpos($source,'function testOwn');$second=strpos($source,'function testOwn',$first+1);if($second!==false)$source=substr_replace($source,'',$second,strlen("function testOwn(...\$args):bool{return true;}\n"));
$checks=0;function ok($v,$msg){global $checks;$checks++;if(!$v)throw new RuntimeException($msg);}
function runPhase($phase,$state='exited') {global $source,$base;$s=str_replace('__LPLK_RECOVERY_PHASE__',$phase,$source);$s=preg_replace('~ \$state=trim\(\(string\)shell_exec\(.*?;~'," \$state=".var_export($state,true).';',$s);file_put_contents($base.'/run.php',$s);$p=proc_open([PHP_BINARY,'-d','extension='.dirname(PHP_BINARY).'/ext/php_zip.dll',$base.'/run.php'],[1=>['pipe','w'],2=>['pipe','w']],$pipes);$output=stream_get_contents($pipes[1]);$error=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);$code=proc_close($p);return [$code,json_decode($output,true),$error];}
[$c,$r,$e]=runPhase('check','running');ok($c===0&&$r['readOnly']===true,'read-only precheck '.$e);ok(!is_dir($root.'/.lplk-framework-recovery'),'check creates no stage');
[$c,$r,$e]=runPhase('apply','running');ok($c!==0&&str_contains($r['reason']??'','stopped'),'live mutation refused');
[$c,$r,$e]=runPhase('prepare');ok($c===0,'prepare '.$e);$stage=$root.'/.lplk-framework-recovery';copy($base.'/fixture.zip',$stage.'/package.zip');
[$c,$r,$e]=runPhase('apply');ok($c===0&&($r['applied']??false),'apply '.json_encode($r).$e);
ok(file_get_contents($sw.'/bin/linuxsteamrt64/swiftlys2.so')==='new-native','candidate activated');
ok(file_get_contents($stage.'/old-bin/linuxsteamrt64/swiftlys2.so')==='old-native','original saved');
foreach(['configs','plugins','data','translations'] as $n)ok(file_get_contents($sw.'/'.$n.'/preserve.txt')==='user-content','preserve '.$n);
ok(file_get_contents($root.'/egg/versions.txt')==="Swiftly=v1.4.14\nOther=unchanged\n",'only Swiftly marker changed');
[$c,$r,$e]=runPhase('start-check');ok($c===0&&$r['readyToStart'],'verified start '.$e);
[$c,$r,$e]=runPhase('rollback');ok($c===0&&$r['rolledBack'],'rollback '.$e);ok(file_get_contents($sw.'/bin/linuxsteamrt64/swiftlys2.so')==='old-native','native restored');
ok(file_get_contents($root.'/egg/versions.txt')==="Swiftly=v1.5.0-beta.11\nOther=unchanged\n",'markers restored');
echo json_encode(['checks'=>$checks,'passed'=>true]),"\n";
