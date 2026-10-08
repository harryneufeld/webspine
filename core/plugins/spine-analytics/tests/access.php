<?php
declare(strict_types=1);
require dirname(__DIR__).'/ReportAccess.php';
require dirname(__DIR__,4).'/core/bootstrap.php';
require dirname(__DIR__).'/TrafficInsights.php';
function verifyAccess(bool $ok,string $label):void{if(!$ok)throw new RuntimeException($label);echo 'PASS '.$label."\n";}
$root=sys_get_temp_dir().'/spine-access-test-'.bin2hex(random_bytes(6));mkdir($root,0700);
$stats=new \Webspine\Providers\SpineAnalytics\TrafficInsights($root);$stats->install();
$access=new \Webspine\Providers\SpineAnalytics\ReportAccess();
$password=bin2hex(random_bytes(16));$auth='Basic '.base64_encode('analytics:'.$password);
try{
 verifyAccess($access->status($auth,true)===503,'Missing hash fails closed');
 $hash=\Webspine\Providers\SpineAnalytics\ReportAccess::hashPassword($password);
 $access=new \Webspine\Providers\SpineAnalytics\ReportAccess($hash);
 verifyAccess(!str_contains($hash,$password) && password_verify($password,$hash),'Generated hash verifies without writing credentials to storage');
 foreach ([null,'invalid',false,[]] as $invalid) verifyAccess((new \Webspine\Providers\SpineAnalytics\ReportAccess($invalid))->status($auth,true)===503,'Invalid configured hash fails closed');
 foreach (['short',str_repeat('a',73),str_repeat('a',16)."\0"] as $invalid) {
  $rejected=false;try {\Webspine\Providers\SpineAnalytics\ReportAccess::hashPassword($invalid);}catch(InvalidArgumentException){$rejected=true;}
  verifyAccess($rejected,'Unsafe password input rejected');
 }
 verifyAccess($access->status($auth,false)===503,'Plain HTTP rejected');
 foreach(['','Bearer token','Basic !!!','Basic '.base64_encode('other:'.$password),'Basic '.base64_encode('analytics:incorrect')] as $bad)verifyAccess($access->status($bad,true)===401,'Invalid credentials rejected');
 verifyAccess($access->status($auth,true)===200,'Valid credentials accepted');
 $app=(new ReflectionClass(\Webspine\App::class))->newInstanceWithoutConstructor();$app->root=$root;$app->router=new \Webspine\Router();
 $app->config=['spine_analytics'=>['enabled'=>false,'preview'=>true,'report_enabled'=>true,'password_hash'=>$hash]];
 $_SERVER['REMOTE_ADDR']='127.0.0.1';$_SERVER['HTTPS']='on';unset($_SERVER['PHP_AUTH_USER'],$_SERVER['PHP_AUTH_PW']);
 $plugin=require dirname(__DIR__).'/plugin.php';$plugin->register($app);
 $denied=$app->router->dispatch('GET','/_insights');
 verifyAccess($denied->status===401 && isset($denied->headers['WWW-Authenticate']),'Protected route cannot bypass login through preview');
 $request=new \Webspine\Request('GET','/_insights',['Authorization'=>$auth]);
 $allowed=$app->router->dispatch('GET','/_insights',$request);
 verifyAccess($allowed->status===200 && $allowed->headers['Cache-Control']==='no-store' && !isset($allowed->headers['Set-Cookie']),'Authenticated report without session cookies');
 $newPassword=bin2hex(random_bytes(16));$access=new \Webspine\Providers\SpineAnalytics\ReportAccess(\Webspine\Providers\SpineAnalytics\ReportAccess::hashPassword($newPassword));
 verifyAccess($access->status($auth,true)===401 && $access->status('Basic '.base64_encode('analytics:'.$newPassword),true)===200,'Rotation immediately revokes old password');
 verifyAccess(!is_file($root.'/storage/spine-analytics/report-password.hash'),'Authentication creates no password file');
 unlink($root.'/storage/spine-analytics/counts.sqlite');
 $unavailable=$app->router->dispatch('GET','/_insights',$request);
 verifyAccess($unavailable->status===503 && $unavailable->headers['Cache-Control']==='no-store' && !str_contains($unavailable->body,'Counted requests'),'Missing analytics storage returns a private unavailable response');
}finally{
 $app=null;$stats=null;$access=null;
 foreach(['counts.sqlite','counts.sqlite-journal','counts.sqlite-wal','counts.sqlite-shm','report-password.hash'] as $name){$path=$root.'/storage/spine-analytics/'.$name;if(is_file($path))unlink($path);}
 rmdir($root.'/storage/spine-analytics');rmdir($root.'/storage');rmdir($root);
}
