/**
 * AreaChart — Shadcn/Recharts-inspired SVG area chart.
 * Supports single or dual (stacked) series with dots and crosshair.
 * Zero external dependencies. Respects CSS custom properties for theming.
 *
 * Usage (single series):
 *   chart.setData([{ label: '14:00', timestamp: '...', count: 45 }, ...]);
 *
 * Usage (stacked dual series — API on top of UI):
 *   chart.setData(apiArray, uiArray);  // same-length arrays
 */
class AreaChart {
  constructor(container, opts = {}) {
    this._container  = container;
    this._colorVar   = opts.color ?? 'info';
    this._s1         = [];     // primary (api or single series)
    this._s2         = null;   // secondary (ui), stacked under primary
    this._tooltip    = null;
    this._crosshair  = null;
    this._dotGroup   = null;   // primary (API) dots
    this._dotGroup2  = null;   // secondary (UI) dots
    this._ro         = null;
    this._rafPending = false;
    this._emptyMsg   = null;

    container.style.position = 'relative';
    this._svg = this._mkSvg();
    container.appendChild(this._svg);

    this._tooltip = document.createElement('div');
    this._tooltip.className = 'chart-tooltip';
    this._tooltip.style.display = 'none';
    container.appendChild(this._tooltip);

    this._ro = new ResizeObserver(() => {
      if (!this._rafPending) {
        this._rafPending = true;
        requestAnimationFrame(() => { this._rafPending = false; this._render(); });
      }
    });
    this._ro.observe(container);
  }

  /** primary = [{label, timestamp, count}], secondary (optional) = same shape */
  setData(primary, secondary = null) {
    this._emptyMsg = null;
    this._s1 = primary ?? [];
    this._s2 = (secondary?.length > 0) ? secondary : null;
    this._render();
  }

  setEmpty(msg) {
    this._emptyMsg = msg ?? 'No data';
    this._s1 = [];
    this._s2 = null;
    this._render();
  }

  destroy() {
    this._ro?.disconnect();
    this._tooltip?.remove();
    this._svg?.remove();
  }

  // ── Rendering ─────────────────────────────────────────────

  _render() {
    const svg = this._svg;
    const W   = this._container.clientWidth  || 400;
    const H   = this._container.clientHeight || 180;
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.innerHTML = '';
    this._crosshair = this._dotGroup = this._dotGroup2 = null;

    if (this._emptyMsg || this._s1.length === 0) {
      this._renderEmpty(svg, W, H);
      return;
    }

    const PAD  = { top: 12, right: 12, bottom: 28, left: 42 };
    const cW   = W - PAD.left - PAD.right;
    const cH   = H - PAD.top  - PAD.bottom;
    const bot  = PAD.top + cH;
    const n    = this._s1.length;
    const step = cW / Math.max(n - 1, 1);

    // Y scale — max of stacked total
    const totals = this._s2
      ? this._s1.map((d, i) => d.count + this._s2[i].count)
      : this._s1.map(d => d.count);
    const maxV = Math.max(...totals, 1);
    const yOf  = v => PAD.top + cH * (1 - Math.min(v, maxV) / maxV);

    // Point sets
    // pts1 = top of stacked chart (total when dual, api alone when single)
    // pts2 = top of UI layer (base of API layer)
    const pts1 = this._s1.map((d, i) => ({
      x: PAD.left + i * step, y: yOf(totals[i]),
      count: d.count, label: d.label, timestamp: d.timestamp ?? null
    }));
    const pts2 = this._s2
      ? this._s2.map((d, i) => ({ x: PAD.left + i * step, y: yOf(d.count), count: d.count }))
      : null;

    // ── Defs: gradients ──────────────────────────────────────
    const defs  = this._el('defs');
    const gMain = `ag-${this._colorVar}`;
    const gUi   = `ag-${this._colorVar}-ui`;

    const makeGrad = (id, color, opacity0, opacity1) => {
      const g = this._el('linearGradient', { id, x1:'0', y1:'0', x2:'0', y2:'1' });
      g.append(
        this._el('stop', { offset:'0%',   'stop-color': color, 'stop-opacity': String(opacity0) }),
        this._el('stop', { offset:'100%', 'stop-color': color, 'stop-opacity': String(opacity1) })
      );
      return g;
    };
    defs.appendChild(makeGrad(gMain, `hsl(var(--${this._colorVar}))`, 0.22, 0.02));
    if (pts2) defs.appendChild(makeGrad(gUi, 'hsl(var(--muted-foreground))', 0.15, 0.02));
    svg.appendChild(defs);

    // ── Gridlines ────────────────────────────────────────────
    const gridG = this._el('g');
    for (let i = 0; i <= 4; i++) {
      const y = PAD.top + cH * (1 - i / 4);
      gridG.appendChild(this._el('line', {
        x1: PAD.left, y1: y, x2: PAD.left + cW, y2: y,
        stroke: 'hsl(var(--border))', 'stroke-width': '1'
      }));
      const lbl = this._el('text', {
        x: PAD.left - 6, y: y + 4, 'text-anchor': 'end',
        fill: 'hsl(var(--muted-foreground))', 'font-size': '10', 'font-family': 'var(--app-font)'
      });
      lbl.textContent = this._fmt(Math.round(maxV * i / 4));
      gridG.appendChild(lbl);
    }
    svg.appendChild(gridG);

    // ── X-axis labels ─────────────────────────────────────────
    const maxLbls  = Math.floor(cW / 52);
    const lblEvery = Math.max(1, Math.ceil(n / maxLbls));
    const xG = this._el('g');
    for (let i = 0; i < n; i++) {
      if (i % lblEvery !== 0 && i !== n - 1) continue;
      const lbl = this._el('text', {
        x: PAD.left + i * step, y: H - 6, 'text-anchor': 'middle',
        fill: 'hsl(var(--muted-foreground))', 'font-size': '10', 'font-family': 'var(--app-font)'
      });
      lbl.textContent = this._s1[i].label;
      xG.appendChild(lbl);
    }
    svg.appendChild(xG);

    // ── UI base fill ──────────────────────────────────────────
    if (pts2) {
      const uiPath = this._cubic(pts2) + ` L${pts2[n-1].x},${bot} L${pts2[0].x},${bot} Z`;
      svg.appendChild(this._el('path', { d: uiPath, fill: `url(#${gUi})`, stroke: 'none' }));
    }

    // ── API / primary fill ────────────────────────────────────
    // When stacked: band between pts2 (linear bottom) and pts1 (cubic top)
    // When single:  area from baseline to pts1
    let apiPath;
    if (pts2) {
      apiPath = this._cubic(pts1);
      apiPath += ` L${pts2[n-1].x},${pts2[n-1].y}`;
      for (let i = n - 2; i >= 0; i--) apiPath += ` L${pts2[i].x},${pts2[i].y}`;
      apiPath += ' Z';
    } else {
      apiPath = this._cubic(pts1) + ` L${pts1[n-1].x},${bot} L${pts1[0].x},${bot} Z`;
    }
    svg.appendChild(this._el('path', { d: apiPath, fill: `url(#${gMain})`, stroke: 'none' }));

    // ── Top stroke ────────────────────────────────────────────
    svg.appendChild(this._el('path', {
      d: this._cubic(pts1), fill: 'none',
      stroke: `hsl(var(--${this._colorVar}))`, 'stroke-width': '1.5',
      'stroke-linecap': 'round', 'stroke-linejoin': 'round'
    }));

    // ── Crosshair (hidden until hover) ────────────────────────
    const xhair = this._el('line', {
      x1: 0, y1: PAD.top, x2: 0, y2: bot,
      stroke: 'hsl(var(--muted-foreground))', 'stroke-width': '1',
      'stroke-dasharray': '3 3', opacity: '0', 'pointer-events': 'none'
    });
    svg.appendChild(xhair);
    this._crosshair = xhair;

    // ── Dots: secondary UI series ─────────────────────────────
    if (pts2) {
      const dotG2 = this._el('g', { 'pointer-events': 'none' });
      pts2.forEach(pt => {
        dotG2.appendChild(this._el('circle', {
          cx: pt.x, cy: pt.y, r: '2',
          fill: 'hsl(var(--muted-foreground) / 0.6)',
          stroke: 'hsl(var(--background))', 'stroke-width': '1.5'
        }));
      });
      svg.appendChild(dotG2);
      this._dotGroup2 = dotG2;
    }

    // ── Dots: primary API series ──────────────────────────────
    const dotG = this._el('g', { 'pointer-events': 'none' });
    pts1.forEach(pt => {
      dotG.appendChild(this._el('circle', {
        cx: pt.x, cy: pt.y, r: '2.5',
        fill: `hsl(var(--${this._colorVar}))`,
        stroke: 'hsl(var(--background))', 'stroke-width': '1.5'
      }));
    });
    svg.appendChild(dotG);
    this._dotGroup = dotG;

    // ── Hover hit-test rects ──────────────────────────────────
    const colW   = cW / n;
    const hoverG = this._el('g');
    pts1.forEach((pt, i) => {
      const r = this._el('rect', {
        x: pt.x - colW / 2, y: PAD.top, width: colW, height: cH, fill: 'transparent'
      });
      const p2 = pts2?.[i] ?? null;
      r.addEventListener('mouseenter', ev => this._enter(ev, pt, p2, i));
      r.addEventListener('mousemove',  ev => this._moveTip(ev));
      r.addEventListener('mouseleave', ()  => this._leave(i));
      hoverG.appendChild(r);
    });
    svg.appendChild(hoverG);
  }

  // ── Hover handlers ────────────────────────────────────────

  _enter(ev, pt, pt2, idx) {
    if (this._crosshair) {
      this._crosshair.setAttribute('x1', pt.x);
      this._crosshair.setAttribute('x2', pt.x);
      this._crosshair.setAttribute('opacity', '0.5');
    }
    const dot = this._dotGroup?.children[idx];
    if (dot) dot.setAttribute('r', '4');
    const dot2 = this._dotGroup2?.children[idx];
    if (dot2) dot2.setAttribute('r', '3.5');

    this._tooltip.style.display = 'flex';
    this._tooltip.innerHTML = this._tipHtml(pt, pt2);
    this._moveTip(ev);
  }

  _leave(idx) {
    this._crosshair?.setAttribute('opacity', '0');
    const dot = this._dotGroup?.children[idx];
    if (dot) dot.setAttribute('r', '2.5');
    const dot2 = this._dotGroup2?.children[idx];
    if (dot2) dot2.setAttribute('r', '2');
    this._tooltip.style.display = 'none';
  }

  _moveTip(ev) {
    const r  = this._container.getBoundingClientRect();
    let x    = ev.clientX - r.left + 12;
    let y    = ev.clientY - r.top  - 36;
    const tw = this._tooltip.offsetWidth;
    if (x + tw > r.width) x = ev.clientX - r.left - tw - 12;
    this._tooltip.style.left = x + 'px';
    this._tooltip.style.top  = y + 'px';
  }

  _tipHtml(pt, pt2) {
    const lbl = this._fmtLabel(pt);
    const dot = (color) =>
      `<span style="display:inline-block;width:7px;height:7px;border-radius:50%;background:${color};margin-right:5px;flex-shrink:0"></span>`;
    if (pt2) {
      return `<span class="chart-tooltip-label">${lbl}</span>
        <span class="chart-tooltip-value" style="display:flex;align-items:center">${dot(`hsl(var(--${this._colorVar}))`)}API &nbsp;${this._fmt(pt.count)}</span>
        <span class="chart-tooltip-value" style="display:flex;align-items:center;font-weight:500;color:hsl(var(--muted-foreground))">${dot('hsl(var(--muted-foreground)/.6)')}UI &nbsp;${this._fmt(pt2.count)}</span>`;
    }
    return `<span class="chart-tooltip-label">${lbl}</span>
      <span class="chart-tooltip-value">${this._fmt(pt.count)} req</span>`;
  }

  _renderEmpty(svg, W, H) {
    const t = this._el('text', {
      x: W / 2, y: H / 2 + 4, 'text-anchor': 'middle',
      fill: 'hsl(var(--muted-foreground))', 'font-size': '13', 'font-family': 'var(--app-font)'
    });
    t.textContent = this._emptyMsg ?? 'No data yet';
    svg.appendChild(t);
  }

  // ── Monotone cubic bezier (Fritsch-Carlson) ───────────────

  _cubic(pts) {
    const n = pts.length;
    if (n === 0) return '';
    if (n === 1) return `M${pts[0].x},${pts[0].y}`;

    const dx = [], dy = [], sl = [], m = [];
    for (let i = 0; i < n - 1; i++) {
      dx[i] = pts[i+1].x - pts[i].x;
      dy[i] = pts[i+1].y - pts[i].y;
      sl[i] = dy[i] / dx[i];
    }
    m[0] = sl[0];
    for (let i = 1; i < n - 1; i++) m[i] = (sl[i-1] + sl[i]) / 2;
    m[n-1] = sl[n-2];
    for (let i = 0; i < n - 1; i++) {
      if (sl[i] === 0) { m[i] = m[i+1] = 0; continue; }
      const a = m[i] / sl[i], b = m[i+1] / sl[i], s = a*a + b*b;
      if (s > 9) { const t = 3 / Math.sqrt(s); m[i] = t*a*sl[i]; m[i+1] = t*b*sl[i]; }
    }
    let d = `M${pts[0].x},${pts[0].y}`;
    for (let i = 0; i < n - 1; i++) {
      d += ` C${pts[i].x + dx[i]/3},${pts[i].y + m[i]*dx[i]/3} ${pts[i+1].x - dx[i]/3},${pts[i+1].y - m[i+1]*dx[i]/3} ${pts[i+1].x},${pts[i+1].y}`;
    }
    return d;
  }

  _fmtLabel(pt) {
    if (!pt.timestamp) return pt.label;
    const d = new Date(pt.timestamp);
    const isHourly = this._s1.length > 1 && this._s1[1]?.timestamp
      ? (new Date(this._s1[1].timestamp) - new Date(this._s1[0].timestamp)) < 86_400_000
      : false;
    return isHourly
      ? d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
      : d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
  }

  _fmt(n) { return n >= 1e6 ? (n/1e6).toFixed(1)+'M' : n >= 1e3 ? (n/1e3).toFixed(1)+'k' : String(n ?? 0); }

  _mkSvg() {
    const s = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    s.style.cssText = 'width:100%;height:100%;display:block;overflow:visible';
    return s;
  }

  _el(tag, attrs = {}) {
    const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
    return el;
  }
}
