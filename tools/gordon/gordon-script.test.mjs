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

test('confirming runs the plan, and the result reads the run', () => {
    const page = fakePage();
    const { engine } = converse(page, [
        { choice: 'start' }, { text: 'June 1980' }, { text: 'at 65' }, { text: 'single' }, { text: '1.2m' }, { text: 'none' },
        { text: '300k' }, { choice: 'none' }, { choice: 'unknown' }, { choice: 'unknown' }, { text: 'growth' }, { choice: 'suggested' },
    ]);
    const running = engine.reply({ choice: 'run' });
    assert.equal(running.run, true);
    assert.deepEqual(keys(running), ['gordon.running']);

    const result = engine.result({ ok: true, total: 99, survived: 83, rate: 83 / 99, years: 30, median: 2.1e6, lower: 0, endYear: '2075', endAge: 95, lowerOutYear: '2068', lowerOutAge: 88, monthly: 9000 });
    assert.deepEqual(keys(result), ['gordon.result.survived', 'gordon.result.median', 'gordon.result.lowerRunsOut', 'gordon.tip.inRange']);
    assert.deepEqual(plain(result.choices.map((c) => c.value)), ['details', 'change', 'restart']);
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

test('"Show me an example" answers every question itself, through to the run', () => {
    const page = fakePage();
    const engine = create(page);
    let turn = page.play(engine.start());
    turn = page.play(engine.reply({ choice: 'example' }));
    for (let guard = 0; guard < 20 && !turn.run; guard++) {
        const answer = engine.exampleAnswer();
        assert.ok(answer, `no example answer at ${engine.step}`);
        turn = page.play(engine.reply(plain(answer)));
        assert.ok(!keys(turn).some((key) => key.startsWith('gordon.retry')), `the example's answer at ${engine.step} wasn't understood`);
    }
    assert.equal(turn.run, true);
    assert.equal(engine.exampleAnswer(), null);
    assert.equal(page.state.filingStatus, 'married');
    // The suggested spend: 85% of 4% of $1.3M a year plus $4,800 over the 34 years less the 6.4 before it starts
    assert.equal(page.state.spending, 7000);
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
