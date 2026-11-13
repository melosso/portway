// Sign-in page: CSRF, identity providers, password sign-in and the first-sign-in password change
let csrfToken = '';

async function initCsrf() {
    try {
        const r = await fetch('/_/api/auth/csrf');
        if (r.ok) {
            const d = await r.json();
            csrfToken = d.csrf;
            if (d.setup_pending)
                document.getElementById('signinIntro').textContent =
                    'Find the account name and one-time password in the startup log.';
        }
    } catch (e) {
        console.error('Failed to fetch CSRF token:', e);
    }
}
let csrfReady = initCsrf();

async function initProviders() {
    const base = window.PortwayBase || '';
    let providers = [];
    try {
        const r = await fetch('/_/api/auth/providers');
        if (!r.ok) return;
        providers = (await r.json()).providers || [];
    } catch (e) {
        return;
    }
    if (!providers.length) return;
    const host = document.getElementById('ssoProviders');
    for (const p of providers) {
        const a = document.createElement('a');
        a.className = 'btn btn-outline sso-btn';
        a.rel = 'nofollow';
        a.href = base + '/_/api/auth/oidc/' + encodeURIComponent(p.slug) + '/start';
        a.textContent = 'Continue with ' + p.name;
        host.append(a);
    }
    document.getElementById('ssoBlock').hidden = false;
}
initProviders();

// A federated sign-in that was refused comes back here with a reason
(function () {
    const reason = new URLSearchParams(location.search).get('sso');
    if (!reason) return;
    const messages = {
        failed: 'Sign-in failed. See the server log.',
        denied: 'Sign-in cancelled.',
        no_account: 'Account not permitted for this console.',
        inactive: 'Account inactive.'
    };
    const err = document.getElementById('authError');
    err.textContent = messages[reason] ?? messages.failed;
    err.hidden = false;
    history.replaceState(null, '', location.pathname);
})();

let _changing = false;

async function signIn() {
    const username = document.getElementById('usernameInput').value;
    const password = document.getElementById('passwordInput').value;
    const newPassword = document.getElementById('newPasswordInput').value;
    const remember = document.getElementById('rememberInput').checked;
    const btn = document.getElementById('signInBtn');
    const err = document.getElementById('authError');
    err.hidden = true;
    btn.classList.add('is-loading');
    btn.disabled = true;
    try {
        // a cold start answers the token request after the first submit
        await csrfReady;
        const r = await fetch(_changing ? '/_/api/auth/password' : '/_/api/auth', {
            method: 'POST',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password, newPassword, remember, csrf: csrfToken })
        });
        const d = await r.json().catch(() => ({}));

        // A first sign-in stops here: the account picks its own password before it gets a session
        if (r.ok && d.must_change_password) {
            _changing = true;
            csrfToken = d.csrf;
            document.getElementById('newPasswordField').hidden = false;
            document.querySelector('.signin-header p').textContent = 'Set a new password to complete sign-in.';
            document.querySelector('#signInBtn .btn-label').textContent = 'Set password';
            err.hidden = true;
            btn.classList.remove('is-loading');
            btn.disabled = false;
            document.getElementById('newPasswordInput').focus();
            return;
        }

        if (r.ok) {
            window.location.href = (window.PortwayBase || '') + '/_/dashboard';
        } else {
            err.textContent = d.error || 'Invalid username or password';
            err.hidden = false;
            btn.classList.remove('is-loading');
            btn.disabled = false;
            document.getElementById('passwordInput').focus();
            csrfReady = initCsrf();
        }
    } catch (e) {
        toast('Connection error. Server unreachable.', 'error');
        btn.classList.remove('is-loading');
        btn.disabled = false;
    }
}

document.getElementById('signinForm').addEventListener('submit', (e) => {
    e.preventDefault();
    signIn();
});
