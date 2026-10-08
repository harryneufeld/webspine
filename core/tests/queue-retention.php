<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/plugins/job-queue/bootstrap.php';
use Webspine\Jobs\{SqliteQueue,QueueWork,HandlerRegistry};
function verify(bool $ok,string $name):void{if(!$ok)throw new RuntimeException($name);echo "PASS $name\n";}
$root=sys_get_temp_dir().'/webspine-retention-'.bin2hex(random_bytes(8));mkdir($root,0700);$queue=new SqliteQueue($root);$db=null;
try{
    $queue->install();$db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');$handlers=new HandlerRegistry();$now=time();
    $completed=$queue->enqueue('test.complete',1,['private'=>'old completed'],'retained-key');$job=$queue->claim(['test.complete']);$queue->complete($job);
    $failed=$queue->enqueue('test.fail',1,['private'=>'old failure']);$job=$queue->claim(['test.fail']);$queue->fail($job,'handler_failed',true);
    $pending=$queue->enqueue('test.pending',1,['private'=>'old pending']);
    $processing=$queue->enqueue('test.processing',1,['private'=>'old processing']);$queue->claim(['test.processing']);
    $fresh=$queue->enqueue('test.fresh',1,['private'=>'recent completed']);$job=$queue->claim(['test.fresh']);$queue->complete($job);
    $db->prepare('UPDATE jobs SET created_at=?,completed_at=? WHERE id IN (?,?,?,?)')->execute([$now-40*86400,$now-40*86400,$completed,$failed,$pending,$processing]);
    verify($queue->status()['completed']===2 && $queue->diagnostics()['completed']===2,'Status and health diagnostics never prune old submissions');
    $before=hash_file('sha256',$root.'/storage/job-queue/jobs.sqlite');
    foreach([0,3651,'30',false] as $days){try{QueueWork::run($queue,$handlers,['retention_days'=>$days]);throw new LogicException('Invalid retention accepted.');}catch(InvalidArgumentException){}}
    verify(hash_file('sha256',$root.'/storage/job-queue/jobs.sqlite')===$before && !is_file($root.'/storage/job-queue/worker.json'),'Invalid retention fails before worker activity or deletion');
    $disabled=QueueWork::run($queue,$handlers,['retention_days'=>null]);
    verify(!$disabled['retention']['enabled'] && $disabled['retention']['supported'] && $disabled['retention']['pruned_completed']===0 && $queue->status()['completed']===2,'Explicit null disables automatic completed retention');
    $work=QueueWork::run($queue,$handlers);
    verify($work['retention']===['days'=>30,'supported'=>true,'enabled'=>true,'pruned_completed'=>1],'Normal CLI workflow defaults to 30-day completed cleanup');
    $counts=$queue->status();verify($counts===['pending'=>1,'processing'=>1,'completed'=>1,'failed'=>1],'Automatic cleanup preserves pending, processing, recent completed and old failed jobs');
    verify($queue->enqueue('test.complete',1,['private'=>'new submission'],'retained-key')!==$completed,'Completed pruning explicitly releases dedupe history');
    $db->prepare('UPDATE jobs SET completed_at=? WHERE id=?')->execute([$now-2*86400,$fresh]);
    verify(QueueWork::run($queue,$handlers,['retention_days'=>1])['retention']['pruned_completed']===1,'Configured retention interval is applied');
    // Seed enough completed fixture rows in one transaction to verify the per-run deletion bound.
    $db->beginTransaction();$insert=$db->prepare("INSERT INTO jobs(id,type,payload_version,payload,status,max_attempts,available_at,created_at,completed_at) VALUES(?,'test.bulk',1,'{}','completed',5,?,?,?)");
    for($i=0;$i<1005;$i++)$insert->execute([bin2hex(random_bytes(16)),$now-31*86400,$now-31*86400,$now-31*86400]);$db->commit();
    verify(QueueWork::run($queue,$handlers)['retention']['pruned_completed']===1000 && $queue->status()['completed']===5,'Automatic retention deletes at most 1000 rows per invocation');
    verify(QueueWork::run($queue,$handlers)['retention']['pruned_completed']===5,'Later worker invocations finish the retention backlog');
    // A future cutoff just outside the interval is retained; the exact cutoff is removed.
    $cutoff=time()-86400;
    $boundary=$queue->enqueue('test.boundary',1,[]);$job=$queue->claim(['test.boundary']);$queue->complete($job);
    $db->prepare('UPDATE jobs SET completed_at=? WHERE id=?')->execute([$cutoff+3600,$boundary]);
    verify($queue->pruneCompleted(1)===0,'Completed jobs younger than the cutoff are retained');
    $db->prepare('UPDATE jobs SET completed_at=? WHERE id=?')->execute([time()-86400,$boundary]);
    verify($queue->pruneCompleted(1)===1,'Completed jobs at the cutoff are pruned');
    verify($queue->prune(30)===1 && $queue->status()['failed']===0,'Explicit manual prune still deletes reviewed old failed jobs');
    echo "Queue retention tests passed; disposable data only.\n";
}finally{
    $db=null;$queue=null;unset($insert);
    foreach(['jobs.sqlite','jobs.sqlite-wal','jobs.sqlite-shm','jobs.sqlite-journal','worker.json'] as $name){$path=$root.'/storage/job-queue/'.$name;if(is_file($path))unlink($path);}
    rmdir($root.'/storage/job-queue');rmdir($root.'/storage');rmdir($root);
}
