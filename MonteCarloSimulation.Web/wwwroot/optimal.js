// The Optimal page: the most you can spend each month in your asset mix, and the best age to start Social Security.
// Its inputs tile is the Scenario runner's (inputs.js); the recommendation fills the tile beside it and the claiming-age
// detail sits below the form. Text comes from i18n.js (t), so the page reads in English or Spanish.
const optimalForm = document.getElementById('optimal-form');
const optimalCard = document.getElementById('optimal-card');
const cardPlaceholder = document.getElementById('card-placeholder');
const optimalResults = document.getElementById('optimal-results');
const optimalSubmit = document.getElementById('optimal-submit');

// The request behind the results on screen, so "Run in Scenario runner" hands over what's shown even if the form has
// changed since.
let lastRequest = null;

// Everything the page shows, so switching language can redraw it - mid-stream included - without re-running.
// view: null before a run; { kind: 'calculating' } until the stream starts; { kind: 'errors', errors } or
// { kind: 'failed' } for a run that never started; { kind: 'stream', start, progress, optimum, done, stopped } after.
let view = null;

// sessionStorage key for handing the result to the Scenario runner (app.js reads it once and runs it).
const SIMULATOR_HANDOFF_KEY = 'simulatorHandoff';

const { parseNumber, formatWithCommas, formatAsOf } = Inputs;

function formatCurrency(value) {
    return Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

// The optimizer works in annual spend; the page shows it per month.
function perMonth(annual) {
    return formatCurrency(annual / 12);
}

function formatPercent(value, digits = 1) {
    return (Number(value) * 100).toFixed(digits) + '%';
}

function formatMonthYear(isoDate) {
    const [y, m] = isoDate.split('-').map(Number);
    return I18n.monthYear(new Date(Date.UTC(y, m - 1, 1)));
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// The annuity the last result bought, shown in the inputs: each balance it was paid from lowered by what it took, the
// Annuity tab filled in. { decision, full } (full: the request's balances before the purchase), or null before a result
// that weighed a purchase.
let bought = null;

// What a decision's purchase took from one balance field (Inputs.purchaseTaken): by default the last result's
const taken = (field, decision = bought?.decision) => Inputs.purchaseTaken(decision, field);

const cents = (value) => Math.round(value * 100) / 100;

function readRequest() {
    const data = new FormData(optimalForm);
    return {
        years: Number(data.get('years')),
        birthdate: data.get('birthdate'),
        retirementDate: data.get('retirementDate'),
        // Each run decides afresh: the balance fields hold what's left after the last purchase, so what it took is added back
        initialTaxableBalance: cents(parseNumber(data.get('initialTaxableBalance')) + taken('initialTaxableBalance')),
        initialRothBasis: cents(parseNumber(data.get('initialRothBasis')) + taken('initialRothBasis')),
        initialRothUnrealizedGain: cents(parseNumber(data.get('initialRothUnrealizedGain')) + taken('initialRothUnrealizedGain')),
        initialBrokerageBasis: cents(parseNumber(data.get('initialBrokerageBasis')) + taken('initialBrokerageBasis')),
        initialBrokerageUnrealizedGain: cents(parseNumber(data.get('initialBrokerageUnrealizedGain')) + taken('initialBrokerageUnrealizedGain')),
        newMoney: parseNumber(data.get('newMoney')),
        yearNewMoney: Number(data.get('yearNewMoney')),
        socialSecurityAt62: parseNumber(data.get('socialSecurityAt62')),
        socialSecurityAt67: parseNumber(data.get('socialSecurityAt67')),
        socialSecurityAt70: parseNumber(data.get('socialSecurityAt70')),
        annualStandardDeduction: parseNumber(data.get('annualStandardDeduction')),
        filingStatus: data.get('filingStatus'),
        enableRothConversions: data.get('enableRothConversions') === 'on',
        paths: Number(data.get('paths')),
        // The rate a suggested annuity is bought at: a man's unless a Pro visitor chose Female (a disabled radio
        // isn't in FormData, so it's read from the element)
        annuitySex: Paywall.can('annuity-gender') ? optimalForm.elements['annuitySex'].value : 'male',
        // Unticked (Plus), the Optimizer skips the purchase search; a Free visitor's stays ticked
        considerAnnuities: optimalForm.elements['considerAnnuities'].checked,
        ...Inputs.readAssetMix(optimalForm),
    };
}

// A result's annuity decision, put into the inputs. With a purchase: each balance it was paid from shows what's left,
// with a note, and the Annuity tab shows what was bought. Without one (none helped, or none was weighed): the full
// balances and an empty tab. `request`: the request the result came from, with the balances before any purchase.
function applyPurchase(decision, request) {
    const el = optimalForm.elements;
    bought = decision ? { decision, full: request } : null;
    const amount = decision?.amount > 0 ? decision.amount : 0;
    Object.keys(Inputs.PURCHASE_FIELDS).forEach((field) => {
        el[field].value = formatWithCommas(String(cents(request[field] - taken(field))));
        el[field].dispatchEvent(new Event('input', { bubbles: true }));
    });
    // A Free visitor's account totals follow the fields (inputs.js)
    Inputs.refreshAccountTotals(optimalForm);
    el['annuityInvestment'].value = formatWithCommas(String(amount));
    el['annuityMonthlyAmount'].value = formatWithCommas(String(amount > 0 ? decision.monthlyPayment : 0));
    el['boughtCola'].checked = amount > 0 && decision.annualIncrease > 0;
    // Ticked for a qualified annuity, bought with Tax Deferred money; unticked for one bought with after-tax money
    el['annuityTaxDeferred'].checked = !(amount > 0 && decision.source?.funding === 'AfterTax');
    renderPurchaseNotes();
}

// Where an after-tax purchase's money came from, in words: Brokerage (with its sale's estimated tax) and Roth
function afterTaxFrom(source) {
    const parts = [];
    const brokerage = source.fromBrokerageBasis + source.fromBrokerageGain;
    if (brokerage > 0.5) parts.push(t('optimal.annuity.fromBrokerage', { amount: formatCurrency(brokerage), tax: formatCurrency(source.brokerageSaleTax) }));
    const roth = source.fromRothBasis + source.fromRothGain;
    if (roth > 0.5) parts.push(t('optimal.annuity.fromRoth', { amount: formatCurrency(roth) }));
    return parts.join(t('optimal.annuity.fromAnd'));
}

const isAfterTax = (decision) => decision?.amount > 0 && decision.source?.funding === 'AfterTax';

// The Annuity tab's status line and the notes under the balances a purchase was paid from, in the page's language
function renderPurchaseNotes() {
    const status = document.getElementById('annuity-status');
    const decision = bought?.decision;
    const result = !!view?.optimum || !!view?.teaser;
    const off = !optimalForm.elements['considerAnnuities'].checked;
    document.getElementById('annuity-not-yet').hidden = result || off;
    document.getElementById('annuity-off').hidden = result || !off;
    status.hidden = !result;
    renderBalanceNotes();
    if (view?.teaser) {
        status.innerHTML = annuityTeaserLine(view.teaser);
        Paywall.decorate(status);
        return;
    }
    if (!decision) {
        status.innerHTML = t('optimal.annuity.notConsidered');
        return;
    }
    const asOf = formatAsOf(decision.rateAsOf);
    if (decision.amount > 0) {
        const values = {
            amount: formatCurrency(decision.amount),
            payment: formatCurrency(decision.monthlyPayment),
            per: formatCurrency(decision.monthlyPer100k),
            asOf,
        };
        status.innerHTML = (isAfterTax(decision)
            ? t('optimal.annuity.statusAfterTax', { ...values, from: afterTaxFrom(decision.source) })
            : t('optimal.annuity.statusBought', values))
            + (decision.annualIncrease > 0 ? ` ${t('optimal.annuity.statusCola')}` : '');
    } else {
        status.innerHTML = t('optimal.annuity.statusNone', { count: decision.amountsConsidered, asOf });
    }
}

// Under Tax Deferred, Brokerage and Roth: what the purchase took from each, and that Find optimal adds it back
function renderBalanceNotes() {
    const full = bought?.full;
    const note = (id, text) => {
        const el = document.getElementById(id);
        el.hidden = !text;
        el.textContent = text ?? '';
    };
    const td = taken('initialTaxableBalance');
    const brokerage = taken('initialBrokerageBasis') + taken('initialBrokerageUnrealizedGain');
    const roth = taken('initialRothBasis') + taken('initialRothUnrealizedGain');
    note('td-purchase-note', td > 0.5
        ? t('optimal.tdPurchaseNote', { amount: formatCurrency(td), full: formatCurrency(full.initialTaxableBalance) })
        : null);
    note('brokerage-purchase-note', brokerage > 0.5
        ? t('optimal.brokeragePurchaseNote', {
            amount: formatCurrency(brokerage),
            tax: formatCurrency(bought.decision.source.brokerageSaleTax),
            full: formatCurrency(full.initialBrokerageBasis + full.initialBrokerageUnrealizedGain),
        })
        : null);
    note('roth-purchase-note', roth > 0.5
        ? t('optimal.rothPurchaseNote', { amount: formatCurrency(roth), full: formatCurrency(full.initialRothBasis + full.initialRothUnrealizedGain) })
        : null);
}

function renderErrors(errors) {
    const items = Object.entries(errors)
        .map(([field, messages]) => `<li><strong>${escapeHtml(I18n.fieldName(field))}:</strong> ${escapeHtml([].concat(messages).map(I18n.translateServerMessage).join(' '))}</li>`)
        .join('');
    return `<div class="error-box"><p>${t('common.fixErrors')}</p><ul>${items}</ul></div>`;
}

function renderClaimingTable(optimum) {
    const rows = optimum.claimingAges.map((c) => {
        const isRecommended = c.age === optimum.recommended.age;
        return `
            <tr${isRecommended ? ' class="recommended-row"' : ''}>
                <td>${c.age}${isRecommended ? ' &#9733;' : ''}</td>
                <td>${formatMonthYear(c.startDate)}</td>
                <td>${formatCurrency(c.monthlyBenefit)}</td>
                <td>${formatCurrency(c.totalSocialSecurity)}</td>
                <td>${perMonth(c.spendAt85)}</td>
                <td>${perMonth(c.spendAtMidpoint)}</td>
                <td>${perMonth(c.spendAt80)}</td>
                <td>${t(`order.${c.withdrawalStrategy}`)}</td>
                <td>${t(`target.short.${c.rothConversionTarget}`)}</td>
            </tr>`;
    }).join('');
    const headings = ['startAt', 'firstPayment', 'monthlyBenefit', 'totalSs', 'spend85', 'spend825', 'spend80', 'order', 'convertsTo']
        .map((key) => `<th>${t(`optimal.table.${key}`)}</th>`).join('');
    return `
        <div class="table-scroll">
            <table class="run-table">
                <thead><tr>${headings}</tr></thead>
                <tbody>${rows}</tbody>
            </table>
        </div>`;
}

// The Free Optimizer's card: the recommended spend as a $500 range, and what the full Optimizer adds - or, once it's
// unlocked in another tab, that running again shows it
function renderTeaser(teaser) {
    const more = Paywall.can('optimizer-full')
        ? `<p><em>${t('optimal.teaserRunAgain')}</em></p>`
        : `<p>${t('optimal.teaserMore')} <span class="paid-badge" data-feature="optimizer-full" hidden></span></p>`;
    return `
        <div class="summary-box ok">
            <p>${t('optimal.teaser', { low: formatCurrency(teaser.monthlyLow), high: formatCurrency(teaser.monthlyHigh) })}</p>
            <p>${annuityTeaserLine(teaser)}</p>
            ${more}
        </div>`;
}

// The Free teaser's nudge toward annuities (Plus), always shown: with the gain when buying one would add $100 a month or
// more (the server rounds it down to $100), else just the suggestion
function annuityTeaserLine(teaser) {
    const text = teaser.annuityGainMonthly
        ? t('optimal.teaserAnnuityGain', { gain: formatCurrency(teaser.annuityGainMonthly) })
        : t('optimal.teaserAnnuity');
    return `${text} <span class="paid-badge" data-feature="annuity" hidden></span>`;
}

// The recommendation card in the tile beside the inputs
function renderCard(optimum) {
    const r = optimum.recommended;
    const inBand = optimum.verifiedSurvivalRate >= 0.8 && optimum.verifiedSurvivalRate <= 0.85;
    const bandNote = inBand ? '' : `<p><em>${t('optimal.bandNote')}</em></p>`;
    return `
        <div class="summary-box ${inBand ? 'ok' : 'warn'}">
            <p>${t('optimal.spendAbout', { amount: perMonth(r.spendAtMidpoint), at85: perMonth(r.spendAt85), at80: perMonth(r.spendAt80) })}</p>
            ${purchaseLine(optimum.purchase)}
            <p>${t('optimal.startSs', { age: r.age, date: formatMonthYear(r.startDate), benefit: formatCurrency(r.monthlyBenefit), total: formatCurrency(r.totalSocialSecurity) })}</p>
            <p>${t('optimal.accounts', { order: t(`order.${r.withdrawalStrategy}`), conversions: t(`target.phrase.${r.rothConversionTarget}`) })} (<a href="model-info.html" class="summary-link">${t('common.why')}</a>)</p>
            ${bandNote}
            <p class="simulator-actions">
                <button type="button" class="run-in-simulator">${t('optimal.runInScenarioRunner')}</button>
                <span class="simulator-message" role="alert"></span>
            </p>
        </div>`;
}

// What the Optimizer decided about buying an annuity (Plus), with Tax Deferred or after-tax money, when it weighed one
function purchaseLine(purchase) {
    if (!purchase) return '';
    const asOf = formatAsOf(purchase.rateAsOf);
    if (!(purchase.amount > 0)) return `<p>${t('optimal.purchaseNone', { count: purchase.amountsConsidered, asOf })}</p>`;
    const values = {
        amount: formatCurrency(purchase.amount),
        payment: formatCurrency(purchase.monthlyPayment),
        asOf,
        without: perMonth(purchase.spendWithout),
        with: perMonth(purchase.spendWith),
    };
    return `<p>${isAfterTax(purchase)
        ? t('optimal.purchase.afterTax', { ...values, from: afterTaxFrom(purchase.source) })
        : t('optimal.purchase', values)}${purchase.annualIncrease > 0 ? ` ${t('optimal.purchase.cola')}` : ''}</p>`;
}

function renderBenefitCurve(benefits) {
    const cells = benefits.map((b) => `<td>${formatCurrency(b.monthlyBenefit)}</td>`).join('');
    const ages = benefits.map((b) => `<th>${b.age}</th>`).join('');
    return `
        <details>
            <summary>${t('optimal.benefitCurve')}</summary>
            <div class="table-scroll">
                <table class="run-table"><thead><tr>${ages}</tr></thead><tbody><tr>${cells}</tr></tbody></table>
            </div>
        </details>`;
}

// Results stream in (newline-delimited JSON from /api/optimal): `start`, a `progress` per claiming age, the
// `result`, then `done` - or `error`.
function progressText(completed, total) {
    return t('optimal.progress', { completed, total });
}

// The card and the detail below the form, from `view`: after each event, and whenever the language changes
function render() {
    let card = '';
    let detail = '';
    if (view?.kind === 'calculating') {
        card = `<p class="optimal-progress">${t('optimal.calculating')}</p>`;
    } else if (view?.kind === 'errors') {
        detail = renderErrors(view.errors);
    } else if (view?.kind === 'failed') {
        detail = `<div class="error-box"><p>${t('common.somethingWrong')}</p></div>`;
    } else if (view?.kind === 'stream') {
        const { start, progress, optimum, teaser, stopped } = view;
        if (optimum) card = renderCard(optimum);
        else if (teaser) card = renderTeaser(teaser);
        else if (stopped) card = `<div class="error-box"><p>${t('optimal.streamStopped')}</p></div>`;
        else card = `<p class="optimal-progress" id="optimal-progress">${progressText(progress.completed, progress.total)}</p>`;
        // The claiming-age comparison and the benefit curve are the full Optimizer's
        detail = `
            <p class="page-intro">${t('optimal.basedOn', { paths: start.paths })}${lastRequest?.filingStatus === 'married' ? ` ${t('runner.filingMarried')}` : ''}</p>
            ${optimum ? `<details open><summary>${t('optimal.compareAges')} <span class="paid-badge" data-feature="optimizer-full" hidden></span></summary>${renderClaimingTable(optimum)}</details>` : ''}
            ${teaser ? '' : renderBenefitCurve(start.benefitByAge)}`;
    }
    cardPlaceholder.hidden = card !== '';
    optimalCard.innerHTML = card;
    optimalResults.innerHTML = detail;
    Paywall.decorate(optimalCard);
    Paywall.decorate(optimalResults);
}

function renderProgress(progress) {
    view.progress = progress;
    const line = document.getElementById('optimal-progress');
    if (line) line.innerHTML = progressText(progress.completed, progress.total);
}

optimalForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    optimalSubmit.disabled = true;
    // A Free visitor's locked inputs hold their Free values once /api/me has answered (paywall.js)
    await Paywall.ready;
    view = { kind: 'calculating' };
    render();
    const request = readRequest();
    let started = false;
    let finished = false;
    const stop = () => {
        if (started) { view.stopped = true; render(); }
        else { view = { kind: 'failed' }; render(); }
    };
    try {
        const response = await fetch('/api/optimal', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request),
        });
        if (!response.ok) {
            const body = await response.json();
            view = { kind: 'errors', errors: body.errors ?? { request: body.title ?? 'The request failed.' } };
            render();
            return;
        }
        await Inputs.readEvents(response, (event) => {
            switch (event.type) {
                case 'start':
                    started = true;
                    lastRequest = request;
                    view = { kind: 'stream', start: event, progress: { completed: 0, total: event.totalSteps }, optimum: null, teaser: null, done: false, stopped: false };
                    render();
                    break;
                case 'progress':
                    renderProgress(event);
                    break;
                case 'result':
                    view.optimum = event.optimum;
                    applyPurchase(event.optimum.purchase ?? null, request);
                    render();
                    break;
                case 'teaser':
                    view.teaser = event;
                    applyPurchase(null, request);
                    render();
                    break;
                case 'done':
                    finished = true;
                    view.done = true;
                    break;
            }
        });
        if (!finished || !(view.optimum || view.teaser)) stop();
    } catch (err) {
        stop();
    } finally {
        optimalSubmit.disabled = false;
    }
});

// "Run in Scenario runner": hand the recommendation and the inputs behind it (the asset mix included) to the Scenario
// runner (index.html), which fills its form and runs. It re-picks the withdrawal order and conversion line with its own test.
function simulatorHandoff(optimum) {
    const r = optimum.recommended;
    const q = lastRequest;
    // A suggested annuity was paid for from the balances: the runner starts with what's left, and the annuity
    const bought = optimum.purchase?.amount > 0 ? optimum.purchase : null;
    const left = (field) => cents(q[field] - taken(field, bought));
    return {
        version: 2,
        recommendedAge: r.age,
        iterations: 99,
        withdrawalMonthly: Math.round(r.spendAtMidpoint / 12),
        socialSecurityStartDate: r.startDate,
        socialSecurityMonthlyAmount: Math.round(r.monthlyBenefit),
        years: q.years,
        birthdate: q.birthdate,
        retirementDate: q.retirementDate,
        initialTaxableBalance: left('initialTaxableBalance'),
        initialRothBasis: left('initialRothBasis'),
        initialRothUnrealizedGain: left('initialRothUnrealizedGain'),
        initialBrokerageBasis: left('initialBrokerageBasis'),
        initialBrokerageUnrealizedGain: left('initialBrokerageUnrealizedGain'),
        newMoney: q.newMoney,
        yearNewMoney: q.yearNewMoney,
        annualStandardDeduction: q.annualStandardDeduction,
        filingStatus: q.filingStatus,
        enableRothConversions: q.enableRothConversions,
        // cola: bought with the app's COLA (its monthlyPayment is the first year's); missing in older hand-offs, meaning
        // none. taxDeferred: false when bought with after-tax money (missing means true), and saleTax the estimated tax
        // on the Brokerage sold for it, already out of the Brokerage balance.
        annuity: bought ? {
            investment: bought.amount,
            monthlyPayment: bought.monthlyPayment,
            cola: bought.annualIncrease > 0,
            taxDeferred: !isAfterTax(bought),
            saleTax: bought.source?.brokerageSaleTax ?? 0,
        } : null,
        assetMix: {
            stockAllocation: q.stockAllocation,
            bondAllocation: q.bondAllocation,
            cashAllocation: q.cashAllocation,
            stockReturn: q.stockReturn,
            stockStdDev: q.stockStdDev,
            bondReturn: q.bondReturn,
            bondStdDev: q.bondStdDev,
            cashReturn: q.cashReturn,
            cashStdDev: q.cashStdDev,
            stockBondCorrelation: q.stockBondCorrelation,
        },
    };
}

function initRunInSimulator() {
    optimalCard.addEventListener('click', (e) => {
        const button = e.target.closest('.run-in-simulator');
        if (!button || !lastRequest || !view?.optimum) return;
        try {
            sessionStorage.setItem(SIMULATOR_HANDOFF_KEY, JSON.stringify(simulatorHandoff(view.optimum)));
        } catch {
            button.parentElement.querySelector('.simulator-message').textContent = t('optimal.handoffFailed');
            return;
        }
        window.location.href = 'index.html';
    });
}

// Access changed in another tab (paywall.js): the teaser card's note follows
document.addEventListener('paywall:change', render);

// Switching language redraws what's on screen; nothing re-runs
document.addEventListener('i18n:change', () => {
    Inputs.updateAllocation(optimalForm);
    Inputs.updateBalanceTotals(optimalForm);
    render();
    renderPurchaseNotes();
});

Inputs.initTabs(optimalForm, 'optimalTab');
Inputs.initAllocation(optimalForm);
Inputs.initMoneyInputs();
Inputs.initBalanceTotals(optimalForm);
Inputs.initAccountTotals(optimalForm);
Inputs.initFilingStatus(optimalForm);
Inputs.sizeInputs(optimalForm);
Inputs.initCollapsibleInputs(optimalForm);
Inputs.initFormFootnotes(optimalForm);
optimalForm.elements['considerAnnuities'].addEventListener('change', renderPurchaseNotes);
initRunInSimulator();
