<?php
declare(strict_types=1);
// This script must run inside the isolated Linux test container, as maintenance user.
if (PHP_SAPI !== 'cli' || !is_dir('/source/core') || !is_dir('/work') || !is_dir('/srv/webspine')) {
    throw new RuntimeException('Run through core/tests/hosting/run.sh, not on a website.');
}
require '/source/core/bootstrap.php';
use Webspine\{App, Files, Release, Updater};
function copyHostingTree(string $source, string $target): void {
    if (!is_dir($target)) mkdir($target, 0755, true);
    foreach (new DirectoryIterator($source) as $file) {
        if ($file->isDot()) continue;
        if ($file->isLink()) throw new RuntimeException('Fixture sources cannot contain links.');
        $destination = $target . '/' . $file->getFilename();
        if ($file->isDir()) copyHostingTree($file->getPathname(), $destination);
        else copy($file->getPathname(), $destination);
    }
}
function command(array $args): string {
    $process = proc_open($args, [0=>['pipe','r'], 1=>['pipe','w'], 2=>['pipe','w']], $pipes);
    fclose($pipes[0]);
    $out = stream_get_contents($pipes[1]); $err = stream_get_contents($pipes[2]);
    fclose($pipes[1]); fclose($pipes[2]);
    if (proc_close($process) !== 0) throw new RuntimeException($err . $out);
    return $out;
}
function storageMode(bool $writable): void {
    foreach (new RecursiveIteratorIterator(new RecursiveDirectoryIterator('/srv/webspine/storage', FilesystemIterator::SKIP_DOTS), RecursiveIteratorIterator::SELF_FIRST) as $file) {
        chgrp($file->getPathname(), 33);
        chmod($file->getPathname(), $file->isDir() ? ($writable ? 02770 : 0550) : ($writable ? 0660 : 0440));
    }
    chgrp('/srv/webspine/storage', 33);
    chmod('/srv/webspine/storage', $writable ? 02770 : 0550);
}
function backupHostingFixture(string $label): void {
    // All PHP workers have been stopped by run.sh: consistent SQLite snapshot.
    $archive = '/work/' . $label . '-' . gmdate('YmdHis') . '.tar';
    command(['tar', '-cf', $archive, '-C', '/srv/webspine', '.']);
    $listing = command(['tar', '-tf', $archive]);
    foreach (['core/version.php', 'public/index.php', 'site/meta.php', 'config/local.php', 'storage/site.sqlite', 'README.md', 'AGENTS.md'] as $required) {
        if (!str_contains($listing, './' . $required . "\n")) throw new RuntimeException('Incomplete backup: ' . $required);
    }
    // Read the archive and compare every extracted byte with the quiescent source.
    command(['tar', '-df', $archive, '-C', '/srv/webspine']);
}
function nginxConfig(string $server): string {
    return "events {}\nhttp { include /etc/nginx/mime.types;\n" . $server . "\n}\n";
}
$mode = $argv[1] ?? '';
if ($mode === 'prepare') {
    if (is_file('/srv/webspine/core/version.php')) throw new RuntimeException('Expected a fresh test volume.');
    foreach (['core', 'public'] as $directory) copyHostingTree('/source/' . $directory, '/srv/webspine/' . $directory);
    copyHostingTree('/source/core/tests/fixtures/website/site', '/srv/webspine/site');
    mkdir('/srv/webspine/config'); mkdir('/srv/webspine/storage'); mkdir('/srv/webspine/.dist');
    foreach (['README.md','LICENSE','AGENTS.md','.gitignore','.gitattributes'] as $file) copy('/source/' . $file, '/srv/webspine/' . $file);
    $config = "<?php /* HOSTING_PRIVATE_SENTINEL */ return ['providers'=>['storage'=>'sqlite','mail'=>'capture-mail'], 'plugins'=>['contact-form'], 'sqlite'=>['path'=>'storage/site.sqlite'], 'contact_form'=>['recipient'=>'owner@example.test','secure_cookie'=>false,'max_attempts'=>20]];\n";
    file_put_contents('/srv/webspine/config/example.php', $config);
    file_put_contents('/srv/webspine/config/local.php', $config);
    rename('/srv/webspine/site/routes.php', '/srv/webspine/site/base-routes.php');
    copy(__DIR__ . '/routes.php', '/srv/webspine/site/routes.php');
    Files::json('/srv/webspine/site/errors.json', ['language'=>'de-DE','http'=>[
        '400'=>['title'=>'Ungültige Anfrage','message'=>'Bitte prüfen: <script> & Text.'],
        '405'=>['title'=>'Methode nicht erlaubt','message'=>'Bitte die Seite öffnen.'],
    ],'unavailable'=>['title'=>'Vorübergehend nicht erreichbar','message'=>'Bitte versuchen Sie es später erneut.','operator'=>'']]);
    mkdir('/srv/webspine/site/plugins/contact-form');
    foreach (['plugin.php','plugin.json','FormText.php','ContactForm.php','MailDelivery.php'] as $file) copy('/source/core/docs/examples/contact-form/'.$file,'/srv/webspine/site/plugins/contact-form/'.$file);
    copy('/source/core/docs/examples/contact-form/template.php','/srv/webspine/site/themes/test-theme/contact-form.php');
    copy('/source/core/docs/examples/contact-form/content.php','/srv/webspine/site/content/contact-form.php');
    copy('/source/core/docs/examples/contact-form/contact-form.css','/srv/webspine/site/themes/test-theme/assets/contact-form.css');
    foreach (['core/private.txt','storage/private.txt','site/content/private.txt','.dist/private.txt','private.txt'] as $file) {
        file_put_contents('/srv/webspine/' . $file, 'HOSTING_PRIVATE_SENTINEL');
    }
    $assets = ['css'=>'text/css','js'=>'text/javascript','svg'=>'image/svg+xml','woff2'=>'font/woff2','png'=>'image/png','jpg'=>'image/jpeg','jpeg'=>'image/jpeg','webp'=>'image/webp',
        'ico'=>'image/vnd.microsoft.icon','avif'=>'image/avif','gif'=>'image/gif','pdf'=>'application/pdf','txt'=>'text/plain; charset=utf-8','webmanifest'=>'application/manifest+json'];
    foreach ($assets as $ext => $_) file_put_contents('/srv/webspine/site/themes/test-theme/assets/probe.' . $ext, 'asset-' . $ext);
    mkdir('/srv/webspine/site/themes/test-theme/assets/fonts');
    file_put_contents('/srv/webspine/site/themes/test-theme/assets/fonts/a.b.woff2', 'dotted-font');
    foreach (['danger.php.css','danger.PHP8.txt','danger.phar.gif','unsupported.html','unreadable.css','private.php'] as $file) {
        file_put_contents('/srv/webspine/site/themes/test-theme/assets/' . $file, 'HOSTING_PRIVATE_SENTINEL');
    }
    file_put_contents('/srv/webspine/public/control.css', 'static-control');
    file_put_contents('/srv/webspine/site/themes/test-theme/assets/blocked.php', '<?php echo "HOSTING_PRIVATE_SENTINEL";');
    symlink('/srv/webspine/config/local.php', '/srv/webspine/site/themes/test-theme/assets/escape.css');
    symlink('/srv/webspine/site/themes/test-theme/assets/private.php', '/srv/webspine/site/themes/test-theme/assets/disguised.css');
    (new App('/srv/webspine'))->install();
    (new App('/srv/webspine'))->services->get(\Webspine\Jobs\Queue::class)->install();
    Files::write('/srv/webspine/storage/hosting-test-fixture','Disposable contact/queue fixture');
    // Runtime group can read code, but only storage is writable.
    command(['chown','-R','root:33','/srv/webspine']);
    command(['chmod','-R','u=rwX,g=rX,o=rX','/srv/webspine']);
    chmod('/srv/webspine/config', 0750); chmod('/srv/webspine/config/local.php', 0640);
    chmod('/srv/webspine/site/themes/test-theme/assets/unreadable.css', 0000);
    storageMode(true);
    $initial = (require '/source/core/version.php')['version'];
    $parts = array_map('intval', explode('.', $initial)); $parts[2]++;
    $new = implode('.', $parts);
    Files::json('/work/versions.json', ['initial'=>$initial,'updated'=>$new, 'assets'=>$assets]);
    foreach (['core','public'] as $directory) copyHostingTree('/source/' . $directory, '/work/candidate/' . $directory);
    foreach (['README.md','LICENSE'] as $file) copy('/source/' . $file, '/work/candidate/' . $file);
    $version = require '/source/core/version.php'; $version['version'] = $new;
    file_put_contents('/work/candidate/core/version.php', '<?php return ' . var_export($version, true) . ';');
    Release::package('/work/candidate', false, '/work/update.zip');
    $nginx = file_get_contents('/source/core/docs/hosting/nginx.conf');
    $nginx = str_replace('unix:/run/php/php8.3-fpm.sock', 'fpm:9000', $nginx);
    file_put_contents('/work/nginx.conf', nginxConfig($nginx));
    $backend = file_get_contents('/source/core/docs/hosting/cloudpanel-backend.conf');
    $backend = str_replace(['127.0.0.1:{{php_fpm_port}}','{{php_settings}}'], ['fpm:9000','display_errors=0'], $backend);
    file_put_contents('/work/backend.conf', nginxConfig("server { listen 80; root /srv/webspine/public;\n" . $backend . "\n}"));
    $frontend = file_get_contents('/source/core/docs/hosting/cloudpanel-frontend.conf');
    $frontend = str_replace('{{varnish_proxy_pass}}', 'proxy_pass http://backend:80;', $frontend);
    file_put_contents('/work/cloudpanel.conf', nginxConfig("server { listen 80; root /srv/webspine/public;\n" . $frontend . "\n}"));
    // Negative control: the original static rule must fail to serve theme CSS.
    $broken = str_replace('^(?!/assets/)', '^', $frontend);
    file_put_contents('/work/cloudpanel-broken.conf', nginxConfig("server { listen 80; root /srv/webspine/public;\n" . $broken . "\n}"));
    echo "Prepared isolated fixtures and configs from versioned examples.\n";
} elseif ($mode === 'reset-contact') {
    foreach (['storage/contact-forms/rate.json','storage/captured-mail.jsonl','storage/job-queue/jobs.sqlite','storage/job-queue/jobs.sqlite-wal','storage/job-queue/jobs.sqlite-shm','storage/job-queue/worker.json'] as $file) if (is_file('/srv/webspine/'.$file)) unlink('/srv/webspine/'.$file);
    (new App('/srv/webspine'))->services->get(\Webspine\Jobs\Queue::class)->install();
    storageMode(true);
} elseif ($mode === 'deny-storage' || $mode === 'allow-storage') {
    storageMode($mode === 'allow-storage');
} elseif ($mode === 'update' || $mode === 'rollback') {
    backupHostingFixture($mode);
    $updater = new Updater('/srv/webspine');
    $result = $mode === 'update' ? $updater->apply('/work/update.zip') : $updater->rollback();
    echo json_encode($result, JSON_THROW_ON_ERROR) . "\n";
    storageMode(true);
} else {
    throw new RuntimeException('Unknown fixture phase.');
}
