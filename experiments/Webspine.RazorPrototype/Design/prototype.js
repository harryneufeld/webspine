// Static HTML uses ordinary browser code. No Blazor runtime or C# event handlers.
for (const section of document.querySelectorAll('.faq')) {
  const button = section.querySelector('[data-faq-toggle]');
  const answers = [...section.querySelectorAll('details')];
  if (!button || !answers.length) continue;
  const sync = () => {
    const expanded = answers.every(answer => answer.open);
    button.setAttribute('aria-expanded', String(expanded));
    button.textContent = expanded ? 'Collapse all answers' : 'Expand all answers';
  };
  button.hidden = false;
  button.addEventListener('click', () => {
    const expand = !answers.every(answer => answer.open);
    for (const answer of answers) answer.open = expand;
    sync();
  });
  for (const answer of answers) answer.addEventListener('toggle', sync);
  sync();
}
