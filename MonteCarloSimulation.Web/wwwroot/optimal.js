// The Optimal page - the site's landing page. Self-contained on purpose: it shares styles.css with the Scenario runner
// but none of app.js (its header menu is menu.js).
const optimalForm = document.getElementById('optimal-form');
const optimalResults = document.getElementById('optimal-results');
const optimalSubmit = document.getElementById('optimal-submit');

// The request and results on screen, so "Run in Scenario runner" describes what's shown even if the form has changed since.
let lastRequest = null;
let lastResult = null;

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
    return new Date(Date.UTC(y, m - 1, 1)).toLocaleString('en-US', { month: 'short', year: 'numeric', timeZone: 'UTC' });
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
        .map(([field, messages]) => `<li><strong>${escapeHtml(field)}:</strong> ${escapeHtml([].concat(messages).join(' '))}</li>`)
        .join('');
    optimalResults.innerHTML = `<div class="error-box"><p>Please fix the following:</p><ul>${items}</ul></div>`;
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
                <td>${ORDER_NAMES[c.withdrawalStrategy] ?? c.withdrawalStrategy}</td>
                <td>${TARGET_NAMES[c.rothConversionTarget] ?? c.rothConversionTarget}</td>
            </tr>`;
    }).join('');
    return `
        <table class="run-table">
            <thead>
                <tr><th>Start SS at</th><th>First payment</th><th>Monthly benefit</th><th>Total SS collected</th><th>Spend/mo @ 85%</th><th>@ 82.5%</th><th>@ 80%</th><th>Order</th><th>Converts to</th></tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>`;
}

const ORDER_NAMES = { TaxOptimized: 'Tax-optimized', ProRata: 'Pro-rata' };
const TARGET_NAMES = { None: 'No conversions', Bracket12: '12% bracket', Bracket22: 'Top of 22%', Bracket24: 'Top of 24%' };
const TARGET_PHRASES = {
    None: 'no Roth conversions',
    Bracket12: 'Roth conversions filling the 12% bracket',
    Bracket22: 'Roth conversions up to the top of the 22% bracket',
    Bracket24: 'Roth conversions up to the top of the 24% bracket',
};

function renderScenario(scenario) {
    const r = scenario.recommended;
    const inBand = scenario.verifiedSurvivalRate >= 0.8 && scenario.verifiedSurvivalRate <= 0.85;
    const bandNote = inBand ? '' : `
        <p><em>This scenario has so little volatility that nearly every simulated market behaves the same way &mdash;
        survival jumps from almost all to almost none within a few hundred dollars, so it can't be tuned into the 80&ndash;85% band.</em></p>`;
    return `
        <div class="summary-box ${inBand ? 'ok' : 'warn'}">
            <p><strong>${escapeHtml(scenario.description)}</strong></p>
            <p>Spend about <strong>${perMonth(r.spendAtMidpoint)}/month</strong>
               (between ${perMonth(r.spendAt85)} at 85% survival and ${perMonth(r.spendAt80)} at 80%).</p>
            <p>Start Social Security at <strong>${r.age}</strong> (${formatMonthYear(r.startDate)}),
               about ${formatCurrency(r.monthlyBenefit)}/month in today's dollars &mdash;
               <strong>${formatCurrency(r.totalSocialSecurity)}</strong> collected in total over your retirement.</p>
            <p>At ${perMonth(r.spendAtMidpoint)}/month, ${formatPercent(scenario.verifiedSurvivalRate)} of the simulated markets last the full period.</p>
            <p>Accounts drawn in the ${ORDER_NAMES[r.withdrawalStrategy] ?? r.withdrawalStrategy} order, with ${TARGET_PHRASES[r.rothConversionTarget] ?? r.rothConversionTarget} &mdash; the best combination for these inputs, chosen by the app (<a href="model-info.html" class="summary-link">why</a>).</p>
            ${bandNote}
            <p class="simulator-actions">
                <button type="button" class="run-in-simulator" data-scenario-id="${scenario.scenarioId}">Run in Scenario runner</button>
                <span class="simulator-message" role="alert"></span>
            </p>
            <details>
                <summary>Compare Social Security start ages</summary>
                ${renderClaimingTable(scenario)}
            </details>
        </div>`;
}

function renderBenefitCurve(benefits) {
    const cells = benefits.map((b) => `<td>${formatCurrency(b.monthlyBenefit)}</td>`).join('');
    const ages = benefits.map((b) => `<th>${b.age}</th>`).join('');
    return `
        <details>
            <summary>Monthly Social Security benefit by start age (estimated from your three amounts)</summary>
            <table class="run-table"><thead><tr>${ages}</tr></thead><tbody><tr>${cells}</tr></tbody></table>
        </details>`;
}

// Results stream in (newline-delimited JSON from /api/optimal): `start` lays out the page with a slot per scenario,
// `progress` counts claiming ages, each `scenario` fills its slot as soon as it's computed, then `done` - or `error`.
function progressText(completed, total) {
    return `Calculating&hellip; ${completed} of ${total} Social Security start ages checked.`;
}

function renderStart(start) {
    lastResult = { paths: start.paths, scenarios: [], benefitByAge: start.benefitByAge };
    const slots = start.scenarios.map((s) => `
        <div class="summary-box" id="scenario-slot-${s.scenarioId}">
            <p><strong>${escapeHtml(s.description)}</strong></p>
            <p class="scenario-pending">Calculating&hellip;</p>
        </div>`).join('');
    optimalResults.innerHTML = `
        <p class="page-intro">Based on ${start.paths} simulated market paths per investment scenario. "Survival" means the money lasts the full "Years money lasts".</p>
        <p class="page-intro" id="optimal-progress">${progressText(0, start.totalClaimingAges)}</p>
        ${slots}
        ${renderBenefitCurve(start.benefitByAge)}`;
}

function renderProgress(progress) {
    const line = document.getElementById('optimal-progress');
    if (line) line.innerHTML = progressText(progress.completed, progress.total);
}

function renderScenarioResult(scenario) {
    lastResult.scenarios.push(scenario);
    const slot = document.getElementById(`scenario-slot-${scenario.scenarioId}`);
    if (slot) slot.outerHTML = renderScenario(scenario);
}

function renderDone() {
    document.getElementById('optimal-progress')?.remove();
}

// The run stopped early: say so above whatever finished, and mark the scenarios that never arrived.
function renderStreamError() {
    renderDone();
    optimalResults.querySelectorAll('.scenario-pending').forEach((p) => { p.textContent = 'Not calculated.'; });
    optimalResults.insertAdjacentHTML('afterbegin',
        '<div class="error-box"><p>The calculation stopped before it finished. Please try again.</p></div>');
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
    optimalResults.innerHTML = '<p class="page-intro">Calculating&hellip;</p>';
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
            renderErrors(body.errors ?? { request: body.title ?? 'The request failed.' });
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
            else optimalResults.innerHTML = '<div class="error-box"><p>Something went wrong. Please try again.</p></div>';
        }
    } catch (err) {
        if (started) renderStreamError();
        else optimalResults.innerHTML = '<div class="error-box"><p>Something went wrong. Please try again.</p></div>';
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
            button.parentElement.querySelector('.simulator-message').textContent =
                "Couldn't pass the values to the Scenario runner in this browser.";
            return;
        }
        window.location.href = 'index.html';
    });
}

initRunInSimulator();
initMoneyInputs();
