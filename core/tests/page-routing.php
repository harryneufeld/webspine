<?php
declare(strict_types=1);
use Webspine\{App, Request, Response, Router, Files};

return static function(string $base,string $site):void {
    check('Redirect helpers accept local and fixed HTTP(S) destinations with explicit statuses',function():bool {
        foreach([301,302,303,307,308]as$status){$r=Response::redirect('/new?x=1#part',$status);if($r->status!==$status||$r->body!==''||$r->headers['Location']!=='/new?x=1#part')return false;}
        return Response::redirect('https://example.org:8443/new')->status===302&&Response::redirect('http://[::1]:8080/new')->headers['Location']==='http://[::1]:8080/new'&&Response::redirect('/encoded%20name?q=%0a')->headers['Location']==='/encoded%20name?q=%0a';
    });
    check('Redirect validation rejects injection, authority ambiguity and unsupported schemes/statuses',function():bool {
        foreach(['','relative','//evil.example','/%2fevil.example','/\\evil.example',"/new\r\nX-Test: bad",'/new%0d%0aheader','/%5cevil.example','/bad%zz','javascript:alert(1)','mailto:test@example.org','https://user:password@example.org/','https:///missing-host',"/bad\xff"]as$bad)if(!expectError(fn()=>Response::redirect($bad)))return false;
        return expectError(fn()=>Response::redirect('/',200))&&expectError(fn()=>Response::redirect('/',304));
    });
    $router=new Router();$router->page('/',fn()=>new Response('root'),'append');
    $router->page('/plain',fn()=>new Response('plain'),'strip');$router->page('/nested/page/',fn()=>new Response('nested'),'append');
    check('Canonical page routes serve root and declared paths without redirect loops',fn()=>$router->dispatch('GET','/')->body==='root'&&$router->dispatch('GET','/plain')->body==='plain'&&$router->dispatch('GET','/nested/page/')->body==='nested');
    $raw='x=a%20b&x=c+d&name=é&line=%0a&path=%5c&literal=back\\slash&value=a%26b';
    $request=new Request('GET','/plain/?'.$raw);$r=$router->dispatch('GET',$request->path,$request);
    check('Slash redirects preserve raw queries, including repeated/encoded and UTF-8 values',fn()=>$r->status===308&&$r->headers['Location']==='/plain?'.$raw&&$router->dispatch('GET','/nested/page')->headers['Location']==='/nested/page/');
    $emptyQuery=new Request('HEAD','/plain/?');
    check('HEAD and empty query delimiters follow GET canonicalization',fn()=>$router->dispatch('HEAD',$emptyQuery->path,$emptyQuery)->headers['Location']==='/plain?'&&$router->dispatch('HEAD','/nested/page')->status===308);
    $router->post('/plain/',fn(Request $r)=>new Response($r->body));
    $post=new Request('POST','/plain/',[], 'kept-body');
    check('Slash policies never rewrite POST into GET or swallow explicit POST handlers',fn()=>$router->dispatch('POST',$post->path,$post)->body==='kept-body'&&$router->dispatch('POST','/nested/page')->status===405&&$router->dispatch('PUT','/plain/')->status===405);
    $router->page('/distinct',fn()=>new Response('one'));$router->page('/distinct/',fn()=>new Response('two'));
    check('Preserve policy allows intentionally distinct slash paths',fn()=>$router->dispatch('GET','/distinct')->body==='one'&&$router->dispatch('GET','/distinct/')->body==='two'&&$router->dispatch('GET','/unknown/')===null);
    check('Invalid page paths and mismatched canonical declarations fail clearly',function():bool {
        foreach(['plain','//plain','/a//b','/a/../b','/./plain','/a?x=1','/a#part','/a%20b','/assets/theme/x','/assets',"/back\\slash"]as$bad)if(!expectError(fn()=>(new Router())->page($bad,fn()=>new Response())))return false;
        return expectError(fn()=>(new Router())->page('/a/',fn()=>new Response(),'strip'))&&expectError(fn()=>(new Router())->page('/a',fn()=>new Response(),'append'))&&expectError(fn()=>(new Router())->page('/a',fn()=>new Response(),'unknown'));
    });
    check('Exact and alias conflicts reject partial page registration',function():bool {
        $r=new Router();$r->get('/a/',fn()=>new Response('custom'));
        return expectError(fn()=>$r->page('/a',fn()=>new Response(),'strip'),'Duplicate page route')&&$r->dispatch('GET','/a')===null&&$r->dispatch('GET','/a/')->body==='custom';
    });
    check('Page/pattern conflicts fail regardless of registration order',function():bool {
        $r=new Router();$r->getPattern('~^/a/?$~D',fn()=>new Response());$s=new Router();$s->page('/a',fn()=>new Response(),'strip');
        return expectError(fn()=>$r->page('/a',fn()=>new Response(),'strip'),'conflicts')&&expectError(fn()=>$s->getPattern('~^/a/$~D',fn()=>new Response()),'conflicts')&&!expectError(fn()=>$s->postPattern('~^/a/$~D',fn()=>new Response()));
    });
    $root=$base.'/page-route-site';copyTree($site,$root);$pages=require $root.'/site/pages.php';
    $pages['auto']=['template'=>'page','title'=>'Auto page','path'=>'/auto','slash'=>'strip','data'=>['body'=>'Auto content']];
    $pages['appended']=['template'=>'page','title'=>'Append page','path'=>'/appended/','slash'=>'append','data'=>['body'=>'Append content']];
    Files::write($root.'/site/pages.php','<?php return '.var_export($pages,true).';');$app=new App($root);
    check('Site page paths auto-register while existing custom and pattern routes work',fn()=>str_contains($app->handle('GET','/auto')->body,'Auto content')&&$app->handle('GET','/auto/')->headers['Location']==='/auto'&&$app->handle('GET','/appended')->headers['Location']==='/appended/'&&$app->handle('GET','/docs')->status===200&&$app->handle('GET','/field-notes')->status===200&&$app->handle('GET','/missing')->status===404);
    $directory=$root.'/site/themes/test-theme';Files::write($directory.'/section2.php','<p>Digit template</p>');Files::write($directory.'/-legacy.php','<p>Legacy template</p>');
    check('Digit templates render and legacy leading-hyphen templates remain compatible',fn()=>str_contains($app->theme->render('section2',['title'=>'Digits'])->body,'Digit template')&&str_contains($app->theme->render('-legacy',['title'=>'Legacy'])->body,'Legacy template'));
    check('Template safety and missing-template diagnostics remain intact',function()use($app):bool {
        foreach(['2section','../section2','section2.php','path/section2','path\\section2','Section2','section_2',"section2\0"]as$bad)if(!expectError(fn()=>$app->theme->render($bad),'Invalid template'))return false;
        return expectError(fn()=>$app->theme->render('missing2'),'Missing theme template');
    });
    $baseline=$pages;
    foreach(['duplicate'=>['path'=>'/docs'],'alias'=>['path'=>'/auto/'],'pattern'=>['path'=>'/pages/example'],'health'=>['path'=>'/health'],'not-found'=>['path'=>'/404']]as$name=>$changes){
        $changed=$baseline;
        if($name==='not-found')$changed['not-found']=array_replace($changed['not-found'],$changes);
        else $changed['extra']=['template'=>'page','title'=>'Conflict','data'=>['body'=>'Conflict']]+$changes;
        Files::write($root.'/site/pages.php','<?php return '.var_export($changed,true).';');
        check('Site declaration rejects '.$name.' route conflicts',fn()=>expectError(fn()=>new App($root)));
    }
    Files::write($root.'/site/pages.php','<?php return '.var_export($baseline,true).';');
    unset($app);gc_collect_cycles();
};
