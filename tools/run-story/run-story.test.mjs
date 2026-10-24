// Checks the run in plain words (wwwroot/run-story.js; run by ci.yml: node --test), which the Scenario runner's results
// summary shows under its first two lines and Gordon says after a run: the facts it reads from a run, and its sentences.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const script = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'MonteCarloSimulation.Web', 'wwwroot', 'run-story.js');
const context = { window: {}, I18n: { translateServerMessage: (m) => `translated: ${m}` } };
vm.runInNewContext(readFileSync(script, 'utf8'), context);
const { yearShortfall, facts, survival, markets, tip } = context.window.RunStory;
const plain = (value) => JSON.parse(JSON.stringify(value));

// A year as /api/run reports it, with only what the story reads
const year = (calendarYear, ageInYear, values = {}) => ({
    calendarYear, ageInYear, withdrawal: 60000, irmaaSurcharge: 0, socialSecurityIncome: 0, socialSecurityTax: 0,
    annuityIncome: 0, annuityTax: 0, ...values,
});

// Ten runs: `failed` of them fail, the first in 2040, each later one a year later; the rest end with growing balances
function view(failed) {
    const runs = [];
    for (let i = 0; i < 10; i++) {
        if (i < failed) {
            const years = Array.from({ length: 14 + i }, (_, y) => year(2027 + y, 58 + y));
            years[years.length - 1] = year(2027 + 13 + i, 71 + i, { socialSecurityIncome: 36000, socialSecurityTax: 6000 });
            runs.push({ failed: true, endingBalance: 0, years });
        } else {
            runs.push({ failed: false, endingBalance: 1e6 + i * 1e5, years: [] });
        }
    }
    return {
        kind: 'results',
        parameters: { iterations: 10, years: 30, birthdate: '1969-07-07', withdrawal: 72000 },
        output: {
            result: { runs, outOfMoneyCount: failed },
            balanceBands: [{ date: '2027-01-01', middle: 1e6, lower: 1e6 }, { date: '2057-01-01', middle: 2_048_411, lower: 104_000 }],
        },
    };
}

test('a year\'s shortfall is its need after Social Security and the annuity, after their taxes', () => {
    const s = plain(yearShortfall(year(2045, 76, { irmaaSurcharge: 2000, socialSecurityIncome: 36000, socialSecurityTax: 6000, annuityIncome: 12000, annuityTax: 2000 })));
    assert.deepEqual(s, { need: 62000, ss: 30000, annuity: 10000, net: 40000, short: 22000, share: 22000 / 62000 });
});

test('when every market lasts, it says what\'s left in a typical and a weak market', () => {
    const f = facts(view(0));
    assert.equal(f.ok, true);
    assert.equal(f.rate, 1);
    assert.deepEqual(plain(markets(f)), [
        { key: 'story.median', values: { amount: '$2,050,000', year: '2057', age: 87 } },
        { key: 'story.lowerHolds', values: { amount: '$100,000', year: '2057' } },
    ]);
    assert.deepEqual(plain(survival(f, { counts: true })), { key: 'story.allSurvived', values: { total: 10, years: 30 } });
});

test('when the weakest markets fall short, it says from when and by how much, not that the money is gone', () => {
    // Two of ten fail: the weakest 10% is the run that fails first, in 2040 at 71
    const f = facts(view(2));
    const [typical, weak] = plain(markets(f));
    assert.equal(typical.key, 'story.median');
    assert.deepEqual(weak, { key: 'story.lowerShort', values: { year: 2040, age: 71, amount: '$30,000', percent: 50 } });
    assert.deepEqual(plain(survival(f, { counts: false })), { key: 'story.survivedRate', values: { years: 30, rate: 80 } });
});

test('when a typical market falls short too, both say so', () => {
    const f = facts(view(6));
    assert.deepEqual(plain(markets(f)).map((m) => m.key), ['story.medianShort', 'story.lowerShort']);
    assert.equal(f.medianShortYear, 2044);
});

test('the tip compares the survival, as shown, with the 80-85% range', () => {
    const at = (rate) => tip({ rate, monthly: 6000 }).key;
    assert.equal(at(0.79), 'story.tip.lower');
    assert.equal(at(0.795), 'story.tip.inRange');
    assert.equal(at(0.89), 'story.tip.inRange');
    assert.equal(at(0.9), 'story.tip.more');
    assert.deepEqual(plain(tip({ rate: 0.5, monthly: 6000 }).values), { amount: '$6,000' });
});

test('a run that didn\'t happen is reported as such, with the server\'s first message translated', () => {
    assert.deepEqual(plain(facts({ kind: 'errors', errors: { years: ['Years must be positive.'] } })), { ok: false, message: 'translated: Years must be positive.' });
    assert.deepEqual(plain(facts({ kind: 'failed', message: 'offline' })), { ok: false, message: 'offline' });
});
