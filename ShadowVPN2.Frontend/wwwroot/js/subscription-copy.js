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
            await copyText(button.dataset.copyText);
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

async function copyText(text) {
    if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }

    const input = document.createElement('textarea');
    input.value = text;
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.focus();
    input.select();
    if (!document.execCommand('copy'))
        throw new Error('Clipboard copy failed');
    input.remove();
}

/*
 * Keep disposal below the helper so module consumers retain the same API.
 */
export function dispose(id) {
    const state = pages.get(id);
    if (!state) return;
    state.root.removeEventListener('click', state.click);
    pages.delete(id);
}
