<?php
declare(strict_types=1);
$website=dirname(__DIR__,4);
require $website.'/core/bootstrap.php';
require dirname(__DIR__).'/TrafficInsights.php';
$root=sys_get_temp_dir().'/spine-insights-integration-'.bin2hex(random_bytes(6));
mkdir($root,0700);
$stats=new \Webspine\Providers\SpineAnalytics\TrafficInsights($root);$stats->install();
try {
    foreach([[false,'127.0.0.1',false],[true,'198.51.100.10',false],[true,'127.0.0.1',true],[true,'::1',true]] as [$preview,$peer,$expected]) {
        // Only the public App fields used by this plugin are needed here.
        $app=(new ReflectionClass(\Webspine\App::class))->newInstanceWithoutConstructor();
        $app->root=$root;$app->router=new \Webspine\Router();
        $app->config=['spine_analytics'=>['enabled'=>false,'preview'=>$preview]];
        $_SERVER['REMOTE_ADDR']=$peer;
        $plugin=require dirname(__DIR__).'/plugin.php';$plugin->register($app);
        $response=$app->router->dispatch('GET','/_insights');
        if(($response!==null)!==$expected) throw new RuntimeException('Preview isolation failed');
        if($response!==null) {
            if($response->status!==200 || $response->headers['Cache-Control']!=='no-store') throw new RuntimeException('Report response failed');
            $hash=base64_encode(hash('sha256',str_replace(["\r\n","\r"],"\n",file_get_contents(dirname(__DIR__).'/assets/report.css')),true));
            if(!str_contains($response->headers['Content-Security-Policy'],$hash)) throw new RuntimeException('Report style CSP failed');
            if(str_contains($response->body,'<script') || str_contains($response->body,'https://')) throw new RuntimeException('Report must work without scripts or external resources');
        }
        echo 'PASS preview='.($preview?'on':'off').' peer='.$peer."\n";
    }
    if($stats->report()['requests']!==0) throw new RuntimeException('CLI/disabled plugin must not collect');
    echo "PASS CLI collection disabled and report isolation\n";
} finally {
    $app=null;$stats=null;
    foreach(['counts.sqlite','counts.sqlite-wal','counts.sqlite-shm','counts.sqlite-journal'] as $name) {
        $path=$root.'/storage/spine-analytics/'.$name;if(is_file($path))unlink($path);
    }
    rmdir($root.'/storage/spine-analytics');rmdir($root.'/storage');rmdir($root);
}
