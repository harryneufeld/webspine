<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
use Webspine\{App,Files,ConsoleCommands};
$source=dirname(__DIR__,2);$root=$source.'/storage/console-test-'.bin2hex(random_bytes(6));$results=[];$app=null;$db=null;$queue=null;
function commandCheck(bool $ok,string $name):void {global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name;}
function copyConsoleFixture(string $source,string $target):void {
    if(!is_dir($target))mkdir($target,0700,true);
    foreach(new DirectoryIterator($source) as $entry){if($entry->isDot())continue;if($entry->isLink())throw new RuntimeException('Unexpected fixture link.');if($entry->isDir())copyConsoleFixture($entry->getPathname(),$target.'/'.$entry->getFilename());else copy($entry->getPathname(),$target.'/'.$entry->getFilename());}
}
function removeConsoleFixture(string $path,string $base):void {
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe cleanup.');
    foreach(new DirectoryIterator($path)as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())removeConsoleFixture($entry->getPathname(),$base);else unlink($entry->getPathname());}rmdir($path);
}
function consoleProcess(string $root,array $args,bool $legacy=false):array {
    $entry=$root.'/core/'.($legacy?'plugins/job-queue/cli.php':'bin/console.php');
    $env=getenv();unset($env['WEBSPINE_SITE_ROOT'],$env['WEBSPINE_CORE_ROOT'],$env['WEBSPINE_UPDATE_PROBE']);
    $process=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'',$entry,...$args],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes,$root,$env);
    fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);return ['code'=>proc_close($process),'out'=>$out,'err'=>$err];
}
function badCommand(callable $call):bool {try{$call();}catch(InvalidArgumentException){return true;}return false;}
try {
    copyConsoleFixture($source.'/core',$root.'/core');copyConsoleFixture(__DIR__.'/fixtures/website/site',$root.'/site');
    copyConsoleFixture(__DIR__.'/fixtures/console-plugin',$root.'/site/plugins/console-demo');mkdir($root.'/storage');mkdir($root.'/config');mkdir($root.'/.dist');
    $config=['providers'=>['storage'=>'sqlite','mail'=>null],'plugins'=>['console-demo','job-queue'],'sqlite'=>['path'=>'storage/site.sqlite']];
    $write=static fn(array $value)=>Files::write($root.'/config/local.php','<?php return '.var_export($value,true).';');$write($config);
    $app=new App($root);commandCheck(!is_file($root.'/storage/command-discovery'),'Web App boot does not invoke CLI discovery');
    $help=consoleProcess($root,['help']);
    commandCheck($help['code']===0 && $help['err']==='' && str_contains($help['out'],'demo:echo <value> [extra]') && str_contains($help['out'],'job-queue:work'),'Help discovers enabled plugin and queue commands');
    commandCheck(!is_file($root.'/storage/site.sqlite') && !is_file($root.'/storage/job-queue/jobs.sqlite'),'Help never installs site or queue storage');
    $detail=consoleProcess($root,['help','demo:echo']);commandCheck($detail['code']===0 && str_contains($detail['out'],'Usage: php core/bin/console.php demo:echo <value> [extra]'),'Per-command help exposes usage');
    $coreDetail=consoleProcess($root,['help','recover']);commandCheck($coreDetail['code']===0 && str_contains($coreDetail['out'],'Restore after interrupted activation'),'Core command help is discoverable');
    $result=consoleProcess($root,['demo:echo','--literal','a b']);
    commandCheck($result['code']===0 && $result['err']==='' && json_decode($result['out'],true)===['--literal','a b'] && !is_file($root.'/storage/site.sqlite'),'Literal positional arguments run before storage installation');
    foreach([['demo:echo'],['demo:echo','one','two','three'],['demo:fail','extra'],['help','demo:echo','extra'],['demo:missing']] as $args) {
        $r=consoleProcess($root,$args);commandCheck($r['code']===1 && $r['out']==='' && $r['err']!=='','Unknown commands and invalid argument counts fail on stderr');
    }
    $r=consoleProcess($root,['demo:fail']);commandCheck($r['code']===7 && $r['out']==='' && $r['err']==="Fixture failure\n",'Handler failure status and stderr are preserved');
    $r=consoleProcess($root,['demo:throw']);commandCheck($r['code']===1 && $r['out']==='' && str_contains($r['err'],'Fixture exception'),'Exceptions report local diagnostics and fail');
    $r=consoleProcess($root,['demo:bad-status']);commandCheck($r['code']===1 && str_contains($r['err'],'integer exit status'),'Noninteger exit status is rejected');
    $r=consoleProcess($root,['demo:lock']);commandCheck($r['code']===0 && trim($r['out'])==='locked','Plugin execution holds the shared update lock');
    $handle=fopen($root.'/storage/update.lock','c+');$locked=flock($handle,LOCK_EX|LOCK_NB);commandCheck($locked,'CLI releases update lock after return');if($locked)flock($handle,LOCK_UN);fclose($handle);
    foreach(['duplicate','collision'] as $mode){$changed=$config;$changed['console_fixture']=$mode;$write($changed);$r=consoleProcess($root,['help']);commandCheck($r['code']===1 && $r['out']==='' && str_contains($r['err'],$mode==='duplicate'?'Duplicate command':'Reserved core command'),'Duplicate and reserved core names are rejected before dispatch');}
    $changed=$config;$changed['plugins']=['job-queue'];$write($changed);
    $r=consoleProcess($root,['help']);$disabled=consoleProcess($root,['demo:echo','one']);commandCheck($r['code']===0 && !str_contains($r['out'],'demo:echo') && $disabled['code']===1,'Disabled plugin commands are neither listed nor executable');
    $write($config);
    $r=consoleProcess($root,['job-queue:install']);commandCheck($r['code']===0 && json_decode($r['out'],true)['installed']===true && !is_file($root.'/storage/site.sqlite'),'Queue installation is explicit and independent of site installation');
    $r=consoleProcess($root,['job-queue:status']);$legacy=consoleProcess($root,['status'],true);commandCheck($r['code']===0 && $legacy['code']===0 && json_decode($r['out'],true)===json_decode($legacy['out'],true),'Unified queue status matches the legacy CLI');
    $r=consoleProcess($root,['job-queue:work']);commandCheck($r['code']===0 && json_decode($r['out'],true)['completed']===0,'Unified worker executes without implicit site installation');
    $r=consoleProcess($root,['job-queue:health']);$legacy=consoleProcess($root,['health'],true);$health=json_decode($r['out'],true);$oldHealth=json_decode($legacy['out'],true);
    commandCheck($r['code']===0 && $legacy['code']===0 && $health['ok']===$oldHealth['ok'] && $health['warnings']===$oldHealth['warnings'] && array_keys($health)===array_keys($oldHealth),'Unified queue health matches legacy output and exit status');
    $queue=$app->services->get(\Webspine\Jobs\Queue::class);$queue->enqueue('fixture.pending',1,[]);
    $db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');$db->prepare('UPDATE jobs SET created_at=?, available_at=?')->execute([time()-1000,time()-1000]);
    $r=consoleProcess($root,['job-queue:health']);$legacy=consoleProcess($root,['health'],true);$health=json_decode($r['out'],true);$oldHealth=json_decode($legacy['out'],true);
    commandCheck($r['code']===1 && $legacy['code']===1 && $r['err']==='' && $legacy['err']==='' && !$health['ok'] && $health['warnings']===$oldHealth['warnings'] && in_array('overdue_jobs',$health['warnings'],true),'Unified queue health preserves nonzero warning status and JSON diagnostics');
    foreach([['job-queue:retry'],['job-queue:prune','bad'],['job-queue:work','extra']] as $args){$r=consoleProcess($root,$args);commandCheck($r['code']===1 && $r['out']==='','Queue command arguments are validated');}
    $changed=$config;$changed['console_fixture']='collision';$write($changed);unlink($root.'/storage/command-discovery');
    foreach([['install'],['health']] as $args){$r=consoleProcess($root,$args);commandCheck($r['code']===0 && !is_file($root.'/storage/command-discovery'),'Core app commands do not invoke plugin command discovery');}
    Files::write($root.'/config/local.php','<?php this is invalid');if(is_file($root.'/storage/command-discovery'))unlink($root.'/storage/command-discovery');
    $r=consoleProcess($root,['help','--core']);commandCheck($r['code']===0 && str_contains($r['out'],'recover') && !is_file($root.'/storage/command-discovery'),'Core-only help works with broken site configuration');
    $r=consoleProcess($root,['package']);commandCheck($r['code']===0 && !is_file($root.'/storage/command-discovery'),'Packaging does not boot site plugins');
    foreach([['update',$root.'/missing.zip'],['rollback'],['recover']] as $args){$r=consoleProcess($root,$args);commandCheck($r['code']===1 && !str_contains($r['err'],'config/local.php') && !is_file($root.'/storage/command-discovery'),'Maintenance commands do not boot configuration or plugin discovery');}
    $write($config);Files::write($root.'/storage/update-pending.json','{}');
    foreach([['demo:echo','one'],['help'],['job-queue:work']] as $args){$r=consoleProcess($root,$args);commandCheck($r['code']===1 && $r['out']==='' && str_contains($r['err'],'Interrupted update') && !is_file($root.'/storage/command-discovery'),'Interrupted activation blocks discovery and plugin execution');}
    unlink($root.'/storage/update-pending.json');
    $commands=new ConsoleCommands();
    foreach(['unqualified','bad:name:extra','../demo:run','DEMO:run'] as $name)commandCheck(badCommand(fn()=>$commands->register($name,'Description',static fn(array $args):int=>0)),'Invalid command names are rejected');
    commandCheck(badCommand(fn()=>$commands->register('demo:bad',"bad\nhelp",static fn(array $args):int=>0)) && badCommand(fn()=>$commands->register('demo:bad','Description',static fn(array $args):int=>0,2,1)),'Invalid metadata and argument bounds are rejected');
    foreach([-1,126,null,true] as $status){$registry=new ConsoleCommands();$registry->register('demo:status','Status',static fn(array $args)=>$status);try{$registry->run('demo:status',[]);throw new LogicException('Invalid status accepted.');}catch(RuntimeException $e){if($e instanceof LogicException)throw $e;}}
    foreach($results as $name)echo "PASS $name\n";echo "Console command tests passed; disposable local configuration and data only.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n".$e->getTraceAsString()."\n");$code=1;}
finally{$db=null;$queue=null;$app=null;gc_collect_cycles();if(is_dir($root))removeConsoleFixture($root,$source.'/storage');}
exit($code??0);
