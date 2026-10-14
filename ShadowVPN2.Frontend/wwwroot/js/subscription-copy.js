const pages = new Map();

export function initialize(id) {
    const root = document.getElementById(id);
    if (!root || pages.get(id)?.root === root) return;
    dispose(id);
    const click = async event => {
        const button = event.target.closest('button[data-copy-text]');
        if (!button || !root.contains(button) || button.disabled) return;
        const error = root.querySelector('[data-copy-error]');
        error.hidden = true;
        button.disabled = true;
        try {
            await navigator.clipboard.writeText(button.dataset.copyText);
        } catch {
            error.textContent = 'Could not copy. Allow clipboard access and try again.';
            error.hidden = false;
        } finally {
            button.disabled = false;
        }
    };
    root.addEventListener('click', click);
    pages.set(id, {root, click});
}

export function dispose(id) {
    const state = pages.get(id);
    if (!state) return;
    state.root.removeEventListener('click', state.click);
    pages.delete(id);
}
