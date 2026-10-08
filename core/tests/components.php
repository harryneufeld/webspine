<?php
declare(strict_types=1);
use Webspine\{App, Files, Registry};
use Webspine\Contracts\{Settings, Storage, Pages};

return static function (App $app, string $root): void {
    $directory = $root . '/site/themes/test-theme';
    $manifest = file_get_contents($directory . '/theme.json');
    $originalServices = $app->services;
    $counting = new class($originalServices->get(Settings::class)) implements Settings {
        public int $reads = 0;
        public function __construct(private Settings $settings) {}
        public function get(string $key, ?string $default = null): ?string {
            if ($key === 'theme') $this->reads++;
            return $this->settings->get($key, $default);
        }
        public function set(string $key, string $value): void { $this->settings->set($key, $value); }
    };
    $app->services = new Registry();
    $app->services->set(Settings::class, $counting);
    foreach ([Storage::class, Pages::class] as $contract) $app->services->set($contract, $originalServices->get($contract));
    $alternate = $root . '/site/themes/component-alternate';
    $files = ['components/test-button.php','components/test-icon.php','components/test-failure.php','components/test-recursion.php','components/test-buffer.php','assets/test-icon.svg'];
    try {
        Files::write($directory . '/components/test-button.php', <<<'PHP'
<?php
$parentOnly = 'private';
echo '<button>' . e($props['label']) . $ui->component('test-icon', ['label'=>$props['icon'], 'ui'=>'prop']) . '</button>';
PHP);
        Files::write($directory . '/components/test-icon.php', <<<'PHP'
<?php
if (isset($app) || isset($site) || isset($parentOnly) || isset($title) || isset($this) || $props['ui'] !== 'prop') throw new RuntimeException('Component scope leaked');
echo '<img data-theme="' . e($ui->id()) . '" src="' . e($ui->assetUrl('test-icon.svg')) . '" alt="' . e($props['label']) . '">';
PHP);
        Files::write($directory . '/assets/test-icon.svg', '<svg xmlns="http://www.w3.org/2000/svg"></svg>');
        $app->theme->refresh();
        check('Nested components receive independent escaped props, theme identity and asset URLs', static function () use ($app): bool {
            $first = $app->theme->component('test-button', ['label'=>'<First>', 'icon'=>'"icon']);
            $second = $app->theme->component('test-button', ['label'=>'Second', 'icon'=>'Other']);
            return str_contains($first, '<button>&lt;First&gt;<img data-theme="test-theme"') && str_contains($first, 'alt="&quot;icon"')
                && preg_match('~/assets/theme/test-theme/test-icon.svg\?v=[a-f0-9]{12}~', $first) === 1
                && str_contains($second, '<button>Second') && str_contains($second, 'alt="Other"') && !str_contains($second, '&lt;First&gt;');
        });
        check('150 nested component renders resolve the theme only once', static function () use ($app, $counting, $directory, $manifest): bool {
            $app->theme->refresh(); $counting->reads = 0;
            $started = hrtime(true);
            for ($i = 0; $i < 150; $i++) $app->theme->component('test-button', ['label'=>'Button', 'icon'=>'Icon']);
            $elapsed = (hrtime(true) - $started) / 1000000;
            // A broken manifest becomes visible after refresh, not midway through cached rendering.
            Files::write($directory . '/theme.json', '{}');
            try {
                $same = $app->theme->active() === 'test-theme';
                $app->theme->refresh();
                $failed = expectError(fn() => $app->theme->active(), 'Incompatible theme');
            } finally { Files::write($directory . '/theme.json', $manifest); $app->theme->refresh(); }
            echo 'INFO 150 nested renders: ' . round($elapsed, 2) . ' ms; one Settings read before refresh' . "\n";
            return $same && $failed && $counting->reads === 2;
        });
        check('Asset URLs match served content, cache per lifecycle and change after refresh', static function () use ($app, $directory): bool {
            $app->theme->refresh();
            $before = $app->theme->assetUrl('test-icon.svg');
            $response = $app->handle('GET', parse_url($before, PHP_URL_PATH));
            if ($response->status !== 200 || !str_ends_with($before, substr(hash('sha256', $response->body), 0, 12))) return false;
            $before = $app->theme->assetUrl('test-icon.svg');
            Files::write($directory . '/assets/test-icon.svg', '<svg xmlns="http://www.w3.org/2000/svg"><title>Changed</title></svg>');
            if ($app->theme->assetUrl('test-icon.svg') !== $before) return false;
            $app->theme->refresh();
            return $app->theme->assetUrl('test-icon.svg') !== $before;
        });
        check('Asset URL helper rejects missing, executable and hostile paths', static function () use ($app): bool {
            foreach (['missing.svg','../layout.php','/style.css','..//style.css','%2e%2e/style.css','style.css?x=1','style.css#x','style.css.php','style.css' . "\0"] as $path) {
                if (!expectError(fn() => $app->theme->assetUrl($path), 'theme asset')) return false;
            }
            return true;
        });
        $link = $directory . '/assets/test-link.svg';
        if (@symlink($directory . '/layout.php', $link)) {
            try {
                check('Asset helper and delivery reject links escaping the assets directory', fn() => expectError(fn() => $app->theme->assetUrl('test-link.svg'), 'Invalid theme asset path') && $app->handle('GET','/assets/theme/test-theme/test-link.svg')->status === 404);
            } finally { unlink($link); }
        } else echo "INFO Symlink containment check unavailable on this platform; runs where symlinks are supported\n";
        Files::write($directory . '/components/test-buffer.php', '<?php echo "outer"; ob_start(); echo "inner";');
        check('Successful components collect nested output buffers without leaking them', static function () use ($app): bool {
            $level = ob_get_level();
            return $app->theme->component('test-buffer') === 'outerinner' && ob_get_level() === $level;
        });
        Files::write($directory . '/components/test-failure.php', '<?php echo "discard"; ob_start(); echo "nested"; $ui->component("missing");');
        check('Nested component failures restore caller buffers and later renders work', static function () use ($app): bool {
            $level = ob_get_level(); ob_start(); echo 'caller';
            try {
                $failed = expectError(fn() => $app->theme->component('test-failure'), 'Missing theme component');
                return $failed && ob_get_level() === $level + 1 && ob_get_contents() === 'caller' && $app->theme->component('wordmark', ['href'=>'/','prefix'=>'web','bold'=>'spine','label'=>'Home']) !== '';
            } finally { while (ob_get_level() > $level) ob_end_clean(); }
        });
        Files::write($directory . '/components/test-recursion.php', '<?php echo $ui->component("test-recursion");');
        check('Recursive components fail within a bound and leave the renderer reusable', static function () use ($app): bool {
            $level = ob_get_level();
            return expectError(fn() => $app->theme->component('test-recursion'), 'nesting limit') && ob_get_level() === $level
                && $app->theme->component('test-button', ['label'=>'After recursion','icon'=>'Icon']) !== '';
        });
        copyTree($directory, $alternate);
        Files::json($alternate . '/theme.json', ['id'=>'component-alternate','version'=>'0.1.0','api'=>1,'dependencies'=>[]]);
        check('Health refreshes settings changes and failure recovery; Apps have independent caches', static function () use ($app, $counting, $root, $alternate): bool {
            $app->theme->refresh(); $counting->set('theme','test-theme');
            if ($app->theme->active() !== 'test-theme') return false;
            $counting->set('theme','component-alternate');
            if ($app->theme->active() !== 'test-theme') return false;
            if ((new App($root))->theme->active() !== 'component-alternate') return false;
            if (!$app->health()['ok'] || $app->theme->active() !== 'component-alternate' || !str_starts_with($app->theme->assetUrl('style.css'), '/assets/theme/component-alternate/')) return false;
            $alternateManifest = file_get_contents($alternate . '/theme.json');
            try {
                Files::write($alternate . '/theme.json', '{}');
                if ($app->health()['ok']) return false;
            } finally { Files::write($alternate . '/theme.json', $alternateManifest); }
            $counting->set('theme','test-theme');
            return $app->health()['ok'] && $app->theme->active() === 'test-theme' && str_starts_with($app->theme->assetUrl('style.css'), '/assets/theme/test-theme/');
        });
        check('Each handled request sees a fresh theme selection', static function () use ($app, $counting): bool {
            $counting->set('theme','component-alternate');
            $alternateAsset = $app->handle('GET','/assets/theme/component-alternate/style.css');
            $counting->set('theme','test-theme');
            return $alternateAsset->status === 200 && $app->handle('GET','/assets/theme/test-theme/style.css')->status === 200;
        });
    } finally {
        $counting->set('theme','test-theme');
        $app->services = $originalServices; $app->theme->refresh();
        Files::write($directory . '/theme.json', $manifest);
        foreach ($files as $file) if (is_file($directory . '/' . $file)) unlink($directory . '/' . $file);
        if (is_dir($alternate)) removeFixture($alternate, $root);
    }
};
