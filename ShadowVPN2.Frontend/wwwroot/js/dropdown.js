const dropdowns = new Map();

export function initialize(id) {
    const root = document.getElementById(id);
    if (!root || dropdowns.get(id)?.root === root) return;
    dispose(id);
    const trigger = root.querySelector('[data-dropdown-trigger]');
    const popup = root.querySelector('[data-dropdown-popup]');
    if (!trigger || !popup) return;
    const items = () => [...popup.querySelectorAll('button:not(:disabled)')];
    const isOpen = () => trigger.getAttribute('aria-expanded') === 'true';
    const setOpen = (open, restoreFocus = false) => {
        popup.classList.toggle('show', open);
        trigger.setAttribute('aria-expanded', String(open));
        if (restoreFocus) trigger.focus();
    };
    const click = event => {
        if (trigger.contains(event.target)) setOpen(!isOpen());
        else if (popup.contains(event.target) && event.target.closest('button:not(:disabled)')) setOpen(false, true);
    };
    const outside = event => {
        if (isOpen() && !root.contains(event.target)) setOpen(false);
    };
    const focusOut = event => {
        if (isOpen() && !root.contains(event.relatedTarget)) setOpen(false);
    };
    const keydown = event => {
        if (event.key === 'Escape' && isOpen()) {
            event.preventDefault();
            event.stopPropagation();
            setOpen(false, true);
            return;
        }
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        setOpen(true);
        const options = items();
        if (!options.length) return;
        const current = options.indexOf(document.activeElement);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1
            : event.key === 'ArrowDown' ? (current + 1) % options.length
                : current <= 0 ? options.length - 1 : current - 1;
        options[next].focus();
    };
    root.addEventListener('click', click);
    root.addEventListener('keydown', keydown);
    root.addEventListener('focusout', focusOut);
    document.addEventListener('pointerdown', outside);
    dropdowns.set(id, {root, trigger, popup, click, outside, focusOut, keydown});
}

export function dispose(id) {
    const state = dropdowns.get(id);
    if (!state) return;
    state.root.removeEventListener('click', state.click);
    state.root.removeEventListener('keydown', state.keydown);
    state.root.removeEventListener('focusout', state.focusOut);
    document.removeEventListener('pointerdown', state.outside);
    dropdowns.delete(id);
}
