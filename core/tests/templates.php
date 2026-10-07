<?php
declare(strict_types=1);
use Webspine\{App, Files};
return static function (App $app, string $root): void {
    $directory = $root . '/site/themes/test-theme';
    $originalLayout = file_get_contents($directory . '/layout.php');
    try {
        Files::write($directory . '/scope.php', <<<'PHP'
<?php
if (!$app instanceof \Webspine\App || !$site instanceof \Webspine\Site || !$ui instanceof \Webspine\ThemeContext || $ui->id() !== 'test-theme' || $theme !== 'test-theme' || basename($directory) !== 'test-theme' || basename($file) !== 'scope.php' || $template !== 'scope' || $status !== 201 || $content !== '') throw new RuntimeException('Renderer context was replaced');
foreach (['content','app','site','theme','ui','directory','file','template','status','data','__file','__context','__level'] as $key) echo '<p>' . e($data[$key]) . '</p>';
?>
<h1><?= e($title) ?></h1>
PHP);
        Files::write($directory . '/layout.php', '<title><?= e($title) ?></title><main><?= $content ?></main><aside><?= e($data["content"]) ?></aside>');
        check('Reserved renderer collisions remain accessible through explicit data', static function () use ($app): bool {
            $data = ['title'=>'Original'];
            foreach (['content','app','site','theme','ui','directory','file','template','status','data','__file','__context','__level'] as $key) $data[$key] = '<' . $key . '>';
            $response = $app->theme->render('scope', $data, 201);
            foreach (array_diff(array_keys($data), ['title']) as $key) if (!str_contains($response->body, '<p>&lt;' . $key . '&gt;</p>')) return false;
            return $response->status === 201 && str_contains($response->body, '<main>') && str_contains($response->body, '<h1>Original</h1>') && str_contains($response->body, '<aside>&lt;content&gt;</aside>');
        });
        Files::write($directory . '/scope.php', <<<'PHP'
<?php
$pageOnly = 'must not leak'; $title = 'Changed'; $body = 'Changed';
$app = null; $site = null; $theme = 'bad'; $ui = null; $directory = '/missing'; $file = '/missing';
$template = 'bad'; $status = 500; $content = 'Changed'; $data['title'] = 'Changed';
echo '<p>Rendered page</p>';
PHP);
        Files::write($directory . '/layout.php', <<<'PHP'
<?php
if (isset($pageOnly) || isset($this) || !$app instanceof \Webspine\App || !$site instanceof \Webspine\Site || !$ui instanceof \Webspine\ThemeContext || $ui->id() !== 'test-theme' || $theme !== 'test-theme' || basename($directory) !== 'test-theme' || basename($file) !== 'layout.php' || $template !== 'scope' || $status !== 201 || $data['title'] !== 'Original') throw new RuntimeException('Page variables leaked');
?>
<title><?= e($title) ?></title><main><?= $content ?></main><aside><?= e($body) ?></aside>
PHP);
        check('Page assignments cannot replace layout context or original page data', static function () use ($app): bool {
            $response = $app->theme->render('scope', ['title'=>'Original','body'=>'Original body'], 201);
            return $response->status === 201 && str_contains($response->body, '<title>Original</title>') && str_contains($response->body, '<main><p>Rendered page</p></main>') && str_contains($response->body, '<aside>Original body</aside>');
        });
        Files::write($directory . '/scope.php', '<?php echo e($title); ob_start(); echo " nested";');
        Files::write($directory . '/layout.php', '<main><?= $content ?></main>');
        check('Nested template buffers are collected without leaking output', static function () use ($app): bool {
            $level = ob_get_level(); $response = $app->theme->render('scope', ['title'=>'<text>']);
            return $response->body === '<main>&lt;text&gt; nested</main>' && ob_get_level() === $level;
        });
        foreach (['page','layout'] as $target) {
            Files::write($directory . '/scope.php', $target === 'page' ? '<?php echo "discard"; ob_start(); throw new RuntimeException("page failure");' : '<p>Page</p>');
            Files::write($directory . '/layout.php', $target === 'layout' ? '<?php echo "discard"; ob_start(); throw new RuntimeException("layout failure");' : '<main><?= $content ?></main>');
            check(ucfirst($target) . ' exceptions restore caller output buffers', static function () use ($app, $target): bool {
                $level = ob_get_level(); ob_start(); echo 'caller';
                try {
                    $failed = expectError(fn() => $app->theme->render('scope', []), $target . ' failure');
                    return $failed && ob_get_level() === $level + 1 && ob_get_contents() === 'caller';
                } finally { while (ob_get_level() > $level) ob_end_clean(); }
            });
        }
    } finally {
        Files::write($directory . '/layout.php', $originalLayout);
        unlink($directory . '/scope.php');
    }
    check('Normal shared layouts still render after template failures', fn() => $app->handle('GET','/')->status === 200 && str_contains($app->handle('GET','/')->body, 'web<strong>spine</strong>'));
};
