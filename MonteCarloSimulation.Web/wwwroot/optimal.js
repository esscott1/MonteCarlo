// The Optimal page. Self-contained on purpose: it shares styles.css with the main page but none of app.js.
const optimalForm = document.getElementById('optimal-form');
const optimalResults = document.getElementById('optimal-results');
const optimalSubmit = document.getElementById('optimal-submit');

function formatCurrency(value) {
    return Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
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
        withdrawalStrategy: data.get('withdrawalStrategy'),
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
                <td>${formatCurrency(c.spendAt85)}</td>
                <td>${formatCurrency(c.spendAtMidpoint)}</td>
                <td>${formatCurrency(c.spendAt80)}</td>
            </tr>`;
    }).join('');
    return `
        <table class="run-table">
            <thead>
                <tr><th>Start SS at</th><th>First payment</th><th>Monthly benefit</th><th>Spend @ 85%</th><th>@ 82.5%</th><th>@ 80%</th></tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>`;
}

function renderScenario(scenario) {
    const r = scenario.recommended;
    const inBand = scenario.verifiedSurvivalRate >= 0.8 && scenario.verifiedSurvivalRate <= 0.85;
    const bandNote = inBand ? '' : `
        <p><em>This scenario has so little volatility that nearly every simulated market behaves the same way &mdash;
        survival jumps from almost all to almost none within a few hundred dollars, so it can't be tuned into the 80&ndash;85% band.</em></p>`;
    return `
        <div class="summary-box ${inBand ? 'ok' : 'warn'}">
            <p><strong>${escapeHtml(scenario.description)}</strong></p>
            <p>Spend about <strong>${formatCurrency(r.spendAtMidpoint)}/yr</strong>
               (between ${formatCurrency(r.spendAt85)} at 85% survival and ${formatCurrency(r.spendAt80)} at 80%).</p>
            <p>Start Social Security at <strong>${r.age}</strong> (${formatMonthYear(r.startDate)}),
               about ${formatCurrency(r.monthlyBenefit)}/month in today's dollars.</p>
            <p>At ${formatCurrency(r.spendAtMidpoint)}/yr, ${formatPercent(scenario.verifiedSurvivalRate)} of the simulated markets last the full period.</p>
            ${bandNote}
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

function renderResults(result) {
    optimalResults.innerHTML = `
        <p class="page-intro">Based on ${result.paths} simulated market paths per investment scenario. "Survival" means the money lasts the full "Years money lasts".</p>
        ${result.scenarios.map(renderScenario).join('')}
        ${renderBenefitCurve(result.benefitByAge)}`;
}

optimalForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    optimalSubmit.disabled = true;
    optimalResults.innerHTML = '<p class="page-intro">Calculating&hellip; this takes a few seconds.</p>';
    try {
        const response = await fetch('/api/optimal', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(readRequest()),
        });
        const body = await response.json();
        if (!response.ok) {
            renderErrors(body.errors ?? { request: body.title ?? 'The request failed.' });
            return;
        }
        renderResults(body);
    } catch (err) {
        optimalResults.innerHTML = '<div class="error-box"><p>Something went wrong. Please try again.</p></div>';
    } finally {
        optimalSubmit.disabled = false;
    }
});

initMoneyInputs();
