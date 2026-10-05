async function passkeyRequest(event, button) {
    const form = button.form;
    event.preventDefault();
    try {
        const username = form.querySelector('[name="Email"]')?.value ?? '';
        const optionsResponse = await fetch(`/api/auth/passkey/request-options?username=${encodeURIComponent(username)}`, {
            method: 'POST',
            credentials: 'include',
            headers: {'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]')?.value ?? ''}
        });
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
    try {
        const token = form.querySelector('[name="__RequestVerificationToken"]')?.value ?? '';
        const optionsResponse = await fetch('/api/auth/passkey/creation-options', {
            method: 'POST', credentials: 'include', headers: {'RequestVerificationToken': token}
        });
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
        console.error(error);
    }
}

document.addEventListener('click', event => {
    const button = event.target.closest('[data-passkey-register]');
    if (button) registerPasskey(event, button);
});
