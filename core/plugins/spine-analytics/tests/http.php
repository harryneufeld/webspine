<?php
declare(strict_types=1);
require dirname(__DIR__,4).'/core/bootstrap.php';
require dirname(__DIR__).'/TrafficInsights.php';
require dirname(__DIR__).'/ReportAccess.php';
use Webspine\Providers\SpineAnalytics\{TrafficInsights,ReportAccess};
function httpCheck(bool $ok,string $label):void {if(!$ok)throw new RuntimeException($label);echo "PASS $label\n";}
function analyticsCopy(string $source,string $target):void {
    if(!is_dir($target))mkdir($target,0700,true);
    foreach(new DirectoryIterator($source) as $entry){if($entry->isDot())continue;if($entry->isDir())analyticsCopy($entry->getPathname(),$target.'/'.$entry->getFilename());else copy($entry->getPathname(),$target.'/'.$entry->getFilename());}
}
function analyticsRemove(string $path,string $allowed):void {
    $resolved=realpath($path);$base=realpath($allowed);
    if(!$resolved || !$base || !str_starts_with($resolved,$base.DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe cleanup');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())analyticsRemove($entry->getPathname(),$allowed);else unlink($entry->getPathname());}rmdir($path);
}
function analyticsCLI(string $root,array $args,string $input=''):array {
    $env=getenv();$env['WEBSPINE_SITE_ROOT']=$root;
    $p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'',dirname(__DIR__).'/cli.php',...$args],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes,null,$env);
    fwrite($pipes[0],$input);fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);return [proc_close($p),$out,$err];
}
$parent=sys_get_temp_dir();$root=$parent.'/spine-http-'.bin2hex(random_bytes(6));mkdir($root,0700);
$server=null;
try {
    analyticsCopy(dirname(__DIR__,4).'/core/tests/fixtures/website/site',$root.'/site');mkdir($root.'/config');mkdir($root.'/storage');mkdir($root.'/public');
    $password=bin2hex(random_bytes(16));[$code,$hash,$error]=analyticsCLI($root,['hash-password'],$password);
    httpCheck($code===0 && password_verify($password,trim($hash)) && !str_contains($hash,$password),'Hash CLI works before config or database installation');
    [$code]=analyticsCLI($root,['hash-password'],'short');httpCheck($code!==0,'Hash CLI rejects short input');
    $config=['providers'=>['storage'=>'sqlite','mail'=>null],'plugins'=>['spine-analytics'],'sqlite'=>['path'=>'storage/site.sqlite'],'spine_analytics'=>['enabled'=>true,'preview'=>true,'report_enabled'=>true,'password_hash'=>trim($hash),'pages'=>['/','/docs'],'device_metrics'=>true]];
    $configFile=$root.'/config/local.php';file_put_contents($configFile,'<?php return '.var_export($config,true).';');
    $config['spine_analytics']['password']=$password;
    $config['spine_analytics']['password_hash']=null;
    file_put_contents($configFile,'<?php return '.var_export($config,true).';');
    [$code,$out,$error]=analyticsCLI($root,['set-password']);
    $config=require $configFile;
    httpCheck($code===0 && $config['spine_analytics']['password']===null && password_verify($password,$config['spine_analytics']['password_hash']) && !str_contains($out.$error,$password),'CLI hashes private configured password and clears plaintext');
    $app=new \Webspine\App($root,$config);
    httpCheck(!is_file($root.'/storage/spine-analytics/counts.sqlite'),'Real App registration does not install analytics');
    $app->install();httpCheck($app->health()['ok'],'Local fixture installs and passes health');unset($app);
    [$code,$out,$error]=analyticsCLI($root,['install']);httpCheck($code===0,'Explicit analytics installer succeeds: '.$error);
    [$code,$out,$error]=analyticsCLI($root,['html','7']);httpCheck($code===0 && str_contains($out,'<style>') && str_contains($out,'spineanalytics') && !str_contains($out,'<script'),'CLI HTML export works standalone: '.$error);
    $bootstrap=var_export(dirname(__DIR__,4).'/core/bootstrap.php',true);
    file_put_contents($root.'/public/router.php','<?php require '.$bootstrap.'; $request=\\Webspine\\Request::fromGlobals(); $app=new \\Webspine\\App(dirname(__DIR__)); $app->handle($request)->send($request->method==="HEAD");');
    $socket=stream_socket_server('tcp://127.0.0.1:0',$errno,$error);if(!$socket)throw new RuntimeException($error);$address=stream_socket_get_name($socket,false);fclose($socket);
    $server=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-S',$address,'-t',$root.'/public',$root.'/public/router.php'],[0=>['pipe','r'],1=>['file',$root.'/server.log','a'],2=>['file',$root.'/server.log','a']],$pipes,$root);fclose($pipes[0]);
    for($i=0;$i<100;$i++){$probe=@stream_socket_client('tcp://'.$address,$errno,$error,.05);if($probe){fclose($probe);break;}usleep(50000);}
    $fetch=static function(string $path,array $headers=[])use($address):array {
        $context=stream_context_create(['http'=>['ignore_errors'=>true,'timeout'=>5,'header'=>implode("\r\n",$headers)]]);
        $body=file_get_contents('http://'.$address.$path,false,$context);$responseHeaders=$http_response_header??[];preg_match('/\s(\d{3})\s/',$responseHeaders[0]??'',$match);return [(int)($match[1]??0),$body,$responseHeaders];
    };
    [$status]=$fetch('/health');httpCheck($status===200,'HTTP fixture health');
    [$status,$body]=$fetch('/docs?secret=private@example.com',['User-Agent: Mozilla/5.0 (Windows NT 10.0) Chrome/123 Safari/537']);httpCheck($status===200,'HTTP page served');
    [$status]=$fetch('/missing',['User-Agent: GPTBot/1.4']);httpCheck($status===404,'HTTP error response');
    $fetch('/assets/theme/test-theme/style.css');
    [$status,$body,$headers]=$fetch('/_insights');httpCheck($status===401 && !str_contains($body,'Counted requests') && (bool)preg_grep('/^WWW-Authenticate:/i',$headers),'Preview cannot bypass HTTP authentication');
    [$status]=$fetch('/_insights',['Authorization: Basic '.base64_encode('analytics:incorrect')]);httpCheck($status===401,'Wrong HTTP password denied');
    [$status,$body,$headers]=$fetch('/_insights?days=7',['Authorization: Basic '.base64_encode('analytics:'.$password)]);
    httpCheck($status===200 && str_contains($body,'7 days') && (bool)preg_grep('/^Cache-Control: no-store$/i',$headers) && !(bool)preg_grep('/^Set-Cookie:/i',$headers),'Authenticated HTTP report is private and cookie-free');
    $stats=new TrafficInsights($root);for($i=0;$i<50;$i++){if($stats->report(7)['requests']>=2)break;usleep(20000);}
    $report=$stats->report(7);httpCheck($report['requests']===2 && $report['errors']===1 && $report['pages']['/docs']===1 && $report['headers']['browser']['Chrome']===1,'Shutdown records actual status and excludes report, health and assets');
    $config['spine_analytics']['password_hash']=null;file_put_contents($configFile,'<?php return '.var_export($config,true).';');
    [$status,$body]=$fetch('/_insights',['Authorization: Basic '.base64_encode('analytics:'.$password)]);httpCheck($status===503 && !str_contains($body,'Counted requests'),'Missing configured hash fails closed over HTTP');
    $config['spine_analytics']['enabled']=false;$config['spine_analytics']['preview']=false;$config['spine_analytics']['report_enabled']=false;file_put_contents($configFile,'<?php return '.var_export($config,true).';');
    [$status]=$fetch('/_insights');httpCheck($status===404,'Dashboard disabled by configuration');$fetch('/docs');usleep(50000);httpCheck($stats->report()['requests']===2,'Collection disabled independently');
} finally {
    if(is_resource($server)){proc_terminate($server);proc_close($server);}unset($stats);gc_collect_cycles();analyticsRemove($root,$parent);
}
