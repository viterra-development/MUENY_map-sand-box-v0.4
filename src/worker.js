// MUENY Cloudflare Worker
// Handles /api/* requests (visitor logging today, more endpoints later) and
// falls through to Workers Static Assets for everything else.

export default {
    async fetch(request, env, ctx) {
        const url = new URL(request.url);

        if (url.pathname === '/api/log-visit' && request.method === 'POST') {
            return handleLogVisit(request, env, ctx);
        }

        // Fall through to static assets (Blazor WASM build output).
        return env.ASSETS.fetch(request);
    },
};

async function handleLogVisit(request, env, ctx) {
    // Extract the real visitor IP. CF-Connecting-IP is set by Cloudflare's edge
    // for every request; True-Client-IP is a fallback for some routing paths.
    const ip = request.headers.get('CF-Connecting-IP')
             || request.headers.get('True-Client-IP')
             || request.headers.get('X-Forwarded-For')?.split(',')[0]?.trim()
             || 'unknown';
    const cf = request.cf || {};

    let body = {};
    try {
        body = await request.json();
    } catch (_) {
        // Beacon may send text/plain; parse loosely.
        try {
            const text = await request.text();
            body = text ? JSON.parse(text) : {};
        } catch (_) { /* ignore */ }
    }

    const visit = {
        timestamp: new Date().toISOString(),
        ip,
        path: body.path || '/',
        user_agent: body.userAgent || request.headers.get('User-Agent') || '',
        referrer: body.referrer || request.headers.get('Referer') || '',
        country: cf.country || '',
        city: cf.city || '',
        region: cf.region || '',
        asn: cf.asn ? String(cf.asn) : '',
    };

    // Immediate visibility via `wrangler tail` and the Workers dashboard.
    console.log(`[visit] ${JSON.stringify(visit)}`);

    // Optional: forward to a Google Form so entries land in a linked Sheet in Drive.
    // Configure via wrangler.toml [vars] once you have the Form ID + entry IDs.
    if (env.GOOGLE_FORM_ID && env.GOOGLE_FORM_ENTRY_IP) {
        ctx.waitUntil(sendToGoogleForm(env, visit));
    }

    return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: {
            'content-type': 'application/json',
            'access-control-allow-origin': '*',
        },
    });
}

async function sendToGoogleForm(env, visit) {
    const url = `https://docs.google.com/forms/d/e/${env.GOOGLE_FORM_ID}/formResponse`;
    const params = new URLSearchParams();
    params.set(env.GOOGLE_FORM_ENTRY_IP,        visit.ip);
    if (env.GOOGLE_FORM_ENTRY_TS)      params.set(env.GOOGLE_FORM_ENTRY_TS,      visit.timestamp);
    if (env.GOOGLE_FORM_ENTRY_PATH)    params.set(env.GOOGLE_FORM_ENTRY_PATH,    visit.path);
    if (env.GOOGLE_FORM_ENTRY_UA)      params.set(env.GOOGLE_FORM_ENTRY_UA,      visit.user_agent);
    if (env.GOOGLE_FORM_ENTRY_REF)     params.set(env.GOOGLE_FORM_ENTRY_REF,     visit.referrer);
    if (env.GOOGLE_FORM_ENTRY_COUNTRY) params.set(env.GOOGLE_FORM_ENTRY_COUNTRY, visit.country);
    if (env.GOOGLE_FORM_ENTRY_CITY)    params.set(env.GOOGLE_FORM_ENTRY_CITY,    visit.city);
    if (env.GOOGLE_FORM_ENTRY_REGION)  params.set(env.GOOGLE_FORM_ENTRY_REGION,  visit.region);
    if (env.GOOGLE_FORM_ENTRY_ASN)     params.set(env.GOOGLE_FORM_ENTRY_ASN,     visit.asn);

    try {
        await fetch(url, {
            method: 'POST',
            headers: { 'content-type': 'application/x-www-form-urlencoded' },
            body: params.toString(),
        });
    } catch (e) {
        console.warn(`[visit] Google Form POST failed: ${e.message}`);
    }
}
