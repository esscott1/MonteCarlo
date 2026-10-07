// Light or dark. The site follows the computer's setting until the visitor picks one with the header's toggle (beside
// the language button); the choice is then remembered ('theme' in localStorage) on every page. Loaded in each page's
// <head>, so a saved choice is on <html> (data-theme) before the page draws; styles.css gives the dark colours both to
// the computer's dark setting (unless data-theme="light") and to data-theme="dark".
(function () {
    const STORAGE_KEY = 'theme';
    const root = document.documentElement;
    try {
        const saved = localStorage.getItem(STORAGE_KEY);
        if (saved === 'light' || saved === 'dark') root.dataset.theme = saved;
    } catch {
        // Storage unavailable: follow the computer's setting
    }

    const systemDark = window.matchMedia?.('(prefers-color-scheme: dark)');
    const current = () => root.dataset.theme ?? (systemDark?.matches ? 'dark' : 'light');

    // The button names the mode it switches to. Its English is in the HTML until the page's text has loaded.
    function label() {
        if (!window.I18n) return;
        const toDark = current() === 'light';
        document.querySelectorAll('.theme-toggle').forEach((button) => {
            button.textContent = toDark ? `☾ ${t('theme.dark')}` : `☀ ${t('theme.light')}`;
            button.setAttribute('aria-label', toDark ? t('theme.toDark') : t('theme.toLight'));
        });
    }

    function set(theme) {
        root.dataset.theme = theme;
        try {
            localStorage.setItem(STORAGE_KEY, theme);
        } catch {
            // Storage unavailable: the choice lasts until the page is left
        }
        label();
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('.theme-toggle')) set(current() === 'dark' ? 'light' : 'dark');
    });
    document.addEventListener('DOMContentLoaded', () => window.I18n?.ready.then(label));
    document.addEventListener('i18n:change', label);
    systemDark?.addEventListener?.('change', label);
})();
