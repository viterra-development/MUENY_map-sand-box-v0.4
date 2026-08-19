// MUENY account client — session state, login/register modal, and gating
// helper. Plain script (not a module) so it can run before the app bootstraps
// and be shared by both the Blazor and JS-fallback paths.
// API (window.MuenyAuth):
//   me()                -> Promise<{email,name}|null>   (cached)
//   requireAuth(next)   -> runs next() if logged in, else opens the modal
//   showModal(mode)     -> 'login' | 'register'
//   logout()            -> Promise<void>
(function () {
    let cachedUser;            // undefined = unknown, null = anonymous
    let pendingAction = null;  // action to run after a successful login

    async function me(force = false) {
        if (cachedUser !== undefined && !force) return cachedUser;
        try {
            const res = await fetch('/api/auth/me', { credentials: 'same-origin' });
            cachedUser = res.ok ? (await res.json()).user : null;
        } catch (_) {
            cachedUser = null;
        }
        updateHeaderChip();
        return cachedUser;
    }

    async function logout() {
        try { await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }); } catch (_) {}
        cachedUser = null;
        updateHeaderChip();
    }

    function requireAuth(next) {
        me().then(user => {
            if (user) { next(); }
            else { pendingAction = next; showModal('login'); }
        });
    }

    // ------------------------------------------------------------------
    // Modal (DOM built with createElement/textContent — never innerHTML
    // with dynamic strings)
    // ------------------------------------------------------------------
    let modalEl = null;

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function field(labelText, type, name, autocomplete, placeholder) {
        const wrap = el('label', 'mueny-auth-field');
        wrap.appendChild(el('span', 'mueny-auth-label', labelText));
        const input = el('input');
        input.type = type;
        input.name = name;
        input.required = true;
        if (autocomplete) input.autocomplete = autocomplete;
        if (placeholder) input.placeholder = placeholder;
        wrap.appendChild(input);
        return wrap;
    }

    function showModal(mode) {
        closeModal();
        const overlay = el('div', 'mueny-auth-overlay');
        overlay.addEventListener('click', (e) => { if (e.target === overlay) closeModal(); });

        const card = el('div', 'mueny-auth-card');
        card.setAttribute('role', 'dialog');
        card.setAttribute('aria-modal', 'true');

        const close = el('button', 'mueny-auth-close', '×');
        close.setAttribute('aria-label', 'Close');
        close.addEventListener('click', closeModal);
        card.appendChild(close);

        card.appendChild(el('div', 'mueny-auth-logo', 'MUENY'));
        const title = el('h2', 'mueny-auth-title',
            mode === 'register' ? 'Create your account' : 'Welcome back');
        card.appendChild(title);
        card.appendChild(el('p', 'mueny-auth-sub',
            'Sign in to generate reports and save map views.'));

        const form = el('form', 'mueny-auth-form');
        form.noValidate = false;
        if (mode === 'register') {
            form.appendChild(field('Name', 'text', 'name', 'name', 'Jane Public Works'));
        }
        form.appendChild(field('Email', 'email', 'email', 'email', 'you@city.gov'));
        form.appendChild(field('Password', 'password', 'password',
            mode === 'register' ? 'new-password' : 'current-password',
            mode === 'register' ? 'At least 8 characters' : ''));

        const error = el('div', 'mueny-auth-error');
        error.setAttribute('role', 'alert');
        form.appendChild(error);

        const submit = el('button', 'mueny-auth-submit',
            mode === 'register' ? 'Create account' : 'Sign in');
        submit.type = 'submit';
        form.appendChild(submit);

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            error.textContent = '';
            submit.disabled = true;
            submit.textContent = mode === 'register' ? 'Creating…' : 'Signing in…';
            const data = Object.fromEntries(new FormData(form).entries());
            try {
                const res = await fetch(mode === 'register' ? '/api/auth/register' : '/api/auth/login', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'content-type': 'application/json' },
                    body: JSON.stringify(data),
                });
                const body = await res.json().catch(() => ({}));
                if (res.ok && body.ok) {
                    cachedUser = body.user;
                    updateHeaderChip();
                    closeModal();
                    const action = pendingAction;
                    pendingAction = null;
                    if (action) action();
                } else {
                    error.textContent = body.error || 'Something went wrong — try again.';
                }
            } catch (_) {
                error.textContent = 'Network error — check your connection and try again.';
            } finally {
                submit.disabled = false;
                submit.textContent = mode === 'register' ? 'Create account' : 'Sign in';
            }
        });
        card.appendChild(form);

        const switcher = el('p', 'mueny-auth-switch');
        switcher.appendChild(document.createTextNode(
            mode === 'register' ? 'Already have an account? ' : 'New to MUENY? '));
        const switchLink = el('button', 'mueny-auth-link',
            mode === 'register' ? 'Sign in' : 'Create an account');
        switchLink.type = 'button';
        switchLink.addEventListener('click', () =>
            showModal(mode === 'register' ? 'login' : 'register'));
        switcher.appendChild(switchLink);
        card.appendChild(switcher);

        overlay.appendChild(card);
        document.body.appendChild(overlay);
        modalEl = overlay;
        const firstInput = card.querySelector('input');
        if (firstInput) firstInput.focus();
        document.addEventListener('keydown', escToClose);
    }

    function escToClose(e) { if (e.key === 'Escape') closeModal(); }

    function closeModal() {
        if (modalEl) { modalEl.remove(); modalEl = null; }
        document.removeEventListener('keydown', escToClose);
    }

    // ------------------------------------------------------------------
    // Header account chip (shared static header in index.html)
    // ------------------------------------------------------------------
    function updateHeaderChip() {
        const header = document.getElementById('terravex-header');
        if (!header) return;
        let chip = document.getElementById('mueny-account-chip');
        if (!chip) {
            chip = el('button', 'mueny-account-chip');
            chip.id = 'mueny-account-chip';
            header.appendChild(chip);
        }
        chip.replaceChildren();
        if (cachedUser) {
            chip.appendChild(el('span', 'mueny-chip-dot'));
            chip.appendChild(document.createTextNode(cachedUser.name || cachedUser.email));
            chip.title = 'Click to sign out';
            chip.onclick = () => { if (confirm('Sign out of MUENY?')) logout(); };
        } else {
            chip.appendChild(document.createTextNode('Sign in'));
            chip.title = 'Sign in or create an account';
            chip.onclick = () => showModal('login');
        }
    }

    window.MuenyAuth = { me, requireAuth, showModal, logout };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => me());
    } else {
        me();
    }
})();
