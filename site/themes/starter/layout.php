<?php
$copy = $site->content('layout');
$indexable = ($site->meta['indexable'] ?? false) === true;
$brandProps = ['href' => '/', 'prefix' => $site->meta['brand_prefix'], 'bold' => $site->meta['brand_bold'], 'label' => $site->meta['name'] . ' home'];
?>
<!doctype html>
<html lang="<?= e($site->meta['language']) ?>">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="description" content="<?= e($site->meta['description']) ?>">
    <?php if (!$indexable): ?><meta name="robots" content="noindex"><?php endif; ?>
    <title><?= e($title) ?> · <?= e($site->meta['name']) ?></title>
    <link rel="icon" href="<?= e($ui->assetUrl('favicon.svg')) ?>" type="image/svg+xml">
    <link rel="stylesheet" href="<?= e($ui->assetUrl('style.css')) ?>">
</head>
<body class="ws-starter">
<a class="skip-link" href="#main"><?= e($copy['skip_link']) ?></a>
<header class="site-header wrap">
    <?= $app->theme->component('wordmark', $brandProps) ?>
    <nav aria-label="<?= e($copy['navigation_label']) ?>">
        <?php foreach ($copy['navigation'] as $link): ?>
        <a href="<?= e($link['href']) ?>"<?= ($page ?? '') === $link['page'] ? ' aria-current="page"' : '' ?>><?= e($link['label']) ?></a>
        <?php endforeach; ?>
    </nav>
</header>
<?php if (!$indexable): ?>
<aside class="indexing-notice wrap" aria-label="<?= e($copy['indexing_label']) ?>">
    <p><?= e($copy['indexing_notice']) ?></p>
</aside>
<?php endif; ?>
<main id="main" class="wrap"><?= $content ?></main>
<footer class="site-footer wrap">
    <?= $app->theme->component('wordmark', $brandProps) ?>
    <p><?= e($copy['footer']) ?></p>
    <a class="credit" href="https://github.com/harryneufeld/webspine"><?= e($copy['credit']) ?> web<strong>spine</strong> ↗</a>
</footer>
</body>
</html>
