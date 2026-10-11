// Shared utilities for every console page

// CSRF: echo the portway_csrf cookie on every mutating /ui/api request (double-submit pattern)
(function () {
    function csrfToken() {
        var m = document.cookie.match(/(?:^|;\s*)portway_csrf=([^;]+)/);
        return m ? decodeURIComponent(m[1]) : null;
    }
    var origFetch = window.fetch;
    window.fetch = function (input, init) {
        try {
            var url = typeof input === 'string' ? input : (input && input.url) || '';
            var method = ((init && init.method) || (input && input.method) || 'GET').toUpperCase();
            if (method !== 'GET' && method !== 'HEAD' && url.indexOf('/ui/api/') !== -1) {
                var token = csrfToken();
                if (token) {
                    init = init || {};
                    var headers = new Headers(init.headers || (typeof input !== 'string' && input.headers) || {});
                    if (!headers.has('X-CSRF-Token')) headers.set('X-CSRF-Token', token);
                    init.headers = headers;
                }
            }
        } catch (e) {
            /* never block the request */
        }
        return origFetch.call(this, input, init);
    };
})();

const ROLE_LABELS = { administrator: 'Administrator', viewer: 'Viewer' };

// Sanitizer
function esc(s) {
    if (s == null) return '';
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

const STATUS_ICON_PATHS = {
    ok: '<path d="M20 6 9 17l-5-5"/>',
    warn: '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
    bad: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>',
    unknown: '<circle cx="12" cy="12" r="9"/>'
};

// status icon whose shape and colour follow the state; the tooltip renders html, so the label is escaped twice
function statusIcon(state, label, side = 'top') {
    return `<span class="status-icon ${state}" role="img" tabindex="0" aria-label="${esc(label)}" data-tooltip="${esc(esc(label))}" data-tooltip-side="${side}"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.25" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${STATUS_ICON_PATHS[state] ?? STATUS_ICON_PATHS.unknown}</svg></span>`;
}

async function api(url, options = {}) {
    const { method = 'GET', body, success, failure = 'Something went wrong.', silent = false } = options;
    const init = { method };
    if (body !== undefined) {
        init.headers = { 'Content-Type': 'application/json' };
        init.body = typeof body === 'string' ? body : JSON.stringify(body);
    }
    let res;
    try {
        res = await fetch(url, init);
    } catch (e) {
        if (!silent) toast(failure, 'error');
        throw e;
    }
    let data = null;
    try {
        data = await res.json();
    } catch (_) {}
    if (!res.ok) {
        const message = (data && (data.error || data.message)) || failure;
        if (!silent) toast(message, 'error');
        const err = new Error(message);
        err.status = res.status;
        err.field = data && data.field;
        throw err;
    }
    if (success) toast(success, 'success');
    return data ?? {};
}

// Password field show/hide toggle
function togglePasswordVis(inputId, btn) {
    const el = document.getElementById(inputId);
    if (!el) return;
    const showing = el.type === 'text';
    el.type = showing ? 'password' : 'text';
    btn.title = showing ? 'Show / hide' : 'Hide';
    btn.style.color = showing ? 'hsl(var(--muted-foreground))' : 'hsl(var(--foreground))';
}

// Animates a counter to its target; a non-numeric suffix is appended at the end
function animateCounter(el, target, duration) {
    duration = duration || 380;
    var targetStr = String(target);
    var targetNum = parseInt(targetStr, 10);
    if (isNaN(targetNum) || targetNum === 0) {
        el.textContent = target;
        return;
    }
    var suffix = targetStr.slice(String(targetNum).length); // e.g. " (+1)" or ""
    var fromNum = parseInt(el.textContent, 10);
    if (isNaN(fromNum)) fromNum = 0;
    if (fromNum === targetNum) {
        el.textContent = target;
        return;
    }
    var start = performance.now();
    function step(now) {
        var p = Math.min((now - start) / duration, 1);
        var eased = 1 - Math.pow(1 - p, 3); // ease-out-cubic
        el.textContent = Math.round(fromNum + (targetNum - fromNum) * eased) + (p < 1 ? '' : suffix);
        if (p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
}

// Console easter egg — shown once per session for curious developers.
(function () {
    if (sessionStorage.getItem('_pw_console')) return;
    sessionStorage.setItem('_pw_console', '1');
    console.log(
        '%c PORTWAY ',
        'background:#0f0f10;color:#f1f5f9;font-size:13px;font-weight:700;padding:3px 8px;border-radius:4px;letter-spacing:0.08em'
    );
    console.log(
        '%cAPI gateway running. All endpoints routing.\n%chttps://github.com/melosso/portway',
        'color:#64748b;font-size:11px',
        'color:#94a3b8;font-size:11px'
    );
})();

// Reports unhandled client errors to the server log, rate limited
(function () {
    var _reported = 0,
        _maxReports = 10;
    function reportError(msg, source, lineno, colno, err) {
        if (_reported >= _maxReports) return;
        _reported++;
        try {
            navigator.sendBeacon(
                '/ui/api/client-error',
                JSON.stringify({
                    message: String(msg).slice(0, 500),
                    source: String(source || '').slice(0, 200),
                    line: lineno,
                    col: colno,
                    stack: err && err.stack ? String(err.stack).slice(0, 1000) : null,
                    url: location.pathname,
                    ts: Date.now()
                })
            );
        } catch (_) {}
    }
    window.onerror = function (msg, source, lineno, colno, err) {
        reportError(msg, source, lineno, colno, err);
    };
    window.addEventListener('unhandledrejection', function (e) {
        reportError('Unhandled promise rejection: ' + (e.reason || ''), '', 0, 0, null);
    });
})();

// Prefix absolute fetch paths with PortwayBase for sub-path hosting
(function () {
    var _fetch = window.fetch;
    window.fetch = function (url, options) {
        if (typeof url === 'string' && url.startsWith('/') && window.PortwayBase) {
            url = window.PortwayBase + url;
        }
        return _fetch.call(this, url, options);
    };
})();

// MCP AI provider catalog; single source of truth for the settings and chat wizards.
const PROVIDERS = [
    {
        id: 'mistral',
        label: 'Mistral AI',
        model: 'mistral-medium-latest',
        hint: 'mistral-large-latest / mistral-medium-latest'
    },
    { id: 'anthropic', label: 'Anthropic', model: 'claude-sonnet-5', hint: 'claude-fable-5 / claude-opus-4-8' },
    { id: 'openai', label: 'OpenAI', model: 'gpt-5.5', hint: 'gpt-5.5 / gpt-5.6-terra / gpt-5.6-luna' },
    { id: 'gemini', label: 'Google Gemini', model: 'gemini-3.5-flash', hint: 'gemini-3.5-pro / gemini-3.5-flash' }
];
const PROVIDER_LABELS = Object.fromEntries(PROVIDERS.map((p) => [p.id, p.label]));
