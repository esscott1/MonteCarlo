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
            initFeatures(token, denied);
            return;
        }
    } catch {
        // Falls through to the redirect below - a network error is treated the same as denied.
    }

    denied();
})();

// The Features section: the Plus and Pro switches for the whole site (GET/PUT /api/features, with the same token). Each
// flip saves at once; if saving fails the switch goes back. The token lasts 30 minutes, after which a call is a 401 and
// the page returns to the passphrase.
function initFeatures(token, denied) {
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

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

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

    async function call(method, path, body) {
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
