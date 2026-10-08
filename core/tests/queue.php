<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/plugins/job-queue/bootstrap.php';
use Webspine\Jobs\{SqliteQueue,HandlerRegistry,Handler,Job,Worker,PermanentFailure};
function check(bool $ok,string $name): void { if (!$ok) throw new RuntimeException($name);echo "PASS $name\n"; }
function rejects(callable $callback): bool { try { $callback();return false; } catch (Throwable $e) { return true; } }
if (($argv[1]??'')==='child') {
    $queue=new SqliteQueue($argv[2]);$count=0;
    while ($job=$queue->claim(['test.parallel'],60)) { usleep(1000);if (!$queue->complete($job)) exit(2);$count++; }
    echo $count;exit;
}
$root=sys_get_temp_dir().'/webspine-queue-'.bin2hex(random_bytes(8));mkdir($root,0700);
$queue=new SqliteQueue($root);$checks=0;
try {
    check(!is_dir($root.'/storage'),'Construction never installs or writes storage');
    check(rejects(fn()=>$queue->enqueue('test.one',1,['x'=>1])),'Uninstalled queue rejects writes');
    $queue->install();$queue->install();
    check((new SqliteQueue($root))->status()['pending']===0,'Explicit installation is idempotent');
    $payload=['nested'=>['extra'=>['a'=>1,'b'=>true,'c'=>null]],'unicode'=>'Grüße','list'=>[1,2,3]];
    $id=$queue->enqueue('test.one',2,$payload,'same');
    check($queue->enqueue('test.one',2,$payload,'same')===$id,'Submission key returns the durable original job');
    check($queue->enqueue('test.one',2,array_reverse($payload,true),'same')===$id,'Object key ordering does not change deduplication identity');
    check(rejects(fn()=>$queue->enqueue('test.one',2,['different'=>true],'same')),'Conflicting dedupe data is rejected');
    check($queue->enqueue('test.two',2,$payload,'same')!==$id,'Deduplication is scoped to job type');
    check(rejects(fn()=>$queue->enqueue('../php',1,[])),'Executable/path job types rejected');
    check(rejects(fn()=>$queue->enqueue('test.invalid',0,[])),'Invalid payload versions rejected');
    check(rejects(fn()=>$queue->enqueue('test.invalid',1,['x'=>new stdClass])),'PHP objects are never serialized');
    check(rejects(fn()=>$queue->enqueue('test.invalid',1,['x'=>INF])),'Non-JSON numeric values rejected');
    check(rejects(fn()=>$queue->enqueue('test.invalid',1,['x'=>str_repeat('x',65537)])),'Payload byte bound enforced');
    $deep=[];for($i=0;$i<20;$i++)$deep=['x'=>$deep];
    check(rejects(fn()=>$queue->enqueue('test.invalid',1,$deep)),'Payload depth bound enforced');
    check($queue->claim(['unregistered.type'])===null,'Unregistered types remain untouched');
    $now=time();$job=$queue->claim(['test.one'],10,$now);
    check($job->payload==$payload && $job->version===2,'Extensible nested payload and version round-trip');
    check($queue->claim(['test.one'],10,$now+9)===null,'Active lease prevents simultaneous claims');
    check($queue->renew($job,20,$now+5),'Active claim can renew its lease');
    check($queue->claim(['test.one'],10,$now+15)===null,'Renewal extends exclusivity');
    $replacement=$queue->claim(['test.one'],10,$now+26);
    check($replacement!==null && $replacement->attempt===2,'Expired claims recover with another attempt');
    check(!$queue->complete($job,$now+27) && !$queue->fail($job,'old_worker',false,$now+27) && !$queue->renew($job,20,$now+27),'Stale worker cannot complete, fail or renew replacement');
    check($queue->complete($replacement,$now+27),'Current claim can complete');
    check($queue->claim(['test.one'],10,$now+28)===null,'Completed jobs do not resend');
    $retryId=$queue->enqueue('test.retry',1,['new'=>'field'],null,0,2);
    $first=$queue->claim(['test.retry'],30,$now);
    check($queue->fail($first,'handler_failed',false,$now),'Transient failure returns to pending');
    check($queue->claim(['test.retry'],30,$now+59)===null,'Retry respects exponential delay');
    $second=$queue->claim(['test.retry'],30,$now+60);$queue->fail($second,'handler_failed',false,$now+60);
    check($queue->status()['failed']===1,'Attempt bound retains a terminal failed job');
    check($queue->retry($retryId,$now+61),'Explicit retry requeues failed job');
    $retried=$queue->claim(['test.retry'],30,$now+61);check($retried->attempt===1,'Explicit retry resets attempts');$queue->complete($retried,$now+62);
    $exhaustId=$queue->enqueue('test.exhaust',1,[],null,0,1);$queue->claim(['test.exhaust'],1,$now);
    check($queue->claim(['test.exhaust'],1,$now+2)===null && $queue->status()['failed']===1,'Crash on last attempt is retained as failed');
    $delayed=$queue->enqueue('test.delayed',1,['delay'=>true],null,120);
    check($queue->claim(['test.delayed'],30,$now)===null,'Scheduled jobs wait until due');
    $registry=new HandlerRegistry();$handler=new class implements Handler {
        public bool $enabled=false;public array $received=[];
        public function ready(): bool { return $this->enabled; }
        public function handle(Job $job): void { $this->received[]=$job->payload; }
    };
    $registry->register('test.two',$handler);$worker=new Worker($queue,$registry);
    check($worker->run()['completed']===0,'Paused handler consumes no jobs');
    check(rejects(fn()=>$registry->register('test.two',$handler)),'Duplicate handler registration is rejected');
    $handler->enabled=true;check($worker->run()['completed']===1 && $handler->received[0]==$payload,'Worker dispatches only explicitly registered ready handlers');
    $queue->enqueue('test.permanent',99,['secret'=>'not logged']);
    $registry->register('test.permanent',new class implements Handler {
        public function ready():bool{return true;}
        public function handle(Job $j):void{throw new PermanentFailure('unsupported_version');}
    });
    check($worker->run()['failed']===1,'Unsupported payload version can fail permanently');
    $queue->enqueue('test.throw',1,['secret'=>'never copy this']);
    $registry->register('test.throw',new class implements Handler {
        public function ready():bool{return true;}
        public function handle(Job $j):void{throw new RuntimeException('SECRET credentials or payload');}
    });
    check($worker->run()['retried']===1,'Unexpected handler failure is retryable');
    $db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');
    check($db->query("SELECT error_code FROM jobs WHERE type='test.throw'")->fetchColumn()==='handler_failed','Exception text is never stored in operational diagnostics');
    for($i=0;$i<30;$i++)$queue->enqueue('test.parallel',1,['n'=>$i]);
    $children=[];
    for($i=0;$i<4;$i++){
        $p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'',__FILE__,'child',$root],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);
        fclose($pipes[0]);$children[]=[$p,$pipes];
    }
    $total=0;
    foreach($children as [$p,$pipes]) { $out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);check(proc_close($p)===0,'Concurrent worker finishes successfully');$total+=(int)$out; }
    check($total===30 && (int)$db->query("SELECT COUNT(*) FROM jobs WHERE type='test.parallel' AND status='completed'")->fetchColumn()===30,'Concurrent workers claim each job exactly once before lease expiry');
    check($db->query('PRAGMA integrity_check')->fetchColumn()==='ok','SQLite integrity after concurrent work');$db=null;
    $old=time()-40*86400;
    $db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');
    $oldCompleted=$queue->enqueue('test.retention',1,['private'=>'completed'],'old-completed');
    $oldJob=$queue->claim(['test.retention'],60);$queue->complete($oldJob);
    $oldFailed=$queue->enqueue('test.retention',1,['private'=>'failed'],'old-failed');
    $oldJob=$queue->claim(['test.retention'],60);$queue->fail($oldJob,'invalid_payload',true);
    $db->prepare('UPDATE jobs SET completed_at=? WHERE id IN (?,?)')->execute([$old,$oldCompleted,$oldFailed]);
    $pendingId=$queue->enqueue('test.retention',1,['private'=>'pending']);
    check($queue->prune(30)===2,'Retention removes old completed and failed payloads');
    check($queue->claim(['test.retention'])?->id===$pendingId,'Retention preserves pending jobs');
    check($queue->enqueue('test.retention',1,['private'=>'new'],'old-completed')!==$oldCompleted,'Explicit pruning releases dedupe history');
    check(rejects(fn()=>$queue->prune(0)) && rejects(fn()=>$queue->prune(3651)),'Retention bounds are explicit');
    $db=null;
    echo "Queue tests passed; no network or SMTP used.\n";
} finally {
    $queue=null;$worker=null;
    foreach (['jobs.sqlite','jobs.sqlite-wal','jobs.sqlite-shm','jobs.sqlite-journal'] as $name) { $p=$root.'/storage/job-queue/'.$name;if(is_file($p))unlink($p); }
    rmdir($root.'/storage/job-queue');rmdir($root.'/storage');rmdir($root);
}
