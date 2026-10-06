// The Model Info page: renders the Strategy Lab's published results (model-info/strategy-lab.json, written by
// `dotnet run --project MonteCarloSimulation.StrategyLab -- --publish`). Numbers come from the JSON; the
// explanations around them are written here, through t() (i18n.js), so the page reads in English or Spanish.
// Self-contained on purpose, like optimal.js.
const container = document.getElementById('model-info');

// ---- formatting (numbers and money keep the US format in both languages)

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

// ---- the lab's own text

// The lab writes its labels and definitions in English. Each has Spanish under a lab.* key: lab.label.<slug of the
// English> for labels and names, lab.def.<code> for definitions. I18n.translateData uses it only while en.json's
// English still matches the data (tools/i18n checks that), so a republished lab with new wording shows its English
// rather than a stale translation. Returns escaped HTML.
function slug(english) {
    return english.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
}

function labLabel(english) {
    return escapeHtml(I18n.translateData(`lab.label.${slug(english)}`, english));
}

function labDefinition(d) {
    return escapeHtml(I18n.translateData(`lab.def.${d.code}`, d.definition));
}

// "Total assets: Over $3M" - a slice and its bin
function labSlice(text) {
    return text.split(': ').map(labLabel).join(': ');
}

// A household's one-line description ("#664: retire 2029 at 55.7 for 21 yrs; ..."), translated piece by piece with
// the lab.household.* templates; a piece that matches none stays English
function household(description) {
    return escapeHtml(description.split('; ').map((part) => {
        const whole = I18n.translateTemplate(part, 'lab.household.');
        return whole !== part ? whole : part.split(', ').map((piece) => I18n.translateTemplate(piece, 'lab.household.')).join(', ');
    }).join('; '));
}

// An investment scenario's description, as the other pages show it
function scenarioDescription(id, english) {
    return escapeHtml(I18n.translateData(`scenario.${id}.description`, english));
}

// ---- sections

// The app's per-household order pick, from the order-choice measurement
function appOrderPick(s) {
    return (s.conversionChoice?.rows ?? s.orderChoice ?? []).find((o) => o.name.endsWith('(app)'));
}

// How the app picks how far to convert, together with the order
function conversionChoice(s) {
    const c = s.conversionChoice;
    const rows = c.rows.map((o) => `<tr class="${o.name.endsWith('(app)') ? 'recommended-row' : ''}">
        <td>${labLabel(o.name)}</td>
        <td class="num">${share(o.shareWithinTieBand, 1)}</td>
        <td class="num">${pct(o.meanShortfall, 2)}</td>
        <td>${labSlice(o.worstSlice)}</td>
        <td class="num ${o.worstSliceMeanShortfall < -0.01 ? 'loss' : ''}">${pct(o.worstSliceMeanShortfall, 2)}</td></tr>`);
    const targetLabels = { None: t('info.target.None'), Bracket12: t('info.target.Bracket12'), Bracket22: t('info.target.Bracket22'), Bracket24: t('info.target.Bracket24') };
    const targets = Object.entries(c.targetShares).map(([target, v]) =>
        `<tr><td>${targetLabels[target] ?? escapeHtml(target)}</td><td class="num">${share(v, 1)}</td><td class="num">${share(c.picker.targetShares[target] ?? 0, 1)}</td></tr>`);
    return `
        <h3>${t('info.conversion.heading')}</h3>
        <p>${t('info.conversion.intro')}</p>
        ${table([t('info.conversion.col.pick'), t('info.col.withinBestOfAll'), t('info.col.averageShortfall'), t('info.col.worstHousehold'), t('info.col.shortfallThere')], rows)}
        ${table([t('info.conversion.col.line'), t('info.conversion.col.idealPick'), t('info.conversion.col.picker')], targets)}
        <p>${t('info.conversion.picker', { share: share(c.picker.shareWithinTieBand, 1), shortfall: pct(c.picker.meanShortfall, 2) })}</p>
        <p>${t('info.conversion.c5', { code: code('C5'), share: share(c.belowIrmaaBestShare, 1), added: pct(c.belowIrmaaAddedMean, 2) })}
        ${c.belowIrmaaAdopted ? t('info.conversion.c5Adopted') : t('info.conversion.c5NotAdopted')}</p>`;
}

function headline(s) {
    const heavy = s.subsets[3];
    const sy = s.singleYear;
    const pick = appOrderPick(s);
    const app = appFunding(s);
    const fundingGain = s.funding && app ? s.funding.vsBaseline.find((c) => c.code === `W2+C1+${app.code}`) : null;
    // "Out of the conversion (app)" -> "out of the conversion"
    const funding = app ? labLabel(app.name).replace(' (app)', '').toLowerCase() : '';
    return `
        <p class="info-lede">${t('info.headline.lede', { households: s.config.scenarios.toLocaleString('en-US'), funding, median: share(s.regret.median, 2) })}</p>
        <div class="stat-grid">
            <div class="stat-tile key"><b>${sy.withinFiveDollars} / ${sy.checked}</b><span>${t('info.headline.singleYear')}</span></div>
            ${fundingGain ? `<div class="stat-tile"><b>${pct(fundingGain.meanSpendDiff, 2)}</b><span>${t('info.headline.fundingGain')}</span></div>` : ''}
            ${pick ? `<div class="stat-tile"><b>${share(pick.shareWithinTieBand, 1)}</b><span>${t('info.headline.orderPick')}</span></div>` : ''}
            <div class="stat-tile"><b>${pct(heavy.medianGap, 2)}</b><span>${t('info.headline.heavyGap', { households: heavy.scenarios })}</span></div>
        </div>`;
}

function whatWasTested(s) {
    return `
        <p>${t('info.tested.profile')}</p>
        <p>${t('info.tested.method', { paths: s.config.paths })}</p>`;
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

    const orderRows = orders.map((d) => `<tr><td>${code(d.code)}</td><td>${labLabel(d.name)}</td><td>${labDefinition(d)}</td></tr>`);
    const policyRows = policies.map((d) => {
        let line = '&mdash;';
        if (d.line2026 != null) {
            line = money(d.line2026);
            if (d.zeroRateAlternative != null) line += t('info.definitions.zeroRateAlternative', { amount: money(d.zeroRateAlternative) });
        }
        return `<tr><td>${code(d.code)}</td><td>${labDefinition(d)}</td><td class="num">${line}</td></tr>`;
    });

    return `
        <h3>${t('info.definitions.orders')}</h3>
        <p>${t('info.definitions.ordersIntro')}</p>
        ${table([t('info.col.code'), t('info.col.name'), t('info.col.rule')], orderRows)}
        <h3>${t('info.definitions.policies')}</h3>
        <p>${t('info.definitions.policiesIntro')}</p>
        ${table([t('info.col.code'), t('info.definitions.col.convertsUntil'), t('info.definitions.col.line2026')], policyRows)}
        <ul class="info-notes">
            <li>${t('info.definitions.note.grossIncome')}</li>
            <li>${t('info.definitions.note.appPicks', { c0: code('C0'), c1: code('C1'), c2: code('C2'), c3: code('C3'), c4: code('C4'), c5: code('C5') })}</li>
            <li>${t('info.definitions.note.gate')}</li>
            <li>${t('info.definitions.note.irmaa')}</li>
            <li>${app ? t('info.definitions.note.fundingApp', { code: code(app.code) }) : t('info.definitions.note.funding')}</li>
        </ul>
        ${fundings.length === 0 ? '' : `
        <h3>${t('info.definitions.funding')}</h3>
        <p>${t('info.definitions.fundingIntro')}</p>
        ${table([t('info.col.code'), t('info.col.name'), t('info.col.rule')], fundings.map((d) => `<tr><td>${code(d.code)}</td><td>${labLabel(d.name)}</td><td>${labDefinition(d)}</td></tr>`))}`}`;
}

// Where conversion tax comes from: every funding rule against the app before the change (W2+C1 paying
// conversion tax only from Brokerage), on the same households and markets.
function funding(s) {
    const f = s.funding;
    const byCode = Object.fromEntries(f.vsBaseline.map((c) => [c.code, c]));
    const rules = s.definitions.filter((d) => d.kind === 'funding');
    const app = appFunding(s);
    const orderNames = { W2: t('order.TaxOptimized'), W1: t('order.ProRata') };
    const rows = ['W2', 'W1'].flatMap((order) => rules.map((r) => {
        const c = byCode[`${order}+C1+${r.code}`];
        if (!c) return '';
        const isBaseline = c.code === f.baseline;
        return `<tr class="${app && r.code === app.code && order === 'W2' ? 'recommended-row' : ''}">
            <td>${code(c.code)}</td><td>${orderNames[order]}, ${labLabel(r.name)}</td>
            <td class="num ${tone(c.meanSpendDiff)}">${isBaseline ? t('info.baseline') : pct(c.meanSpendDiff, 2)}</td>
            <td class="num">${isBaseline ? '' : t('info.range', { from: pct(c.meanCi[0], 2), to: pct(c.meanCi[1], 2) })}</td>
            <td class="num">${isBaseline ? '' : pct(c.medianSpendDiff, 2)}</td>
            <td class="num">${isBaseline ? '' : share(c.winRate)}</td>
            <td class="num">${isBaseline ? '' : share(c.lossRate)}</td></tr>`;
    }));
    const early = f.slices.find((sl) => sl.dimension.startsWith('Retires before'));
    const earlyRows = early ? early.bins.map((b) => `<tr><td>${labLabel(b.label)}</td><td class="num">${b.scenarios}</td>
        ${rules.filter((r) => r.code !== 'F0').map((r) => {
            const k = `W2+C1+${r.code}`;
            return `<td class="num ${tone(b.meanDiff[k])}">${pct(b.meanDiff[k], 2)} <span class="info-meta">(${t('info.median', { value: pct(b.medianDiff[k], 2) })})</span></td>`;
        }).join('')}</tr>`) : [];
    return `
        <p>${t('info.funding.intro', { households: f.scenariosCompared.toLocaleString('en-US'), c1: code('C1'), baseline: code(f.baseline) })}</p>
        ${table([t('info.col.combination'), t('info.funding.col.orderAndFunding'), t('info.col.average'), t('info.col.interval'), t('info.col.median'), t('info.col.wins'), t('info.col.loses')], rows)}
        ${early ? `<h3>${t('info.funding.byAge')}</h3>
        ${table([t('info.funding.col.retiresBefore'), t('info.col.households'), ...rules.filter((r) => r.code !== 'F0').map((r) => `${code(`W2+C1+${r.code}`)}`)], earlyRows)}` : ''}
        ${app ? `<p>${t('info.funding.appUses', { code: code(app.code), definition: labDefinition(app) })}</p>` : ''}`;
}

// How the app picks the withdrawal order: each order under the 12% fill against the best order
// for the same household.
function orderChoice(s) {
    const rows = s.orderChoice.map((o) => `<tr class="${o.qualifies ? 'recommended-row' : ''}">
        <td>${code(o.code)} ${labLabel(o.name)}</td>
        <td class="num">${share(o.shareWithinTieBand, 1)}</td>
        <td class="num">${pct(o.meanShortfall, 2)}</td>
        <td>${labSlice(o.worstSlice)}</td>
        <td class="num ${o.worstSliceMeanShortfall < -0.01 ? 'loss' : ''}">${pct(o.worstSliceMeanShortfall, 2)}</td>
        <td>${o.qualifies ? t('info.yes') : t('info.no')}</td></tr>`);
    const chosen = s.orderChoice.filter((o) => o.qualifies);
    return `
        <p>${t('info.orders.intro')}</p>
        ${table([t('info.orders.col.order'), t('info.col.withinBest'), t('info.col.averageShortfall'), t('info.col.worstHousehold'), t('info.col.shortfallThere'), t('info.orders.col.qualifies')], rows)}
        <p>${chosen.length > 0
            ? t('info.orders.appUses', { orders: chosen.map((o) => `${code(o.code)} ${labLabel(o.name)}`).join(' / ') })
            : t('info.orders.noneQualifies')}</p>`;
}

// The Optimal page's default inputs (its default asset mix) before and after the app chose its own accounts
function optimalDefaults(s) {
    const r = s.optimalDefaults;
    const rows = [r].map(() => {
        const b = r.before;
        const a = r.after;
        const change = (x, y) => `<span class="info-meta">(${pct(y / x - 1, 1)})</span>`;
        return `<tr><td>${I18n.translateData('info.optimal.defaultMix', r.mix)}</td>
            <td class="num">${b.recommendedAge} &rarr; ${a.recommendedAge}</td>
            <td class="num">${perMonth(b.spendAt85)} &rarr; ${perMonth(a.spendAt85)} ${change(b.spendAt85, a.spendAt85)}</td>
            <td class="num">${perMonth(b.spendAtMidpoint)} &rarr; ${perMonth(a.spendAtMidpoint)} ${change(b.spendAtMidpoint, a.spendAtMidpoint)}</td>
            <td class="num">${perMonth(b.spendAt80)} &rarr; ${perMonth(a.spendAt80)} ${change(b.spendAt80, a.spendAt80)}</td>
            <td class="num">${share(b.survivalRate, 1)} &rarr; ${share(a.survivalRate, 1)}</td></tr>`;
    });
    return `
        <p>${t('info.optimal.intro')}</p>
        ${table([t('info.optimal.col.mix'), t('info.optimal.col.age'), t('info.optimal.col.survival85'), t('info.optimal.col.survival825'), t('info.optimal.col.survival80'), t('info.optimal.col.survivalAtSpend')], rows)}`;
}

function grid(s, byCode) {
    const orders = s.definitions.filter((d) => d.kind === 'order' && d.code !== 'W2nh');
    const policies = s.definitions.filter((d) => d.kind === 'conversion');
    const rows = orders.map((o) => {
        const cells = policies.map((p) => {
            const c = byCode[`${o.code}+${p.code}`];
            if (c.code === s.baseline) {
                return `<td class="grid-cell baseline"><span class="grid-main">${t('info.grid.baseline')}</span><span class="grid-sub">${t('info.grid.todaysDefault')}</span></td>`;
            }
            return `<td class="grid-cell ${tone(c.meanSpendDiff)}"><span class="grid-main">${pct(c.meanSpendDiff, 2)}</span>
                <span class="grid-sub">${t('info.grid.cell', { median: pct(c.medianSpendDiff, 2), wins: share(c.winRate), loses: share(c.lossRate) })}</span></td>`;
        });
        return `<tr><th scope="row">${code(o.code)} ${labLabel(o.name)}</th>${cells.join('')}</tr>`;
    });
    const nh = byCode['W2nh+C1'];
    return `
        <p>${t('info.grid.intro')}</p>
        ${table([t('info.grid.col.order'), ...policies.map((p) => `${code(p.code)} ${labLabel(p.name)}`)], rows, 'strategy-grid')}
        <p class="info-meta">${t('info.grid.noHarvesting', { code: code('W2nh+C1'), average: pct(nh.meanSpendDiff, 2), median: pct(nh.medianSpendDiff, 2), wins: share(nh.winRate), loses: share(nh.lossRate) })}</p>`;
}

function directQuestion(s) {
    const rows = s.directQuestion.map((c) => `<tr>
        <td>${labLabel(c.label)}</td>
        <td class="num ${tone(c.meanSpendDiff)}">${pct(c.meanSpendDiff, 2)}</td>
        <td class="num">${t('info.range', { from: pct(c.meanCi[0], 2), to: pct(c.meanCi[1], 2) })}</td>
        <td class="num">${pct(c.medianSpendDiff, 2)}</td>
        <td class="num">${share(c.winRate)}</td>
        <td class="num">${share(c.lossRate)}</td>
        <td class="num">${pct(c.p5SpendDiff, 1)} / ${pct(c.p95SpendDiff, 1)}</td></tr>`);
    return `
        <p>${t('info.direct.intro')}</p>
        ${table([t('info.direct.col.comparison'), t('info.col.average'), t('info.col.interval'), t('info.col.median'), t('info.col.wins'), t('info.col.loses'), t('info.direct.col.percentiles')], rows)}
        <p>${t('info.direct.explanation')}</p>`;
}

function whoLoses(s) {
    const rows = s.subsets.map((d) => `<tr>
        <td>${labLabel(d.label)}</td><td class="num">${d.scenarios}</td>
        <td class="num">${pct(d.medianGap, 2)}</td><td class="num">${pct(d.meanGap, 2)}</td><td class="num">${pct(d.p90Gap, 1)}</td>
        <td class="num">${share(d.shareOver1Pct)}</td><td class="num">${share(d.shareOver3Pct)}</td>
        <td class="num ${tone(d.proRataMedianDiff)}">${pct(d.proRataMedianDiff, 2)}</td></tr>`);
    const over3 = Object.entries(s.regret.bestCombinationCountsOver3Pct);
    const over3Total = over3.reduce((sum, [, n]) => sum + n, 0);
    const winners = over3.map(([c, n]) => `${code(c)} ${n}`).join(', ');
    return `
        <p>${t('info.whoLoses.intro')}</p>
        ${table([t('info.col.households'), t('info.whoLoses.col.count'), t('info.whoLoses.col.medianGap'), t('info.whoLoses.col.averageGap'), t('info.whoLoses.col.p90Gap'), t('info.whoLoses.col.over1'), t('info.whoLoses.col.over3'), t('info.whoLoses.col.proRata')], rows)}
        <p class="info-meta">${t('info.whoLoses.winners', { households: over3Total, winners })}</p>`;
}

function caseStudy(s) {
    const cs = s.caseStudy;
    const intro = `
        <p>${t('info.case.gate')}</p>
        <p class="info-callout">${t('info.case.callout', { c1: code('C1') })}</p>`;
    if (!cs) return intro;

    const byYear = cs.baseline.map((b, i) => [b, cs.alternative[i]]);
    const names = [t('order.TaxOptimized'), t('order.ProRata')];
    const rows = byYear.flatMap(([b, a]) => [[b, cs.baselineCombination], [a, cs.alternativeCombination]].map(([y, c], k) => {
        const last = y === cs.baseline[cs.baseline.length - 1];
        const result = k === 0 && last
            ? `<span class="loss">${t('info.case.runsOut')}</span>`
            : (k === 1 && b === cs.baseline[cs.baseline.length - 1] ? `<span class="gain">${t('info.case.stillGoing')}</span>` : '');
        return `<tr class="${k === 0 ? 'year-first' : ''}">
            <td>${k === 0 ? t('info.case.yearAge', { year: y.calendarYear, age: y.age.toFixed(1) }) : ''}</td>
            <td>${code(c)} ${names[k]}</td>
            <td class="num">${money(y.brokerageSold)}</td><td class="num">${money(y.rothSpent)}</td><td class="num">${money(y.taxDeferredWithdrawal)}</td>
            <td class="num">${money(y.converted)}</td><td class="num">${money(y.taxes)}</td><td class="num">${money(y.rothBalance)}</td>
            <td>${result}</td></tr>`;
    }));

    return `${intro}
        <p>${t('info.case.household', { household: household(cs.scenario), baseline: perMonth(cs.baselineSpend825), alternative: perMonth(cs.alternativeSpend825), path: cs.path, spend: perMonth(cs.spend) })}</p>
        ${table([t('info.case.col.year'), t('info.case.col.order'), t('info.case.col.brokerageSold'), t('info.case.col.rothSpent'), t('info.case.col.taxDeferred'), t('info.case.col.converted'), t('info.case.col.taxes'), t('info.case.col.rothYearEnd'), ''], rows, 'case-study')}
        <h3>${t('info.case.realHeading')}</h3>
        <p>${t('info.case.realIntro')}</p>
        <ul class="info-notes">
            <li>${t('info.case.seasoning')}</li>
            <li>${t('info.case.noOtherRoutes')}</li>
        </ul>`;
}

function slices(s) {
    const shown = [['W1+C1', t('info.slices.proRataFill')], ['W1+C0', t('info.slices.proRataNone')], ['W2+C0', t('info.slices.taxOptimizedNone')], ['W2+C3', t('info.slices.taxOptimized22')]];
    const body = s.slices.map((sl) => `
        <tr class="slice-group"><th colspan="${2 + shown.length}">${labLabel(sl.dimension)}</th></tr>
        ${sl.bins.map((b) => `<tr><td>${labLabel(b.label)}</td><td class="num">${b.scenarios}</td>
            ${shown.map(([c]) => `<td class="num ${tone(b.medianDiff[c])}">${pct(b.medianDiff[c], 2)} <span class="info-meta">(${pct(b.meanDiff[c], 1)})</span></td>`).join('')}</tr>`).join('')}`);
    return `
        <p>${t('info.slices.intro')}</p>
        ${table([t('info.slices.col.slice'), t('info.col.households'), ...shown.map(([c, n]) => `${code(c)}<br>${n}`)], body)}`;
}

function largestGaps(s) {
    const ages = s.topRegret.map((r) => r.retirementAge);
    const shares = s.topRegret.map((r) => r.taxDeferredShare);
    const rows = s.topRegret.map((r) => `<tr>
        <td class="household">${household(r.scenario)}</td><td class="num">${perMonth(r.baselineSpend825)}</td>
        <td>${code(r.bestCombination)}</td><td class="num">${perMonth(r.bestSpend825)}</td><td class="num gain">${pct(r.regret, 0)}</td></tr>`);
    return `
        <p>${t('info.largest.intro', {
            minAge: Math.floor(Math.min(...ages)), maxAge: Math.ceil(Math.max(...ages)),
            minShare: share(Math.min(...shares)), maxShare: share(Math.max(...shares)),
        })}</p>
        ${table([t('info.largest.col.household'), t('info.largest.col.baseline'), t('info.largest.col.best'), t('info.largest.col.bestSpend'), t('info.largest.col.gap')], rows)}`;
}

function singleYear(s) {
    const sy = s.singleYear;
    return `
        <p>${t('info.singleYear.intro', { cases: sy.cases.toLocaleString('en-US') })}</p>
        <ul class="info-notes">
            <li>${t('info.singleYear.minimum', { within: sy.withinFiveDollars, checked: sy.checked, maxGap: money(sy.maxGap), shortfalls: sy.shortfalls })}</li>
            <li>${t('info.singleYear.roth', { cases: sy.rothBeforeOthers })}</li>
        </ul>
        <p>${t('info.singleYear.conclusion')}</p>`;
}

function pageDefaults(s) {
    const rows = s.pageDefault.map((p) => {
        const byCombination = Object.fromEntries(p.rows.map((r) => [r.combination, r]));
        const base = byCombination[s.baseline].spend825;
        const best = p.rows.reduce((a, b) => (b.spend825 > a.spend825 ? b : a));
        const cell = (c) => `<td class="num">${perMonth(byCombination[c].spend825)} <span class="info-meta">(${pct(byCombination[c].spend825 / base - 1, 1)})</span></td>`;
        return `<tr><td>${p.investmentScenarioId}. ${scenarioDescription(p.investmentScenarioId, p.description)}</td><td class="num">${perMonth(base)}</td>
            ${cell('W1+C1')}${cell('W2+C0')}${cell('W1+C0')}<td>${code(best.combination)} ${pct(best.spend825 / base - 1, 2)}</td></tr>`;
    });
    return `
        <p>${t('info.pageDefaults.intro')}</p>
        ${table([t('info.col.investmentScenario'), t('info.pageDefaults.col.taxOptimizedConv'), t('info.pageDefaults.col.proRataConv'), t('info.pageDefaults.col.taxOptimizedNone'), t('info.pageDefaults.col.proRataNone'), t('info.pageDefaults.col.best')], rows)}`;
}

function conclusions(s, byCode) {
    const harvesting = s.directQuestion.find((c) => c.code.startsWith(`${s.baseline} vs W2nh`));
    const noConversions = byCode['W2+C0'];
    const pick = appOrderPick(s);
    const app = appFunding(s);
    const fundingGain = s.funding && app ? s.funding.vsBaseline.find((c) => c.code === `W2+C1+${app.code}`) : null;
    const top22 = byCode['W2+C3'];
    const heavy = s.slices.find((sl) => sl.dimension.startsWith('Tax Deferred share'))?.bins.find((b) => b.label === '75-100%');
    const conversionPick = () => {
        const appRow = s.conversionChoice.rows.find((o) => o.name.endsWith('(app)'));
        const before = s.conversionChoice.rows[0];
        return `<li>${t('info.conclusions.conversionPick', {
            c3: code('C3'),
            median: pct(top22.medianSpendDiff, 2),
            heavy: heavy ? t('info.conclusions.conversionPickHeavy', { average: pct(heavy.meanDiff['W2+C3'], 1) }) : '',
            both: share(appRow.shareWithinTieBand, 1),
            orderOnly: share(before.shareWithinTieBand, 1),
        })}</li>`;
    };
    return `<ul class="info-notes">
        <li>${t('info.conclusions.withinYear')}</li>
        ${fundingGain ? `<li>${t('info.conclusions.funding', { average: pct(fundingGain.meanSpendDiff, 2), from: pct(fundingGain.meanCi[0], 2), to: pct(fundingGain.meanCi[1], 2), lossShare: share(fundingGain.lossRate, 1) })}</li>` : ''}
        ${pick ? `<li>${t('info.conclusions.orderPick', { share: share(pick.shareWithinTieBand, 1) })}</li>` : ''}
        <li>${t('info.conclusions.conversionsMatter', { average: share(-noConversions.meanSpendDiff, 1), worst: share(-noConversions.p5SpendDiff) })}</li>
        ${s.conversionChoice ? conversionPick() : ''}
        ${harvesting ? `<li>${t('info.conclusions.harvesting', { average: pct(harvesting.meanSpendDiff, 2) })}</li>` : ''}
    </ul>`;
}

function reproduce(s) {
    // Spanish spells the month out: "30/9/2026" would read as a US date the wrong way round
    const date = new Date(s.config.generatedAtUtc);
    const published = I18n.language === 'es'
        ? date.toLocaleDateString('es-US', { day: 'numeric', month: 'long', year: 'numeric' })
        : date.toLocaleDateString('en-US');
    return `
        <pre class="info-code">dotnet run -c Release --project MonteCarloSimulation.StrategyLab -- --seed ${s.config.seed} --publish</pre>
        <p class="info-meta">${t('info.reproduce.notes', { seed: s.config.seed, paths: s.config.paths, published })}</p>`;
}

// The loaded results, kept so a language switch re-renders without fetching again
let summary = null;

function render() {
    const s = summary;
    if (!s) return;
    const byCode = Object.fromEntries(s.vsBaseline.map((c) => [c.code, c]));
    container.innerHTML = [
        section('summary', t('info.section.summary'), headline(s)),
        section('tested', t('info.section.tested'), whatWasTested(s)),
        section('definitions', t('info.section.definitions'), definitions(s)),
        s.funding ? section('funding', t('info.section.funding'), funding(s)) : '',
        section('choosing', t('info.section.choosing'),
            (s.orderChoice ? orderChoice(s) : '') + (s.conversionChoice ? conversionChoice(s) : '') + `<h3>${t('info.direct.heading')}</h3>` + directQuestion(s)),
        s.optimalDefaults ? section('optimal', t('info.section.optimal'), optimalDefaults(s)) : '',
        section('grid', t('info.section.grid'), grid(s, byCode)),
        section('who-loses', t('info.section.whoLoses'), whoLoses(s)),
        section('why', t('info.section.why'), caseStudy(s)),
        section('slices', t('info.section.slices'), slices(s)),
        section('largest', t('info.section.largest'), largestGaps(s)),
        section('single-year', t('info.section.singleYear'), singleYear(s)),
        section('page-defaults', t('info.section.pageDefaults'), pageDefaults(s)),
        section('conclusions', t('info.section.conclusions'), conclusions(s, byCode)),
        section('reproduce', t('info.section.reproduce'), reproduce(s)),
    ].join('');
}

let loadError = null;

function renderError() {
    container.innerHTML = `<div class="error-box">${t('info.loadFailed', { message: escapeHtml(loadError.message) })}</div>`;
}

document.addEventListener('i18n:change', () => (loadError ? renderError() : render()));

Promise.all([
    fetch('model-info/strategy-lab.json').then((response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json();
    }),
    I18n.ready,
])
    .then(([data]) => {
        summary = data;
        render();
    })
    .catch((err) => {
        loadError = err;
        renderError();
    });
