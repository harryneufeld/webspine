<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
$root=dirname(__DIR__,3);$lock=null;$exitCode=0;
try {
    $lock=fopen($root.'/storage/update.lock','c+');
    if (!$lock || !flock($lock,LOCK_SH)) throw new RuntimeException('Cannot lock framework.');
    if (is_file($root.'/storage/update-pending.json')) throw new RuntimeException('Recover the interrupted framework update first.');
    require $root.'/core/bootstrap.php';
    $app=new \Webspine\App($root);
    $commands=new \Webspine\ConsoleCommands();
    \Webspine\Jobs\QueueCommands::register($commands,$app);
    $command=$argv[1]??'status';
    $exitCode=$commands->run('job-queue:'.$command,array_slice($argv,2));
} catch (Throwable $e) {
    $cause=$e->getPrevious()??$e;
    $file=str_replace('\\','/',$cause->getFile());$prefix=rtrim(str_replace('\\','/',$root),'/').'/';
    if(str_starts_with($file,$prefix))$file=substr($file,strlen($prefix));
    fwrite(STDERR,'Queue command failed: '.$cause::class.' ['.$file.', line '.$cause->getLine()."]. Check configuration, installation and update state.\n");$exitCode=1;
}
finally { if (is_resource($lock)) { flock($lock,LOCK_UN);fclose($lock); } }
exit($exitCode);
