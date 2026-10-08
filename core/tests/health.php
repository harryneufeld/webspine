<?php
declare(strict_types=1);
use Webspine\{App, Files};
use Webspine\Contracts\Settings;

return static function (App $app, string $root): void {
    $directory = $root . '/site/themes/test-theme';
    $home = file_get_contents($directory . '/home.php');
    $layout = file_get_contents($directory . '/layout.php');
    $reporting = error_reporting(E_ALL);
    try {
        foreach (['file_get_contents','hash_file'] as $operation) {
            $call = $operation === 'hash_file' ? "hash_file('sha256', __DIR__ . '/assets/missing-health.svg')" : "file_get_contents(__DIR__ . '/assets/missing-health.svg')";
            Files::write($directory . '/home.php', '<?php echo "discard"; ob_start(); ' . $call . ';');
            check($operation . ' warnings fail local/CLI health without leaking template output', static function () use ($app, $root, $directory): bool {
                $level = ob_get_level(); ob_start(); echo 'caller';
                try {
                    $health = $app->health();
                    $cli = cli($root,['health']); $cliHealth = json_decode($cli['out'],true);
                    return !$health['ok'] && str_contains($health['error'],'missing-health.svg') && str_contains(str_replace('\\','/',$health['error']), str_replace('\\','/',$directory . '/home.php'))
                        && $cli['code'] === 1 && is_array($cliHealth) && !$cliHealth['ok'] && $cli['err'] === ''
                        && ob_get_level() === $level + 1 && ob_get_contents() === 'caller';
                } finally { while (ob_get_level() > $level) ob_end_clean(); }
            });
            check('Public health hides ' . $operation . ' diagnostic details', static function () use ($app): bool {
                $response = $app->handle('GET','/health');
                return $response->status === 503 && $response->body === '{"status":"unavailable"}' && !str_contains($response->body,'missing-health');
            });
        }
        Files::write($directory . '/home.php', '<?php echo "discard"; $ui->assetUrl("missing-health.svg");');
        check('Explicit asset URL exceptions still fail health', fn() => !$app->health()['ok'] && $app->handle('GET','/health')->status === 503);
        foreach (['undefined array key'=>'<?php $values=[]; echo $values["missing-health"];', 'user warning'=>'<?php trigger_error("health-warning", E_USER_WARNING);', 'user notice'=>'<?php trigger_error("health-notice", E_USER_NOTICE);'] as $name=>$template) {
            Files::write($directory . '/home.php', $template);
            check('Reported ' . $name . ' fails rendering health', fn() => !$app->health()['ok']);
        }
        Files::write($directory . '/home.php', $home);
        Files::write($directory . '/layout.php', '<?php echo "discard"; ob_start(); trigger_error("layout-health-warning", E_USER_WARNING);');
        check('Layout warnings fail health and clean up nested buffers', static function () use ($app): bool {
            $level = ob_get_level(); $health = $app->health();
            return !$health['ok'] && str_contains($health['error'],'layout-health-warning') && ob_get_level() === $level;
        });
        Files::write($directory . '/layout.php', $layout);
        Files::write($directory . '/home.php', '<?php @file_get_contents(__DIR__ . "/assets/missing-health.svg"); echo "suppressed";');
        check('Suppressed warnings respect error_reporting and do not fail health', fn() => $app->health()['ok']);
        Files::write($directory . '/home.php', '<?php trigger_error("masked-health-warning", E_USER_WARNING);');
        check('Warnings excluded by error_reporting do not fail health', static function () use ($app): bool {
            $saved = error_reporting(E_ALL & ~E_USER_WARNING);
            try { return $app->health()['ok']; } finally { error_reporting($saved); }
        });
        check('Health restores caller handlers after success and failure, including unrelated severities', static function () use ($app, $directory, $home): bool {
            $handled = [];
            $handler = static function (int $severity, string $message) use (&$handled): bool { $handled[] = [$severity,$message]; return true; };
            set_error_handler($handler);
            try {
                Files::write($directory . '/home.php', '<?php trigger_error("legacy-health-deprecation", E_USER_DEPRECATED);');
                if (!$app->health()['ok'] || $handled !== [[E_USER_DEPRECATED,'legacy-health-deprecation']]) return false;
                Files::write($directory . '/home.php', '<?php trigger_error("health-only-warning", E_USER_WARNING);');
                if ($app->health()['ok']) return false;
                $current = set_error_handler($handler); restore_error_handler();
                if ($current !== $handler) return false;
                trigger_error('outside-health-warning', E_USER_WARNING);
                return end($handled) === [E_USER_WARNING,'outside-health-warning'];
            } finally { restore_error_handler(); Files::write($directory . '/home.php', $home); }
        });
        check('Normal requests retain their existing warning-handler behavior', static function () use ($app, $directory, $home): bool {
            $seen = false;
            set_error_handler(static function () use (&$seen): bool { $seen = true; return true; });
            try {
                Files::write($directory . '/home.php', '<?php trigger_error("ordinary-request-warning", E_USER_WARNING); echo "ordinary response";');
                $response = $app->handle('GET','/');
                return $seen && $response->status === 200 && str_contains($response->body,'ordinary response');
            } finally { restore_error_handler(); Files::write($directory . '/home.php', $home); }
        });
        $alternate = $root . '/site/themes/health-warning';
        copyTree($directory, $alternate);
        try {
            Files::json($alternate . '/theme.json', ['id'=>'health-warning','version'=>'0.1.0','api'=>1,'dependencies'=>[]]);
            Files::write($alternate . '/home.php', '<?php trigger_error("theme-selection-warning", E_USER_WARNING);');
            check('CLI restores the selected theme when the candidate emits warnings', static function () use ($root): bool {
                $result = cli($root,['theme','health-warning']);
                return $result['code'] !== 0 && str_contains($result['err'],'selection restored') && (new App($root))->services->get(Settings::class)->get('theme') === 'test-theme';
            });
        } finally { removeFixture($alternate, $root); }
        check('Health recovers after warning templates are repaired', fn() => $app->health()['ok'] && $app->handle('GET','/health')->status === 200);
    } finally {
        Files::write($directory . '/home.php', $home); Files::write($directory . '/layout.php', $layout);
        error_reporting($reporting);
    }
};
