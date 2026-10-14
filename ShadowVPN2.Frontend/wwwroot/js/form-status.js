document.addEventListener('submit', event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post' || event.defaultPrevented) return;
    const submitter = event.submitter;
    if (!(submitter instanceof HTMLButtonElement)) return;
    // Keep successful form controls enabled until the browser has collected POST data.
    form.setAttribute('aria-busy', 'true');
    const original = submitter.textContent;
    submitter.style.minWidth = `${submitter.getBoundingClientRect().width}px`;
    submitter.textContent = 'Processing…';
    setTimeout(() => {
        if (!event.defaultPrevented) {
            submitter.disabled = true;
            for (const button of form.querySelectorAll('button[type="button"]')) button.disabled = true;
        } else {
            form.removeAttribute('aria-busy');
            submitter.textContent = original;
        }
    }, 0);
});
