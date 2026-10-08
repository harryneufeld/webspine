<?php
declare(strict_types=1);
use Webspine\{App, Files, Response, VisitorErrors};

return static function(string $base,string $original):void {
    $root=$base.'/language-errors';copyTree($original,$root);
    $catalogue=$root.'/site/errors.json';$meta=file_get_contents($root.'/site/meta.php');
    $german=['language'=>'de-DE','http'=>[
        '400'=>['title'=>'Ungültige Anfrage','message'=>'Bitte prüfen: <script> & "Text".'],
        '404'=>['title'=>'Datei fehlt','message'=>'Diese Datei wurde nicht gefunden.'],
        '405'=>['title'=>'Methode nicht erlaubt','message'=>'Bitte die Seite öffnen.'],
        '503'=>['title'=>'Dienst fehlt','message'=>'Bitte später versuchen.'],
    ],'unavailable'=>['title'=>'Vorübergehend nicht erreichbar','message'=>'Bitte versuchen Sie es später erneut. <private>','operator'=>'']];
    $write=static fn(array $data)=>Files::write($catalogue,json_encode($data,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE));
    $public=static function(string $method='GET',string $uri='/')use($root):array {
        $log=$root.'/storage/visitor-errors.log';if(is_file($log))unlink($log);
        $script='$_SERVER["REQUEST_METHOD"]='.var_export($method,true).';$_SERVER["REQUEST_URI"]='.var_export($uri,true).';ob_start();require '.var_export($root.'/public/index.php',true).';echo json_encode(["status"=>http_response_code(),"body"=>ob_get_clean()]);';
        $p=proc_open([PHP_BINARY,'-c',php_ini_loaded_file()?:'','-d','display_errors=0','-d','log_errors=1','-d','error_log='.$log,'-r',$script],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes,$root);
        fclose($pipes[0]);$out=stream_get_contents($pipes[1]);$err=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);
        if(proc_close($p)!==0)throw new RuntimeException('Visitor error process failed: '.$err);
        return json_decode($out,true,flags:JSON_THROW_ON_ERROR)+['log'=>is_file($log)?file_get_contents($log):''];
    };
    $safeGerman=static fn(array $r):bool=>$r['status']===503&&str_contains($r['body'],'lang="de-DE"')&&str_contains($r['body'],'Bitte versuchen Sie')&&str_contains($r['body'],'&lt;private&gt;')&&!str_contains($r['body'],'PRIVATE_SENTINEL')&&!str_contains($r['body'],'console.php');
    check('Site language defaults to English for existing metadata',fn()=>(new App($root))->site->meta['language']==='en');
    check('Language accepts supported language/script/region/variant tags',static function():bool {
        foreach(['en','de-DE','zh-Hans-CN','es-419','de-CH-1901','sl-rozaj-biske']as$tag)if(VisitorErrors::language($tag)!==$tag)return false;return true;
    });
    check('Language rejects unsafe and unsupported values without echoing them',static function():bool {
        foreach(['',null,42,'x','en_US','../en','en-','en-u-ca-gregory','en" onload="PRIVATE_SENTINEL',str_repeat('a',64)]as$value) {
            try{VisitorErrors::language($value);return false;}catch(InvalidArgumentException $e){if(str_contains($e->getMessage(),'PRIVATE_SENTINEL'))return false;}
        }return true;
    });
    Files::write($root.'/site/meta.php',str_replace('return [',"return ['language'=>'de-DE',",$meta));
    check('Site metadata exposes configured language',fn()=>(new App($root))->site->meta['language']==='de-DE');
    Files::write($root.'/site/meta.php',str_replace('return [',"return ['language'=>null,",$meta));
    check('Invalid metadata language fails CLI health with actionable diagnostics',static function()use($root):bool {
        $r=cli($root,['health']);return $r['code']!==0&&str_contains($r['err'],'Invalid site language');
    });
    Files::write($root.'/site/meta.php',$meta);
    check('Absent catalogue retains existing plain-text client errors',static function()use($root):bool {
        $r=(new App($root))->handle('GET','/%00');return $r->status===400&&($r->headers['Content-Type']??'')==='text/plain; charset=utf-8'&&($r->headers['Cache-Control']??'')==='no-store';
    });
    $write($german);$app=new App($root);
    check('Localized client errors escape plain copy and set language/cache headers',static function()use($app):bool {
        $r=$app->handle('GET','/%00');return $r->status===400&&str_contains($r->body,'lang="de-DE"')&&str_contains($r->body,'&lt;script&gt; &amp; &quot;Text&quot;')&&!str_contains($r->body,'<script>')&&($r->headers['Content-Language']??'')==='de-DE'&&($r->headers['Cache-Control']??'')==='no-store';
    });
    check('Both router and asset method errors preserve Allow and status',static function()use($app):bool {
        foreach(['/','/assets/theme/test-theme/style.css']as$path){$r=$app->handle('POST',$path);if($r->status!==405||($r->headers['Allow']??'')!=='GET, HEAD'||!str_contains($r->body,'Methode nicht erlaubt'))return false;}return true;
    });
    check('Configured asset errors do not replace site-owned 404 pages',fn()=>$app->handle('GET','/assets/theme/test-theme/missing.css')->status===404&&str_contains($app->handle('GET','/assets/theme/test-theme/missing.css')->body,'Datei fehlt')&&!str_contains($app->handle('GET','/missing')->body,'Datei fehlt'));
    $app->router->get('/visitor-api',fn()=>Response::json(['error'=>'machine-copy'],503));
    check('Application responses and health JSON retain their presentation',static function()use($app):bool {
        $api=$app->handle('GET','/visitor-api');$health=$app->handle('GET','/health');return $api->status===503&&json_decode($api->body,true)===['error'=>'machine-copy']&&str_starts_with($health->headers['Content-Type'],'application/json');
    });
    check('Explicit presentation preserves retry/cache/security headers',static function()use($root):bool {
        $r=VisitorErrors::load($root)->present(new Response('private',503,['Retry-After'=>'60','Cache-Control'=>'private, no-store','X-Content-Type-Options'=>'nosniff']));return $r->status===503&&$r->headers['Retry-After']==='60'&&$r->headers['Cache-Control']==='private, no-store'&&$r->headers['X-Content-Type-Options']==='nosniff'&&!str_contains($r->body,'private');
    });
    check('Pre-App request rejection uses the data-only catalogue',static function()use($public):bool {
        $r=$public('GET','/%00');return $r['status']===400&&str_contains($r['body'],'Ungültige Anfrage')&&str_contains($r['body'],'&lt;script&gt;');
    });
    unset($app);gc_collect_cycles();
    $failures=['config/local.php'=>"<?php return ['PRIVATE_SENTINEL'=>];",'site/meta.php'=>'<?php throw new RuntimeException("PRIVATE_SENTINEL");','site/themes/test-theme/home.php'=>'<?php throw new RuntimeException("PRIVATE_SENTINEL");','core/bootstrap.php'=>'<?php throw new RuntimeException("PRIVATE_SENTINEL");'];
    foreach($failures as$file=>$broken){
        $path=$root.'/'.$file;$saved=is_file($path)?file_get_contents($path):null;Files::write($path,$broken);
        try{
            check('Localized emergency survives '.$file.' failure with private logs',static function()use($public,$safeGerman,$file):bool{$r=$public();return $safeGerman($r)&&str_contains($r['log'],$file)&&str_contains($r['log'],$file==='config/local.php'?'syntax error':'PRIVATE_SENTINEL');});
            check('Localized emergency HEAD omits its body for '.$file,static function()use($public):bool{$r=$public('HEAD');return $r['status']===503&&$r['body']==='';});
        }finally{if($saved===null)unlink($path);else Files::write($path,$saved);}
    }
    rename($root.'/storage/site.sqlite',$root.'/storage/installed.sqlite');
    try { check('Uninstalled database uses localized setup/unavailable copy without creating data',fn()=>$safeGerman($public())&&!is_file($root.'/storage/site.sqlite')); }
    finally { rename($root.'/storage/installed.sqlite',$root.'/storage/site.sqlite'); }
    Files::write($root.'/storage/update-pending.json','{}');
    check('Interrupted update uses localized emergency without exposing recovery detail',static function()use($public,$safeGerman):bool{$r=$public();return $safeGerman($r)&&!str_contains($r['body'],'Update recovery')&&str_contains($r['log'],'Update recovery required');});
    unlink($root.'/storage/update-pending.json');
    // A directory where the lock file belongs reliably simulates lock acquisition failure.
    unlink($root.'/storage/update.lock');mkdir($root.'/storage/update.lock');
    check('Lock acquisition failure uses localized safe 503',fn()=>$safeGerman($public()));
    rmdir($root.'/storage/update.lock');
    foreach(['broken'=>'{"PRIVATE_SENTINEL":','oversized'=>str_repeat('x',65537),'unknown'=>'{"PRIVATE_SENTINEL":true}','object'=>'[]','map'=>'{"http":[]}','unavailable'=>'{"unavailable":[]}','type'=>'{"http":{"405":{"title":[],"message":"x"}}}','status'=>'{"http":{"200":{"title":"x","message":"x"}}}','control'=>'{"unavailable":{"message":"\u0000"}}','language'=>'{"language":"en_US"}']as$name=>$raw){
        Files::write($catalogue,$raw);
        check('Invalid '.$name.' catalogue fails health and falls back without disclosure',static function()use($root,$public):bool {
            $health=cli($root,['health']);$r=$public();return $health['code']!==0&&$r['status']===503&&str_contains($r['body'],'lang="en"')&&!str_contains($r['body'],'PRIVATE_SENTINEL')&&str_contains($r['log'],'visitor catalogue failed');
        });
    }
    $write($german);
    $helper=$root.'/core/src/VisitorErrors.php';$saved=file_get_contents($helper);
    foreach(['missing'=>null,'parse failure'=>'<?php return ];']as$name=>$broken){
        if($broken===null)unlink($helper);else Files::write($helper,$broken);
        check('Minimal English emergency survives presentation helper '.$name,static function()use($public):bool{$r=$public();return $r['status']===503&&str_contains($r['body'],'lang="en"')&&str_contains($r['body'],'console.php health')&&!str_contains($r['body'],'syntax error');});
        Files::write($helper,$saved);
    }
    unset($app);gc_collect_cycles();
    // Site-owned catalogue participates in the existing updater preservation assertions.
    Files::write($original.'/site/errors.json',json_encode(['language'=>'en'],JSON_THROW_ON_ERROR));
};
