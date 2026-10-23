// Checks the Scenario runner's shared links (wwwroot/scenario-link.js; run by ci.yml: node --test): a request survives
// the round trip, a damaged link is refused whole, version 1's order never drifts, and the paid features a link's
// inputs use follow the server's Free-tier rules.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const script = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'MonteCarloSimulation.Web', 'wwwroot', 'scenario-link.js');
const context = { window: {}, btoa, atob };
vm.runInNewContext(readFileSync(script, 'utf8'), context);
const { encode, decode, featuresNeeded } = context.window.ScenarioLink;
// Objects made in the script's context have their own prototypes, so compare them as plain data
const plain = (value) => JSON.parse(JSON.stringify(value));

const NO_ANNUITY = { includeAnnuity: false, annuityMonthlyAmount: 0, annuityInvestment: 0, annuityType: 'spia', annuityStartDate: null, annuityTaxDeferred: false, annuityCola: false };

// The Scenario runner's defaults, as its submit handler sends them
const pageDefaults = {
    years: 40, iterations: 20, withdrawal: 96000, birthdate: '1969-07-07', retirementDate: '2027-01-01', filingStatus: 'single',
    initialTaxableBalance: 1000000, initialRothBasis: 15000, initialRothUnrealizedGain: 5000,
    initialBrokerageBasis: 100000, initialBrokerageUnrealizedGain: 300000, enableRothConversions: true,
    socialSecurityMonthlyAmount: 2750, socialSecurityStartDate: '2031-07-07', newMoney: 1000000, yearNewMoney: 10,
    annualStandardDeduction: 16000,
    stockAllocation: 0.6, bondAllocation: 0.3, cashAllocation: 0.1, stockReturn: 0.08, stockStdDev: 0.19,
    bondReturn: 0.045, bondStdDev: 0.04, cashReturn: 0.035, cashStdDev: 0.01, stockBondCorrelation: 0.1,
    ...NO_ANNUITY,
};

const defaults = {
    standardDeduction: { single: 16000, married: 32000 },
    assetMix: { stockReturn: 0.08, stockStdDev: 0.19, bondReturn: 0.045, bondStdDev: 0.04, cashReturn: 0.035, cashStdDev: 0.01, stockBondCorrelation: 0.1 },
    shares: { rothBasisShare: 1, brokerageGainShare: 0.5 },
};

// What a Free visitor's page sends with its defaults: no conversions or inheritance, the totals split by the Free shares
const freeRequest = { ...pageDefaults, enableRothConversions: false, newMoney: 0, initialRothBasis: 20000, initialRothUnrealizedGain: 0, initialBrokerageBasis: 200000, initialBrokerageUnrealizedGain: 200000 };

const withAnnuity = (annuity) => ({ ...pageDefaults, includeAnnuity: true, annuityMonthlyAmount: 2600, annuityInvestment: 400000, annuityType: 'spia', annuityStartDate: null, annuityTaxDeferred: false, annuityCola: false, ...annuity });

// Rebuilds a hash from version 1's values, so a test can damage one
const hashOf = (values) => `s=1.${btoa(values.join('~')).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')}`;
const valuesOf = (hash) => atob(hash.slice(4).replaceAll('-', '+').replaceAll('_', '/')).split('~');

test('a request survives the round trip, with and without an annuity', () => {
    for (const request of [
        pageDefaults,
        freeRequest,
        { ...pageDefaults, filingStatus: 'married', annualStandardDeduction: 32000, withdrawal: 8000.5 * 12, stockReturn: 0.0725, stockBondCorrelation: -0.25 },
        withAnnuity({}),
        withAnnuity({ annuityType: 'dia', annuityStartDate: '2029-03-15', annuityTaxDeferred: true }),
        withAnnuity({ annuityCola: true }),
    ]) {
        assert.deepEqual(plain(decode(encode(request))), request);
        assert.deepEqual(plain(decode(`#${encode(request)}`)), request);
    }
});

test('the COLA has one rate: a link writes 2%, and an older link with 1% or 3% still opens, at 2%', () => {
    const values = valuesOf(encode(withAnnuity({ annuityCola: true })));
    assert.equal(values[34], '2');
    const older = hashOf(values.map((v, i) => (i === 34 ? '3' : v)));
    assert.deepEqual(plain(decode(older)), withAnnuity({ annuityCola: true }));
    assert.equal(decode(hashOf(values.map((v, i) => (i === 34 ? 'x' : v)))), null);
});

test('a SPIA leaves out the start date it does not use', () => {
    const values = valuesOf(encode(withAnnuity({ annuityStartDate: '2030-01-01' })));
    assert.equal(values[31], '');
});

test('version 1 keeps its order: a link made today still opens the same scenario', () => {
    const pinned = 's=1.NDB-MjB-ODAwMH4xOTY5MDcwN34yMDI3MDEwMX5zfjEwMDAwMDB-MTUwMDB-NTAwMH4xMDAwMDB-MzAwMDAwfjF-Mjc1MH4yMDMxMDcwN34xMDAwMDAwfjEwfjE2MDAwfjYwfjMwfjEwfjh-MTl-NC41fjR-My41fjF-MC4xfjA';
    assert.equal(encode(pageDefaults), pinned);
    assert.deepEqual(plain(decode(pinned)), pageDefaults);
});

test('the default scenario makes a short link', () => {
    const link = `https://montecarlo.otsconsulting.ai/index.html#${encode(pageDefaults)}`;
    assert.ok(link.length < 350, `${link.length} characters`);
    assert.match(encode(withAnnuity({ annuityType: 'dia', annuityStartDate: '2029-03-15', annuityCola: true })), /^s=1\.[A-Za-z0-9_-]+$/);
});

test('a damaged link is refused whole', () => {
    const good = valuesOf(encode(pageDefaults));
    const damaged = (index, value) => hashOf(good.map((v, i) => (i === index ? value : v)));
    for (const hash of [
        '', '#', 's=1.', 's=2.' + encode(pageDefaults).slice(4), 'x=1.' + encode(pageDefaults).slice(4), 's=1.not*base64',
        encode(pageDefaults) + '%',
        hashOf(good.slice(0, -1)),                // a value missing
        hashOf([...good, '0']),                   // one too many
        hashOf([...good.slice(0, -1), '1']),      // an annuity said to be included but not there
        damaged(0, ''), damaged(0, 'abc'), damaged(0, '0x10'), damaged(2, 'Infinity'), damaged(2, '1e999'),
        damaged(3, '19690230'), damaged(3, '1969-07-07'), damaged(5, 'x'), damaged(11, 'true'),
    ]) {
        assert.equal(decode(hash), null, hash);
    }
    const annuity = valuesOf(encode(withAnnuity({ annuityType: 'dia', annuityStartDate: '2029-03-15' })));
    assert.equal(decode(hashOf(annuity.map((v, i) => (i === 31 ? '' : v)))), null, 'a DIA needs its start date');
    assert.equal(decode(hashOf(annuity.map((v, i) => (i === 30 ? 'q' : v)))), null, 'an unknown annuity type');
});

test('the Free defaults need no paid feature', () => {
    assert.deepEqual(plain(featuresNeeded(freeRequest, defaults)), []);
    assert.deepEqual(plain(featuresNeeded({ ...freeRequest, filingStatus: 'married', annualStandardDeduction: 32000 }, defaults)), []);
    // Without /api/me's shares, the split isn't judged
    assert.deepEqual(plain(featuresNeeded({ ...freeRequest, initialRothBasis: 1 }, { ...defaults, shares: null })), []);
});

test('each paid input names its feature', () => {
    const cases = [
        [{ enableRothConversions: true }, 'roth-conversions'],
        [{ newMoney: 1 }, 'inheritance'],
        [{ annualStandardDeduction: 16001 }, 'standard-deduction'],
        [{ filingStatus: 'married' }, 'standard-deduction'],
        [{ stockReturn: 0.09 }, 'custom-returns'],
        [{ cashStdDev: 0.02 }, 'custom-returns'],
        [{ stockBondCorrelation: 0 }, 'custom-returns'],
        [{ initialRothBasis: 15000, initialRothUnrealizedGain: 5000 }, 'account-basis-split'],
        [{ initialBrokerageBasis: 100000, initialBrokerageUnrealizedGain: 300000 }, 'account-basis-split'],
    ];
    for (const [change, feature] of cases) {
        assert.deepEqual(plain(featuresNeeded({ ...freeRequest, ...change }, defaults)), [feature], JSON.stringify(change));
    }
    const annuity = { ...freeRequest, ...withAnnuity({}), ...pick(freeRequest, ['enableRothConversions', 'newMoney', 'initialRothBasis', 'initialRothUnrealizedGain', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain']) };
    assert.deepEqual(plain(featuresNeeded(annuity, defaults)), ['annuity']);
    assert.deepEqual(plain(featuresNeeded({ ...annuity, annuityCola: true }, defaults)), ['annuity', 'annuity-cola']);
    // The page's own defaults use Plus inputs: conversions, the inheritance and a basis split of their own
    assert.deepEqual(plain(featuresNeeded(pageDefaults, defaults)), ['roth-conversions', 'inheritance', 'account-basis-split']);
});

function pick(source, names) {
    return Object.fromEntries(names.map((name) => [name, source[name]]));
}
