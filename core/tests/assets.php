<?php
declare(strict_types=1);
use Webspine\{App, Files};

return static function (App $app, string $root): void {
    $directory = $root . '/site/themes/test-theme/assets/asset-check';
    $types = ['css'=>'text/css', 'js'=>'text/javascript', 'svg'=>'image/svg+xml', 'woff2'=>'font/woff2',
        'png'=>'image/png', 'jpg'=>'image/jpeg', 'jpeg'=>'image/jpeg', 'webp'=>'image/webp',
        'ico'=>'image/vnd.microsoft.icon', 'avif'=>'image/avif', 'gif'=>'image/gif',
        'pdf'=>'application/pdf', 'txt'=>'text/plain; charset=utf-8', 'webmanifest'=>'application/manifest+json'];
    try {
        foreach ($types as $extension=>$type) {
            $relative = 'asset-check/nested.v2/file.name.' . $extension;
            $body = 'asset-bytes-' . $extension;
            Files::write($root . '/site/themes/test-theme/assets/' . $relative, $body);
            check('Dotted ' . $extension . ' URL and delivery share bytes, MIME and cache policy', static function () use ($app, $relative, $body, $type): bool {
                $url = $app->theme->assetUrl($relative);
                $response = $app->handle('GET', $url);
                return $url === '/assets/theme/test-theme/' . $relative . '?v=' . substr(hash('sha256', $body), 0, 12)
                    && $response->status === 200 && $response->body === $body && $response->headers['Content-Type'] === $type
                    && $response->headers['Cache-Control'] === 'public, max-age=3600';
            });
        }
        Files::write($directory . '/fonts/a.b.woff2', 'font');
        check('Dotted package font name is supported', fn() => $app->handle('GET', $app->theme->assetUrl('asset-check/fonts/a.b.woff2'))->body === 'font');
        Files::write($directory . '/danger.php.css', 'PRIVATE_ASSET_SENTINEL');
        Files::write($directory . '/danger.PHP8.txt', 'PRIVATE_ASSET_SENTINEL');
        Files::write($directory . '/danger.phar.gif', 'PRIVATE_ASSET_SENTINEL');
        Files::write($directory . '/private.php', '<?php echo "PRIVATE_ASSET_SENTINEL";');
        Files::write($directory . '/unsupported.html', 'PRIVATE_ASSET_SENTINEL');
        $invalid = ['/style.css', '../style.css', 'asset-check/../style.css', 'asset-check//file.css',
            'asset-check/.hidden.css', 'asset-check/file..css', 'asset-check/file.css.', 'asset-check/./file.css',
            'asset-check/file.css?x=1', 'asset-check/file.css#x', 'asset-check/file.css' . "\0", 'asset-check\\file.css',
            'asset-check/danger.php.css', 'asset-check/danger.PHP8.txt', 'asset-check/danger.phar.gif',
            'asset-check/private.php', '%2e%2e/style.css', 'asset-check/file%2ecss', 'asset-check/file:css'];
        check('Invalid asset paths and executable suffix chains have distinct helper diagnostics', static function () use ($app, $invalid): bool {
            foreach ($invalid as $path) if (!expectError(fn() => $app->theme->assetUrl($path), 'Invalid theme asset path:')) return false;
            return true;
        });
        check('Unsupported extensions and missing files have separate helper diagnostics', fn() =>
            expectError(fn() => $app->theme->assetUrl('asset-check/unsupported.html'), 'Unsupported theme asset extension:')
            && expectError(fn() => $app->theme->assetUrl('asset-check/missing.css'), 'Missing theme asset:'));
        check('Public asset failures disclose neither helper diagnostics nor file bytes', static function () use ($app, $invalid): bool {
            foreach ([...$invalid, 'asset-check/unsupported.html', 'asset-check/missing.css'] as $path) {
                // Request rejects control bytes/query fragments before asset resolution.
                if (strpbrk($path, "\0?#") !== false) continue;
                $response = $app->handle('GET', '/assets/theme/test-theme/' . $path);
                if ($response->status !== 404 || $response->body !== 'Not found') return false;
            }
            return true;
        });
        check('Once-decoded safe filenames work and encoded traversal stays blocked', static function () use ($app): bool {
            if ($app->handle('GET', '/assets/theme/test-theme/asset-check/fonts/a%2eb.woff2')->status !== 200) return false;
            foreach (['%2e%2e/style.css', '%252e%252e/style.css', 'asset-check%5cprivate.php', 'asset-check/danger%2ephp.css'] as $path) {
                if ($app->handle('GET', '/assets/theme/test-theme/' . $path)->status !== 404) return false;
            }
            return $app->handle('GET', '/assets/theme/other-theme/asset-check/fonts/a.b.woff2')->status === 404;
        });
        $unreadable = $directory . '/unreadable.css';
        Files::write($unreadable, 'PRIVATE_ASSET_SENTINEL');
        @chmod($unreadable, 0000); clearstatcache(true, $unreadable);
        try {
            if (!is_readable($unreadable)) {
                check('Unreadable allowed assets have private diagnostics and generic 404s', fn() =>
                    expectError(fn() => $app->theme->assetUrl('asset-check/unreadable.css'), 'Cannot read theme asset:')
                    && $app->handle('GET', '/assets/theme/test-theme/asset-check/unreadable.css')->body === 'Not found');
            } else echo "INFO Unreadable-file permissions unavailable for this user/platform; hosting CI tests worker access\n";
        } finally { chmod($unreadable, 0600); }
        foreach (['contained.css'=>$directory . '/nested.v2/file.name.css', 'executable.css'=>$directory . '/private.php',
            'outside.txt'=>$root . '/config/example.php'] as $name=>$target) {
            $link = $directory . '/' . $name;
            if (!@symlink($target, $link)) { echo "INFO Asset link checks unavailable on this platform; run in Linux CI\n"; break; }
            try {
                if ($name === 'contained.css') {
                    check('Contained links to allowed assets remain servable', fn() => $app->handle('GET', $app->theme->assetUrl('asset-check/' . $name))->body === 'asset-bytes-css');
                } else {
                    check('Asset link cannot disguise ' . $name . ' target', fn() =>
                        expectError(fn() => $app->theme->assetUrl('asset-check/' . $name), 'Invalid theme asset path:')
                        && $app->handle('GET', '/assets/theme/test-theme/asset-check/' . $name)->body === 'Not found');
                }
            } finally { unlink($link); }
        }
    } finally {
        $app->theme->refresh();
        if (is_dir($directory)) removeFixture($directory, $root);
    }
};
