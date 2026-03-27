// setup-guide.js — first-run setup checklist for the Portway dashboard.
// Shows once per browser until dismissed. Reads /ui/api/overview to determine
// which setup steps are complete.
(function () {
  'use strict';

  const DISMISSED_KEY = 'portway_setup_dismissed';

  // ── Public API ─────────────────────────────────────────────────────────────
  window.SetupGuide = {
    /** Mount the guide into the element with the given id, if not yet dismissed. */
    init(containerId) {
      if (localStorage.getItem(DISMISSED_KEY)) return;
      const el = document.getElementById(containerId);
      if (!el) return;
      _mount(el);
      _load();
    },
    /** Animate out and permanently dismiss. */
    dismiss() {
      localStorage.setItem(DISMISSED_KEY, '1');
      const root = document.getElementById('_sgRoot');
      if (!root) return;
      root.style.transition = 'opacity .15s ease, max-height .22s ease, margin-bottom .22s ease';
      root.style.opacity = '0';
      root.style.maxHeight = '0';
      root.style.marginBottom = '0';
      root.style.overflow = 'hidden';
      setTimeout(() => root.remove(), 240);
    }
  };

  // ── Mount ──────────────────────────────────────────────────────────────────
  function _mount(container) {
    const root = document.createElement('div');
    root.id = '_sgRoot';
    root.style.cssText = 'margin-bottom:1.25rem;max-height:600px';
    root.innerHTML =
      '<div class="card">' +
        '<div class="card-header">' +
          '<span class="card-title" style="font-size:0.82rem">Getting started</span>' +
          '<button onclick="SetupGuide.dismiss()" style="background:none;border:none;cursor:pointer;' +
            'font-size:0.75rem;color:hsl(var(--muted-foreground));padding:0;line-height:1;' +
            'text-decoration:underline;text-underline-offset:2px">Dismiss</button>' +
        '</div>' +
        '<div class="card-body" id="_sgSteps">' + _skeleton() + '</div>' +
      '</div>';
    container.appendChild(root);
  }

  function _skeleton() {
    return [140, 180, 165].map(function (w) {
      return '<div style="display:flex;align-items:center;gap:0.75rem;padding:0.38rem 0">' +
        '<div style="width:16px;height:16px;border-radius:50%;background:hsl(var(--muted));flex-shrink:0"></div>' +
        '<div style="height:11px;width:' + w + 'px;background:hsl(var(--muted));border-radius:3px"></div>' +
        '</div>';
    }).join('');
  }

  // ── Load ───────────────────────────────────────────────────────────────────
  async function _load() {
    try {
      const d = await fetch('/ui/api/overview').then(function (r) { return r.json(); });
      const hasEnvs = (d.environments ?? 0) > 0;
      const hasEps  = (d.endpoints?.total ?? 0) > 0;
      _render(hasEnvs, hasEps);
    } catch (_) {
      _render(null, null);
    }
  }

  // ── Render ─────────────────────────────────────────────────────────────────
  function _render(hasEnvs, hasEps) {
    const el = document.getElementById('_sgSteps');
    if (!el) return;

    const steps = [
      {
        done: true,
        label: 'Portway is running'
      },
      {
        done: hasEnvs,
        label: 'Environments configured',
        hint: 'Add at least one environment to route requests.',
        link: '/ui/environments',
        linkLabel: 'Open Environments →'
      },
      {
        done: hasEps,
        label: 'Endpoints registered',
        hint: 'Create an entity.json in the endpoints/ directory.',
        link: '/ui/endpoints',
        linkLabel: 'Open Endpoints →'
      }
    ];

    const allDone = steps.every(function (s) { return s.done === true; });

    el.innerHTML = steps.map(_stepHtml).join('') +
      (allDone
        ? '<div style="margin-top:0.75rem;padding-top:0.75rem;border-top:1px solid hsl(var(--border));' +
            'font-size:0.75rem;color:hsl(var(--muted-foreground))">' +
            'The dashboard updates in real time as traffic flows through Portway. ' +
            '<button onclick="SetupGuide.dismiss()" style="background:none;border:none;cursor:pointer;' +
              'font-size:0.75rem;color:hsl(var(--foreground));text-decoration:underline;' +
              'text-underline-offset:2px;padding:0">Dismiss this guide</button>' +
          '</div>'
        : '');
  }

  function _stepHtml(s) {
    const icon = s.done === true  ? _iconDone()
               : s.done === false ? _iconPending()
               :                    _iconUnknown();

    const labelColor  = s.done ? 'hsl(var(--muted-foreground))' : 'hsl(var(--foreground))';
    const labelWeight = s.done ? '400' : '500';

    const hint = (s.done === false && s.hint)
      ? '<div style="font-size:0.75rem;color:hsl(var(--muted-foreground));margin-top:0.18rem">' +
          s.hint +
          (s.link
            ? ' <a href="' + s.link + '" style="color:hsl(var(--foreground));' +
                'text-decoration:underline;text-underline-offset:2px">' + s.linkLabel + '</a>'
            : '') +
        '</div>'
      : '';

    return '<div style="display:flex;align-items:flex-start;gap:0.75rem;padding:0.42rem 0">' +
      '<div style="flex-shrink:0;margin-top:1px">' + icon + '</div>' +
      '<div style="min-width:0;line-height:1.45">' +
        '<div style="font-size:0.82rem;font-weight:' + labelWeight + ';color:' + labelColor + '">' + s.label + '</div>' +
        hint +
      '</div>' +
    '</div>';
  }

  // ── Icons ──────────────────────────────────────────────────────────────────
  function _iconDone() {
    return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
      'stroke="hsl(var(--success))" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">' +
      '<path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>' +
      '<polyline points="22 4 12 14.01 9 11.01"/></svg>';
  }

  function _iconPending() {
    return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
      'stroke="hsl(var(--muted-foreground))" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">' +
      '<circle cx="12" cy="12" r="10"/></svg>';
  }

  function _iconUnknown() {
    return '<div style="width:16px;height:16px;border-radius:50%;background:hsl(var(--muted))"></div>';
  }
})();
