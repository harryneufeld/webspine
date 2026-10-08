<?php
declare(strict_types=1);
use Webspine\{App, Request, Files, AssetHashCache};
use Webspine\Contracts\Settings;

return static function(string $base,string $site):void {
    $root=$base.'/asset-cache-site';copyTree($site,$root);
    $config=require $root.'/config/example.php';
    $config['theme']=['asset_hash_cache'=>true,'debug'=>false];
    $app=new App($root,$config);$app->theme->refresh();
    $directory=$root.'/site/themes/test-theme/assets';
    $assets=['bench-font.woff2'=>65536,'bench-style.css'=>4096,'bench-image.png'=>196608];
    foreach($assets as$name=>$size)Files::write($directory.'/'.$name,str_repeat('x',$size));
    $start=hrtime(true);
    for($i=0;$i<50;$i++){$app->theme->refresh(false);foreach($assets as$name=>$size)$app->theme->assetUrl($name);}
    $elapsed=(hrtime(true)-$start)/1e6;$stats=$app->theme->assetHashStats();
    check('Warm lifecycles reuse hashes instead of hashing the full asset mix',fn()=>$stats===['hashes'=>3,'bytes'=>266240,'hits'=>147]);
    echo 'MEASURE 50 URL lifecycles: '.round($elapsed,2).' ms; hashed bytes='.$stats['bytes'].'; cache hits='.$stats['hits']."\n";
    $other=new App($root,$config);foreach($assets as$name=>$size)$other->theme->assetUrl($name);
    check('Fresh Apps reuse the per-site disk cache',fn()=>$other->theme->assetHashStats()===['hashes'=>0,'bytes'=>0,'hits'=>3]);
    $file=$directory.'/bench-style.css';$before=$app->theme->assetUrl('bench-style.css');
    Files::write($file,'changed-size');$app->theme->refresh(false);
    check('Metadata changes invalidate warm asset hashes',fn()=>$app->theme->assetUrl('bench-style.css')!==$before);
    $mtime=filemtime($file);Files::write($file,'changed-text');touch($file,$mtime);$app->theme->refresh();
    check('Explicit refresh rehashes even metadata-preserving edits',fn()=>str_ends_with($app->theme->assetUrl('bench-style.css'),substr(hash('sha256','changed-text'),0,12)));
    $url=$app->theme->assetUrl('bench-image.png');$first=$app->handle('GET',$url);$tag=$first->headers['ETag'];
    check('ETags describe exact delivered bytes and preserve MIME/cache policy',fn()=>$tag==='"'.hash('sha256',$first->body).'"'&&$first->headers['Cache-Control']==='public, max-age=3600'&&$first->headers['Content-Type']==='image/png');
    foreach([$tag,'W/'.$tag,'"different", W/'.$tag,'"comma,inside", '.$tag,'*']as$condition){
        check('Conditional GET matches '.$condition,fn()=>$app->handle(new Request('GET',$url,['If-None-Match'=>$condition]))->status===304&&$app->handle(new Request('GET',$url,['If-None-Match'=>$condition]))->body==='');
    }
    check('Malformed/nonmatching conditions return complete current bytes',function()use($app,$url,$first,$tag):bool {
        foreach(['"different"','garbage, '.$tag,$tag.',','*, '.$tag,'w/'.$tag,str_repeat('x',8193)]as$condition){$r=$app->handle(new Request('GET',$url,['If-None-Match'=>$condition]));if($r->status!==200||$r->body!==$first->body)return false;}return true;
    });
    check('Conditional HEAD returns 304 with matching validators and no body',function()use($app,$url,$tag):bool{$r=$app->handle(new Request('HEAD',$url,['If-None-Match'=>$tag]));return $r->status===304&&$r->body===''&&$r->headers['ETag']===$tag&&$r->headers['Cache-Control']==='public, max-age=3600';});
    check('Unconditional HEAD exposes current validators without body',function()use($app,$url,$tag):bool{$r=$app->handle(new Request('HEAD',$url));return $r->status===200&&$r->body===''&&$r->headers['ETag']===$tag;});
    $image=$directory.'/bench-image.png';$mtime=filemtime($image);Files::write($image,str_repeat('y',196608));touch($image,$mtime);
    check('ETags never trust stale metadata-cache hashes',function()use($app,$url,$tag):bool{$r=$app->handle(new Request('GET',$url,['If-None-Match'=>$tag]));return $r->status===200&&$r->body===str_repeat('y',196608)&&$r->headers['ETag']!==$tag;});
    check('Missing resources never satisfy wildcard conditions; mutations remain denied',fn()=>$app->handle(new Request('GET','/assets/theme/test-theme/missing.png',['If-None-Match'=>'*']))->status===404&&$app->handle(new Request('POST',$url,['If-None-Match'=>'*']))->status===405);
    $hits=new App($root,$config);$hits->theme->assetUrl('bench-style.css');unlink($file);$hits->theme->refresh(false);
    check('Deletion is checked before cache reuse',fn()=>expectError(fn()=>$hits->theme->assetUrl('bench-style.css'),'Missing theme asset')&&$hits->handle('GET','/assets/theme/test-theme/bench-style.css')->status===404);
    Files::write($file,'restored');
    $cache=$root.'/storage/theme-asset-hashes.json';Files::write($cache,'{broken');
    $corrupt=new App($root,$config);$corrupt->theme->assetUrl('bench-style.css');
    check('Corrupt caches fall back to hashing and repair disposable state',fn()=>$corrupt->theme->assetHashStats()['hashes']===1&&is_array(json_decode(file_get_contents($cache),true)));
    $disabled=new App($root,array_replace($config,['theme'=>['asset_hash_cache'=>false]]));
    $disabled->theme->assetUrl('bench-style.css');$disabled->theme->refresh(false);$disabled->theme->assetUrl('bench-style.css');
    check('Disabling the cache hashes each fresh lifecycle',fn()=>$disabled->theme->assetHashStats()['hashes']===2&&$disabled->theme->assetHashStats()['hits']===0);
    $different=new AssetHashCache($root,'different-version',true);$different->hash(realpath($file));
    check('Core-version changes reject previous cache entries',fn()=>$different->stats()['hashes']===1);
    unlink($cache);mkdir($cache);
    try{$unavailable=new App($root,$config);$unavailable->theme->assetUrl('bench-style.css');check('Unavailable cache storage does not break rendering',fn()=>$unavailable->theme->assetHashStats()['hashes']===1);}finally{rmdir($cache);}
    $bounded=new AssetHashCache($root,'bounded',true);for($i=0;$i<257;$i++){$path=$directory.'/bounded-'.$i.'.txt';Files::write($path,'x');$bounded->hash(realpath($path));}
    check('Disk hash cache has bounded entries and no leftover temporary files',fn()=>count(json_decode(file_get_contents($cache),true)['entries'])===256&&!glob($cache.'.*.tmp'));
    $secondRoot=$base.'/other-cache-site';copyTree($root,$secondRoot);$second=new App($secondRoot,$config);$second->theme->assetUrl('bench-style.css');
    check('Copied cache state cannot cross site identity',fn()=>$second->theme->assetHashStats()['hashes']===1);

    $debugConfig=array_replace($config,['theme'=>['asset_hash_cache'=>true,'debug'=>true]]);
    $debug=new App($root,$debugConfig);$debug->theme->render('home',['title'=>'Fixture']);$debug->theme->render('docs',['title'=>'Fixture']);
    $settings=$debug->services->get(Settings::class);$settings->set('theme','missing-theme');
    check('Debug detects stale Settings without switching identity midway',fn()=>expectError(fn()=>$debug->theme->render('home',['title'=>'Fixture']),'Stale theme rendering lifecycle')&&$debug->theme->active()==='test-theme');
    $settings->set('theme','test-theme');$debug->theme->refresh();
    check('Explicit refresh and health begin clean debug lifecycles',fn()=>$debug->theme->render('home',['title'=>'Fixture'])->status===200&&$debug->health()['ok']);
    $manifest=$root.'/site/themes/test-theme/theme.json';$original=file_get_contents($manifest);$debug->theme->refresh();$debug->theme->render('home',['title'=>'Fixture']);
    Files::write($manifest,$original."\n");
    check('Debug detects manifest edits at the next outer render boundary',fn()=>expectError(fn()=>$debug->theme->render('home',['title'=>'Fixture']),'Stale theme rendering lifecycle'));
    Files::write($manifest,$original);$debug->theme->refresh();$debug->theme->assetUrl('bench-style.css');Files::write($file,'changed-longer');
    check('Debug detects observed asset edits even with a cached URL',fn()=>expectError(fn()=>$debug->theme->assetUrl('bench-style.css'),'Stale theme rendering lifecycle'));
    $withDependency=json_decode($original,true);$withDependency['dependencies']=['field-notes'=>'0.1.0'];Files::json($manifest,$withDependency);
    $debug->theme->refresh();$debug->theme->render('home',['title'=>'Fixture']);
    $dependency=$root.'/site/plugins/field-notes/plugin.json';$dependencyOriginal=file_get_contents($dependency);
    try {
        Files::write($dependency,$dependencyOriginal."\n");
        check('Debug observes declared dependency manifest edits',fn()=>expectError(fn()=>$debug->theme->render('home',['title'=>'Fixture']),'Stale theme rendering lifecycle'));
    } finally { Files::write($dependency,$dependencyOriginal); Files::write($manifest,$original); }
    $debug->theme->refresh();$debug->theme->render('home',['title'=>'Fixture']);$debug->plugins->loaded['sqlite']['version']='999.0.0';
    check('Debug detects loaded dependency state changes',fn()=>expectError(fn()=>$debug->theme->render('home',['title'=>'Fixture']),'Stale theme rendering lifecycle'));
    $normal=new App($root,$config);$normal->theme->render('home',['title'=>'Fixture']);$normal->services->get(Settings::class)->set('theme','missing-theme');
    check('Debug disabled retains existing manual lifecycle behavior',fn()=>$normal->theme->render('home',['title'=>'Fixture'])->status===200);
    $normal->services->get(Settings::class)->set('theme','test-theme');
    check('Separate Apps own independent debug state',fn()=>(new App($root,$debugConfig))->theme->render('home',['title'=>'Fixture'])->status===200);
    $page=$root.'/site/themes/test-theme/boundary.php';
    Files::write($page,'<?php $app->services->get(\Webspine\Contracts\Settings::class)->set("theme","missing-theme"); echo $ui->component("wordmark", ["prefix"=>"web","bold"=>"spine","label"=>"wordmark","href"=>"/"]);');
    $boundary=new App($root,$debugConfig);$level=ob_get_level();$response=$boundary->theme->render('boundary',['title'=>'Fixture']);
    check('Debug keeps one identity through nested rendering and restores buffers',fn()=>$response->status===200&&ob_get_level()===$level&&expectError(fn()=>$boundary->theme->render('home',['title'=>'Fixture']),'Stale theme rendering lifecycle'));
    $boundary->services->get(Settings::class)->set('theme','test-theme');
    unset($boundary);
    unset($app,$other,$hits,$corrupt,$disabled,$unavailable,$second,$debug,$normal,$settings);gc_collect_cycles();
};
