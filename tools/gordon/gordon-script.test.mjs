// Checks Gordon's guided setup (wwwroot/gordon-script.js; run by ci.yml: node --test): the answers it understands, a
// whole conversation's page actions in order, changing an answer from the summary, and the example household playing
// through to a run.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const wwwroot = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'MonteCarloSimulation.Web', 'wwwroot');
const context = { window: {}, I18n: { language: 'en' }, t: (key) => key };
// Gordon's run sentences are the results summary's (run-story.js), loaded first as on the page
vm.runInNewContext(readFileSync(join(wwwroot, 'run-story.js'), 'utf8'), context);
context.RunStory = context.window.RunStory;
vm.runInNewContext(readFileSync(join(wwwroot, 'gordon-script.js'), 'utf8'), context);
const { create, parseMoney, parseDate, parseAge } = context.window.GordonScript;
const plain = (value) => JSON.parse(JSON.stringify(value));

// A stand-in for gordon.js's page: the inputs as data, each action recorded and applied. offer: whether a Free visitor
// is offered Plus (until it's answered)
function fakePage({ plus = true, offer = false } = {}) {
    const state = {
        birthdate: '1969-07-07', retirementDate: '2027-01-01', filingStatus: 'single', taxDeferred: 1000000, roth: 20000,
        brokerage: 400000, inheritance: 0, inheritanceYears: 10, socialSecurityMonthly: 2750, socialSecurityStart: '2031-07-07',
        mix: { stocks: 60, bonds: 30, cash: 10 }, spending: 8000, years: 40, iterations: 20,
    };
    const actions = [];
    const plusOffer = { open: offer, answered: 0 };
    const apply = {
        setBirthdate: (v) => { state.birthdate = v; },
        setRetirementDate: (v) => { state.retirementDate = v; },
        setHorizon: (v) => { state.years = v; },
        setRuns: (v) => { state.iterations = v; },
        setFilingStatus: (v) => { state.filingStatus = v; },
        setTaxDeferred: (v) => { state.taxDeferred = v; },
        setRoth: (v) => { state.roth = v; },
        setBrokerage: (v) => { state.brokerage = v; },
        setInheritance: (v) => { state.inheritance = v.amount; state.inheritanceYears = v.years; },
        setSocialSecurityAmount: (v) => { state.socialSecurityMonthly = v; },
        setSocialSecurityStart: (v) => { state.socialSecurityStart = v; },
        setMix: (v) => { state.mix = v; },
        setSpending: (v) => { state.spending = v; },
    };
    return {
        state,
        actions,
        plusOffer,
        can: (feature) => plus || feature !== 'inheritance',
        offersPlus: () => plusOffer.open,
        plusOfferAnswered() {
            plusOffer.open = false;
            plusOffer.answered++;
        },
        plusHref: () => '/billing/subscribe?tier=plus&return=%2Findex.html',
        monthYear: (iso) => iso.slice(0, 7),
        // SSA's shape, as app.js's ssShareOnDate: whole months of age, 5/9% less a month for 36 months early, 5/12% after,
        // and 2/3% more a month to 70
        socialSecurityShare(birthdate, start) {
            const [by, bm, bd] = birthdate.split('-').map(Number);
            const [sy, sm, sd] = start.split('-').map(Number);
            const months = Math.min(Math.max((sy - by) * 12 + sm - bm - (sd < bd ? 1 : 0), 744), 840);
            const early = Math.max(804 - months, 0);
            return early > 0 ? 1 - (5 / 9 * Math.min(early, 36) + 5 / 12 * Math.max(0, early - 36)) / 100 : 1 + (2 / 3) * (months - 804) / 100;
        },
        get: () => state,
        play(turn) {
            for (const action of turn.actions) {
                actions.push(plain(action));
                apply[action.name]?.(action.value);
            }
            return turn;
        },
    };
}

function converse(page, answers) {
    const engine = create(page);
    let turn = page.play(engine.start());
    for (const answer of answers) turn = page.play(engine.reply(answer));
    return { engine, turn };
}

const keys = (turn) => plain(turn.say.map((m) => m.key ?? m.kind));

test('amounts are read the way people type them', () => {
    assert.equal(parseMoney('400k'), 400000);
    assert.equal(parseMoney('$1.2M'), 1200000);
    assert.equal(parseMoney('1,250,000'), 1250000);
    assert.equal(parseMoney('about 1.5 million'), 1500000);
    assert.equal(parseMoney('half a million'), 500000);
    assert.equal(parseMoney('$9.5k a month'), 9500);
    assert.equal(parseMoney('none'), 0);
    assert.equal(parseMoney("I don't have one"), 0);
    assert.equal(parseMoney('300 mil'), 300000);
    assert.equal(parseMoney('lots'), null);
});

test('dates and ages are read the way people type them', () => {
    assert.deepEqual(plain(parseDate('June 1967')), { year: 1967, month: 6, day: null });
    assert.deepEqual(plain(parseDate('6/15/1967')), { year: 1967, month: 6, day: 15 });
    assert.deepEqual(plain(parseDate('1967-06-15')), { year: 1967, month: 6, day: 15 });
    assert.deepEqual(plain(parseDate('March 3, 2030')), { year: 2030, month: 3, day: 3 });
    assert.deepEqual(plain(parseDate('junio 1967')), { year: 1967, month: 6, day: null });
    assert.deepEqual(plain(parseDate('1967')), { year: 1967, month: null, day: null });
    assert.equal(parseDate('soon'), null);
    assert.equal(parseAge('at 62', 62, 70), 62);
    assert.equal(parseAge("when I'm 65", 40, 85), 65);
    assert.equal(parseAge('at 75', 62, 70), null);
});

test('a whole conversation fills each input once, in order, then runs', () => {
    const page = fakePage();
    const { turn } = converse(page, [
        { choice: 'start' },
        { text: 'June 1980' },
        { text: 'January 2045' },
        { choice: 'married' },
        { text: '$900k' },
        { text: '150k' },
        { text: '250,000' },
        { choice: 'none' },
        { text: '4,800' },
        { choice: 67 },
        { choice: 'balanced' },
        { text: '9,000' },
    ]);

    assert.deepEqual(page.actions, [
        { name: 'setBirthdate', value: '1980-06-01' },
        { name: 'setRetirementDate', value: '2045-01-01' },
        { name: 'setHorizon', value: 30 },
        { name: 'setRuns', value: 99 },
        { name: 'setFilingStatus', value: 'married' },
        { name: 'setTaxDeferred', value: 900000 },
        { name: 'setRoth', value: 150000 },
        { name: 'setBrokerage', value: 250000 },
        { name: 'setInheritance', value: { amount: 0, years: 0 } },
        { name: 'setSocialSecurityStart', value: '2047-06-01' },
        { name: 'setSocialSecurityAmount', value: 4800 },
        { name: 'setMix', value: { stocks: 60, bonds: 30, cash: 10 } },
        { name: 'setSpending', value: 9000 },
    ]);
    assert.deepEqual(keys(turn), ['gordon.ack.spending', 'gordon.confirm.lead', 'summary', 'gordon.confirm.ask']);
});

const UP_TO_SPENDING = [
    { choice: 'start' }, { text: 'June 1980' }, { text: 'at 65' }, { text: 'single' }, { text: '1.2m' }, { text: 'none' },
    { text: '300k' }, { choice: 'none' }, { choice: 'unknown' }, { choice: 'unknown' }, { text: 'growth' },
];

test('confirming a typed spend runs the plan, and the result reads the run', () => {
    const page = fakePage();
    const { engine } = converse(page, [...UP_TO_SPENDING, { text: '9,000' }]);
    const running = engine.reply({ choice: 'run' });
    assert.equal(running.run, true);
    assert.equal(running.optimize, false);
    assert.deepEqual(keys(running), ['gordon.running']);

    const result = engine.result({ ok: true, total: 99, survived: 83, rate: 83 / 99, years: 30, median: 2.1e6, lower: 0, endYear: '2075', endAge: 95, lowerShortYear: 2068, lowerShortAge: 88, lowerShort: 31400, lowerShortShare: 0.35, monthly: 9000 });
    assert.deepEqual(keys(result), ['story.survived', 'story.median', 'story.lowerShort', 'story.tip.inRange']);
    assert.deepEqual(plain(result.choices.map((c) => c.value)), ['details', 'change', 'restart']);
});

test('"Find the most I can safely spend" leaves the spend to Optimize, which runs instead of Run', () => {
    const page = fakePage();
    const { engine, turn } = converse(page, [...UP_TO_SPENDING, { choice: 'optimize' }]);
    assert.ok(!page.actions.some((a) => a.name === 'setSpending'));
    const summary = turn.say.find((m) => m.kind === 'summary');
    assert.ok(plain(summary.lines()).some((line) => line.key === 'gordon.summary.optimize'));
    assert.deepEqual(plain(turn.choices.map((c) => c.key)), ['gordon.choice.optimizeRun', 'gordon.choice.change']);

    const optimizing = engine.reply({ choice: 'run' });
    assert.equal(optimizing.optimize, true);
    assert.equal(optimizing.run, false);
    assert.deepEqual(keys(optimizing), ['gordon.optimizing']);

    // Nothing is said about Optimize's answer until the run it starts is done
    const found = plain(engine.optimized({ kind: 'optimized', withdrawalMonthly: 7400, recommendedAge: 68, annuity: { investment: 300000, monthlyPayment: 1900, taxDeferred: true } }));
    assert.deepEqual(found.say, []);

    // Then one bulleted message: what Optimize decided and how the run went, with no run count and no tip
    const result = plain(engine.result({ ok: true, total: 99, survived: 78, rate: 78 / 99, years: 30, median: 1e6, lower: 0, endYear: '2075', endAge: 95, lowerShortYear: 2068, lowerShortAge: 88, lowerShort: 31400, lowerShortShare: 0.35, monthly: 7400 }));
    assert.equal(result.say.length, 1);
    const message = result.say[0];
    assert.equal(message.kind, 'summary');
    assert.deepEqual(message.lines.map((line) => line.key), ['gordon.optimized.found', 'gordon.optimized.annuity', 'story.survivedRate', 'story.median', 'story.lowerShort']);
    assert.equal(message.lines[0].values.amount, '$7,400');
    assert.deepEqual(message.lines[2].values, { years: 30, rate: 79 });
    // The weakest markets fall short of the plan, about $31,000 a year (35%), from 2068 at 88; they don't run dry
    assert.deepEqual(message.lines[4].values, { year: 2068, age: 88, amount: '$31,000', percent: 35 });
    assert.deepEqual(result.choices.map((c) => c.value), ['details', 'change', 'restart']);

    // A later plain run is told in the usual sentences again
    engine.reply({ choice: 'change' });
    engine.reply({ choice: 'spending' });
    engine.reply({ text: '7,000' });
    engine.reply({ choice: 'run' });
    const later = engine.result({ ok: true, total: 99, survived: 99, rate: 1, years: 30, median: 1e6, lower: 1e5, endYear: '2075', endAge: 95, lowerShortYear: null, lowerShortAge: null, lowerShort: null, lowerShortShare: null, monthly: 7000 });
    assert.deepEqual(keys(later), ['story.allSurvived', 'story.median', 'story.lowerHolds', 'story.tip.more']);
});

test('a Free visitor\'s Optimize is the teaser range, and says Plus finds the exact spend', () => {
    // Free isn't asked about an inheritance, the eighth answer
    const answers = UP_TO_SPENDING.filter((_, i) => i !== 7);
    const { engine } = converse(fakePage({ plus: false }), [...answers, { text: 'find the most I can safely spend' }]);
    assert.equal(engine.step, 'confirm');
    engine.reply({ choice: 'run' });
    assert.deepEqual(plain(engine.optimized({ kind: 'optimizedTeaser', low: 6500, high: 7000 })).say, []);
    const result = plain(engine.result({ ok: true, total: 99, survived: 99, rate: 1, years: 30, median: 1e6, lower: 1e5, endYear: '2075', endAge: 95, lowerShortYear: null, lowerShortAge: null, lowerShort: null, lowerShortShare: null, monthly: 6500 }));
    assert.equal(result.say.length, 1);
    const lines = result.say[0].lines;
    assert.deepEqual(lines.map((line) => line.key), ['gordon.optimized.teaser', 'story.allSurvivedRate', 'story.median', 'story.lowerHolds']);
    assert.equal(lines[0].values.low, '$6,500');
});

test('a typed spend after choosing Optimize goes back to a plain run', () => {
    const page = fakePage();
    const { engine } = converse(page, [...UP_TO_SPENDING, { choice: 'optimize' }, { choice: 'change' }, { choice: 'spending' }, { text: '6,000' }]);
    assert.equal(engine.step, 'confirm');
    assert.equal(page.state.spending, 6000);
    assert.equal(engine.reply({ choice: 'run' }).run, true);
});

test('an answer it doesn\'t understand is asked again, without changing anything', () => {
    const page = fakePage();
    const { turn, engine } = converse(page, [{ choice: 'start' }, { text: 'a while ago' }]);
    assert.deepEqual(keys(turn), ['gordon.retry.birth']);
    assert.equal(engine.step, 'birth');
    assert.deepEqual(page.actions, []);
});

test('Social Security is asked at full retirement age, and the start age sets what the form gets', () => {
    const upToSs = [{ choice: 'start' }, { text: 'June 1980' }, { text: 'January 2045' }, { choice: 'single' }, { text: '900k' }, { text: '0' }, { text: '100k' }, { choice: 'none' }];
    const claim = (age) => {
        const page = fakePage();
        const { turn } = converse(page, [...upToSs, { text: '4,800' }, age]);
        return { page, turn };
    };
    // 30% less at 62, 24% more at 70, 10% less 18 months early
    assert.equal(claim({ choice: 62 }).page.state.socialSecurityMonthly, 3360);
    assert.equal(claim({ choice: 67 }).page.state.socialSecurityMonthly, 4800);
    assert.equal(claim({ choice: 70 }).page.state.socialSecurityMonthly, 5952);
    const { page, turn } = claim({ text: 'December 2045' });
    assert.equal(page.state.socialSecurityStart, '2045-12-01');
    assert.equal(page.state.socialSecurityMonthly, 4320);
    assert.equal(turn.say[0].key, 'gordon.ack.ssStart');
    assert.equal(turn.say[0].values.amount, '$4,320');

    // Not sure: the form's claim moved back to 67 (2,750 at 62 is 3,929 at 67), then to the start chosen
    const unsure = fakePage();
    const { engine } = converse(unsure, [{ choice: 'start' }, { text: 'July 1969' }, { text: 'January 2027' }, { choice: 'single' }, { text: '900k' }, { text: '0' }, { text: '100k' }, { choice: 'none' }]);
    unsure.state.birthdate = '1969-07-07';
    const ack = unsure.play(engine.reply({ choice: 'unknown' }));
    assert.equal(ack.say[0].values.amount, '$3,929');
    unsure.play(engine.reply({ choice: 67 }));
    assert.equal(unsure.state.socialSecurityMonthly, 3929);

    // None: $0 on the form, and no start age asked
    const none = fakePage();
    const after = converse(none, [...upToSs, { choice: 'none' }]);
    assert.equal(none.state.socialSecurityMonthly, 0);
    assert.equal(after.engine.step, 'mix');
});

test('a change from the summary asks that question, then goes back to the summary', () => {
    const page = fakePage();
    const { engine } = converse(page, [
        { choice: 'start' }, { text: 'June 1980' }, { text: 'January 2045' }, { choice: 'single' }, { text: '900k' }, { text: '0' },
        { text: '100k' }, { choice: 'none' }, { text: '3000' }, { choice: 70 }, { choice: 'balanced' }, { text: '7000' },
    ]);
    page.play(engine.reply({ choice: 'change' }));
    page.play(engine.reply({ choice: 'spending' }));
    const back = page.play(engine.reply({ text: '6,500' }));
    assert.equal(engine.step, 'confirm');
    assert.deepEqual(keys(back), ['gordon.ack.spending', 'gordon.confirm.lead', 'summary', 'gordon.confirm.ask']);
    assert.equal(page.state.spending, 6500);

    // Social Security asks the start age too before going back
    page.play(engine.reply({ choice: 'change' }));
    page.play(engine.reply({ choice: 'ssAmount' }));
    page.play(engine.reply({ text: '3,200' }));
    assert.equal(engine.step, 'ssStart');
    page.play(engine.reply({ choice: 67 }));
    assert.equal(engine.step, 'confirm');
});

test('a Free visitor isn\'t asked about an inheritance', () => {
    const page = fakePage({ plus: false });
    const { engine } = converse(page, [
        { choice: 'start' }, { text: 'June 1980' }, { text: 'January 2045' }, { choice: 'single' }, { text: '900k' }, { text: '0' }, { text: '100k' },
    ]);
    assert.equal(engine.step, 'ssAmount');
});

test('"Show me an example" fills every input at once and optimizes, saying nothing until the run is done', () => {
    const page = fakePage();
    const engine = create(page);
    page.play(engine.start());
    const turn = page.play(engine.reply({ choice: 'example' }));

    // No questions and no message yet: the inputs fill, then Optimize runs quietly
    assert.deepEqual(keys(turn), []);
    assert.equal(turn.optimize, true);
    assert.equal(turn.quiet, true);
    assert.equal(engine.step, 'running');
    assert.deepEqual(page.actions.map((a) => a.name), [
        'setBirthdate', 'setRetirementDate', 'setHorizon', 'setRuns', 'setFilingStatus', 'setTaxDeferred', 'setRoth',
        'setBrokerage', 'setInheritance', 'setSocialSecurityStart', 'setSocialSecurityAmount', 'setMix',
    ]);
    assert.equal(page.state.birthdate, '1967-06-01');
    assert.equal(page.state.taxDeferred, 900000);
    assert.equal(page.state.socialSecurityStart, '2034-06-01');
    assert.equal(page.state.socialSecurityMonthly, 4800);

    // Nothing about Optimize's answer either; once the run is done, one message: born, retiring, the spend with about
    // 80% confidence, and the money they have as entered (an annuity Optimize buys is paid from the balances)
    const quiet = plain(engine.optimized({ kind: 'optimized', withdrawalMonthly: 8997, recommendedAge: 67, annuity: { investment: 900000, monthlyPayment: 5229, taxDeferred: true, cola: true } }));
    assert.deepEqual(quiet.say, []);
    page.state.taxDeferred = 0;
    const result = plain(engine.result({ ok: true, total: 99, survived: 82, rate: 82 / 99, years: 34, median: 440000, lower: 0, endYear: '2062', endAge: 94, lowerShortYear: 2045, lowerShortAge: 77, lowerShort: 31400, lowerShortShare: 0.35, monthly: 8997 }));
    assert.equal(result.say.length, 1);
    const report = result.say[0];
    assert.equal(report.kind, 'summary');
    assert.equal(report.lead, 'gordon.example.lead');
    assert.equal(report.after, undefined);
    assert.deepEqual(report.lines.map((line) => line.key), ['gordon.summary.born', 'gordon.example.retire', 'gordon.example.spend', 'gordon.example.savings']);
    assert.deepEqual(report.lines.map((line) => Object.values(line.values ?? {})[0]), ['1967-06', '2028-01', '$8,997', '$1,300,000']);
    assert.deepEqual(result.choices.map((c) => c.value), ['details', 'change', 'restart']);

    // Change something still works as after any conversation, and a later run reports in the usual sentences
    page.play(engine.reply({ choice: 'change' }));
    page.play(engine.reply({ choice: 'spending' }));
    page.play(engine.reply({ text: '8,000' }));
    assert.equal(engine.step, 'confirm');
    engine.reply({ choice: 'run' });
    const later = engine.result({ ok: true, total: 99, survived: 90, rate: 90 / 99, years: 34, median: 1e6, lower: 1e5, endYear: '2062', endAge: 94, lowerShortYear: null, lowerShortAge: null, lowerShort: null, lowerShortShare: null, monthly: 8000 });
    assert.deepEqual(keys(later), ['story.survived', 'story.median', 'story.lowerHolds', 'story.tip.more']);
});

test('a Free visitor\'s example leaves out the inheritance, and its spend is the low end of the range', () => {
    const page = fakePage({ plus: false });
    const engine = create(page);
    page.play(engine.start());
    page.play(engine.reply({ text: 'show me an example' }));
    assert.ok(!page.actions.some((a) => a.name === 'setInheritance'));

    assert.deepEqual(plain(engine.optimized({ kind: 'optimizedTeaser', low: 7000, high: 7500 })).say, []);
    const report = plain(engine.result({ ok: true, total: 99, survived: 81, rate: 81 / 99, years: 34, median: 2.06e6, lower: 0, endYear: '2062', endAge: 94, lowerShortYear: 2054, lowerShortAge: 86, lowerShort: 31400, lowerShortShare: 0.35, monthly: 7000 })).say[0];
    assert.deepEqual(report.lines.map((line) => line.key), ['gordon.summary.born', 'gordon.example.retire', 'gordon.example.spendTeaser', 'gordon.example.savings']);
    assert.equal(report.lines[2].values.amount, '$7,000');
});

test('when the example\'s run can\'t start, the one message says why', () => {
    const engine = create(fakePage());
    engine.start();
    engine.reply({ choice: 'example' });
    const failed = plain(engine.result({ ok: false, message: 'Allocations must total 100%.' }));
    assert.deepEqual(failed.say.map((m) => m.key), ['gordon.result.problem']);
});

test('the summary reads the form when it\'s shown, so it includes the last answer', () => {
    const page = fakePage();
    const { turn } = converse(page, [
        { choice: 'start' }, { text: 'June 1980' }, { text: 'January 2045' }, { choice: 'single' }, { text: '900k' }, { text: '0' },
        { text: '100k' }, { choice: 'none' }, { text: '3000' }, { choice: 70 }, { choice: 'balanced' }, { text: '7,250' },
    ]);
    const summary = turn.say.find((m) => m.kind === 'summary');
    const spending = plain(summary.lines()).find((line) => line.key === 'gordon.summary.spending');
    assert.equal(spending.values.amount, '$7,250');
});

test('the tip follows the survival percent as shown, and amounts are rounded', () => {
    const { engine } = converse(fakePage(), [{ choice: 'start' }]);
    const result = plain(engine.result({ ok: true, total: 99, survived: 79, rate: 79 / 99, years: 34, median: 2048411, lower: 0, endYear: '2062', endAge: 94, lowerShortYear: 2055, lowerShortAge: 87, lowerShort: 31400, lowerShortShare: 0.35, monthly: 7000 }));
    assert.equal(result.say[0].values.rate, 80);
    assert.equal(result.say[1].values.amount, '$2,050,000');
    assert.equal(result.say[3].key, 'story.tip.inRange');
});

test('a run whose typical market falls short says when and by how much, not that $0 is left', () => {
    const { engine } = converse(fakePage(), [{ choice: 'start' }]);
    const result = engine.result({ ok: true, total: 99, survived: 33, rate: 1 / 3, years: 34, median: 0, lower: 0, endYear: '2062', endAge: 94, lowerShortYear: 2043, lowerShortAge: 75, lowerShort: 31400, lowerShortShare: 0.35, medianShortYear: 2055, medianShortAge: 87, medianShort: 18650, medianShortShare: 0.2, monthly: 9000 });
    assert.deepEqual(keys(result), ['story.survived', 'story.medianShort', 'story.lowerShort', 'story.tip.lower']);
    // A typical market falls short too, from 2055 at 87, by about $19,000 a year (20%)
    assert.deepEqual(plain(result.say[1].values), { year: 2055, age: 87, amount: '$19,000', percent: 20 });
});

// --- The Plus offer, after a Free visitor's results ---

const FREE_UP_TO_SPENDING = UP_TO_SPENDING.filter((_, i) => i !== 7);
const DONE = { ok: true, total: 99, survived: 85, rate: 85 / 99, years: 30, median: 1e6, lower: 1e5, endYear: '2075', endAge: 95, lowerShortYear: null, lowerShortAge: null, lowerShort: null, lowerShortShare: null, monthly: 6000 };

function freeResult(page) {
    const { engine } = converse(page, [...FREE_UP_TO_SPENDING, { text: '6,000' }, { choice: 'run' }]);
    return { engine, result: plain(engine.result(DONE)) };
}

test('a Free visitor\'s results end with the Plus question, only Yes and No, and Gordon stays open', () => {
    const { engine, result } = freeResult(fakePage({ plus: false, offer: true }));
    assert.equal(result.say.at(-1).key, 'gordon.plus.ask');
    assert.deepEqual(result.say.slice(0, -1).map((m) => m.key), ['story.survived', 'story.median', 'story.lowerHolds', 'story.tip.inRange']);
    assert.deepEqual(result.choices.map((c) => c.value), ['yes', 'no']);
    assert.equal(result.keepOpen, true);
    assert.equal(engine.step, 'plusOffer');
});

test('Yes says what Plus unlocks, with only Unlock Plus (a link to the access page) and No thanks, and isn\'t asked again', () => {
    const page = fakePage({ plus: false, offer: true });
    const { engine } = freeResult(page);
    const yes = plain(engine.reply({ choice: 'yes' }));
    assert.deepEqual(keys(yes), ['gordon.plus.features']);
    assert.deepEqual(yes.choices, [
        { key: 'gordon.choice.unlockPlus', value: 'unlockPlus', href: '/billing/subscribe?tier=plus&return=%2Findex.html' },
        { key: 'gordon.choice.noThanks', value: 'noThanks' },
    ]);
    assert.equal(page.plusOffer.answered, 1);

    // Anything else asks again with the same two
    const again = plain(engine.reply({ text: 'what does it cost?' }));
    assert.deepEqual(keys(again), ['gordon.retry.plusUnlock']);
    assert.deepEqual(again.choices.map((c) => c.value), ['unlockPlus', 'noThanks']);

    // Unlock Plus (its tab opens) brings the results' buttons
    const opened = plain(engine.reply({ choice: 'unlockPlus' }));
    assert.deepEqual(keys(opened), ['gordon.plus.opened']);
    assert.deepEqual(opened.choices.map((c) => c.value), ['details', 'change', 'restart']);
    assert.equal(engine.step, 'results');

    // The next run's results don't ask again
    engine.reply({ choice: 'change' });
    engine.reply({ choice: 'spending' });
    engine.reply({ text: '5,500' });
    engine.reply({ choice: 'run' });
    const later = plain(engine.result(DONE));
    assert.ok(!keys(later).includes('gordon.plus.ask'));
    assert.equal(later.keepOpen, undefined);
});

test('No (or a typed no) goes back to the results\' buttons and isn\'t asked again', () => {
    for (const answer of [{ choice: 'no' }, { text: 'no thanks' }]) {
        const page = fakePage({ plus: false, offer: true });
        const { engine } = freeResult(page);
        const no = plain(engine.reply(answer));
        assert.deepEqual(keys(no), ['gordon.plus.declined']);
        assert.deepEqual(no.choices.map((c) => c.value), ['details', 'change', 'restart']);
        assert.equal(page.plusOffer.answered, 1);
        assert.equal(engine.step, 'results');
    }
});

test('anything but Yes or No asks again, with only Yes and No', () => {
    const page = fakePage({ plus: false, offer: true });
    const { engine } = freeResult(page);
    const again = plain(engine.reply({ text: 'maybe later' }));
    assert.deepEqual(keys(again), ['gordon.retry.plusOffer']);
    assert.deepEqual(again.choices.map((c) => c.value), ['yes', 'no']);
    assert.equal(page.plusOffer.answered, 0);
    assert.equal(engine.step, 'plusOffer');
});

test('no Plus question for Plus or Pro visitors, in Free Only, or after a run that didn\'t work', () => {
    const { result } = freeResult(fakePage({ plus: false, offer: false }));
    assert.ok(!keys(result).includes('gordon.plus.ask'));
    assert.deepEqual(result.choices.map((c) => c.value), ['details', 'change', 'restart']);

    const { engine } = converse(fakePage({ plus: false, offer: true }), [...FREE_UP_TO_SPENDING, { text: '6,000' }, { choice: 'run' }]);
    const failed = plain(engine.result({ ok: false, message: 'Allocations must total 100%.' }));
    assert.deepEqual(keys(failed), ['gordon.result.problem']);
});

test('the example\'s report ends with the Plus question too', () => {
    const page = fakePage({ plus: false, offer: true });
    const engine = create(page);
    page.play(engine.start());
    page.play(engine.reply({ choice: 'example' }));
    engine.optimized({ kind: 'optimizedTeaser', low: 6500, high: 7000 });
    const report = plain(engine.result(DONE));
    assert.deepEqual(keys(report), ['summary', 'gordon.plus.ask']);
    assert.equal(report.keepOpen, true);
});

test('No thanks after Yes brings the results\' buttons', () => {
    const { engine } = freeResult(fakePage({ plus: false, offer: true }));
    engine.reply({ choice: 'yes' });
    const no = plain(engine.reply({ choice: 'noThanks' }));
    assert.deepEqual(keys(no), ['gordon.plus.declined']);
    assert.deepEqual(no.choices.map((c) => c.value), ['details', 'change', 'restart']);
});

test('once Plus is unlocked, Run optimizes again with the full Optimizer', () => {
    const page = fakePage({ plus: false, offer: true });
    const { engine } = freeResult(page);
    engine.reply({ choice: 'yes' });
    engine.reply({ choice: 'unlockPlus' });

    const unlocked = plain(engine.unlocked());
    assert.deepEqual(keys(unlocked), ['gordon.plus.unlocked']);
    assert.deepEqual(unlocked.choices.map((c) => c.key), ['gordon.choice.runPlus', 'gordon.choice.details', 'gordon.choice.changeAndRerun', 'gordon.choice.startOver']);

    const run = engine.reply({ choice: 'rerun' });
    assert.equal(run.optimize, true);
    assert.equal(run.run, false);
    assert.deepEqual(keys(run), ['gordon.optimizing']);
    assert.equal(engine.step, 'running');
});
