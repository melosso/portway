// Owns console UI settings in localStorage: theme and sidebar state, shared by the sign-in page, the landing page and the console
const UI_SETTINGS_KEY = 'portway_settings';
const LEGACY_UI_SETTINGS_KEY = 'beacon_settings';

function uiSettings() {
    try {
        return JSON.parse(
            localStorage.getItem(UI_SETTINGS_KEY) ?? localStorage.getItem(LEGACY_UI_SETTINGS_KEY) ?? '{}'
        );
    } catch {
        return {};
    }
}

function saveUiSettings(patch) {
    try {
        localStorage.setItem(UI_SETTINGS_KEY, JSON.stringify({ ...uiSettings(), ...patch }));
        localStorage.removeItem(LEGACY_UI_SETTINGS_KEY);
    } catch {}
}

let _themeChoice = uiSettings().theme || 'system';

function themeChoice() {
    return _themeChoice;
}

function isDarkTheme() {
    const t = themeChoice();
    return t === 'dark' || (t === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
}

function applyTheme(choice) {
    _themeChoice = choice;
    saveUiSettings({ theme: choice });
    if (choice === 'system') document.documentElement.removeAttribute('data-theme');
    else document.documentElement.setAttribute('data-theme', choice);
    paintThemeToggles();
    document.dispatchEvent(new CustomEvent('portway:theme'));
}

function toggleTheme() {
    applyTheme(isDarkTheme() ? 'light' : 'dark');
}

// The icon shows the theme a click switches to, matching the label
function _themeIcon(dark, size) {
    return dark
        ? `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>`
        : `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/></svg>`;
}

function paintThemeToggles() {
    const dark = isDarkTheme();
    const label = dark ? 'Switch to light theme' : 'Switch to dark theme';
    document.querySelectorAll('[data-theme-toggle]').forEach((btn) => {
        btn.innerHTML = _themeIcon(!dark, btn.dataset.iconSize || 16);
        btn.setAttribute('aria-label', label);
        btn.dataset.tooltip = label;
    });
}

(function () {
    const t = themeChoice();
    if (t !== 'system') document.documentElement.setAttribute('data-theme', t);
})();

window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', paintThemeToggles);
document.addEventListener('DOMContentLoaded', paintThemeToggles);
document.addEventListener('click', (e) => {
    if (e.target.closest?.('[data-theme-toggle]')) toggleTheme();
});
