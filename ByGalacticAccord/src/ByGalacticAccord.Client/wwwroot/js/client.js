// Small browser helpers for the Blazor client. Storage can throw or be empty (private windows, blocked
// site data), so every access is guarded and the page works without it.

const key = 'bga-theme';

export function storedTheme() {
    try {
        const theme = localStorage.getItem(key);
        return theme === 'dark' || theme === 'light' ? theme : 'system';
    } catch {
        return 'system';
    }
}

export function setTheme(theme) {
    const root = document.documentElement;
    if (theme === 'dark' || theme === 'light') {
        root.dataset.theme = theme;
    } else {
        delete root.dataset.theme;
    }

    try {
        localStorage.setItem(key, theme);
    } catch {
        // Not remembered; the choice still applies to this page.
    }
}
