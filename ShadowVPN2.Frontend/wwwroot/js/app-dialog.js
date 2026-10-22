const dialogs = new WeakMap();

export function open(element, reference, busy, lightDismiss = true) {
    const previousFocus = document.activeElement;
    const state = {busy, closing: false};
    const cancel = async event => {
        event.preventDefault();
        if (state.busy || state.closing || element.querySelector('form[aria-busy="true"]')) return;
        state.closing = true;
        try {
            await reference.invokeMethodAsync('CancelAsync');
        } finally {
            state.closing = false;
        }
    };
    let startedOutside = false;
    const outside = event => {
        const bounds = element.getBoundingClientRect();
        return event.target === element && (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom);
    };
    const pointerDown = event => {
        startedOutside = outside(event);
    };
    const click = event => {
        if (lightDismiss && startedOutside && outside(event)) void cancel(event);
        startedOutside = false;
    };
    const restoreFocus = () => {
        if (previousFocus instanceof HTMLElement && previousFocus.isConnected)
            previousFocus.focus({preventScroll: true});
    };
    const observer = new MutationObserver(() => {
        if (!element.isConnected) cleanup();
    });
    const cleanup = () => {
        observer.disconnect();
        element.removeEventListener('cancel', cancel);
        element.removeEventListener('close', restoreFocus);
        element.removeEventListener('pointerdown', pointerDown);
        element.removeEventListener('click', click);
        if (element.open) element.close();
        restoreFocus();
        dialogs.delete(element);
    };
    dialogs.set(element, {state, cleanup});
    element.addEventListener('cancel', cancel);
    element.addEventListener('close', restoreFocus);
    element.addEventListener('pointerdown', pointerDown);
    element.addEventListener('click', click);
    observer.observe(document.body, {childList: true, subtree: true});
    element.showModal();
}

export function setBusy(element, busy) {
    const dialog = dialogs.get(element);
    if (dialog) dialog.state.busy = busy;
}

export function dispose(element) {
    dialogs.get(element)?.cleanup();
}
