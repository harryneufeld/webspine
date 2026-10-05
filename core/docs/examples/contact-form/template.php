<link rel="stylesheet" href="/assets/theme/<?= e($theme) ?>/contact-form.css">
<section class="contact-form" aria-labelledby="contact-title">
    <h1 id="contact-title"><?= e($title) ?></h1>
    <p role="status"><?= e($notice) ?></p>
    <?php if ($errors): ?><ul><?php foreach ($errors as $key=>$error): ?><li><a href="#contact-<?= e($key) ?>"><?= e($error) ?></a></li><?php endforeach; ?></ul><?php endif; ?>
    <form method="post" action="/contact-example">
        <input type="hidden" name="csrf" value="<?= e($token) ?>">
        <div hidden><label>Leave empty <input name="website" tabindex="-1" autocomplete="off"></label></div>
        <?php foreach (['name','email','message'] as $key): ?>
        <label for="contact-<?= e($key) ?>"><?= e($copy[$key]) ?></label>
        <?php $attributes = isset($errors[$key]) ? ' aria-invalid="true" aria-describedby="contact-' . $key . '-error"' : ''; ?>
        <?php if ($key === 'message'): ?>
        <textarea id="contact-message" name="message" rows="6" maxlength="5000" required<?= $attributes ?>><?= e($fields[$key] ?? '') ?></textarea>
        <?php else: ?>
        <input id="contact-<?= e($key) ?>" name="<?= e($key) ?>" type="<?= $key === 'email' ? 'email' : 'text' ?>" autocomplete="<?= e($key) ?>" maxlength="<?= $key === 'email' ? 254 : 120 ?>" required value="<?= e($fields[$key] ?? '') ?>"<?= $attributes ?>>
        <?php endif; ?>
        <?php if (isset($errors[$key])): ?><p id="contact-<?= e($key) ?>-error"><?= e($errors[$key]) ?></p><?php endif; ?>
        <?php endforeach; ?>
        <button type="submit"><?= e($copy['submit']) ?></button>
    </form>
</section>
