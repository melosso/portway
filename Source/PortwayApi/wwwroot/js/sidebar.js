// Theme + sidebar init (runs synchronously before body paint) ─
(function () {
  try {
    var s = JSON.parse(localStorage.getItem('beacon_settings') || '{}');
    var theme = s.theme || 'system';
    if (theme !== 'system') document.documentElement.setAttribute('data-theme', theme);
    if (s.sidebarCollapsed) document.body.classList.add('sidebar-collapsed');
  } catch (e) {}
})();

// Data-driven sidebar, edit NAV_GROUPS to add, remove, or reorder nav items.
// The active item is auto-detected from location.pathname.
// Adding a new page: push an entry into the appropriate group and include this script.

const NAV_GROUPS = [
  {
    label: 'Navigation',
    items: [
      {
        href: '/ui/dashboard',
        label: 'Dashboard',
        icon: '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>',
      },
      {
        href: '/ui/endpoints',
        label: 'Endpoints',
        icon: '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>',
      },
      {
        href: '/ui/environments',
        label: 'Environments',
        icon: '<polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/>',
      },
      {
        href: '/ui/tokens',
        label: 'Access Tokens',
        icon: '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
      },
    ],
  },
  {
    label: 'System',
    items: [
      {
        href: '/ui/settings',
        label: 'Settings',
        icon: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
      },
      {
        href: '/ui/logs',
        label: 'Logs',
        icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/>',
      },
    ],
  },
  {
    label: 'Resources',
    items: [
      {
        href: 'https://github.com/melosso/portway/blob/main/LICENSE',
        label: 'License',
        icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/>',
        external: true,
      },
      {
        href: 'https://github.com/melosso/portway/blob/main/README.md',
        label: 'Documentation',
        icon: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>',
        external: true,
      },
    ],
  },
];

const BRAND_SVG = `
<svg viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg" class="brand-avatar-bg" aria-hidden="true">
  <g clip-path="url(#sb-clip)">
    <rect width="20" height="20" fill="#000" rx="5.5"/>
    <rect width="20" height="20" fill="url(#sb-grad)" fill-opacity="0.2" rx="5.5"/>
    <g filter="url(#sb-blur-1)" opacity="0.3"><circle cx="16" cy="17" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
    <g filter="url(#sb-blur-2)" opacity="0.1"><circle cx="16" cy="16" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
    <g filter="url(#sb-blur-3)" opacity="0.4"><circle cx="17" cy="19" r="6" fill="#FF64B4" fill-opacity="0.671"/></g>
    <rect width="20" height="20" fill="#FF64B4" fill-opacity="0.15" rx="5.5"/>
    <g style="mix-blend-mode:hard-light"><rect width="20" height="20" fill="#6A62FF" fill-opacity="0.1" rx="5.5"/></g>
  </g>
  <rect width="19" height="19" x="0.5" y="0.5" stroke="#FDFDFD" stroke-opacity="0.1" rx="5"/>
  <defs>
    <filter id="sb-blur-1" width="32" height="32" x="0" y="1" color-interpolation-filters="sRGB" filterUnits="userSpaceOnUse"><feFlood flood-opacity="0" result="BackgroundImageFix"/><feBlend in="SourceGraphic" in2="BackgroundImageFix" result="shape"/><feGaussianBlur result="effect1_foregroundBlur" stdDeviation="5"/></filter>
    <filter id="sb-blur-2" width="22" height="22" x="5" y="5" color-interpolation-filters="sRGB" filterUnits="userSpaceOnUse"><feFlood flood-opacity="0" result="BackgroundImageFix"/><feBlend in="SourceGraphic" in2="BackgroundImageFix" result="shape"/><feGaussianBlur result="effect1_foregroundBlur" stdDeviation="2.5"/></filter>
    <filter id="sb-blur-3" width="22" height="22" x="6" y="8" color-interpolation-filters="sRGB" filterUnits="userSpaceOnUse"><feFlood flood-opacity="0" result="BackgroundImageFix"/><feBlend in="SourceGraphic" in2="BackgroundImageFix" result="shape"/><feGaussianBlur result="effect1_foregroundBlur" stdDeviation="2.5"/></filter>
    <linearGradient id="sb-grad" x1="10" x2="10" y1="0" y2="20" gradientUnits="userSpaceOnUse"><stop stop-color="#FDFDFD"/><stop offset="1" stop-color="#FDFDFD" stop-opacity="0"/></linearGradient>
    <clipPath id="sb-clip"><rect width="20" height="20" fill="#FDFDFD" rx="5.5"/></clipPath>
  </defs>
</svg>
<svg class="brand-avatar-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#FDFDFD" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
  <circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/>
  <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"/><line x1="15.41" y1="6.51" x2="8.59" y2="10.49"/>
</svg>`;

const GITHUB_SVG = `<svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>`;

function icon(paths) {
  return `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${paths}</svg>`;
}

function renderSidebar(mcpEnabled) {
  const aside = document.querySelector('aside');
  if (!aside) return;

  const currentPath = location.pathname.replace(/\/$/, '');
  const base = window.PortwayBase || '';

  let groups = [...NAV_GROUPS];

  // Add MCP group if enabled
  if (mcpEnabled) {
    groups.splice(1, 0, {
      label: 'MCP',
      items: [
        {
          href: '/ui/mcp/chat',
          label: 'Chat',
          icon: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>',
        },
        {
          href: '/ui/mcp/explorer',
          label: 'Explorer',
          icon: '<circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
        },
      ],
    });
  }

  const groupsHtml = groups.map((group, i) => {
    const sep = i > 0 ? '<div class="nav-sep"></div>' : '';
    const items = group.items.map(item => {
      const href   = item.external ? item.href : base + item.href;
      const active = !item.external && (currentPath === href || currentPath.startsWith(href + '/'));
      const ext    = item.external ? ' target="_blank" rel="noopener"' : '';
      return `<a href="${href}"${ext} title="${item.label}" class="nav-item${active ? ' active' : ''}">${icon(item.icon)}<span>${item.label}</span></a>`;
    }).join('\n    ');
    return `${sep}<div class="nav-group">
    <div class="nav-label">${group.label}</div>
    ${items}
  </div>`;
  }).join('\n  ');

  const currentTheme = _getTheme();
  const isDarkNow = currentTheme === 'dark' ||
    (currentTheme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  const nextTheme = isDarkNow ? 'light' : 'dark';

  aside.innerHTML = `
  <div class="brand select-none" onclick="toggleSidebar()" style="cursor:pointer;" title="Toggle sidebar">
    <div class="brand-avatar">${BRAND_SVG}</div>
    <span class="brand-name">Portway</span>
  </div>

  ${groupsHtml}

  <div class="sidebar-footer">
    <div class="sidebar-footer-meta">
      <div class="sidebar-version" id="sidebarVersion"></div>
      <span class="tooltip-wrapper">
        <a href="https://github.com/melosso/portway" target="_blank" rel="noopener" class="github-link">${GITHUB_SVG}</a>
        <span class="tooltip tooltip-above tooltip-right">View on GitHub</span>
      </span>
      <span class="tooltip-wrapper">
        <button class="theme-toggle" onclick="cycleTheme()">${_themeIcon(currentTheme)}</button>
        <span class="tooltip tooltip-above tooltip-right" id="themeTooltip">Theme: ${nextTheme}</span>
      </span>
    </div>
    <button class="btn-logout" onclick="logout()">Log Out</button>
  </div>`;
}

// Fetch settings + overview, then render
Promise.all([
  fetch((window.PortwayBase || '') + '/ui/api/overview').then(r => r.json()),
  fetch((window.PortwayBase || '') + '/ui/api/settings').then(r => r.json()).catch(() => ({ mcp: { enabled: false } }))
]).then(([d, settings]) => {
  const mcpEnabled = settings?.mcp?.enabled ?? false;

  if (!mcpEnabled && location.pathname.startsWith((window.PortwayBase || '') + '/ui/mcp')) {
    location.href = (window.PortwayBase || '') + '/ui/dashboard';
    return;
  }

  renderSidebar(mcpEnabled);

  const v = (d.version ?? '').split('+')[0];
  const el = document.getElementById('sidebarVersion');
  if (el && v) {
    const label = `v${v}`;
    const maxLen = 12;
    if (label.length > maxLen) {
      el.textContent = label.slice(0, maxLen) + '…';
      el.title = label;
      el.style.cursor = 'default';
    } else {
      el.textContent = label;
    }
  }

  if (d.openapi_enabled) {
    const meta = document.querySelector('.sidebar-footer-meta');
    if (meta) {
      const link = document.createElement('span');
      link.className = 'tooltip-wrapper';
      link.innerHTML = `<a href="${window.PortwayBase || ''}/docs" target="_blank" rel="noopener" class="github-link" aria-label="API Documentation">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
          <polyline points="14 2 14 8 20 8"></polyline>
          <line x1="16" y1="13" x2="8" y2="13"></line>
          <line x1="16" y1="17" x2="8" y2="17"></line>
          <polyline points="10 9 9 9 8 9"></polyline>
        </svg>
      </a>
      <span class="tooltip tooltip-above tooltip-right">API Documentation</span>`;
      const githubWrapper = meta.querySelector('.tooltip-wrapper');
      meta.insertBefore(link, githubWrapper);
    }
  }
}).catch(() => {
  renderSidebar(false);
});

function logout() {
  var base = window.PortwayBase || '';
  fetch(base + '/ui/api/auth/logout', { method: 'POST', credentials: 'include' })
    .finally(() => { window.location.href = base + '/ui/login'; });
}

// Sidebar collapse 
function toggleSidebar() {
  const collapsed = document.body.classList.contains('sidebar-collapsed');
  if (collapsed) {
    // Thisw'l apply blur during transition
    document.body.classList.add('sidebar-expanding');
    document.body.classList.remove('sidebar-collapsed');
    setTimeout(() => document.body.classList.remove('sidebar-expanding'), 240);
  } else {
    document.body.classList.add('sidebar-collapsed');
  }
  try {
    const s = JSON.parse(localStorage.getItem('beacon_settings') || '{}');
    s.sidebarCollapsed = !collapsed;
    localStorage.setItem('beacon_settings', JSON.stringify(s));
  } catch {}
}

// Theme 
function _getTheme() {
  try { return JSON.parse(localStorage.getItem('beacon_settings') || '{}').theme || 'system'; } catch { return 'system'; }
}

function _saveTheme(t) {
  try {
    var s = JSON.parse(localStorage.getItem('beacon_settings') || '{}');
    s.theme = t;
    localStorage.setItem('beacon_settings', JSON.stringify(s));
  } catch {}
}

function cycleTheme() {
  const current = _getTheme();
  // Determine what is *visually* active right now (system resolves via media query)
  const isDark = current === 'dark' ||
    (current === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  const next = isDark ? 'light' : 'dark';
  _saveTheme(next);
  document.documentElement.setAttribute('data-theme', next);
  const btn = document.querySelector('.theme-toggle');
  if (btn) { btn.innerHTML = _themeIcon(next); }
  const tip = document.getElementById('themeTooltip');
  if (tip) { tip.textContent = `Theme: ${next === 'dark' ? 'light' : 'dark'}`; }
}

function _themeIcon(theme) {
  if (theme === 'dark')
    return `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>`;
  if (theme === 'light')
    return `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>`;
  // system
  return `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>`;
}

// Render immediately (script is in <head>, so wait for DOM)
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', renderSidebar);
} else {
  renderSidebar();
}
