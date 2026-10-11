// Chart period tabs, new ChartPeriodSelector(el, periods, onChange)
class ChartPeriodSelector {
    /** @param {HTMLElement} container @param {string[]} periods @param {(p:string)=>void} onChange @param {string} [active] */
    constructor(container, periods, onChange, active = periods[0]) {
        this._container = container;
        this._onChange = onChange;
        this._active = active;

        const wrap = document.createElement('div');
        wrap.className = 'segmented';
        wrap.setAttribute('role', 'group');
        wrap.setAttribute('aria-label', 'Period');

        this._buttons = periods.map((p) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'seg-btn' + (p === this._active ? ' seg-btn--active' : '');
            btn.setAttribute('aria-pressed', String(p === this._active));
            btn.textContent = p.toUpperCase();
            btn.addEventListener('click', () => this._select(p));
            wrap.appendChild(btn);
            return [p, btn];
        });

        container.appendChild(wrap);
    }

    _select(period) {
        if (period === this._active) return;
        this._active = period;
        for (const [p, btn] of this._buttons) {
            btn.classList.toggle('seg-btn--active', p === period);
            btn.setAttribute('aria-pressed', String(p === period));
        }
        this._onChange(period);
    }

    destroy() {
        this._container.innerHTML = '';
    }
}
