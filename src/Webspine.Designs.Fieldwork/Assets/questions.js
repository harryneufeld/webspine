// Progressive enhancement: individual native disclosures work without this script.
document.querySelectorAll('[data-questions]').forEach(section => {
    const button = section.querySelector('[data-toggle-questions]');
    const answers = [...section.querySelectorAll('details')];
    if (!button || answers.length === 0) return;
    const update = () => {
        const expanded = answers.every(answer => answer.open);
        button.textContent = expanded ? 'Hide all answers' : 'Show all answers';
        button.setAttribute('aria-expanded', String(expanded));
    };
    button.hidden = false;
    button.addEventListener('click', () => {
        const open = !answers.every(answer => answer.open);
        answers.forEach(answer => { answer.open = open; });
        update();
    });
    answers.forEach(answer => answer.addEventListener('toggle', update));
    update();
});
