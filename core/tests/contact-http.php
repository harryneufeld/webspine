<?php
declare(strict_types=1);
$prepare=require __DIR__.'/contact-fixture.php';
$source=dirname(__DIR__,2);$root=$source.'/storage/contact-http-test-'.bin2hex(random_bytes(6));$server=null;
function contactProcess(array $args,string $root): array {
    $p=proc_open($args,[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes,$root);
    fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);
    return ['code'=>proc_close($p),'out'=>$out,'err'=>$err];
}
function contactRemove(string $path,string $base): void {
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe fixture cleanup.');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())contactRemove($entry->getPathname(),$base);else unlink($entry->getPathname());}
    rmdir($path);
}
try{
    $prepare($root,'/inquiry');
    $socket=stream_socket_server('tcp://127.0.0.1:0',$errno,$error);
    if(!$socket)throw new RuntimeException('Cannot reserve local test port: '.$error);
    $address=stream_socket_get_name($socket,false);fclose($socket);
    $server=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-d','display_errors=0','-S',$address,'-t',$root.'/public',$root.'/public/router.php'],
        [0=>['pipe','r'],1=>['file',$root.'/storage/server.log','a'],2=>['file',$root.'/storage/server.log','a']],$pipes,$root);
    fclose($pipes[0]);$ready=false;
    for($i=0;$i<50;$i++){if($probe=@stream_socket_client('tcp://'.$address,$errno,$error,.1)){fclose($probe);$ready=true;break;}usleep(100000);}
    if(!$ready)throw new RuntimeException('Contact fixture server did not start.');
    $result=contactProcess([PHP_BINARY,'-c',php_ini_loaded_file()?:'',__DIR__.'/hosting/form-check.php','http://'.$address,'/inquiry',$root],$root);
    echo $result['out'];if($result['code']!==0)throw new RuntimeException($result['err']);
    // The worker must obey update recovery boundaries without altering jobs.
    \Webspine\Files::write($root.'/storage/update-pending.json','{}');
    $result=contactProcess([PHP_BINARY,'-c',php_ini_loaded_file()?:'',$root.'/core/plugins/job-queue/cli.php','work'],$root);
    unlink($root.'/storage/update-pending.json');
    if($result['code']===0)throw new RuntimeException('Worker ignored pending recovery.');
    echo "PASS Queue CLI refuses interrupted update\n";
    // CLI progress cannot break sessions when checks remain in a subprocess.
    $script='require '.var_export($root.'/core/bootstrap.php',true).';$app=new \\Webspine\\App('.var_export($root,true).');echo "progress\n";try{$app->handle(new \\Webspine\\Request("GET","/inquiry"));exit(1);}catch(RuntimeException $e){if(!str_contains($e->getMessage(),"after output"))throw $e;echo "PASS Early output has an actionable session diagnostic\n";}';
    $result=contactProcess([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-d','output_buffering=0','-r',$script],$root);
    if($result['code']!==0)throw new RuntimeException($result['err'].$result['out']);echo $result['out'];
    echo "Contact HTTP tests passed; captured mail only.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n");$code=1;}
finally{if(is_resource($server)){proc_terminate($server);proc_close($server);}if(is_dir($root))contactRemove($root,$source.'/storage');}
exit($code??0);
