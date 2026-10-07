// Paid tiers on the Scenario runner and the Optimizer: what this visitor may use (GET /api/me), the paid-feature
// badges with their price flyouts, and the inputs a Free visitor can't change. Loaded after i18n.js, before the
// page's own script. The server enforces every gate; this only keeps the pages in step with it.
//
// Markup it reads:
//   <span class="paid-badge" data-feature="custom-returns" hidden></span>   a badge, shown while the feature is locked
//   data-requires="custom-returns" on an input                            locked at its Free value while locked:
//       data-free-value="0" (or "false" for a checkbox), else the value the page loaded with
//   data-requires="..." data-locked="hide" on any element                 hidden while locked
//   data-free-only="..." on any element                                   shown only while locked
//
// A badge's link opens the access-code page (/billing/subscribe, which becomes Stripe Checkout later) in a new tab, so
// the visitor keeps their place. Entering a code there broadcasts on the 'tier-access' channel; this tab then re-reads
// /api/me (it also does when the tab regains focus) and unlocks in place: nothing reloads and no input changes.
// Pages listen for 'paywall:change' to redraw anything that depends on access.
window.Paywall = (function () {
    let me = { gated: false, tier: 'Free', features: [], offers: [] };
    let checking = null;

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    const can = (feature) => !me.gated || me.features.includes(feature);

    // The tiers on offer that include a feature, cheapest first (the server lists them in that order)
    const offersFor = (feature) => me.offers.filter((offer) => offer.features.includes(feature));

    function badgeHtml(feature) {
        const [first, ...others] = offersFor(feature);
        if (!first) return '';
        const tier = escapeHtml(first.tier);
        const alsoIn = others.map((offer) => `<br>${t('paywall.orTier', { tier: escapeHtml(offer.tier), price: escapeHtml(offer.priceLabel) })}`).join('');
        const link = first.action === 'code' ? t('paywall.enterCode') : t('paywall.subscribe');
        // A span acting as a button, not a <button>: inside a <label>, a button would become the thing the label names
        return `<span class="paid-badge-button" role="button" tabindex="0" aria-expanded="false" aria-label="${escapeHtml(t('paywall.badgeLabel', { tier: first.tier }))}">`
            + `<span aria-hidden="true">&#10022;</span> ${tier}</span>`
            + `<span class="paid-tip" role="dialog">`
            + `<strong>${t('paywall.tierFeature', { tier })}</strong> &middot; ${escapeHtml(first.priceLabel)}${alsoIn}`
            + `<span class="paid-tip-adds">${t('paywall.adds')}</span>`
            + `<a href="/billing/subscribe?tier=${encodeURIComponent(first.tier.toLowerCase())}" target="_blank" rel="noopener">${link}</a>`
            + `</span>`;
    }

    // Fills and shows the badges under root whose feature is locked; hides the rest
    function decorate(root = document) {
        root.querySelectorAll('.paid-badge[data-feature]').forEach((badge) => {
            const locked = !can(badge.dataset.feature);
            const html = locked ? badgeHtml(badge.dataset.feature) : '';
            badge.hidden = html === '';
            if (badge.dataset.rendered !== html) {
                badge.innerHTML = html;
                badge.dataset.rendered = html;
            }
            badge.classList.remove('open');
        });
    }

    function lockInput(input, locked) {
        if (locked && !input.classList.contains('locked')) {
            if (input.type === 'checkbox') {
                input.checked = input.dataset.freeValue === 'true';
            } else {
                input.value = input.dataset.freeValue ?? input.defaultValue;
            }
            // The page's totals, allocation line and the like follow the new value
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
        input.classList.toggle('locked', locked);
        if (input.type === 'checkbox') input.disabled = locked;
        else input.readOnly = locked;
    }

    function apply() {
        document.querySelectorAll('[data-requires]').forEach((el) => {
            const locked = !can(el.dataset.requires);
            if (el.dataset.locked === 'hide') el.hidden = locked;
            else lockInput(el, locked);
        });
        document.querySelectorAll('[data-free-only]').forEach((el) => { el.hidden = can(el.dataset.freeOnly); });
        decorate();
        renderTierMarker();
    }

    // "Plus" / "Pro" in the header while a paid tier is on, linking to the access-code page (to switch, or go back to Free)
    function renderTierMarker() {
        const header = document.querySelector('.page-header');
        if (!header) return;
        let marker = header.querySelector('.tier-marker');
        if (!me.gated || me.tier === 'Free') {
            marker?.remove();
            return;
        }
        if (!marker) {
            marker = document.createElement('a');
            marker.className = 'tier-marker';
            marker.href = '/access.html';
            marker.target = '_blank';
            marker.rel = 'noopener';
            header.insertBefore(marker, header.querySelector('.lang-toggle'));
        }
        marker.textContent = me.tier;
        marker.title = t('paywall.markerTitle', { tier: me.tier });
    }

    async function load() {
        try {
            const response = await fetch('/api/me', { cache: 'no-store' });
            if (response.ok) return await response.json();
        } catch {
            // Offline or failed: keep what we had; the server still decides
        }
        return null;
    }

    // Re-reads /api/me and, if anything changed, re-applies and tells the page
    function refresh() {
        if (checking) return checking;
        checking = load().then((next) => {
            checking = null;
            if (!next || JSON.stringify(next) === JSON.stringify(me)) return;
            me = next;
            apply();
            document.dispatchEvent(new CustomEvent('paywall:change', { detail: me }));
        });
        return checking;
    }

    // The first read: the pages wait for it before sending a request, so a Free visitor never sends a locked value
    const ready = Promise.all([load(), I18n.ready]).then(([first]) => {
        if (first) me = first;
        apply();
    });

    // A code entered in another tab, or coming back to this one
    try {
        new BroadcastChannel('tier-access').addEventListener('message', refresh);
    } catch {
        // No BroadcastChannel: the focus check below still catches it
    }
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') refresh(); });
    window.addEventListener('focus', refresh);
    document.addEventListener('i18n:change', () => {
        decorate();
        renderTierMarker();
    });

    function closeAll(except) {
        document.querySelectorAll('.paid-badge.open').forEach((badge) => {
            if (badge === except) return;
            badge.classList.remove('open');
            badge.querySelector('.paid-badge-button')?.setAttribute('aria-expanded', 'false');
        });
    }

    function toggle(button) {
        const badge = button.parentElement;
        closeAll(badge);
        button.setAttribute('aria-expanded', String(badge.classList.toggle('open')));
    }

    // Tapping a badge toggles its flyout (hover and keyboard focus show it too, in CSS); a tap elsewhere or Escape closes
    // it. Inside a <label>, preventing the click's default stops it also ticking the checkbox or focusing the input.
    document.addEventListener('click', (e) => {
        // Inside an open flyout only its link acts
        if (e.target.closest('.paid-tip')) {
            if (!e.target.closest('a')) e.preventDefault();
            return;
        }
        const button = e.target.closest('.paid-badge-button');
        if (!button) {
            closeAll(null);
            return;
        }
        e.preventDefault();
        toggle(button);
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeAll(null);
        } else if ((e.key === 'Enter' || e.key === ' ') && e.target.classList?.contains('paid-badge-button')) {
            e.preventDefault();
            toggle(e.target);
        }
    });

    return {
        ready,
        can,
        decorate,
        refresh,
        get me() { return me; },
    };
})();
