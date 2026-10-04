<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
$root = getenv('WEBSPINE_SITE_ROOT') ?: dirname(__DIR__, 2);
$core = getenv('WEBSPINE_CORE_ROOT') ?: $root . '/core';
$command = $argv[1] ?? 'help';
$lock = null;
try {
    if (!in_array($command, ['update', 'rollback', 'recover'], true) && getenv('WEBSPINE_UPDATE_PROBE') !== '1') {
        $lock = fopen($root . '/storage/update.lock', 'c+');
        if (!$lock || !flock($lock, LOCK_SH)) throw new RuntimeException('Cannot lock framework.');
        if (is_file($root . '/storage/update-pending.json')) throw new RuntimeException('Interrupted update: run recover.');
    }
    require $core . '/bootstrap.php';
    if ($command === 'package') {
        echo \Webspine\Release::package($root, in_array('--full', $argv, true)) . "\n";
    } elseif ($command === 'update') {
        if (empty($argv[2])) throw new InvalidArgumentException('Usage: update /path/to/core-release.zip');
        echo json_encode((new \Webspine\Updater($root))->apply($argv[2]), JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR) . "\n";
    } elseif (in_array($command, ['rollback', 'recover'], true)) {
        echo json_encode((new \Webspine\Updater($root))->rollback($command === 'recover'), JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR) . "\n";
    } elseif (in_array($command, ['install', 'theme', 'health'], true)) {
        $app = new \Webspine\App($root);
        if ($command === 'install') {
            $app->install();
            if (!$app->health()['ok']) throw new RuntimeException('Installed schema but health check failed.');
            echo "Installed. Existing content and settings preserved.\n";
        } elseif ($command === 'theme') {
            $id = $argv[2] ?? throw new InvalidArgumentException('Usage: theme <theme-id>');
            $app->theme->validate($id);
            $settings = $app->services->get(\Webspine\Contracts\Settings::class);
            $old = $settings->get('theme');
            if ($old === null) throw new RuntimeException('Install the site before switching themes.');
            $settings->set('theme', $id);
            if (!$app->health()['ok']) { $settings->set('theme', $old); throw new RuntimeException('Theme health failed; selection restored.'); }
            echo "Active theme: $id\n";
        } else {
            $health = $app->health(); echo json_encode($health, JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR) . "\n";
            if (!$health['ok']) exit(1);
        }
    } else {
        if ($command !== 'help') throw new InvalidArgumentException('Unknown command: ' . $command);
        echo "webspine CLI\n\ninstall                 Explicit, idempotent installation\ntheme <id>              Switch active theme\nhealth                  Check services and rendering\npackage [--full]        Create core release or bootstrap ZIP\nupdate <archive>        Apply a trusted local core release\nrollback                Restore the previous core\nrecover                 Restore after interrupted activation\n";
    }
} catch (Throwable $e) { fwrite(STDERR, 'webspine: ' . $e->getMessage() . "\n"); exit(1); }
finally { if (is_resource($lock)) { flock($lock, LOCK_UN); fclose($lock); } }
