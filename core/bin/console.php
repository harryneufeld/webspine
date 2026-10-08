<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli') { http_response_code(404); exit; }
$root = getenv('WEBSPINE_SITE_ROOT') ?: dirname(__DIR__, 2);
$core = getenv('WEBSPINE_CORE_ROOT') ?: $root . '/core';
$command = $argv[1] ?? 'help';
$lock = null;
$exitCode = 0;
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
    } elseif (in_array($command, ['install', 'entities:install', 'theme', 'health'], true)) {
        $app = new \Webspine\App($root);
        if ($command === 'install') {
            $app->install();
            if (!$app->health()['ok']) throw new RuntimeException('Installed schema but health check failed.');
            echo "Installed. Existing content and settings preserved.\n";
        } elseif ($command === 'entities:install') {
            $app->services->get(\Webspine\Contracts\Entities::class)->install();
            echo "Entity storage and declared entities installed. Existing records preserved.\n";
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
            if (!$health['ok']) $exitCode = 1;
        }
    } else {
        $commands = new \Webspine\ConsoleCommands();
        $args = array_slice($argv, 2);
        if ($command === 'help' && (count($args) > 1)) throw new InvalidArgumentException('Usage: help [command|--core]');
        if ($command === 'help' && ($args[0] ?? null) === '--core') {
            echo $commands->help();
        } else {
            $app = new \Webspine\App($root);
            // Discovery never runs for web requests or the core maintenance branches above.
            $app->hooks->fire('cli.register', $commands);
            if ($command === 'help') echo $commands->help($args[0] ?? null);
            else $exitCode = $commands->run($command, $args);
        }
    }
} catch (Throwable $e) {
    // Native Throwable diagnostics also work when configuration/bootstrap fails.
    $cause = $e->getPrevious() ?? $e;
    $file = str_replace('\\', '/', $cause->getFile());
    $prefix = rtrim(str_replace('\\', '/', $root), '/') . '/';
    if (str_starts_with($file, $prefix)) $file = substr($file, strlen($prefix));
    fwrite(STDERR, 'webspine: ' . $e->getMessage() . ' [' . $file . ', line ' . $cause->getLine() . "]\n");
    $exitCode = 1;
}
finally { if (is_resource($lock)) { flock($lock, LOCK_UN); fclose($lock); } }
exit($exitCode);
