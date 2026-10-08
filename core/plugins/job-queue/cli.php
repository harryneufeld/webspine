<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
$root=dirname(__DIR__,3);$lock=null;
try {
    $lock=fopen($root.'/storage/update.lock','c+');
    if (!$lock || !flock($lock,LOCK_SH)) throw new RuntimeException('Cannot lock framework.');
    if (is_file($root.'/storage/update-pending.json')) throw new RuntimeException('Recover the interrupted framework update first.');
    require $root.'/core/bootstrap.php';
    $app=new \Webspine\App($root);
    $queue=$app->services->get(\Webspine\Jobs\Queue::class);
    $command=$argv[1]??'status';
    $result=match ($command) {
        'install'=>(static function () use ($queue) { $queue->install();return ['installed'=>true]; })(),
        'status'=>$queue->status(),
        'work'=>(new \Webspine\Jobs\Worker($queue,$app->services->get(\Webspine\Jobs\Handlers::class)))->run(),
        'retry'=>['requeued'=>$queue->retry($argv[2]??throw new InvalidArgumentException('Usage: retry <job-id>'))],
        'prune'=>['pruned'=>$queue->prune(isset($argv[2]) ? (filter_var($argv[2], FILTER_VALIDATE_INT) !== false ? (int)$argv[2] : throw new InvalidArgumentException('Usage: prune [days]')) : 30)],
        default=>throw new InvalidArgumentException('Commands: install, status, work, retry <job-id>, prune [days]'),
    };
    echo json_encode($result,JSON_PRETTY_PRINT|JSON_THROW_ON_ERROR)."\n";
} catch (Throwable $e) {
    $cause=$e->getPrevious()??$e;
    $file=str_replace('\\','/',$cause->getFile());$prefix=rtrim(str_replace('\\','/',$root),'/').'/';
    if(str_starts_with($file,$prefix))$file=substr($file,strlen($prefix));
    fwrite(STDERR,'Queue command failed: '.$cause::class.' ['.$file.', line '.$cause->getLine()."]. Check configuration, installation and update state.\n");exit(1);
}
finally { if (is_resource($lock)) { flock($lock,LOCK_UN);fclose($lock); } }
