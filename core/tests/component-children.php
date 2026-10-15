<?php
declare(strict_types=1);
use Webspine\{App, Files};

return static function (App $app, string $root): void {
    $directory = $root . '/site/themes/test-theme';
    $files = ['components/slot-card.php', 'components/slot-repeat.php', 'components/slot-ignore.php',
        'components/slot-recursion.php', 'components/slot-failure.php', 'slot-page.php'];
    $layout = file_get_contents($directory . '/layout.php');
    try {
        Files::write($directory . '/components/slot-card.php', <<<'PHP'
<?php
if (isset($app) || isset($site) || isset($callerOnly) || isset($this)) throw new RuntimeException('Slot scope leaked');
if (!$ui instanceof \Webspine\ThemeContext || !is_string($children)) throw new RuntimeException('Slot context replaced');
echo '<article><h2>' . e($props['title'] ?? '') . '</h2>' . $children . '</article>';
PHP);
        Files::write($directory . '/components/slot-repeat.php', '<?php echo $children . $children;');
        Files::write($directory . '/components/slot-ignore.php', '<?php echo "ignored";');
        Files::write($directory . '/components/slot-failure.php', '<?php echo $children; ob_start(); throw new RuntimeException("wrapper failure");');
        Files::write($directory . '/components/slot-recursion.php', '<?php echo $ui->component("slot-recursion", children: fn() => $ui->component("slot-recursion"));');
        check('Default slots accept omitted, empty, emitted, returned and mixed content', static function () use ($app): bool {
            $card = fn($children = null) => $app->theme->component('slot-card', children: $children);
            $empty = '<article><h2></h2></article>';
            return $card() === $empty && $card(static fn() => '') === $empty
                && $card(static function (): void { echo '<p>Emitted</p>'; }) === '<article><h2></h2><p>Emitted</p></article>'
                && $card(static fn() => '<p>Returned</p>') === '<article><h2></h2><p>Returned</p></article>'
                && $card(static function (): string { echo 'first'; ob_start(); echo 'second'; return 'third'; }) === '<article><h2></h2>firstsecondthird</article>';
        });
        check('Slots evaluate once before the wrapper, even when repeated or unused', static function () use ($app): bool {
            $calls = 0;
            $children = static function () use (&$calls): string { $calls++; return (string) $calls; };
            return $app->theme->component('slot-repeat', children: $children) === '11' && $calls === 1
                && $app->theme->component('slot-ignore', children: $children) === 'ignored' && $calls === 2
                && $app->theme->component('slot-repeat', children: $children) === '33' && $calls === 3;
        });
        check('Nested slots preserve explicit scope, escaped text and colliding props', static function () use ($app): bool {
            $callerOnly = '<script>unsafe</script>';
            $html = $app->theme->component('slot-card', ['title'=>'<Outer>', 'ui'=>'data', 'children'=>'data'], children: static function () use ($app, $callerOnly): string {
                echo '<p>' . e($callerOnly) . '</p>';
                return $app->theme->component('slot-card', ['title'=>'Inner'], children: fn() => $app->theme->component('wordmark', ['href'=>'/','prefix'=>'<web>','bold'=>'spine','label'=>'Home']));
            });
            return str_contains($html, '<h2>&lt;Outer&gt;</h2><p>&lt;script&gt;unsafe&lt;/script&gt;</p><article><h2>Inner</h2>')
                && str_contains($html, '&lt;web&gt;') && !str_contains($html, '<script>');
        });
        Files::write($directory . '/components/slot-repeat.php', '<?php echo $children . $children . e($props["children"] ?? "") . e($props["ui"] ?? "");');
        check('Slot and helper props remain accessible as data', fn() => $app->theme->component('slot-repeat', ['children'=>'<prop>', 'ui'=>'<ui>'], children: fn() => 'slot') === 'slotslot&lt;prop&gt;&lt;ui&gt;');
        check('Invalid child return types fail explicitly without leaking output', static function () use ($app): bool {
            foreach ([false, 0, [], new stdClass()] as $value) {
                $level = ob_get_level(); ob_start(); echo 'caller';
                try {
                    $failed = expectError(fn() => $app->theme->component('slot-card', children: static function () use ($value) { echo 'discard'; ob_start(); echo 'nested'; return $value; }), 'must return a string or null');
                    if (!$failed || ob_get_level() !== $level + 1 || ob_get_contents() !== 'caller') return false;
                } finally { while (ob_get_level() > $level) ob_end_clean(); }
            }
            return true;
        });
        check('Child exceptions and nested failures restore caller buffers and renderer depth', static function () use ($app): bool {
            foreach ([static function (): void { echo 'discard'; ob_start(); throw new RuntimeException('child failure'); },
                static fn() => $app->theme->component('missing')] as $children) {
                $level = ob_get_level(); ob_start(); echo 'caller';
                try {
                    if (!expectError(fn() => $app->theme->component('slot-card', children: $children)) || ob_get_level() !== $level + 1 || ob_get_contents() !== 'caller') return false;
                } finally { while (ob_get_level() > $level) ob_end_clean(); }
            }
            return $app->theme->component('slot-repeat', children: fn() => 'ok') === 'okok';
        });
        check('Invalid and missing components do not evaluate children', static function () use ($app): bool {
            $calls = 0;
            foreach (['../bad', 'missing'] as $name) {
                if (!expectError(fn() => $app->theme->component($name, children: static function () use (&$calls): void { $calls++; }))) return false;
            }
            return $calls === 0;
        });
        check('Wrapper failures discard completed child output and preserve caller buffers', static function () use ($app): bool {
            $level = ob_get_level(); ob_start(); echo 'caller';
            try {
                $failed = expectError(fn() => $app->theme->component('slot-failure', children: static function (): void { echo 'discard'; ob_start(); echo 'nested'; }), 'wrapper failure');
                return $failed && ob_get_level() === $level + 1 && ob_get_contents() === 'caller'
                    && $app->theme->component('slot-repeat', children: fn() => 'ok') === 'okok';
            } finally { while (ob_get_level() > $level) ob_end_clean(); }
        });
        check('Child recursion shares the component nesting limit and recovers cleanly', static function () use ($app): bool {
            $level = ob_get_level();
            return expectError(fn() => $app->theme->component('slot-recursion'), 'nesting limit') && ob_get_level() === $level
                && $app->theme->component('slot-repeat', children: fn() => 'ok') === 'okok';
        });
        Files::write($directory . '/slot-page.php', <<<'PHP'
<?= $ui->component('slot-card', ['title'=>$title], children: function () use ($ui, $body): void { ?>
<p><?= e($body) ?></p><?= $ui->component('slot-card', children: fn() => '<strong>Nested</strong>') ?>
<?php }) ?>
PHP);
        Files::write($directory . '/layout.php', '<main><?= $ui->component("slot-card", children: fn() => $content) ?></main>');
        check('Pages and layouts compose slots through the same three-method helper', static function () use ($app): bool {
            $methods = get_class_methods(\Webspine\ThemeContext::class);
            sort($methods);
            $response = $app->theme->render('slot-page', ['title'=>'<Card>', 'body'=>'<unsafe>']);
            return $methods === ['__construct', 'assetUrl', 'component', 'id'] && $response->status === 200
                && str_starts_with($response->body, '<main><article><h2></h2><article><h2>&lt;Card&gt;</h2>')
                && str_contains($response->body, '<p>&lt;unsafe&gt;</p>') && str_contains($response->body, '<strong>Nested</strong>');
        });
    } finally {
        Files::write($directory . '/layout.php', $layout);
        foreach ($files as $file) if (is_file($directory . '/' . $file)) unlink($directory . '/' . $file);
    }
};
