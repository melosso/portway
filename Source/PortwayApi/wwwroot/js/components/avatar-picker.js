// Avatar picker, new AvatarPicker(container, label), reset(user) then value() for the request body
class AvatarPicker {
    static COUNT = 8;
    static SHUFFLE_SVG =
        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="16 3 21 3 21 8"/><line x1="4" y1="20" x2="21" y2="3"/><polyline points="21 16 21 21 16 21"/><line x1="15" y1="15" x2="21" y2="21"/><line x1="4" y1="4" x2="9" y2="9"/></svg>';

    /** @param {HTMLElement} container @param {string} label */
    constructor(container, label) {
        this._container = container;
        this._label = label;
        this._initial = '?';
        this._current = null;
        this._candidates = null;
        this._selected = 'none';
        this._next = this._fetch();
    }

    /** @param {{avatar?: string|null, display_name?: string, username?: string}|null} user */
    reset(user) {
        this._initial = (user?.display_name || user?.username || '?').charAt(0).toUpperCase();
        this._current = user?.avatar ?? null;
        this._selected = user ? (this._current ? 'current' : 'none') : 0;
        this._candidates = null;
        this._render();
        this._take();
    }

    /** Fields for the request body; empty keeps the stored avatar, or lets the server pick one for a new user */
    value() {
        if (this._selected === 'none') return { avatar_style: '' };
        const c = typeof this._selected === 'number' ? this._candidates?.[this._selected] : null;
        return c ? { avatar_style: c.style, avatar_seed: c.seed } : {};
    }

    _fetch() {
        return api(`/ui/api/avatars/candidates?count=${AvatarPicker.COUNT}`, { silent: true })
            .then((d) => d.candidates ?? [])
            .catch(() => []);
    }

    // shows the prefetched batch and starts fetching the next one
    async _take() {
        const batch = await this._next;
        this._next = this._fetch();
        this._candidates = batch;
        this._render();
    }

    _shuffle() {
        if (typeof this._selected === 'number') this._selected = 0;
        this._candidates = null;
        this._render();
        this._take();
    }

    _options() {
        const options = [];
        if (this._current)
            options.push({ key: 'current', label: 'Current avatar', html: `<img src="${esc(this._current)}" alt="">` });
        options.push({
            key: 'none',
            label: 'No avatar',
            html: `<span aria-hidden="true">${esc(this._initial)}</span>`
        });
        if (this._candidates) {
            this._candidates.forEach((c, i) =>
                options.push({
                    key: i,
                    label: `Avatar ${i + 1}, ${c.style}`,
                    html: `<img src="${esc(c.avatar)}" alt="">`
                })
            );
        }
        return options;
    }

    _render() {
        const options = this._options();
        const placeholders = this._candidates
            ? ''
            : '<span class="avatar-tile is-loading" aria-hidden="true"></span>'.repeat(AvatarPicker.COUNT);
        const tiles = options
            .map((o) => {
                const checked = o.key === this._selected;
                return `<button type="button" class="avatar-tile" role="radio" aria-checked="${checked}" tabindex="${checked ? 0 : -1}" aria-label="${esc(o.label)}" data-key="${o.key}">${o.html}</button>`;
            })
            .join('');

        this._container.innerHTML = `
            <div class="avatar-picker">
                <div class="avatar-grid" role="radiogroup" aria-label="${esc(this._label)}" aria-busy="${!this._candidates}">${tiles}${placeholders}</div>
                <button type="button" class="btn-icon" data-action="shuffle" aria-label="Show other avatars" data-tooltip="Shuffle"${this._candidates ? '' : ' disabled'}>${AvatarPicker.SHUFFLE_SVG}</button>
            </div>`;

        // keep a focusable radio when the selection points at a candidate that is still loading
        const radios = [...this._container.querySelectorAll('.avatar-tile[role="radio"]')];
        if (!radios.some((r) => r.tabIndex === 0) && radios.length) radios[0].tabIndex = 0;

        radios.forEach((btn, i) => {
            btn.addEventListener('click', () => this._select(btn.dataset.key));
            btn.addEventListener('keydown', (e) => {
                const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
                if (!step) return;
                e.preventDefault();
                this._select(radios[(i + step + radios.length) % radios.length].dataset.key);
            });
        });
        this._container.querySelector('[data-action="shuffle"]').addEventListener('click', () => this._shuffle());
    }

    _select(key) {
        this._selected = key === 'current' || key === 'none' ? key : +key;
        this._render();
        this._container.querySelector(`.avatar-tile[data-key="${key}"]`)?.focus();
    }
}
