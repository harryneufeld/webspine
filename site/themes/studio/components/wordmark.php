<?php
/** Props: href, prefix, bold; optional accessible label. All values are plain text. */
?>
<a class="wordmark" href="<?= e($props['href']) ?>"<?php if (isset($props['label'])): ?> aria-label="<?= e($props['label']) ?>"<?php endif; ?>><?= e($props['prefix']) ?><strong><?= e($props['bold']) ?></strong><span class="brand-dot" aria-hidden="true"></span></a>
