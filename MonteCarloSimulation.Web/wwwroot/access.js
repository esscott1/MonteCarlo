// The access-code page (access.html), opened in a new tab from a paid-feature badge's flyout via /billing/subscribe.
// While access codes stand in for sign-in and payment, entering a tier's code here (POST /api/tier-access) sets a signed
// cookie for this browser, and "Use the Free version" (DELETE) clears it. Either way the page broadcasts on the
// 'tier-access' channel, so the tab the visitor came from unlocks (or locks) in place. Text comes from i18n.js (t).
(function () {
    const form = document.getElementById('access-form');
    const status = document.getElementById('access-status');
    const tierChoices = document.getElementById('access-tiers');
    const result = document.getElementById('access-result');
    const freeRow = document.getElementById('access-free');
    const useFree = document.getElementById('use-free');
    const submitButton = form.querySelector('button[type="submit"]');
    const quota = window.QuotaNotice.create({
        storageKey: 'quota:tier-access',
        notice: document.getElementById('access-quota'),
        submitButton,
        blockedText: (limit, clock, minutes) => t('access.quota.blocked', {
            limit, clock, relative: minutes < 1 ? t('quota.relative.soon') : t('quota.relative.minutes', { minutes }),
        }),
        remainingText: (remaining, limit) => t('access.quota.remaining', { remaining, limit }),
    });

    let requested = (new URLSearchParams(location.search).get('tier') || '').toLowerCase();
    let me = null;
    // The last message shown under the form, kept so switching language can re-word it: { key, values } or { errors }
    let message = null;

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function announce() {
        try {
            const channel = new BroadcastChannel('tier-access');
            channel.postMessage('changed');
            channel.close();
        } catch {
            // The other tab re-checks when it regains focus
        }
    }

    async function load() {
        try {
            const response = await fetch('/api/me', { cache: 'no-store' });
            me = response.ok ? await response.json() : null;
        } catch {
            me = null;
        }
    }

    function renderMessage() {
        if (!message) {
            result.innerHTML = '';
        } else if (message.errors) {
            result.innerHTML = `<div class="error-box"><p>${escapeHtml(message.errors.map(I18n.translateServerMessage).join(' '))}</p></div>`;
        } else {
            result.innerHTML = `<div class="summary-box ok"><p>${t(message.key, message.values)}</p></div>`;
        }
    }

    function render() {
        renderMessage();
        if (!me) {
            status.textContent = t('access.unavailable');
            form.hidden = true;
            freeRow.hidden = true;
            return;
        }
        if (!me.gated) {
            status.textContent = t('access.notGated');
            form.hidden = true;
            freeRow.hidden = true;
            return;
        }

        status.textContent = me.tier === 'Free' ? t('access.statusFree') : t('access.statusTier', { tier: me.tier });
        freeRow.hidden = me.tier === 'Free';
        form.hidden = me.offers.length === 0;
        if (form.hidden) return;

        const checked = tierChoices.querySelector('input:checked')?.value
            ?? (me.offers.some((o) => o.tier.toLowerCase() === requested) ? requested : me.offers[0].tier.toLowerCase());
        tierChoices.innerHTML = me.offers.map((offer) => {
            const value = offer.tier.toLowerCase();
            return `<label class="checkbox-label"><input type="radio" name="tier" value="${escapeHtml(value)}"${value === checked ? ' checked' : ''}>`
                + `${escapeHtml(t('access.tierOption', { tier: offer.tier, price: offer.priceLabel }))}</label>`;
        }).join('');
    }

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (quota.isBlocked()) {
            quota.restore();
            return;
        }
        const tier = form.elements.tier?.value;
        if (!tier) return;

        submitButton.disabled = true;
        message = null;
        renderMessage();
        try {
            const response = await fetch('/api/tier-access', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tier, code: form.elements.code.value }),
            });
            const data = await response.json().catch(() => ({}));
            quota.update(response, data);
            if (response.status === 429) return;
            if (!response.ok) {
                message = { errors: data.errors ? Object.values(data.errors).flat() : [data.message || 'The request failed.'] };
                return;
            }
            form.elements.code.value = '';
            requested = tier;
            message = { key: 'access.success', values: { tier: data.tier } };
            announce();
            await load();
        } catch (err) {
            message = { errors: [err.message] };
        } finally {
            submitButton.disabled = quota.isBlocked();
            render();
        }
    });

    useFree.addEventListener('click', async () => {
        useFree.disabled = true;
        try {
            await fetch('/api/tier-access', { method: 'DELETE' });
            message = { key: 'access.backToFree' };
            announce();
            await load();
        } catch (err) {
            message = { errors: [err.message] };
        } finally {
            useFree.disabled = false;
            render();
        }
    });

    document.addEventListener('i18n:change', () => {
        render();
        quota.restore();
    });

    Promise.all([load(), I18n.ready]).then(() => {
        render();
        quota.restore();
    });
})();
