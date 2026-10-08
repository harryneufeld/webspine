<link rel="stylesheet" href="<?= e($ui->assetUrl('contact-form.css')) ?>">
<section class="contact-form" aria-labelledby="contact-title">
    <h1 id="contact-title"><?= e($title) ?></h1>
    <p role="status"><?= e($notice) ?></p>
    <?php if ($errors): ?><ul role="alert"><?php foreach ($errors as $key=>$error): ?><li><a href="#contact-<?= e($key) ?>"><?= e($definitions[$key]['label'] . ': ' . $error) ?></a></li><?php endforeach; ?></ul><?php endif; ?>
    <form method="post" action="<?= e($formPath) ?>">
        <input type="hidden" name="csrf" value="<?= e($token) ?>">
        <div hidden><label>Leave empty <input name="website" tabindex="-1" autocomplete="off"></label></div>
        <?php foreach ($definitions as $key=>$definition): ?>
        <label for="contact-<?= e($key) ?>"><?= e($definition['label']) ?></label>
        <?php $attributes = ($definition['required'] ?? false) ? ' required' : ''; $attributes .= isset($errors[$key]) ? ' aria-invalid="true" aria-describedby="contact-' . e($key) . '-error"' : ''; $type=$definition['type'] ?? 'text'; ?>
        <?php if ($type === 'textarea'): ?>
        <textarea id="contact-<?= e($key) ?>" name="<?= e($key) ?>" rows="6" maxlength="<?= $definition['max'] ?? 5000 ?>"<?= $attributes ?>><?= e($fields[$key] ?? '') ?></textarea>
        <?php else: ?>
        <input id="contact-<?= e($key) ?>" name="<?= e($key) ?>" type="<?= e($type) ?>" maxlength="<?= $definition['max'] ?? 5000 ?>" value="<?= e($fields[$key] ?? '') ?>"<?= $attributes ?>>
        <?php endif; ?>
        <?php if (isset($errors[$key])): ?><p id="contact-<?= e($key) ?>-error"><?= e($errors[$key]) ?></p><?php endif; ?>
        <?php endforeach; ?>
        <button type="submit"><?= e($copy['submit']) ?></button>
    </form>
</section>
