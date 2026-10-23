// Checks Gordon's guided setup (wwwroot/gordon-script.js; run by ci.yml: node --test): the answers it understands, a
// whole conversation's page actions in order, changing an answer from the summary, and the example household playing
// through to a run.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const script = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'MonteCarloSimulation.Web', 'wwwroot', 'gordon-script.js');
const context = { window: {}, I18n: { language: 'en' }, t: (key) => key };
vm.runInNewContext(readFileSync(script, 'utf8'), context);
const { create, parseMoney, parseDate, parseAge } = context.window.GordonScript;
const plain = (value) => JSON.parse(JSON.stringify(value));

// A stand-in for gordon.js's page: the inputs as data, each action recorded and applied
function fakePage({ plus = true } = {}) {
    const state = {
        birthdate: '1969-07-07', retirementDate: '2027-01-01', filingStatus: 'single', taxDeferred: 1000000, roth: 20000,
        brokerage: 400000, inheritance: 0, inheritanceYears: 10, socialSecurityMonthly: 2750, socialSecurityStart: '2031-07-07',
        mix: { stocks: 60, bonds: 30, cash: 10 }, spending: 8000, years: 40, iterations: 20,
    };
    const actions = [];
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
        can: (feature) => plus || feature !== 'inheritance',
        monthYear: (iso) => iso.slice(0, 7),
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
        { name: 'setSocialSecurityAmount', value: 4800 },
        { name: 'setSocialSecurityStart', value: '2047-06-01' },
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

    const result = engine.result({ ok: true, total: 99, survived: 83, rate: 83 / 99, years: 30, median: 2.1e6, lower: 0, endYear: '2075', endAge: 95, lowerOutYear: '2068', lowerOutAge: 88, monthly: 9000 });
    assert.deepEqual(keys(result), ['gordon.result.survived', 'gordon.result.median', 'gordon.result.lowerRunsOut', 'gordon.tip.inRange']);
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

    const found = plain(engine.optimized({ kind: 'optimized', withdrawalMonthly: 7400, recommendedAge: 68, annuity: { investment: 300000, monthlyPayment: 1900, taxDeferred: true } }));
    assert.deepEqual(found.say.map((m) => m.key), ['gordon.optimized.found', 'gordon.optimized.annuity', 'gordon.running']);
    assert.equal(found.say[0].values.amount, '$7,400');

    // The run's survival lands near 82.5%, so the tip explains that rather than saying to spend less
    const result = engine.result({ ok: true, total: 99, survived: 78, rate: 78 / 99, years: 30, median: 1e6, lower: 0, endYear: '2075', endAge: 95, lowerOutYear: '2068', lowerOutAge: 88, monthly: 7400 });
    assert.equal(keys(result)[3], 'gordon.tip.optimized');
});

test('a Free visitor\'s Optimize is the teaser range, and says Plus finds the exact spend', () => {
    // Free isn't asked about an inheritance, the eighth answer
    const answers = UP_TO_SPENDING.filter((_, i) => i !== 7);
    const { engine } = converse(fakePage({ plus: false }), [...answers, { text: 'find the most I can safely spend' }]);
    assert.equal(engine.step, 'confirm');
    engine.reply({ choice: 'run' });
    const teaser = plain(engine.optimized({ kind: 'optimizedTeaser', low: 6500, high: 7000 }));
    assert.deepEqual(teaser.say.map((m) => m.key), ['gordon.optimized.teaser']);
    assert.equal(teaser.say[0].values.low, '$6,500');
    const result = engine.result({ ok: true, total: 99, survived: 90, rate: 90 / 99, years: 30, median: 1e6, lower: 1e5, endYear: '2075', endAge: 95, lowerOutYear: null, lowerOutAge: null, monthly: 6500 });
    assert.equal(keys(result)[3], 'gordon.tip.teaser');
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

test('"Show me an example" lists the household as bullets, fills every input at once, then optimizes', () => {
    const page = fakePage();
    const engine = create(page);
    page.play(engine.start());
    const turn = page.play(engine.reply({ choice: 'example' }));

    // One turn: the lead, the list (read after the inputs are filled), and Optimize - no questions
    assert.deepEqual(keys(turn), ['gordon.intro.example', 'summary', 'gordon.optimizing']);
    assert.equal(turn.lead, 1);
    assert.equal(turn.optimize, true);
    assert.equal(engine.step, 'running');
    assert.deepEqual(page.actions.map((a) => a.name), [
        'setBirthdate', 'setRetirementDate', 'setHorizon', 'setRuns', 'setFilingStatus', 'setTaxDeferred', 'setRoth',
        'setBrokerage', 'setInheritance', 'setSocialSecurityAmount', 'setSocialSecurityStart', 'setMix',
    ]);
    assert.equal(page.state.birthdate, '1967-06-01');
    assert.equal(page.state.taxDeferred, 900000);
    assert.equal(page.state.socialSecurityStart, '2034-06-01');

    const lines = plain(turn.say[1].lines()).map((line) => line.key);
    assert.ok(lines.includes('gordon.summary.married'));
    assert.ok(lines.includes('gordon.summary.optimize'));

    // Then it explains Optimize's answer and the run like any conversation, and Change something still works
    engine.optimized({ kind: 'optimized', withdrawalMonthly: 8997, recommendedAge: 67, annuity: null });
    const result = engine.result({ ok: true, total: 99, survived: 80, rate: 80 / 99, years: 34, median: 1e6, lower: 0, endYear: '2062', endAge: 94, lowerOutYear: '2058', lowerOutAge: 90, monthly: 8997 });
    assert.equal(keys(result)[3], 'gordon.tip.optimized');
    page.play(engine.reply({ choice: 'change' }));
    page.play(engine.reply({ choice: 'spending' }));
    page.play(engine.reply({ text: '8,000' }));
    assert.equal(engine.step, 'confirm');
});

test('a Free visitor\'s example leaves out the inheritance', () => {
    const page = fakePage({ plus: false });
    const engine = create(page);
    page.play(engine.start());
    page.play(engine.reply({ text: 'show me an example' }));
    assert.ok(!page.actions.some((a) => a.name === 'setInheritance'));
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
    const result = plain(engine.result({ ok: true, total: 99, survived: 79, rate: 79 / 99, years: 34, median: 2048411, lower: 0, endYear: '2062', endAge: 94, lowerOutYear: '2055', lowerOutAge: 87, monthly: 7000 }));
    assert.equal(result.say[0].values.rate, 80);
    assert.equal(result.say[1].values.amount, '$2,050,000');
    assert.equal(result.say[3].key, 'gordon.tip.inRange');
});

test('a run whose typical market runs out says when, not that $0 is left', () => {
    const { engine } = converse(fakePage(), [{ choice: 'start' }]);
    const result = engine.result({ ok: true, total: 99, survived: 33, rate: 1 / 3, years: 34, median: 0, lower: 0, endYear: '2062', endAge: 94, lowerOutYear: '2043', lowerOutAge: 75, medianOutYear: '2055', medianOutAge: 87, monthly: 9000 });
    assert.deepEqual(keys(result), ['gordon.result.survived', 'gordon.result.medianRunsOut', 'gordon.result.lowerRunsOut', 'gordon.tip.lower']);
});
