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

const { parseNumber } = Inputs;

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

function readRequest() {
    const data = new FormData(optimalForm);
    return {
        years: Number(data.get('years')),
        birthdate: data.get('birthdate'),
        retirementDate: data.get('retirementDate'),
        initialTaxableBalance: parseNumber(data.get('initialTaxableBalance')),
        initialRothBasis: parseNumber(data.get('initialRothBasis')),
        initialRothUnrealizedGain: parseNumber(data.get('initialRothUnrealizedGain')),
        initialBrokerageBasis: parseNumber(data.get('initialBrokerageBasis')),
        initialBrokerageUnrealizedGain: parseNumber(data.get('initialBrokerageUnrealizedGain')),
        newMoney: parseNumber(data.get('newMoney')),
        yearNewMoney: Number(data.get('yearNewMoney')),
        socialSecurityAt62: parseNumber(data.get('socialSecurityAt62')),
        socialSecurityAt67: parseNumber(data.get('socialSecurityAt67')),
        socialSecurityAt70: parseNumber(data.get('socialSecurityAt70')),
        annualStandardDeduction: parseNumber(data.get('annualStandardDeduction')),
        filingStatus: data.get('filingStatus'),
        enableRothConversions: data.get('enableRothConversions') === 'on',
        paths: Number(data.get('paths')),
        ...Inputs.readAssetMix(optimalForm),
    };
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
            ${more}
        </div>`;
}

// The recommendation card in the tile beside the inputs
function renderCard(optimum) {
    const r = optimum.recommended;
    const inBand = optimum.verifiedSurvivalRate >= 0.8 && optimum.verifiedSurvivalRate <= 0.85;
    const bandNote = inBand ? '' : `<p><em>${t('optimal.bandNote')}</em></p>`;
    return `
        <div class="summary-box ${inBand ? 'ok' : 'warn'}">
            <p>${t('optimal.spendAbout', { amount: perMonth(r.spendAtMidpoint), at85: perMonth(r.spendAt85), at80: perMonth(r.spendAt80) })}</p>
            <p>${t('optimal.startSs', { age: r.age, date: formatMonthYear(r.startDate), benefit: formatCurrency(r.monthlyBenefit), total: formatCurrency(r.totalSocialSecurity) })}</p>
            <p>${t('optimal.survival', { amount: perMonth(r.spendAtMidpoint), rate: formatPercent(optimum.verifiedSurvivalRate) })}</p>
            <p>${t('optimal.accounts', { order: t(`order.${r.withdrawalStrategy}`), conversions: t(`target.phrase.${r.rothConversionTarget}`) })} (<a href="model-info.html" class="summary-link">${t('common.why')}</a>)</p>
            ${bandNote}
            <p class="simulator-actions">
                <button type="button" class="run-in-simulator">${t('optimal.runInScenarioRunner')}</button>
                <span class="simulator-message" role="alert"></span>
            </p>
        </div>`;
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
        card = `<p class="chart-note">${t('optimal.calculating')}</p>`;
    } else if (view?.kind === 'errors') {
        detail = renderErrors(view.errors);
    } else if (view?.kind === 'failed') {
        detail = `<div class="error-box"><p>${t('common.somethingWrong')}</p></div>`;
    } else if (view?.kind === 'stream') {
        const { start, progress, optimum, teaser, stopped } = view;
        if (optimum) card = renderCard(optimum);
        else if (teaser) card = renderTeaser(teaser);
        else if (stopped) card = `<div class="error-box"><p>${t('optimal.streamStopped')}</p></div>`;
        else card = `<p class="chart-note" id="optimal-progress">${progressText(progress.completed, progress.total)}</p>`;
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

// Calls onEvent with each JSON line of a streamed response as it arrives.
async function readEvents(response, onEvent) {
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffered = '';
    for (;;) {
        const { value, done } = await reader.read();
        buffered += decoder.decode(value, { stream: !done });
        let newline;
        while ((newline = buffered.indexOf('\n')) >= 0) {
            const line = buffered.slice(0, newline).trim();
            buffered = buffered.slice(newline + 1);
            if (line) onEvent(JSON.parse(line));
        }
        if (done) break;
    }
    if (buffered.trim()) onEvent(JSON.parse(buffered));
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
        await readEvents(response, (event) => {
            switch (event.type) {
                case 'start':
                    started = true;
                    lastRequest = request;
                    view = { kind: 'stream', start: event, progress: { completed: 0, total: event.totalClaimingAges }, optimum: null, teaser: null, done: false, stopped: false };
                    render();
                    break;
                case 'progress':
                    renderProgress(event);
                    break;
                case 'result':
                    view.optimum = event.optimum;
                    render();
                    break;
                case 'teaser':
                    view.teaser = event;
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
        initialTaxableBalance: q.initialTaxableBalance,
        initialRothBasis: q.initialRothBasis,
        initialRothUnrealizedGain: q.initialRothUnrealizedGain,
        initialBrokerageBasis: q.initialBrokerageBasis,
        initialBrokerageUnrealizedGain: q.initialBrokerageUnrealizedGain,
        newMoney: q.newMoney,
        yearNewMoney: q.yearNewMoney,
        annualStandardDeduction: q.annualStandardDeduction,
        filingStatus: q.filingStatus,
        enableRothConversions: q.enableRothConversions,
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
});

Inputs.initTabs(optimalForm, 'optimalTab');
Inputs.initAllocation(optimalForm);
Inputs.initMoneyInputs();
Inputs.initBalanceTotals(optimalForm);
Inputs.initAccountTotals(optimalForm);
Inputs.initFilingStatus(optimalForm);
Inputs.initCollapsibleInputs(optimalForm);
initRunInSimulator();
