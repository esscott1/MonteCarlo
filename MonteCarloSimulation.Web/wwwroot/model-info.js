// The Model Info page: renders the Strategy Lab's published results (model-info/strategy-lab.json, written by
// `dotnet run --project MonteCarloSimulation.StrategyLab -- --publish`). Numbers come from the JSON; the
// explanations around them are written here. Self-contained on purpose, like optimal.js.
const container = document.getElementById('model-info');

// ---- formatting

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function money(value) {
    return Number(value).toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

// Annual spend shown per month, like the Optimal page
function perMonth(annual) {
    return money(annual / 12);
}

// A fraction as a signed percentage: 0.0123 -> "+1.23%"
function pct(value, digits = 1) {
    const v = Number(value) * 100;
    return (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toFixed(digits) + '%';
}

// A share as an unsigned percentage: 0.31 -> "31%"
function share(value, digits = 0) {
    return (Number(value) * 100).toFixed(digits) + '%';
}

// Differences within the lab's tie band (0.5%) are search noise
function tone(value) {
    if (value > 0.005) return 'gain';
    if (value < -0.005) return 'loss';
    return '';
}

function code(text) {
    return `<span class="strategy-code">${escapeHtml(text)}</span>`;
}

function table(head, rows, className = '') {
    return `<div class="table-scroll"><table class="run-table ${className}">
        <thead><tr>${head.map((h) => `<th>${h}</th>`).join('')}</tr></thead>
        <tbody>${rows.join('')}</tbody>
    </table></div>`;
}

function section(id, title, body) {
    return `<section class="info-section" id="${id}"><h2>${title}</h2>${body}</section>`;
}

// ---- sections

// The app's per-household order pick, from the order-choice measurement
function appOrderPick(s) {
    return (s.orderChoice ?? []).find((o) => o.name.endsWith('(app)'));
}

function headline(s, byCode) {
    const heavy = s.subsets[3];
    const sy = s.singleYear;
    const pick = appOrderPick(s);
    const app = appFunding(s);
    const fundingGain = s.funding && app ? s.funding.vsBaseline.find((c) => c.code === `W2+C1+${app.code}`) : null;
    return `
        <p class="info-lede">The app makes two choices about where the money comes from on its own, each measured here on
        ${s.config.scenarios.toLocaleString('en-US')} simulated households: Roth conversion tax ${app ? escapeHtml(app.name.replace(' (app)', '').toLowerCase()) : ''},
        and each household draws in whichever of Pro-rata or Tax-optimized sustains more spending. Against every combination of six withdrawal
        orders and five conversion policies, the median household can't do more than ${share(s.regret.median, 2)} better. The biggest remaining
        gaps come from converting more than the app does, for early retirees with most of their money in Tax Deferred accounts.</p>
        <div class="stat-grid">
            <div class="stat-tile key"><b>${sy.withinFiveDollars} / ${sy.checked}</b><span>single years where Tax-optimized paid exactly the brute-force minimum tax</span></div>
            ${fundingGain ? `<div class="stat-tile"><b>${pct(fundingGain.meanSpendDiff, 2)}</b><span>average gain in sustainable spend from paying conversion tax out of the conversion instead of only from Brokerage</span></div>` : ''}
            ${pick ? `<div class="stat-tile"><b>${share(pick.shareWithinTieBand, 1)}</b><span>of households where the app's per-household order pick is within 0.5% of the best of all six orders</span></div>` : ''}
            <div class="stat-tile"><b>${pct(heavy.medianGap, 2)}</b><span>median gap to the best combination for early retirees with 75%+ Tax Deferred (${heavy.scenarios} households)</span></div>
        </div>`;
}

function whatWasTested(s) {
    return `
        <p>Each household got a random but reproducible profile: retiring at 50&ndash;70 between 2026 and 2032 for 20&ndash;40 years,
        $300k&ndash;$5M split across Tax Deferred, Roth and Brokerage (0&ndash;90% embedded gains), Social Security of $0&ndash;$4,500 a month
        starting at 62&ndash;70, an inheritance half the time, and one of the four investment scenarios.</p>
        <p>Every combination of a withdrawal order and a Roth conversion policy (defined below) ran on the <em>same</em> ${s.config.paths}
        market paths for each household, so every difference on this page is paired: same household, same markets, only the strategy changed.
        For each path the lab found the largest annual spend it survives (to $100); the <strong>82.5% spend</strong> &mdash; the most spending that
        survives 82.5% of paths &mdash; is read off those break-evens exactly as the Optimal page does, and it's the yardstick throughout.
        The lab runs the app's own year-by-year engine, and a self-check confirms it survives exactly when the app does.</p>`;
}

// The funding rule the app uses, marked "(app)" by the lab
function appFunding(s) {
    return s.definitions.find((d) => d.kind === 'funding' && d.name.endsWith('(app)'));
}

function definitions(s) {
    const orders = s.definitions.filter((d) => d.kind === 'order');
    const policies = s.definitions.filter((d) => d.kind === 'conversion');
    const fundings = s.definitions.filter((d) => d.kind === 'funding');
    const app = appFunding(s);

    const orderRows = orders.map((d) => `<tr><td>${code(d.code)}</td><td>${escapeHtml(d.name)}</td><td>${escapeHtml(d.definition)}</td></tr>`);
    const policyRows = policies.map((d) => {
        let line = '&mdash;';
        if (d.line2026 != null) {
            line = money(d.line2026);
            if (d.zeroRateAlternative != null) line += `, or ${money(d.zeroRateAlternative)} minus the gains already realized that year, if lower`;
        }
        return `<tr><td>${code(d.code)}</td><td>${escapeHtml(d.definition)}</td><td class="num">${line}</td></tr>`;
    });

    return `
        <h3>Withdrawal orders</h3>
        <p>Which accounts pay for each year's spending, and in what order.</p>
        ${table(['Code', 'Name', 'Rule'], orderRows)}
        <h3>Roth conversion policies</h3>
        <p>After each year's spending, Tax Deferred money is converted to Roth until the year's <em>gross ordinary income</em> reaches the policy's line.</p>
        ${table(['Code', 'Converts until gross ordinary income reaches', '2026 line (single filer, $16,000 deduction)'], policyRows)}
        <ul class="info-notes">
            <li>Gross ordinary income includes taxable Social Security and the year's Tax Deferred withdrawals. The lines are inflated each year like the tax brackets, and a year already past its line converts nothing.</li>
            <li>Conversions ignore the 59&frac12; gate, and converted dollars count as Roth basis, which can be spent at any age.</li>
            <li>Big conversions raise Medicare premiums later. From 65, the model charges the Medicare IRMAA surcharge set by income two
            years earlier (2026 single-filer tiers, starting above $109,000 of MAGI, inflated each year) on top of spending. The first two
            years of retirement pay none, as after a work-stoppage appeal.</li>
            <li>Where the conversion's tax comes from is set by the funding rule below${app ? ` &mdash; the app uses ${code(app.code)}` : ''}.</li>
        </ul>
        ${fundings.length === 0 ? '' : `
        <h3>Conversion tax funding</h3>
        <p>Where a Roth conversion's tax comes from. Paying out of the conversion is the same, dollar for dollar, as withholding from Tax Deferred
        or paying from Roth afterwards, so those aren't separate rules.</p>
        ${table(['Code', 'Name', 'Rule'], fundings.map((d) => `<tr><td>${code(d.code)}</td><td>${escapeHtml(d.name)}</td><td>${escapeHtml(d.definition)}</td></tr>`))}`}`;
}

// Where conversion tax comes from: every funding rule against the app before the change (W2+C1 paying
// conversion tax only from Brokerage), on the same households and markets.
function funding(s) {
    const f = s.funding;
    const byCode = Object.fromEntries(f.vsBaseline.map((c) => [c.code, c]));
    const rules = s.definitions.filter((d) => d.kind === 'funding');
    const app = appFunding(s);
    const rows = [['W2', 'Tax-optimized'], ['W1', 'Pro-rata']].flatMap(([order, orderName]) => rules.map((r) => {
        const c = byCode[`${order}+C1+${r.code}`];
        if (!c) return '';
        const isBaseline = c.code === f.baseline;
        return `<tr class="${app && r.code === app.code && order === 'W2' ? 'recommended-row' : ''}">
            <td>${code(c.code)}</td><td>${orderName}, ${escapeHtml(r.name)}</td>
            <td class="num ${tone(c.meanSpendDiff)}">${isBaseline ? 'baseline' : pct(c.meanSpendDiff, 2)}</td>
            <td class="num">${isBaseline ? '' : `${pct(c.meanCi[0], 2)} to ${pct(c.meanCi[1], 2)}`}</td>
            <td class="num">${isBaseline ? '' : pct(c.medianSpendDiff, 2)}</td>
            <td class="num">${isBaseline ? '' : share(c.winRate)}</td>
            <td class="num">${isBaseline ? '' : share(c.lossRate)}</td></tr>`;
    }));
    const early = f.slices.find((sl) => sl.dimension.startsWith('Retires before'));
    const earlyRows = early ? early.bins.map((b) => `<tr><td>${escapeHtml(b.label)}</td><td class="num">${b.scenarios}</td>
        ${rules.filter((r) => r.code !== 'F0').map((r) => {
            const k = `W2+C1+${r.code}`;
            return `<td class="num ${tone(b.meanDiff[k])}">${pct(b.meanDiff[k], 2)} <span class="info-meta">(median ${pct(b.medianDiff[k], 2)})</span></td>`;
        }).join('')}</tr>`) : [];
    return `
        <p>The Strategy Lab found that paying a conversion's tax only by selling Brokerage stalls conversions once Brokerage runs low, which hurts
        early retirees most. Every funding rule was run on the same ${f.scenariosCompared.toLocaleString('en-US')} households and markets,
        with the app's conversion policy (${code('C1')}), compared with Tax-optimized paying only from Brokerage (${code(f.baseline)}, the app before this change).
        The rule for adopting one: the highest average gain with its 95% interval above zero, a median of at least zero, and losses (worse than &minus;0.5%)
        in at most 5% of households.</p>
        ${table(['Combination', 'Order and funding', 'Average', '95% interval', 'Median', 'Wins', 'Loses'], rows)}
        ${early ? `<h3>Tax-optimized with each rule, by retirement age</h3>
        ${table(['Retires before 59½', 'Households', ...rules.filter((r) => r.code !== 'F0').map((r) => `${code(`W2+C1+${r.code}`)}`)], earlyRows)}` : ''}
        ${app ? `<p>The app now uses ${code(app.code)}: ${escapeHtml(app.definition)}</p>` : ''}`;
}

// How the app picks the withdrawal order: each order under the app's conversion policy against the best order
// for the same household.
function orderChoice(s) {
    const rows = s.orderChoice.map((o) => `<tr class="${o.qualifies ? 'recommended-row' : ''}">
        <td>${code(o.code)} ${escapeHtml(o.name)}</td>
        <td class="num">${share(o.shareWithinTieBand, 1)}</td>
        <td class="num">${pct(o.meanShortfall, 2)}</td>
        <td>${escapeHtml(o.worstSlice)}</td>
        <td class="num ${o.worstSliceMeanShortfall < -0.01 ? 'loss' : ''}">${pct(o.worstSliceMeanShortfall, 2)}</td>
        <td>${o.qualifies ? 'Yes' : 'No'}</td></tr>`);
    const chosen = s.orderChoice.filter((o) => o.qualifies);
    return `
        <p>Which accounts pay for each year's spending is the app's choice, not an input. Each withdrawal order was run with the app's
        conversion policy and conversion tax funding, and compared with the best order for the same household. An order can be the app's
        one fixed rule if it's within 0.5% of the best in at least 95% of households and no household type falls short by more than 1% on average.</p>
        ${table(['Order', 'Within 0.5% of the best', 'Average shortfall', 'Household type where it falls shortest', 'Shortfall there', 'Qualifies'], rows)}
        <p>${chosen.length > 0
            ? `The app uses ${chosen.map((o) => `${code(o.code)} ${escapeHtml(o.name)}`).join(' / ')}.`
            : 'No single order qualifies, so the app picks the order for each household.'}</p>`;
}

// The Optimal page's default inputs before and after the app chose its own accounts
function optimalDefaults(s) {
    const rows = s.optimalDefaults.map((r) => {
        const b = r.before;
        const a = r.after;
        const change = (x, y) => `<span class="info-meta">(${pct(y / x - 1, 1)})</span>`;
        return `<tr><td>${r.scenarioId}. ${escapeHtml(r.description)}</td>
            <td class="num">${b.recommendedAge} &rarr; ${a.recommendedAge}</td>
            <td class="num">${perMonth(b.spendAt85)} &rarr; ${perMonth(a.spendAt85)} ${change(b.spendAt85, a.spendAt85)}</td>
            <td class="num">${perMonth(b.spendAtMidpoint)} &rarr; ${perMonth(a.spendAtMidpoint)} ${change(b.spendAtMidpoint, a.spendAtMidpoint)}</td>
            <td class="num">${perMonth(b.spendAt80)} &rarr; ${perMonth(a.spendAt80)} ${change(b.spendAt80, a.spendAt80)}</td>
            <td class="num">${share(b.survivalRate, 1)} &rarr; ${share(a.survivalRate, 1)}</td></tr>`;
    });
    return `
        <p>The Optimal page's default inputs, run through its optimizer with the behavior before this change (Tax-optimized, conversion tax
        only from Brokerage) and with the app's current choices. Monthly spend in today's dollars at the recommended Social Security age.</p>
        ${table(['Investment scenario', 'Recommended age', '85% survival', '82.5% survival', '80% survival', 'Survival at 82.5% spend'], rows)}`;
}

function grid(s, byCode) {
    const orders = s.definitions.filter((d) => d.kind === 'order' && d.code !== 'W2nh');
    const policies = s.definitions.filter((d) => d.kind === 'conversion');
    const rows = orders.map((o) => {
        const cells = policies.map((p) => {
            const c = byCode[`${o.code}+${p.code}`];
            if (c.code === s.baseline) {
                return '<td class="grid-cell baseline"><span class="grid-main">Baseline</span><span class="grid-sub">today\'s default</span></td>';
            }
            return `<td class="grid-cell ${tone(c.meanSpendDiff)}"><span class="grid-main">${pct(c.meanSpendDiff, 2)}</span>
                <span class="grid-sub">median ${pct(c.medianSpendDiff, 2)} &middot; wins ${share(c.winRate)} &middot; loses ${share(c.lossRate)}</span></td>`;
        });
        return `<tr><th scope="row">${code(o.code)} ${escapeHtml(o.name)}</th>${cells.join('')}</tr>`;
    });
    const nh = byCode['W2nh+C1'];
    return `
        <p>Average change in the 82.5% spend versus the Tax-optimized baseline. Wins and losses count households where the difference is more than
        &plusmn;0.5% (smaller gaps are within the $100 search precision). A few early retirees pull the averages up; the median is the typical household.</p>
        ${table(['Withdrawal order', ...policies.map((p) => `${code(p.code)} ${escapeHtml(p.name)}`)], rows, 'strategy-grid')}
        <p class="info-meta">Plus Tax-optimized without 0% gain harvesting (${code('W2nh+C1')}): ${pct(nh.meanSpendDiff, 2)} average,
        median ${pct(nh.medianSpendDiff, 2)}, wins ${share(nh.winRate)}, loses ${share(nh.lossRate)}.</p>`;
}

function directQuestion(s) {
    const rows = s.directQuestion.map((c) => `<tr>
        <td>${escapeHtml(c.label)}</td>
        <td class="num ${tone(c.meanSpendDiff)}">${pct(c.meanSpendDiff, 2)}</td>
        <td class="num">${pct(c.meanCi[0], 2)} to ${pct(c.meanCi[1], 2)}</td>
        <td class="num">${pct(c.medianSpendDiff, 2)}</td>
        <td class="num">${share(c.winRate)}</td>
        <td class="num">${share(c.lossRate)}</td>
        <td class="num">${pct(c.p5SpendDiff, 1)} / ${pct(c.p95SpendDiff, 1)}</td></tr>`);
    return `
        <p>For most households the two orders are close once Roth conversions are on; the median household sees a difference of a few hundredths of a percent.</p>
        ${table(['Comparison (first vs second)', 'Average', '95% interval', 'Median', 'Wins', 'Loses', '5th / 95th pct'], rows)}
        <p>Without conversions, Pro-rata beats Tax-optimized in about half of households by a small margin: spreading Tax Deferred draws
        evenly over the whole retirement keeps more of them in low brackets than a strict order. With the app's conversions on, that
        advantage mostly disappears, because conversions fill the cheap brackets every year under either order.</p>`;
}

function whoLoses(s) {
    const rows = s.subsets.map((d) => `<tr>
        <td>${escapeHtml(d.label)}</td><td class="num">${d.scenarios}</td>
        <td class="num">${pct(d.medianGap, 2)}</td><td class="num">${pct(d.meanGap, 2)}</td><td class="num">${pct(d.p90Gap, 1)}</td>
        <td class="num">${share(d.shareOver1Pct)}</td><td class="num">${share(d.shareOver3Pct)}</td>
        <td class="num ${tone(d.proRataMedianDiff)}">${pct(d.proRataMedianDiff, 2)}</td></tr>`);
    const over3 = Object.entries(s.regret.bestCombinationCountsOver3Pct);
    const over3Total = over3.reduce((sum, [, n]) => sum + n, 0);
    const winners = over3.map(([c, n]) => `${code(c)} ${n}`).join(', ');
    return `
        <p>The gap between the Tax-optimized baseline and the best of all combinations, by household type. The last column is Pro-rata + app conversions versus the Tax-optimized baseline.</p>
        ${table(['Households', 'Count', 'Median gap', 'Average gap', '90th pct gap', 'Gap over 1%', 'Gap over 3%', 'Pro-rata + conv. (median)'], rows)}
        <p class="info-meta">In the ${over3Total} households where some combination beat the Tax-optimized baseline by more than 3%, the winners were: ${winners}.</p>`;
}

function caseStudy(s) {
    const cs = s.caseStudy;
    const intro = `
        <p>Before 59&frac12; the model locks Tax Deferred money and Roth gains; only Brokerage and Roth basis can be spent. Roth conversions
        are the way through: converted dollars count as Roth basis, so they can be spent right away.</p>
        <p class="info-callout">Tax-optimized is gains-first: it sells Brokerage while the gains are taxed at 0%. Those gains fill the 0% band,
        and the app's conversion policy (${code('C1')}) stops conversions short rather than push them to 15%. Converting less means less new
        Roth basis, so when Brokerage runs out before 59&frac12; there isn't enough accessible money &mdash; even with Tax Deferred still full.
        Pro-rata spends some Roth basis alongside Brokerage, realizes fewer gains and keeps converting. This is why the app picks the order
        for each household rather than always using one.</p>`;
    if (!cs) return intro;

    const byYear = cs.baseline.map((b, i) => [b, cs.alternative[i]]);
    const rows = byYear.flatMap(([b, a]) => [[b, cs.baselineCombination, 'Tax-optimized'], [a, cs.alternativeCombination, 'Pro-rata']].map(([y, c, name], k) => {
        const last = y === cs.baseline[cs.baseline.length - 1];
        const result = k === 0 && last
            ? '<span class="loss">Runs out of accessible money</span>'
            : (k === 1 && b === cs.baseline[cs.baseline.length - 1] ? '<span class="gain">Still going</span>' : '');
        return `<tr class="${k === 0 ? 'year-first' : ''}">
            <td>${k === 0 ? `${y.calendarYear}, age ${y.age.toFixed(1)}` : ''}</td>
            <td>${code(c)} ${name}</td>
            <td class="num">${money(y.brokerageSold)}</td><td class="num">${money(y.rothSpent)}</td><td class="num">${money(y.taxDeferredWithdrawal)}</td>
            <td class="num">${money(y.converted)}</td><td class="num">${money(y.taxes)}</td><td class="num">${money(y.rothBalance)}</td>
            <td>${result}</td></tr>`;
    }));

    return `${intro}
        <p>The household where Pro-rata + app conversions gains the most (${escapeHtml(cs.scenario)}): its 82.5% spend is
        ${perMonth(cs.baselineSpend825)}/month under the Tax-optimized baseline and ${perMonth(cs.alternativeSpend825)}/month under Pro-rata.
        Below is market path ${cs.path} at the baseline's spend (${perMonth(cs.spend)}/month), the first path where the baseline fails and Pro-rata doesn't:</p>
        ${table(['Year', 'Order', 'Brokerage sold', 'Roth spent', 'Tax Deferred', 'Converted', 'Taxes', 'Roth at year end', ''], rows, 'case-study')}
        <h3>How much of this is real</h3>
        <p>The model is generous to this strategy in two ways, so treat the size of the early-retiree gap as an upper bound:</p>
        <ul class="info-notes">
            <li>Converted dollars can be spent immediately. In reality each conversion must season for five years before it can be withdrawn
            penalty-free before 59&frac12;, so a conversion ladder has to start five years earlier.</li>
            <li>There are no other early-access routes (Rule of 55, 72(t) payments).</li>
        </ul>`;
}

function slices(s) {
    const shown = [['W1+C1', 'Pro-rata + app conversions'], ['W1+C0', 'Pro-rata, no conversions'], ['W2+C0', 'Tax-optimized, no conversions'], ['W2+C3', 'Tax-optimized, top of 22%']];
    const body = s.slices.map((sl) => `
        <tr class="slice-group"><th colspan="${2 + shown.length}">${escapeHtml(sl.dimension)}</th></tr>
        ${sl.bins.map((b) => `<tr><td>${escapeHtml(b.label)}</td><td class="num">${b.scenarios}</td>
            ${shown.map(([c]) => `<td class="num ${tone(b.medianDiff[c])}">${pct(b.medianDiff[c], 2)} <span class="info-meta">(${pct(b.meanDiff[c], 1)})</span></td>`).join('')}</tr>`).join('')}`);
    return `
        <p>Median change in the 82.5% spend versus the Tax-optimized baseline, with the average in brackets.</p>
        ${table(['Slice', 'Households', ...shown.map(([c, n]) => `${code(c)}<br>${n}`)], body)}`;
}

function largestGaps(s) {
    const ages = s.topRegret.map((t) => t.retirementAge);
    const shares = s.topRegret.map((t) => t.taxDeferredShare);
    const rows = s.topRegret.map((t) => `<tr>
        <td class="household">${escapeHtml(t.scenario)}</td><td class="num">${perMonth(t.baselineSpend825)}</td>
        <td>${code(t.bestCombination)}</td><td class="num">${perMonth(t.bestSpend825)}</td><td class="num gain">${pct(t.regret, 0)}</td></tr>`);
    return `
        <p>Monthly 82.5% spend in today's dollars. All ten retire at ${Math.floor(Math.min(...ages))}&ndash;${Math.ceil(Math.max(...ages))}
        with ${share(Math.min(...shares))}&ndash;${share(Math.max(...shares))} of their money in Tax Deferred.</p>
        ${table(['Household', 'Tax-optimized baseline', 'Best', 'Best spend', 'Gap'], rows)}`;
}

function singleYear(s) {
    const sy = s.singleYear;
    return `
        <p>${sy.cases.toLocaleString('en-US')} random single years (spending need, Social Security, balances, gain fraction, age gate, inflation).
        For each, the Tax-optimized plan's tax was compared with a brute-force search over every Tax Deferred / Brokerage split that
        nets the same spending, holding Roth at what the plan used.</p>
        <ul class="info-notes">
            <li><strong>${sy.withinFiveDollars} of ${sy.checked}</strong> checkable years: the plan paid the minimum possible tax
            (largest difference ${money(sy.maxGap)}). The other ${sy.shortfalls} were years where the accessible money couldn't cover the need.</li>
            <li>The plan used Roth while Tax Deferred or Brokerage still had money in ${sy.rothBeforeOthers} cases.</li>
        </ul>
        <p>So within a year the ordering is exactly right; the gaps above are about the years ahead &mdash; keeping Brokerage available for conversions.</p>`;
}

function pageDefaults(s) {
    const rows = s.pageDefault.map((p) => {
        const byCombination = Object.fromEntries(p.rows.map((r) => [r.combination, r]));
        const base = byCombination[s.baseline].spend825;
        const best = p.rows.reduce((a, b) => (b.spend825 > a.spend825 ? b : a));
        const cell = (c) => `<td class="num">${perMonth(byCombination[c].spend825)} <span class="info-meta">(${pct(byCombination[c].spend825 / base - 1, 1)})</span></td>`;
        return `<tr><td>${p.investmentScenarioId}. ${escapeHtml(p.description)}</td><td class="num">${perMonth(base)}</td>
            ${cell('W1+C1')}${cell('W2+C0')}${cell('W1+C0')}<td>${code(best.combination)} ${pct(best.spend825 / base - 1, 2)}</td></tr>`;
    });
    return `
        <p>Retire 1/1/2027 at 57, $950k Tax Deferred, $20k Roth, $400k Brokerage (half gains), a $1M inheritance in year 10, and $2,750 a month
        from 62. Monthly 82.5% spend in today's dollars, with the change versus the Tax-optimized baseline.</p>
        ${table(['Investment scenario', 'Tax-optimized + conv.', 'Pro-rata + conv.', 'Tax-optimized, no conv.', 'Pro-rata, no conv.', 'Best of all'], rows)}`;
}

function conclusions(s, byCode) {
    const harvesting = s.directQuestion.find((c) => c.code.startsWith(`${s.baseline} vs W2nh`));
    const noConversions = byCode['W2+C0'];
    const pick = appOrderPick(s);
    const app = appFunding(s);
    const fundingGain = s.funding && app ? s.funding.vsBaseline.find((c) => c.code === `W2+C1+${app.code}`) : null;
    const top22 = byCode['W2+C3'];
    const heavy = s.slices.find((sl) => sl.dimension.startsWith('Tax Deferred share'))?.bins.find((b) => b.label === '75-100%');
    return `<ul class="info-notes">
        <li><strong>Tax-optimized works as intended within a year.</strong> Its single-year decisions are exactly tax-minimal.</li>
        ${fundingGain ? `<li><strong>Conversion tax comes out of the conversion.</strong> Compared with paying it only by selling Brokerage, this raises the
        82.5% spend by ${pct(fundingGain.meanSpendDiff, 2)} on average (95% interval ${pct(fundingGain.meanCi[0], 2)} to ${pct(fundingGain.meanCi[1], 2)}),
        mostly for early retirees whose conversions used to stall; it lowers it by more than 0.5% for ${share(fundingGain.lossRate, 1)} of households.</li>` : ''}
        ${pick ? `<li><strong>The app picks the withdrawal order for each household.</strong> Neither Pro-rata nor Tax-optimized is best everywhere;
        the better of the two is within 0.5% of the best of all six orders for ${share(pick.shareWithinTieBand, 1)} of households.</li>` : ''}
        <li><strong>Roth conversions matter.</strong> Without them, Tax-optimized's 82.5% spend is ${share(-noConversions.meanSpendDiff, 1)} lower on average
        and at least ${share(-noConversions.p5SpendDiff)} lower for the worst-hit 5% of households &mdash; early retirees who need the bridge to 59&frac12;.</li>
        <li><strong>How much to convert is the remaining question.</strong> Converting to the top of the 22% bracket (${code('C3')}) lowers the typical
        household's spend (median ${pct(top22.medianSpendDiff, 2)}) but helps some a great deal${heavy ? ` (an average of ${pct(heavy.meanDiff['W2+C3'], 1)} for households with 75%+ in Tax Deferred)` : ''}.
        Letting the app choose the conversion amount per household, as it now does the order, is the natural next step.</li>
        ${harvesting ? `<li><strong>0% gain harvesting makes no measurable difference</strong> (${pct(harvesting.meanSpendDiff, 2)} on average).</li>` : ''}
    </ul>`;
}

function reproduce(s) {
    return `
        <pre class="info-code">dotnet run -c Release --project MonteCarloSimulation.StrategyLab -- --seed ${s.config.seed} --publish</pre>
        <p class="info-meta">Writes CSVs and summary.json to MonteCarloSimulation.StrategyLab/output/, and with --publish updates this page's data.
        The same seed gives the same numbers. After-tax ending wealth values Tax Deferred at 78% and Brokerage gains at 85%.
        Seed ${s.config.seed}, ${s.config.paths} paths per household, published ${new Date(s.config.generatedAtUtc).toLocaleDateString('en-US')}.</p>`;
}

function render(s) {
    const byCode = Object.fromEntries(s.vsBaseline.map((c) => [c.code, c]));
    container.innerHTML = [
        section('summary', 'Does the app draw from the right accounts?', headline(s, byCode)),
        section('tested', 'What was tested', whatWasTested(s)),
        section('definitions', 'Strategy definitions', definitions(s)),
        s.funding ? section('funding', 'Where conversion tax comes from', funding(s)) : '',
        section('choosing', 'How the app chooses which accounts to draw from',
            (s.orderChoice ? orderChoice(s) : '') + '<h3>Pro-rata vs Tax-optimized</h3>' + directQuestion(s)),
        s.optimalDefaults ? section('optimal', 'What changed on the Optimal page', optimalDefaults(s)) : '',
        section('grid', "Every combination against the Tax-optimized baseline", grid(s, byCode)),
        section('who-loses', "Who loses with the Tax-optimized baseline", whoLoses(s)),
        section('why', 'Why early retirees are the exception', caseStudy(s)),
        section('slices', 'Results by household type', slices(s)),
        section('largest', 'The ten largest gaps', largestGaps(s)),
        section('single-year', 'Single-year check', singleYear(s)),
        section('page-defaults', "The main page's default inputs", pageDefaults(s)),
        section('conclusions', 'What this means', conclusions(s, byCode)),
        section('reproduce', 'Reproduce', reproduce(s)),
    ].join('');
}

fetch('model-info/strategy-lab.json')
    .then((response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    })
    .then(render)
    .catch((err) => {
        container.innerHTML = `<div class="error-box">Couldn't load the Strategy Lab results (${escapeHtml(err.message)}). Run the lab with --publish to create them.</div>`;
    });
