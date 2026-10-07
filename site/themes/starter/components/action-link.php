<?php // Reusable navigation action. href comes from trusted site content. ?>
<a class="action-link" href="<?= e($props['href']) ?>"><?= e($props['label']) ?><?= $ui->component('action-icon') ?></a>
