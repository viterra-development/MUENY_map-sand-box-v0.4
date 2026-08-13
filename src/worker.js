// MUENY Cloudflare Worker
// Handles /api/* requests (visitor logging today, more endpoints later) and
// falls through to Workers Static Assets for everything else, adding
// security headers to every response.

// script-src: 'self' + pinned unpkg bundles; 'unsafe-eval' is required by the
// map layer-config compiler (js/map.js "@@=" functions) and older Blazor WASM
// runtimes, 'wasm-unsafe-eval' by current ones. No 'unsafe-inline' — all
// scripts are external files.
const CSP = [
    "default-src 'self'",
    "script-src 'self' https://unpkg.com 'wasm-unsafe-eval' 'unsafe-eval'",
    "style-src 'self' 'unsafe-inline' https://unpkg.com https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com data:",
    "img-src 'self' data: blob: https:",
    "connect-src 'self' https:",
    "worker-src 'self' blob:",
    "child-src 'self' blob:",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
].join('; ');

const SECURITY_HEADERS = {
    'content-security-policy': CSP,
    'x-content-type-options': 'nosniff',
    'x-frame-options': 'DENY',
    'referrer-policy': 'strict-origin-when-cross-origin',
    'permissions-policy': 'geolocation=(), camera=(), microphone=(), payment=()',
};

function withSecurityHeaders(response) {
    const wrapped = new Response(response.body, response);
    for (const [name, value] of Object.entries(SECURITY_HEADERS)) {
        wrapped.headers.set(name, value);
    }
    return wrapped;
}

export default {
    async fetch(request, env, ctx) {
        const url = new URL(request.url);

        if (url.pathname === '/api/log-visit' && request.method === 'POST') {
            return withSecurityHeaders(await handleLogVisit(request, env, ctx));
        }

        // Fall through to static assets (Blazor WASM build output).
        return withSecurityHeaders(await env.ASSETS.fetch(request));
    },
};

// Best-effort per-isolate rate limit. Isolates are ephemeral and per-PoP, so
// this is NOT a hard guarantee — pair it with a Cloudflare WAF rate-limiting
// rule on POST /api/log-visit for real protection.
const RATE_WINDOW_MS = 60_000;
const RATE_MAX_PER_IP = 20;
const RATE_MAX_GLOBAL = 600;
const rateBuckets = new Map();
let globalCount = 0;
let windowStart = 0;

function rateLimited(ip, now) {
    if (now - windowStart > RATE_WINDOW_MS) {
        windowStart = now;
        globalCount = 0;
        rateBuckets.clear();
    }
    globalCount++;
    const ipCount = (rateBuckets.get(ip) || 0) + 1;
    rateBuckets.set(ip, ipCount);
    return ipCount > RATE_MAX_PER_IP || globalCount > RATE_MAX_GLOBAL;
}

const MAX_BODY_BYTES = 2048;
const MAX_FIELD_CHARS = 512;

// Truncate, strip control chars, and neutralize spreadsheet formula injection
// (leading = + - @ or tab become live formulas when the linked Sheet is
// exported to Excel/CSV and reopened).
function sanitizeField(value) {
    if (typeof value !== 'string') return '';
    let v = value.slice(0, MAX_FIELD_CHARS).replace(/[\x00-\x1f\x7f]/g, ' ');
    if (/^[=+\-@\t]/.test(v)) v = "'" + v;
    return v;
}

async function handleLogVisit(request, env, ctx) {
    // CF-Connecting-IP is set by Cloudflare's edge for every request;
    // True-Client-IP is a fallback for some routing paths. X-Forwarded-For is
    // deliberately NOT used: it is client-spoofable.
    const ip = request.headers.get('CF-Connecting-IP')
             || request.headers.get('True-Client-IP')
             || 'unknown';
    const cf = request.cf || {};

    if (rateLimited(ip, Date.now())) {
        return new Response(JSON.stringify({ ok: false, error: 'rate limited' }), {
            status: 429,
            headers: { 'content-type': 'application/json' },
        });
    }

    let body = {};
    try {
        const text = await request.text();
        if (text.length <= MAX_BODY_BYTES) {
            body = JSON.parse(text);
        }
    } catch (_) { /* beacon bodies may be malformed; log the request anyway */ }
    if (typeof body !== 'object' || body === null) body = {};

    // User-Agent and Referer come from headers only — the request body is
    // fully attacker-controlled and adds nothing the headers don't have.
    const visit = {
        timestamp: new Date().toISOString(),
        ip,
        path: sanitizeField(body.path || '/'),
        user_agent: sanitizeField(request.headers.get('User-Agent') || ''),
        referrer: sanitizeField(request.headers.get('Referer') || ''),
        country: sanitizeField(cf.country || ''),
        city: sanitizeField(cf.city || ''),
        region: sanitizeField(cf.region || ''),
        asn: cf.asn ? String(cf.asn) : '',
    };

    // Immediate visibility via `wrangler tail` and the Workers dashboard.
    console.log(`[visit] ${JSON.stringify(visit)}`);

    // Optional: forward to a Google Form so entries land in a linked Sheet in
    // Drive. Configure the Form/entry IDs as Worker secrets
    // (`wrangler secret put GOOGLE_FORM_ID` etc.) — see wrangler.toml.
    if (env.GOOGLE_FORM_ID && env.GOOGLE_FORM_ENTRY_IP) {
        ctx.waitUntil(sendToGoogleForm(env, visit));
    }

    // Same-origin beacon only — no CORS header on purpose.
    return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
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
