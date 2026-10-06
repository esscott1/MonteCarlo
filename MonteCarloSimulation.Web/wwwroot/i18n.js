// English/Spanish for the landing page, the Scenario runner and Model Info. Loaded first on each translated page.
//
// - Static page text stays English in the HTML, marked with data-i18n="key" (or data-i18n-title, -placeholder,
//   -aria-label for attributes); the HTML is the English source. Text the scripts build comes from t('key', values),
//   whose English is in i18n/en.json. i18n/es.json has the Spanish for every key.
// - The site always starts in English; the choice is remembered in localStorage ('lang'). A one-line script in each
//   page's <head> hides the page until Spanish is applied, so a Spanish visitor never sees an English flash.
// - Switching language fires 'i18n:change' on document, so pages can re-render what's on screen without re-running.
window.I18n = (function () {
    const STORAGE_KEY = 'lang';
    const ATTRIBUTES = ['title', 'placeholder', 'aria-label'];
    const dictionaries = {};
    let language = readStoredLanguage();

    function readStoredLanguage() {
        try {
            return localStorage.getItem(STORAGE_KEY) === 'es' ? 'es' : 'en';
        } catch {
            return 'en';
        }
    }

    function load(lang) {
        if (!dictionaries[lang]) {
            dictionaries[lang] = fetch(`i18n/${lang}.json`)
                .then((response) => (response.ok ? response.json() : {}))
                .catch(() => ({}));
        }
        return dictionaries[lang];
    }

    let en = {};
    let current = {};

    // Fills {name} placeholders. Values are inserted as given: callers escape anything user-supplied.
    function format(template, values) {
        return values ? template.replace(/\{(\w+)\}/g, (match, name) => (name in values ? String(values[name]) : match)) : template;
    }

    function t(key, values) {
        const template = (language === 'es' ? current[key] : undefined) ?? en[key] ?? key;
        return format(template, values);
    }

    // English for static text is whatever the HTML says, captured the first time an element is translated
    function applyStatic(root = document) {
        root.querySelectorAll('[data-i18n]').forEach((el) => {
            if (el.dataset.i18nEn === undefined) el.dataset.i18nEn = el.innerHTML;
            const spanish = language === 'es' ? current[el.dataset.i18n] : undefined;
            el.innerHTML = spanish ?? el.dataset.i18nEn;
        });
        for (const attribute of ATTRIBUTES) {
            const keyAttribute = `data-i18n-${attribute}`;
            const englishAttribute = `data-i18n-en-${attribute}`;
            root.querySelectorAll(`[${keyAttribute}]`).forEach((el) => {
                if (!el.hasAttribute(englishAttribute)) el.setAttribute(englishAttribute, el.getAttribute(attribute) ?? '');
                const spanish = language === 'es' ? current[el.getAttribute(keyAttribute)] : undefined;
                el.setAttribute(attribute, spanish ?? el.getAttribute(englishAttribute));
            });
        }
        document.documentElement.lang = language;
        document.querySelectorAll('.lang-toggle').forEach((button) => {
            button.textContent = language === 'es' ? 'English' : 'Español';
            button.setAttribute('aria-label', language === 'es' ? 'View in English' : 'Ver en español');
            button.lang = language === 'es' ? 'en' : 'es';
        });
    }

    async function setLanguage(lang) {
        language = lang === 'es' ? 'es' : 'en';
        try {
            localStorage.setItem(STORAGE_KEY, language);
        } catch {
            // Storage unavailable: the choice lasts until the page is left
        }
        if (language === 'es') current = await load('es');
        applyStatic();
        document.dispatchEvent(new CustomEvent('i18n:change', { detail: { language } }));
    }

    // English text built elsewhere (server messages, the Strategy Lab's household descriptions): each en.json template
    // whose key starts with `prefix` (with {placeholders} matching anything) is tried against the text; a match is
    // re-worded in the current language with the captured values. No match returns the text unchanged.
    function translateTemplate(message, prefix) {
        if (language !== 'es' || typeof message !== 'string') return message;
        for (const [key, template] of Object.entries(en)) {
            if (!key.startsWith(prefix)) continue;
            const names = [];
            const pattern = template
                .replace(/[.*+?^$()|[\]\\]/g, '\\$&')
                .replace(/\\?\{(\w+)\\?\}/g, (m, name) => { names.push(name); return '(.+?)'; });
            const match = new RegExp(`^${pattern}$`).exec(message);
            if (match) return t(key, Object.fromEntries(names.map((name, i) => [name, match[i + 1]])));
        }
        return message;
    }

    // Server messages come back in English; their templates are the "server." keys
    function translateServerMessage(message) {
        return translateTemplate(message, 'server.');
    }

    // Text that arrives in English as data (the Strategy Lab's labels): translated under `key` only while en.json's
    // English for it still matches, so changed wording shows in English rather than as a stale translation
    function translateData(key, english) {
        return en[key] === english ? t(key) : english;
    }

    // A field name from a server validation error, as the page labels it
    function fieldName(field) {
        const key = `field.${field}`;
        return en[key] || current[key] ? t(key) : field;
    }

    // Month and year in the page's language ("Jan 2038" / "ene 2038")
    function monthYear(date) {
        return date.toLocaleString(language === 'es' ? 'es-US' : 'en-US', { month: 'short', year: 'numeric', timeZone: 'UTC' });
    }

    const ready = Promise.all([load('en'), language === 'es' ? load('es') : Promise.resolve({})])
        .then(([english, spanish]) => {
            en = english;
            current = spanish;
            applyStatic();
        })
        .finally(() => document.documentElement.classList.remove('i18n-pending'));

    document.addEventListener('click', (e) => {
        if (e.target.closest('.lang-toggle')) setLanguage(language === 'es' ? 'en' : 'es');
    });

    return {
        ready,
        t,
        translateServerMessage,
        translateTemplate,
        translateData,
        fieldName,
        monthYear,
        applyStatic,
        setLanguage,
        get language() { return language; },
    };
})();

// Shorthand for the page scripts
window.t = (key, values) => window.I18n.t(key, values);
