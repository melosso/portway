// Toast notification component
// Usage: toast('Message', 'success' | 'error' | 'warning')
// Requires: <div class="toast-container" id="toastContainer"> in the page
(function(global) {
  'use strict';

  const DEFAULT_DURATION = 3500;
  const TRANSITION_DURATION = 200;

  const icons = {
    success: '<polyline points="20 6 9 17 4 12"/>',
    error: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>',
    warning: '<path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
    info: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>'
  };

  function esc(s) {
    if (s == null) return '';
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // Ensure the toast container has aria-live so screen readers announce notifications.
  function ensureAriaLive() {
    const c = document.getElementById('toastContainer');
    if (c && !c.getAttribute('aria-live')) {
      c.setAttribute('aria-live', 'polite');
      c.setAttribute('aria-atomic', 'false');
    }
  }

  function toast(msg, type = 'success', duration = DEFAULT_DURATION) {
    const c = document.getElementById('toastContainer');
    if (!c) {
      console.warn('Toast: toastContainer not found');
      return;
    }
    ensureAriaLive();

    const t = document.createElement('div');
    t.className = `toast ${type}`;
    // role="status" for success/info; role="alert" for error/warning (assertive)
    const role = (type === 'error' || type === 'warning') ? 'alert' : 'status';
    t.setAttribute('role', role);
    t.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[type] || icons.info}</svg><span>${esc(msg)}</span>`;
    
    c.appendChild(t);
    
    setTimeout(() => { 
      t.classList.add('hiding'); 
      setTimeout(() => t.remove(), TRANSITION_DURATION); 
    }, duration);
  }

  // Export to global
  global.toast = toast;

})(typeof window !== 'undefined' ? window : this);
