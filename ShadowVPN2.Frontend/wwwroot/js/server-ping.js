window.pingServer = async function (url, timeoutMilliseconds) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), timeoutMilliseconds);
    const startedAt = performance.now();

    try {
        const response = await fetch(url, {
            method: 'GET',
            cache: 'no-store',
            signal: controller.signal
        });

        if (!response.ok) {
            throw new Error('Server returned an error');
        }

        return Math.round(performance.now() - startedAt);
    } finally {
        clearTimeout(timeout);
    }
};
