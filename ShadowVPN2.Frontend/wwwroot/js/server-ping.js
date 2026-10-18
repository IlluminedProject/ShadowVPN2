window.pingServer = async function (url, timeoutMilliseconds) {
    let totalLatency = 0;

    for (let attempt = 0; attempt < 3; attempt++) {
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

            await response.arrayBuffer();
            if (attempt > 0) {
                totalLatency += performance.now() - startedAt;
            }
        } finally {
            clearTimeout(timeout);
        }
    }

    return Math.round(totalLatency / 2);
};
