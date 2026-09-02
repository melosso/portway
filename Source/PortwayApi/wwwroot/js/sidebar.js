// Theme + sidebar init (runs synchronously before body paint) ─
(function () {
  try {
    var s = JSON.parse(localStorage.getItem('beacon_settings') || '{}');
    var theme = s.theme || 'system';
    if (theme !== 'system') document.documentElement.setAttribute('data-theme', theme);
    if (s.sidebarCollapsed) document.documentElement.classList.add('sidebar-collapsed');
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
        href: '/ui/logs',
        label: 'Logs',
        icon: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/>',
      },
      {
        href: '/ui/users',
        label: 'Users',
        icon: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
      },
      {
        href: '/ui/settings',
        label: 'Settings',
        children: [
          { hash: 'security',     label: 'Security' },
          { hash: 'performance',  label: 'Performance' },
          { hash: 'storage',      label: 'Storage & Logs' },
          { hash: 'integrations', label: 'Integrations' },
        ],
        icon: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
      },
    ],
  },
  {
    label: 'Resources',
    items: [
      {
        href: 'https://melosso.github.io/portway/',
        label: 'Help Center',
        icon: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>',
        external: true,
      },
    ],
  },
];

const NAV_CHEVRON = `<svg class="nav-chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>`;

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


function icon(paths) {
  return `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">${paths}</svg>`;
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

  const groupsHtml = groups.map((group) => {
    const items = group.items.map(item => {
      const href   = item.external ? item.href : base + item.href;
      const active = !item.external && (currentPath === href || currentPath.startsWith(href + '/'));
      const ext    = item.external ? ' target="_blank" rel="noopener"' : '';
      const extBadge = item.external
        ? `<svg class="nav-external" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>`
        : '';
      const row = `<a href="${href}"${ext} title="${item.label}" class="nav-item${active ? ' active' : ''}">${icon(item.icon)}<span>${item.label}</span>${extBadge}${item.children ? NAV_CHEVRON : ''}</a>`;
      if (!item.children || !active) return row;
      const current = (location.hash || '').replace('#', '') || item.children[0].hash;
      const kids = item.children.map(c =>
        `<a href="${href}#${c.hash}" class="nav-item nav-child${c.hash === current ? ' active' : ''}" data-section="${c.hash}"><span>${c.label}</span></a>`
      ).join('\n      ');
      return `${row}\n    <div class="nav-kids">\n      ${kids}\n    </div>`;
    }).join('\n    ');
    return `<div class="nav-group">
    <div class="nav-label">${group.label}</div>
    ${items}
  </div>`;
  }).join('\n  ');

  aside.innerHTML = `
  <div class="sidebar-brand">
  <button type="button" class="brand select-none" onclick="toggleSidebar()" title="Toggle sidebar" aria-expanded="true">
    <div class="brand-avatar">${BRAND_SVG}</div>
    <span class="brand-name">Portway</span>
    <svg class="brand-collapse" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>
  </button>
  </div>

  <nav class="side-nav">
  ${groupsHtml}
  </nav>

  <div class="sidebar-footer">
    <button type="button" class="account-trigger" onclick="toggleAccountMenu(event)" title="Account">
      <span class="avatar" id="sidebarAvatar"></span>
      <span class="account-meta">
        <span class="account-name" id="sidebarAccountName">Portway</span>
        <span class="account-version" id="sidebarVersion"></span>
      </span>
    </button>
    <div class="account-menu hidden" id="accountMenu" role="menu" aria-label="Account">
      <div class="account-menu-head">
        <strong id="accountMenuName">Portway</strong>
        <span id="accountMenuServer"></span>
      </div>
      <div class="account-menu-sep" role="separator"></div>
      <div class="account-menu-nest">
        <button type="button" role="menuitem" id="appearanceTrigger" aria-haspopup="menu" aria-expanded="false" onclick="toggleAppearance(event)">
          Appearance
          <svg class="account-menu-chevron" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>
        </button>
        <div class="account-submenu hidden" id="appearanceMenu" role="menu" aria-label="Appearance">
          <button type="button" role="menuitemradio" data-appearance="light" onclick="chooseAppearance('light', event)">Light</button>
          <button type="button" role="menuitemradio" data-appearance="dark" onclick="chooseAppearance('dark', event)">Dark</button>
          <button type="button" role="menuitemradio" data-appearance="system" onclick="chooseAppearance('system', event)">System</button>
        </div>
      </div>
      <div class="account-menu-sep" role="separator"></div>
      <div id="accountMenuDocs"></div>
      <a href="https://github.com/melosso/portway" target="_blank" rel="noopener" role="menuitem">View on GitHub</a>
      <div class="account-menu-sep" role="separator"></div>
      <button type="button" role="menuitem" onclick="logout()">Sign out</button>
    </div>
  </div>`;

  mountChrome(_openapiEnabled);
}

let _openapiEnabled = false;

// Fetch settings + overview, then render
Promise.all([
  api('/ui/api/overview', { silent: true }),
  api('/ui/api/settings', { silent: true }).catch(() => ({ mcp: { enabled: false } })),
  api('/ui/api/users/me', { silent: true }).catch(() => ({ signed_in: false }))
]).then(([d, settings, me]) => {
  const mcpEnabled = settings?.mcp?.enabled ?? false;

  if (!mcpEnabled && location.pathname.startsWith((window.PortwayBase || '') + '/ui/mcp')) {
    location.href = (window.PortwayBase || '') + '/ui/dashboard';
    return;
  }

  _openapiEnabled = d.openapi_enabled ?? false;
  renderSidebar(mcpEnabled);
  if (_openapiEnabled) mountDocsButton();

  const v = (d.version ?? '').split('+')[0];
  const el = document.getElementById('sidebarVersion');
  if (el && v) {
    const label = `v${v}`;
    const maxLen = 20;
    if (label.length > maxLen) {
      el.textContent = label.slice(0, maxLen) + '…';
      el.title = label;
      el.style.cursor = 'default';
    } else {
      el.textContent = label;
    }
  }

  const server = document.getElementById('accountMenuServer');
  if (server) server.textContent = d.server_name ?? location.host;

  paintAccount(me);

  if (d.openapi_enabled) {
    const docs = document.getElementById('accountMenuDocs');
    if (docs) {
      const link = document.createElement('a');
      link.href = `${window.PortwayBase || ''}/docs`;
      link.target = '_blank';
      link.rel = 'noopener';
      link.setAttribute('role', 'menuitem');
      link.textContent = 'OpenAPI';
      docs.append(link);
    }
  }
}).catch(() => {
  renderSidebar(false);
});

function logout() {
  api('/ui/api/auth/logout', { method: 'POST', silent: true })
    .catch(() => {}).finally(() => { window.location.href = (window.PortwayBase || '') + '/ui/login'; });
}

// Mobile drawer
const MENU_SVG = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="18" x2="21" y2="18"/></svg>`;

function isMobileNav() {
  return window.matchMedia('(max-width: 768px)').matches;
}

function openNav() {
  document.body.classList.add('nav-open');
}

function closeNav() {
  document.body.classList.remove('nav-open');
}

const DOCS_SVG = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/></svg>`;
const THEME_SVG = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 12a4 4 0 1 0 8 0 4 4 0 1 0-8 0M3 12h1m8-9v1m8 8h1m-9 8v1M5.6 5.6l.7.7m12.1-.7-.7.7m0 11.4.7.7m-12.1-.7-.7.7"/></svg>`;

function mountChrome(openapiEnabled) {
  if (!document.querySelector('.nav-scrim')) {
    const scrim = document.createElement('div');
    scrim.className = 'nav-scrim';
    scrim.addEventListener('click', closeNav);
    document.body.append(scrim);
  }

  const main = document.querySelector('main');
  if (!main || main.querySelector('.topbar')) return;

  const bar = document.createElement('header');
  bar.className = 'topbar';
  bar.innerHTML = `
    <button type="button" class="topbar-btn nav-trigger" aria-label="Toggle navigation" aria-expanded="true">${MENU_SVG}</button>
    <nav class="breadcrumb" id="breadcrumb" aria-label="Breadcrumb"></nav>
    <div class="topbar-actions">
      <button type="button" class="topbar-btn" id="topbarTheme" data-tooltip="Toggle theme">${THEME_SVG}</button>
    </div>`;
  main.prepend(bar);

  bar.querySelector('.nav-trigger').addEventListener('click', toggleSidebar);
  bar.querySelector('#topbarTheme').addEventListener('click', toggleTheme);
  if (openapiEnabled) mountDocsButton();

  renderBreadcrumb();
}

function mountDocsButton() {
  const actions = document.querySelector('.topbar-actions');
  if (!actions || document.getElementById('topbarDocs')) return;
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'topbar-btn';
  btn.id = 'topbarDocs';
  btn.setAttribute('data-tooltip', 'API reference');
  btn.innerHTML = DOCS_SVG;
  btn.addEventListener('click', () => {
    window.open((window.PortwayBase || '') + '/docs', '_blank', 'noopener');
  });
  actions.prepend(btn);
}

function paintAccount(me) {
  if (!me?.signed_in) return;

  const name = me.username ?? 'Portway';
  const nameEl = document.getElementById('sidebarAccountName');
  if (nameEl) { nameEl.textContent = name; nameEl.title = name; }

  const headEl = document.getElementById('accountMenuName');
  if (headEl) headEl.textContent = name;

  const avatar = document.getElementById('sidebarAvatar');
  if (!avatar) return;
  avatar.innerHTML = me.avatar
    ? `<img src="${me.avatar}" alt="" width="32" height="32">`
    : '';
  if (!me.avatar) avatar.textContent = name.charAt(0).toUpperCase();
}

function renderBreadcrumb() {
  const el = document.getElementById('breadcrumb');
  if (!el) return;
  const active = document.querySelector('.nav-item.active');
  const group = active?.closest('.nav-group')?.querySelector('.nav-label')?.textContent?.trim();
  const page = active?.querySelector('span')?.textContent?.trim() ?? document.title;
  const crumbs = group && group.toLowerCase() !== 'navigation' ? [group, page] : [page];
  el.innerHTML = crumbs.map(c => `<span class="crumb">${esc(c)}</span>`).join('');
}

document.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  closeAccountMenu();
  closeNav();
});

// Sidebar collapse 
function setNavExpanded(open) {
  document.querySelectorAll('.brand, .nav-trigger').forEach(el => el.setAttribute('aria-expanded', String(open)));
}

function toggleSidebar() {
  if (isMobileNav()) {
    document.body.classList.contains('nav-open') ? closeNav() : openNav();
    return;
  }
  const collapsed = document.documentElement.classList.contains('sidebar-collapsed');
  if (collapsed) {
    // Thisw'l apply blur during transition
    document.body.classList.add('sidebar-expanding');
    document.documentElement.classList.remove('sidebar-collapsed');
    setTimeout(() => document.body.classList.remove('sidebar-expanding'), 240);
  } else {
    document.documentElement.classList.add('sidebar-collapsed');
  }
  setNavExpanded(collapsed);
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

function toggleTheme() {
  const current = _getTheme();
  const isDark = current === 'dark' ||
    (current === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  setTheme(isDark ? 'light' : 'dark');
  markAppearance();
}

function setTheme(choice) {
  _saveTheme(choice);
  if (choice === 'system') document.documentElement.removeAttribute('data-theme');
  else document.documentElement.setAttribute('data-theme', choice);
}

function toggleAccountMenu(e) {
  if (e) e.stopPropagation();
  const menu = document.getElementById('accountMenu');
  if (!menu) return;
  const opening = menu.classList.contains('hidden');
  closeAppearance();
  menu.classList.toggle('hidden', !opening);
}

function closeAccountMenu() {
  document.getElementById('accountMenu')?.classList.add('hidden');
  closeAppearance();
}

function closeAppearance() {
  document.getElementById('appearanceMenu')?.classList.add('hidden');
  document.getElementById('appearanceTrigger')?.setAttribute('aria-expanded', 'false');
}

function toggleAppearance(e) {
  if (e) e.stopPropagation();
  const menu = document.getElementById('appearanceMenu');
  if (!menu) return;
  const opening = menu.classList.contains('hidden');
  menu.classList.toggle('hidden', !opening);
  document.getElementById('appearanceTrigger')?.setAttribute('aria-expanded', String(opening));
  if (opening) markAppearance();
}

function markAppearance() {
  const chosen = _getTheme();
  document.querySelectorAll('#appearanceMenu [data-appearance]').forEach(btn => {
    const on = btn.dataset.appearance === chosen;
    btn.classList.toggle('checked', on);
    btn.setAttribute('aria-checked', String(on));
  });
}

function chooseAppearance(next, e) {
  if (e) e.stopPropagation();
  setTheme(next);
  markAppearance();
  closeAccountMenu();
}

document.addEventListener('click', () => closeAccountMenu());

// Render immediately (script is in <head>, so wait for DOM)
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', renderSidebar);
} else {
  renderSidebar();
}
