// Passive visit log: fire-and-forget beacon, no user friction. The Worker
// extracts CF-Connecting-IP and User-Agent/Referer server-side from headers;
// the body only adds the SPA path. sendBeacon survives navigation and
// doesn't block page load.
(function () {
    try {
        const payload = JSON.stringify({
            path: location.pathname + location.search
        });
        if (navigator.sendBeacon) {
            const blob = new Blob([payload], { type: 'application/json' });
            navigator.sendBeacon('/api/log-visit', blob);
        } else {
            fetch('/api/log-visit', { method: 'POST', keepalive: true, body: payload, headers: { 'content-type': 'application/json' } }).catch(() => {});
        }
    } catch (_) { /* never let logging break the page */ }
})();
