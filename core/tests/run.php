<?php
declare(strict_types=1);
require dirname(__DIR__) . '/bootstrap.php';
use Webspine\{App, Files, Release, Updater, Registry};
use Webspine\Contracts\{Storage, Settings, Pages, Mail};

$passed = 0; $failed = 0;
function check(string $name, callable $assert): void {
    global $passed, $failed;
    try { if ($assert() === false) throw new RuntimeException('Assertion returned false.'); $passed++; echo "PASS $name\n"; }
    catch (Throwable $e) { $failed++; echo "FAIL $name: " . $e->getMessage() . "\n"; }
}
function expectError(callable $callback, ?string $message = null): bool {
    try { $callback(); } catch (Throwable $e) { return $message === null || str_contains($e->getMessage(), $message); }
    return false;
}
function copyTree(string $source, string $destination): void {
    if (!is_dir($destination)) mkdir($destination, 0700, true);
    foreach (new DirectoryIterator($source) as $entry) {
        if ($entry->isDot()) continue;
        if ($entry->isLink()) throw new RuntimeException('Test fixtures may not be links.');
        if ($entry->isDir()) copyTree($entry->getPathname(), $destination . '/' . $entry->getFilename());
        else copy($entry->getPathname(), $destination . '/' . $entry->getFilename());
    }
}
function removeFixture(string $path, string $allowed): void {
    $resolved = realpath($path); $base = realpath($allowed);
    if (!$resolved || !$base || !str_starts_with($resolved, $base . DIRECTORY_SEPARATOR)) throw new RuntimeException('Unsafe test cleanup.');
    foreach (new DirectoryIterator($path) as $file) {
        if ($file->isDot()) continue;
        if ($file->isDir() && !$file->isLink()) removeFixture($file->getPathname(), $allowed);
        else unlink($file->getPathname());
    }
    rmdir($path);
}
function cli(string $root, array $args): array {
    $env = getenv(); $env['WEBSPINE_SITE_ROOT'] = $root;
    unset($env['WEBSPINE_CORE_ROOT'], $env['WEBSPINE_UPDATE_PROBE']);
    $p = proc_open([PHP_BINARY, '-c', php_ini_loaded_file() ?: '', $root . '/core/bin/console.php', ...$args], [0 => ['pipe','r'], 1 => ['pipe','w'], 2 => ['pipe','w']], $pipes, $root, $env);
    fclose($pipes[0]); $out = stream_get_contents($pipes[1]); $err = stream_get_contents($pipes[2]); fclose($pipes[1]); fclose($pipes[2]);
    return ['code' => proc_close($p), 'out' => $out, 'err' => $err];
}
function mutateZip(string $original, string $destination, callable $mutate): string {
    copy($original, $destination); $z = new ZipArchive(); $z->open($destination); $mutate($z); $z->close(); return $destination;
}
$source = dirname(__DIR__, 2);
$base = $source . '/storage/test-' . bin2hex(random_bytes(6));
$site = $base . '/site'; $candidate = $base . '/candidate';
mkdir($base, 0700, true);
try {
    foreach (['core','public','site','config'] as $directory) copyTree($source . '/' . $directory, $site . '/' . $directory);
    foreach (['README.md','LICENSE','AGENTS.md','.gitignore'] as $file) copy($source . '/' . $file, $site . '/' . $file);
    mkdir($site . '/storage'); mkdir($site . '/.dist'); touch($site . '/storage/.gitkeep'); touch($site . '/.dist/.gitkeep');
    $app = new App($site);
    check('Registration does not create a database', fn() => !is_file($site . '/storage/site.sqlite'));
    $schemaOnly = new \Webspine\Providers\SQLiteStorage($base . '/schema-only.sqlite');
    $schemaOnly->install();
    check('Provider schema installation contains no theme selection or website copy', fn() => $schemaOnly->get('theme') === null && $schemaOnly->find('hello') === null);
    unset($schemaOnly);
    check('An uninstalled provider reports unhealthy without creating data', fn() => !$app->health()['ok'] && !is_file($site . '/storage/site.sqlite'));
    $storage = $app->services->get(Storage::class); $app->install();
    check('Explicit installation records supported schema', fn() => $app->health()['ok'] && $storage->health()['schema'] === [1]);
    $settings = $app->services->get(Settings::class); $pages = $app->services->get(Pages::class);
    $settings->set('custom', 'preserved'); $pages->put('custom-page', '<script>alert(1)</script>', '<img src=x onerror=alert(1)>'); $app->install();
    check('Installation is idempotent and preserves custom content', fn() => $settings->get('custom') === 'preserved' && $pages->find('custom-page')['body'] === '<img src=x onerror=alert(1)>');
    check('Persistent pages escape title and body', function () use ($app) { $r = $app->handle('GET','/pages/custom-page'); return $r->status === 200 && str_contains($r->body, '&lt;script&gt;') && !str_contains($r->body, '<script>alert'); });
    check('Parameterized queries do not interpolate injected slugs', fn() => $pages->find("' OR 1=1 --") === null);
    check('Page writes validate identity and title', fn() => expectError(fn() => $pages->put('../bad', '', '')));
    check('Home uses shared layout and correct brand', fn() => str_contains($app->handle('GET','/')->body, 'web<strong>spine</strong>'));
    check('Documentation route is live', fn() => str_contains($app->handle('GET','/docs')->body, 'docs-search'));
    check('Feature route registers through action hook', fn() => str_contains($app->handle('GET','/field-notes')->body, 'app.ready'));
    check('Unknown paths return 404', fn() => $app->handle('GET','/not-a-route')->status === 404);
    check('Mutation requests return 405', fn() => $app->handle('POST','/')->status === 405 && $app->handle('POST','/assets/theme/studio/app.js')->status === 405);
    check('Public health discloses only generic status', fn() => $app->handle('GET','/health')->body === '{"status":"ok"}');
    check('Theme CSS is served with proper type', fn() => $app->handle('GET','/assets/theme/studio/style.css')->headers['Content-Type'] === 'text/css');
    check('Asset traversal and PHP serving are rejected', fn() => $app->handle('GET','/assets/theme/studio/../layout.php')->status === 404 && $app->handle('GET','/assets/theme/studio/layout.php')->status === 404);
    check('Private paths are not served', fn() => $app->handle('GET','/config/example.php')->status === 404 && $app->handle('GET','/storage/site.sqlite')->status === 404 && $app->handle('GET','/site/content/home.php')->status === 404);
    check('Registry rejects implementations of the wrong contract', fn() => expectError(fn() => (new Registry())->set(Storage::class, new stdClass())));
    check('Registry rejects duplicate services', fn() => expectError(fn() => $app->services->set(Storage::class, $storage)));
    check('Missing optional mail service is explicit', fn() => !$app->services->has(Mail::class) && expectError(fn() => $app->services->get(Mail::class)));
    check('Theme IDs reject traversal', fn() => expectError(fn() => $app->theme->validate('../studio')));
    mkdir($site . '/site/plugins/sqlite');
    check('Custom plugins cannot shadow a bundled system provider', fn() => expectError(fn() => new App($site), 'Ambiguous plugin identity'));
    rmdir($site . '/site/plugins/sqlite');
    copyTree($site . '/site/themes/studio', $site . '/site/themes/alternate');
    Files::json($site . '/site/themes/alternate/theme.json', ['id'=>'alternate','version'=>'0.1.0','api'=>1,'dependencies'=>[]]);
    check('CLI switches theme through persistent settings', function () use ($site) { $r = cli($site,['theme','alternate']); return $r['code'] === 0 && (new App($site))->theme->active() === 'alternate'; });
    cli($site, ['theme','studio']);
    check('Failed theme selection preserves the previous theme', fn() => cli($site,['theme','missing'])['code'] !== 0 && (new App($site))->theme->active() === 'studio');
    // A different website can choose completely different template names.
    $savedSitePages = file_get_contents($site . '/site/pages.php');
    $savedSiteRoutes = file_get_contents($site . '/site/routes.php');
    Files::json($site . '/site/themes/minimal/theme.json', ['id'=>'minimal','version'=>'0.1.0','api'=>1,'dependencies'=>[]]);
    Files::write($site . '/site/themes/minimal/layout.php', '<!doctype html><html lang="en"><title><?= e($title) ?></title><main><?= $content ?></main></html>');
    Files::write($site . '/site/themes/minimal/landing.php', '<h1><?= e($title) ?></h1>');
    Files::write($site . '/site/pages.php', "<?php return ['home'=>['template'=>'landing','title'=>'Independent presentation'], 'not-found'=>['template'=>'landing','title'=>'Missing','status'=>404]];");
    Files::write($site . '/site/routes.php', "<?php return static function (\\Webspine\\App \$app): void { \$app->router->get('/', fn()=>\$app->site->render('home')); };");
    $settings->set('theme','minimal');
    check('A site can use another theme without starter home or docs templates', fn() => (new App($site))->health()['ok'] && str_contains((new App($site))->handle('GET','/')->body,'Independent presentation'));
    Files::write($site . '/site/pages.php', $savedSitePages); Files::write($site . '/site/routes.php', $savedSiteRoutes); $settings->set('theme','studio');
    copyTree($source . '/core/tests/fixtures/memory', $site . '/site/plugins/memory');
    $memory = new App($site, ['providers'=>['storage'=>'memory','mail'=>null], 'plugins'=>['field-notes']]);
    $memory->install(); $memory->services->get(Pages::class)->put('memory','From memory','No SQL involved.');
    check('Substitute provider supplies settings and pages without SQLite', fn() => $memory->health()['storage']['provider'] === 'memory' && str_contains($memory->handle('GET','/pages/memory')->body,'No SQL involved.'));
    Files::json($site . '/site/plugins/memory/plugin.json', ['id'=>'memory','version'=>'0.1.0','api'=>99,'provider'=>'storage','dependencies'=>[]]);
    check('Incompatible plugin APIs are rejected', fn() => expectError(fn() => new App($site,['providers'=>['storage'=>'memory'], 'plugins'=>[]])));
    copyTree($source . '/core/tests/fixtures/memory', $site . '/site/plugins/memory');
    check('Feature dependencies cannot silently choose providers', fn() => expectError(fn() => $app->plugins->load('smtp'), 'explicitly selected'));
    $smtp = new App($site, ['providers'=>['storage'=>'sqlite','mail'=>'smtp'],'plugins'=>[], 'sqlite'=>['path'=>'storage/site.sqlite'], 'smtp'=>['host'=>'localhost','port'=>587,'encryption'=>'tls','username'=>'','password'=>'','from'=>'hello@example.com','from_name'=>'webspine']]);
    check('Bundled SMTP implements the mail contract', fn() => $smtp->services->get(Mail::class) instanceof Mail);
    check('SMTP validates recipients before connecting', fn() => expectError(fn() => $smtp->services->get(Mail::class)->send('bad-address','hello','body')));
    check('Bundled dependencies match recorded checksums', function () use ($source) {
        foreach (['core/vendor/dependencies.json','site/themes/studio/assets/fonts/dependency.json'] as $file) {
            $m = json_decode(file_get_contents($source . '/' . $file), true, 32, JSON_THROW_ON_ERROR);
            foreach ($m['files'] as $path=>$hash) if (hash_file('sha256',$source . '/' . $path) !== $hash) return false;
        } return true;
    });
    $full = Release::package($site,true);
    check('Generated release artifacts are absent from the bootstrap inventory', function () use ($full) {
        $z = new ZipArchive(); $z->open($full); $ok = true;
        for ($i = 0; $i < $z->numFiles; $i++) if (str_starts_with($z->getNameIndex($i), '.dist/')) $ok = false;
        $z->close(); return $ok;
    });
    check('Bootstrap includes site files but excludes private config and data', function () use ($full) { $z=new ZipArchive(); $z->open($full); $ok=$z->locateName('config/local.php')===false && $z->locateName('storage/site.sqlite')===false && $z->locateName('site/content/home.php')!==false && $z->locateName('site/themes/studio/home.php')!==false && $z->locateName('core/plugins/sqlite/plugin.php')!==false; $z->close(); return $ok; });
    check('Download route serves a real ZIP', fn() => str_starts_with($app->handle('GET','/download')->body,'PK') && $app->handle('GET','/download')->headers['Content-Type']==='application/zip');
    copyTree($site, $candidate);
    Files::write($candidate . '/core/version.php', "<?php\nreturn ['version' => '0.1.1', 'api' => 1, 'php' => '8.3.0'];\n");
    $release = Release::package($candidate);
    check('Core release includes bundled providers, tools, tests, and guides', function () use ($release) {
        $z = new ZipArchive(); $z->open($release); $ok = true;
        foreach (['core/plugins/sqlite/plugin.php','core/plugins/smtp/plugin.php','core/bin/console.php','core/tests/run.php','core/docs/site.md'] as $file) if ($z->locateName($file) === false) $ok = false;
        $z->close(); return $ok;
    });
    $providerFile = file_get_contents($candidate . '/core/plugins/sqlite/plugin.php');
    Files::write($candidate . '/core/plugins/sqlite/plugin.php', "<?php throw new RuntimeException('Candidate provider rejected');");
    $badProviderRelease = Release::package($candidate);
    check('Candidate health probes execute staged system providers', fn() => expectError(fn() => (new Updater($site))->apply($badProviderRelease), 'Candidate provider rejected') && (require $site . '/core/version.php')['version'] === '0.1.0');
    Files::write($candidate . '/core/plugins/sqlite/plugin.php', $providerFile);
    $release = Release::package($candidate);
    check('Core release excludes every site-owned directory', function () use ($release) {
        $zip = new ZipArchive(); $zip->open($release); $ok = true;
        for ($i = 0; $i < $zip->numFiles; $i++) {
            if (preg_match('~^(site|themes|plugins|config|storage|\.dist)/~', $zip->getNameIndex($i))) $ok = false;
        }
        $zip->close(); return $ok;
    });
    // Customize the installed site after building the candidate release.
    // Candidate health and activation must keep this site, not its starter copy.
    $customPages = require $site . '/site/pages.php';
    $customPages['home']['title'] = 'My independent website';
    Files::write($site . '/site/pages.php', '<?php return ' . var_export($customPages, true) . ';');
    $customCopy = require $site . '/site/content/home.php';
    $customCopy['headline_line_1'] = '<script>custom content</script>';
    Files::write($site . '/site/content/home.php', '<?php return ' . var_export($customCopy, true) . ';');
    $customRoutes = file_get_contents($site . '/site/routes.php');
    $customRoutes = str_replace("\n};", "\n    \$app->router->get('/about-us', fn()=>\$app->site->render('home'));\n};", $customRoutes);
    Files::write($site . '/site/routes.php', $customRoutes);
    check('Content is editable and escaped independently of the theme', function () use ($site) {
        $body = (new App($site))->handle('GET', '/')->body;
        return str_contains($body, '&lt;script&gt;custom content&lt;/script&gt;') && str_contains($body, 'My independent website');
    });
    Files::write($site . '/core/stale.php', '<?php // stale managed file');
    $dbHash = hash_file('sha256',$site . '/storage/site.sqlite');
    $siteOwned = [];
    foreach (['AGENTS.md','site/themes/studio/home.php','site/plugins/field-notes/plugin.php','config/example.php','site/meta.php','site/pages.php','site/routes.php','site/install.php','site/content/home.php','site/content/docs.php','site/content/layout.php'] as $p) $siteOwned[$p]=hash_file('sha256',$site.'/'.$p);
    Files::write($site . '/config/local.php', file_get_contents($site . '/config/example.php')); $siteOwned['config/local.php']=hash_file('sha256',$site.'/config/local.php');
    $updater = new Updater($site);
    check('Bootstrap ZIPs are rejected as core updates', fn() => expectError(fn() => $updater->apply($full)));
    $bad = mutateZip($release,$base.'/corrupt.zip',fn(ZipArchive $z)=>$z->addFromString('core/bootstrap.php','tampered'));
    check('Corrupt release hashes are rejected before activation', fn() => expectError(fn() => $updater->apply($bad),'hash mismatch'));
    $bad = mutateZip($release,$base.'/private.zip',fn(ZipArchive $z)=>$z->addFromString('config/local.php','evil'));
    check('Unlisted and private archive files are rejected', fn() => expectError(fn() => $updater->apply($bad),'unsafe archive'));
    $bad = mutateZip($release,$base.'/site-content.zip',fn(ZipArchive $z)=>$z->addFromString('site/content/home.php','evil'));
    check('A core archive cannot overwrite site content', fn() => expectError(fn() => $updater->apply($bad),'unsafe archive'));
    $bad = mutateZip($release,$base.'/traversal.zip',fn(ZipArchive $z)=>$z->addFromString('../escape.php','evil'));
    check('Archive traversal is rejected', fn() => expectError(fn() => $updater->apply($bad),'unsafe archive'));
    $bad = mutateZip($release,$base.'/missing.zip',fn(ZipArchive $z)=>$z->deleteName('core/src/Router.php'));
    check('Missing inventory files are rejected', fn() => expectError(fn() => $updater->apply($bad)));
    $bad = mutateZip($release,$base.'/case.zip',fn(ZipArchive $z)=>$z->addFromString('CORE/bootstrap.php','evil'));
    check('Case-colliding archive entries are rejected', fn() => expectError(fn() => $updater->apply($bad),'Duplicate'));
    $bad = mutateZip($release,$base.'/api.zip',function(ZipArchive $z){ $m=json_decode($z->getFromName('release.json'),true);$m['api']=2;$z->addFromString('release.json',json_encode($m));});
    check('Incompatible core API is rejected', fn() => expectError(fn() => $updater->apply($bad),'Incompatible'));
    $bad = mutateZip($release,$base.'/linked.zip',function(ZipArchive $z){ $z->setExternalAttributesName('core/src/Router.php',ZipArchive::OPSYS_UNIX,0120777<<16);});
    check('Archive symlinks are rejected', fn() => expectError(fn() => $updater->apply($bad),'links'));
    check('CLI activates a newer core and removes stale framework files', function()use($release,$site){ $r=cli($site,['update',$release]);if($r['code']!==0)throw new RuntimeException($r['err']);return json_decode($r['out'],true)['to']==='0.1.1' && !is_file($site.'/core/stale.php') && (require $site.'/core/version.php')['version']==='0.1.1';});
    check('Update preserves site content, routes, identity, config, themes, plugins, and database', function()use($siteOwned,$site,$dbHash){foreach($siteOwned as $path=>$hash)if(hash_file('sha256',$site.'/'.$path)!==$hash)return false;return hash_file('sha256',$site.'/storage/site.sqlite')===$dbHash;});
    check('Custom site routes and title still work after core activation', fn() => (new App($site))->handle('GET','/about-us')->status === 200 && str_contains((new App($site))->handle('GET','/about-us')->body,'My independent website'));
    check('Updates reject the installed version and downgrade', fn()=>expectError(fn()=>$updater->apply($release),'newer'));
    check('Rollback restores deleted framework files and previous version', function()use($updater,$site){$updater->rollback();return is_file($site.'/core/stale.php')&&(require $site.'/core/version.php')['version']==='0.1.0';});
    Files::write($candidate.'/core/version.php',"<?php\nreturn ['version' => '0.1.2', 'api' => 1, 'php' => '8.3.0'];\n");
    $appFile=file_get_contents($candidate.'/core/src/App.php');
    Files::write($candidate.'/core/src/App.php',str_replace('$status = $this->services->get(Storage::class)->health();',"if (is_file(\$this->root . '/core/activation-failure')) throw new \\RuntimeException('Injected activation failure');\n            ".'$status = $this->services->get(Storage::class)->health();',$appFile));
    Files::write($candidate.'/core/activation-failure','Only visible after activation');
    $failureRelease=Release::package($candidate);
    check('Post-activation health failure automatically restores the old core', fn()=>expectError(fn()=>$updater->apply($failureRelease),'previous core restored') && (require $site.'/core/version.php')['version']==='0.1.0' && !is_file($site.'/core/activation-failure') && !is_file($site.'/storage/update-pending.json'));
    Files::write($candidate.'/core/src/App.php',$appFile); unlink($candidate.'/core/activation-failure');
    $good=Release::package($candidate);$updater->apply($good);
    $journal=json_decode(file_get_contents($site.'/storage/update-last.json'),true);
    Files::json($site.'/storage/update-pending.json',$journal);
    check('CLI refuses normal work during interrupted activation', fn()=>cli($site,['health'])['code']!==0);
    check('Recovery restores the verified previous core', function()use($site){$r=cli($site,['recover']);if($r['code']!==0)throw new RuntimeException($r['err'].$r['out']);return (require $site.'/core/version.php')['version']==='0.1.0' && !is_file($site.'/storage/update-pending.json');});
    check('Recovered site has healthy persistent data', fn()=>cli($site,['health'])['code']===0 && (new App($site))->services->get(Settings::class)->get('custom')==='preserved');
    check('Portable release paths reject Windows device aliases', fn()=>!Files::safe('core/con.php') && !Files::safe('core/../config') && !Files::safe('core/file.'));
} catch(Throwable $e) { $failed++; echo 'FAIL test setup: '.$e->getMessage()."\n".$e->getTraceAsString()."\n"; }
finally { unset($app,$storage,$settings,$pages,$memory,$smtp); gc_collect_cycles(); removeFixture($base,$source.'/storage'); }
echo "\n$passed passed, $failed failed.\n";
exit($failed ? 1 : 0);
