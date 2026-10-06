// The Optimal page - the site's landing page. Self-contained on purpose: it shares styles.css with the Scenario runner
// but none of app.js (its header menu is menu.js). Text comes from i18n.js (t), so the page reads in English or Spanish.
const optimalForm = document.getElementById('optimal-form');
const optimalResults = document.getElementById('optimal-results');
const optimalSubmit = document.getElementById('optimal-submit');

// The request and results on screen, so "Run in Scenario runner" describes what's shown even if the form has changed since.
let lastRequest = null;
let lastResult = null;

// Everything the results area shows, so switching language can redraw it - mid-stream included - without re-running.
// view: null before a run; { kind: 'errors', errors } or { kind: 'failed' } for a run that never started;
// { kind: 'stream', start, progress, done, stopped } once results started arriving.
let view = null;

// sessionStorage key for handing one result to the Scenario runner (app.js reads it once and runs it).
const SIMULATOR_HANDOFF_KEY = 'simulatorHandoff';

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

function parseNumber(value) {
    const stripped = String(value).replace(/,/g, '');
    return stripped === '' ? NaN : Number(stripped);
}

function formatWithCommas(value) {
    const num = parseNumber(value);
    return Number.isNaN(num) ? String(value) : num.toLocaleString('en-US', { maximumFractionDigits: 2 });
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// An investment scenario's description, from the server in English; in Spanish, the translation for its id
function scenarioDescription(id, english) {
    return I18n.language === 'es' ? t(`scenario.${id}.description`) : english;
}

function initMoneyInputs() {
    optimalForm.querySelectorAll('.money-input').forEach((input) => {
        input.addEventListener('blur', () => { input.value = formatWithCommas(input.value); });
    });
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
        enableRothConversions: data.get('enableRothConversions') === 'on',
        paths: Number(data.get('paths')),
    };
}

function renderErrors(errors) {
    const items = Object.entries(errors)
        .map(([field, messages]) => `<li><strong>${escapeHtml(I18n.fieldName(field))}:</strong> ${escapeHtml([].concat(messages).map(I18n.translateServerMessage).join(' '))}</li>`)
        .join('');
    return `<div class="error-box"><p>${t('common.fixErrors')}</p><ul>${items}</ul></div>`;
}

function renderClaimingTable(scenario) {
    const rows = scenario.claimingAges.map((c) => {
        const isRecommended = c.age === scenario.recommended.age;
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
        <table class="run-table">
            <thead><tr>${headings}</tr></thead>
            <tbody>${rows}</tbody>
        </table>`;
}

function renderScenario(scenario) {
    const r = scenario.recommended;
    const inBand = scenario.verifiedSurvivalRate >= 0.8 && scenario.verifiedSurvivalRate <= 0.85;
    const bandNote = inBand ? '' : `<p><em>${t('optimal.bandNote')}</em></p>`;
    return `
        <div class="summary-box ${inBand ? 'ok' : 'warn'}" id="scenario-slot-${scenario.scenarioId}">
            <p><strong>${escapeHtml(scenarioDescription(scenario.scenarioId, scenario.description))}</strong></p>
            <p>${t('optimal.spendAbout', { amount: perMonth(r.spendAtMidpoint), at85: perMonth(r.spendAt85), at80: perMonth(r.spendAt80) })}</p>
            <p>${t('optimal.startSs', { age: r.age, date: formatMonthYear(r.startDate), benefit: formatCurrency(r.monthlyBenefit), total: formatCurrency(r.totalSocialSecurity) })}</p>
            <p>${t('optimal.survival', { amount: perMonth(r.spendAtMidpoint), rate: formatPercent(scenario.verifiedSurvivalRate) })}</p>
            <p>${t('optimal.accounts', { order: t(`order.${r.withdrawalStrategy}`), conversions: t(`target.phrase.${r.rothConversionTarget}`) })} (<a href="model-info.html" class="summary-link">${t('common.why')}</a>)</p>
            ${bandNote}
            <p class="simulator-actions">
                <button type="button" class="run-in-simulator" data-scenario-id="${scenario.scenarioId}">${t('optimal.runInScenarioRunner')}</button>
                <span class="simulator-message" role="alert"></span>
            </p>
            <details>
                <summary>${t('optimal.compareAges')}</summary>
                ${renderClaimingTable(scenario)}
            </details>
        </div>`;
}

function renderPendingScenario(info, stopped) {
    return `
        <div class="summary-box" id="scenario-slot-${info.scenarioId}">
            <p><strong>${escapeHtml(scenarioDescription(info.scenarioId, info.description))}</strong></p>
            <p class="scenario-pending">${stopped ? t('optimal.notCalculated') : t('optimal.calculating')}</p>
        </div>`;
}

function renderBenefitCurve(benefits) {
    const cells = benefits.map((b) => `<td>${formatCurrency(b.monthlyBenefit)}</td>`).join('');
    const ages = benefits.map((b) => `<th>${b.age}</th>`).join('');
    return `
        <details>
            <summary>${t('optimal.benefitCurve')}</summary>
            <table class="run-table"><thead><tr>${ages}</tr></thead><tbody><tr>${cells}</tr></tbody></table>
        </details>`;
}

// Results stream in (newline-delimited JSON from /api/optimal): `start` lays out the page with a slot per scenario,
// `progress` counts claiming ages, each `scenario` fills its slot as soon as it's computed, then `done` - or `error`.
function progressText(completed, total) {
    return t('optimal.progress', { completed, total });
}

// The whole results area from `view` - after `start`, and whenever the language changes
function render() {
    if (!view) {
        optimalResults.innerHTML = '';
    } else if (view.kind === 'calculating') {
        optimalResults.innerHTML = `<p class="page-intro">${t('optimal.calculating')}</p>`;
    } else if (view.kind === 'errors') {
        optimalResults.innerHTML = renderErrors(view.errors);
    } else if (view.kind === 'failed') {
        optimalResults.innerHTML = `<div class="error-box"><p>${t('common.somethingWrong')}</p></div>`;
    } else {
        const { start, progress, done, stopped } = view;
        const finished = new Map(lastResult.scenarios.map((s) => [s.scenarioId, s]));
        const slots = start.scenarios
            .map((info) => (finished.has(info.scenarioId) ? renderScenario(finished.get(info.scenarioId)) : renderPendingScenario(info, stopped)))
            .join('');
        optimalResults.innerHTML = `
            ${stopped ? `<div class="error-box"><p>${t('optimal.streamStopped')}</p></div>` : ''}
            <p class="page-intro">${t('optimal.basedOn', { paths: start.paths })}</p>
            ${done || stopped ? '' : `<p class="page-intro" id="optimal-progress">${progressText(progress.completed, progress.total)}</p>`}
            ${slots}
            ${renderBenefitCurve(start.benefitByAge)}`;
    }
}

function renderStart(start) {
    lastResult = { paths: start.paths, scenarios: [], benefitByAge: start.benefitByAge };
    view = { kind: 'stream', start, progress: { completed: 0, total: start.totalClaimingAges }, done: false, stopped: false };
    render();
}

function renderProgress(progress) {
    view.progress = progress;
    const line = document.getElementById('optimal-progress');
    if (line) line.innerHTML = progressText(progress.completed, progress.total);
}

function renderScenarioResult(scenario) {
    lastResult.scenarios.push(scenario);
    const slot = document.getElementById(`scenario-slot-${scenario.scenarioId}`);
    if (slot) slot.outerHTML = renderScenario(scenario);
}

function renderDone() {
    view.done = true;
    document.getElementById('optimal-progress')?.remove();
}

// The run stopped early: say so above whatever finished, and mark the scenarios that never arrived.
function renderStreamError() {
    view.stopped = true;
    render();
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
    lastResult = null;
    view = { kind: 'calculating' };
    render();
    const request = readRequest();
    let started = false;
    let finished = false;
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
                    renderStart(event);
                    break;
                case 'progress':
                    renderProgress(event);
                    break;
                case 'scenario':
                    renderScenarioResult(event.scenario);
                    break;
                case 'done':
                    finished = true;
                    renderDone();
                    break;
            }
        });
        if (!finished) {
            if (started) renderStreamError();
            else { view = { kind: 'failed' }; render(); }
        }
    } catch (err) {
        if (started) renderStreamError();
        else { view = { kind: 'failed' }; render(); }
    } finally {
        optimalSubmit.disabled = false;
    }
});

// "Run in Scenario runner": hand one scenario's recommendation and the inputs behind it to the Scenario runner
// (index.html), which fills its form and runs. It re-picks the withdrawal order and conversion line with its own test.
function simulatorHandoff(scenario) {
    const r = scenario.recommended;
    const q = lastRequest;
    return {
        version: 1,
        scenarioId: scenario.scenarioId,
        scenarioDescription: scenario.description,
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
        enableRothConversions: q.enableRothConversions,
    };
}

function initRunInSimulator() {
    optimalResults.addEventListener('click', (e) => {
        const button = e.target.closest('.run-in-simulator');
        if (!button || !lastRequest || !lastResult) return;
        const scenario = lastResult.scenarios.find((s) => String(s.scenarioId) === button.dataset.scenarioId);
        if (!scenario) return;
        try {
            sessionStorage.setItem(SIMULATOR_HANDOFF_KEY, JSON.stringify(simulatorHandoff(scenario)));
        } catch {
            button.parentElement.querySelector('.simulator-message').textContent = t('optimal.handoffFailed');
            return;
        }
        window.location.href = 'index.html';
    });
}

// Switching language redraws the results from what's on screen; nothing re-runs
document.addEventListener('i18n:change', render);

initRunInSimulator();
initMoneyInputs();
