<?php
declare(strict_types=1);
// Isolated fixtures only: capture-mail never contacts SMTP.
$base = $argv[1] ?? '';
if (in_array($base,['apache','nginx','cloudpanel'],true)) $base = 'http://'.$base;
elseif (!preg_match('~^http://(?:localhost|127\.0\.0\.1):[0-9]+$~D',$base)) throw new RuntimeException('Use local fixture server only.');
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
$first=contactRequest('/contact-example');
preg_match('/name="csrf" value="([a-f0-9]{64})"/',$first['body'],$match); $token=$match[1] ?? '';
$cookie=explode(';',$first['headers']['set-cookie'] ?? '')[0];
$headers=['Content-Type'=>'application/x-www-form-urlencoded','Cookie'=>$cookie];
$valid=['csrf'=>$token,'name'=>'Example user','email'=>'visitor@example.test','message'=>'A valid test message.','website'=>''];
formCheck('Form uses a private cookie, CSRF token, and no-store cache policy',fn()=>$first['status']===200 && strlen($token)===64 && str_contains(strtolower($first['headers']['set-cookie'] ?? ''),'httponly') && str_contains(strtolower($first['headers']['set-cookie'] ?? ''),'samesite=lax') && $first['headers']['cache-control']==='no-store');
formCheck('Missing CSRF token cannot dispatch mail',function()use($headers){ $r=contactRequest('/contact-example','POST','name=user',$headers); return $r['status']===403 && mailCount()===0; });
formCheck('Array-shaped fields are rejected',function()use($headers,$valid){ $bad=$valid; $bad['name']=['bad']; $r=contactRequest('/contact-example','POST',http_build_query($bad),$headers); return $r['status']===422 && mailCount()===0; });
formCheck('Validation retains escaped values and accessible field errors',function()use($headers,$valid){ $bad=$valid; $bad['name']='<script>alert(1)</script>'; $bad['email']='invalid'; $r=contactRequest('/contact-example','POST',http_build_query($bad),$headers); return $r['status']===422 && str_contains($r['body'],'&lt;script&gt;') && !str_contains($r['body'],'<script>') && str_contains($r['body'],'aria-invalid="true"') && mailCount()===0; });
formCheck('Honeypot and unknown fields cannot dispatch mail',function()use($headers,$valid){ $bad=$valid; $bad['website']='filled'; $r=contactRequest('/contact-example','POST',http_build_query($bad),$headers); $bad=$valid; $bad['to']='attacker@example.test'; $extra=contactRequest('/contact-example','POST',http_build_query($bad),$headers); return $r['status']===422 && $extra['status']===422 && mailCount()===0; });
formCheck('Unsupported form media and oversized bodies return 415/413',function()use($headers){ $plain=$headers; $plain['Content-Type']='text/plain'; $unsupported=contactRequest('/contact-example','POST','body',$plain); $large=contactRequest('/contact-example','POST',str_repeat('x',65537),$headers); return $unsupported['status']===415 && $large['status']===413 && mailCount()===0; });
formCheck('Valid submission uses a fixed recipient and a 303 redirect',function()use($headers,$valid){ $r=contactRequest('/contact-example','POST',http_build_query($valid),$headers); $messages=json_decode(contactRequest('/hosting-mail')['body'],true)['messages']; return $r['status']===303 && $r['headers']['location']==='/contact-example' && count($messages)===1 && $messages[0]['to']==='owner@example.test' && $messages[0]['subject']==='Website contact'; });
formCheck('Successful token cannot be replayed',function()use($headers,$valid){ $r=contactRequest('/contact-example','POST',http_build_query($valid),$headers); return $r['status']===403 && mailCount()===1; });
formCheck('Redirected form shows confirmation and a fresh token',function()use($headers,$token){ $r=contactRequest('/contact-example','GET','',['Cookie'=>$headers['Cookie']]); preg_match('/name="csrf" value="([a-f0-9]{64})"/',$r['body'],$m); return $r['status']===200 && str_contains($r['body'],'Your message has been sent.') && isset($m[1]) && $m[1]!==$token; });
formCheck('Token from another session is rejected',function()use($valid){ $r=contactRequest('/contact-example','POST',http_build_query($valid),['Content-Type'=>'application/x-www-form-urlencoded']); return $r['status']===403 && mailCount()===1; });
formCheck('Wrong methods and HEAD retain method contracts',function(){ $wrong=contactRequest('/contact-example','PUT'); $head=contactRequest('/contact-example','HEAD'); return $wrong['status']===405 && $wrong['headers']['allow']==='GET, HEAD, POST' && $head['status']===200 && $head['body']===''; });
formCheck('Request context carries JSON, form values, query, and headers',function(){ $json=contactRequest('/hosting-body?q=value','POST','{"value":42}',['Content-Type'=>'application/json','X-Example'=>'sent']); $data=json_decode($json['body'],true); $form=contactRequest('/hosting-body','POST','a=one&a=two&list[]=1&list[]=2',['Content-Type'=>'application/x-www-form-urlencoded']); $values=json_decode($form['body'],true)['values']; return $json['status']===200 && $data===['values'=>['value'=>42],'header'=>'sent','query'=>['q'=>'value']] && $form['status']===200 && $values===['a'=>'two','list'=>['1','2']]; });
formCheck('Malformed JSON and unregistered methods never reach a write handler',function(){ $bad=contactRequest('/hosting-body','POST','{bad',['Content-Type'=>'application/json']); $get=contactRequest('/hosting-body'); $missing=contactRequest('/not-registered','POST',''); return $bad['status']===400 && $get['status']===405 && $get['headers']['allow']==='POST' && $missing['status']===404; });
formCheck('Legacy redirect ignores untrusted destinations',function(){ $r=contactRequest('/hosting-legacy?page_id=41&next=https%3A%2F%2Fevil.test'); $bad=contactRequest('/hosting-legacy?page_id[]=41'); return $r['status']===302 && $r['headers']['location']==='/docs' && $bad['status']===404; });
formCheck('Cookie rotation and forged forwarded addresses cannot bypass rate limiting',function(){ $limited=false; for($i=0;$i<21;$i++){ $r=contactRequest('/contact-example','POST','csrf=wrong',['Content-Type'=>'application/x-www-form-urlencoded','X-Forwarded-For'=>'203.0.113.'.($i+1)]); if($r['status']===429){ $limited=$r['headers']['retry-after']==='600'; break; } if($r['status']!==403)return false; } return $limited && mailCount()===1; });
echo "$passed passed, $failed failed ($base).\n"; exit($failed ? 1 : 0);
