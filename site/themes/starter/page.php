<?php $copy = $site->content('page'); ?>
<article class="page">
    <h1><?= e($title) ?></h1>
    <p><?= e($body ?? '') ?></p>
    <?= $app->theme->component('action-link', ['label' => $copy['back_label'], 'href' => $copy['back_href']]) ?>
</article>
