<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
use Webspine\{Release,ReleaseVerification,Files};
$source=dirname(__DIR__,2);$base=$source.'/storage/release-verification-'.bin2hex(random_bytes(6));$root=$base.'/source';$results=[];
function releaseCheck(bool $ok,string $name):void {global $results;if(!$ok)throw new RuntimeException($name);$results[]=$name;}
function releaseFails(callable $call,string $message):bool {try{$call();}catch(Throwable $e){return str_contains($e->getMessage(),$message);}return false;}
function releaseCopy(string $source,string $target):void {
    if(!is_dir($target))mkdir($target,0700,true);
    foreach(new DirectoryIterator($source)as $entry){if($entry->isDot())continue;if($entry->isLink())throw new RuntimeException('Fixture link.');if($entry->isDir())releaseCopy($entry->getPathname(),$target.'/'.$entry->getFilename());else copy($entry->getPathname(),$target.'/'.$entry->getFilename());}
}
function releaseRemove(string $path,string $allowed):void {
    if(!str_starts_with(realpath($path)?:'',realpath($allowed).DIRECTORY_SEPARATOR))throw new RuntimeException('Unsafe cleanup.');
    foreach(new DirectoryIterator($path)as $entry){if($entry->isDot())continue;if($entry->isDir()&&!$entry->isLink())releaseRemove($entry->getPathname(),$allowed);else unlink($entry->getPathname());}rmdir($path);
}
function rewriteRelease(string $input,string $output,string $path,string $body):void {
    copy($input,$output);$zip=new ZipArchive();$zip->open($output);$m=json_decode($zip->getFromName('release.json'),true,flags:JSON_THROW_ON_ERROR);
    $m['files'][$path]=hash('sha256',$body);$zip->addFromString($path,$body);$zip->addFromString('release.json',json_encode($m,JSON_THROW_ON_ERROR)."\n");$zip->close();
}
try {
    mkdir($base,0700,true);foreach(['core','public','site','config']as $directory)releaseCopy($source.'/'.$directory,$root.'/'.$directory);
    foreach(['README.md','LICENSE','AGENTS.md','.gitattributes','.gitignore']as $file)copy($source.'/'.$file,$root.'/'.$file);
    Files::write($root.'/storage/.gitkeep','');$version=(require $root.'/core/version.php')['version'];$out=$base.'/assets';
    $vendor="<?php\r\n// Synthetic vendor bytes.\r\n";$binary="\x89PNG\r\n\x1a\n\0\r\xff";
    Files::write($root.'/core/vendor/synthetic.php',$vendor);Files::write($root.'/public/binary.png',$binary);
    $archives=[];foreach([false,true]as $full)$archives[] = Release::package($root,$full,$out.'/webspine-'.($full?'':'core-').$version.'.zip');
    foreach($archives as $archive){
        $m=ReleaseVerification::archive($archive,$archive.'.sha256.json');$zip=new ZipArchive();$zip->open($archive);
        releaseCheck($zip->getFromName('core/vendor/synthetic.php')===$vendor && $zip->getFromName('public/binary.png')===$binary,'Binary and upstream vendor bytes survive packaging unchanged');
        foreach($m['files']as $path=>$hash)releaseCheck($hash===hash_file('sha256',$root.'/'.$path),'Inventory hashes exactly match source bytes');
        $zip->close();
    }
    Release::stage($archives[0],$base.'/stage',['version'=>'0.0.0','api'=>1]);releaseCheck(is_file($base.'/stage/core/src/App.php'),'Verified core ZIP passes updater staging');
    Files::write($out.'/webspine-0.1.7.zip','Old unrelated artifact');Files::write($out.'/webspine-core-0.1.7.zip.sha256.json','Old unrelated sidecar');
    $assets=ReleaseVerification::assets($out,$version,$root);releaseCheck(count($assets)===4 && count(array_filter($assets,static fn(array $asset):bool=>str_contains($asset['name'],$version)))===4,'Asset selection ignores unrelated archives and returns exactly four verified files');
    $original=file_get_contents($root.'/README.md');$zipHash=hash_file('sha256',$archives[0]);
    foreach(["line\r\nline\n","line\rline\n"]as $mixed){
        Files::write($root.'/README.md',$mixed);
        releaseCheck(releaseFails(fn()=>Release::package($root,false,$archives[0]),'Noncanonical text') && file_get_contents($root.'/README.md')===$mixed && hash_file('sha256',$archives[0])===$zipHash,'CRLF and bare CR sources fail without rewriting source or replacing prior artifacts');
    }
    Files::write($root.'/README.md',$original);
    $metadata=file_get_contents($root.'/core/vendor/dependencies.json');Files::write($root.'/core/vendor/dependencies.json',str_replace("\n","\r\n",$metadata));
    releaseCheck(releaseFails(fn()=>Release::package($root,false,$archives[0]),'Noncanonical text'),'Repository-owned vendor metadata follows the targeted LF policy');Files::write($root.'/core/vendor/dependencies.json',$metadata);
    rewriteRelease($archives[0],$base.'/mixed.zip','README.md',"mixed\r\ntext\n");
    releaseCheck(releaseFails(fn()=>ReleaseVerification::archive($base.'/mixed.zip'),'Noncanonical text'),'Archive check rejects mixed text even with matching rewritten hashes');
    rewriteRelease($archives[0],$base.'/vendor.zip','core/vendor/phpmailer/src/PHPMailer.php',"<?php\n// Replaced vendor.\n");
    releaseCheck(releaseFails(fn()=>ReleaseVerification::archive($base.'/vendor.zip'),'Vendor checksum mismatch'),'Archive check rejects altered vendor bytes despite a rewritten inventory');
    copy($archives[0],$base.'/corrupt.zip');$zip=new ZipArchive();$zip->open($base.'/corrupt.zip');$zip->addFromString('README.md','Tampered');$zip->close();
    releaseCheck(releaseFails(fn()=>ReleaseVerification::archive($base.'/corrupt.zip'),'hash mismatch'),'Archive hashes detect changed payloads');
    copy($archives[0],$base.'/extra.zip');$zip=new ZipArchive();$zip->open($base.'/extra.zip');$zip->addFromString('config/local.php','private');$zip->close();
    releaseCheck(releaseFails(fn()=>ReleaseVerification::archive($base.'/extra.zip'),'private'),'Archive verification rejects unlisted private paths');
    $sidecar=$archives[0].'.sha256.json';$originalSidecar=file_get_contents($sidecar);Files::json($sidecar,['file'=>basename($archives[0]),'sha256'=>str_repeat('0',64)]);
    releaseCheck(releaseFails(fn()=>ReleaseVerification::assets($out,$version),'sidecar mismatch'),'A bad sidecar blocks the whole publication set');Files::write($sidecar,$originalSidecar);
    $marker=$base.'/executed';rewriteRelease($archives[0],$base.'/code.zip','core/bootstrap.php','<?php file_put_contents('.var_export($marker,true).',"executed");' . "\n");
    ReleaseVerification::archive($base.'/code.zip');releaseCheck(!is_file($marker),'Archive verification treats PHP as data and never executes it');
    Files::write($root.'/README.md',$original."Changed\n");releaseCheck(releaseFails(fn()=>ReleaseVerification::assets($out,$version,$root),'publisher checkout'),'Publication source comparison rejects a different checkout');Files::write($root.'/README.md',$original);
    rewriteRelease($archives[1],$base.'/pair.zip','README.md',$original."Changed\n");copy($base.'/pair.zip',$archives[1]);Files::json($archives[1].'.sha256.json',['file'=>basename($archives[1]),'sha256'=>hash_file('sha256',$archives[1])]);
    releaseCheck(releaseFails(fn()=>ReleaseVerification::assets($out,$version),'inventories differ'),'Publication rejects individually valid but inconsistent core/bootstrap pairs');
    echo 'PASS '.count($results)." archive/source assertions\nRelease verification tests passed; isolated Windows/Linux fixtures only.\n";
}catch(Throwable $e){fwrite(STDERR,$e->getMessage()."\n".$e->getTraceAsString()."\n");$code=1;}
finally{if(is_dir($base))releaseRemove($base,$source.'/storage');}
exit($code??0);
