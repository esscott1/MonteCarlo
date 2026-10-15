// The Scenario runner. Text comes from i18n.js (t), so the page reads in English or Spanish.
const form = document.getElementById('run-form');
const results = document.getElementById('results');

// What the page shows, kept so switching language can redraw it without re-running: the results area
// ({ kind: 'loading' | 'errors' | 'failed' | 'results', ... }) and the note about values loaded from the Optimal page.
let view = null;
let handoff = null;

// The inputs tile's shared code (tabs, money fields, the Asset Classes table, balance totals) is in inputs.js
const { parseNumber, formatWithCommas, formatAllocation, formatAsOf } = Inputs;
const updateAllocation = () => Inputs.updateAllocation(form);
const updateBalanceTotals = () => Inputs.updateBalanceTotals(form);

// One formatter for every amount: toLocaleString with options builds a new one on every call (about 60 times slower),
// and a run's year table formats over a thousand amounts
const usdFormat = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });

function formatCurrency(value) {
    return usdFormat.format(Number(value));
}

function formatPercent(value) {
    return (Number(value) * 100).toFixed(2) + '%';
}

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

// The Annuity tab (Plus; its COLA Pro): an annuity already owned. Its fields are disabled until "Include an annuity"
// is ticked; a DIA shows its start date, at least two years from today (the visitor's own date), and a COLA its rate.
function initAnnuity() {
    const include = form.elements['includeAnnuity'];
    const details = document.getElementById('annuity-details');
    const start = form.elements['annuityStartDate'];
    const cola = form.elements['annuityCola'];
    const today = new Date();
    const pad = (n) => String(n).padStart(2, '0');
    const earliest = addYears(`${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`, 2);
    start.min = earliest;
    // Also what a Free visitor's lock holds it at (paywall.js)
    start.defaultValue = earliest;
    start.value = earliest;
    const update = () => {
        details.disabled = !include.checked;
        const dia = form.elements['annuityType'].value === 'dia';
        document.getElementById('annuity-start').hidden = !dia;
        start.disabled = !dia;
        document.getElementById('annuity-cola-rate').hidden = !cola.checked;
    };
    [include, cola, ...form.elements['annuityType']].forEach((input) => input.addEventListener('change', update));
    update();
}

// The annuity as /api/run takes it. Read from the elements, not FormData: disabled controls aren't in FormData.
function annuityRequest() {
    const el = form.elements;
    const dia = el['annuityType'].value === 'dia';
    return {
        includeAnnuity: el['includeAnnuity'].checked,
        annuityMonthlyAmount: parseNumber(el['annuityMonthlyAmount'].value),
        annuityInvestment: parseNumber(el['annuityInvestment'].value),
        annuityType: el['annuityType'].value,
        annuityStartDate: dia && el['annuityStartDate'].value ? el['annuityStartDate'].value : null,
        annuityTaxDeferred: el['annuityTaxDeferred'].checked,
        annuityCola: el['annuityCola'].checked,
        annuityColaRate: Number(el['annuityColaRate'].value),
    };
}

// The Annuity tab's suggested SPIA rate (Plus; choosing the gender is Pro, else the male rate): the best current payout
// per $100,000 for the age at the Retire On Date, from the LifeAnnuities.us SPIA Payout Index by way of /api/spia-rate,
// with the figures' as-of date. Shown for a SPIA only. The last answer is kept, so a language switch redraws it.
let annuityRate = null;
let annuityRateRequest = 0;

function annuityPurchaseAge() {
    const birthdate = form.elements['birthdate'].value;
    const retirement = form.elements['retirementDate'].value;
    if (!birthdate || !retirement) return null;
    return (Date.parse(retirement) - Date.parse(birthdate)) / (365.25 * 86_400_000);
}

async function refreshAnnuityRate() {
    const box = document.getElementById('annuity-rate');
    const el = form.elements;
    const request = ++annuityRateRequest;
    if (!el['includeAnnuity'].checked || !Paywall.can('annuity')) {
        box.hidden = true;
        return;
    }
    if (el['annuityType'].value !== 'spia') {
        annuityRate = { kind: 'dia' };
        renderAnnuityRate();
        return;
    }
    const age = annuityPurchaseAge();
    if (age === null) {
        box.hidden = true;
        return;
    }
    const sex = Paywall.can('annuity-gender') ? el['annuitySex'].value : 'male';
    try {
        const response = await fetch(`/api/spia-rate?age=${age.toFixed(2)}&sex=${sex}`);
        if (request !== annuityRateRequest) return;
        if (!response.ok) {
            box.hidden = true;
            return;
        }
        annuityRate = { kind: 'rate', ...(await response.json()) };
    } catch {
        if (request === annuityRateRequest) box.hidden = true;
        return;
    }
    if (request === annuityRateRequest) renderAnnuityRate();
}

function renderAnnuityRate() {
    const box = document.getElementById('annuity-rate');
    const text = document.getElementById('annuity-rate-text');
    const use = document.getElementById('annuity-rate-use');
    const source = document.getElementById('annuity-rate-source');
    const rate = annuityRate;
    if (!rate) return;
    box.hidden = false;
    if (rate.kind === 'dia') {
        text.textContent = t('runner.rate.dia');
        use.hidden = true;
        source.innerHTML = '';
        return;
    }
    const asOf = formatAsOf(rate.asOf);
    const links = {
        index: `<a href="${escapeHtml(rate.sourceUrl)}" target="_blank" rel="noopener">LifeAnnuities.us SPIA Payout Index</a>`,
        license: `<a href="${escapeHtml(rate.licenseUrl)}" target="_blank" rel="noopener">CC BY 4.0</a>`,
    };
    if (rate.monthlyPer100k === null) {
        text.textContent = t('runner.rate.noAge', { age: rate.age, asOf });
        use.hidden = true;
        source.innerHTML = t('runner.rate.sourcePublished', links);
        return;
    }
    const investment = parseNumber(form.elements['annuityInvestment'].value) || 0;
    const monthly = Math.round(rate.monthlyPer100k * investment / 100_000);
    text.textContent = t(rate.estimated ? 'runner.rate.estimated' : 'runner.rate.published', {
        who: t(rate.sex === 'female' ? 'runner.rate.woman' : 'runner.rate.man'),
        age: rate.age,
        asOf,
        per: formatCurrency(rate.monthlyPer100k),
        amount: formatCurrency(monthly),
        investment: formatCurrency(investment),
    });
    use.hidden = !(monthly > 0);
    use.dataset.amount = String(monthly);
    source.innerHTML = t(rate.estimated ? 'runner.rate.sourceEstimated' : 'runner.rate.sourcePublished', links);
}

function initAnnuityRate() {
    const el = form.elements;
    [el['includeAnnuity'], ...el['annuityType'], ...el['annuitySex'], el['birthdate'], el['retirementDate']]
        .forEach((input) => input.addEventListener('change', refreshAnnuityRate));
    // The amount scales with the investment; the rate per $100,000 doesn't change
    el['annuityInvestment'].addEventListener('input', renderAnnuityRate);
    document.getElementById('annuity-rate-use').addEventListener('click', (e) => {
        const payment = el['annuityMonthlyAmount'];
        payment.value = formatWithCommas(e.currentTarget.dataset.amount);
        payment.dispatchEvent(new Event('input', { bubbles: true }));
    });
    document.addEventListener('paywall:change', refreshAnnuityRate);
    document.addEventListener('i18n:change', renderAnnuityRate);
    Paywall.ready.then(refreshAnnuityRate);
}

// User-supplied text is echoed back into the change-request result panel, so it has to be
// escaped rather than interpolated raw.
function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// Validation errors from the server, which writes them in English: field names and messages are shown in the
// page's language
function errorList(errors) {
    const list = Object.entries(errors)
        .map(([field, messages]) =>
            `<li><strong>${escapeHtml(I18n.fieldName(field))}:</strong> ${escapeHtml([].concat(messages).map(I18n.translateServerMessage).join(' '))}</li>`)
        .join('');
    return `<div class="error-box"><p>${t('common.fixErrors')}</p><ul>${list}</ul></div>`;
}

function bulletList(items) {
    return `<ul class="breakdown-list">${items.map((item) => `<li>${item}</li>`).join('')}</ul>`;
}

// The engine's day-count/365.25 age can land a hair under a whole number on a birthday
// (e.g. 64.9993), so allow ~2 days of slack before flooring to completed years.
function yearWithAge(yd) {
    const partial = yd.yearFraction < 0.9995 ? `<br><small>${t('runner.partial', { months: Math.round(yd.yearFraction * 12) })}</small>` : '';
    return `${t('runner.yearAge', { year: yd.calendarYear, age: Math.floor(yd.ageInYear + 0.005) })}${partial}`;
}

// The Total Balance column: the year-end total and each account
function balanceBreakdown(yd) {
    return bulletList([
        t('runner.money.total', { amount: formatCurrency(yd.balance) }),
        t('runner.money.taxable', { amount: formatCurrency(yd.taxableBalance) }),
        t('runner.money.brokerage', { amount: formatCurrency(yd.brokerageBalance) }),
        t('runner.money.roth', { amount: formatCurrency(yd.rothBalance) }),
    ]);
}

// Everything the year drew before tax: each account's withdrawal, Social Security, the annuity, and the part of a Roth
// conversion withheld from Tax Deferred for its tax
const yearWithdrawals = (yd) => yd.taxableWithdrawal + yd.brokerageWithdrawal + yd.rothWithdrawal
    + yd.socialSecurityIncome + yd.annuityIncome + (yd.rothConversionTaxWithheld ?? 0);

const yearTaxes = (yd) => yd.ordinaryTaxAmount + yd.capitalGainsTaxAmount;

// The Withdrawal column: the year's net after taxes on top, then the total withdrawals it came from and each one.
// Total withdrawals less the Taxes column's total taxes is the net, to the dollar as shown. The net is the year's
// spending, plus any Medicare surcharge and any income beyond spending saved to Brokerage, which it then names.
function withdrawalBreakdown(yd) {
    const net = Math.round(yearWithdrawals(yd)) - Math.round(yearTaxes(yd));
    const irmaa = yd.irmaaSurcharge ?? 0; // not sent without the tax detail
    const saved = yd.incomeSurplusToBrokerage + yd.rmdSurplusToBrokerage;
    const uses = irmaa > 0.5 || saved > 0.5
        ? ` (${[
            t('runner.money.netSpending', { amount: formatCurrency(yd.withdrawal) }),
            ...(irmaa > 0.5 ? [t('runner.money.netIrmaa', { amount: formatCurrency(irmaa) })] : []),
            ...(saved > 0.5 ? [t('runner.money.netSaved', { amount: formatCurrency(saved) })] : []),
        ].join(', ')})`
        : '';

    const pct = ` ${t('runner.money.ofBalance', { pct: formatPercent(yd.taxableWithdrawalPercentOfBalance) })}`;
    // A Brokerage sale is part embedded gain, part tax-free return of basis (average cost)
    const brokerageSplit = yd.realizedGains > 0.5 && yd.brokerageWithdrawal > 0
        ? ` ${t('runner.money.gainsBasis', { gains: formatCurrency(yd.realizedGains), basis: formatCurrency(yd.brokerageWithdrawal - yd.realizedGains) })}`
        : '';
    const parts = [
        t('runner.money.taxable', { amount: formatCurrency(yd.taxableWithdrawal) }) + pct + rmdFootnote(yd),
        t('runner.money.brokerage', { amount: formatCurrency(yd.brokerageWithdrawal) }) + brokerageSplit,
        t('runner.money.roth', { amount: formatCurrency(yd.rothWithdrawal) }),
    ];
    if (yd.socialSecurityIncome > 0) {
        const detail = yd.socialSecurityMonths < 12
            ? t('runner.money.ssMonthsTax', { months: yd.socialSecurityMonths, tax: formatCurrency(yd.socialSecurityTax) })
            : t('runner.money.ssTax', { tax: formatCurrency(yd.socialSecurityTax) });
        parts.push(t('runner.money.socialSecurity', { amount: formatCurrency(yd.socialSecurityIncome), detail }));
    }
    if (yd.annuityIncome > 0) {
        const detail = yd.annuityPayments < 12
            ? t('runner.money.ssMonthsTax', { months: yd.annuityPayments, tax: formatCurrency(yd.annuityTax) })
            : t('runner.money.ssTax', { tax: formatCurrency(yd.annuityTax) });
        parts.push(t('runner.money.annuity', { amount: formatCurrency(yd.annuityIncome), detail }) + annuityFootnote(yd));
    }
    if (yd.rothConversionTaxWithheld > 0.5) {
        parts.push(t('runner.money.conversionWithheld', { amount: formatCurrency(yd.rothConversionTaxWithheld) }));
    }
    return bulletList([
        t('runner.money.net', { amount: formatCurrency(net) }) + uses,
        t('runner.money.totalWithdrawals', { amount: formatCurrency(yearWithdrawals(yd)) }) + bulletList(parts),
    ]);
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
// notes appear; a labelled note (RMD) passes its label and its own aria label instead.
function footnote(text, marker, ariaLabel = t('runner.note.aria', { number: marker })) {
    return `<sup class="footnote"><a href="#" class="footnote-ref" aria-label="${ariaLabel}">${marker}</a><span class="footnote-tip" role="tooltip">${text}</span></sup>`;
}

// A year with a required minimum distribution: what the IRS required from Tax Deferred and why (the age reached this
// year and the table's divisor), then whether the year drew more than that for spending or saved what spending
// didn't need.
function rmdFootnote(yd) {
    if (!(yd.requiredMinimumDistribution > 0.5)) return '';
    const age = yd.calendarYear - Number(view.parameters.birthdate.slice(0, 4));
    const parts = [t('runner.note.rmd', { amount: formatCurrency(yd.requiredMinimumDistribution), age, divisor: yd.rmdDivisor.toFixed(1) })];
    const extra = yd.taxableWithdrawal - yd.requiredMinimumDistribution;
    if (extra > 0.5) {
        parts.push(t('runner.note.rmd.more', { rmd: formatCurrency(yd.requiredMinimumDistribution), extra: formatCurrency(extra) }));
    }
    if (yd.rmdSurplusToBrokerage > 0.5) {
        parts.push(t('runner.note.rmd.surplus', { amount: formatCurrency(yd.rmdSurplusToBrokerage) }));
    }
    return footnote(parts.join(' '), t('runner.note.rmd.marker'), t('runner.note.rmd.aria'));
}

// How the year's annuity payments were taxed: part a tax-free return of basis (the first 26 years of a non-qualified
// annuity), or all ordinary income (in a tax-deferred account, or once the basis has come back)
function annuityFootnote(yd) {
    const returned = yd.annuityIncome - yd.annuityTaxableIncome;
    let text;
    if (returned > 0.5) {
        text = t('runner.note.annuity.basis', { basis: formatCurrency(returned), taxable: formatCurrency(yd.annuityTaxableIncome) });
    } else if (view.parameters.annuity?.basis > 0) {
        text = t('runner.note.annuity.recovered');
    } else {
        text = t('runner.note.annuity.taxDeferred');
    }
    return footnote(text, 1);
}

function zeroRateBandNote(yd, conversionsEnabled) {
    let ending = '';
    if (yd.rothConversionAmount > 0) {
        ending = t('runner.note.zeroRate.conversionStopped', { amount: formatCurrency(yd.rothConversionAmount) });
    } else if (conversionsEnabled) {
        ending = t('runner.note.zeroRate.noConversion');
    }
    return t('runner.note.zeroRate', { gains: formatCurrency(yd.realizedGains), ending });
}

// Which account paid the year's ordinary income tax. The parts add up to the "ord tax paid" line; the conversion's
// capital gains cost is on the LTCG line, not here.
function ordinaryTaxSourcesNote(yd) {
    const parts = [];
    if (yd.taxDeferredWithdrawalTax > 0.5) {
        parts.push(t('runner.note.taxDeferred', { amount: formatCurrency(yd.taxDeferredWithdrawalTax) }));
    }
    if (yd.socialSecurityTax > 0.5) {
        parts.push(t('runner.note.socialSecurity', { amount: formatCurrency(yd.socialSecurityTax) }));
    }
    if (yd.annuityTax > 0.5) {
        parts.push(t('runner.note.annuityTax', { amount: formatCurrency(yd.annuityTax) }));
    }
    const fromBrokerage = yd.rothConversionOrdinaryTaxFromBrokerage;
    const fromConversion = yd.rothConversionOrdinaryTaxFromConversion;
    if (fromBrokerage + fromConversion > 0.5) {
        const total = formatCurrency(fromBrokerage + fromConversion);
        if (fromBrokerage > 0.5 && fromConversion > 0.5) {
            parts.push(t('runner.note.conversionSplit', { total, brokerage: formatCurrency(fromBrokerage), conversion: formatCurrency(fromConversion) }));
        } else {
            parts.push(t(fromBrokerage > 0.5 ? 'runner.note.conversionFromBrokerage' : 'runner.note.conversionFromConversion', { total }));
        }
    }
    return `${t('runner.note.whoPaid', { amount: formatCurrency(yd.ordinaryTaxAmount) })}<br>`
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
        t('runner.tax.total', { amount: formatCurrency(yearTaxes(yd)) }),
        t('runner.tax.capGains', { amount: formatCurrency(yd.capitalGainsTaxAmount), rate: ratePercent(yd.capitalGainsBracketRate) }) + zeroRateOnGainsLine,
        t('runner.tax.ordinary', { amount: formatCurrency(yd.ordinaryTaxAmount), rate: ratePercent(yd.ordinaryBracketRate) }) + sourcesMarker,
        hasNextBracket(yd.nextBracketRate)
            ? t('runner.tax.untilNext', { amount: formatCurrency(yd.amountUntilNextBracket), rate: ratePercent(yd.nextBracketRate) })
            : t('runner.tax.topBracket'),
    ];
    if (yd.harvestedGains > 0) {
        items.push(t('runner.tax.harvested', { amount: formatCurrency(yd.harvestedGains) }));
    }
    if (yd.rothConversionAmount > 0) {
        // What was withheld beyond the conversion's own ordinary tax is the capital gains tax it caused, by pushing the
        // year's gains into a higher bracket (the Withdrawal column's withheld line is both)
        const gainsWithheld = (yd.rothConversionTaxWithheld ?? 0) - yd.rothConversionOrdinaryTaxFromConversion;
        const converted = { amount: formatCurrency(yd.rothConversionAmount), tax: formatCurrency(yd.rothConversionTax), gains: formatCurrency(gainsWithheld) };
        items.push(t(gainsWithheld > 0.5 ? 'runner.tax.convertedWithGains' : 'runner.tax.converted', converted) + zeroRateOnConversionLine);
    }
    if (yd.irmaaSurcharge > 0.5) {
        // Medicare premiums surcharge, set by income two years earlier and paid on top of spending
        items.push(t('runner.tax.irmaa', { amount: formatCurrency(yd.irmaaSurcharge), year: yd.calendarYear - 2 }));
    }
    return bulletList(items);
}

// Without the tax-detail feature the server sends only each year's tax totals (FreeRunView)
function taxTotals(yd) {
    return bulletList([
        t('runner.tax.total', { amount: formatCurrency(yearTaxes(yd)) }),
        t('runner.tax.capGainsTotal', { amount: formatCurrency(yd.capitalGainsTaxAmount) }),
        t('runner.tax.ordinaryTotal', { amount: formatCurrency(yd.ordinaryTaxAmount) }),
    ]);
}

// Above a year table without the tax detail: that the first years preview it, or, once it's unlocked, that a new run
// shows every year's
function taxDetailNote() {
    return Paywall.can('tax-detail')
        ? `<p class="table-note">${t('runner.tax.runAgain')}</p>`
        : `<p class="table-note">${t('runner.tax.teaserNote', { years: view?.taxDetailYears ?? 0 })}</p>`;
}

// A year's Taxes cell. Without the tax-detail feature the server keeps each run's first years' detail as a preview of
// Plus (FreeRunView): those show greyed with the Plus badge, and later years their totals only.
function taxCell(yd, conversionsEnabled, taxDetail) {
    if (taxDetail) return `<td>${taxesBreakdown(yd, conversionsEnabled)}</td>`;
    if (yd.ordinaryBracketRate === undefined) return `<td>${taxTotals(yd)}</td>`;
    return Paywall.can('tax-detail')
        ? `<td>${taxesBreakdown(yd, conversionsEnabled)}</td>`
        : `<td class="teaser">${taxesBreakdown(yd, conversionsEnabled)} <span class="paid-badge" data-feature="tax-detail" hidden></span></td>`;
}

function renderRunDetailTable(yearDetails, conversionsEnabled, taxDetail) {
    const rows = yearDetails.map((yd) => {
        return `
        <tr>
            <td>${yearWithAge(yd)}</td>
            <td>${withdrawalBreakdown(yd)}</td>
            ${taxCell(yd, conversionsEnabled, taxDetail)}
            <td>${formatCurrency(yd.returnAmount)} (${formatPercent(yd.rateOfReturn)}) ${yd.returnAmount > yd.withdrawal ? '&uarr;' : '&darr;'}</td>
            <td>${balanceBreakdown(yd)}</td>
        </tr>
    `;
    }).join('');

    return `${taxDetail ? '' : taxDetailNote()}
        <table class="run-table">
            <thead>
                <tr>${['year', 'withdrawal', 'taxes', 'return', 'totalBalance'].map((key) => `<th>${t(`runner.table.${key}`)}${key === 'taxes' ? ' <span class="paid-badge" data-feature="tax-detail" hidden></span>' : ''}</th>`).join('')}</tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

// One line per run. A run's year table is built the first time it's expanded (initRunToggles): building all of them
// up front took seconds for 99 runs, and most are never opened.
function renderPerRunTable(result) {
    const rows = result.runs.map((run, i) => {
        const failureNote = run.failed
            ? `<span class="failure">${t('runner.run.failed', { year: run.failureYear })}</span>`
            : '<span class="success">&mdash;</span>';
        return `
            <tr>
                <td><button type="button" class="run-toggle" data-run="${i}" aria-expanded="false">${i + 1} <span class="run-toggle-icon">&#9656;</span></button></td>
                <td>${formatCurrency(run.endingBalance)}</td>
                <td>${t('runner.run.lowest', { amount: formatCurrency(run.lowestBalanceValue), year: run.lowestBalanceYear })}</td>
                <td>${formatPercent(run.averageAnnualReturn)}</td>
                <td>${formatPercent(run.averageTaxRate)}</td>
                <td>${t('runner.run.yearRate', { year: run.highestReturnYear, rate: formatPercent(run.highestReturnValue) })}</td>
                <td>${t('runner.run.yearRate', { year: run.lowestReturnYear, rate: formatPercent(run.lowestReturnValue) })}</td>
                <td>${failureNote}</td>
            </tr>
            <tr class="run-detail-row" hidden>
                <td colspan="8"></td>
            </tr>
        `;
    }).join('');

    return `
        <table class="run-table">
            <thead>
                <tr>${['run', 'endingBalance', 'lowestBalance', 'avgReturn', 'avgTaxRate', 'highestReturn', 'lowestReturn', 'failureYear']
                    .map((key) => `<th>${t(`runner.table.${key}`)}</th>`).join('')}</tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `;
}

// Which accounts the engine drew from: the order it ran (with Automatic, the one it picked for these inputs) and
// the conversion-tax rule from the parameters the server ran with. The app chooses these, not the user.
function accountChoiceLine(parameters, output) {
    const order = t(`order.${output.withdrawalStrategy ?? parameters.withdrawalStrategy}`);
    const target = output.rothConversionTarget ?? 'None';
    const funding = target !== 'None' ? t('runner.funding', { how: t(`funding.${parameters.conversionTaxFunding}`) }) : '';
    const picked = parameters.withdrawalStrategy === 'Automatic' || parameters.rothConversionTarget === 'Automatic'
        ? t('runner.pickedBest')
        : t('runner.pickedApp');
    return `<p>${t('runner.accounts', { order, conversions: t(`target.phrase.${target}`), funding, picked })} (<a href="model-info.html" class="summary-link">${t('common.why')}</a>)</p>`;
}

// The annuity the run included: what it pays from when, any yearly increase, and how its payments are taxed
function annuityLine(parameters) {
    const annuity = parameters.annuity;
    if (!annuity) return '';
    return `<p>${t('runner.annuityLine', {
        amount: formatCurrency(annuity.monthlyPayment),
        start: I18n.monthYear(new Date(`${annuity.firstPayment}T00:00:00Z`)),
        cola: annuity.annualIncrease > 0 ? t('runner.annuityLine.cola', { rate: ratePercent(annuity.annualIncrease) }) : '',
        taxes: annuity.basis > 0
            ? t('runner.annuityLine.basis', { basis: formatCurrency(annuity.basis) })
            : t('runner.annuityLine.taxDeferred'),
    })}</p>`;
}

// The results summary: its first sentence (how many runs survived) always shows, with a caret that unfolds the details
// under it. Folded at first; once opened or closed it stays that way while the page is open, through new runs and
// language switches (which redraw it).
let summaryExpanded = false;

function summaryBox(kind, headline, details) {
    return `
        <div class="summary-box ${kind}">
            <details class="summary-details"${summaryExpanded ? ' open' : ''}>
                <summary><span class="summary-caret" aria-hidden="true"></span>${headline}</summary>
                ${details}
            </details>
        </div>
    `;
}

function renderSummary(parameters, output) {
    const result = output.result;
    const totalAvgRate = output.allRates.reduce((a, b) => a + b, 0) / output.allRates.length;
    const variance = output.allRates.reduce((a, b) => a + Math.pow(b - totalAvgRate, 2), 0) / output.allRates.length;
    const stdDev = Math.sqrt(variance);
    const avgLifetimeTax = result.runs.reduce((a, run) => a + run.lifetimeTaxesPaid, 0) / result.runs.length;
    // Not sent without the tax-detail feature
    const avgLifetimeIrmaa = result.runs.reduce((a, run) => a + (run.lifetimeIrmaaSurcharges ?? 0), 0) / result.runs.length;
    const irmaaLine = avgLifetimeIrmaa > 0.5 ? `<p>${t('runner.irmaaLine', { amount: formatCurrency(avgLifetimeIrmaa) })}</p>` : '';
    const lifetimeTaxLine = `<p>${t('runner.lifetimeTax', {
        amount: formatCurrency(avgLifetimeTax),
        through: result.outOfMoneyCount > 0 ? t('runner.lifetimeTax.through') : '',
    })}</p>${irmaaLine}`;
    const mix = parameters.assetMix;
    const scenarioLine = `<p>${t('runner.mixLine', {
        stocks: formatAllocation(mix.stockWeight * 100),
        bonds: formatAllocation(mix.bondWeight * 100),
        cash: formatAllocation(mix.cashWeight * 100),
        mean: formatPercent(parameters.mean),
        stdDev: formatPercent(parameters.stdDev),
    })}</p>${parameters.filingStatus === 'MarriedJoint' ? `<p>${t('runner.filingMarried')}</p>` : ''}${annuityLine(parameters)}`;

    if (result.outOfMoneyCount > 0) {
        const survival = 1 - (result.outOfMoneyCount / parameters.iterations);
        const avgFailureYear = result.yearsOutOfMoney.reduce((a, b) => a + b, 0) / result.yearsOutOfMoney.length;
        const avgFailureReturn = result.failedScenarioAverages.reduce((a, b) => a + b, 0) / result.failedScenarioAverages.length;
        return summaryBox(
            survival > 0.8 ? 'ok' : 'warn',
            `<strong>${survival > 0.8 ? '🙂' : '🙁'} ${t('runner.failedCount', { failed: result.outOfMoneyCount, total: parameters.iterations, years: parameters.years })}</strong> ${t('runner.survivalRate', { rate: formatPercent(survival) })}`,
            `${scenarioLine}
                <p>${t('runner.realized', { mean: formatPercent(totalAvgRate), stdDev: stdDev.toFixed(4) })}</p>
                <p>${t('runner.inheritance', { amount: formatCurrency(parameters.newMoney), year: new Date(parameters.retirementDate).getUTCFullYear() + parameters.yearNewMoney })}</p>
                <p>${t('runner.avgFailure', { year: avgFailureYear.toFixed(0), rate: formatPercent(avgFailureReturn) })}</p>
                ${lifetimeTaxLine}
                ${accountChoiceLine(parameters, output)}`);
    }

    const avgBalance = result.successMoneyRemaining.reduce((a, b) => a + b, 0) / result.successMoneyRemaining.length;
    return summaryBox(
        'ok',
        `<strong>🙂 ${t('runner.allSurvived')}</strong>`,
        `${scenarioLine}
            <p>${t('runner.avgBalance', { amount: formatCurrency(avgBalance) })}</p>
            ${lifetimeTaxLine}
            ${accountChoiceLine(parameters, output)}`);
}

// Remembers the summary's folded or unfolded state (toggle doesn't bubble, so it's caught on the way down)
function initSummaryToggle() {
    results.addEventListener('toggle', (e) => {
        if (e.target.classList?.contains('summary-details')) summaryExpanded = e.target.open;
    }, true);
}

// Every failed run's years, as plain text. Built here from the run data (rather than the server's own trace) so it reads
// in the page's language, with US dollar and percent formatting wherever the server runs.
function failureTrace(runs) {
    const percent = (value) => `${(value * 100).toFixed(2)}%`;
    return runs.filter((run) => run.failed).map((run) => run.years.map((y) => [
        t('runner.trace.year', { year: y.year }),
        t('runner.trace.rate', { rate: percent(y.rateOfReturn) }),
        t('runner.trace.withdrawal', {
            total: formatCurrency(y.withdrawal), taxable: formatCurrency(y.taxableWithdrawal),
            brokerage: formatCurrency(y.brokerageWithdrawal), roth: formatCurrency(y.rothWithdrawal),
        }),
        t('runner.trace.taxRate', { rate: percent(y.taxRate) }),
        t('runner.trace.balance', {
            total: formatCurrency(y.balance), taxable: formatCurrency(y.taxableBalance),
            brokerage: formatCurrency(y.brokerageBalance), roth: formatCurrency(y.rothBalance),
        }),
    ].join('\n')).join('\n\n')).join('\n\n\n');
}

function renderDetail(output, conversionsEnabled, taxDetail) {
    if (output.result.outOfMoneyCount > 0) {
        return `
            <details>
                <summary>${t('runner.detail.failed')}</summary>
                <pre>${escapeHtml(failureTrace(output.result.runs))}</pre>
            </details>
        `;
    }

    if (!output.lastSuccessfulRun) return '';

    return `
        <details>
            <summary>${t('runner.detail.lastSuccess')}</summary>
            ${renderRunDetailTable(output.lastSuccessfulRun.slice(1), conversionsEnabled, taxDetail)}
        </details>
    `;
}

// The results area from `view` - after each run, and whenever the language changes
function renderView() {
    if (!view) {
        results.innerHTML = '';
    } else if (view.kind === 'loading') {
        results.innerHTML = `<p class="loading">${t('runner.running')}</p>`;
    } else if (view.kind === 'errors') {
        results.innerHTML = errorList(view.errors);
    } else if (view.kind === 'failed') {
        results.innerHTML = `<div class="error-box"><p>${t('common.requestFailedWith', { message: escapeHtml(view.message) })}</p></div>`;
    } else {
        const { parameters, output, taxDetail } = view;
        results.innerHTML =
            renderSummary(parameters, output) +
            renderPerRunTable(output.result) +
            renderDetail(output, parameters.enableRothConversions, taxDetail);
        Paywall.decorate(results);
    }
}

function initFootnotes() {
    results.addEventListener('click', (e) => {
        const ref = e.target.closest('.footnote-ref');
        if (!ref) return;
        e.preventDefault();
        ref.parentElement.classList.toggle('open');
    });
}

// The Annuity tab's footnotes, like the year tables': hover or focus shows a note, a tap toggles it, and a note that would
// run past the window's right edge moves left to fit. Cancelling the click also stops a footnote inside a label from
// ticking its checkbox or radio.
function initFormFootnotes() {
    form.addEventListener('click', (e) => {
        const ref = e.target.closest('.footnote-ref');
        if (!ref) return;
        e.preventDefault();
        const note = ref.parentElement;
        const open = !note.classList.contains('open');
        note.classList.add('open');
        placeFootnoteTip(note);
        note.classList.toggle('open', open);
    });
    ['mouseover', 'focusin'].forEach((type) => form.addEventListener(type, (e) => {
        const note = e.target.closest?.('.footnote');
        if (note) placeFootnoteTip(note);
    }));
}

// Measured while the tip shows (hover, focus or open): a tip that would run past the window's right edge moves left just
// enough to fit, but never past the left edge
function placeFootnoteTip(note) {
    const tip = note.querySelector('.footnote-tip');
    if (!tip) return;
    tip.style.left = '';
    const box = tip.getBoundingClientRect();
    const overflow = box.right - (document.documentElement.clientWidth - 4);
    if (overflow > 0) tip.style.left = `${-Math.min(overflow, box.left - 4)}px`;
}

function initRunToggles() {
    results.addEventListener('click', (e) => {
        const button = e.target.closest('.run-toggle');
        if (!button) return;

        const detailRow = button.closest('tr').nextElementSibling;
        const expanded = button.getAttribute('aria-expanded') === 'true';

        // The run's year table, built on its first expand since the results were drawn (renderView starts over,
        // collapsed, after a language or tier change, so a table built later always reads the current ones)
        if (!expanded && !detailRow.dataset.built) {
            const run = view.output.result.runs[Number(button.dataset.run)];
            const cell = detailRow.firstElementChild;
            cell.innerHTML = renderRunDetailTable(run.years, view.parameters.enableRothConversions, view.taxDetail);
            Paywall.decorate(cell);
            detailRow.dataset.built = 'true';
        }

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
    const quota = window.QuotaNotice.create({
        storageKey: 'quota:change-request',
        notice: document.getElementById('edit-quota'),
        submitButton,
        blockedText: (limit, clock, minutes) => t('quota.changeRequests.blocked', {
            limit, clock, relative: minutes < 1 ? t('quota.relative.soon') : t('quota.relative.minutes', { minutes }),
        }),
        remainingText: (remaining, limit) => t('quota.changeRequests.remaining', { remaining, limit }),
    });

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
        quota.restore();
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

    // A countdown on screen re-words itself in the new language
    document.addEventListener('i18n:change', () => quota.restore());

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
        const verdict = data.serverCorrected ? t('flyout.verdict.corrected') : t('flyout.verdict.passed');
        const link = `<a href="${escapeHtml(data.issueUrl)}" target="_blank" rel="noopener">${escapeHtml(data.issueKey)}</a>`;

        editResult.innerHTML = `
            <div class="summary-box ok">
                <p>${t('flyout.created', { link })}</p>
            </div>
            <details class="agent-trace">
                <summary>${t('flyout.whatAgentDid')}</summary>
                <p>${t('flyout.forcedTool')}</p>
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

        // Still refused: say until when, without spending a request
        if (quota.isBlocked()) {
            quota.restore();
            return;
        }

        submitButton.disabled = true;
        editResult.innerHTML = `<p class="loading">${t('flyout.asking')}</p>`;

        try {
            const response = await fetch('/api/change-request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            const data = await response.json().catch(() => ({}));
            // Every response says how many requests are left; a 429 says when the next one is allowed
            quota.update(response, data);
            if (response.status === 429) {
                editResult.innerHTML = '';
                return;
            }

            if (!response.ok) {
                editResult.innerHTML = data.errors
                    ? errorList(data.errors)
                    : `<div class="error-box"><p>${escapeHtml(I18n.translateServerMessage(data.message || 'The request failed.'))}</p></div>`;
                return;
            }

            renderAgentResult(data);
        } catch (err) {
            editResult.innerHTML = `<div class="error-box"><p>${t('common.requestFailedWith', { message: escapeHtml(err.message) })}</p></div>`;
        } finally {
            submitButton.disabled = quota.isBlocked();
        }
    });
}

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    // A Free visitor's locked inputs hold their Free values once /api/me has answered (paywall.js)
    await Paywall.ready;

    const formData = new FormData(form);
    const payload = {
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
        filingStatus: formData.get('filingStatus'),
        enableRothConversions: form.elements['enableRothConversions'].checked,
        ...annuityRequest(),
        ...Inputs.readAssetMix(form)
    };

    view = { kind: 'loading' };
    renderView();

    try {
        const response = await fetch('/api/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const problem = await response.json();
            view = { kind: 'errors', errors: problem.errors || {} };
        } else {
            const data = await response.json();
            // taxDetail is false when the server left out the year-by-year tax detail (FreeRunView)
            view = { kind: 'results', parameters: data.parameters, output: data.output, taxDetail: data.taxDetail !== false, taxDetailYears: data.taxDetailYears ?? 0 };
            chartData = { points: data.output.balanceBands, runs: data.output.result.runs.length };
        }
    } catch (err) {
        view = { kind: 'failed', message: err.message };
    }
    renderView();
    renderChart();
});

// The chart beside the inputs: the latest run's balances (90th percentile, median, 10th percentile), or until the
// first run an example - 30 years from this January, $1M growing to $10M / $3M / staying at $1M. A run that fails
// validation or the request keeps the last chart.
const chartBox = document.getElementById('balance-chart');
let chartData = null;

function exampleBands() {
    const year = new Date().getFullYear();
    return Array.from({ length: 31 }, (_, i) => ({
        date: `${year + i}-01-01`,
        upper: 1e6 * 10 ** (i / 30),
        middle: 1e6 * 3 ** (i / 30),
        lower: 1e6,
    }));
}

function renderChart() {
    const points = chartData ? chartData.points : exampleBands();
    const end = points[points.length - 1];
    document.getElementById('chart-note').textContent = chartData
        ? t('runner.chart.actual', { runs: chartData.runs })
        : t('runner.chart.example');
    BalanceChart.render(chartBox, points, {
        ariaLabel: t('runner.chart.aria', {
            year: end.date.slice(0, 4),
            upper: formatCurrency(end.upper),
            middle: formatCurrency(end.middle),
            lower: formatCurrency(end.lower),
        }),
        names: { upper: t('runner.chart.tipUpper'), middle: t('runner.chart.tipMiddle'), lower: t('runner.chart.tipLower') },
        formatDate: (iso) => I18n.monthYear(new Date(`${iso}T00:00:00Z`)),
        formatMoney: formatCurrency,
    });
}

function initChart() {
    // Drawn once the page's text has loaded; redrawn when the inputs section reopens, since it has no width while closed
    I18n.ready.then(renderChart);
    document.getElementById('inputs-section').addEventListener('toggle', (e) => { if (e.target.open) renderChart(); });
}

// "Run in Scenario runner" on the Optimal page: it leaves its recommendation and the inputs behind it (the asset mix
// included) in sessionStorage. Read it once (so a reload doesn't re-run), fill the form, explain where the values came from, and run.
const SIMULATOR_HANDOFF_KEY = 'simulatorHandoff';

function readSimulatorHandoff() {
    try {
        const raw = sessionStorage.getItem(SIMULATOR_HANDOFF_KEY);
        sessionStorage.removeItem(SIMULATOR_HANDOFF_KEY);
        const handoff = raw ? JSON.parse(raw) : null;
        return handoff && handoff.version === 2 ? handoff : null;
    } catch {
        return null;
    }
}

function applySimulatorHandoff() {
    const h = readSimulatorHandoff();
    if (!h) return;

    const set = (name, value) => { form.elements[name].value = value; };
    const setMoney = (name, value) => set(name, formatWithCommas(String(value)));

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
    // The status first: it moves the deduction to its default, which the hand-off's own amount then replaces
    Inputs.setFilingStatus(form, h.filingStatus ?? 'single');
    setMoney('annualStandardDeduction', h.annualStandardDeduction);
    ['initialTaxableBalance', 'initialRothBasis', 'initialRothUnrealizedGain', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain', 'newMoney']
        .forEach((name) => setMoney(name, h[name]));
    set('yearNewMoney', h.yearNewMoney);
    form.elements['enableRothConversions'].checked = h.enableRothConversions;
    applyHandoffAnnuity(h.annuity);
    Inputs.setAssetMix(form, h.assetMix);
    updateBalanceTotals();
    Inputs.refreshAccountTotals(form);

    handoff = h;
    renderHandoffNote();
    form.requestSubmit();
    document.getElementById('handoff-note').scrollIntoView({ block: 'start' });
}

// The annuity the Optimizer decided to buy (with Tax Deferred money: the hand-off's Tax Deferred balance is already
// what's left), as this page's own: a SPIA from the Retire On date in a tax-deferred account, no COLA. Without one, the
// Annuity tab is left off.
function applyHandoffAnnuity(annuity) {
    const el = form.elements;
    el['includeAnnuity'].checked = !!annuity;
    if (annuity) {
        el['annuityMonthlyAmount'].value = formatWithCommas(String(annuity.monthlyPayment));
        el['annuityInvestment'].value = formatWithCommas(String(annuity.investment));
        el['annuityType'].value = 'spia';
        el['annuityTaxDeferred'].checked = true;
        el['annuityCola'].checked = false;
    }
    // The tab redraws from all its fields (the DIA date, the COLA rate, the suggested rate) on this one change
    el['includeAnnuity'].dispatchEvent(new Event('change'));
}

function renderHandoffNote() {
    if (!handoff) return;
    document.getElementById('handoff-note')?.remove();
    results.insertAdjacentHTML('beforebegin', `
        <div id="handoff-note" class="handoff-note">
            ${t('runner.handoff', {
                age: handoff.recommendedAge,
                amount: formatCurrency(handoff.withdrawalMonthly),
                iterations: handoff.iterations,
            })}
        </div>`);
}

// Access changed in another tab (paywall.js): the results' notes about the tax detail follow
document.addEventListener('paywall:change', renderView);

// Switching language redraws everything the page shows; nothing re-runs
document.addEventListener('i18n:change', () => {
    updateBalanceTotals();
    updateAllocation();
    renderHandoffNote();
    renderView();
    renderChart();
});

I18n.ready.then(applySimulatorHandoff);
Inputs.initTabs(form, 'runnerTab');
Inputs.initAllocation(form);
initChart();
Inputs.initMoneyInputs();
Inputs.initBalanceTotals(form);
Inputs.initAccountTotals(form);
Inputs.initFilingStatus(form);
Inputs.sizeInputs(form);
initSocialSecurityDefault();
initAnnuity();
initAnnuityRate();
Inputs.initCollapsibleInputs(form);
initRunToggles();
initFootnotes();
initFormFootnotes();
initSummaryToggle();
initEditFlyout();
