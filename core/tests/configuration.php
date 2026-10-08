<?php
declare(strict_types=1);
use Webspine\{App, Files};

return static function (App $app, string $root): void {
    $local = $root . '/config/local.php';
    $original = is_file($local) ? file_get_contents($local) : null;
    $example = file_get_contents($root . '/config/example.php');
    $routes = file_get_contents($root . '/site/routes.php');
    $bootstrap = file_get_contents($root . '/core/bootstrap.php');
    $homeFile = $root . '/site/themes/test-theme/home.php';
    $home = file_get_contents($homeFile);
    $log = $root . '/storage/configuration-test.log';
    // Run the real public entry point in a separate process, including pre-App failures.
    $public = static function (string $method = 'GET') use ($root, $log): array {
        if (is_file($log)) unlink($log);
        $script = '$_SERVER["REQUEST_METHOD"]=' . var_export($method, true) . '; $_SERVER["REQUEST_URI"]="/"; ob_start(); require '
            . var_export($root . '/public/index.php', true) . '; $body=ob_get_clean(); echo json_encode(["status"=>http_response_code(),"body"=>$body]);';
        $p = proc_open([PHP_BINARY, '-c', php_ini_loaded_file() ?: '', '-d', 'display_errors=0', '-d', 'log_errors=1', '-d', 'error_log=' . $log, '-r', $script],
            [0=>['pipe','r'], 1=>['pipe','w'], 2=>['pipe','w']], $pipes, $root);
        fclose($pipes[0]); $out = stream_get_contents($pipes[1]); $err = stream_get_contents($pipes[2]);
        fclose($pipes[1]); fclose($pipes[2]); $code = proc_close($p);
        if ($code !== 0) throw new RuntimeException('Public test process failed: ' . $err);
        return json_decode($out, true, flags: JSON_THROW_ON_ERROR) + ['log'=>is_file($log) ? file_get_contents($log) : ''];
    };
    $privateFailure = static function (array $response): bool {
        return $response['status'] === 503 && str_contains($response['body'], 'console.php health')
            && str_contains($response['body'], 'PHP error log') && str_contains($response['body'], 'For initial setup only')
            && !str_contains($response['body'], 'setup required') && !str_contains($response['body'], 'PRIVATE_CONFIG_SENTINEL')
            && !str_contains($response['body'], 'config/local.php') && !str_contains($response['body'], 'syntax error');
    };
    try {
        Files::write($local, "<?php\n// PRIVATE_CONFIG_SENTINEL\n\n\n\nreturn ['broken' => ];\n");
        check('Configuration syntax errors retain their original cause', static function () use ($root): bool {
            try { new App($root); } catch (RuntimeException $e) {
                $cause = $e->getPrevious();
                return str_contains($e->getMessage(), 'Cannot load config/local.php') && $cause instanceof ParseError
                    && $cause->getLine() === 6 && str_ends_with(str_replace('\\', '/', $cause->getFile()), '/config/local.php');
            }
            return false;
        });
        check('CLI config failure exits nonzero with relative file and line', static function () use ($root): bool {
            $result = cli($root, ['health']);
            return $result['code'] !== 0 && $result['out'] === '' && str_contains($result['err'], 'config/local.php, line 6')
                && str_contains($result['err'], 'syntax error') && !str_contains($result['err'], str_replace('\\', '/', $root));
        });
        check('Public config failure is generic 503 with private actionable logs', static function () use ($public, $privateFailure, $root): bool {
            $response = $public();
            return $privateFailure($response) && !str_contains($response['body'], $root)
                && str_contains($response['log'], 'config/local.php, line 6') && str_contains($response['log'], 'syntax error');
        });
        check('Emergency HEAD response has no body and retains diagnostics', static function () use ($public): bool {
            $response = $public('HEAD');
            return $response['status'] === 503 && $response['body'] === '' && str_contains($response['log'], 'config/local.php, line 6');
        });
        check('Explicit App configuration bypasses malformed config files', fn() => (new App($root, $app->config))->health()['ok']);
        foreach (['string'=>'"PRIVATE_CONFIG_SENTINEL"', 'int'=>'42', 'null'=>'null'] as $type=>$value) {
            Files::write($local, '<?php return ' . $value . ';');
            check('Invalid ' . $type . ' config return is identified without its value', static function () use ($root, $type, $public, $privateFailure): bool {
                $result = cli($root, ['health']); $response = $public();
                return $result['code'] !== 0 && str_contains($result['err'], 'config/local.php must return an array; returned ' . $type)
                    && !str_contains($result['err'], 'PRIVATE_CONFIG_SENTINEL') && $privateFailure($response);
            });
        }
        Files::write($local, $example);
        check('Clean local configuration boots and CLI health succeeds', fn() => cli($root, ['health'])['code'] === 0 && $public()['status'] === 200);
        unlink($local);
        check('Missing local configuration uses the example successfully', fn() => cli($root, ['health'])['code'] === 0 && $public()['status'] === 200 && !is_file($local));
        Files::write($root . '/config/example.php', "<?php\nreturn ];");
        check('Malformed example fallback includes its own file and line', static function () use ($root, $public, $privateFailure): bool {
            $result = cli($root, ['health']); $response = $public();
            return $result['code'] !== 0 && str_contains($result['err'], 'config/example.php, line 2')
                && $privateFailure($response) && str_contains($response['log'], 'config/example.php, line 2');
        });
        Files::write($root . '/config/example.php', $example);
        Files::write($root . '/site/routes.php', '<?php throw new RuntimeException("PRIVATE_CONFIG_SENTINEL boot failure");');
        check('Unrelated boot failure retains location only in private diagnostics', static function () use ($root, $public, $privateFailure): bool {
            $result = cli($root, ['health']); $response = $public();
            return $result['code'] !== 0 && str_contains($result['err'], 'site/routes.php, line 1')
                && $privateFailure($response) && str_contains($response['log'], 'site/routes.php, line 1');
        });
        Files::write($root . '/site/routes.php', $routes);
        Files::write($homeFile, '<?php throw new RuntimeException("PRIVATE_CONFIG_SENTINEL render failure");');
        check('Unrelated render failure stays generic and health exits nonzero', static function () use ($root, $public, $privateFailure): bool {
            $response = $public();
            return cli($root, ['health'])['code'] !== 0 && $privateFailure($response)
                && str_contains($response['log'], 'site/themes/test-theme/home.php, line 1');
        });
        Files::write($homeFile, $home);
        Files::write($root . '/core/bootstrap.php', '<?php throw new RuntimeException("PRIVATE_CONFIG_SENTINEL bootstrap failure");');
        check('Diagnostics and public fallback work before bootstrap loads', static function () use ($root, $public, $privateFailure): bool {
            $result = cli($root, ['health']); $response = $public();
            return $result['code'] !== 0 && str_contains($result['err'], 'core/bootstrap.php, line 1')
                && $privateFailure($response) && str_contains($response['log'], 'core/bootstrap.php, line 1');
        });
    } finally {
        if ($original === null) { if (is_file($local)) unlink($local); } else Files::write($local, $original);
        Files::write($root . '/config/example.php', $example);
        Files::write($root . '/site/routes.php', $routes);
        Files::write($homeFile, $home);
        Files::write($root . '/core/bootstrap.php', $bootstrap);
        if (is_file($log)) unlink($log);
    }
};
