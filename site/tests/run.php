<?php
declare(strict_types=1);
// Site-owned checks: extend these when adding pages, routes or components.
// Use explicit local config and a throwaway SQLite database, never local.php.
require dirname(__DIR__, 2) . '/core/bootstrap.php';
use Webspine\App;
use Webspine\Contracts\Pages;

$root = dirname(__DIR__, 2);
$directory = sys_get_temp_dir() . '/webspine-site-test-' . bin2hex(random_bytes(6));
mkdir($directory, 0700);
$passed = 0; $failed = 0;
function siteCheck(string $name, callable $assert): void {
    global $passed, $failed;
    try {
        if ($assert() === false) throw new RuntimeException('Assertion returned false.');
        $passed++; echo "PASS $name\n";
    } catch (Throwable $error) { $failed++; echo "FAIL $name: {$error->getMessage()}\n"; }
}
try {
    $app = new App($root, ['providers'=>['storage'=>'sqlite','mail'=>null], 'plugins'=>[], 'sqlite'=>['path'=>$directory . '/site.sqlite']]);
    $app->install();
    siteCheck('Fresh starter installs and renders all defined pages', fn() => $app->health()['ok']);
    siteCheck('Starter installation does not seed unwanted database pages', fn() => $app->services->get(Pages::class)->find('hello') === null);
    foreach (['/','/about','/contact'] as $path) {
        siteCheck('Shared layout renders ' . $path, function () use ($app, $path): bool {
            $response = $app->handle('GET', $path);
            return $response->status === 200 && str_contains($response->body, '<main id="main"') && str_contains($response->body, '<footer');
        });
    }
    siteCheck('Unknown page returns the site-owned 404', fn() => $app->handle('GET','/missing')->status === 404);
    siteCheck('Starter stylesheet is available', fn() => ($app->handle('GET','/assets/theme/starter/style.css')->headers['Content-Type'] ?? null) === 'text/css');
    siteCheck('Starter favicon is available', fn() => $app->handle('GET','/assets/theme/starter/favicon.svg')->status === 200);
    siteCheck('Reusable action escapes its text', fn() => str_contains($app->theme->component('action-link',['label'=>'<script>','href'=>'/about']), '&lt;script&gt;'));
    $app->services->get(Pages::class)->put('example','<script>Title</script>','<img src=x onerror=alert(1)>');
    siteCheck('Database page content stays escaped in the starter', function () use ($app): bool {
        $response = $app->handle('GET','/pages/example');
        return $response->status === 200 && str_contains($response->body, '&lt;script&gt;Title') && str_contains($response->body, '&lt;img') && !str_contains($response->body, '<img');
    });
} catch (Throwable $error) { $failed++; echo "FAIL setup: {$error->getMessage()}\n"; }
finally {
    unset($app); gc_collect_cycles();
    foreach (['site.sqlite','site.sqlite-journal','site.sqlite-wal','site.sqlite-shm'] as $file) {
        if (is_file($directory . '/' . $file)) unlink($directory . '/' . $file);
    }
    rmdir($directory);
}
echo "\n$passed passed, $failed failed.\n";
exit($failed ? 1 : 0);
