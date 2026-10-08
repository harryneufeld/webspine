<?php
declare(strict_types=1);
require dirname(__DIR__).'/TrafficInsights.php';
use Webspine\Providers\SpineAnalytics\TrafficInsights;
if (($argv[1]??'')==='child') {
    $stats=new TrafficInsights($argv[2],['/','/docs','/de/docs']);
    for ($i=0;$i<25;$i++) $stats->record('/docs','GPTBot/1.4','GET',200,(int)$argv[3]);
    exit;
}
function verify(bool $ok,string $message):void {if (!$ok) throw new RuntimeException($message);echo 'PASS '.$message."\n";}
$root=sys_get_temp_dir().'/spine-insights-test-'.bin2hex(random_bytes(6));mkdir($root,0700);
$stats=new TrafficInsights($root,['/','/docs','/de/docs']);$now=strtotime('2026-10-05 12:00:00 UTC');
try {
    $stats->install();$stats->install();
    foreach ([['Mozilla/5.0 Chrome/123 Safari/537.36','browser'],['Mozilla/5.0 Chrome/123 OAI-SearchBot/1.4','ai-search'],['ChatGPT-User/1.0','ai-user'],['GPTBot/1.4','ai-training'],['ClaudeBot/1','ai-training'],['Claude-User/1','ai-user'],['Claude-SearchBot/1','ai-search'],['Googlebot/2.1','search-bot'],['curl/8.0','other-bot'],['Mozilla/5.0 HeadlessChrome/123','other-bot'],['','unknown']] as [$ua,$kind]) verify(TrafficInsights::classify($ua)[0]===$kind,'Classify '.$kind);
    $stats->record('/docs?email=private@example.com','Mozilla/5.0 Chrome/123 Safari/537.36 private-agent-token','GET',200,$now);
    $stats->record('/docs?email=second@example.com','Mozilla/5.0 Chrome/123 Safari/537.36','GET',200,$now);
    $stats->record('/de/docs','GPTBot/1.4','HEAD',200,$now);
    $stats->record('/secret/customer/private@example.com','curl/8.0','POST',404,$now);
    $stats->record('/assets/theme/studio/style.css','GPTBot','GET',200,$now);
    $stats->record('/health','GPTBot','GET',200,$now);
    $stats->record('/_insights?days=30','GPTBot','GET',200,$now);
    $r=$stats->report(30,$now);
    verify($r['requests']===4 && $r['page_requests']===2 && $r['errors']===1,'Aggregate counts, GETs, errors and excluded assets/health/report');
    verify($r['pages']['/docs']===2 && isset($r['pages']['[other]']),'Queries removed and unknown paths bucketed');
    $db=new PDO('sqlite:'.$root.'/storage/spine-analytics/counts.sqlite');
    $stored=json_encode($db->query('SELECT * FROM counts')->fetchAll(PDO::FETCH_ASSOC));
    verify(!str_contains($stored,'example.com') && !str_contains($stored,'secret') && !str_contains($stored,'private-agent-token'),'No raw paths, query strings or raw user-agent data stored');
    $stats->record('/','GPTBot','GET',200,$now-90*86400);
    $stats->record('/','GPTBot','GET',200,$now-89*86400);
    $stats->record('/','GPTBot','GET',200,$now);
    verify((int)$db->query('SELECT COUNT(DISTINCT day) FROM counts')->fetchColumn()===2,'90-day retention removes expired aggregates');
    $db=null;$before=$stats->report(30,$now)['requests'];$children=[];
    for ($i=0;$i<5;$i++) {
        $pipes=[];$p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file() ?: '',__FILE__,'child',$root,(string)$now],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);
        fclose($pipes[0]);$children[]=[$p,$pipes];
    }
    $childErrors=[];
    foreach ($children as [$p,$pipes]) { $stdout=stream_get_contents($pipes[1]);$error=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);if(proc_close($p)!==0) $childErrors[]=$stdout.$error; }
    verify(!$childErrors,'Concurrent writers completed without errors: '.implode(' | ',$childErrors));
    verify($stats->report(30,$now)['requests']===$before+125,'Concurrent increments are not lost');
    verify($stats->report(7,$now)['days']===7 && count($stats->report(90,$now)['daily'])===90,'Report ranges include empty days');
    $db=new PDO('sqlite:'.$root.'/storage/spine-analytics/counts.sqlite');verify($db->query('PRAGMA integrity_check')->fetchColumn()==='ok','Database integrity');$db=null;
} finally {
    $stats=null;foreach (['counts.sqlite','counts.sqlite-journal','counts.sqlite-wal','counts.sqlite-shm'] as $n) { $p=$root.'/storage/spine-analytics/'.$n;if(is_file($p))unlink($p); }
    rmdir($root.'/storage/spine-analytics');rmdir($root.'/storage');rmdir($root);
}
echo "All isolated analytics checks passed. No production traffic or settings changed.\n";
