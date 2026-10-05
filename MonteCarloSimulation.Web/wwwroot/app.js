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

function updateBalanceTotals() {
    const taxDeferred = parseNumber(form.elements['initialTaxableBalance'].value) || 0;
    const rothBasis = parseNumber(form.elements['initialRothBasis'].value) || 0;
    const rothGain = parseNumber(form.elements['initialRothUnrealizedGain'].value) || 0;
    const basis = parseNumber(form.elements['initialBrokerageBasis'].value) || 0;
    const gain = parseNumber(form.elements['initialBrokerageUnrealizedGain'].value) || 0;

    document.getElementById('roth-total').textContent =
        `Total Roth = ${formatCurrency(rothBasis + rothGain)}`;
    document.getElementById('brokerage-total').textContent =
        `Total Brokerage = ${formatCurrency(basis + gain)}`;
    document.getElementById('grand-total').textContent =
        `Total money = ${formatCurrency(taxDeferred + rothBasis + rothGain + basis + gain)}`;
}

// A required input hidden inside the collapsed section can't show its validation message,
// so expand the section whenever any field fails validation on submit.
// Adds whole years to a yyyy-mm-dd date, clamping Feb 29 to Feb 28 in non-leap years (like DateOnly.AddYears).
function addYears(isoDate, years) {
    const [y, m, d] = isoDate.split('-').map(Number);
    const date = new Date(Date.UTC(y + years, m - 1, d));
    if (date.getUTCMonth() !== m - 1) date.setUTCDate(0);
    return date.toISOString().slice(0, 10);
}

// The Social Security start date defaults to the 62nd birthday and follows the birthdate until the user picks
// a start date of their own.
function initSocialSecurityDefault() {
    const birthdate = form.elements['birthdate'];
    const startDate = form.elements['socialSecurityStartDate'];
    let chosenByUser = false;
    const followBirthdate = () => {
        if (!chosenByUser && birthdate.value) startDate.value = addYears(birthdate.value, 62);
    };
    startDate.addEventListener('input', () => { chosenByUser = true; });
    birthdate.addEventListener('input', followBirthdate);
    birthdate.addEventListener('change', followBirthdate);
    followBirthdate();
}

function initCollapsibleInputs() {
    const section = document.getElementById('inputs-section');
    form.addEventListener('invalid', () => { section.open = true; }, true);
}

function initBalanceTotals() {
    ['initialTaxableBalance', 'initialRothBasis', 'initialRothUnrealizedGain', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain']
        .forEach((name) => form.elements[name].addEventListener('input', updateBalanceTotals));
    updateBalanceTotals();
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

function bulletList(items) {
    return `<ul class="breakdown-list">${items.map((item) => `<li>${item}</li>`).join('')}</ul>`;
}

// The engine's day-count/365.25 age can land a hair under a whole number on a birthday
// (e.g. 64.9993), so allow ~2 days of slack before flooring to completed years.
function yearWithAge(yd) {
    const partial = yd.yearFraction < 0.9995 ? `<br><small>partial: ${Math.round(yd.yearFraction * 12)} mo</small>` : '';
    return `${yd.calendarYear} (${Math.floor(yd.ageInYear + 0.005)}yrs)${partial}`;
}

function moneyBreakdown(total, taxableAmt, brokerageAmt, rothAmt, taxablePercentOfBalance, socialSecurity, socialSecurityTax, socialSecurityMonths, brokerageGains) {
    const taxablePct = taxablePercentOfBalance === undefined ? '' : ` (${formatPercent(taxablePercentOfBalance)} of balance)`;
    // A Brokerage sale is part embedded gain, part tax-free return of basis (average cost)
    const brokerageSplit = brokerageGains > 0.5 && brokerageAmt > 0
        ? ` (${formatCurrency(brokerageGains)} gains, ${formatCurrency(brokerageAmt - brokerageGains)} basis)`
        : '';
    const items = [
        `Total: ${formatCurrency(total)}`,
        `Taxable: ${formatCurrency(taxableAmt)}${taxablePct}`,
        `Brokerage: ${formatCurrency(brokerageAmt)}${brokerageSplit}`,
        `Roth: ${formatCurrency(rothAmt)}`,
    ];
    if (socialSecurity > 0) {
        const months = socialSecurityMonths < 12 ? `${socialSecurityMonths} mo, ` : '';
        items.push(`Social Security: ${formatCurrency(socialSecurity)} (${months}${formatCurrency(socialSecurityTax)} tax)`);
    }
    return bulletList(items);
}

function ratePercent(rate) {
    return `${(rate * 100).toFixed(0)}%`;
}

function hasNextBracket(nextRate) {
    return nextRate !== null && nextRate !== undefined;
}

// The year's own Brokerage sales filled the 0% capital gains band, so more taxable income would have pushed
// those gains to 15% - which is why Tax Deferred draws and Roth conversions stopped short. Not when the year
// harvested gains: harvesting only fills room left over, so extra income would just have meant harvesting less.
function zeroRateBandFilledBySales(yd) {
    const bandFull = yd.capitalGainsBracketRate > 0 || (yd.amountUntilNextCapitalGainsBracket ?? 0) < 1;
    return yd.realizedGains > 0.5 && yd.zeroRateGains > 0.5 && !(yd.harvestedGains > 0.5) && bandFull;
}

// A superscript marker with a hover/focus/tap tooltip. Each note in a cell gets its own number, in the order the
// notes appear.
function footnote(text, number) {
    return `<sup class="footnote"><a href="#" class="footnote-ref" aria-label="Note ${number}">${number}</a><span class="footnote-tip" role="tooltip">${text}</span></sup>`;
}

function zeroRateBandNote(yd, conversionsEnabled) {
    let ending = '';
    if (yd.rothConversionAmount > 0) {
        ending = `, so the Roth conversion stopped at <strong>${formatCurrency(yd.rothConversionAmount)}</strong>`;
    } else if (conversionsEnabled) {
        ending = ', so no Roth conversion was made this year';
    }
    return `Brokerage sales this year realized <strong>${formatCurrency(yd.realizedGains)}</strong> of gains, filling the 0% capital gains band. `
        + `Each additional dollar of taxable income (a Tax Deferred withdrawal or Roth conversion) would move a dollar of these gains to the 15% rate${ending}.`;
}

// Which account paid the year's ordinary income tax. The parts add up to the "ord tax paid" line; the conversion's
// capital gains cost is on the LTCG line, not here.
function ordinaryTaxSourcesNote(yd) {
    const parts = [];
    if (yd.taxDeferredWithdrawalTax > 0.5) {
        parts.push(`<strong>${formatCurrency(yd.taxDeferredWithdrawalTax)}</strong> on the Tax Deferred withdrawal, withheld from it`);
    }
    if (yd.socialSecurityTax > 0.5) {
        parts.push(`<strong>${formatCurrency(yd.socialSecurityTax)}</strong> on Social Security, paid out of the benefit`);
    }
    const fromBrokerage = yd.rothConversionOrdinaryTaxFromBrokerage;
    const fromConversion = yd.rothConversionOrdinaryTaxFromConversion;
    if (fromBrokerage + fromConversion > 0.5) {
        const total = `<strong>${formatCurrency(fromBrokerage + fromConversion)}</strong> on the Roth conversion`;
        if (fromBrokerage > 0.5 && fromConversion > 0.5) {
            parts.push(`${total}: <strong>${formatCurrency(fromBrokerage)}</strong> by selling Brokerage, <strong>${formatCurrency(fromConversion)}</strong> out of the converted amount`);
        } else {
            parts.push(`${total}, paid ${fromBrokerage > 0.5 ? 'by selling Brokerage' : 'out of the converted amount'}`);
        }
    }
    return `Who paid this year's <strong>${formatCurrency(yd.ordinaryTaxAmount)}</strong> of ordinary income tax:<br>`
        + parts.map((p) => `&bull; ${p}`).join('<br>');
}

function taxesBreakdown(yd, conversionsEnabled) {
    const zeroRateNote = zeroRateBandFilledBySales(yd) ? zeroRateBandNote(yd, conversionsEnabled) : '';
    const zeroRateOnConversion = zeroRateNote && yd.rothConversionAmount > 0;
    const sourcesNote = yd.ordinaryTaxAmount > 0.5 ? ordinaryTaxSourcesNote(yd) : '';

    // Number the notes in the order they appear: LTCG line, then ordinary tax line, then conversion line
    let next = 1;
    const zeroRateOnGainsLine = zeroRateNote && !zeroRateOnConversion ? footnote(zeroRateNote, next++) : '';
    const sourcesMarker = sourcesNote ? footnote(sourcesNote, next++) : '';
    const zeroRateOnConversionLine = zeroRateOnConversion ? footnote(zeroRateNote, next++) : '';

    const items = [
        `${formatCurrency(yd.capitalGainsTaxAmount)} cap gains paid (${ratePercent(yd.capitalGainsBracketRate)} LTCG bracket)${zeroRateOnGainsLine}`,
        `${formatCurrency(yd.ordinaryTaxAmount)} ord tax paid (${ratePercent(yd.ordinaryBracketRate)} bracket)${sourcesMarker}`,
        hasNextBracket(yd.nextBracketRate)
            ? `${formatCurrency(yd.amountUntilNextBracket)} until ${ratePercent(yd.nextBracketRate)} bracket`
            : 'Already in top ordinary tax bracket',
    ];
    if (yd.harvestedGains > 0) {
        items.push(`${formatCurrency(yd.harvestedGains)} gains harvested at 0%`);
    }
    if (yd.rothConversionAmount > 0) {
        items.push(`${formatCurrency(yd.rothConversionAmount)} converted to Roth (${formatCurrency(yd.rothConversionTax)} tax)${zeroRateOnConversionLine}`);
    }
    if (yd.irmaaSurcharge > 0.5) {
        // Medicare premiums surcharge, set by income two years earlier and paid on top of spending
        items.push(`${formatCurrency(yd.irmaaSurcharge)} Medicare IRMAA surcharge (from ${yd.calendarYear - 2} income)`);
    }
    return bulletList(items);
}

function renderRunDetailTable(yearDetails, conversionsEnabled) {
    const rows = yearDetails.map((yd) => {
        return `
        <tr>
            <td>${yearWithAge(yd)}</td>
            <td>${moneyBreakdown(yd.withdrawal, yd.taxableWithdrawal, yd.brokerageWithdrawal, yd.rothWithdrawal, yd.taxableWithdrawalPercentOfBalance, yd.socialSecurityIncome, yd.socialSecurityTax, yd.socialSecurityMonths, yd.realizedGains)}</td>
            <td>${taxesBreakdown(yd, conversionsEnabled)}</td>
            <td>${formatCurrency(yd.returnAmount)} (${formatPercent(yd.rateOfReturn)}) ${yd.returnAmount > yd.withdrawal ? '&uarr;' : '&darr;'}</td>
            <td>${moneyBreakdown(yd.balance, yd.taxableBalance, yd.brokerageBalance, yd.rothBalance)}</td>
        </tr>
    `;
    }).join('');

    return `
        <table class="run-table">
            <thead>
                <tr><th>Year</th><th>Withdrawal</th><th>Taxes</th><th>Return</th><th>Total Balance</th></tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

function renderPerRunTable(result, conversionsEnabled) {
    const rows = result.runs.map((run, i) => {
        const failureNote = run.failed
            ? `<span class="failure">ran out of money in year ${run.failureYear}</span>`
            : '<span class="success">&mdash;</span>';
        return `
            <tr>
                <td><button type="button" class="run-toggle" aria-expanded="false">${i + 1} <span class="run-toggle-icon">&#9656;</span></button></td>
                <td>${formatCurrency(run.endingBalance)}</td>
                <td>${formatCurrency(run.lowestBalanceValue)} in year ${run.lowestBalanceYear}</td>
                <td>${formatPercent(run.averageAnnualReturn)}</td>
                <td>${formatPercent(run.averageTaxRate)}</td>
                <td>Year ${run.highestReturnYear} (${formatPercent(run.highestReturnValue)})</td>
                <td>Year ${run.lowestReturnYear} (${formatPercent(run.lowestReturnValue)})</td>
                <td>${failureNote}</td>
            </tr>
            <tr class="run-detail-row" hidden>
                <td colspan="8">${renderRunDetailTable(run.years, conversionsEnabled)}</td>
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

// Which accounts the engine drew from: the order it ran (with Automatic, the one it picked for these inputs) and
// the conversion-tax rule from the parameters the server ran with. The app chooses these, not the user.
const WITHDRAWAL_ORDER_NAMES = { TaxOptimized: 'Tax-optimized', ProRata: 'Pro-rata' };
const CONVERSION_TAX_FUNDING = {
    Brokerage: 'paid by selling Brokerage',
    FromConversion: 'paid out of the converted amount',
    BrokerageThenConversion: 'paid by selling Brokerage, then out of the converted amount',
    BridgeAware: "paid by selling Brokerage (keeping what's needed before 59½), then out of the converted amount",
};

// How far conversions filled ordinary income, by RothConversionTarget
const CONVERSION_TARGETS = {
    None: 'no Roth conversions',
    Bracket12: 'Roth conversions filling the 12% bracket',
    Bracket22: 'Roth conversions up to the top of the 22% bracket',
    Bracket24: 'Roth conversions up to the top of the 24% bracket',
};

function accountChoiceLine(parameters, output) {
    const used = output.withdrawalStrategy ?? parameters.withdrawalStrategy;
    const order = WITHDRAWAL_ORDER_NAMES[used] ?? used;
    const target = output.rothConversionTarget ?? 'None';
    const conversions = CONVERSION_TARGETS[target] ?? target;
    const funding = target !== 'None'
        ? `, their tax ${CONVERSION_TAX_FUNDING[parameters.conversionTaxFunding] ?? parameters.conversionTaxFunding}`
        : '';
    const picked = parameters.withdrawalStrategy === 'Automatic' || parameters.rothConversionTarget === 'Automatic'
        ? ' &mdash; the best combination for your inputs, chosen by the app'
        : ' &mdash; chosen by the app';
    return `<p>Accounts drawn in the ${order} order, with ${conversions}${funding}${picked} (<a href="model-info.html" class="summary-link">why</a>)</p>`;
}

function renderSummary(parameters, output) {
    const result = output.result;
    const totalAvgRate = output.allRates.reduce((a, b) => a + b, 0) / output.allRates.length;
    const variance = output.allRates.reduce((a, b) => a + Math.pow(b - totalAvgRate, 2), 0) / output.allRates.length;
    const stdDev = Math.sqrt(variance);
    const avgLifetimeTax = result.runs.reduce((a, run) => a + run.lifetimeTaxesPaid, 0) / result.runs.length;
    const avgLifetimeIrmaa = result.runs.reduce((a, run) => a + run.lifetimeIrmaaSurcharges, 0) / result.runs.length;
    const irmaaLine = avgLifetimeIrmaa > 0.5
        ? `<p>Average lifetime Medicare IRMAA surcharges: ${formatCurrency(avgLifetimeIrmaa)} per run (paid on top of spending, not counted as tax)</p>`
        : '';
    const lifetimeTaxLine = `<p>Average lifetime tax paid: ${formatCurrency(avgLifetimeTax)} per run (ordinary + capital gains, incl. Roth conversions${result.outOfMoneyCount > 0 ? ', through the year a run ran out' : ''})</p>${irmaaLine}`;

    if (result.outOfMoneyCount > 0) {
        const survival = 1 - (result.outOfMoneyCount / parameters.iterations);
        const avgFailureYear = result.yearsOutOfMoney.reduce((a, b) => a + b, 0) / result.yearsOutOfMoney.length;
        const avgFailureReturn = result.failedScenarioAverages.reduce((a, b) => a + b, 0) / result.failedScenarioAverages.length;
        return `
            <div class="summary-box ${survival > 0.8 ? 'ok' : 'warn'}">
                <p><strong>${survival > 0.8 ? '🙂' : '🙁'} ${result.outOfMoneyCount} of ${parameters.iterations} portfolios did not survive ${parameters.years} years.</strong> Survival rate: ${formatPercent(survival)}</p>
                <p>Scenario: ${parameters.scenarioDescription} &mdash; Initial mean: ${formatPercent(parameters.mean)}, Initial std dev: ${formatPercent(parameters.stdDev)}</p>
                <p>Actual realized average return: ${formatPercent(totalAvgRate)} with std dev ${stdDev.toFixed(4)}</p>
                <p>Inheritance of ${formatCurrency(parameters.newMoney)} in ${new Date(parameters.retirementDate).getUTCFullYear() + parameters.yearNewMoney} was considered</p>
                <p>Average year of failure: ${avgFailureYear.toFixed(0)}, with an average return of ${formatPercent(avgFailureReturn)}</p>
                ${lifetimeTaxLine}
                ${accountChoiceLine(parameters, output)}
            </div>
        `;
    }

    const avgBalance = result.successMoneyRemaining.reduce((a, b) => a + b, 0) / result.successMoneyRemaining.length;
    return `
        <div class="summary-box ok">
            <p><strong>🙂 All scenarios survived!</strong></p>
            <p>Scenario: ${parameters.scenarioDescription} &mdash; Initial mean: ${formatPercent(parameters.mean)}, Initial std dev: ${formatPercent(parameters.stdDev)}</p>
            <p>Average balance remaining: ${formatCurrency(avgBalance)}</p>
            ${lifetimeTaxLine}
            ${accountChoiceLine(parameters, output)}
        </div>
    `;
}

function renderDetail(output, conversionsEnabled) {
    if (output.result.outOfMoneyCount > 0) {
        return `
            <details>
                <summary>Show year-by-year detail for failed runs</summary>
                <pre>${output.outOfMoneyMessage}</pre>
            </details>
        `;
    }

    if (!output.lastSuccessfulRun) return '';

    return `
        <details>
            <summary>Show year-by-year detail for the last successful run</summary>
            ${renderRunDetailTable(output.lastSuccessfulRun.slice(1), conversionsEnabled)}
        </details>
    `;
}

function renderResults(parameters, output) {
    results.innerHTML =
        renderSummary(parameters, output) +
        renderPerRunTable(output.result, parameters.enableRothConversions) +
        renderDetail(output, parameters.enableRothConversions);
}

function initFootnotes() {
    results.addEventListener('click', (e) => {
        const ref = e.target.closest('.footnote-ref');
        if (!ref) return;
        e.preventDefault();
        ref.parentElement.classList.toggle('open');
    });
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
    const closeButton = document.getElementById('edit-close');

    const AUTO_CLOSE_MS = 5000;
    let autoCloseTimer = null;

    function cancelAutoClose() {
        if (autoCloseTimer === null) return;
        clearTimeout(autoCloseTimer);
        autoCloseTimer = null;
    }

    // Restarted from scratch on each call, so any interaction inside the flyout
    // gives the reader another full 5 seconds.
    function startAutoClose() {
        cancelAutoClose();
        autoCloseTimer = setTimeout(closeFlyout, AUTO_CLOSE_MS);
    }

    // Only extends a countdown that is already running - it never starts one, so
    // interacting with the form before submitting can't arm the timer.
    function noteActivity() {
        if (autoCloseTimer !== null) startAutoClose();
    }

    function openFlyout() {
        flyout.hidden = false;
        toggle.setAttribute('aria-expanded', 'true');
        flyout.querySelector('input, textarea').focus();
    }

    function closeFlyout() {
        cancelAutoClose();
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
    closeButton.addEventListener('click', closeFlyout);

    // Fires after the button handlers above, so a click that closed the flyout
    // finds the timer already cancelled and doesn't resurrect it.
    flyout.addEventListener('click', noteActivity);
    flyout.addEventListener('keydown', noteActivity);

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

        // The <details> toggle event doesn't bubble, so it needs its own listener
        // rather than relying on the delegated handlers above.
        const trace = editResult.querySelector('.agent-trace');
        if (trace) trace.addEventListener('toggle', noteActivity);

        startAutoClose();
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
        // Entered per month; the model and API work in annual amounts.
        withdrawal: parseNumber(formData.get('withdrawal')) * 12,
        birthdate: formData.get('birthdate'),
        retirementDate: formData.get('retirementDate'),
        initialTaxableBalance: parseNumber(formData.get('initialTaxableBalance')),
        initialRothBasis: parseNumber(formData.get('initialRothBasis')),
        initialRothUnrealizedGain: parseNumber(formData.get('initialRothUnrealizedGain')),
        initialBrokerageBasis: parseNumber(formData.get('initialBrokerageBasis')),
        initialBrokerageUnrealizedGain: parseNumber(formData.get('initialBrokerageUnrealizedGain')),
        newMoney: parseNumber(formData.get('newMoney')),
        yearNewMoney: Number(formData.get('yearNewMoney')),
        socialSecurityStartDate: formData.get('socialSecurityStartDate'),
        socialSecurityMonthlyAmount: parseNumber(formData.get('socialSecurityMonthlyAmount')),
        annualStandardDeduction: parseNumber(formData.get('annualStandardDeduction')),
        enableRothConversions: form.elements['enableRothConversions'].checked
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

// "Run in Scenario runner" on the Optimal page: it leaves one scenario's recommendation and the inputs behind it in
// sessionStorage. Read it once (so a reload doesn't re-run), fill the form, explain where the values came from, and run.
const SIMULATOR_HANDOFF_KEY = 'simulatorHandoff';

function readSimulatorHandoff() {
    try {
        const raw = sessionStorage.getItem(SIMULATOR_HANDOFF_KEY);
        sessionStorage.removeItem(SIMULATOR_HANDOFF_KEY);
        const handoff = raw ? JSON.parse(raw) : null;
        return handoff && handoff.version === 1 ? handoff : null;
    } catch {
        return null;
    }
}

function applySimulatorHandoff() {
    const h = readSimulatorHandoff();
    if (!h) return;

    const set = (name, value) => { form.elements[name].value = value; };
    const setMoney = (name, value) => set(name, formatWithCommas(String(value)));

    const radio = form.querySelector(`input[name="scenarioId"][value="${h.scenarioId}"]`);
    if (radio) radio.checked = true;
    set('years', h.years);
    set('iterations', h.iterations);
    setMoney('withdrawal', h.withdrawalMonthly);
    set('retirementDate', h.retirementDate);
    // Birthdate first, then the Social Security start date - marked as the user's own, so the page's
    // follow-the-62nd-birthday default doesn't overwrite it if the birthdate changes later
    set('birthdate', h.birthdate);
    set('socialSecurityStartDate', h.socialSecurityStartDate);
    form.elements['socialSecurityStartDate'].dispatchEvent(new Event('input'));
    setMoney('socialSecurityMonthlyAmount', h.socialSecurityMonthlyAmount);
    setMoney('annualStandardDeduction', h.annualStandardDeduction);
    ['initialTaxableBalance', 'initialRothBasis', 'initialRothUnrealizedGain', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain', 'newMoney']
        .forEach((name) => setMoney(name, h[name]));
    set('yearNewMoney', h.yearNewMoney);
    form.elements['enableRothConversions'].checked = h.enableRothConversions;
    updateBalanceTotals();

    document.getElementById('handoff-note')?.remove();
    results.insertAdjacentHTML('beforebegin', `
        <div id="handoff-note" class="handoff-note">
            Loaded from the Optimal page: <strong>${escapeHtml(h.scenarioDescription)}</strong>, Social Security at ${h.recommendedAge},
            <strong>${formatCurrency(h.withdrawalMonthly)}/month</strong> &mdash; the spend that survived 82.5% of its simulated markets.
            This run uses ${h.iterations} new random markets, so its survival rate will be close to, not exactly, 82.5%.
            It also re-picks the withdrawal order and conversion line for these inputs.
        </div>`);

    form.requestSubmit();
    document.getElementById('handoff-note').scrollIntoView({ block: 'start' });
}

loadScenarios().then(applySimulatorHandoff);
initMoneyInputs();
initBalanceTotals();
initSocialSecurityDefault();
initCollapsibleInputs();
initRunToggles();
initFootnotes();
initEditFlyout();
