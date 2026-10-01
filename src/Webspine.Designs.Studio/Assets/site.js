// Package-owned enhancement; no scripts can be uploaded as content.
for (const button of document.querySelectorAll('[data-back-to-top]')) {
  button.hidden = false;
  button.addEventListener('click', () => {
    document.getElementById('main')?.focus({ preventScroll: true });
    window.scrollTo({ top: 0, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
  });
}
