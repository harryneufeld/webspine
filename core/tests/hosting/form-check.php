<?php
declare(strict_types=1);
// Isolated fixtures only: capture-mail never contacts SMTP.
$base = $argv[1] ?? '';
if (in_array($base,['apache','nginx','cloudpanel'],true)) $base = 'http://'.$base;
elseif (!preg_match('~^http://(?:localhost|127\.0\.0\.1):[0-9]+$~D',$base)) throw new RuntimeException('Use local fixture server only.');
define('CONTACT_PATH', $argv[2] ?? '/contact-example');
$fixtureRoot = $argv[3] ?? '/srv/webspine';
if (!is_file($fixtureRoot.'/storage/hosting-test-fixture')) throw new RuntimeException('Use a marked disposable fixture only.');
$passed=0; $failed=0;
function formCheck(string $name, callable $test): void {
    global $passed,$failed;
    try { if ($test() === false) throw new RuntimeException('Assertion failed'); echo "PASS $name\n"; $passed++; }
    catch (Throwable $e) { echo "FAIL $name: {$e->getMessage()}\n"; $failed++; }
}
function contactRequest(string $path,string $method='GET',string $body='',array $headers=[]): array {
    global $base;
    $lines=[]; foreach ($headers as $key=>$value) $lines[]=$key.': '.$value;
    $context=stream_context_create(['http'=>['method'=>$method,'content'=>$body,'header'=>implode("\r\n",$lines),'ignore_errors'=>true,'follow_location'=>0,'timeout'=>5]]);
    $text=@file_get_contents($base.$path,false,$context); $raw=$http_response_header ?? [];
    preg_match('/^HTTP\/\S+ (\d+)/',$raw[0] ?? '',$match); $result=[];
    foreach (array_slice($raw,1) as $line) if (str_contains($line,':')) { [$key,$value]=explode(':',$line,2); $result[strtolower($key)]=trim($value); }
    return ['status'=>(int)($match[1] ?? 0),'body'=>$text === false ? '' : $text,'headers'=>$result];
}
function mailCount(): int { return count(json_decode(contactRequest('/hosting-mail')['body'],true)['messages']); }
function fixturePhp(string $script): array {
    $p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-r',$script],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);
    fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);
    if(proc_close($p)!==0)throw new RuntimeException($err.$out);
    return json_decode($out,true,flags:JSON_THROW_ON_ERROR);
}
function queueAction(bool $work=false, bool $paused=false): array {
    global $fixtureRoot;
    $root=var_export($fixtureRoot,true);
    return fixturePhp('require '.$root.'."/core/bootstrap.php"; $config=require '.$root.'."/config/local.php";'
        .($paused?'$config["contact_form"]["delivery_enabled"]=false;':'').'$app=new \\Webspine\\App('.$root.',$config);'
        .'echo json_encode('.($work?'(new \\Webspine\\Jobs\\Worker($app->services->get(\\Webspine\\Jobs\\Queue::class),$app->services->get(\\Webspine\\Jobs\\Handlers::class)))->run()':'$app->services->get(\\Webspine\\Jobs\\Queue::class)->status()').');');
}
function queueCli(string $command): array {
    global $fixtureRoot;
    $p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'',$fixtureRoot.'/core/plugins/job-queue/cli.php',$command],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);
    fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);
    return ['code'=>proc_close($p),'out'=>$out,'err'=>$err];
}
function refreshFormToken(array $headers): string {
    $r=contactRequest(CONTACT_PATH,'GET','',['Cookie'=>$headers['Cookie']]);
    preg_match('/name="csrf" value="([a-f0-9]{64})"/',$r['body'],$m);
    return $m[1]??'';
}
$first=contactRequest(CONTACT_PATH);
preg_match('/name="csrf" value="([a-f0-9]{64})"/',$first['body'],$match); $token=$match[1] ?? '';
$cookie=explode(';',$first['headers']['set-cookie'] ?? '')[0];
$headers=['Content-Type'=>'application/x-www-form-urlencoded','Cookie'=>$cookie];
$valid=['csrf'=>$token,'name'=>'Example user','email'=>'visitor@example.test','message'=>'A valid test message.','website'=>''];
formCheck('Form uses a private cookie, CSRF token, and no-store cache policy',fn()=>$first['status']===200 && strlen($token)===64 && str_contains(strtolower($first['headers']['set-cookie'] ?? ''),'httponly') && str_contains(strtolower($first['headers']['set-cookie'] ?? ''),'samesite=lax') && $first['headers']['cache-control']==='no-store');
formCheck('Missing CSRF token returns a usable no-store form without saving mail',function()use($headers,&$valid){
    $r=contactRequest(CONTACT_PATH,'POST','name=user',$headers);$valid['csrf']=refreshFormToken($headers);
    return $r['status']===403 && str_contains($r['body'],'fresh form below') && str_contains($r['body'],'<form') && $r['headers']['cache-control']==='no-store'
        && strlen($valid['csrf'])===64 && mailCount()===0 && queueAction()['pending']===0;
});
formCheck('Mismatched token is retired without retaining submitted values',function()use($headers,&$valid){
    $bad=$valid;$bad['csrf']=str_repeat('0',64);$bad['name']='PRIVATE_CSRF_VALUE';$old=$valid['csrf'];
    $r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);$valid['csrf']=refreshFormToken($headers);
    return $r['status']===403 && !str_contains($r['body'],'PRIVATE_CSRF_VALUE') && $valid['csrf']!==$old && queueAction()['pending']===0;
});
formCheck('Expired matching token preserves escaped input with a fresh token without queueing',function()use($headers,&$valid,$fixtureRoot){
    $sid=explode('=',$headers['Cookie'],2)[1];if(!preg_match('/^[a-zA-Z0-9,-]+$/D',$sid))return false;
    fixturePhp('session_id('.var_export($sid,true).');session_start(["save_path"=>'.var_export($fixtureRoot.'/storage/contact-forms/sessions',true).',"use_cookies"=>0,"cache_limiter"=>""]);$_SESSION["contact_forms"]["contact"]["expires"]=0;session_write_close();echo "{}";');
    $old=$valid['csrf'];$bad=$valid;$bad['name']='<b>Änne</b>';$r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);$valid['csrf']=refreshFormToken($headers);
    return $r['status']===403 && str_contains($r['body'],'Your input has been kept') && str_contains($r['body'],'&lt;b&gt;Änne&lt;/b&gt;')
        && str_contains($r['body'],$valid['email']) && str_contains($r['body'],$valid['message']) && $valid['csrf']!==$old && queueAction()['pending']===0;
});
formCheck('Array-shaped fields are rejected',function()use($headers,$valid){ $bad=$valid; $bad['name']=['bad']; $r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers); return $r['status']===422 && mailCount()===0; });
formCheck('Validation retains escaped values and accessible field errors',function()use($headers,$valid){ $bad=$valid; $bad['name']='<script>alert(1)</script>'; $bad['email']='invalid'; $r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers); return $r['status']===422 && str_contains($r['body'],'&lt;script&gt;') && !str_contains($r['body'],'<script>') && str_contains($r['body'],'aria-invalid="true"') && mailCount()===0; });
formCheck('Honeypot and unknown fields cannot dispatch mail',function()use($headers,$valid){ $bad=$valid; $bad['website']='filled'; $r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers); $bad=$valid; $bad['to']='attacker@example.test'; $extra=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers); return $r['status']===422 && $extra['status']===422 && mailCount()===0; });
formCheck('Unsupported form media and oversized bodies return 415/413',function()use($headers){ $plain=$headers; $plain['Content-Type']='text/plain'; $unsupported=contactRequest(CONTACT_PATH,'POST','body',$plain); $large=contactRequest(CONTACT_PATH,'POST',str_repeat('x',65537),$headers); return $unsupported['status']===415 && $large['status']===413 && mailCount()===0; });
formCheck('Values above the field limit remain editable without queueing',function()use($headers,$valid){$bad=$valid;$bad['name']=str_repeat('x',121);$r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);return $r['status']===422 && str_contains($r['body'],str_repeat('x',121)) && queueAction()['pending']===0;});
formCheck('Unicode over-limit text remains editable; malformed and hard-byte-limit values are discarded',function()use($headers,$valid){
    foreach ([str_repeat('ä',5001)=>true,str_repeat('x',20001)=>false,"bad\xfftext"=>false,"\x00bad text"=>false] as $value=>$keep) {
        $bad=$valid;$bad['message']=$value;$r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);
        preg_match('~<textarea[^>]*>(.*?)</textarea>~s',$r['body'],$textarea);
        if($r['status']!==422 || ($keep && !str_contains($r['body'],$value)) || (!$keep && ($textarea[1]??null)!==''))throw new RuntimeException('Unicode case failed: bytes='.strlen($value).', status='.$r['status']);
    }
    return queueAction()['pending']===0;
});
// Native maxlength counts astral emoji twice and combining marks separately.
formCheck('Unicode length follows browser UTF-16 limits without optional extensions',function()use($fixtureRoot){
    return fixturePhp('require '.var_export($fixtureRoot.'/site/plugins/contact-form/FormText.php',true).';echo json_encode([\\Webspine\\Examples\\FormText::units("ä"),\\Webspine\\Examples\\FormText::units("😀"),\\Webspine\\Examples\\FormText::units("e\\u{0301}")]);')===[1,2,2];
});
formCheck('Astral emoji and combining marks enforce browser limits through HTTP',function()use($headers,$valid){
    foreach([str_repeat('😀',61),str_repeat("e\u{0301}",61)] as $name){$bad=$valid;$bad['name']=$name;$r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);if($r['status']!==422 || !str_contains($r['body'],$name))return false;}
    return queueAction()['pending']===0;
});
formCheck('Queue failure preserves input and leaves the token eligible for a later save',function()use($headers,$valid,$fixtureRoot){
    $path=$fixtureRoot.'/storage/job-queue/jobs.sqlite';
    fixturePhp('rename('.var_export($path,true).','.var_export($path.'.test-disabled',true).');echo "{}";');
    try{$r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);}
    finally{fixturePhp('rename('.var_export($path.'.test-disabled',true).','.var_export($path,true).');echo "{}";');}
    return $r['status']===503 && str_contains($r['body'],$valid['message']) && str_contains($r['body'],'could not be saved')
        && refreshFormToken($headers)===$valid['csrf'] && queueAction()['pending']===0;
});
$valid['message']=str_repeat('ä',5000);
formCheck('Valid submission is durable before delivery and uses the configured 303 path',function()use($headers,$valid){ $r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers); return $r['status']===303 && $r['headers']['location']===CONTACT_PATH && mailCount()===0 && queueAction()['pending']===1; });
formCheck('HTTP contact submission saves the default 30-attempt retry envelope',function()use($fixtureRoot){
    return fixturePhp('$db=new PDO("sqlite:".'.var_export($fixtureRoot.'/storage/job-queue/jobs.sqlite',true).');echo json_encode([(int)$db->query("SELECT max_attempts FROM jobs WHERE status=\'pending\'")->fetchColumn()]);')===[30];
});
formCheck('Paused worker consumes no delivery attempts',function(){return queueAction(true,true)['completed']===0 && queueAction()['pending']===1 && mailCount()===0;});
formCheck('Captured background delivery accepts 5000 umlauts and preserves recipient and subject',function()use($valid){ $result=queueAction(true);$messages=json_decode(contactRequest('/hosting-mail')['body'],true)['messages']; return $result['completed']===1 && count($messages)===1 && $messages[0]['to']==='owner@example.test' && $messages[0]['subject']==='Website contact' && str_contains($messages[0]['body'],'Submission ID:') && str_contains($messages[0]['body'],$valid['message']); });
formCheck('Repeated worker invocation does not resend completed mail',fn()=>queueAction(true)['completed']===0 && mailCount()===1);
formCheck('Successful exact replay returns success without creating or delivering another job',function()use($headers,$valid){ $r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers); return $r['status']===303 && $r['headers']['location']===CONTACT_PATH && $r['headers']['cache-control']==='no-store' && mailCount()===1 && queueAction()['completed']===1 && queueAction()['pending']===0; });
formCheck('Successful token cannot authorize altered input',function()use($headers,$valid){$bad=$valid;$bad['name']='Changed visitor';$r=contactRequest(CONTACT_PATH,'POST',http_build_query($bad),$headers);return $r['status']===403 && mailCount()===1 && queueAction()['pending']===0;});
formCheck('Redirected form shows confirmation and a fresh token',function()use($headers,$token){ $r=contactRequest(CONTACT_PATH,'GET','',['Cookie'=>$headers['Cookie']]); preg_match('/name="csrf" value="([a-f0-9]{64})"/',$r['body'],$m); return $r['status']===200 && str_contains($r['body'],'Your message has been saved for delivery.') && isset($m[1]) && $m[1]!==$token; });
formCheck('Token from another session is rejected',function()use($valid){ $r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),['Content-Type'=>'application/x-www-form-urlencoded']); return $r['status']===403 && mailCount()===1; });
formCheck('Wrong methods and HEAD retain method contracts',function(){ $wrong=contactRequest(CONTACT_PATH,'PUT'); $head=contactRequest(CONTACT_PATH,'HEAD'); return $wrong['status']===405 && $wrong['headers']['allow']==='GET, HEAD, POST' && $head['status']===200 && $head['body']===''; });
formCheck('Request context carries JSON, form values, query, and headers',function(){ $json=contactRequest('/hosting-body?q=value','POST','{"value":42}',['Content-Type'=>'application/json','X-Example'=>'sent']); $data=json_decode($json['body'],true); $form=contactRequest('/hosting-body','POST','a=one&a=two&list[]=1&list[]=2',['Content-Type'=>'application/x-www-form-urlencoded']); $values=json_decode($form['body'],true)['values']; return $json['status']===200 && $data===['values'=>['value'=>42],'header'=>'sent','query'=>['q'=>'value']] && $form['status']===200 && $values===['a'=>'two','list'=>['1','2']]; });
formCheck('Malformed JSON and unregistered methods never reach a write handler',function(){ $bad=contactRequest('/hosting-body','POST','{bad',['Content-Type'=>'application/json']); $get=contactRequest('/hosting-body'); $missing=contactRequest('/not-registered','POST',''); return $bad['status']===400 && $get['status']===405 && $get['headers']['allow']==='POST' && $missing['status']===404; });
formCheck('Legacy redirect ignores untrusted destinations',function(){ $r=contactRequest('/hosting-legacy?page_id=41&next=https%3A%2F%2Fevil.test'); $bad=contactRequest('/hosting-legacy?page_id[]=41'); return $r['status']===302 && $r['headers']['location']==='/docs' && $bad['status']===404; });
formCheck('Expired successful receipt cannot authorize a replay',function()use($headers,$valid,$fixtureRoot){
    $sid=explode('=',$headers['Cookie'],2)[1];
    fixturePhp('session_id('.var_export($sid,true).');session_start(["save_path"=>'.var_export($fixtureRoot.'/storage/contact-forms/sessions',true).',"use_cookies"=>0,"cache_limiter"=>""]);foreach($_SESSION["contact_forms"]["contact"]["receipts"] as &$receipt)$receipt["expires"]=0;unset($receipt);session_write_close();echo "{}";');
    $r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);
    return $r['status']===403 && queueAction()['pending']===0 && mailCount()===1;
});
formCheck('Concurrent identical POSTs both redirect and queue exactly one job',function()use($headers,&$valid,$base,$fixtureRoot){
    // Reset only this disposable fixture limiter so this independently tested scenario has a budget.
    fixturePhp('unlink('.var_export($fixtureRoot.'/storage/contact-forms/rate.json',true).');echo "{}";');
    $valid['csrf']=refreshFormToken($headers);$valid['message']='Concurrent test message.';
    $script='$ctx=stream_context_create(["http"=>["method"=>"POST","content"=>'.var_export(http_build_query($valid),true).',"header"=>'.var_export('Content-Type: application/x-www-form-urlencoded'."\r\n".'Cookie: '.$headers['Cookie'],true).',"ignore_errors"=>true,"follow_location"=>0,"timeout"=>10]]);file_get_contents('.var_export($base.CONTACT_PATH,true).',false,$ctx);echo $http_response_header[0];';
    $children=[];
    for($i=0;$i<2;$i++){$p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-r',$script],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);fclose($pipes[0]);$children[]=[$p,$pipes];}
    $ok=true;foreach($children as [$p,$pipes]){$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);if(proc_close($p)!==0 || !str_contains($out,'303'))$ok=false;}
    $saved=contactRequest(CONTACT_PATH,'GET','',['Cookie'=>$headers['Cookie']]);
    $again=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);
    $savedAgain=contactRequest(CONTACT_PATH,'GET','',['Cookie'=>$headers['Cookie']]);
    return $ok && queueAction()['pending']===1 && mailCount()===1 && $again['status']===303
        && str_contains($saved['body'],'saved for delivery') && str_contains($savedAgain['body'],'saved for delivery');
});
formCheck('Cookie rotation and forged forwarded addresses cannot bypass rate limiting',function(){ $limited=false; for($i=0;$i<21;$i++){ $r=contactRequest(CONTACT_PATH,'POST','csrf=wrong',['Content-Type'=>'application/x-www-form-urlencoded','X-Forwarded-For'=>'203.0.113.'.($i+1)]); if($r['status']===429){ $limited=$r['headers']['retry-after']==='600'; break; } if($r['status']!==403)return false; } return $limited && mailCount()===1; });
formCheck('A successful receipt still works after the ordinary attempt budget is exhausted',function()use($headers,$valid){$r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);return $r['status']===303 && queueAction()['pending']===1 && mailCount()===1;});
formCheck('Receipt history retains only the last three successful tokens',function()use($headers,$valid,$fixtureRoot){
    fixturePhp('unlink('.var_export($fixtureRoot.'/storage/contact-forms/rate.json',true).');echo "{}";');
    $first=null;
    for($i=0;$i<4;$i++){$valid['csrf']=refreshFormToken($headers);$valid['message']='Receipt history message '.$i;if($i===0)$first=$valid;$r=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);if($r['status']!==303)return false;}
    $old=contactRequest(CONTACT_PATH,'POST',http_build_query($first),$headers);
    $recent=contactRequest(CONTACT_PATH,'POST',http_build_query($valid),$headers);
    return $old['status']===403 && $recent['status']===303 && queueAction()['pending']===5 && mailCount()===1;
});
formCheck('Private queue CLI reports worker activity and fresh pending jobs are healthy',function(){
    $status=queueCli('status');$health=queueCli('health');$data=json_decode($status['out'],true,flags:JSON_THROW_ON_ERROR);$result=json_decode($health['out'],true,flags:JSON_THROW_ON_ERROR);
    return $status['code']===0 && $health['code']===0 && $result['ok'] && $data['pending']===5 && $data['latest_worker_run_finished']===true
        && is_int($data['last_worker_started_at']) && !str_contains($status['out'],'visitor@example.test') && !str_contains($status['out'],'Receipt history message');
});
formCheck('Overdue queue CLI warns nonzero without changing public application health',function()use($fixtureRoot){
    fixturePhp('$db=new PDO("sqlite:".'.var_export($fixtureRoot.'/storage/job-queue/jobs.sqlite',true).');$db->exec("UPDATE jobs SET available_at=".(time()-1000)." WHERE status=\'pending\'");echo "{}";');
    $r=queueCli('health');$data=json_decode($r['out'],true,flags:JSON_THROW_ON_ERROR);$public=contactRequest('/health');
    return $r['code']===1 && !$data['ok'] && $data['warnings']===['overdue_jobs'] && $data['queue']['pending']===5
        && $public['status']===200 && !str_contains($public['body'],'oldest_due') && !str_contains($public['body'],'worker');
});
formCheck('CLI work prunes only expired completed payloads with shared runtime permissions',function()use($fixtureRoot){
    fixturePhp('$db=new PDO("sqlite:".'.var_export($fixtureRoot.'/storage/job-queue/jobs.sqlite',true).');$now=time();$s=$db->prepare("INSERT INTO jobs(id,type,payload_version,payload,status,max_attempts,available_at,created_at,completed_at) VALUES(?,\'test.retention\',1,\'{}\',?,5,?,?,?)");foreach(["completed","failed"] as $status)$s->execute([bin2hex(random_bytes(16)),$status,$now-40*86400,$now-40*86400,$now-40*86400]);echo "{}";');
    $r=queueCli('work');$result=json_decode($r['out'],true,flags:JSON_THROW_ON_ERROR);$status=queueAction();
    return $r['code']===0 && $result['completed']===5 && $result['retention']===['days'=>30,'supported'=>true,'enabled'=>true,'pruned_completed'=>1]
        && $status['completed']===6 && $status['failed']===1 && $status['pending']===0 && mailCount()===6;
});
echo "$passed passed, $failed failed ($base).\n"; exit($failed ? 1 : 0);
