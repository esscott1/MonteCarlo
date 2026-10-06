// Tests for apply-translation-update.mjs (run by ci.yml: node --test).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { applyEdits, checkEdit, decodePayload, validate, MAX_EDITS, PREFIX } from './apply-translation-update.mjs';

// The web app's encoding of these two edits (TranslationTests.EncodePayload_MatchesTheWorkflowScript pins the same
// string), so the app and this script can't drift apart
const EDITS = {
    'info.case.runsOut': 'Se queda sin dinero <strong>accesible</strong>',
    'optimal.startSs': 'Empiece el Seguro Social a los {age} años ({date}), "citado" — {benefit}/mes, {total} en total.',
};
const ENCODED = 'translation-update:v1:eyJ2ZXJzaW9uIjoxLCJlZGl0cyI6eyJpbmZvLmNhc2UucnVuc091dCI6IlNlIHF1ZWRhIHNpbiBkaW5lcm8gPHN0cm9uZz5hY2Nlc2libGU8L3N0cm9uZz4iLCJvcHRpbWFsLnN0YXJ0U3MiOiJFbXBpZWNlIGVsIFNlZ3VybyBTb2NpYWwgYSBsb3Mge2FnZX0gYcOxb3MgKHtkYXRlfSksIFwiY2l0YWRvXCIg4oCUIHtiZW5lZml0fS9tZXMsIHt0b3RhbH0gZW4gdG90YWwuIn19';

const encode = (payload) => PREFIX + Buffer.from(JSON.stringify(payload), 'utf8').toString('base64url');

test('decodes the edits block the web app writes, wherever it sits in the description', () => {
    assert.deepEqual(decodePayload(ENCODED), EDITS);
    // As Jira automation may render the story: paragraphs, then the code block in wiki markup
    assert.deepEqual(decodePayload(`Spanish translation update.\nChanges (2):\n{code}\n${ENCODED}\n{code}`), EDITS);
});

test('tolerates line wrapping and escaping inside the block', () => {
    const wrapped = ENCODED.slice(0, 60) + '\n' + ENCODED.slice(60, 120) + '\\' + ENCODED.slice(120);
    assert.deepEqual(decodePayload(wrapped), EDITS);
});

test('refuses a missing, damaged or unexpected block', () => {
    assert.throws(() => decodePayload('Just a description'), /no translation-update:v1: block/);
    assert.throws(() => decodePayload(`${PREFIX}not-json`), /damaged/);
    assert.throws(() => decodePayload(encode({ version: 2, edits: {} })), /version 1/);
    assert.throws(() => decodePayload(encode({ version: 1, edits: ['a'] })), /version 1/);
    assert.throws(() => decodePayload(encode({ version: 1, edits: { a: 5 } })), /not text/);
});

test('accepts a real correction', () => {
    assert.equal(checkEdit('Hola {name}, <strong>bienvenido</strong>', 'Hola {name}'), null);
    assert.equal(checkEdit('Usa <code>dotnet</code> y <em>listo</em>', 'Usa <code>dotnet</code>'), null);
});

test('refuses edits that break the rules', () => {
    assert.match(checkEdit('   ', 'Hola'), /empty/);
    assert.match(checkEdit('x'.repeat(1001), 'Hola'), /1000 characters/);
    assert.match(checkEdit('Hola', 'Hola'), /unchanged/);
    assert.match(checkEdit('Hola', 'Hola {name}'), /placeholders.*\{name\}/);
    assert.match(checkEdit('Hola {nombre}', 'Hola {name}'), /placeholders/);
    assert.match(checkEdit('Hola {name} {name}', 'Hola {name}'), /placeholders/);
    assert.match(checkEdit('<script>alert(1)</script>', 'Hola'), /<strong> and <em>.*<script>/);
    assert.match(checkEdit('<a href="https://example.com">aquí</a>', 'Hola'), /found <a href/);
    assert.match(checkEdit('<strong onclick="x()">Hola</strong>', 'Hola'), /found <strong onclick/);
    assert.match(checkEdit('5 < 6', 'Hola'), /< or >/);
});

test('validate reports unknown keys and every broken edit, and caps the count', () => {
    const spanish = { 'a.b': 'Hola {name}', 'c.d': 'Adiós' };
    assert.deepEqual(validate({ 'a.b': 'Buenas {name}' }, spanish), []);
    assert.deepEqual(validate({ 'x.y': 'Nuevo', 'a.b': 'Buenas' }, spanish), [
        "x.y: isn't a translation key on this site.",
        'a.b: must keep the same {placeholders} as the current text ({name}).',
    ]);
    assert.deepEqual(validate({ __proto__: null, ['__proto__']: 'x' }, spanish), ["__proto__: isn't a translation key on this site."]);
    assert.deepEqual(validate({}, spanish), ['There are no edits.']);
    const many = Object.fromEntries(Array.from({ length: MAX_EDITS + 1 }, (_, i) => [`k${i}`, 'v']));
    assert.match(validate(many, spanish)[0], /at most 100/);
});

const ES = [
    '{',
    '  "a.b": "Hola {name}",',
    '  "c.d": "Adiós",',
    '',
    '  "e.f": "Último"',
    '}',
    '',
].join('\n');

test('changes only the edited lines, keeping grouping, order and the rest byte for byte', () => {
    const result = applyEdits(ES, { 'c.d': 'Hasta luego "amigo" \\ $1 $&', 'e.f': 'Fin' });
    const changed = result.split('\n').filter((line, i) => line !== ES.split('\n')[i]);
    assert.deepEqual(changed, ['  "c.d": "Hasta luego \\"amigo\\" \\\\ $1 $&",', '  "e.f": "Fin"']);
    assert.equal(JSON.parse(result)['c.d'], 'Hasta luego "amigo" \\ $1 $&');
});

test('keeps CRLF line endings', () => {
    const crlf = ES.replace(/\n/g, '\r\n');
    const result = applyEdits(crlf, { 'a.b': 'Buenas {name}' });
    assert.equal(result, crlf.replace('"Hola {name}"', '"Buenas {name}"'));
});

test('refuses a key whose line it can\'t find', () => {
    assert.throws(() => applyEdits(ES, { 'z.z': 'Nada' }), /Couldn't find z.z's line/);
});
