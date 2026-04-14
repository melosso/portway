// Shared utilities; included by every protected UI page.
// sidebar.js handles: renderSidebar(), logout(), sidebarVersion fetch.
// Toast functionality moved to /js/components/toast.js

// Sanitizer
function esc(s) {
  if (s == null) return '';
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// Toast - moved to /js/components/toast.js
// Include <script src="/js/components/toast.js"></script> in your page
// Requires: <div class="toast-container" id="toastContainer"></div>

// Password field show/hide toggle
function togglePasswordVis(inputId, btn) {
  const el = document.getElementById(inputId);
  if (!el) return;
  const showing = el.type === 'text';
  el.type = showing ? 'password' : 'text';
  btn.title = showing ? 'Show / hide' : 'Hide';
  btn.style.color = showing ? 'hsl(var(--muted-foreground))' : 'hsl(var(--foreground))';
}

// Animate a numeric counter from its current displayed value to a target.
// If target contains a non-numeric suffix (e.g. "3 (+1)"), the number is
// animated and the suffix appended when the animation completes.
function animateCounter(el, target, duration) {
  duration = duration || 380;
  var targetStr = String(target);
  var targetNum = parseInt(targetStr, 10);
  if (isNaN(targetNum) || targetNum === 0) { el.textContent = target; return; }
  var suffix = targetStr.slice(String(targetNum).length); // e.g. " (+1)" or ""
  var fromNum = parseInt(el.textContent, 10);
  if (isNaN(fromNum)) fromNum = 0;
  if (fromNum === targetNum) { el.textContent = target; return; }
  var start = performance.now();
  function step(now) {
    var p = Math.min((now - start) / duration, 1);
    var eased = 1 - Math.pow(1 - p, 3); // ease-out-cubic
    el.textContent = Math.round(fromNum + (targetNum - fromNum) * eased) + (p < 1 ? '' : suffix);
    if (p < 1) {
      requestAnimationFrame(step);
    } else {
      el.classList.add('pw-popped');
      setTimeout(function() { el.classList.remove('pw-popped'); }, 250);
    }
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

// Capture unhandled client-side errors and report them to the server log.
// Errors are rate-limited to avoid flooding.
(function () {
  var _reported = 0, _maxReports = 10;
  function reportError(msg, source, lineno, colno, err) {
    if (_reported >= _maxReports) return;
    _reported++;
    try {
      navigator.sendBeacon('/ui/api/client-error', JSON.stringify({
        message: String(msg).slice(0, 500),
        source: String(source || '').slice(0, 200),
        line: lineno,
        col: colno,
        stack: err && err.stack ? String(err.stack).slice(0, 1000) : null,
        url: location.pathname,
        ts: Date.now()
      }));
    } catch (_) {}
  }
  window.onerror = function(msg, source, lineno, colno, err) {
    reportError(msg, source, lineno, colno, err);
  };
  window.addEventListener('unhandledrejection', function(e) {
    reportError('Unhandled promise rejection: ' + (e.reason || ''), '', 0, 0, null);
  });
})();

// Auto-prepend PortwayBase to all absolute fetch paths so the UI works
// correctly when the app is hosted under a sub-path (PathBase) like /v1.
(function () {
  var _fetch = window.fetch;
  window.fetch = function (url, options) {
    if (typeof url === 'string' && url.startsWith('/') && window.PortwayBase) {
      url = window.PortwayBase + url;
    }
    return _fetch.call(this, url, options);
  };
})();
