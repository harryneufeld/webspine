<?php
declare(strict_types=1);
$prepare=require __DIR__.'/contact-fixture.php';
use Webspine\{App,Request};
use Webspine\Jobs\Queue;
$source=dirname(__DIR__,2);$root=$source.'/storage/contact-retry-test-'.bin2hex(random_bytes(6));$db=null;$app=null;$queue=null;$results=[];
// Keep all progress until after cookie/session interactions in this disposable subprocess.
function verify(bool $ok,string $name):void{global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name;}
function removeRetryFixture(string $path,string $base):void{
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe fixture cleanup.');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())removeRetryFixture($entry->getPathname(),$base);else unlink($entry->getPathname());}rmdir($path);
}
function submitRetryForm(App $app,string $message):void{
    $get=$app->handle(new Request('GET','/inquiry'));preg_match('/name="csrf" value="([a-f0-9]{64})"/',$get->body,$m);
    $post=$app->handle(new Request('POST','/inquiry',['Content-Type'=>'application/x-www-form-urlencoded'],http_build_query(['csrf'=>$m[1],'name'=>'Fixture visitor','email'=>'visitor@example.test','message'=>$message,'website'=>'']),'127.0.0.1'));
    verify($post->status===303,'Valid fixture submission is saved');
}
try{
    $prepare($root,'/inquiry');$app=new App($root);$queue=$app->services->get(Queue::class);$db=new PDO('sqlite:'.$root.'/storage/job-queue/jobs.sqlite');
    submitRetryForm($app,'Default retry window message.');
    $row=$db->query('SELECT id,max_attempts,payload FROM jobs')->fetch(PDO::FETCH_ASSOC);$id=$row['id'];$payload=json_decode($row['payload'],true,flags:JSON_THROW_ON_ERROR);
    verify((int)$row['max_attempts']===30,'Default contact attempt limit is saved in the durable envelope');
    $config=require $root.'/config/local.php';$config['contact_form']['retry']=['max_attempts'=>50];$configured=new App($root,$config);
    submitRetryForm($configured,'Configured retry window message.');
    $configuredId=$db->query('SELECT id FROM jobs WHERE max_attempts=50')->fetchColumn();
    verify(is_string($configuredId),'Configured contact attempt limit reaches the queue');
    $config['contact_form']['retry']=['max_attempts'=>1];$single=new App($root,$config);submitRetryForm($single,'Single-attempt contact message.');
    $singleId=$db->query('SELECT id FROM jobs WHERE max_attempts=1')->fetchColumn();
    verify(is_string($singleId),'A deliberate single-attempt policy is supported');
    verify((int)$db->query('SELECT max_attempts FROM jobs WHERE id='.$db->quote($id))->fetchColumn()===30,'Later configuration never rewrites a previously queued contact job');
    $dedupe=$db->query('SELECT dedupe_key FROM jobs WHERE id='.$db->quote($id))->fetchColumn();
    verify($queue->enqueue('contact.deliver',2,$payload,$dedupe,maxAttempts:50)===$id && (int)$db->query('SELECT max_attempts FROM jobs WHERE id='.$db->quote($id))->fetchColumn()===30,'Deduplication preserves the original retry envelope');
    $start=time();$db->prepare('UPDATE jobs SET available_at=? WHERE id<>?')->execute([$start+31536000,$id]);
    file_put_contents($root.'/storage/mail-unavailable','Disposable simulated outage');
    $delivery=new \Webspine\Examples\MailDelivery($app);$clock=$start;$sent=0;$attempts=0;$recovered=false;
    while($job=$queue->claim(['contact.deliver'],300,$clock)){
        verify($job->id===$id,'Virtual-time worker claims the expected contact job');$attempts++;
        if($clock-$start>=86400){unlink($root.'/storage/mail-unavailable');$recovered=true;}
        try{$delivery->handle($job);$sent++;verify($queue->complete($job,$clock),'Recovered delivery completes its claim');break;}
        catch(RuntimeException $e){verify($queue->fail($job,'handler_failed',false,$clock),'Transient outage schedules another attempt');}
        if($attempts===5)verify($clock-$start===900 && $queue->status()['failed']===0 && $queue->status()['pending']===3,'Contact mail remains pending after the old 15-minute failure point');
        $clock=(int)$db->query('SELECT available_at FROM jobs WHERE id='.$db->quote($id))->fetchColumn();
    }
    verify($recovered && $sent===1 && $attempts===30 && $clock-$start===86580,'Mail recovers after 24 hours and is captured once at the final default attempt');
    verify($queue->claim(['contact.deliver'],300,$clock)===null,'Completed contact mail is not dispatched again');
    $messages=file($root.'/storage/captured-mail.jsonl',FILE_IGNORE_NEW_LINES|FILE_SKIP_EMPTY_LINES);
    verify(count($messages)===1 && str_contains($messages[0],'Default retry window message.'),'Only the recovered inquiry was captured; no SMTP was contacted');
    // The already queued one-attempt job retains its policy and exhausts on a transient failure.
    $db->prepare('UPDATE jobs SET available_at=? WHERE id=?')->execute([$clock,$singleId]);
    file_put_contents($root.'/storage/mail-unavailable','Disposable simulated outage');
    $job=$queue->claim(['contact.deliver'],300,$clock);
    try{$delivery->handle($job);throw new LogicException('Simulated outage ignored.');}catch(RuntimeException $e){if($e instanceof LogicException)throw $e;$queue->fail($job,'handler_failed',false,$clock);}
    verify($queue->status()['failed']===1 && $queue->claim(['contact.deliver'],300,$clock+3600)===null,'Configured attempts still exhaust and require operator retry');
    $legacyId=$queue->enqueue('test.legacy',1,['legacy'=>true]);$legacy=$queue->claim(['test.legacy'],300,$clock);verify($legacy->maxAttempts===5,'Generic jobs retain the original five-attempt default');
    for($i=1;$i<=5;$i++){$queue->fail($legacy,'handler_failed',false,$clock);if($i<5){$clock=(int)$db->query('SELECT available_at FROM jobs WHERE id='.$db->quote($legacyId))->fetchColumn();$legacy=$queue->claim(['test.legacy'],300,$clock);}}
    verify($queue->status()['failed']===2,'Generic five-attempt jobs retain their exhaustion behavior');
    $queue->enqueue('test.maximum',1,[],maxAttempts:100);$maximum=$queue->claim(['test.maximum'],300,$clock);verify($maximum->maxAttempts===100,'Bundled queue supports the bounded expanded attempt range');
    $queue->fail($maximum,'invalid_payload',true,$clock);verify($queue->status()['failed']===3,'Permanent failure remains immediate despite a longer attempt limit');
    foreach([0,101] as $bad){try{$queue->enqueue('test.invalid',1,[],maxAttempts:$bad);throw new LogicException('Invalid attempt bound accepted.');}catch(InvalidArgumentException){}}
    foreach($results as $name)echo "PASS $name\n";
    echo "Contact retry tests passed; 24-hour outage simulated without sleeping or SMTP.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n".$e->getTraceAsString()."\n");$code=1;}
finally{if(session_status()===PHP_SESSION_ACTIVE)session_write_close();$db=null;$app=null;$queue=null;unset($configured,$single,$delivery);gc_collect_cycles();if(is_dir($root))removeRetryFixture($root,$source.'/storage');}
exit($code??0);
