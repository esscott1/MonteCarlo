const form = document.getElementById('run-form');
const scenarioOptions = document.getElementById('scenario-options');
const results = document.getElementById('results');

async function loadScenarios() {
    try {
        const response = await fetch('/api/scenarios');
        const scenarios = await response.json();
        scenarioOptions.innerHTML = scenarios.map((s, i) => `
            <label class="scenario-option">
                <input type="radio" name="scenarioId" value="${s.id}" ${i === 0 ? 'checked' : ''}>
                ${s.menuLabel}
            </label>
        `).join('');
    } catch (err) {
        scenarioOptions.textContent = 'Failed to load scenarios.';
    }
}

function formatCurrency(value) {
    return Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function formatPercent(value) {
    return (Number(value) * 100).toFixed(2) + '%';
}

function renderErrors(errors) {
    const list = Object.entries(errors)
        .map(([field, messages]) => `<li><strong>${field}:</strong> ${messages.join(' ')}</li>`)
        .join('');
    results.innerHTML = `<div class="error-box"><p>Please fix the following:</p><ul>${list}</ul></div>`;
}

function renderPerRunTable(result) {
    const rows = result.endingBalances.map((balance, i) => {
        const failureYear = result.failureYears[i];
        const failureNote = (failureYear !== null && failureYear !== undefined)
            ? `<span class="failure">ran out of money in year ${failureYear}</span>`
            : '<span class="success">&mdash;</span>';
        return `
            <tr>
                <td>${i + 1}</td>
                <td>${formatCurrency(balance)}</td>
                <td>${formatPercent(result.averageAnnualReturns[i])}</td>
                <td>Year ${result.highestReturnYears[i]} (${formatPercent(result.highestReturnValues[i])})</td>
                <td>Year ${result.lowestReturnYears[i]} (${formatPercent(result.lowestReturnValues[i])})</td>
                <td>${failureNote}</td>
            </tr>
        `;
    }).join('');

    return `
        <table class="run-table">
            <thead>
                <tr>
                    <th>Run</th>
                    <th>Ending Balance</th>
                    <th>Avg Annual Return</th>
                    <th>Highest Return</th>
                    <th>Lowest Return</th>
                    <th>Failure Year</th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

function renderSummary(parameters, output) {
    const result = output.result;
    const totalAvgRate = output.allRates.reduce((a, b) => a + b, 0) / output.allRates.length;
    const variance = output.allRates.reduce((a, b) => a + Math.pow(b - totalAvgRate, 2), 0) / output.allRates.length;
    const stdDev = Math.sqrt(variance);

    if (result.outOfMoneyCount > 0) {
        const survival = 1 - (result.outOfMoneyCount / parameters.iterations);
        const avgFailureYear = result.yearsOutOfMoney.reduce((a, b) => a + b, 0) / result.yearsOutOfMoney.length;
        const avgFailureReturn = result.failedScenarioAverages.reduce((a, b) => a + b, 0) / result.failedScenarioAverages.length;
        return `
            <div class="summary-box ${survival > 0.8 ? 'ok' : 'warn'}">
                <p><strong>${survival > 0.8 ? '🙂' : '🙁'} ${result.outOfMoneyCount} of ${parameters.iterations} portfolios did not survive ${parameters.years} years.</strong> Survival rate: ${formatPercent(survival)}</p>
                <p>Scenario: ${parameters.scenarioDescription} &mdash; Initial mean: ${formatPercent(parameters.mean)}, Initial std dev: ${formatPercent(parameters.stdDev)}</p>
                <p>Actual realized average return: ${formatPercent(totalAvgRate)} with std dev ${stdDev.toFixed(4)}</p>
                <p>Inheritance of ${formatCurrency(parameters.newMoney)} in year ${parameters.yearNewMoney} was considered</p>
                <p>Average year of failure: ${avgFailureYear.toFixed(0)}, with an average return of ${formatPercent(avgFailureReturn)}</p>
            </div>
        `;
    }

    const avgBalance = result.successMoneyRemaining.reduce((a, b) => a + b, 0) / result.successMoneyRemaining.length;
    return `
        <div class="summary-box ok">
            <p><strong>🙂 All scenarios survived!</strong></p>
            <p>Scenario: ${parameters.scenarioDescription} &mdash; Initial mean: ${formatPercent(parameters.mean)}, Initial std dev: ${formatPercent(parameters.stdDev)}</p>
            <p>Average balance remaining: ${formatCurrency(avgBalance)}</p>
        </div>
    `;
}

function renderDetail(output) {
    if (output.result.outOfMoneyCount > 0) {
        return `
            <details>
                <summary>Show year-by-year detail for failed runs</summary>
                <pre>${output.outOfMoneyMessage}</pre>
            </details>
        `;
    }

    if (!output.lastBalances) return '';

    const rows = output.lastBalances.slice(1).map((balance, idx) => {
        const ji = idx + 1;
        return `
            <tr>
                <td>${ji}</td>
                <td>${formatCurrency(output.lastAnnualWithdrawals[ji])} (taxable ${formatCurrency(output.lastTaxableWithdrawals[ji])}, nontaxable ${formatCurrency(output.lastNontaxableWithdrawals[ji])})</td>
                <td>${formatCurrency(output.lastAnnualReturns[ji])}</td>
                <td>${formatCurrency(balance)} (taxable ${formatCurrency(output.lastTaxableBalances[ji])}, nontaxable ${formatCurrency(output.lastNontaxableBalances[ji])})</td>
            </tr>
        `;
    }).join('');

    return `
        <details>
            <summary>Show year-by-year detail for the last successful run</summary>
            <table class="run-table">
                <thead>
                    <tr><th>Year</th><th>Withdrawal</th><th>Year's Return ($)</th><th>Total Balance</th></tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </details>
    `;
}

function renderResults(parameters, output) {
    results.innerHTML =
        renderSummary(parameters, output) +
        renderPerRunTable(output.result) +
        renderDetail(output);
}

form.addEventListener('submit', async (e) => {
    e.preventDefault();

    const formData = new FormData(form);
    const payload = {
        scenarioId: Number(formData.get('scenarioId')),
        years: Number(formData.get('years')),
        iterations: Number(formData.get('iterations')),
        withdrawal: Number(formData.get('withdrawal')),
        initialInvestment: Number(formData.get('initialInvestment')),
        newMoney: Number(formData.get('newMoney')),
        yearNewMoney: Number(formData.get('yearNewMoney')),
        socialSecurityYearsUntilStart: Number(formData.get('socialSecurityYearsUntilStart')),
        socialSecurityAnnualAmount: Number(formData.get('socialSecurityAnnualAmount'))
    };

    results.innerHTML = '<p class="loading">Running simulation&hellip;</p>';

    try {
        const response = await fetch('/api/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const problem = await response.json();
            renderErrors(problem.errors || {});
            return;
        }

        const data = await response.json();
        renderResults(data.parameters, data.output);
    } catch (err) {
        results.innerHTML = `<div class="error-box"><p>Request failed: ${err.message}</p></div>`;
    }
});

loadScenarios();
