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

// User-supplied text is echoed back into the change-request result panel, so it has to be
// escaped rather than interpolated raw.
function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function renderErrors(errors) {
    const list = Object.entries(errors)
        .map(([field, messages]) => `<li><strong>${field}:</strong> ${messages.join(' ')}</li>`)
        .join('');
    results.innerHTML = `<div class="error-box"><p>Please fix the following:</p><ul>${list}</ul></div>`;
}

function renderRunDetailTable(yearDetails) {
    const rows = yearDetails.map((yd) => `
        <tr>
            <td>${yd.year}</td>
            <td>${formatCurrency(yd.withdrawal)} (taxable ${formatCurrency(yd.taxableWithdrawal)}, nontaxable ${formatCurrency(yd.nontaxableWithdrawal)})</td>
            <td>${formatPercent(yd.taxRate)}</td>
            <td>${formatCurrency(yd.returnAmount)} (${formatPercent(yd.rateOfReturn)}) ${yd.returnAmount > yd.withdrawal ? '&uarr;' : '&darr;'}</td>
            <td>${formatCurrency(yd.balance)} (taxable ${formatCurrency(yd.taxableBalance)}, nontaxable ${formatCurrency(yd.nontaxableBalance)})</td>
        </tr>
    `).join('');

    return `
        <table class="run-table">
            <thead>
                <tr><th>Year</th><th>Withdrawal</th><th>Tax Rate</th><th>Return</th><th>Total Balance</th></tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

function renderPerRunTable(result) {
    const rows = result.endingBalances.map((balance, i) => {
        const failureYear = result.failureYears[i];
        const failureNote = (failureYear !== null && failureYear !== undefined)
            ? `<span class="failure">ran out of money in year ${failureYear}</span>`
            : '<span class="success">&mdash;</span>';
        return `
            <tr>
                <td><button type="button" class="run-toggle" aria-expanded="false">${i + 1} <span class="run-toggle-icon">&#9656;</span></button></td>
                <td>${formatCurrency(balance)}</td>
                <td>${formatCurrency(result.lowestBalanceValues[i])} in year ${result.lowestBalanceYears[i]}</td>
                <td>${formatPercent(result.averageAnnualReturns[i])}</td>
                <td>${formatPercent(result.averageTaxRates[i])}</td>
                <td>Year ${result.highestReturnYears[i]} (${formatPercent(result.highestReturnValues[i])})</td>
                <td>Year ${result.lowestReturnYears[i]} (${formatPercent(result.lowestReturnValues[i])})</td>
                <td>${failureNote}</td>
            </tr>
            <tr class="run-detail-row" hidden>
                <td colspan="8">${renderRunDetailTable(result.runDetails[i])}</td>
            </tr>
        `;
    }).join('');

    return `
        <table class="run-table">
            <thead>
                <tr>
                    <th>Run</th>
                    <th>Ending Balance</th>
                    <th>Lowest Balance</th>
                    <th>Avg Annual Return</th>
                    <th>Avg Tax Rate</th>
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
                <td>${formatPercent(output.lastTaxRates[ji])}</td>
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
                    <tr><th>Year</th><th>Withdrawal</th><th>Tax Rate</th><th>Year's Return ($)</th><th>Total Balance</th></tr>
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

function initRunToggles() {
    results.addEventListener('click', (e) => {
        const button = e.target.closest('.run-toggle');
        if (!button) return;

        const detailRow = button.closest('tr').nextElementSibling;
        const expanded = button.getAttribute('aria-expanded') === 'true';

        detailRow.hidden = expanded;
        button.setAttribute('aria-expanded', String(!expanded));
        button.querySelector('.run-toggle-icon').innerHTML = expanded ? '&#9656;' : '&#9662;';
    });
}

function initEditFlyout() {
    const toggle = document.getElementById('edit-toggle');
    const flyout = document.getElementById('edit-flyout');
    const cancel = document.getElementById('edit-cancel');
    const header = toggle.closest('.page-header');
    const editResult = document.getElementById('edit-result');
    const submitButton = flyout.querySelector('button[type="submit"]');

    function openFlyout() {
        flyout.hidden = false;
        toggle.setAttribute('aria-expanded', 'true');
        flyout.querySelector('input, textarea').focus();
    }

    function closeFlyout() {
        flyout.hidden = true;
        toggle.setAttribute('aria-expanded', 'false');
        flyout.reset();
        editResult.innerHTML = '';
        toggle.focus();
    }

    toggle.addEventListener('click', () => {
        if (flyout.hidden) openFlyout(); else closeFlyout();
    });

    cancel.addEventListener('click', closeFlyout);

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !flyout.hidden) closeFlyout();
    });

    document.addEventListener('click', (e) => {
        if (flyout.hidden) return;
        if (!header.contains(e.target)) closeFlyout();
    });

    function renderAgentResult(data) {
        const verdict = data.serverCorrected
            ? "Server verification rebuilt the fields — the agent's draft did not match the required format exactly."
            : "Server verification passed — the agent's draft matched the required format exactly.";

        editResult.innerHTML = `
            <div class="summary-box ok">
                <p>Created <a href="${escapeHtml(data.issueUrl)}" target="_blank" rel="noopener">${escapeHtml(data.issueKey)}</a> in Jira, still in To Do.</p>
            </div>
            <details class="agent-trace">
                <summary>What the agent did</summary>
                <p>The agent was forced to call one tool, <code>create_jira_story</code>, with these arguments:</p>
                <p><strong>summary:</strong> ${escapeHtml(data.summary)}</p>
                <p><strong>description:</strong> ${escapeHtml(data.description)}</p>
                <p class="agent-note">${verdict}</p>
            </details>
        `;
    }

    flyout.addEventListener('submit', async (e) => {
        e.preventDefault();

        const payload = {
            summary: flyout.elements.summary.value,
            description: flyout.elements.description.value,
            passphrase: flyout.elements.passphrase.value
        };

        submitButton.disabled = true;
        editResult.innerHTML = '<p class="loading">Asking the agent to create the story&hellip;</p>';

        try {
            const response = await fetch('/api/change-request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            // The rate limiter rejects before the endpoint runs, so there's no JSON body to read.
            if (response.status === 429) {
                editResult.innerHTML = '<div class="error-box"><p>Too many change requests from this address. Try again later.</p></div>';
                return;
            }

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                if (data.errors) {
                    const list = Object.entries(data.errors)
                        .map(([field, messages]) => `<li><strong>${escapeHtml(field)}:</strong> ${escapeHtml(messages.join(' '))}</li>`)
                        .join('');
                    editResult.innerHTML = `<div class="error-box"><p>Please fix the following:</p><ul>${list}</ul></div>`;
                } else {
                    editResult.innerHTML = `<div class="error-box"><p>${escapeHtml(data.message || 'The request failed.')}</p></div>`;
                }
                return;
            }

            renderAgentResult(data);
        } catch (err) {
            editResult.innerHTML = `<div class="error-box"><p>Request failed: ${escapeHtml(err.message)}</p></div>`;
        } finally {
            submitButton.disabled = false;
        }
    });
}

form.addEventListener('submit', async (e) => {
    e.preventDefault();

    const formData = new FormData(form);
    const payload = {
        scenarioId: Number(formData.get('scenarioId')),
        years: Number(formData.get('years')),
        iterations: Number(formData.get('iterations')),
        withdrawal: parseNumber(formData.get('withdrawal')),
        initialTaxableBalance: parseNumber(formData.get('initialTaxableBalance')),
        initialNontaxableBalance: parseNumber(formData.get('initialNontaxableBalance')),
        newMoney: parseNumber(formData.get('newMoney')),
        yearNewMoney: Number(formData.get('yearNewMoney')),
        socialSecurityYearsUntilStart: Number(formData.get('socialSecurityYearsUntilStart')),
        socialSecurityAnnualAmount: parseNumber(formData.get('socialSecurityAnnualAmount')),
        annualStandardDeduction: parseNumber(formData.get('annualStandardDeduction'))
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
initMoneyInputs();
initRunToggles();
initEditFlyout();
