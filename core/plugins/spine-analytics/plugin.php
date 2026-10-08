<?php
declare(strict_types=1);
require_once __DIR__.'/TrafficInsights.php';
require_once __DIR__.'/ReportAccess.php';
return new class implements \Webspine\Contracts\Plugin {
    public function register(\Webspine\App $app): void {
        $pages=$app->config['spine_analytics']['pages']??['/'];
        $insights=new \Webspine\Providers\SpineAnalytics\TrafficInsights($app->root,$pages,($app->config['spine_analytics']['device_metrics']??false)===true);
        $preview=($app->config['spine_analytics']['preview']??false)===true && in_array($_SERVER['REMOTE_ADDR']??'', ['127.0.0.1','::1'],true);
        $protected=($app->config['spine_analytics']['report_enabled']??false)===true;
        if ($preview || $protected) {
            $app->router->get('/_insights', static function (\Webspine\Request $request) use ($insights,$app,$preview,$protected): \Webspine\Response {
                if($protected) {
                    $https=in_array(strtolower((string)($_SERVER['HTTPS']??'')),['on','1'],true);
                    $local=PHP_SAPI==='cli-server' && in_array($_SERVER['REMOTE_ADDR']??'', ['127.0.0.1','::1'],true);
                    $authorization=$request->header('Authorization','');
                    if($authorization==='' && isset($_SERVER['PHP_AUTH_USER'],$_SERVER['PHP_AUTH_PW'])) $authorization='Basic '.base64_encode($_SERVER['PHP_AUTH_USER'].':'.$_SERVER['PHP_AUTH_PW']);
                    $status=(new \Webspine\Providers\SpineAnalytics\ReportAccess($app->config['spine_analytics']['password_hash'] ?? null))->status($authorization,$https || $local);
                    if($status!==200) {
                        $headers=['Cache-Control'=>'no-store','Content-Type'=>'text/plain; charset=utf-8'];
                        if($status===401)$headers['WWW-Authenticate']='Basic realm="spineanalytics", charset="UTF-8"';
                        return new \Webspine\Response($status===401?'Authentication required.':'Private report is unavailable.',$status,$headers);
                    }
                }
                $days=$request->query('days','30');$days=is_string($days) && in_array($days,['7','30','90'],true)?(int)$days:30;
                try {$report=$insights->report($days);}
                catch (\Throwable) {return new \Webspine\Response('Private report is unavailable.',503,['Cache-Control'=>'no-store','Content-Type'=>'text/plain; charset=utf-8']);}
                $demo=$preview && $request->query('demo')==='1';
                if ($demo) {
                    $file=$app->root.'/storage/spine-analytics/demo-'.$days.'.json';
                    if (is_file($file)) $report=json_decode(file_get_contents($file),true,32,JSON_THROW_ON_ERROR);
                    else $demo=false;
                }
                $reportCss=str_replace(["\r\n","\r"],"\n",file_get_contents(__DIR__.'/assets/report.css'));
                ob_start();require __DIR__.'/report.php';$html=ob_get_clean();
                $hash=base64_encode(hash('sha256',$reportCss,true));
                return new \Webspine\Response($html,200,['Cache-Control'=>'no-store','Content-Security-Policy'=>"default-src 'none'; style-src 'sha256-$hash'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'"]);
            });
        }
        if (PHP_SAPI==='cli' || ($app->config['spine_analytics']['enabled']??false)!==true) return;
        // HTTP requests only; CLI health checks and render probes are never counted.
        $uri=(string)($_SERVER['REQUEST_URI']??'/');
        if (\Webspine\Providers\SpineAnalytics\TrafficInsights::page($uri)===null) return;
        $ua=(string)($_SERVER['HTTP_USER_AGENT']??'');$method=(string)($_SERVER['REQUEST_METHOD']??'GET');
        register_shutdown_function(static function () use ($insights,$uri,$ua,$method): void {
            try {$insights->record($uri,$ua,$method,(int)(http_response_code()?:200));}
            catch (\Throwable $error) {error_log('webspine aggregate insights could not be saved');}
        });
    }
};
