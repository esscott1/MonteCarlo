// The inputs tile both the Scenario runner (app.js) and the Optimal page (optimal.js) use: money fields, the tabs,
// the Asset Classes table (allocation total and blended line) and the balance totals. Each takes the page's form, so
// the pages share the same markup ids. Text comes from i18n.js (t).
window.Inputs = (() => {
    const formatCurrency = (value) =>
        Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const formatPercent = (value) => (Number(value) * 100).toFixed(2) + '%';

    // --- Money fields: "1,000,000" in the box, a number to the API ---

    function parseNumber(value) {
        const stripped = String(value).replace(/,/g, '');
        return stripped === '' ? NaN : Number(stripped);
    }

    function formatWithCommas(value) {
        const num = parseNumber(value);
        return Number.isNaN(num) ? String(value) : num.toLocaleString('en-US', { maximumFractionDigits: 2 });
    }

    function countDigits(str) {
        return (str.match(/[0-9]/g) || []).length;
    }

    // Reformats a money input with thousands separators as the user types, keeping
    // the cursor sitting after the same digit it followed before reformatting.
    // Tracks which side of the decimal point the cursor is on, since a plain
    // digit-count-before-cursor can't tell "just before the dot" apart from
    // "just after it" and would otherwise misplace the next typed character.
    function formatMoneyInputLive(input) {
        const value = input.value;
        const cursorPos = input.selectionStart ?? value.length;

        const dotIndexOriginal = value.indexOf('.');
        const cursorInDecimal = dotIndexOriginal !== -1 && cursorPos > dotIndexOriginal;
        const digitsBeforeCursor = cursorInDecimal
            ? countDigits(value.slice(dotIndexOriginal + 1, cursorPos))
            : countDigits(value.slice(0, cursorPos));

        let raw = value.replace(/[^0-9.]/g, '');
        const firstDot = raw.indexOf('.');
        if (firstDot !== -1) {
            raw = raw.slice(0, firstDot + 1) + raw.slice(firstDot + 1).replace(/\./g, '');
        }

        const hasDot = raw.includes('.');
        const [intPart, decPart] = raw.split('.');
        const formattedInt = intPart ? Number(intPart).toLocaleString('en-US') : '';
        const formatted = hasDot ? `${formattedInt}.${decPart ?? ''}` : formattedInt;

        input.value = formatted;

        let newPos;
        if (cursorInDecimal) {
            const dotIndexFormatted = formatted.indexOf('.');
            newPos = dotIndexFormatted + 1 + digitsBeforeCursor;
        } else {
            let seen = 0;
            newPos = formatted.length;
            for (let i = 0; i < formatted.length; i++) {
                if (formatted[i] === '.') break;
                if (/[0-9]/.test(formatted[i])) {
                    seen++;
                    if (seen === digitsBeforeCursor) {
                        newPos = i + 1;
                        break;
                    }
                }
            }
            if (digitsBeforeCursor === 0) newPos = 0;
        }
        input.setSelectionRange(newPos, newPos);
    }

    function initMoneyInputs() {
        document.querySelectorAll('.money-input').forEach((input) => {
            input.addEventListener('keyup', () => formatMoneyInputLive(input));
            input.addEventListener('blur', () => {
                input.value = formatWithCommas(input.value);
            });
        });
    }

    // --- The tabs: click or arrow keys to switch, the choice remembered (per page) for the next visit ---

    function selectTab(form, storageKey, tab, focus) {
        form.querySelectorAll('[role="tab"]').forEach((other) => {
            const selected = other === tab;
            other.setAttribute('aria-selected', String(selected));
            other.tabIndex = selected ? 0 : -1;
            document.getElementById(other.getAttribute('aria-controls')).hidden = !selected;
        });
        if (focus) tab.focus();
        try { localStorage.setItem(storageKey, tab.id); } catch { }
    }

    function initTabs(form, storageKey) {
        const tabs = [...form.querySelectorAll('[role="tab"]')];
        tabs.forEach((tab, i) => {
            tab.addEventListener('click', () => selectTab(form, storageKey, tab, false));
            tab.addEventListener('keydown', (e) => {
                const next = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: tabs.length - 1 }[e.key];
                if (next === undefined) return;
                e.preventDefault();
                selectTab(form, storageKey, tabs[(next + tabs.length) % tabs.length], true);
            });
        });

        let saved = null;
        try { saved = localStorage.getItem(storageKey); } catch { }
        const remembered = tabs.find((tab) => tab.id === saved);
        if (remembered) selectTab(form, storageKey, remembered, false);

        // A field that fails validation on a hidden tab can't show its message, so open the tab of the first one
        let opened = false;
        form.addEventListener('invalid', (e) => {
            if (opened) return;
            opened = true;
            setTimeout(() => { opened = false; });
            const panel = e.target.closest('[role="tabpanel"]');
            if (panel?.hidden) selectTab(form, storageKey, document.getElementById(panel.getAttribute('aria-labelledby')), false);
        }, true);
    }

    // A required input hidden inside the collapsed inputs section can't show its validation message, so open the
    // section whenever any field fails validation on submit.
    function initCollapsibleInputs(form) {
        const section = document.getElementById('inputs-section');
        form.addEventListener('invalid', () => { section.open = true; }, true);
    }

    // --- The Asset Classes table ---

    const ALLOCATIONS = ['stockAllocation', 'bondAllocation', 'cashAllocation'];
    const MIX_FIELDS = ['stockAllocation', 'bondAllocation', 'cashAllocation', 'stockReturn', 'stockStdDev',
        'bondReturn', 'bondStdDev', 'cashReturn', 'cashStdDev'];

    const percentField = (form, name) => Number(form.elements[name].value);

    function formatAllocation(percent) {
        return `${Number(percent.toFixed(2))}%`;
    }

    // The allocation must total 100%: say so on the total line, flag the Asset Classes tab, and block submitting
    // until it does. While it does, show what the mix blends to.
    function updateAllocation(form) {
        const values = ALLOCATIONS.map((name) => percentField(form, name));
        const total = values.reduce((a, b) => a + b, 0);
        const valid = values.every(Number.isFinite) && Math.abs(total - 100) < 0.005;
        const message = valid ? '' : t('runner.allocation.mustTotal', { total: formatAllocation(total) });
        ALLOCATIONS.forEach((name) => form.elements[name].setCustomValidity(message));

        const totalLine = document.getElementById('allocation-total');
        totalLine.textContent = valid ? t('runner.allocation.total', { total: formatAllocation(total) }) : message;
        totalLine.classList.toggle('invalid', !valid);
        document.getElementById('tab-assets').classList.toggle('has-error', !valid);

        const mix = readAssetMix(form);
        const s = mix.stockAllocation * mix.stockStdDev;
        const b = mix.bondAllocation * mix.bondStdDev;
        const c = mix.cashAllocation * mix.cashStdDev;
        const mean = mix.stockAllocation * mix.stockReturn + mix.bondAllocation * mix.bondReturn + mix.cashAllocation * mix.cashReturn;
        const stdDev = Math.sqrt(s * s + b * b + c * c + 2 * mix.stockBondCorrelation * s * b);
        document.getElementById('blended-line').textContent = valid && Number.isFinite(mean) && Number.isFinite(stdDev)
            ? t('runner.allocation.blended', { mean: formatPercent(mean), stdDev: formatPercent(stdDev) })
            : '';
    }

    function initAllocation(form) {
        form.querySelectorAll('#panel-assets input').forEach((input) => input.addEventListener('input', () => updateAllocation(form)));
        // Once the page's text has loaded, so the lines never show untranslated keys
        I18n.ready.then(() => updateAllocation(form));
    }

    // The mix as the API takes it: fractions (entered as percentages); the correlation is already -1 to 1.
    function readAssetMix(form) {
        const mix = { stockBondCorrelation: percentField(form, 'stockBondCorrelation') };
        MIX_FIELDS.forEach((name) => { mix[name] = percentField(form, name) / 100; });
        return mix;
    }

    // Fills the table from a mix in API form (fractions).
    function setAssetMix(form, mix) {
        MIX_FIELDS.forEach((name) => { form.elements[name].value = String(Number((mix[name] * 100).toFixed(4))); });
        form.elements['stockBondCorrelation'].value = String(mix.stockBondCorrelation);
        updateAllocation(form);
    }

    // --- Balance totals under Your Money ---

    function updateBalanceTotals(form) {
        const value = (name) => parseNumber(form.elements[name].value) || 0;
        const taxDeferred = value('initialTaxableBalance');
        const rothBasis = value('initialRothBasis');
        const rothGain = value('initialRothUnrealizedGain');
        const basis = value('initialBrokerageBasis');
        const gain = value('initialBrokerageUnrealizedGain');

        document.getElementById('roth-total').textContent = t('runner.totalRoth', { amount: formatCurrency(rothBasis + rothGain) });
        document.getElementById('brokerage-total').textContent = t('runner.totalBrokerage', { amount: formatCurrency(basis + gain) });
        document.getElementById('grand-total').textContent =
            t('runner.totalMoney', { amount: formatCurrency(taxDeferred + rothBasis + rothGain + basis + gain) });

        // A Free visitor's accounts: how each total is split for taxes
        const shares = window.Paywall?.me.freeDefaults;
        if (shares) {
            const percent = (fraction) => `${Math.round(fraction * 100)}%`;
            document.getElementById('roth-split-hint').textContent = t('runner.split.roth', { basis: percent(shares.rothBasisShare) });
            document.getElementById('brokerage-split-hint').textContent = t('runner.split.brokerage', {
                gains: percent(shares.brokerageGainShare), basis: percent(1 - shares.brokerageGainShare),
            });
        }
    }

    function initBalanceTotals(form) {
        ['initialTaxableBalance', 'initialRothBasis', 'initialRothUnrealizedGain', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain']
            .forEach((name) => form.elements[name].addEventListener('input', () => updateBalanceTotals(form)));
        // Once the page's text has loaded, so the totals never show untranslated keys
        I18n.ready.then(() => updateBalanceTotals(form));
    }

    // --- A Free visitor's accounts: one total each, split by the shares GET /api/me reports (paywall.js), which are
    // the shares the server splits by too. Plus sees and sends the basis and gain fields themselves. ---

    const ACCOUNTS = [
        { total: 'rothTotal', basis: 'initialRothBasis', gain: 'initialRothUnrealizedGain', gainShare: (shares) => 1 - shares.rothBasisShare },
        { total: 'brokerageTotal', basis: 'initialBrokerageBasis', gain: 'initialBrokerageUnrealizedGain', gainShare: (shares) => shares.brokerageGainShare },
    ];

    const splitsTotals = () => !Paywall.can('account-basis-split') && Paywall.me.freeDefaults;
    const cents = (value) => Math.round(value * 100) / 100;

    // Each total into the (hidden) basis and gain fields the request is built from
    function splitTotals(form) {
        const shares = Paywall.me.freeDefaults;
        ACCOUNTS.forEach((account) => {
            const total = parseNumber(form.elements[account.total].value) || 0;
            const gain = cents(total * account.gainShare(shares));
            form.elements[account.gain].value = formatWithCommas(gain);
            form.elements[account.basis].value = formatWithCommas(cents(total - gain));
        });
        updateBalanceTotals(form);
    }

    // Free: show each account's total, whatever split it had (the defaults, a hand-off, a Plus visitor's own), and split
    // it by the shares. Plus: nothing to do, the basis and gain fields already hold the split.
    function refreshAccountTotals(form) {
        if (!splitsTotals()) return;
        const value = (name) => parseNumber(form.elements[name].value) || 0;
        ACCOUNTS.forEach((account) => {
            form.elements[account.total].value = formatWithCommas(cents(value(account.basis) + value(account.gain)));
        });
        splitTotals(form);
    }

    function initAccountTotals(form) {
        ACCOUNTS.forEach((account) => form.elements[account.total].addEventListener('input', () => {
            if (splitsTotals()) splitTotals(form);
        }));
        Paywall.ready.then(() => refreshAccountTotals(form));
        document.addEventListener('paywall:change', () => refreshAccountTotals(form));
    }

    return {
        parseNumber,
        formatWithCommas,
        initMoneyInputs,
        initTabs,
        initCollapsibleInputs,
        formatAllocation,
        updateAllocation,
        initAllocation,
        readAssetMix,
        setAssetMix,
        updateBalanceTotals,
        initBalanceTotals,
        initAccountTotals,
        refreshAccountTotals,
    };
})();
