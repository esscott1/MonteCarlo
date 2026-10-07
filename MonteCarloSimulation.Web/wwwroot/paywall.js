// Paid tiers on the Scenario runner and the Optimizer: what this visitor may use (GET /api/me), the paid-feature
// badges with their price flyouts, and the inputs a Free visitor can't change. Loaded after i18n.js, before the
// page's own script. The server enforces every gate; this only keeps the pages in step with it.
//
// The site's mode (switched on the Observe page) decides what's offered. Free Only: locked inputs are greyed with no
// badges and no way to upgrade. Plus Available / Pro Available: each badge and lock names the cheapest tier on offer
// that includes the feature, styled for that tier (a blue Plus, a deep purple Pro). A visitor on Plus or Pro sees the
// same spots as "✓ Plus" / "✓ Pro" (a flyout saying "Enabled"), the inputs outlined in their tier's colour, and a Plus
// visitor sees an "Upgrade to Pro" offer in the header while Pro is on offer.
//
// Markup it reads:
//   <span class="paid-badge" data-feature="custom-returns" hidden></span>   a badge: the offer while the feature is
//       locked, "✓ {tier}" while the visitor's tier has it
//   data-requires="custom-returns" on an input                            locked at its Free value while locked:
//       data-free-value="0" (or "false" for a checkbox), else the value the page loaded with; outlined in the
//       visitor's tier colour (data-enabled-tier) while their tier has it
//   data-requires="..." data-locked="hide" on any element                 hidden while locked (and its inputs
//       outlined while enabled)
//   data-free-only="..." on any element                                   shown only while locked
//
// A badge's link opens the access-code page (/billing/subscribe, which becomes Stripe Checkout later) in a new tab, so
// the visitor keeps their place. Entering a code there broadcasts on the 'tier-access' channel; this tab then re-reads
// /api/me (it also does when the tab regains focus) and unlocks in place: nothing reloads and no input changes.
// Pages listen for 'paywall:change' to redraw anything that depends on access.
window.Paywall = (function () {
    let me = { mode: 'FreeOnly', tier: 'Free', features: [], offers: [] };
    let checking = null;

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    const can = (feature) => me.features.includes(feature);

    // The tiers on offer that include a feature, cheapest first (the server lists them in that order)
    const offersFor = (feature) => me.offers.filter((offer) => offer.features.includes(feature));

    // The tier a locked feature is offered in, as a class name ("plus", "pro"), or '' when nothing offers it
    const lockTier = (feature) => offersFor(feature)[0]?.tier.toLowerCase() ?? '';

    const SYMBOLS = { plus: '&#10022;', pro: '&#9670;' };

    // The visitor's tier as a class name ("plus", "pro"), or '' on Free
    const enabledTier = () => (me.tier === 'Free' ? '' : me.tier.toLowerCase());

    // A paid feature the visitor's tier includes: "✓ Plus" / "✓ Pro", whose flyout just says so
    function enabledHtml() {
        return `<span class="paid-badge-button enabled ${enabledTier()}" role="button" tabindex="0" aria-expanded="false" aria-label="${escapeHtml(t('paywall.enabledLabel', { tier: me.tier }))}">`
            + `<span aria-hidden="true">&#10003;</span> ${escapeHtml(me.tier)}</span>`
            + `<span class="paid-tip paid-tip-short" role="tooltip">${t('paywall.enabled')}</span>`;
    }

    // What a tier will add that isn't built yet ("Return models · Per-account asset mixes · ..."), in the page's language
    const slug = (english) => english.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
    const highlights = (offer) => (offer.highlights ?? [])
        .map((english) => escapeHtml(I18n.translateData(`paywall.highlight.${slug(english)}`, english))).join(' &middot; ');

    function badgeHtml(feature) {
        const [first, ...others] = offersFor(feature);
        if (!first) return '';
        const tier = escapeHtml(first.tier);
        const tierClass = first.tier.toLowerCase();
        const alsoIn = others.map((offer) => `<br>${t('paywall.orTier', { tier: escapeHtml(offer.tier), price: escapeHtml(offer.priceLabel) })}`).join('');
        const link = first.action === 'code' ? t('paywall.enterCode') : t('paywall.subscribe');
        // A span acting as a button, not a <button>: inside a <label>, a button would become the thing the label names
        return `<span class="paid-badge-button ${tierClass}" role="button" tabindex="0" aria-expanded="false" aria-label="${escapeHtml(t('paywall.badgeLabel', { tier: first.tier }))}">`
            + `<span aria-hidden="true">${SYMBOLS[tierClass] ?? SYMBOLS.plus}</span> ${tier}</span>`
            + `<span class="paid-tip" role="dialog">`
            + `<strong>${t('paywall.tierFeature', { tier })}</strong> &middot; ${escapeHtml(first.priceLabel)}${alsoIn}`
            + `<span class="paid-tip-adds">${t('paywall.adds')}</span>`
            + `<a href="/billing/subscribe?tier=${encodeURIComponent(first.tier.toLowerCase())}" target="_blank" rel="noopener">${link}</a>`
            + `</span>`;
    }

    // Fills the badges under root: the offer for a locked feature, "✓ {tier}" for one the visitor's tier has; a badge
    // with neither (Free Only, or nothing on offer with that feature) is hidden
    function decorate(root = document) {
        root.querySelectorAll('.paid-badge[data-feature]').forEach((badge) => {
            const feature = badge.dataset.feature;
            const html = can(feature) ? (enabledTier() ? enabledHtml() : '') : badgeHtml(feature);
            badge.hidden = html === '';
            if (badge.dataset.rendered !== html) {
                badge.innerHTML = html;
                badge.dataset.rendered = html;
            }
            badge.classList.remove('open');
        });
    }

    function lockInput(input, locked, tier) {
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
        // Styled for the tier it's offered in (a Pro lock carries a purple edge); none in Free Only
        if (locked && tier) input.dataset.lockedTier = tier;
        else delete input.dataset.lockedTier;
        if (input.type === 'checkbox') input.disabled = locked;
        else input.readOnly = locked;
    }

    function apply() {
        document.querySelectorAll('[data-requires]').forEach((el) => {
            const locked = !can(el.dataset.requires);
            if (el.dataset.locked === 'hide') el.hidden = locked;
            else lockInput(el, locked, lockTier(el.dataset.requires));
            // Enabled by the visitor's tier: outlined in its colour (the CSS outlines inputs, and inputs inside a group)
            if (!locked && enabledTier()) el.dataset.enabledTier = enabledTier();
            else delete el.dataset.enabledTier;
        });
        document.querySelectorAll('[data-free-only]').forEach((el) => { el.hidden = can(el.dataset.freeOnly); });
        decorate();
        renderTierMarker();
    }

    // A Plus visitor, while Pro is on offer: "◆ Upgrade to Pro" beside the tier marker, whose flyout gives Pro's price,
    // what it will add, and the link to enter its code
    function renderUpgrade(header, marker) {
        const pro = me.tier === 'Plus' ? me.offers.find((offer) => offer.tier === 'Pro') : null;
        let upgrade = header.querySelector('.upgrade-badge');
        if (!pro) {
            upgrade?.remove();
            return;
        }
        if (!upgrade) {
            upgrade = document.createElement('span');
            upgrade.className = 'paid-badge upgrade-badge';
            marker.after(upgrade);
        }
        const link = pro.action === 'code' ? t('paywall.enterCode') : t('paywall.subscribe');
        const adds = highlights(pro);
        upgrade.innerHTML = `<span class="paid-badge-button pro" role="button" tabindex="0" aria-expanded="false">`
            + `<span aria-hidden="true">${SYMBOLS.pro}</span> ${escapeHtml(t('paywall.upgradeTo', { tier: pro.tier }))}</span>`
            + `<span class="paid-tip" role="dialog">`
            + `<strong>${escapeHtml(pro.tier)}</strong> &middot; ${escapeHtml(pro.priceLabel)}`
            + (adds ? `<span class="paid-tip-adds">${t('paywall.comingSoon', { tier: escapeHtml(pro.tier), list: adds })}</span>` : '')
            + `<a href="/billing/subscribe?tier=${encodeURIComponent(pro.tier.toLowerCase())}" target="_blank" rel="noopener">${link}</a>`
            + `</span>`;
    }

    // "Plus" / "Pro" in the header while a paid tier is on, linking to the access-code page (to switch, or go back to Free)
    function renderTierMarker() {
        const header = document.querySelector('.page-header');
        if (!header) return;
        let marker = header.querySelector('.tier-marker');
        if (me.tier === 'Free') {
            marker?.remove();
            header.querySelector('.upgrade-badge')?.remove();
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
        marker.classList.toggle('pro', me.tier === 'Pro');
        marker.title = t('paywall.markerTitle', { tier: me.tier });
        renderUpgrade(header, marker);
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
