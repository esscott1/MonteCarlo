// The Translations page: every translated string side by side (key, where it appears, English, Spanish), the Spanish
// editable. Edits are kept as a draft in this browser, can be previewed on any translated page (i18n.js reads the
// 'i18n-preview' entry), and are submitted to /api/translations/proposal, which turns them into a Jira story labelled
// agent-translation-update; the agent-translation-update workflow then opens a pull request applying them to es.json.
//
// Behind the shared passphrase: unlocking uses the Observe access flow (/api/observe-access, same passphrase and
// hourly limit), and the passphrase is kept in memory for Submit. The page itself stays in English, like Observe.
// Rows are built the way tools/i18n/review-sheet.mjs builds the review sheet: static page text first (from the pages'
// data-i18n attributes), then the keys in en.json.
(function () {
    const PAGES = ['optimal.html', 'index.html', 'model-info.html', 'access.html'];
    const ATTRIBUTES = ['title', 'placeholder', 'aria-label'];
    const DRAFT_KEY = 'translationDraft';
    const PREVIEW_KEY = 'i18n-preview';
    const MAX_VALUE_LENGTH = 1000;
    const ALLOWED_TAGS = ['<strong>', '</strong>', '<em>', '</em>'];

    const unlockForm = document.getElementById('unlock-form');
    const unlockResult = document.getElementById('unlock-result');
    const main = document.getElementById('translations');
    const rowsBody = document.getElementById('rows');
    const search = document.getElementById('search');
    const where = document.getElementById('where');
    const changedOnly = document.getElementById('changed-only');
    const counts = document.getElementById('counts');
    const submitForm = document.getElementById('submit-form');
    const submitButton = document.getElementById('submit');
    const submitSummary = document.getElementById('submit-summary');
    const submitResult = document.getElementById('submit-result');
    const passphraseField = document.getElementById('submit-passphrase-field');

    let passphrase = null;      // kept in memory once typed, for Submit
    let entries = [];           // [{ key, where, english, spanish }]
    let draft = readJson(DRAFT_KEY) ?? {};
    let serverErrors = {};      // key -> message from the last refused Submit

    function readJson(key) {
        try {
            const value = JSON.parse(localStorage.getItem(key) || 'null');
            return value && typeof value === 'object' ? value : null;
        } catch {
            return null;
        }
    }

    function writeJson(key, value) {
        try {
            if (value && Object.keys(value).length > 0) localStorage.setItem(key, JSON.stringify(value));
            else localStorage.removeItem(key);
        } catch {
            // Storage unavailable: edits last until the page is closed
        }
    }

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // ---- the rules (the same as TranslationRules.Check on the server and apply-translation-update.mjs)

    const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
    const tags = (text) => [...text.matchAll(/<[^<>]*>/g)].map((m) => m[0]);

    function check(value, current) {
        if (value.trim().length === 0) return "can't be empty.";
        if (value.length > MAX_VALUE_LENGTH) return `must be ${MAX_VALUE_LENGTH} characters or fewer.`;
        const expected = placeholders(current);
        if (JSON.stringify(placeholders(value)) !== JSON.stringify(expected)) {
            return `must keep the same {placeholders} as the current text (${expected.length ? expected.map((n) => `{${n}}`).join(', ') : 'none'}).`;
        }
        const currentTags = new Set(tags(current));
        for (const tag of tags(value)) {
            if (!ALLOWED_TAGS.includes(tag) && !currentTags.has(tag)) return `can only use <strong> and <em> for formatting (found ${tag}).`;
        }
        if (/[<>]/.test(value.replace(/<[^<>]*>/g, ''))) return "can't contain < or > outside a formatting tag.";
        return null;
    }

    // ---- loading the strings

    // The part of the site a script-built key belongs to (as the review sheet labels it)
    function scriptArea(key) {
        if (key.startsWith('optimal.')) return 'optimal.js (landing page results)';
        if (key.startsWith('runner.') || key.startsWith('flyout.') || key.startsWith('quota.')) return 'app.js (Scenario runner)';
        if (key.startsWith('server.') || key.startsWith('field.')) return 'server messages and field names';
        if (key.startsWith('paywall.')) return 'paywall.js (paid-feature badges)';
        if (key.startsWith('access.')) return 'access.js (access-code page)';
        if (key.startsWith('info.')) return 'model-info.js (Model Info)';
        if (key.startsWith('lab.')) return 'Model Info (Strategy Lab data)';
        return 'shared';
    }

    async function loadEntries() {
        const fetchText = (url) => fetch(url).then((r) => {
            if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
            return r.text();
        });
        const [en, es, ...pages] = await Promise.all([
            fetchText('i18n/en.json').then(JSON.parse),
            fetchText('i18n/es.json').then(JSON.parse),
            ...PAGES.map(fetchText),
        ]);

        const list = [];
        const seen = new Set();
        const add = (key, area, english) => {
            if (seen.has(key) || !(key in es)) return;
            seen.add(key);
            list.push({ key, where: area, english, spanish: es[key] });
        };
        pages.forEach((html, i) => {
            const doc = new DOMParser().parseFromString(html, 'text/html');
            doc.querySelectorAll('[data-i18n]').forEach((el) => add(el.dataset.i18n, PAGES[i], el.innerHTML.trim()));
            for (const attribute of ATTRIBUTES) {
                doc.querySelectorAll(`[data-i18n-${attribute}]`).forEach((el) => add(el.getAttribute(`data-i18n-${attribute}`), PAGES[i], el.getAttribute(attribute) ?? ''));
            }
        });
        for (const [key, english] of Object.entries(en)) add(key, scriptArea(key), english);
        return list;
    }

    // ---- the table

    const changedKeys = () => Object.keys(draft).filter((key) => entries.some((e) => e.key === key && e.spanish !== draft[key]));

    function rowHtml(entry) {
        const value = draft[entry.key] ?? entry.spanish;
        const changed = value !== entry.spanish;
        const problem = serverErrors[entry.key] ?? (changed ? check(value, entry.spanish) : null);
        // English is the site's own text, shown as the page renders it; the Spanish is edited as source, tags included
        return `<tr data-key="${escapeHtml(entry.key)}" class="${changed ? 'changed' : ''}">
            <td class="translation-key">${escapeHtml(entry.key)}<span class="info-meta">${escapeHtml(entry.where)}</span></td>
            <td class="translation-english">${entry.english}</td>
            <td><textarea rows="${Math.min(8, Math.max(2, Math.ceil(value.length / 60)))}" aria-label="Spanish for ${escapeHtml(entry.key)}">${escapeHtml(value)}</textarea>
                <p class="translation-error" ${problem ? '' : 'hidden'}>${problem ? escapeHtml(problem) : ''}</p></td>
        </tr>`;
    }

    function normalize(text) {
        return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    }

    function visibleEntries() {
        const term = normalize(search.value.trim());
        return entries.filter((entry) => {
            if (where.value && entry.where !== where.value) return false;
            const value = draft[entry.key] ?? entry.spanish;
            if (changedOnly.checked && value === entry.spanish) return false;
            return !term || [entry.key, entry.english, value].some((text) => normalize(text).includes(term));
        });
    }

    function renderRows() {
        const shown = visibleEntries();
        rowsBody.innerHTML = shown.map(rowHtml).join('') || '<tr><td colspan="3" class="info-meta">No strings match.</td></tr>';
        renderCounts();
    }

    function problems() {
        return changedKeys().map((key) => {
            const entry = entries.find((e) => e.key === key);
            return [key, check(draft[key], entry.spanish)];
        }).filter(([, problem]) => problem);
    }

    function renderCounts() {
        const changed = changedKeys().length;
        const broken = problems().length;
        counts.textContent = `${entries.length} strings · ${changed} changed`;
        submitSummary.textContent = changed === 0
            ? 'No changes yet.'
            : `${changed} change${changed === 1 ? '' : 's'} ready to submit${broken ? ` - fix ${broken} first (marked in red)` : ''}.`;
        submitButton.disabled = changed === 0 || broken > 0 || quota.isBlocked();
    }

    rowsBody.addEventListener('input', (e) => {
        const textarea = e.target.closest('textarea');
        if (!textarea) return;
        const row = textarea.closest('tr');
        const key = row.dataset.key;
        const entry = entries.find((x) => x.key === key);
        if (textarea.value === entry.spanish) delete draft[key];
        else draft[key] = textarea.value;
        delete serverErrors[key];
        writeJson(DRAFT_KEY, draft);

        const changed = key in draft;
        const problem = changed ? check(textarea.value, entry.spanish) : null;
        row.classList.toggle('changed', changed);
        const error = row.querySelector('.translation-error');
        error.hidden = !problem;
        error.textContent = problem ?? '';
        renderCounts();
    });

    search.addEventListener('input', renderRows);
    where.addEventListener('change', renderRows);
    changedOnly.addEventListener('change', renderRows);

    document.getElementById('discard').addEventListener('click', () => {
        if (changedKeys().length > 0 && !window.confirm('Discard all your unsubmitted changes?')) return;
        draft = {};
        serverErrors = {};
        writeJson(DRAFT_KEY, null);
        writeJson(PREVIEW_KEY, null);
        renderRows();
    });

    // Preview: the translated pages show these edits in Spanish (i18n.js), with a banner to stop, until this page
    // submits or discards them
    document.getElementById('preview').addEventListener('click', () => {
        const edits = Object.fromEntries(changedKeys().map((key) => [key, draft[key]]));
        writeJson(PREVIEW_KEY, edits);
        try {
            localStorage.setItem('lang', 'es');
        } catch {
            // Without storage the preview page opens in English; its Español button still shows the edits
        }
        window.open(document.getElementById('preview-page').value, '_blank');
    });

    // ---- Submit

    const quota = window.QuotaNotice.create({
        storageKey: 'quota:translations',
        notice: document.getElementById('submit-quota'),
        submitButton,
        blockedText: (limit, clock, minutes) =>
            `You've used all ${limit} submissions for this hour from this network. You can submit again at ${clock} (${minutes < 1 ? 'in less than a minute' : `in ${minutes} min`}).`,
        remainingText: (remaining, limit) => `${remaining} of ${limit} submissions left this hour.`,
    });

    submitForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (quota.isBlocked()) {
            quota.restore();
            return;
        }
        const keys = changedKeys();
        const typed = submitForm.elements.passphrase.value;
        const phrase = passphrase ?? typed;
        if (!phrase) {
            submitResult.innerHTML = '<div class="error-box"><p>Enter the passphrase to submit.</p></div>';
            return;
        }

        submitButton.disabled = true;
        submitResult.innerHTML = '<p class="loading">Submitting&hellip;</p>';
        try {
            const response = await fetch('/api/translations/proposal', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ passphrase: phrase, note: submitForm.elements.note.value, edits: Object.fromEntries(keys.map((key) => [key, draft[key]])) }),
            });
            const data = await response.json().catch(() => ({}));
            quota.update(response, data);
            if (response.status === 429) {
                submitResult.innerHTML = '';
                return;
            }
            if (response.status === 401) {
                passphrase = null;
                passphraseField.hidden = false;
                submitResult.innerHTML = '<div class="error-box"><p>Incorrect passphrase.</p></div>';
                return;
            }
            if (response.status === 400 && data.errors) {
                // Per-key problems ("edits[key]") are shown on their rows; anything else here
                const general = [];
                serverErrors = {};
                for (const [field, messages] of Object.entries(data.errors)) {
                    const match = /^edits\[(.+)\]$/.exec(field);
                    if (match) serverErrors[match[1]] = messages[0].replace(`${match[1]} `, '');
                    else general.push(messages[0]);
                }
                changedOnly.checked = true;
                renderRows();
                submitResult.innerHTML = `<div class="error-box"><p>${escapeHtml(general.join(' ') || 'Some changes break the rules; they are marked in red above.')}</p></div>`;
                return;
            }
            if (!response.ok) {
                submitResult.innerHTML = `<div class="error-box"><p>${escapeHtml(data.message || 'The translation update could not be submitted. Please try again.')}</p></div>`;
                return;
            }

            if (typed) passphrase = typed;
            draft = {};
            serverErrors = {};
            writeJson(DRAFT_KEY, null);
            writeJson(PREVIEW_KEY, null);
            submitForm.elements.note.value = '';
            renderRows();
            submitResult.innerHTML = `<div class="summary-box ok"><p>Submitted ${data.count} change${data.count === 1 ? '' : 's'} as
                <a href="${escapeHtml(data.issueUrl)}" target="_blank" rel="noopener">${escapeHtml(data.issueKey)}</a>. A pull request with them
                opens in a few minutes and is linked from the story; they go live once it's merged.</p></div>`;
        } catch (err) {
            submitResult.innerHTML = `<div class="error-box"><p>Request failed: ${escapeHtml(err.message)}</p></div>`;
        } finally {
            renderCounts();
        }
    });

    // ---- unlocking

    const unlockQuota = window.QuotaNotice.create({
        storageKey: 'quota:observe-access',
        notice: document.getElementById('unlock-quota'),
        submitButton: unlockForm.querySelector('button[type="submit"]'),
        blockedText: (limit, clock, minutes) =>
            `You've used all ${limit} passphrase attempts for this hour from this network. You can try again at ${clock} (${minutes < 1 ? 'in less than a minute' : `in ${minutes} min`}).`,
        remainingText: (remaining, limit) => `${remaining} of ${limit} attempts left this hour.`,
    });

    async function open() {
        unlockForm.hidden = true;
        main.hidden = false;
        passphraseField.hidden = passphrase !== null;
        rowsBody.innerHTML = '<tr><td colspan="3" class="loading">Loading the translations&hellip;</td></tr>';
        try {
            entries = await loadEntries();
        } catch (err) {
            rowsBody.innerHTML = `<tr><td colspan="3"><div class="error-box"><p>Couldn't load the translations (${escapeHtml(err.message)}).</p></div></td></tr>`;
            return;
        }
        // Drop draft edits for keys that no longer exist or now match the live Spanish
        for (const key of Object.keys(draft)) {
            const entry = entries.find((x) => x.key === key);
            if (!entry || entry.spanish === draft[key]) delete draft[key];
        }
        writeJson(DRAFT_KEY, draft);
        const areas = [...new Set(entries.map((x) => x.where))];
        where.innerHTML = '<option value="">All parts of the site</option>' + areas.map((a) => `<option>${escapeHtml(a)}</option>`).join('');
        renderRows();
        quota.restore();
    }

    unlockForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (unlockQuota.isBlocked()) {
            unlockQuota.restore();
            return;
        }
        const typed = unlockForm.elements.passphrase.value;
        unlockResult.innerHTML = '<p class="loading">Checking passphrase&hellip;</p>';
        try {
            const response = await fetch('/api/observe-access', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ passphrase: typed }),
            });
            const data = await response.json().catch(() => ({}));
            unlockQuota.update(response, data);
            if (response.status === 429) {
                unlockResult.innerHTML = '';
                return;
            }
            if (!response.ok) {
                unlockResult.innerHTML = `<div class="error-box"><p>${escapeHtml(data.message || 'Incorrect passphrase.')}</p></div>`;
                return;
            }
            sessionStorage.setItem('observeToken', data.token);
            passphrase = typed;
            unlockResult.innerHTML = '';
            await open();
        } catch (err) {
            unlockResult.innerHTML = `<div class="error-box"><p>Request failed: ${escapeHtml(err.message)}</p></div>`;
        }
    });

    // A token from earlier in this browser session (here or on Observe) skips the passphrase until Submit
    (async function start() {
        const token = sessionStorage.getItem('observeToken');
        if (token) {
            try {
                const response = await fetch('/api/observe-access/verify', { headers: { 'X-Observe-Token': token } });
                if (response.ok) {
                    await open();
                    return;
                }
            } catch {
                // Falls through to the passphrase
            }
        }
        unlockForm.hidden = false;
        unlockQuota.restore();
        unlockForm.elements.passphrase.focus();
    })();
})();
