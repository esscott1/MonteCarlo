// Checks the English/Spanish text stays complete and consistent (run by ci.yml: node --test).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { repo, readJson, readText, staticEntries, placeholders, SCRIPTS } from './i18n-lib.mjs';
import { buildSheet, readSheet } from './review-sheet.mjs';

const en = readJson('en.json');
const es = readJson('es.json');
const statics = staticEntries();
const staticKeys = new Set(statics.map((s) => s.key));

// Keys the scripts build from a prefix and a value (an enum name, a table column, a scenario id, a server field...)
const DYNAMIC_PREFIXES = ['order.', 'target.short.', 'target.phrase.', 'funding.', 'optimal.table.', 'runner.table.', 'scenario.', 'field.', 'server.'];

function literalScriptKeys() {
    const keys = new Set();
    for (const script of SCRIPTS) {
        for (const m of readText(script).matchAll(/\bt\('([a-zA-Z0-9_.]+)'/g)) keys.add(m[1]);
    }
    return keys;
}

// Every quoted string in the scripts that is a key - including ones picked by a condition, t(x ? 'a' : 'b')
function quotedScriptKeys() {
    const keys = new Set();
    for (const script of SCRIPTS) {
        for (const m of readText(script).matchAll(/'([a-zA-Z0-9_]+(?:\.[a-zA-Z0-9_]+)+)'/g)) keys.add(m[1]);
    }
    return keys;
}

test('every static page text has English in the HTML and a key no script also defines', () => {
    for (const { key, english, page } of statics) {
        assert.ok(english.length > 0, `${page}: ${key} has no English`);
        assert.ok(!(key in en), `${key} is static page text (in ${page}), so it shouldn't also be in en.json`);
    }
});

test('Spanish has exactly the keys the pages and scripts use', () => {
    const expected = new Set([...staticKeys, ...Object.keys(en)]);
    const missing = [...expected].filter((key) => !(key in es));
    const extra = Object.keys(es).filter((key) => !expected.has(key));
    assert.deepEqual(missing, [], 'missing from es.json');
    assert.deepEqual(extra, [], 'in es.json but not used');
});

test('no translation is empty', () => {
    for (const [key, value] of [...Object.entries(en), ...Object.entries(es)]) assert.ok(value.trim().length > 0, key);
});

test('Spanish keeps the same {placeholders} as the English', () => {
    const english = new Map([...statics.map((s) => [s.key, s.english]), ...Object.entries(en)]);
    for (const [key, spanish] of Object.entries(es)) {
        assert.deepEqual(placeholders(spanish), placeholders(english.get(key) ?? ''), key);
    }
});

test('every key the scripts use is defined', () => {
    for (const key of literalScriptKeys()) assert.ok(key in en, `t('${key}') has no English in en.json`);
});

test('every en.json key is used by a script', () => {
    const used = quotedScriptKeys();
    for (const key of Object.keys(en)) {
        assert.ok(used.has(key) || DYNAMIC_PREFIXES.some((prefix) => key.startsWith(prefix)), `${key} isn't used`);
    }
});

test('every validation message the server can send has an English template to translate it by', () => {
    const templates = Object.entries(en).filter(([key]) => key.startsWith('server.')).map(([, text]) => text);
    const asPattern = (text) => text.replace(/\{\w+\}/g, '{}');
    const known = new Set(templates.map(asPattern));
    const sources = ['RunRequest.cs', 'OptimalRequest.cs', 'ChangeRequest.cs'];
    let count = 0;
    for (const source of sources) {
        const code = readFileSync(join(repo, 'MonteCarloSimulation.Web', source), 'utf8');
        // A message is a "string" or an interpolated $"string", whose {expressions} may contain quotes of their own
        for (const m of code.matchAll(/errors\["\w+"\]\s*=\s*(\$?)"((?:\{[^}]*\}|[^"{])*)"/g)) {
            const message = m[1] ? m[2].replace(/\{[^}]+\}/g, '{}') : m[2];
            assert.ok(known.has(asPattern(message)), `${source}: no server.* template for "${m[2]}"`);
            count++;
        }
    }
    assert.ok(count >= 20, `found only ${count} messages; has the validation code moved?`);
});

test('the review sheet (docs/i18n-review.csv) is up to date', () => {
    assert.equal(readSheet(), buildSheet(), 'run node tools/i18n/review-sheet.mjs');
});

test('the Observe passphrase box stays in English', () => {
    const html = readText('optimal.html');
    const flyout = /<form id="observe-flyout"[\s\S]*?<\/form>/.exec(html)[0];
    assert.doesNotMatch(flyout, /data-i18n/);
});
