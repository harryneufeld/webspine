<?php $copy = $site->content('home'); ?>
<section class="hero" aria-labelledby="home-title">
    <h1 id="home-title"><?= e($copy['headline']) ?></h1>
    <div class="hero-intro">
        <p><?= e($copy['intro']) ?></p>
        <?= $app->theme->component('action-link', $copy['action']) ?>
    </div>
</section>
<section class="example-section" aria-labelledby="example-title">
    <div class="section-intro">
        <h2 id="example-title"><?= e($copy['section_title']) ?></h2>
        <p><?= e($copy['section_intro']) ?></p>
    </div>
    <ul class="example-list">
        <?php foreach ($copy['items'] as $item): ?>
        <li><h3><?= e($item['title']) ?></h3><p><?= e($item['text']) ?></p></li>
        <?php endforeach; ?>
    </ul>
</section>
