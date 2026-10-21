// A Scenario runner link (Share scenario): the inputs of a run, as /api/run takes them, packed into the page's hash -
// index.html#s=1.<base64url> - so the link holds every number and nothing is stored. The values only, no field names,
// joined by ~ in version 1's fixed order (FIELDS); the annuity's own values follow only when it's included. A version's
// order never changes: a new input means a new version beside this one, which old links still decode with.
// Also which paid features a link's inputs use (mirroring FreeTier.LockedInputs/LockedAnnuity and SplitAccounts), so
// app.js can send a visitor without them to the access page first. No page text here; app.js says what happened.
window.ScenarioLink = (function () {
    const VERSION = '1';

    // [request field, how it's written]: num as is, monthly the annual amount ÷ 12 (as the page shows it), pct a fraction
    // as a percent, date yyyymmdd, bool 0/1, and the enums by their first letter
    const FIELDS = [
        ['years', 'num'], ['iterations', 'num'], ['withdrawal', 'monthly'], ['birthdate', 'date'], ['retirementDate', 'date'],
        ['filingStatus', { single: 's', married: 'm' }],
        ['initialTaxableBalance', 'num'], ['initialRothBasis', 'num'], ['initialRothUnrealizedGain', 'num'],
        ['initialBrokerageBasis', 'num'], ['initialBrokerageUnrealizedGain', 'num'], ['enableRothConversions', 'bool'],
        ['socialSecurityMonthlyAmount', 'num'], ['socialSecurityStartDate', 'date'],
        ['newMoney', 'num'], ['yearNewMoney', 'num'], ['annualStandardDeduction', 'num'],
        ['stockAllocation', 'pct'], ['bondAllocation', 'pct'], ['cashAllocation', 'pct'],
        ['stockReturn', 'pct'], ['stockStdDev', 'pct'], ['bondReturn', 'pct'], ['bondStdDev', 'pct'],
        ['cashReturn', 'pct'], ['cashStdDev', 'pct'], ['stockBondCorrelation', 'num'],
        ['includeAnnuity', 'bool'],
    ];
    const ANNUITY_FIELDS = [
        ['annuityMonthlyAmount', 'num'], ['annuityInvestment', 'num'], ['annuityType', { spia: 's', dia: 'd' }],
        ['annuityStartDate', 'date?'], ['annuityTaxDeferred', 'bool'], ['annuityCola', 'bool'], ['annuityColaRate', 'pct'],
    ];
    // What a request without an annuity sends for its fields (app.js's annuityRequest reads them from the form anyway)
    const NO_ANNUITY = { annuityMonthlyAmount: 0, annuityInvestment: 0, annuityType: 'spia', annuityStartDate: null, annuityTaxDeferred: false, annuityCola: false, annuityColaRate: 0 };

    const NUMBER = /^-?\d+(\.\d+)?(e[+-]?\d+)?$/i;
    const DATE = /^(\d{4})(\d{2})(\d{2})$/;
    // Rounded so float noise (0.035 × 100 = 3.5000000000000004) doesn't lengthen the link
    const tidy = (value) => String(Number(value.toFixed(6)));

    function write(value, how) {
        if (typeof how === 'object') return how[value];
        switch (how) {
            case 'num': return tidy(value);
            case 'monthly': return tidy(value / 12);
            case 'pct': return tidy(value * 100);
            case 'bool': return value ? '1' : '0';
            case 'date': return value.replaceAll('-', '');
            case 'date?': return value ? value.replaceAll('-', '') : '';
        }
    }

    // The value, or undefined when the text isn't one
    function read(text, how) {
        if (typeof how === 'object') return Object.keys(how).find((key) => how[key] === text);
        if (how === 'bool') return text === '1' ? true : text === '0' ? false : undefined;
        if (how === 'date?' && text === '') return null;
        if (how === 'date' || how === 'date?') {
            const m = DATE.exec(text);
            if (!m) return undefined;
            const date = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
            return date.getUTCMonth() === +m[2] - 1 && date.getUTCDate() === +m[3] ? `${m[1]}-${m[2]}-${m[3]}` : undefined;
        }
        if (!NUMBER.test(text)) return undefined;
        const n = Number(text);
        if (!Number.isFinite(n)) return undefined;
        return how === 'monthly' ? n * 12 : how === 'pct' ? n / 100 : n;
    }

    const toBase64Url = (text) => btoa(text).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
    const fromBase64Url = (text) => atob(text.replaceAll('-', '+').replaceAll('_', '/'));

    // The hash (without #) for a request as the page sends it to /api/run
    function encode(request) {
        const fields = request.includeAnnuity ? [...FIELDS, ...ANNUITY_FIELDS] : FIELDS;
        const values = fields.map(([name, how]) => write(how === 'date?' && request.annuityType !== 'dia' ? null : request[name], how));
        return `s=${VERSION}.${toBase64Url(values.join('~'))}`;
    }

    // The request a hash holds (with or without its #), or null when it isn't a link of a version this page reads. A link
    // is taken whole or not at all.
    function decode(hash) {
        const m = /^#?s=(\d+)\.([A-Za-z0-9_-]+)$/.exec(hash || '');
        if (!m || m[1] !== VERSION) return null;
        let values;
        try {
            values = fromBase64Url(m[2]).split('~');
        } catch {
            return null;
        }
        const withAnnuity = values.length === FIELDS.length + ANNUITY_FIELDS.length;
        if (values.length !== FIELDS.length && !withAnnuity) return null;
        const fields = withAnnuity ? [...FIELDS, ...ANNUITY_FIELDS] : FIELDS;
        const request = {};
        for (let i = 0; i < fields.length; i++) {
            const value = read(values[i], fields[i][1]);
            if (value === undefined) return null;
            request[fields[i][0]] = value;
        }
        if (request.includeAnnuity !== withAnnuity) return null;
        if (!withAnnuity) Object.assign(request, NO_ANNUITY);
        else if (request.annuityType === 'dia' && !request.annuityStartDate) return null;
        return request;
    }

    // The paid features a request's inputs use: those the server refuses for Free (FreeTier.LockedInputs/LockedAnnuity),
    // and a Roth or Brokerage split other than the Free shares (FreeTier.SplitAccounts would re-split it, so the run
    // wouldn't be the one shared). defaults: the page's own - { standardDeduction: { single, married }, assetMix (the
    // returns, std. devs and correlation as the API takes them), shares (/api/me's freeDefaults, or null) }.
    function featuresNeeded(request, defaults) {
        const needed = [];
        if (request.enableRothConversions) needed.push('roth-conversions');
        if (request.newMoney !== 0) needed.push('inheritance');
        const deduction = defaults.standardDeduction[request.filingStatus === 'married' ? 'married' : 'single'];
        if (Math.abs(request.annualStandardDeduction - deduction) > 0.005) needed.push('standard-deduction');
        if (Object.keys(defaults.assetMix).some((name) => Math.abs(request[name] - defaults.assetMix[name]) > 1e-9)) needed.push('custom-returns');
        if (request.includeAnnuity) needed.push('annuity');
        if (request.includeAnnuity && request.annuityCola) needed.push('annuity-cola');
        const shares = defaults.shares;
        if (shares) {
            const roth = request.initialRothBasis + request.initialRothUnrealizedGain;
            const brokerage = request.initialBrokerageBasis + request.initialBrokerageUnrealizedGain;
            if (Math.abs(request.initialRothBasis - roth * shares.rothBasisShare) > 1
                || Math.abs(request.initialBrokerageUnrealizedGain - brokerage * shares.brokerageGainShare) > 1) needed.push('account-basis-split');
        }
        return needed;
    }

    return { encode, decode, featuresNeeded };
})();
