<?php
declare(strict_types=1);
require dirname(__DIR__).'/TrafficInsights.php';
use Webspine\Providers\SpineAnalytics\TrafficInsights;
function check(bool $ok,string $label):void {if(!$ok)throw new RuntimeException($label);echo 'PASS '.$label."\n";}
$cases=[
 ['Mozilla/5.0 (Windows NT 10.0) Chrome/123 Safari/537 Edg/123',['browser'=>'Edge','os'=>'Windows','device'=>'Desktop']],
 ['Mozilla/5.0 (iPhone) CriOS/123 Mobile Safari/604',['browser'=>'Chrome','os'=>'iOS','device'=>'Phone']],
 ['Mozilla/5.0 (iPad) Version/17 Safari/604',['browser'=>'Safari','os'=>'iOS','device'=>'Tablet']],
 ['Mozilla/5.0 (Linux; Android 10; K) Chrome/123 Mobile Safari/537',['browser'=>'Chrome','os'=>'Android','device'=>'Phone']],
 ['Mozilla/5.0 (Linux; Android 10; K) Chrome/123 Safari/537',['browser'=>'Chrome','os'=>'Android','device'=>'Tablet']],
 ['Mozilla/5.0 (X11; Linux) Firefox/123',['browser'=>'Firefox','os'=>'Linux','device'=>'Desktop']],
 ['Mozilla/5.0 (X11; CrOS) Chrome/123 Safari/537',['browser'=>'Chrome','os'=>'ChromeOS','device'=>'Desktop']],
 ['Mozilla/5.0 Safari/537',['browser'=>'Safari','os'=>'Unknown','device'=>'Unknown']],
];
foreach($cases as [$ua,$expected])check(TrafficInsights::deviceCategories($ua)===$expected,'Coarse '.$expected['browser'].'/'.$expected['os'].'/'.$expected['device']);
$root=sys_get_temp_dir().'/spine-header-test-'.bin2hex(random_bytes(6));mkdir($root,0700);
$stats=new TrafficInsights($root,['/','/docs'],true);$now=time();
try {
 $stats->install();$stats->install();
 $ua='Mozilla/5.0 (Windows NT 10.0) Chrome/123 Safari/537 secret-raw-header';
 $stats->record('/docs?private=query',$ua,'GET',200,$now);
 $stats->record('/private/customer/123',$ua,'POST',404,$now);
 $stats->record('/docs','GPTBot Chrome/123 Windows','GET',200,$now);
 $stats->record('/docs','Mozilla/5.0 HeadlessChrome/123 Windows','GET',200,$now);
 $stats->record('/assets/theme/style.css',$ua,'GET',200,$now);
 $r=$stats->report(7,$now);
 check($r['requests']===4 && $r['headers']===['browser'=>['Chrome'=>2],'os'=>['Windows'=>2],'device'=>['Desktop'=>2]],'Independent totals exclude bots and assets');
 $disabled=new TrafficInsights($root,['/']);$disabled->record('/',$ua,'GET',200,$now);
 check($disabled->report(7,$now)['headers']===$r['headers'],'Device collection disabled by default');
 $db=new PDO('sqlite:'.$root.'/storage/spine-analytics/counts.sqlite');
 $columns=$db->query('PRAGMA table_info(header_totals)')->fetchAll(PDO::FETCH_COLUMN,1);
 check($columns===['day','dimension','value','hits'],'No page, agent, joint device fields or visitor identifiers');
 $rows=json_encode($db->query('SELECT * FROM header_totals')->fetchAll(PDO::FETCH_ASSOC));
 check(!str_contains($rows,'secret') && !str_contains($rows,'customer') && !str_contains($rows,'query') && !str_contains($rows,'123'),'No raw headers, versions or request paths in device storage');
 $db=null;
 $stats->record('/',$ua,'GET',200,$now-90*86400);
 $stats->record('/',$ua,'GET',200,$now);
 $db=new PDO('sqlite:'.$root.'/storage/spine-analytics/counts.sqlite');
 check((int)$db->query('SELECT COUNT(DISTINCT day) FROM header_totals')->fetchColumn()===1,'Header totals obey 90-day retention');
 $db=null;
} finally {
 $stats=null;$disabled=null;$db=null;
 foreach(['counts.sqlite','counts.sqlite-journal','counts.sqlite-wal','counts.sqlite-shm'] as $name){$path=$root.'/storage/spine-analytics/'.$name;if(is_file($path))unlink($path);}
 rmdir($root.'/storage/spine-analytics');rmdir($root.'/storage');rmdir($root);
}
