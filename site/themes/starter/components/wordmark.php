<?php // Plain-text props: href, prefix, bold; optional accessible label. ?>
<a class="wordmark" href="<?= e($props['href']) ?>"<?php if (isset($props['label'])): ?> aria-label="<?= e($props['label']) ?>"<?php endif; ?>><?= e($props['prefix']) ?><strong><?= e($props['bold']) ?></strong><span aria-hidden="true">.</span></a>
