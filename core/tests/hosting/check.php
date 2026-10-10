<?php
declare(strict_types=1);
if (PHP_SAPI !== 'cli' || !is_file('/work/versions.json')) throw new RuntimeException('Use the isolated hosting test runner.');
$passed = 0; $failed = 0;
function hostingCheck(string $name, callable $test): void {
    global $passed, $failed;
    try {
        if ($test() === false) throw new RuntimeException('Assertion failed');
        echo 'PASS ' . $name . "\n"; $passed++;
    } catch (Throwable $e) { echo 'FAIL ' . $name . ': ' . $e->getMessage() . "\n"; $failed++; }
}
function request(string $host, string $path, string $method = 'GET', array $requestHeaders = []): array {
    $context = stream_context_create(['http'=>['method'=>$method,'header'=>implode("\r\n",$requestHeaders),'ignore_errors'=>true,'follow_location'=>0,'timeout'=>3]]);
    $body = @file_get_contents('http://' . $host . $path, false, $context);
    $raw = $http_response_header ?? [];
    preg_match('/^HTTP\/\S+ (\d+)/', $raw[0] ?? '', $match);
    $headers = [];
    foreach (array_slice($raw, 1) as $header) {
        if (str_contains($header, ':')) { [$key,$value] = explode(':', $header, 2); $headers[strtolower($key)] = trim($value); }
    }
    return ['status'=>(int) ($match[1] ?? 0),'headers'=>$headers,'body'=>$body === false ? '' : $body];
}
function waitForServer(string $host, int $status): void {
    for ($i=0; $i<30; $i++) {
        if (request($host, '/health')['status'] === $status) return;
        usleep(200000);
    }
    throw new RuntimeException('Server did not reach expected health status: ' . $host);
}
$mode = $argv[1] ?? '';
$versions = json_decode(file_get_contents('/work/versions.json'), true, 32, JSON_THROW_ON_ERROR);
if (!in_array($mode, ['initial','updated','restored','denied'], true)) throw new RuntimeException('Unknown check phase');
foreach (['apache','nginx','cloudpanel'] as $host) {
    waitForServer($host, $mode === 'denied' ? 503 : 200);
    if ($mode === 'denied') {
        hostingCheck($host . ' rejects unwritable storage without disclosing private data', static function () use ($host): bool {
            $r = request($host, '/hosting-write');
            $head=request($host,'/hosting-write','HEAD');
            return $r['status'] === 503 && !str_contains($r['body'], 'HOSTING_PRIVATE_SENTINEL') && !str_contains($r['body'], '/srv/webspine')
                && str_contains($r['body'],'Bitte versuchen Sie') && ($r['headers']['content-language']??'')==='de-DE'
                && ($r['headers']['cache-control']??'')==='no-store' && $head['status']===503 && $head['body']==='';
        });
        continue;
    }
    hostingCheck($host . ' pages and preserved query string', static function () use ($host): bool {
        return request($host, '/')['status'] === 200 && request($host, '/docs?source=hosting')['status'] === 200;
    });
    hostingCheck($host . ' localized errors preserve status, Allow, security, cache and HEAD through update/rollback',static function()use($host):bool {
        $get=request($host,'/hosting-client-error');$head=request($host,'/hosting-client-error','HEAD');
        $method=request($host,'/hosting-client-error','POST');
        return $get['status']===400&&str_contains($get['body'],'Ungültige Anfrage')&&str_contains($get['body'],'&lt;script&gt; &amp; Text.')
            &&!str_contains($get['body'],'HOSTING_PRIVATE_SENTINEL')&&($get['headers']['content-language']??'')==='de-DE'
            &&($get['headers']['content-type']??'')==='text/html; charset=utf-8'&&($get['headers']['cache-control']??'')==='no-store'
            &&($get['headers']['x-content-type-options']??'')==='nosniff'&&isset($get['headers']['content-security-policy'])
            &&$head['status']===400&&$head['body']===''&&($head['headers']['content-language']??'')==='de-DE'
            &&$method['status']===405&&($method['headers']['allow']??'')==='GET, HEAD'&&str_contains($method['body'],'Methode nicht erlaubt');
    });
    hostingCheck($host . ' canonical page redirects preserve queries, methods and security headers', static function()use($host):bool {
        $get=request($host,'/hosting-canonical/?x=a%20b&x=c+d');$head=request($host,'/hosting-appended?x=1','HEAD');
        $post=request($host,'/hosting-canonical/','POST');
        return $get['status']===308&&($get['headers']['location']??'')==='/hosting-canonical?x=a%20b&x=c+d'
            &&($get['headers']['x-content-type-options']??'')==='nosniff'&&$get['body']===''
            &&$head['status']===308&&($head['headers']['location']??'')==='/hosting-appended/?x=1'&&$head['body']===''
            &&$post['status']===405&&request($host,'/hosting-canonical')['body']==='Canonical page';
    });
    hostingCheck($host . ' decoded query values and original request URI', static function () use ($host): bool {
        $r = request($host, '/hosting-probe?page_id=41&term=a%20b&value=x%26y');
        $data = json_decode($r['body'], true);
        return $r['status'] === 200 && $data['query'] === ['page_id'=>'41','term'=>'a b','value'=>'x&y'] && $data['uri'] === '/hosting-probe?page_id=41&term=a%20b&value=x%26y';
    });
    hostingCheck($host . ' redirect retains query string', static function () use ($host): bool {
        $r = request($host, '/hosting-redirect?page_id=41&term=a%20b');
        return $r['status'] === 302 && $r['headers']['location'] === '/docs?page_id=41&term=a%20b' && request($host, $r['headers']['location'])['status'] === 200;
    });
    hostingCheck($host . ' PHP requirements, runtime permissions, and warmed OPcache', static function () use ($host): bool {
        request($host, '/hosting-probe');
        $data = json_decode(request($host, '/hosting-probe')['body'], true);
        return $data['php'] >= 80300 && $data['extensions'] === [true,true,true] && $data['opcache'] && $data['cached'] && $data['timestamp_checks'] === '0' && $data['storage_writable'] && !$data['core_writable'];
    });
    hostingCheck($host . ' SQLite writes and data persist across update/restart', static function () use ($host, $mode): bool {
        if ($mode !== 'initial' && (json_decode(request($host, '/hosting-probe')['body'], true)['saved'] ?? '') !== 'preserved') return false;
        return request($host, '/hosting-write')['status'] === 200 && json_decode(request($host, '/hosting-probe')['body'], true)['saved'] === 'preserved';
    });
    hostingCheck($host . ' expected core version after activation/rollback and worker restart', static function () use ($host, $mode, $versions): bool {
        return json_decode(request($host, '/hosting-probe')['body'], true)['version'] === $versions[$mode === 'updated' ? 'updated' : 'initial'];
    });
    foreach ($versions['assets'] as $ext => $type) {
        hostingCheck($host . ' ' . $ext . ' asset bytes, MIME, and cache header', static function () use ($host,$ext,$type): bool {
            $r = request($host, '/assets/theme/test-theme/probe.' . $ext . '?v=1');
            return $r['status'] === 200 && $r['body'] === 'asset-' . $ext && explode(';', $r['headers']['content-type'])[0] === explode(';', $type)[0]
                && ($ext !== 'txt' || str_contains($r['headers']['content-type'], 'charset=utf-8'))
                && ($r['headers']['x-content-type-options'] ?? '') === 'nosniff' && ($r['headers']['cache-control'] ?? '') === 'public, max-age=3600';
        });
        hostingCheck($host . ' HEAD ' . $ext . ' asset returns MIME without bytes', static function () use ($host, $ext, $type): bool {
            $r = request($host, '/assets/theme/test-theme/probe.' . $ext, 'HEAD');
            return $r['status'] === 200 && $r['body'] === '' && explode(';', $r['headers']['content-type'])[0] === explode(';', $type)[0];
        });
    }
    hostingCheck($host . ' dotted font filename and encoded dot reach PHP routing', static function () use ($host): bool {
        $plain = request($host, '/assets/theme/test-theme/fonts/a.b.woff2?v=1');
        $encoded = request($host, '/assets/theme/test-theme/fonts/a%2eb.woff2');
        return $plain['status'] === 200 && $plain['body'] === 'dotted-font' && $plain['headers']['content-type'] === 'font/woff2'
            && $encoded['status'] === 200 && $encoded['body'] === 'dotted-font';
    });
    hostingCheck($host . ' HEAD assets return headers without body', static function () use ($host): bool {
        $r = request($host, '/assets/theme/test-theme/probe.css', 'HEAD');
        return $r['status'] === 200 && $r['body'] === '' && str_starts_with($r['headers']['content-type'], 'text/css');
    });
    hostingCheck($host . ' asset validators work through real servers for GET and HEAD', static function()use($host):bool {
        $path='/assets/theme/test-theme/probe.css';$first=request($host,$path);$tag=$first['headers']['etag']??'';
        $get=request($host,$path,'GET',['If-None-Match: W/'.$tag]);$head=request($host,$path,'HEAD',['If-None-Match: '.$tag]);
        $miss=request($host,$path,'GET',['If-None-Match: "different"']);
        return $tag==='"'.hash('sha256',$first['body']).'"'&&$get['status']===304&&$head['status']===304
            &&$get['body']===''&&$head['body']===''&&($get['headers']['etag']??'')===$tag
            &&($get['headers']['cache-control']??'')==='public, max-age=3600'&&($get['headers']['x-content-type-options']??'')==='nosniff'
            &&$miss['status']===200&&$miss['body']===$first['body'];
    });
    foreach (['/missing','/assets/theme/test-theme/missing.css','/assets/theme/test-theme/blocked.php','/assets/theme/test-theme/escape.css','/assets/theme/wrong/probe.css',
        '/assets/theme/test-theme/danger.php.css','/assets/theme/test-theme/danger.PHP8.txt','/assets/theme/test-theme/danger.phar.gif',
        '/assets/theme/test-theme/unsupported.html','/assets/theme/test-theme/disguised.css','/assets/theme/test-theme/unreadable.css'] as $path) {
        hostingCheck($host . ' missing/unsafe resource ' . $path, static fn() => request($host, $path)['status'] === 404);
    }
    hostingCheck($host . ' unreadable assets remain generic for GET and HEAD', static function () use ($host): bool {
        $get = request($host, '/assets/theme/test-theme/unreadable.css');
        $head = request($host, '/assets/theme/test-theme/unreadable.css', 'HEAD');
        return $get['status'] === 404 && $get['body'] === 'Not found' && $head['status'] === 404 && $head['body'] === '';
    });
    foreach (['/assets/theme/test-theme/%2e%2e/layout.php','/assets/theme/test-theme/%2e%2e/%2e%2e/%2e%2e/%2e%2e/config/local.php','/assets/theme/test-theme/%252e%252e/layout.php','/assets/theme/test-theme/%2e%2e%5clayout.php','/assets/theme/test-theme/probe.css%00'] as $path) {
        hostingCheck($host . ' traversal rejected ' . $path, static function () use ($host,$path): bool {
            $r = request($host, $path);
            return in_array($r['status'], [400,403,404], true) && !str_contains($r['body'], 'HOSTING_PRIVATE_SENTINEL');
        });
    }
    foreach (['/config/local.php','/storage/site.sqlite','/storage/private.txt','/core/private.txt','/core/bin/console.php','/site/content/private.txt','/.dist/private.txt','/.git/config','/.htaccess','/private.txt','/router.php'] as $path) {
        hostingCheck($host . ' private-file isolation ' . $path, static function () use ($host,$path): bool {
            $r = request($host, $path);
            return in_array($r['status'], [403,404], true) && !str_contains($r['body'], 'HOSTING_PRIVATE_SENTINEL') && !str_contains($r['body'], 'SQLite format');
        });
    }
}
if ($mode !== 'denied') {
    hostingCheck($mode === 'updated' ? 'Core update removes unlisted files from managed public directory' : 'CloudPanel generic static rule serves physical public files', static function () use ($mode): bool {
        $r = request('cloudpanel', '/control.css');
        return $mode === 'updated' ? $r['status'] === 404 : $r['status'] === 200 && $r['body'] === 'static-control';
    });
    hostingCheck('CloudPanel original static regex reproduces missing theme assets', static fn() => request('cloudpanel-broken', '/assets/theme/test-theme/probe.css')['status'] === 404 && request('cloudpanel-broken', '/')['status'] === 200);
}
echo "$passed passed, $failed failed ($mode).\n";
exit($failed ? 1 : 0);
