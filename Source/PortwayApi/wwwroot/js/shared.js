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
