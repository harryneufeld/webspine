<?php $copy = $site->content('layout'); ?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <meta name="description" content="<?= e($site->meta['description']) ?>">
    <meta name="theme-color" content="#f5f5ef">
    <title><?= e($title) ?> · <?= e($site->meta['name']) ?></title>
    <link rel="icon" href="/assets/theme/<?= e($theme) ?>/favicon.svg" type="image/svg+xml">
    <link rel="stylesheet" href="/assets/theme/<?= e($theme) ?>/style.css?v=<?= e(substr(hash_file('sha256', $directory . '/assets/style.css'), 0, 12)) ?>">
    <script src="/assets/theme/<?= e($theme) ?>/app.js?v=<?= e(substr(hash_file('sha256', $directory . '/assets/app.js'), 0, 12)) ?>" defer></script>
</head>
<body class="ws <?= e($page ?? '') ?>">
<a class="skip-link" href="<?= e($copy['href_main']) ?>"><?= e($copy['skip_to_content']) ?></a>
<header class="site-header shell">
    <a class="wordmark" href="<?= e($copy['href_']) ?>" aria-label="<?= e($site->meta['name'] . ' home') ?>"><?= e($site->meta['brand_prefix']) ?><strong><?= e($site->meta['brand_bold']) ?></strong><span class="brand-dot" aria-hidden="true"></span></a>
    <button class="menu-toggle" type="button" aria-expanded="false" aria-controls="primary-nav"><?= e($copy['menu']) ?> <span aria-hidden="true">+</span></button>
    <nav class="primary-nav" id="primary-nav" aria-label="<?= e($copy['aria_label_main_navigation']) ?>">
        <a href="<?= e($copy['href_framework']) ?>"<?= ($page ?? '') === 'home' ? ' class="active"' : '' ?>><?= e($copy['the_framework']) ?></a>
        <a href="<?= e($copy['href_architecture']) ?>"><?= e($copy['how_it_works']) ?></a>
        <a href="<?= e($copy['href_docs']) ?>"<?= ($page ?? '') === 'docs' ? ' aria-current="page" class="active"' : '' ?>><?= e($copy['documentation']) ?> <span aria-hidden="true">↗</span></a>
    </nav>
    <a class="header-cta" href="<?= e($copy['href_docs_quickstart']) ?>"><?= e($copy['start_building']) ?> <span aria-hidden="true">↗</span></a>
</header>
<main id="main"><?= $content ?></main>
<footer class="site-footer shell">
    <div class="footer-main"><a class="wordmark" href="<?= e($copy['href_']) ?>"><?= e($site->meta['brand_prefix']) ?><strong><?= e($site->meta['brand_bold']) ?></strong><span class="brand-dot" aria-hidden="true"></span></a><p><?= e($copy['a_solid_foundation']) ?><br><?= e($copy['room_to_make_it_yours']) ?></p><div class="footer-links"><a href="<?= e($copy['href_docs']) ?>"><?= e($copy['documentation_2']) ?></a><a href="<?= e($copy['href_download']) ?>"><?= e($copy['download_source']) ?></a><a href="<?= e($copy['href_field_notes']) ?>"><?= e($copy['example_plugin']) ?></a></div></div>
    <div class="footer-bottom"><span><?= e($copy['small_by_design_open_by_default']) ?></span><span><?= e($copy['mit_licensed_v']) ?><?= e($app->version['version']) ?> <?= e($copy['bootstrap']) ?></span><a href="<?= e($copy['href_docs_roadmap']) ?>"><?= e($copy['built_for_what_comes_next']) ?> <span aria-hidden="true">↗</span></a></div>
</footer>
<div class="toast" role="status" aria-live="polite"></div>
</body>
</html>
