<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
use Webspine\{Request,Response,HttpError};
use Webspine\Forms\{FormGuard,FormResult,FormText,RateLimiter};
$source=dirname(__DIR__,2);$root=$source.'/storage/forms-test-'.bin2hex(random_bytes(6));$results=[];
// Keep all progress until after session interactions in this disposable subprocess.
function verify(bool $ok,string $name):void{global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name;}
function removeForms(string $path,string $base):void{
    if(!str_starts_with(realpath($path)?:'',realpath($base).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe fixture cleanup.');
    foreach(new DirectoryIterator($path) as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())removeForms($entry->getPathname(),$base);else unlink($entry->getPathname());}rmdir($path);
}
function guard(string $root,array $options=[]):FormGuard{return new FormGuard($root,'demo',$options+['path'=>'/demo','fields'=>['name','note'],'secure_cookie'=>false,'max_attempts'=>1000]);}
function token(FormGuard $guard):array{
    $seen=null;$response=$guard->show(function(FormResult $form)use(&$seen):Response{$seen=$form;return new Response('form');});
    return [$seen,$response];
}
function post(FormGuard $guard,array $fields,string $ip='203.0.113.7',array $headers=[],?callable $work=null):array{
    $seen=null;$request=new Request('POST','/demo',$headers+['Content-Type'=>'application/x-www-form-urlencoded'],http_build_query($fields),$ip);
    $response=$guard->submit($request,function(FormResult $form)use(&$seen,$work):Response{$seen=$form;if($work)$work($form);return new Response('done',$form->status??200);});
    return [$seen,$response];
}
function session(string $root,callable $edit):void{
    session_name('webspine_demo');session_start(['save_path'=>$root.'/storage/forms/demo/sessions','use_cookies'=>0,'cache_limiter'=>'']);
    $edit();session_write_close();
}
function invalid(callable $build):bool{try{$build();return false;}catch(InvalidArgumentException){return true;}}
try{
    mkdir($root);
    // Limiter
    $limiter=new RateLimiter($root,'storage/limits/rate.json');
    $bucket=fn(string $subject,int $max=2,int $window=600,string $kind='ip')=>['kind'=>$kind,'subject'=>$subject,'max'=>$max,'window'=>$window];
    verify($limiter->allow([$bucket('a')])&&$limiter->allow([$bucket('a')])&&!$limiter->allow([$bucket('a')]),'The call above the limit is refused');
    verify($limiter->allow([$bucket('b')]),'Another subject has its own budget');
    verify($limiter->allow([$bucket('a',2,600,'mail')]),'Kinds have separate budgets');
    verify(!$limiter->allow([$bucket('fresh'),$bucket('a')])&&$limiter->allow([$bucket('fresh',1)]),'A refused call counts in no bucket');
    $raw=file_get_contents($root.'/storage/limits/rate.json');$state=json_decode($raw,true,flags:JSON_THROW_ON_ERROR);
    foreach(array_keys($state['keys']) as $key)verify((bool)preg_match('/^[a-f0-9]{64}$/D',$key),'Limiter keys are hashes');
    $state['keys']=array_map(static fn(array $r):array=>['until'=>time()-1,'count'=>$r['count']],$state['keys']);
    file_put_contents($root.'/storage/limits/rate.json',json_encode($state));
    verify($limiter->allow([$bucket('a')])&&count(json_decode(file_get_contents($root.'/storage/limits/rate.json'),true)['keys'])===1,'Expired records are purged and their subjects restart');
    $small=new RateLimiter($root,'storage/limits/small.json',2);
    verify($small->allow([$bucket('x',5)])&&$small->allow([$bucket('y',5)])&&!$small->allow([$bucket('z',5)])&&$small->allow([$bucket('x',5)]),'A full limiter refuses new subjects and keeps known ones');
    verify(invalid(fn()=>$limiter->allow([]))&&invalid(fn()=>$limiter->allow([$bucket('a',0)]))&&invalid(fn()=>$limiter->allow([$bucket('a',1,0)]))&&invalid(fn()=>$limiter->allow([['kind'=>'A b','subject'=>'x','max'=>1,'window'=>1]])),'Invalid buckets are refused');
    verify(RateLimiter::client('203.0.113.7')==='203.0.113.7'&&RateLimiter::client('::ffff:203.0.113.7')==='203.0.113.7','IPv4 and mapped IPv4 share a subject');
    verify(RateLimiter::client('2001:db8:1:2:aaaa::1')===RateLimiter::client('2001:db8:1:2:bbbb::2')&&RateLimiter::client('2001:db8:1:2::1')!==RateLimiter::client('2001:db8:1:3::1'),'IPv6 counts per /64');
    verify(RateLimiter::client(null)===null&&RateLimiter::client('bad')===null&&RateLimiter::client('fe80::1%eth0')===null,'Invalid addresses have no subject');
    $script='require '.var_export($source.'/core/bootstrap.php',true).';$l=new \\Webspine\\Forms\\RateLimiter('.var_export($root,true).',"storage/limits/shared.json");for($i=0;$i<25;$i++)$l->allow([["kind"=>"ip","subject"=>"same","max"=>1000,"window"=>600]]);';
    $children=[];for($i=0;$i<4;$i++){$p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-r',$script],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);fclose($pipes[0]);$children[]=[$p,$pipes];}
    foreach($children as [$p,$pipes]){fclose($pipes[1]);fclose($pipes[2]);verify(proc_close($p)===0,'Concurrent limiter process exits cleanly');}
    $shared=json_decode(file_get_contents($root.'/storage/limits/shared.json'),true,flags:JSON_THROW_ON_ERROR);
    verify(array_column($shared['keys'],'count')===[100],'Concurrent calls stay valid JSON and lose no count');
    // Text
    verify(FormText::safe("line\nbreak",true)&&!FormText::safe("line\nbreak",false)&&!FormText::safe("\x00x",true)&&!FormText::safe("bad\xff",true),'FormText accepts plain UTF-8 and refuses controls');
    verify(FormText::units('ä')===1&&FormText::units('😀')===2,'FormText counts like maxlength');
    // Options
    foreach([['path'=>'demo'],['path'=>'//demo'],['fields'=>[]],['fields'=>['csrf']],['fields'=>['website']],['fields'=>['a','a']],['honeypot'=>'Bad Name'],['min_seconds'=>-1],['token_ttl'=>1800],['token_ttl'=>59],['secure_cookie'=>1],['max_attempts'=>0],['window'=>0],['unknown'=>1]] as $bad)
        verify(invalid(fn()=>guard($root,$bad)),'Invalid guard option is refused: '.json_encode($bad));
    verify(invalid(fn()=>new FormGuard($root,'Bad-Name',['path'=>'/x','fields'=>['a']])),'Invalid form name is refused');
    // Token
    $guard=guard($root);
    [$get,$response]=token($guard);$token=$get->token;
    verify(strlen($token)===64&&$response->headers['Cache-Control']==='no-store','GET issues a token with no-store');
    verify(token($guard)[0]->token===$token,'Re-rendering keeps the token');
    $valid=['csrf'=>$token,'name'=>' Ann ','note'=>'','website'=>''];
    [$r,$response]=post($guard,['name'=>'x','website'=>'']);
    verify($r->reason==='invalid'&&$r->status===403&&$r->fields===[]&&$response->headers['Cache-Control']==='no-store','A missing token is invalid and drops the input');
    $token=$r->token;$valid['csrf']=$token;
    [$r]=post($guard,['csrf'=>str_repeat('0',64)]+$valid);
    verify($r->reason==='invalid'&&$r->fields===[]&&$r->token!==$token,'A foreign token drops the input and retires the current token');
    $valid['csrf']=$token=$r->token;
    session($root,function():void{$_SESSION['form']['expires']=0;});
    [$r]=post($guard,$valid);
    verify($r->reason==='expired'&&$r->status===403&&$r->fields['name']==='Ann'&&$r->token!==$token,'The own expired token keeps the input and renews the token');
    $valid['csrf']=$token=$r->token;
    [$r]=post($guard,$valid,headers:['Sec-Fetch-Site'=>'cross-site']);
    verify($r->reason==='invalid'&&$r->fields===[]&&$r->token===$token,'A cross-site POST is invalid without retiring the token');
    [$r]=post($guard,$valid,headers:['Sec-Fetch-Site'=>'same-origin']);
    verify($r->ok,'A same-origin POST passes');
    // Rejection
    [$r]=post($guard,['website'=>'bot']+$valid);verify($r->reason==='rejected'&&$r->status===422&&$r->fields===[]&&$r->token===$token,'A filled honeypot is rejected and keeps the token');
    [$r]=post($guard,['to'=>'x']+$valid);verify($r->reason==='rejected','An unknown field is rejected');
    [$r]=post($guard,['name'=>['x']]+$valid);verify($r->reason==='rejected','An array field is rejected');
    [$r]=post($guard,['name'=>"\x00x"]+$valid);verify($r->ok&&$r->fields['name']==="\x00x",'Controls reach field validation untrimmed');
    // Fill time
    $slow=guard($root,['min_seconds'=>60]);
    session($root,function():void{$_SESSION['form']['issued']=time();});
    [$r]=post($slow,$valid);verify($r->reason==='toofast'&&$r->status===422&&$r->fields['name']==='Ann'&&$r->token===$token,'A fast POST is refused, keeps input and token');
    session($root,function():void{$_SESSION['form']['issued']=time()-61;$_SESSION['form']['expires']=0;});
    [$r]=post($slow,$valid);$renewed=$r->token;
    [$r]=post($slow,['csrf'=>$renewed]+$valid);verify($r->ok,'A token renewal does not restart the fill-time clock');
    $valid['csrf']=$token=$renewed;
    // Completion
    [$r,$response]=post($guard,$valid,work:fn(FormResult $form)=>$form->complete('sent'));
    verify($r->ok&&$response->status===200,'An accepted POST completes');
    [$r]=post($guard,$valid);
    verify($r->reason==='replayed'&&$r->status===303,'An exact replay is recognized');
    [$r]=post($guard,['name'=>'Changed']+$valid);
    verify($r->reason==='invalid'&&$r->fields===[],'A used token cannot authorize altered values');
    [$get]=token($guard);
    verify($get->flash==='sent'&&$get->token!==$token,'The flash arrives with a fresh token');
    verify(token($guard)[0]->flash===null,'The flash is shown once');
    try{$get->complete('sent');$completed=true;}catch(LogicException){$completed=false;}
    verify(!$completed,'Only an accepted submission can complete');
    session($root,function():void{foreach($_SESSION['form']['receipts'] as &$receipt)$receipt['expires']=0;});
    [$r]=post($guard,$valid);
    verify($r->reason==='invalid','An expired receipt cannot authorize a replay');
    for($i=0;$i<4;$i++){$current=token($guard)[0]->token;$first??=$current;post($guard,['csrf'=>$current]+$valid,work:fn(FormResult $form)=>$form->complete());}
    verify(post($guard,['csrf'=>$first]+$valid)[0]->reason==='invalid'&&post($guard,['csrf'=>$current]+$valid)[0]->reason==='replayed','Only the last three receipts are kept');
    // Rate limit
    $strict=guard($root,['max_attempts'=>2,'window'=>123]);
    post($strict,['csrf'=>'x'],'198.51.100.1');post($strict,['csrf'=>'x'],'198.51.100.1');
    [$r,$response]=post($strict,['csrf'=>'x'],'198.51.100.1');
    verify($r->reason==='limited'&&$r->status===429&&$r->token===null&&$response->headers['Retry-After']==='123'&&$response->headers['Cache-Control']==='no-store'&&session_status()===PHP_SESSION_NONE,'Above the limit no session opens and Retry-After is set');
    verify(post($strict,['csrf'=>'x'],'198.51.100.2')[0]->reason==='invalid','Another IP is unaffected');
    verify(post($strict,['csrf'=>'x'],'bad')[0]->reason==='limited','An invalid address is refused');
    verify(post($strict,['csrf'=>'x'],'198.51.100.1',['X-Forwarded-For'=>'192.0.2.9'])[0]->reason==='limited','Forwarded headers do not change the client');
    verify(!str_contains(file_get_contents($root.'/storage/forms/demo/rate.json'),'198.51.100'),'Limiter state holds no clear-text address');
    verify($guard->limiter()->allow([['kind'=>'global','subject'=>'day','max'=>1,'window'=>86400]])&&!$guard->limiter()->allow([['kind'=>'global','subject'=>'day','max'=>1,'window'=>86400]]),'Handlers can add global buckets');
    // Boundaries
    try{post($guard,[],headers:['Content-Type'=>'text/plain']);$media=false;}catch(HttpError $e){$media=$e->status===415&&session_status()===PHP_SESSION_NONE;}
    verify($media,'Unsupported media fails before the session');
    try{$guard->show(fn(FormResult $form)=>'text');$type=false;}catch(RuntimeException){$type=session_status()===PHP_SESSION_NONE;}
    verify($type,'A handler must return a Response and the session still closes');
    session_name('webspine_other');session_start(['save_path'=>$root,'use_cookies'=>0]);
    try{token($guard);$hijack=true;}catch(RuntimeException){$hijack=false;}
    session_write_close();
    verify(!$hijack,'The guard refuses to start inside another session');
    foreach($results as $name)echo "PASS $name\n";
    echo "Form guard tests passed.\n";
}catch(Throwable $e){fwrite(STDERR,$e::class.': '.$e->getMessage().' at '.$e->getFile().':'.$e->getLine()."\n");$code=1;}
finally{if(session_status()===PHP_SESSION_ACTIVE)session_write_close();if(is_dir($root))removeForms($root,$source.'/storage');}
exit($code??0);
