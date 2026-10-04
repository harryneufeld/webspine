'use strict';
document.documentElement.classList.add('js');
const menuButton = document.querySelector('.menu-toggle');
const menu = document.querySelector('.primary-nav');
if (menuButton && menu) {
  menuButton.addEventListener('click', () => {
    const open = menuButton.getAttribute('aria-expanded') !== 'true';
    menuButton.setAttribute('aria-expanded', String(open));
    menu.classList.toggle('is-open', open);
  });
  menu.querySelectorAll('a').forEach(link => link.addEventListener('click', () => {
    menu.classList.remove('is-open'); menuButton.setAttribute('aria-expanded', 'false');
  }));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && menu.classList.contains('is-open')) {
      menu.classList.remove('is-open'); menuButton.setAttribute('aria-expanded', 'false'); menuButton.focus();
    }
  });
}
const tabs = [...document.querySelectorAll('[role="tab"]')];
const tabLayout = window.matchMedia('(max-width: 680px)');
function updateTabOrientation() {
  document.querySelector('[role="tablist"]')?.setAttribute('aria-orientation', tabLayout.matches ? 'horizontal' : 'vertical');
}
updateTabOrientation(); tabLayout.addEventListener('change', updateTabOrientation);
function selectTab(tab) {
  tabs.forEach(item => {
    const selected = item === tab;
    item.setAttribute('aria-selected', String(selected)); item.tabIndex = selected ? 0 : -1;
    document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
  });
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectTab(tab));
  tab.addEventListener('keydown', event => {
    let next;
    if (['ArrowDown', 'ArrowRight'].includes(event.key)) next = tabs[(index + 1) % tabs.length];
    if (['ArrowUp', 'ArrowLeft'].includes(event.key)) next = tabs[(index - 1 + tabs.length) % tabs.length];
    if (event.key === 'Home') next = tabs[0];
    if (event.key === 'End') next = tabs[tabs.length - 1];
    if (next) { event.preventDefault(); selectTab(next); next.focus(); }
  });
});
let toastTimer;
function notify(message) {
  const toast = document.querySelector('.toast');
  toast.textContent = message; toast.classList.add('visible');
  clearTimeout(toastTimer); toastTimer = setTimeout(() => toast.classList.remove('visible'), 3000);
}
document.querySelectorAll('[data-copy]').forEach(button => {
  button.addEventListener('click', async () => {
    const source = document.getElementById(button.dataset.copy).cloneNode(true);
    source.querySelectorAll('.terminal-prompt').forEach(prompt => prompt.remove());
    try {
      await navigator.clipboard.writeText(source.textContent.trim());
      notify('Commands copied. Ready for your terminal.');
    } catch {
      const range = document.createRange(); range.selectNodeContents(document.getElementById(button.dataset.copy));
      const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
      notify('Select and copy the highlighted code with your keyboard.');
    }
  });
});
const search = document.getElementById('docs-search');
if (search) {
  const sections = [...document.querySelectorAll('.doc-section')];
  const links = [...document.querySelectorAll('.docs-sidebar nav a')];
  const status = document.querySelector('.search-status');
  search.addEventListener('input', () => {
    const term = search.value.trim().toLowerCase(); let count = 0;
    sections.forEach((section, index) => {
      const matches = !term || section.textContent.toLowerCase().includes(term);
      section.hidden = !matches; links[index].hidden = !matches; if (matches) count++;
    });
    status.hidden = !term;
    status.textContent = count ? `${count} ${count === 1 ? 'section matches' : 'sections match'} “${search.value.trim()}”.` : 'No matching sections. Try “theme”, “install”, or “updates”.';
  });
  document.querySelectorAll('a[href^="#"]').forEach(link => link.addEventListener('click', () => {
    search.value = ''; search.dispatchEvent(new Event('input'));
  }));
  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(entries => entries.forEach(entry => {
      if (entry.isIntersecting) links.forEach(link => {
        if (link.hash === `#${entry.target.id}`) link.setAttribute('aria-current', 'location');
        else link.removeAttribute('aria-current');
      });
    }), { rootMargin: '-10% 0px -70% 0px' });
    sections.forEach(section => observer.observe(section));
  }
}
