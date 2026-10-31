// Checks the spec-number rule the /feature skill uses at gate 1 (next-spec-number.mjs; run by ci.yml: node --test): real
// specs take the next number after the highest in use below 900, test runs the next from 900 up, gaps aren't refilled,
// names that aren't spec folders are ignored, and each range stops at its end rather than spill into the other.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { nextSpecNumber } from './next-spec-number.mjs';

test('no spec folders anywhere gives 001', () => {
    assert.equal(nextSpecNumber([]), '001');
});

test('the next real number is the highest in use plus one', () => {
    assert.equal(nextSpecNumber(['001-feature-skill-fixes', '002-gordon']), '003');
});

test('a folder seen on several branches counts once', () => {
    assert.equal(nextSpecNumber(['001-a', '001-a']), '002');
});

test('gaps are not refilled, so numbers keep the order features started', () => {
    assert.equal(nextSpecNumber(['001-a', '003-c']), '004');
});

test('names that are not spec folders are ignored', () => {
    assert.equal(nextSpecNumber(['readme.md', 'abc-x', '12-short', '0001-long']), '001');
});

test('test folders do not move real numbering', () => {
    assert.equal(nextSpecNumber(['001-a', '900-trial']), '002');
});

test('a test run starts at 900 and ignores real folders', () => {
    assert.equal(nextSpecNumber(['001-a', '002-b'], { test: true }), '900');
});

test('a test run takes the highest test number plus one', () => {
    assert.equal(nextSpecNumber(['900-t', '001-a'], { test: true }), '901');
});

test('real numbering stops at 899 rather than enter the test range', () => {
    assert.throws(() => nextSpecNumber(['899-z']), /899/);
});

test('test numbering stops at 999', () => {
    assert.throws(() => nextSpecNumber(['999-t'], { test: true }), /999/);
});
