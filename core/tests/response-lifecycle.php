<?php
declare(strict_types=1);
require dirname(__DIR__) . '/bootstrap.php';
use Webspine\{App, Files};

$source = dirname(__DIR__, 2); $root = $source . '/storage/response-test-' . bin2hex(random_bytes(6));
$server = null; $passed = 0; $failed = 0;
function responseCheck(string $name, callable $test): void {
    global $passed, $failed;
    try { if ($test() === false) throw new RuntimeException('Assertion returned false.'); $passed++; echo "PASS $name\n"; }
    catch (Throwable $error) { $failed++; echo 'FAIL ' . $name . ': ' . $error->getMessage() . "\n"; }
}
function responseCopy(string $source, string $target): void {
    if (!is_dir($target)) mkdir($target, 0700, true);
    foreach (new DirectoryIterator($source) as $entry) {
        if ($entry->isDot()) continue;
        if ($entry->isLink()) throw new RuntimeException('Fixture sources must not be links.');
        if ($entry->isDir()) responseCopy($entry->getPathname(), $target . '/' . $entry->getFilename());
        else copy($entry->getPathname(), $target . '/' . $entry->getFilename());
    }
}
function responseRemove(string $path, string $base): void {
    if (!str_starts_with(realpath($path) ?: '', realpath($base) . DIRECTORY_SEPARATOR)) throw new RuntimeException('Unsafe fixture cleanup.');
    foreach (new DirectoryIterator($path) as $entry) {
        if ($entry->isDot()) continue;
        if ($entry->isDir() && !$entry->isLink()) responseRemove($entry->getPathname(), $base); else unlink($entry->getPathname());
    }
    rmdir($path);
}
function responseProcess(string $script, string $root): array {
    $process = proc_open([PHP_BINARY, '-c', php_ini_loaded_file() ?: '', '-d', 'display_errors=1', '-d', 'output_buffering=0', '-r', $script],
        [0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']], $pipes, $root);
    fclose($pipes[0]); $out = stream_get_contents($pipes[1]); $err = stream_get_contents($pipes[2]); fclose($pipes[1]); fclose($pipes[2]);
    if (proc_close($process) !== 0) throw new RuntimeException($err . $out);
    return json_decode($out, true, flags: JSON_THROW_ON_ERROR);
}
try {
    mkdir($root, 0700, true); mkdir($root . '/storage'); mkdir($root . '/config');
    foreach (['core','public'] as $directory) responseCopy($source . '/' . $directory, $root . '/' . $directory);
    responseCopy(__DIR__ . '/fixtures/website/site', $root . '/site');
    $config = ['providers'=>['storage'=>'sqlite','mail'=>null], 'plugins'=>[], 'sqlite'=>['path'=>'storage/site.sqlite'], 'response'=>['finish_fastcgi'=>true, 'hook_budget_ms'=>100]];
    Files::write($root . '/config/local.php', '<?php return ' . var_export($config, true) . ';');
    Files::write($root . '/site/routes.php', '<?php return require dirname(__DIR__) . "/core/tests/fixtures/response-routes.php";');
    $app = new App($root, $config); $app->install();
    responseCheck('Handling and health do not fire response.sent', static function () use ($app, $root): bool {
        $response = $app->handle('GET', '/lifecycle-normal?id=handle-only');
        return $response->status === 201 && $app->health()['ok'] && !file_exists($root . '/storage/lifecycle-events.jsonl');
    });
    responseCheck('Response lifecycle configuration rejects invalid flags and budgets before handling', static function () use ($root, $config): bool {
        foreach (['invalid', ['finish_fastcgi'=>1], ['hook_budget_ms'=>0], ['hook_budget_ms'=>1001], ['hook_budget_ms'=>'100']] as $invalid) {
            try { new App($root, array_replace($config, ['response'=>$invalid])); return false; }
            catch (InvalidArgumentException) {}
        }
        return true;
    });
    $bootstrap = 'require ' . var_export($root . '/core/bootstrap.php', true) . ';$app=new \\Webspine\\App(' . var_export($root, true) . ');';
    // Each CLI case starts before output/headers, independent of the test reporter.
    responseCheck('Manual emissions fire once each, snapshot registrations, recover after send errors and restore buffers', static function () use ($bootstrap, $root): bool {
        $script = $bootstrap . <<<'PHP'
$app->hooks=new \Webspine\Hooks();$events=0;$late=0;
$app->hooks->on('response.sent',function($event)use($app,&$events,&$late){$events++;if($events===1)$app->hooks->on('response.sent',function()use(&$late){$late++;});echo 'discard';ob_start();echo 'nested';});
$request=new \Webspine\Request('GET','/manual');$response=new \Webspine\Response('ok',201);
ob_start();$before=ob_get_level();$app->send($response,$request);$app->send($response,$request);$level=ob_get_level();$body=ob_get_clean();
$bad=false;try{$app->send(new \Webspine\Response('bad',200,['X-Bad'=>new stdClass()]),$request);}catch(Throwable){$bad=true;}
echo json_encode(compact('events','late','body','before','level','bad'));
PHP;
        $result = responseProcess($script, $root);
        return $result['events'] === 2 && $result['late'] === 1 && $result['body'] === 'okok' && $result['before'] === $result['level'] && $result['bad'];
    });
    responseCheck('FastCGI completion stays disabled by default even when the function exists', static function () use ($bootstrap, $root): bool {
        $script = 'function fastcgi_finish_request(){throw new RuntimeException("Must not finish by default");}' . $bootstrap . <<<'PHP'
$app=new \Webspine\App($app->root,array_replace($app->config,['response'=>[]]));$app->hooks=new \Webspine\Hooks();$events=[];
$app->hooks->on('response.sent',function($event)use(&$events){$events[]=$event->fastcgiFinished;});
ob_start();$app->send(new \Webspine\Response('ok'),new \Webspine\Request('GET','/manual'));$body=ob_get_clean();echo json_encode(compact('events','body'));
PHP;
        $result = responseProcess($script, $root);
        return $result === ['events'=>[false], 'body'=>'ok'];
    });
    foreach (['true','false','throw new RuntimeException("finish failure")'] as $finish) {
        responseCheck('Optional FastCGI result ' . $finish . ' closes sessions and preserves one event', static function () use ($bootstrap, $root, $finish): bool {
            $implementation = str_starts_with($finish, 'throw') ? $finish . ';' : 'return ' . $finish . ';';
            $script = 'function fastcgi_finish_request(){if(session_status()===PHP_SESSION_ACTIVE)throw new RuntimeException("Session still locked");' . $implementation . '}' . $bootstrap . <<<'PHP'
$beforeAbort=(bool)ignore_user_abort();$duringAbort=[];
$app->hooks=new \Webspine\Hooks();$events=[];$app->hooks->on('response.sent',function($event)use(&$events,&$duringAbort){$events[]=$event->fastcgiFinished;$duringAbort[]=(bool)ignore_user_abort();});
session_start(['save_path'=>$app->root.'/storage','use_cookies'=>0,'cache_limiter'=>'']);
ob_start();$app->send(new \Webspine\Response('ok'),new \Webspine\Request('HEAD','/manual'));$body=ob_get_clean();echo json_encode(['events'=>$events,'body'=>$body,'active'=>session_status()===PHP_SESSION_ACTIVE,'beforeAbort'=>$beforeAbort,'afterAbort'=>(bool)ignore_user_abort(),'duringAbort'=>$duringAbort]);
PHP;
            $result = responseProcess($script, $root);
            return $result['events'] === [$finish === 'true'] && $result['body'] === '' && !$result['active']
                && $result['beforeAbort'] === $result['afterAbort'] && $result['duringAbort'] === [$finish === 'true' || $result['beforeAbort']];
        });
    }
    $socket = stream_socket_server('tcp://127.0.0.1:0', $errno, $error);
    if (!$socket) throw new RuntimeException('Cannot reserve local fixture port: ' . $error);
    $address = stream_socket_get_name($socket, false); fclose($socket);
    $log = $root . '/storage/server.log';
    $server = proc_open([PHP_BINARY, '-c', php_ini_loaded_file() ?: '', '-d', 'display_errors=1', '-d', 'log_errors=1', '-d', 'output_buffering=4096', '-S', $address, '-t', $root . '/public', $root . '/public/router.php'],
        [0=>['pipe','r'],1=>['file',$log,'a'],2=>['file',$log,'a']], $pipes, $root);
    fclose($pipes[0]); $ready = false;
    for ($i = 0; $i < 50; $i++) { if ($probe = @stream_socket_client('tcp://' . $address, $errno, $error, .1)) { fclose($probe); $ready = true; break; } usleep(100000); }
    if (!$ready) throw new RuntimeException('Lifecycle fixture server did not start.');
    $request = static function (string $path, string $method) use ($address): array {
        $context = stream_context_create(['http'=>['method'=>$method,'ignore_errors'=>true,'follow_location'=>0,'timeout'=>3]]);
        $body = @file_get_contents('http://' . $address . $path, false, $context); $raw = $http_response_header ?? [];
        preg_match('/^HTTP\/\S+ (\d+)/', $raw[0] ?? '', $match); $headers = [];
        foreach (array_slice($raw, 1) as $header) if (str_contains($header, ':')) { [$key,$value] = explode(':', $header, 2); $headers[strtolower($key)] = trim($value); }
        return ['status'=>(int)($match[1] ?? 0),'body'=>$body === false ? '' : $body,'headers'=>$headers];
    };
    (require __DIR__ . '/response-check.php')($request, 'responseCheck', false);
    responseCheck('Hook exceptions and warnings log private relative locations without entering the request fallback', static function () use ($log): bool {
        $text = file_get_contents($log);
        return str_contains($text, 'response.sent listener 1 failed: PRIVATE_HOOK_EXCEPTION')
            && str_contains($text, 'response.sent listener 1 failed: PRIVATE_HOOK_WARNING')
            && str_contains($text, 'core/tests/fixtures/response-routes.php, line') && str_contains($text, 'budget exhausted')
            && !str_contains($text, 'request failed: PRIVATE_HOOK_EXCEPTION');
    });
} catch (Throwable $error) { $failed++; echo 'FAIL lifecycle fixture: ' . $error->getMessage() . "\n"; }
finally {
    if (is_resource($server)) { proc_terminate($server); proc_close($server); }
    unset($app); gc_collect_cycles();
    if (is_dir($root)) responseRemove($root, $source . '/storage');
}
echo "$passed passed, $failed failed (response lifecycle).\n";
exit($failed ? 1 : 0);
