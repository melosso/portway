// Key-value row editor, shared by any page that needs a header/property editor.
// Depends on: shared.js (esc)
// Contract: pages using this must define markDirty() (or no-op it).

// ── Render existing key-value pairs as editable rows ─────
function buildKvEditor(headers) {
  return Object.entries(headers).map(([k, v]) =>
    `<div class="kv-row">
      <input type="text" value="${esc(k)}" placeholder="Key" class="kv-key-input" oninput="markDirty()">
      <input type="text" value="${esc(v)}" placeholder="Value" class="kv-val-input" oninput="markDirty()">
      <button class="btn-icon" onclick="this.closest('.kv-row').remove(); markDirty();" title="Remove">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </button>
    </div>`
  ).join('');
}

// ── Append a blank editable row to a container ───────────
// options.onValueInput, optional listener attached to the value input (e.g. to track user edits)
function addKvRow(containerId, options = {}) {
  const c = document.getElementById(containerId);
  if (!c) return;
  const row = document.createElement('div');
  row.className = 'kv-row';
  row.innerHTML = `<input type="text" placeholder="Key" class="kv-key-input" oninput="markDirty()">` +
    `<input type="text" placeholder="Value" class="kv-val-input" oninput="markDirty()">` +
    `<button class="btn-icon" onclick="this.closest('.kv-row').remove(); markDirty();">` +
      `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>` +
    `</button>`;
  if (options.onValueInput) {
    row.querySelector('.kv-val-input').addEventListener('input', options.onValueInput);
  }
  c.appendChild(row);
}

// ── Extract headers from all rows in a container ─────────
function collectHeaders(containerId) {
  const headers = {};
  document.querySelectorAll(`#${containerId} .kv-row`).forEach(row => {
    const k = row.querySelector('.kv-key-input')?.value?.trim();
    const v = row.querySelector('.kv-val-input')?.value?.trim() ?? '';
    if (k) headers[k] = v;
  });
  return headers;
}

// ── Validate that every row with a key also has a value ──
// Returns an error string, or null if valid.
function validateHeaders(containerId) {
  let err = null;
  document.querySelectorAll(`#${containerId} .kv-row`).forEach(row => {
    const k = row.querySelector('.kv-key-input')?.value?.trim();
    const v = row.querySelector('.kv-val-input')?.value?.trim();
    if (k && !v) err = `Header "${k}" must have a value, fill it in or remove the row.`;
  });
  return err;
}
