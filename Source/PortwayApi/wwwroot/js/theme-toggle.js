function _getTheme() {
  try { return JSON.parse(localStorage.getItem('beacon_settings') || '{}').theme || 'system'; } catch { return 'system'; }
}

function _isDark() {
  const t = _getTheme();
  return t === 'dark' || (t === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
}

function _themeIcon(dark) {
  return dark
    ? `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>`
    : `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>`;
}

function _paintThemeToggle() {
  const btn = document.getElementById('themeToggle');
  if (btn) btn.innerHTML = _themeIcon(_isDark());
}

function cycleTheme() {
  const next = _isDark() ? 'light' : 'dark';
  try {
    const st = JSON.parse(localStorage.getItem('beacon_settings') || '{}');
    st.theme = next;
    localStorage.setItem('beacon_settings', JSON.stringify(st));
  } catch {}
  document.documentElement.setAttribute('data-theme', next);
  _paintThemeToggle();
}

(function () {
  const t = _getTheme();
  if (t !== 'system') document.documentElement.setAttribute('data-theme', t);
})();

document.addEventListener('DOMContentLoaded', _paintThemeToggle);
