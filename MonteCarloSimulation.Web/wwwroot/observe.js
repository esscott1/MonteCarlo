// Verifies the session token issued by the passphrase flyout on the landing page before
// revealing anything here. A missing/invalid/expired token - including a direct visit to
// this URL, or one forged in DevTools - bounces straight back to the landing page, which
// reopens the passphrase flyout automatically (see menu.js's `observe=denied` handling).
(async function verifyObserveAccess() {
    const token = sessionStorage.getItem('observeToken');
    const denied = () => window.location.replace('/?observe=denied');

    try {
        const response = await fetch('/api/observe-access/verify', {
            headers: token ? { 'X-Observe-Token': token } : {}
        });

        if (response.ok) {
            document.getElementById('observe-content').hidden = false;
            const call = observeCall(token, denied);
            initSections();
            initFeatures(call);
            initVisits(call);
            return;
        }
    } catch {
        // Falls through to the redirect below - a network error is treated the same as denied.
    }

    denied();
})();

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// Calls an Observe API with the token. The token lasts 30 minutes, after which a call is a 401 and the page returns to
// the passphrase.
function observeCall(token, denied) {
    return async function call(method, path, body) {
        const response = await fetch(path, {
            method,
            headers: { 'X-Observe-Token': token, ...(body ? { 'Content-Type': 'application/json' } : {}) },
            body: body ? JSON.stringify(body) : undefined,
        });
        if (response.status === 401) {
            denied();
            return null;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    };
}

// Each section opens and closes on its own (<details>), and the page remembers which were left open
function initSections() {
    const KEY = 'observeSections';
    let saved = {};
    try { saved = JSON.parse(localStorage.getItem(KEY)) ?? {}; } catch { /* storage unavailable: the HTML defaults */ }
    document.querySelectorAll('details.observe-section').forEach((section) => {
        const name = section.dataset.section;
        if (typeof saved[name] === 'boolean') section.open = saved[name];
        section.addEventListener('toggle', () => {
            saved[name] = section.open;
            try { localStorage.setItem(KEY, JSON.stringify(saved)); } catch { /* not remembered */ }
        });
    });
}

// The Features section: the Plus and Pro switches for the whole site (GET/PUT /api/features, with the same token). Each
// flip saves at once; if saving fails the switch goes back.
function initFeatures(call) {
    const list = document.getElementById('features-list');
    const mode = document.getElementById('features-mode');
    const status = document.getElementById('features-status');

    const MODES = {
        FreeOnly: ['Free Only', 'Everyone has the Free version: paid features are greyed, with no badges and no way to upgrade.'],
        PlusAvailable: ['Plus Available', 'Paid features carry Plus badges; a Plus code unlocks them for that visitor\'s session.'],
        ProAvailable: ['Pro Available', 'Paid features carry Pro badges; a Pro code unlocks them for that visitor\'s session.'],
        PlusAndProAvailable: ['Plus and Pro Available', 'Each badge names the cheapest tier with that feature; either code unlocks its tier.'],
    };
    const WHAT = {
        Plus: ['Offered: Plus badges on its features, and the Plus code works.', 'Not offered: no Plus badges, and the Plus code is refused.'],
        Pro: ['Offered: Pro badges where Pro is the cheapest tier on offer, and the Pro code works.', 'Not offered: no Pro badges, and the Pro code is refused.'],
    };

    const when = (iso) => new Date(iso).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });

    function render(view) {
        const [name, description] = MODES[view.mode] ?? [view.mode, ''];
        mode.innerHTML = `Site mode: <strong>${escapeHtml(name)}</strong>. ${escapeHtml(description)}`;
        list.innerHTML = view.tiers.map((tier) => {
            const [on, off] = WHAT[tier.tier] ?? ['', ''];
            const changed = tier.changedAt ? `Last changed ${escapeHtml(when(tier.changedAt))}` : 'Default (never switched here)';
            return `
                <div class="feature-switch ${tier.tier.toLowerCase()}">
                    <label class="feature-switch-label">
                        <input type="checkbox" role="switch" data-tier="${escapeHtml(tier.tier.toLowerCase())}"${tier.on ? ' checked' : ''}>
                        <span class="feature-switch-track" aria-hidden="true"></span>
                        <span><strong>${escapeHtml(tier.tier)} available</strong> &middot; ${escapeHtml(tier.priceLabel)}</span>
                    </label>
                    <p class="feature-meta">${escapeHtml(tier.on ? on : off)}</p>
                    <p class="feature-meta">Includes: ${tier.features.map(escapeHtml).join(', ')}</p>
                    <p class="feature-meta">${changed}</p>
                </div>`;
        }).join('');
    }

    list.addEventListener('change', async (e) => {
        const input = e.target.closest('input[role="switch"]');
        if (!input) return;
        input.disabled = true;
        status.textContent = 'Saving…';
        try {
            const view = await call('PUT', `/api/features/${input.dataset.tier}`, { on: input.checked });
            if (!view) return;
            render(view);
            status.textContent = `Saved: the site is now ${MODES[view.mode]?.[0] ?? view.mode}.`;
        } catch (err) {
            input.checked = !input.checked;
            input.disabled = false;
            status.textContent = `Couldn't save (${err.message}); the switch is back where it was.`;
        }
    });

    call('GET', '/api/features')
        .then((view) => { if (view) render(view); })
        .catch((err) => { status.textContent = `Couldn't load the switches (${err.message}).`; });
}

// The Visits section (GET /api/visits): a bar a day for the last 7, 30 or 90 days, each range's total on its button, and
// the average a day over the last 30. Days are the server's (Mountain time), sent as yyyy-mm-dd and shown as is.
function initVisits(call) {
    const ranges = document.getElementById('visits-ranges');
    const chart = document.getElementById('visits-chart');
    const average = document.getElementById('visits-average');
    const status = document.getElementById('visits-status');
    const RANGE_KEY = 'observeVisitsRange';
    const RANGES = [7, 30, 90];
    const MARGIN = { top: 12, right: 8, bottom: 26, left: 36 };

    let range = 30;
    try { range = RANGES.find((n) => n === Number(localStorage.getItem(RANGE_KEY))) ?? 30; } catch { /* the default */ }
    let view = null;

    const asDate = (iso) => new Date(`${iso}T00:00:00Z`);
    const day = (iso, options) => asDate(iso).toLocaleDateString([], { timeZone: 'UTC', ...options });
    const count = (n) => n.toLocaleString();
    const plural = (n, word) => `${count(n)} ${word}${n === 1 ? '' : 's'}`;
    const totalFor = (n) => ({ 7: view.last7, 30: view.last30, 90: view.last90 }[n]);

    // A round step for the gridlines: whole visits, about four lines
    function niceStep(highest) {
        const rough = Math.max(1, highest / 4);
        const power = 10 ** Math.floor(Math.log10(rough));
        return [1, 2, 5, 10].map((m) => m * power).find((step) => step >= rough);
    }

    // A bar anchored on the baseline with its top corners rounded
    function bar(x, y, w, base) {
        const r = Math.min(4, w / 2, base - y);
        return `M${x},${base}V${y + r}Q${x},${y} ${x + r},${y}H${x + w - r}Q${x + w},${y} ${x + w},${y + r}V${base}Z`;
    }

    function renderRanges() {
        ranges.innerHTML = RANGES.map((n) => `
            <button type="button" role="radio" class="visits-range" data-range="${n}" aria-checked="${n === range}" tabindex="${n === range ? 0 : -1}">
                ${n} days <span class="visits-range-total">${count(totalFor(n))}</span>
            </button>`).join('');
    }

    function renderChart() {
        const days = view.days.slice(-range);
        const width = Math.max(260, chart.clientWidth);
        const height = width < 500 ? 200 : 240;
        const plotWidth = width - MARGIN.left - MARGIN.right;
        const plotHeight = height - MARGIN.top - MARGIN.bottom;
        const base = MARGIN.top + plotHeight;

        const highest = Math.max(1, ...days.map((d) => d.visits));
        const step = niceStep(highest);
        const top = Math.ceil(highest / step) * step;
        const y = (value) => base - (value / top) * plotHeight;

        const slot = plotWidth / days.length;
        const gap = Math.min(2, slot * 0.25);
        const x = (i) => MARGIN.left + i * slot + gap / 2;
        const middle = (i) => x(i) + (slot - gap) / 2;

        let grid = '';
        for (let value = 0; value <= top; value += step) {
            grid += `<line class="chart-grid" x1="${MARGIN.left}" x2="${width - MARGIN.right}" y1="${y(value)}" y2="${y(value)}"/>`
                + `<text class="chart-label" x="${MARGIN.left - 6}" y="${y(value)}" text-anchor="end" dominant-baseline="middle">${count(value)}</text>`;
        }

        // Date labels counted back from today, every 1, 2, 7 or 14 days so they don't collide; today's is right-aligned
        // when centring it would run past the edge
        const labelEvery = [1, 2, 7, 14, 30].find((n) => n * slot >= 52) ?? 30;
        let labels = '';
        days.forEach((d, i) => {
            if ((days.length - 1 - i) % labelEvery !== 0) return;
            const atEdge = i === days.length - 1 && middle(i) + 22 > width;
            labels += `<line class="chart-tick" x1="${middle(i)}" x2="${middle(i)}" y1="${base}" y2="${base + 4}"/>`
                + `<text class="chart-label" x="${atEdge ? width : middle(i)}" y="${height - 6}" text-anchor="${atEdge ? 'end' : 'middle'}">`
                + `${escapeHtml(day(d.date, { month: 'short', day: 'numeric' }))}</text>`;
        });

        // Today's bar is lighter: the day isn't over
        const bars = days.map((d, i) => d.visits > 0
            ? `<path class="visits-bar${d.date === view.today ? ' today' : ''}" data-i="${i}" d="${bar(x(i), y(d.visits), slot - gap, base)}"/>`
            : '').join('');
        // Each day's hit area is its whole column, so a short bar (or a day with none) is easy to point at
        const hits = days.map((d, i) =>
            `<rect class="visits-hit" data-i="${i}" x="${MARGIN.left + i * slot}" y="${MARGIN.top}" width="${slot}" height="${plotHeight}"/>`).join('');

        const label = `Visits per day, ${day(days[0].date, { month: 'short', day: 'numeric' })} to `
            + `${day(view.today, { month: 'short', day: 'numeric' })}: ${plural(totalFor(range), 'visit')} in all`;
        chart.innerHTML = `
            <svg class="balance-chart visits-chart" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" role="img" aria-label="${escapeHtml(label)}">
                ${grid}
                <line class="chart-axis" x1="${MARGIN.left}" x2="${width - MARGIN.right}" y1="${base}" y2="${base}"/>
                ${labels}
                ${bars}
                ${hits}
            </svg>
            <div class="chart-tooltip" hidden></div>`;

        // Hover or touch: the day's count, with its bar highlighted
        const svg = chart.querySelector('svg');
        const tooltip = chart.querySelector('.chart-tooltip');
        const clear = () => svg.querySelectorAll('.visits-bar.active').forEach((b) => b.classList.remove('active'));
        const show = (event) => {
            const i = Number(event.target.dataset.i);
            const d = days[i];
            clear();
            svg.querySelector(`.visits-bar[data-i="${i}"]`)?.classList.add('active');
            tooltip.innerHTML = `<strong>${escapeHtml(day(d.date, { weekday: 'short', month: 'short', day: 'numeric' }))}</strong>`
                + `<span>${escapeHtml(plural(d.visits, 'visit'))}${d.date === view.today ? ' so far' : ''}</span>`;
            tooltip.hidden = false;
            const box = svg.getBoundingClientRect();
            const left = (middle(i) / width) * box.width;
            tooltip.style.left = `${Math.min(Math.max(left - tooltip.offsetWidth / 2, 0), box.width - tooltip.offsetWidth)}px`;
        };
        svg.querySelectorAll('.visits-hit').forEach((hit) => {
            hit.addEventListener('pointerenter', show);
            hit.addEventListener('pointerdown', show);
        });
        svg.addEventListener('pointerleave', () => { tooltip.hidden = true; clear(); });
    }

    function renderAverage() {
        const since = view.countingSince;
        const counted = since ? Math.min(30, Math.round((asDate(view.today) - asDate(since)) / 864e5) + 1) : 30;
        average.innerHTML = `Average per day, last 30 days: <strong>${escapeHtml(view.averageDaily30.toLocaleString([], { maximumFractionDigits: 1 }))}</strong>`
            + (since && counted < 30
                ? ` <span class="visits-since">(counting since ${escapeHtml(day(since, { month: 'short', day: 'numeric', year: 'numeric' }))}, so over ${plural(counted, 'day')})</span>`
                : '');
    }

    function render() {
        renderRanges();
        renderChart();
        renderAverage();
    }

    function choose(n) {
        range = n;
        try { localStorage.setItem(RANGE_KEY, String(n)); } catch { /* not remembered */ }
        render();
        ranges.querySelector(`[data-range="${n}"]`).focus();
    }

    ranges.addEventListener('click', (e) => {
        const button = e.target.closest('[data-range]');
        if (button && view) choose(Number(button.dataset.range));
    });
    // Arrow keys move between the ranges, as in any radio group
    ranges.addEventListener('keydown', (e) => {
        const move = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
        if (!move || !view) return;
        e.preventDefault();
        choose(RANGES[(RANGES.indexOf(range) + move + RANGES.length) % RANGES.length]);
    });

    // The chart's width follows the section; a closed section has no width, so it's drawn again when opened
    let resizeTimer = null;
    window.addEventListener('resize', () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => { if (view) renderChart(); }, 150);
    });
    document.getElementById('visits-section').addEventListener('toggle', (e) => { if (e.target.open && view) renderChart(); });

    call('GET', '/api/visits')
        .then((result) => {
            if (!result) return;
            view = result;
            document.getElementById('visits-zone').textContent = view.timeZone === 'America/Denver' ? 'Mountain time' : view.timeZone;
            render();
        })
        .catch((err) => { status.textContent = `Couldn't load the visits (${err.message}).`; });
}
