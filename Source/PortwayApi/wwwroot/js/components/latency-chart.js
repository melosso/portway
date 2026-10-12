class LatencyChart {
    constructor(container) {
        this._container = container;
        this._bins = [];
        this._summary = {};
        this._percentile = 'p95';
        this._rafPending = false;

        container.style.position = 'relative';
        this._svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        this._svg.style.cssText = 'width:100%;height:100%;display:block;overflow:visible';
        this._svg.setAttribute('role', 'img');
        container.appendChild(this._svg);

        this._tooltip = document.createElement('div');
        this._tooltip.className = 'chart-tooltip';
        this._tooltip.style.display = 'none';
        container.appendChild(this._tooltip);

        this._ro = new ResizeObserver(() => {
            if (this._rafPending) return;
            this._rafPending = true;
            requestAnimationFrame(() => {
                this._rafPending = false;
                this._render();
            });
        });
        this._ro.observe(container);
    }

    setData(bins, summary, percentile) {
        this._bins = bins ?? [];
        this._summary = summary ?? {};
        if (percentile) this._percentile = percentile;
        this._render();
    }

    setPercentile(percentile) {
        this._percentile = percentile;
        this._render();
    }

    static formatMs(ms) {
        if (ms == null) return '—';
        if (ms < 1) return '<1 ms';
        return ms >= 1000 ? `${+(ms / 1000).toFixed(ms >= 10000 ? 0 : 1)} s` : `${ms} ms`;
    }

    _label(i) {
        const upper = this._bins[i].upper_ms;
        if (upper == null) return `>${LatencyChart.formatMs(this._bins[i - 1]?.upper_ms ?? 0).replace(' ', '')}`;
        return LatencyChart.formatMs(upper).replace(' ', '');
    }

    _position(ms) {
        if (ms == null) return null;
        for (let i = 0; i < this._bins.length; i++) {
            const upper = this._bins[i].upper_ms;
            const lower = i === 0 ? 0 : this._bins[i - 1].upper_ms;
            if (upper == null) return i + 0.5;
            if (ms <= upper) return i + (ms - lower) / (upper - lower || 1);
        }
        return this._bins.length;
    }

    _render() {
        const svg = this._svg;
        const W = this._container.clientWidth || 300;
        const H = this._container.clientHeight || 200;
        svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
        svg.innerHTML = '';

        const total = this._bins.reduce((s, b) => s + b.count, 0);
        if (total === 0) {
            svg.setAttribute('aria-label', 'No API requests in this period');
            const t = this._el('text', {
                x: W / 2,
                y: H / 2,
                'text-anchor': 'middle',
                fill: 'hsl(var(--muted-foreground))',
                'font-size': '12',
                'font-family': 'var(--app-font)'
            });
            t.textContent = 'No API requests in this period';
            svg.appendChild(t);
            return;
        }

        const cutoffMs = this._summary[this._percentile];
        svg.setAttribute(
            'aria-label',
            `Latency distribution of ${total} requests, ${this._percentile} ${LatencyChart.formatMs(cutoffMs)}`
        );

        const PAD = { top: 22, right: 8, bottom: 22, left: 36 };
        const cW = W - PAD.left - PAD.right;
        const cH = H - PAD.top - PAD.bottom;
        const n = this._bins.length;
        const slot = cW / n;
        const barW = slot * 0.72;
        const max = Math.max(...this._bins.map((b) => b.count), 1);
        const cutPos = this._position(cutoffMs);
        const x = (pos) => PAD.left + pos * slot;

        for (const frac of [0, 0.5, 1]) {
            const y = PAD.top + cH - cH * frac;
            svg.appendChild(
                this._el('line', {
                    x1: PAD.left,
                    x2: W - PAD.right,
                    y1: y,
                    y2: y,
                    stroke: 'hsl(var(--border))',
                    'stroke-width': '1'
                })
            );
            const lbl = this._el('text', {
                x: PAD.left - 6,
                y: y + 3,
                'text-anchor': 'end',
                fill: 'hsl(var(--muted-foreground))',
                'font-size': '10',
                'font-family': 'var(--app-font)'
            });
            lbl.textContent = this._fmt(Math.round(max * frac));
            svg.appendChild(lbl);
        }

        this._bins.forEach((bin, i) => {
            const within = cutPos != null && i < cutPos;
            const h = bin.count > 0 ? Math.max(2, cH * (bin.count / max)) : 0;
            const rect = this._el('rect', {
                x: x(i) + (slot - barW) / 2,
                y: PAD.top + cH - h,
                width: barW,
                height: h,
                rx: '3',
                fill: within ? 'hsl(var(--info)/0.85)' : 'hsl(var(--muted-foreground)/0.3)'
            });
            const hit = this._el('rect', { x: x(i), y: PAD.top, width: slot, height: cH, fill: 'transparent' });
            hit.addEventListener('mouseenter', (ev) => this._showTip(ev, i, total));
            hit.addEventListener('mousemove', (ev) => this._moveTip(ev));
            hit.addEventListener('mouseleave', () => (this._tooltip.style.display = 'none'));
            svg.appendChild(rect);
            svg.appendChild(hit);

            if (n <= 8 || i % Math.ceil(n / Math.max(4, Math.floor(cW / 56))) === 0 || i === n - 1) {
                const lbl = this._el('text', {
                    x: x(i) + slot / 2,
                    y: H - 4,
                    'text-anchor': 'middle',
                    fill: 'hsl(var(--muted-foreground))',
                    'font-size': '10',
                    'font-family': 'var(--app-font)'
                });
                lbl.textContent = this._label(i);
                svg.appendChild(lbl);
            }
        });

        const meanPos = this._position(this._summary.mean_ms);
        if (meanPos != null) this._marker(x(meanPos), PAD, cH, true);
        if (cutPos != null) this._marker(x(cutPos), PAD, cH, false);

        let right = W - PAD.right;
        for (const [text, color, weight] of [
            [`${this._percentile} ${LatencyChart.formatMs(cutoffMs)}`, 'hsl(var(--info))', '600'],
            [`Mean ${LatencyChart.formatMs(this._summary.mean_ms)}`, 'hsl(var(--muted-foreground))', '400']
        ]) {
            const lbl = this._el('text', {
                x: right,
                y: 10,
                'text-anchor': 'end',
                fill: color,
                'font-size': '10',
                'font-weight': weight,
                'font-family': 'var(--app-font)'
            });
            lbl.textContent = text;
            svg.appendChild(lbl);
            right -= lbl.getComputedTextLength() + 12;
        }
    }

    _marker(px, PAD, cH, dashed) {
        this._svg.appendChild(
            this._el('line', {
                x1: px,
                x2: px,
                y1: PAD.top - 4,
                y2: PAD.top + cH,
                stroke: dashed ? 'hsl(var(--muted-foreground))' : 'hsl(var(--info))',
                'stroke-width': dashed ? '1' : '1.5',
                'stroke-dasharray': dashed ? '3 3' : 'none',
                'pointer-events': 'none'
            })
        );
    }

    _showTip(ev, i, total) {
        const bin = this._bins[i];
        const lower = i === 0 ? 0 : this._bins[i - 1].upper_ms;
        const range =
            bin.upper_ms == null
                ? `Over ${LatencyChart.formatMs(lower)}`
                : `${LatencyChart.formatMs(lower)} to ${LatencyChart.formatMs(bin.upper_ms)}`;
        const pct = total > 0 ? ((bin.count / total) * 100).toFixed(1) : '0.0';
        this._tooltip.style.display = 'flex';
        this._tooltip.innerHTML = `<span class="chart-tooltip-label">${esc(range)}</span><span class="chart-tooltip-value">${this._fmt(bin.count)} requests (${pct}%)</span>`;
        this._moveTip(ev);
    }

    _moveTip(ev) {
        const r = this._container.getBoundingClientRect();
        let left = ev.clientX - r.left + 12;
        const tw = this._tooltip.offsetWidth;
        if (left + tw > r.width) left = ev.clientX - r.left - tw - 12;
        this._tooltip.style.left = left + 'px';
        this._tooltip.style.top = ev.clientY - r.top - 44 + 'px';
    }

    _fmt(n) {
        return n >= 1e6 ? (n / 1e6).toFixed(1) + 'M' : n >= 1e3 ? (n / 1e3).toFixed(1) + 'k' : String(n ?? 0);
    }

    _el(tag, attrs) {
        const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
        for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
        return el;
    }
}
