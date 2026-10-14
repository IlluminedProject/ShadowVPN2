function beginPasskey(button) {
    const original = button.textContent;
    button.disabled = true;
    button.style.minWidth = `${button.getBoundingClientRect().width}px`;
    button.textContent = 'Waiting for passkey…';
    button.form.setAttribute('aria-busy', 'true');
    button.form.querySelector('[data-passkey-status]')?.remove();
    return original;
}

function endPasskey(button, original, error) {
    button.disabled = false;
    button.textContent = original;
    button.form.removeAttribute('aria-busy');
    const status = document.createElement('div');
    status.dataset.passkeyStatus = '';
    status.className = 'alert alert-danger mt-3';
    status.setAttribute('role', 'alert');
    status.textContent = error.name === 'NotAllowedError'
        ? 'Passkey request cancelled. Try again when ready.'
        : 'Could not complete the passkey request. Try again.';
    button.form.append(status);
}

async function passkeyRequest(event, button) {
    const form = button.form;
    event.preventDefault();
    const original = beginPasskey(button);
    try {
        const username = form.querySelector('[name="Email"]')?.value ?? '';
        const optionsResponse = await fetch(`/api/auth/passkey/request-options?username=${encodeURIComponent(username)}`, {
            method: 'POST',
            credentials: 'include',
            headers: {'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]')?.value ?? ''}
        });
        if (!optionsResponse.ok) throw new Error('Passkey options request failed');
        const options = PublicKeyCredential.parseRequestOptionsFromJSON(await optionsResponse.json());
        const credential = await navigator.credentials.get({publicKey: options});
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'CredentialJson';
        input.value = JSON.stringify(credential);
        form.action = '/api/auth/passkey/sign-in';
        form.appendChild(input);
        form.submit();
    } catch (error) {
        endPasskey(button, original, error);
        if (error.name !== 'NotAllowedError') console.error(error);
    }
}

document.addEventListener('click', event => {
    const button = event.target.closest('[data-passkey-login]');
    if (button) passkeyRequest(event, button);
});

async function registerPasskey(event, button) {
    const form = button.form;
    event.preventDefault();
    const original = beginPasskey(button);
    try {
        const token = form.querySelector('[name="__RequestVerificationToken"]')?.value ?? '';
        const optionsResponse = await fetch('/api/auth/passkey/creation-options', {
            method: 'POST', credentials: 'include', headers: {'RequestVerificationToken': token}
        });
        if (!optionsResponse.ok) throw new Error('Passkey options request failed');
        const options = PublicKeyCredential.parseCreationOptionsFromJSON(await optionsResponse.json());
        const credential = await navigator.credentials.create({publicKey: options});
        const data = new FormData(form);
        data.append('CredentialJson', JSON.stringify(credential));
        form.action = '/api/auth/passkey/register';
        form.submit = HTMLFormElement.prototype.submit;
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'CredentialJson';
        input.value = JSON.stringify(credential);
        form.appendChild(input);
        form.submit();
    } catch (error) {
        endPasskey(button, original, error);
        if (error.name !== 'NotAllowedError') console.error(error);
    }
}

document.addEventListener('click', event => {
    const button = event.target.closest('[data-passkey-register]');
    if (button) registerPasskey(event, button);
});
