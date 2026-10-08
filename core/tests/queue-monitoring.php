<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/plugins/job-queue/bootstrap.php';
use Webspine\Jobs\{SqliteQueue,QueueHealth,HandlerRegistry,Worker};
function verify(bool $ok,string $name): void {if(!$ok)throw new RuntimeException($name);echo "PASS $name\n";}
if(($argv[1]??'')==='child'){
    $queue=new SqliteQueue($argv[2]);$run=$queue->workerStarted();usleep(10000);$queue->workerFinished($run);exit;
}
$root=sys_get_temp_dir().'/webspine-monitor-'.bin2hex(random_bytes(8));mkdir($root,0700);
$queue=new SqliteQueue($root);$db=null;
try{
    try{$queue->diagnostics();throw new LogicException('Uninstalled diagnostics accepted.');}catch(RuntimeException $e){if($e instanceof LogicException)throw $e;}
    verify(!is_dir($root.'/storage'),'Uninstalled monitoring creates no storage');
    $queue->install();$now=time();$empty=$queue->diagnostics($now);
    verify($empty['oldest_pending_age_seconds']===null && $empty['expired_leases']===0 && $empty['last_worker_started_at']===null,'Empty queue has explicit absent timestamps');
    verify(QueueHealth::check($queue,900,$now)['ok'] && !is_file($root.'/storage/job-queue/worker.json'),'Healthy empty diagnostics never write a heartbeat');
    $id=$queue->enqueue('test.pending',1,['recipient'=>'PRIVATE@example.test','secret'=>'PRIVATE_SUBMISSION'],'PRIVATE_DEDUPE');
    $db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');
    $db->prepare('UPDATE jobs SET created_at=?,available_at=? WHERE id=?')->execute([$now-2000,$now-1000,$id]);
    $health=QueueHealth::check($queue,900,$now);
    verify($health['warnings']===['overdue_jobs','worker_missing'] && !$health['ok'],'Missing cron and overdue pending job are reported');
    verify($health['queue']['oldest_pending_age_seconds']===2000 && $health['queue']['oldest_due_age_seconds']===1000,'Pending age and due age are distinct');
    verify(!str_contains(json_encode($health),'PRIVATE') && !str_contains(json_encode($health),$id),'Monitoring omits payloads, dedupe keys, recipient and job IDs');
    $db->prepare('UPDATE jobs SET available_at=? WHERE id=?')->execute([$now+3600,$id]);
    verify(QueueHealth::check($queue,900,$now)['ok'] && $queue->diagnostics($now)['oldest_due_age_seconds']===null,'Scheduled future jobs are not overdue');
    $first=$queue->workerStarted($now-1800);$second=$queue->workerStarted($now-1000);
    $queue->workerFinished($first,$now-999);
    $health=QueueHealth::check($queue,900,$now);
    verify($health['warnings']===['worker_run_incomplete'] && $health['queue']['latest_worker_run_finished']===false,'Finishing an older overlapping run cannot mask the newest unfinished run');
    $queue->workerFinished($second,$now-998);
    verify(QueueHealth::check($queue,900,$now)['ok'],'Finished worker does not warn solely because it has no due work');
    $db->prepare('UPDATE jobs SET available_at=? WHERE id=?')->execute([$now-1000,$id]);
    verify(QueueHealth::check($queue,900,$now)['warnings']===['overdue_jobs','worker_stale'],'Overdue work and stale worker activity are reported together');
    (new Worker($queue,new HandlerRegistry()))->run();
    $status=$queue->diagnostics($now);
    verify($status['latest_worker_run_finished']===true && $status['last_worker_age_seconds']===0 && $queue->status()['pending']===1,'Empty or paused worker records activity without consuming jobs');
    verify(QueueHealth::check($queue,900,$now)['warnings']===['overdue_jobs'],'A running cron cannot hide an undelivered backlog with absent handlers');
    $job=$queue->claim(['test.pending'],1,$now-10);
    // The pending fixture was due before the synthetic claim time.
    verify($job!==null && QueueHealth::check($queue,900,$now)['warnings']===['expired_leases'],'Expired processing leases are reported without changing job state');
    verify($queue->status()['processing']===1,'Monitoring never claims or fails jobs');
    $replacement=$queue->claim(['test.pending'],60,$now);$queue->fail($replacement,'invalid_payload',true,$now);
    verify(QueueHealth::check($queue,900,$now)['warnings']===['failed_jobs'],'Permanent failures remain visible for operator review');
    $broken=new HandlerRegistry();$broken->register('test.broken',new class implements \Webspine\Jobs\Handler{
        public function ready():bool{throw new RuntimeException('PRIVATE_CONFIGURATION');}
        public function handle(\Webspine\Jobs\Job $job):void{throw new LogicException('Unexpected handling');}
    });
    try{(new Worker($queue,$broken))->run();throw new LogicException('Worker unexpectedly returned.');}catch(RuntimeException $e){if($e instanceof LogicException)throw $e;}
    verify(QueueHealth::check($queue,900,$now+1000)['warnings']===['failed_jobs','worker_run_incomplete'],'An aborted worker does not record a normal finish');
    $children=[];
    for($i=0;$i<8;$i++){$p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'',__FILE__,'child',$root],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);fclose($pipes[0]);$children[]=[$p,$pipes];}
    foreach($children as [$p,$pipes]){$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);verify(proc_close($p)===0,'Concurrent heartbeat writer finished: '.$out.$err);}
    $status=$queue->diagnostics();
    verify($status['latest_worker_run_finished']===true && is_int($status['last_worker_finished_at']) && filesize($root.'/storage/job-queue/worker.json')<1024,'Concurrent heartbeat writes retain bounded valid metadata');
    if(PHP_OS_FAMILY!=='Windows')verify((fileperms($root.'/storage/job-queue/worker.json')&0777)===0660,'Heartbeat is private and writable by the shared storage group');
    foreach([59,604801] as $threshold){try{QueueHealth::check($queue,$threshold);throw new LogicException('Invalid threshold accepted.');}catch(InvalidArgumentException){}}
    verify(QueueHealth::check($queue,60)['stale_after_seconds']===60,'Health threshold bounds are enforced');
    $legacy=new class implements \Webspine\Jobs\Queue {
        public function install():void{throw new LogicException('Unexpected install');}
        public function enqueue(string $type,int $version,array $payload,?string $dedupe=null,int $delay=0,int $maxAttempts=5):string{throw new LogicException('Unexpected enqueue');}
        public function claim(array $types,int $leaseSeconds=300,?int $now=null):?\Webspine\Jobs\Job{return null;}
        public function complete(\Webspine\Jobs\Job $job,?int $now=null):bool{throw new LogicException('Unexpected complete');}
        public function fail(\Webspine\Jobs\Job $job,string $code,bool $permanent=false,?int $now=null):bool{throw new LogicException('Unexpected fail');}
        public function renew(\Webspine\Jobs\Job $job,int $seconds=300,?int $now=null):bool{throw new LogicException('Unexpected renew');}
        public function retry(string $id,?int $now=null):bool{throw new LogicException('Unexpected retry');}
        public function status():array{return ['pending'=>0,'processing'=>0,'completed'=>0,'failed'=>0];}
        public function prune(int $days=30):int{throw new LogicException('Unexpected prune');}
    };
    verify((new Worker($legacy,new HandlerRegistry()))->run()['completed']===0 && QueueHealth::check($legacy)['warnings']===['monitor_unavailable'],'Existing Queue-only provider remains usable with an explicit unsupported-monitor warning');
    file_put_contents($root.'/storage/job-queue/worker.json','{"secret":"DO_NOT_PRINT"}');
    try{$queue->diagnostics();throw new LogicException('Malformed heartbeat accepted.');}catch(RuntimeException $e){if($e instanceof LogicException)throw $e;verify(!str_contains($e->getMessage(),'DO_NOT_PRINT'),'Malformed metadata fails without exposing its contents');}
    echo "Queue monitoring tests passed; no network or mail used.\n";
}finally{
    $db=null;$queue=null;
    foreach(['jobs.sqlite','jobs.sqlite-wal','jobs.sqlite-shm','jobs.sqlite-journal','worker.json'] as $name){$path=$root.'/storage/job-queue/'.$name;if(is_file($path))unlink($path);}
    rmdir($root.'/storage/job-queue');rmdir($root.'/storage');rmdir($root);
}
