// MUENY Cloudflare Worker
// Serves the marketing landing page at `/`, the Blazor app + data at every
// other static path, and the dynamic API under /api/*:
//   POST /api/log-visit          visitor beacon (anonymous)
//   POST /api/auth/register      email+password signup (invite-gated if INVITE_CODE set)
//   POST /api/auth/login         session login
//   POST /api/auth/logout        session logout
//   GET  /api/auth/me            current user
//   GET  /api/views              saved map views (auth)
//   POST /api/views              save a view (auth)
//   DELETE /api/views/{id}       delete a view (auth)
// Storage: AUTH_KV (users, sessions, views — key-prefixed).

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

function json(status, body, extraHeaders = {}) {
    return new Response(JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/json', ...extraHeaders },
    });
}

export default {
    async fetch(request, env, ctx) {
        const url = new URL(request.url);
        const path = url.pathname;

        let response;
        if (path === '/') {
            // Marketing landing page at the root; the app lives at /map.
            response = await serveAsset(env, request, '/landing.html');
        } else if (path.startsWith('/api/')) {
            response = await handleApi(request, env, ctx, url);
        } else {
            response = await env.ASSETS.fetch(request);
            // Data & framework paths must return a real 404 when missing.
            // The SPA fallback otherwise rewrites them to index.html, which
            // corrupts geojson/wasm parsing in deck.gl and the Blazor loader.
            // (Replaces the legacy _redirects 404 rules wrangler now rejects.)
            const HARD_404_PREFIXES = ['/tiles/', '/cris-data/', '/soil-data/', '/sample-data/', '/_framework/'];
            if (HARD_404_PREFIXES.some(p => path.startsWith(p))
                && (response.headers.get('content-type') || '').includes('text/html')) {
                response = new Response('Not found', { status: 404, headers: { 'content-type': 'text/plain' } });
            }
        }
        return withSecurityHeaders(response);
    },
};

async function serveAsset(env, request, assetPath) {
    const assetUrl = new URL(assetPath, request.url);
    return env.ASSETS.fetch(new Request(assetUrl, { headers: request.headers }));
}

async function handleApi(request, env, ctx, url) {
    const path = url.pathname;
    const method = request.method;

    // Same-origin check for state-changing requests. Browsers send Origin on
    // cross-site and same-site POSTs; requests without one (curl, beacons)
    // are allowed — this guards CSRF from browsers, not scripted clients.
    if (method !== 'GET') {
        const origin = request.headers.get('Origin');
        if (origin && origin !== url.origin) {
            return json(403, { ok: false, error: 'cross-origin request rejected' });
        }
    }

    if (path === '/api/log-visit' && method === 'POST') return handleLogVisit(request, env, ctx);
    if (path === '/api/auth/register' && method === 'POST') return handleRegister(request, env, url);
    if (path === '/api/auth/login' && method === 'POST') return handleLogin(request, env, url);
    if (path === '/api/auth/logout' && method === 'POST') return handleLogout(request, env);
    if (path === '/api/auth/me' && method === 'GET') return handleMe(request, env);
    if (path === '/api/views' && method === 'GET') return handleViewsList(request, env);
    if (path === '/api/views' && method === 'POST') return handleViewsSave(request, env);
    if (path.startsWith('/api/views/') && method === 'DELETE') return handleViewsDelete(request, env, path);

    return json(404, { ok: false, error: 'not found' });
}

// ---------------------------------------------------------------------------
// Rate limiting — best-effort per-isolate. Pair with a Cloudflare WAF
// rate-limiting rule for hard guarantees.
// ---------------------------------------------------------------------------
const RATE_WINDOW_MS = 60_000;
const rateBuckets = new Map();
let windowStart = 0;

function rateLimited(key, max, now = Date.now()) {
    if (now - windowStart > RATE_WINDOW_MS) {
        windowStart = now;
        rateBuckets.clear();
    }
    const count = (rateBuckets.get(key) || 0) + 1;
    rateBuckets.set(key, count);
    return count > max;
}

function clientIp(request) {
    return request.headers.get('CF-Connecting-IP')
        || request.headers.get('True-Client-IP')
        || 'unknown';
}

async function readJsonBody(request, maxBytes = 4096) {
    try {
        const text = await request.text();
        if (text.length > maxBytes) return null;
        const body = JSON.parse(text);
        return (typeof body === 'object' && body !== null) ? body : null;
    } catch (_) {
        return null;
    }
}

// ---------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------
const SESSION_COOKIE = 'mueny_session';
const SESSION_TTL_SECONDS = 30 * 24 * 3600;
const PBKDF2_ITERATIONS = 100_000;

const b64 = {
    encode: (buf) => btoa(String.fromCharCode(...new Uint8Array(buf))),
    decode: (str) => Uint8Array.from(atob(str), c => c.charCodeAt(0)),
};

async function hashPassword(password, saltBytes) {
    const keyMaterial = await crypto.subtle.importKey(
        'raw', new TextEncoder().encode(password), 'PBKDF2', false, ['deriveBits']);
    const bits = await crypto.subtle.deriveBits(
        { name: 'PBKDF2', hash: 'SHA-256', salt: saltBytes, iterations: PBKDF2_ITERATIONS },
        keyMaterial, 256);
    return b64.encode(bits);
}

function normalizeEmail(raw) {
    if (typeof raw !== 'string') return null;
    const email = raw.trim().toLowerCase();
    if (email.length < 5 || email.length > 254) return null;
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email)) return null;
    return email;
}

function getSessionToken(request) {
    const cookie = request.headers.get('Cookie') || '';
    const match = cookie.match(new RegExp(`(?:^|;\\s*)${SESSION_COOKIE}=([A-Za-z0-9_-]{20,})`));
    return match ? match[1] : null;
}

function sessionCookie(token, maxAge) {
    return `${SESSION_COOKIE}=${token}; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=${maxAge}`;
}

async function createSession(env, email) {
    const raw = new Uint8Array(32);
    crypto.getRandomValues(raw);
    const token = b64.encode(raw).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    await env.AUTH_KV.put(`session:${token}`, JSON.stringify({ email, created: new Date().toISOString() }),
        { expirationTtl: SESSION_TTL_SECONDS });
    return token;
}

async function currentUser(request, env) {
    const token = getSessionToken(request);
    if (!token) return null;
    const session = await env.AUTH_KV.get(`session:${token}`, 'json');
    if (!session?.email) return null;
    const user = await env.AUTH_KV.get(`user:${session.email}`, 'json');
    if (!user) return null;
    return { email: user.email, name: user.name || '', token };
}

async function handleRegister(request, env, url) {
    if (rateLimited(`reg:${clientIp(request)}`, 5)) {
        return json(429, { ok: false, error: 'Too many attempts — try again in a minute.' });
    }
    const body = await readJsonBody(request);
    if (!body) return json(400, { ok: false, error: 'Invalid request.' });

    const email = normalizeEmail(body.email);
    const password = typeof body.password === 'string' ? body.password : '';
    const name = (typeof body.name === 'string' ? body.name : '').trim().slice(0, 80);

    if (!email) return json(400, { ok: false, error: 'Enter a valid email address.' });
    if (password.length < 8 || password.length > 200) {
        return json(400, { ok: false, error: 'Password must be at least 8 characters.' });
    }
    if (env.INVITE_CODE && body.invite !== env.INVITE_CODE) {
        return json(403, { ok: false, error: 'A valid invite code is required.' });
    }

    const existing = await env.AUTH_KV.get(`user:${email}`);
    if (existing) return json(409, { ok: false, error: 'An account with that email already exists — log in instead.' });

    const salt = new Uint8Array(16);
    crypto.getRandomValues(salt);
    const pwHash = await hashPassword(password, salt);
    await env.AUTH_KV.put(`user:${email}`, JSON.stringify({
        email, name, pwSalt: b64.encode(salt), pwHash, created: new Date().toISOString(),
    }));

    const token = await createSession(env, email);
    return json(200, { ok: true, user: { email, name } },
        { 'set-cookie': sessionCookie(token, SESSION_TTL_SECONDS) });
}

async function handleLogin(request, env) {
    if (rateLimited(`login:${clientIp(request)}`, 10)) {
        return json(429, { ok: false, error: 'Too many attempts — try again in a minute.' });
    }
    const body = await readJsonBody(request);
    if (!body) return json(400, { ok: false, error: 'Invalid request.' });

    const email = normalizeEmail(body.email);
    const password = typeof body.password === 'string' ? body.password : '';
    const fail = () => json(401, { ok: false, error: 'Email or password is incorrect.' });
    if (!email || !password) return fail();

    const user = await env.AUTH_KV.get(`user:${email}`, 'json');
    if (!user?.pwSalt || !user?.pwHash) return fail();

    const pwHash = await hashPassword(password, b64.decode(user.pwSalt));
    if (pwHash !== user.pwHash) return fail();

    const token = await createSession(env, email);
    return json(200, { ok: true, user: { email: user.email, name: user.name || '' } },
        { 'set-cookie': sessionCookie(token, SESSION_TTL_SECONDS) });
}

async function handleLogout(request, env) {
    const token = getSessionToken(request);
    if (token) await env.AUTH_KV.delete(`session:${token}`);
    return json(200, { ok: true }, { 'set-cookie': sessionCookie('', 0) });
}

async function handleMe(request, env) {
    const user = await currentUser(request, env);
    if (!user) return json(401, { ok: false, error: 'Not logged in.' });
    return json(200, { ok: true, user: { email: user.email, name: user.name } });
}

// ---------------------------------------------------------------------------
// Saved views — small JSON blobs of map state per user.
// ---------------------------------------------------------------------------
const MAX_VIEWS_PER_USER = 20;
const MAX_VIEW_STATE_BYTES = 8192;

async function handleViewsList(request, env) {
    const user = await currentUser(request, env);
    if (!user) return json(401, { ok: false, error: 'Log in to use saved views.' });
    const views = (await env.AUTH_KV.get(`views:${user.email}`, 'json')) || [];
    return json(200, { ok: true, views });
}

async function handleViewsSave(request, env) {
    const user = await currentUser(request, env);
    if (!user) return json(401, { ok: false, error: 'Log in to use saved views.' });
    if (rateLimited(`views:${user.email}`, 30)) {
        return json(429, { ok: false, error: 'Too many requests.' });
    }

    const body = await readJsonBody(request, 16384);
    if (!body) return json(400, { ok: false, error: 'Invalid request.' });
    const name = (typeof body.name === 'string' ? body.name : '').trim().slice(0, 60);
    if (!name) return json(400, { ok: false, error: 'Give the view a name.' });
    const stateJson = JSON.stringify(body.state ?? {});
    if (stateJson.length > MAX_VIEW_STATE_BYTES) {
        return json(400, { ok: false, error: 'View state is too large.' });
    }

    const views = (await env.AUTH_KV.get(`views:${user.email}`, 'json')) || [];
    if (views.length >= MAX_VIEWS_PER_USER) {
        return json(400, { ok: false, error: `Limit of ${MAX_VIEWS_PER_USER} saved views reached — delete one first.` });
    }
    const view = {
        id: crypto.randomUUID(),
        name,
        state: JSON.parse(stateJson),
        created: new Date().toISOString(),
    };
    views.push(view);
    await env.AUTH_KV.put(`views:${user.email}`, JSON.stringify(views));
    return json(200, { ok: true, view });
}

async function handleViewsDelete(request, env, path) {
    const user = await currentUser(request, env);
    if (!user) return json(401, { ok: false, error: 'Log in to use saved views.' });
    const id = path.slice('/api/views/'.length);
    if (!/^[a-f0-9-]{36}$/.test(id)) return json(400, { ok: false, error: 'Invalid view id.' });

    const views = (await env.AUTH_KV.get(`views:${user.email}`, 'json')) || [];
    const remaining = views.filter(v => v.id !== id);
    if (remaining.length === views.length) return json(404, { ok: false, error: 'View not found.' });
    await env.AUTH_KV.put(`views:${user.email}`, JSON.stringify(remaining));
    return json(200, { ok: true });
}

// ---------------------------------------------------------------------------
// Visit logging (anonymous beacon)
// ---------------------------------------------------------------------------
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
    const ip = clientIp(request);
    const cf = request.cf || {};

    if (rateLimited(`visit:${ip}`, 20)) {
        return json(429, { ok: false, error: 'rate limited' });
    }

    const body = (await readJsonBody(request, 2048)) || {};

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

    console.log(`[visit] ${JSON.stringify(visit)}`);

    // Optional: forward to a Google Form so entries land in a linked Sheet in
    // Drive. Configure the Form/entry IDs as Worker secrets
    // (`wrangler secret put GOOGLE_FORM_ID` etc.) — see wrangler.toml.
    if (env.GOOGLE_FORM_ID && env.GOOGLE_FORM_ENTRY_IP) {
        ctx.waitUntil(sendToGoogleForm(env, visit));
    }

    return json(200, { ok: true });
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
