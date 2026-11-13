// Page defines CARDS and load()

let _writable = {};

// rendered values for edit detection
let _initial = new WeakMap();

// display labels for raw choice values
const CHOICE_LABELS = { _blank: 'New tab', _self: 'Same tab' };

// pages a setting serves, relative to the base href
const SETTING_LINKS = {
    'OpenApi:Enabled': 'docs',
    'OpenApi:ServeMarkdown': 'docs/openapi.md',
    'Console:Customization:LandingPageEnabled': './'
};

const EXTERNAL_SVG =
    '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>';

const LIST_KINDS = ['originlist', 'iplist', 'cidrlist', 'hostlist', 'extlist', 'mediatypelist'];

const MAP_KINDS = ['intmap', 'levelmap'];

function labelBlock(label, desc, forId, key) {
    const env = key ? _writable[key]?.env : null;
    const href = key ? SETTING_LINKS[key] : null;
    const link =
        href != null
            ? ` <a class="setting-link" href="${href}" target="_blank" rel="noopener noreferrer" aria-label="Open" title="Open">${EXTERNAL_SVG}</a>`
            : '';
    const note = env ? `<p title="${esc(env)}">Managed by environment variable</p>` : desc ? `<p>${esc(desc)}</p>` : '';
    return `<div class="setting-label">
      <label${forId ? ` for="${forId}"` : ''}>${label}</label>${link}
      ${note}
    </div>`;
}

function settingItem(label, value, desc, key) {
    return settingItemRaw(label, esc(String(value ?? '—')), desc, key);
}

function settingItemRaw(label, valueHtml, desc, key) {
    return `<div class="setting-row">
    ${labelBlock(label, desc, null, key)}
    <span class="setting-value">${valueHtml}</span>
  </div>`;
}

// setting row with a status icon and the status text as tooltip
function statusItem(label, state, status, desc) {
    return settingItemRaw(label, statusIcon(state, status, 'left'), desc);
}

function riskItem(label, ok, okText, badText, desc) {
    return statusItem(label, ok ? 'ok' : 'warn', ok ? okText : badText, desc);
}

function editable(key) {
    const spec = _writable[key];
    return spec && !spec.env ? spec : null;
}

// env-set values render as disabled controls, skipped on save
function locked(key) {
    return _writable[key]?.env ? ' disabled' : '';
}

// one editable row; control and value come from the writable schema entry
// scale converts the stored value into the displayed unit, e.g. bytes shown as MB
function field(key, label, desc, { unit, scale = 1, multiline, placeholder = '' } = {}) {
    const spec = _writable[key];
    if (!spec) return settingItem(label, '—', desc);
    const id = 'set-' + key.replace(/[:.]/g, '-');
    const dis = locked(key);
    const attrs = `${dis} id="${id}" data-setting="${key}"`;
    const ph = ` placeholder="${esc(placeholder)}"`;
    const maxlength = spec.max != null ? ` maxlength="${spec.max}"` : '';
    const v = spec.value;
    let control;
    let stacked = false;

    if (spec.kind === 'bool') {
        control = `<label class="switch${dis}"><input${attrs} data-kind="bool" type="checkbox"${v ? ' checked' : ''}><span class="slider"></span></label>`;
    } else if (spec.kind === 'int' || spec.kind === 'long' || spec.kind === 'number') {
        const shown = v != null ? Math.round((v / scale) * 100) / 100 : '';
        const min = spec.min != null ? ` min="${spec.min / scale}"` : '';
        const max = spec.max != null ? ` max="${spec.max / scale}"` : '';
        const step = scale !== 1 || spec.kind === 'number' ? ' step="any"' : '';
        const input = `<input${attrs} data-kind="${spec.kind}" data-scale="${scale}" type="number" class="input"${min}${max}${step} value="${shown}">`;
        control = unit ? `<div class="setting-unit">${input}<span>${esc(unit)}</span></div>` : input;
    } else if (spec.kind === 'choice') {
        const opts = (spec.choices ?? [])
            .map(
                (c) =>
                    `<option value="${esc(c)}"${c.toLowerCase() === String(v ?? '').toLowerCase() ? ' selected' : ''}>${esc(CHOICE_LABELS[c] ?? c)}</option>`
            )
            .join('');
        control = `<select${attrs} data-kind="choice" class="input">${opts}</select>`;
    } else if (spec.kind === 'time') {
        control = `<input${attrs} data-kind="time" type="time" class="input" value="${esc(v ?? '')}">`;
    } else if (spec.kind === 'secret') {
        const clear =
            v && !dis
                ? `<button class="btn btn-outline" type="button" data-on="click:clearSecret" data-args='[{"ref": "this"}]'>Clear</button>`
                : '';
        control = `<div class="card-footer-actions"><input${attrs} data-kind="secret" type="password" class="input" autocomplete="new-password" placeholder="${v ? 'Set' : 'Not set'}">${clear}</div>`;
    } else if (spec.kind === 'clear') {
        control =
            v && !dis
                ? `<button class="btn btn-outline" type="button" data-on="click:clearSetting" data-args="${actionArgs(key, label)}">Remove</button>`
                : `<span class="setting-value">${v ? 'Set' : 'Not set'}</span>`;
    } else if (LIST_KINDS.includes(spec.kind)) {
        stacked = true;
        control = `<textarea${attrs} data-kind="list" class="input" rows="3" spellcheck="false"${ph}>${esc((v ?? []).join('\n'))}</textarea>`;
    } else if (MAP_KINDS.includes(spec.kind)) {
        stacked = true;
        const lines = Object.entries(v ?? {})
            .map(([k, val]) => `${k}=${val}`)
            .join('\n');
        control = `<textarea${attrs} data-kind="map" data-map="${spec.kind}" class="input" rows="3" spellcheck="false"${ph}>${esc(lines)}</textarea>`;
    } else if (multiline) {
        stacked = true;
        control = `<textarea${attrs} data-kind="text" class="input" rows="2"${maxlength}${ph}>${esc(v ?? '')}</textarea>`;
    } else {
        control = `<input${attrs} data-kind="text" type="text" class="input"${maxlength} spellcheck="false"${ph} value="${esc(v ?? '')}">`;
    }

    return `<div class="setting-row${stacked ? ' setting-row-stacked' : ''}">
    ${labelBlock(label, desc, id, key)}
    ${control}
  </div>`;
}

function cardFooter(bodyId, actionsHtml = '') {
    return `<div class="card-footer">
    <span class="restart-note" id="${bodyId}-note"></span>
    <div class="card-footer-actions">
      ${actionsHtml}
      <button class="btn btn-primary card-save" disabled data-on="click:saveCard" data-args="${actionArgs(bodyId, ARG.THIS)}">Save</button>
    </div>
  </div>`;
}

// Render every card
function renderCards(cards, data) {
    _writable = Object.fromEntries((data.writable ?? []).map((w) => [w.key, w]));
    for (const [id, card] of Object.entries(cards)) {
        document.getElementById(id).innerHTML =
            card.rows(data).join('') + (card.footer === false ? '' : cardFooter(id, card.actions));
    }
    _initial = new WeakMap();
    document.querySelectorAll('[data-setting]').forEach((el) => _initial.set(el, JSON.stringify(readControl(el))));
}

// a card's save button enables on its first edit
['input', 'change'].forEach((type) =>
    document.addEventListener(type, (e) => {
        e.target
            .closest?.('[data-setting]')
            ?.closest('.card')
            ?.querySelector('.card-save')
            ?.removeAttribute('disabled');
    })
);

function clearSecret(btn) {
    const input = btn.parentElement.querySelector('[data-setting]');
    input.value = '';
    input.dataset.clear = '1';
    input.placeholder = 'Cleared on save';
    btn.remove();
    input.closest('.card')?.querySelector('.card-save')?.removeAttribute('disabled');
}

// Remove a write-once credential
function clearSetting(key, label) {
    AlertDialog.show({
        title: `Remove ${label}?`,
        description: 'The value is removed from the console settings file. Restart to apply.',
        actionLabel: 'Remove',
        variant: 'destructive',
        onConfirm: async () => {
            try {
                await api('/_/api/settings', {
                    method: 'PUT',
                    body: { [key]: '' },
                    success: `${label} removed`,
                    failure: 'Failed to remove the value'
                });
                await load();
            } catch (e) {}
        }
    });
}

function readMap(el) {
    const map = {};
    el.value.split('\n').forEach((line) => {
        const at = line.indexOf('=');
        if (at < 1) return;
        const value = line.slice(at + 1).trim();
        map[line.slice(0, at).trim()] = el.dataset.map === 'intmap' ? Number(value) : value;
    });
    return map;
}

// Undefined leaves the setting unchanged
function readControl(el) {
    switch (el.dataset.kind) {
        case 'bool':
            return el.checked;
        case 'int':
        case 'long':
            return el.value === '' ? undefined : Math.round(Number(el.value) * (Number(el.dataset.scale) || 1));
        case 'number':
            return el.value === '' ? undefined : Number(el.value);
        case 'list':
            return el.value
                .split('\n')
                .map((v) => v.trim())
                .filter(Boolean);
        case 'map':
            return readMap(el);
        case 'secret':
            return el.dataset.clear ? '' : el.value || undefined;
        default:
            return el.value;
    }
}

// Collects the editable controls in one card and sends them as a single request
async function saveCard(bodyId, btn) {
    const body = document.getElementById(bodyId);
    if (!body) return;
    const changes = {};
    body.querySelectorAll('[data-setting]:not(:disabled)').forEach((el) => {
        el.classList.remove('input-invalid');
        const value = readControl(el);
        if (value !== undefined && JSON.stringify(value) !== _initial.get(el)) changes[el.dataset.setting] = value;
    });
    if (!Object.keys(changes).length) {
        btn.disabled = true;
        return;
    }

    const send = async () => {
        btn.classList.add('is-loading');
        btn.disabled = true;
        try {
            const r = await api('/_/api/settings', {
                method: 'PUT',
                body: changes,
                success: 'Settings saved',
                failure: 'Failed to save settings'
            });
            await load();
            const note = document.getElementById(bodyId + '-note');
            if (note) note.textContent = r.restart_required ? 'Saved. Restart Portway to apply.' : '';
        } catch (e) {
            if (e.field) {
                const el = body.querySelector(`[data-setting="${e.field}"]`);
                if (el) {
                    el.classList.add('input-invalid');
                    el.focus();
                }
            }
        } finally {
            btn.classList.remove('is-loading');
            btn.disabled = false;
        }
    };

    // Optional confirm before saving
    const confirm = CARDS[bodyId]?.confirm?.(changes);
    if (confirm) AlertDialog.show({ ...confirm, onConfirm: send });
    else await send();
}
