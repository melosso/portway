/**
 * ErrorChart — SVG bar chart for HTTP error code distribution.
 * 4xx bars use --warning color, 5xx use --destructive.
 * Zero external dependencies. Fully theme-aware via CSS vars.
 *
 * Usage:
 *   const chart = new ErrorChart(containerEl);
 *   chart.setData({ '400': 12, '404': 45, '500': 2 }, 1847);
 *   chart.destroy();
 */
class ErrorChart {
  constructor(container) {
    this._container  = container;
    this._data       = {};
    this._total      = 0;
    this._tooltip    = null;
    this._ro         = null;
    this._rafPending = false;

    container.style.position = 'relative';
    this._svg = this._createSvg();
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

  setData(errors, total) {
    this._data  = errors  ?? {};
    this._total = total   ?? 0;
    this._render();
  }

  destroy() {
    this._ro?.disconnect();
    this._tooltip?.remove();
    this._svg?.remove();
  }

  // Internal

  _createSvg() {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.style.cssText = 'width:100%;height:100%;display:block;overflow:visible';
    return svg;
  }

  _render() {
    const svg = this._svg;
    const W   = this._container.clientWidth  || 300;
    const H   = this._container.clientHeight || 180;
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.innerHTML = '';

    // Collect only codes with count > 0, sorted numerically
    const bars = Object.entries(this._data)
      .map(([code, count]) => ({ code, count: Number(count) }))
      .filter(b => b.count > 0)
      .sort((a, b) => Number(a.code) - Number(b.code));

    const totalErrors = bars.reduce((s, b) => s + b.count, 0);

    if (bars.length === 0) {
      this._renderEmpty(svg, W, H);
      return;
    }

    const PAD    = { top: 24, right: 12, bottom: 36, left: 12 };
    const cW     = W - PAD.left - PAD.right;
    const cH     = H - PAD.top  - PAD.bottom;
    const maxVal = Math.max(...bars.map(b => b.count), 1);

    const n      = bars.length;
    const gapPct = 0.25;                          // 25% gap between bars
    const barW   = (cW / n) * (1 - gapPct);
    const gapW   = (cW / n) * gapPct;
    const halfG  = gapW / 2;

    bars.forEach((bar, i) => {
      const is5xx  = Number(bar.code) >= 500;
      const colorV = is5xx ? '--destructive' : '--warning';
      const barH   = cH * (bar.count / maxVal);
      const x      = PAD.left + i * (barW + gapW) + halfG;
      const y      = PAD.top  + cH - barH;
      const cx     = x + barW / 2;

      // Bar
      const rect = this._elSvg('rect', {
        x, y,
        width: barW,
        height: barH,
        rx: '3',
        fill: `hsl(var(${colorV})/0.85)`
      });
      rect.addEventListener('mouseenter', ev => this._showTip(ev, bar, totalErrors));
      rect.addEventListener('mousemove',  ev => this._moveTip(ev));
      rect.addEventListener('mouseleave', ()  => this._hideTip());
      svg.appendChild(rect);

      // Count label above bar
      const countLbl = this._elSvg('text', {
        x: cx, y: y - 4,
        'text-anchor': 'middle',
        fill: `hsl(var(${colorV}))`,
        'font-size': '11',
        'font-weight': '600',
        'font-family': 'var(--app-font)'
      });
      countLbl.textContent = this._fmt(bar.count);
      svg.appendChild(countLbl);

      // Status code label below bar
      const codeLbl = this._elSvg('text', {
        x: cx, y: PAD.top + cH + 14,
        'text-anchor': 'middle',
        fill: 'hsl(var(--muted-foreground))',
        'font-size': '10',
        'font-family': 'var(--app-font)'
      });
      codeLbl.textContent = bar.code;
      svg.appendChild(codeLbl);
    });

    // Summary line at bottom
    const pct    = this._total > 0 ? ((totalErrors / this._total) * 100).toFixed(1) : '0.0';
    const sumLbl = this._elSvg('text', {
      x: W / 2, y: H - 2,
      'text-anchor': 'middle',
      fill: 'hsl(var(--muted-foreground))',
      'font-size': '10',
      'font-family': 'var(--app-font)'
    });
    sumLbl.textContent = `${this._fmt(totalErrors)} errors · ${pct}% error rate`;
    svg.appendChild(sumLbl);
  }

  _renderEmpty(svg, W, H) {
    // Checkmark circle
    const cx = W / 2, cy = H / 2 - 10;
    const circle = this._elSvg('circle', {
      cx, cy, r: '16',
      fill: 'hsl(var(--success)/0.12)',
      stroke: 'hsl(var(--success))',
      'stroke-width': '1.5'
    });
    svg.appendChild(circle);

    const check = this._elSvg('path', {
      d: `M${cx-6},${cy} L${cx-1},${cy+5} L${cx+7},${cy-5}`,
      fill: 'none',
      stroke: 'hsl(var(--success))',
      'stroke-width': '2',
      'stroke-linecap': 'round',
      'stroke-linejoin': 'round'
    });
    svg.appendChild(check);

    const lbl = this._elSvg('text', {
      x: W / 2, y: cy + 30,
      'text-anchor': 'middle',
      fill: 'hsl(var(--muted-foreground))',
      'font-size': '12',
      'font-family': 'var(--app-font)'
    });
    lbl.textContent = 'No errors in this period';
    svg.appendChild(lbl);
  }

  _showTip(ev, bar, total) {
    const pct = total > 0 ? ((bar.count / total) * 100).toFixed(1) : '0.0';
    this._tooltip.style.display = 'flex';
    this._tooltip.innerHTML = `<span class="chart-tooltip-label">HTTP ${bar.code}</span><span class="chart-tooltip-value">${bar.count} (${pct}%)</span>`;
    this._moveTip(ev);
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
  _hideTip() { this._tooltip.style.display = 'none'; }

  _fmt(n) { return n >= 1e6 ? (n/1e6).toFixed(1)+'M' : n >= 1e3 ? (n/1e3).toFixed(1)+'k' : String(n ?? 0); }

  _elSvg(tag, attrs = {}) {
    const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
    return el;
  }
}
