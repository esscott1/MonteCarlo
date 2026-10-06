// The agent-translation-update handler's work (run by .github/workflows/agent-translation-update.yml): applies a
// reviewer's Spanish corrections from the Translations page to es.json, exactly as typed. No AI is involved.
//
// The Jira story's description carries the edits as "translation-update:v1:" plus base64url of
// { "version": 1, "edits": { key: value } }, written by the web app (TranslationRules.EncodePayload). Every edit is
// checked again here against the es.json on master - the rules match TranslationRules.Check in the app - and the run
// fails without changing anything if any edit breaks them. Only the edited keys' lines change; the review sheet
// (docs/i18n-review.csv) is regenerated so the i18n tests pass on the pull request.
//
// Inputs (environment): DESCRIPTION (the story description). Writes es.json and the review sheet; the workflow commits.
import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export const PREFIX = 'translation-update:v1:';
export const MAX_EDITS = 100;
export const MAX_VALUE_LENGTH = 1000;
const ALLOWED_TAGS = ['<strong>', '</strong>', '<em>', '</em>'];

// Finds and decodes the payload. Jira may wrap or escape the description's text, so whitespace and backslashes inside
// the encoded block are ignored; base64url never contains either.
export function decodePayload(description) {
    const match = new RegExp(`${PREFIX}([A-Za-z0-9_\\-\\\\\\s]+)`).exec(description ?? '');
    if (!match) throw new Error(`The story has no ${PREFIX} block, so there are no edits to apply.`);
    const encoded = match[1].replace(/[\s\\]/g, '');
    let payload;
    try {
        payload = JSON.parse(Buffer.from(encoded, 'base64url').toString('utf8'));
    } catch {
        throw new Error('The edits block in the story is damaged and could not be read.');
    }
    if (payload?.version !== 1 || typeof payload.edits !== 'object' || payload.edits === null || Array.isArray(payload.edits)) {
        throw new Error('The edits block in the story is not a version 1 translation update.');
    }
    for (const [key, value] of Object.entries(payload.edits)) {
        if (typeof value !== 'string') throw new Error(`The edit for ${key} is not text.`);
    }
    return payload.edits;
}

const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
const tags = (text) => [...text.matchAll(/<[^<>]*>/g)].map((m) => m[0]);

// Why `value` can't replace `current`, or null if it can (the app's TranslationRules.Check)
export function checkEdit(value, current) {
    if (value.trim().length === 0) return "can't be empty.";
    if (value.length > MAX_VALUE_LENGTH) return `must be ${MAX_VALUE_LENGTH} characters or fewer.`;
    if (value === current) return 'is unchanged.';
    const expected = placeholders(current);
    if (JSON.stringify(placeholders(value)) !== JSON.stringify(expected)) {
        return `must keep the same {placeholders} as the current text (${expected.length ? expected.map((n) => `{${n}}`).join(', ') : 'none'}).`;
    }
    const currentTags = new Set(tags(current));
    for (const tag of tags(value)) {
        if (!ALLOWED_TAGS.includes(tag) && !currentTags.has(tag)) return `can only use <strong> and <em> for formatting (found ${tag}).`;
    }
    if (/[<>]/.test(value.replace(/<[^<>]*>/g, ''))) return "can't contain < or > outside a formatting tag.";
    return null;
}

// Every problem with the edits against the current Spanish, as "key: problem" lines; empty when all are fine
export function validate(edits, spanish) {
    const keys = Object.keys(edits);
    if (keys.length === 0) return ['There are no edits.'];
    if (keys.length > MAX_EDITS) return [`${keys.length} edits; at most ${MAX_EDITS} are allowed.`];
    return keys.flatMap((key) => {
        if (!Object.hasOwn(spanish, key)) return [`${key}: isn't a translation key on this site.`];
        const problem = checkEdit(edits[key], spanish[key]);
        return problem ? [`${key}: ${problem}`] : [];
    });
}

const escapeRegExp = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// es.json's text with only the edited keys' lines replaced, so its grouping and order stay as they are. Each key must
// sit on its own `  "key": "value"` line; the result is parsed back and compared, key by key, before it's returned.
export function applyEdits(text, edits) {
    let result = text;
    for (const [key, value] of Object.entries(edits)) {
        const line = new RegExp(`^(\\s*${escapeRegExp(JSON.stringify(key))}:\\s*)"(?:[^"\\\\]|\\\\.)*"(,?)(\\r?)$`, 'm');
        if (!line.test(result)) throw new Error(`Couldn't find ${key}'s line in es.json.`);
        result = result.replace(line, (match, start, comma, cr) => `${start}${JSON.stringify(value)}${comma}${cr}`);
    }
    const before = JSON.parse(text);
    const after = JSON.parse(result);
    const expected = { ...before, ...edits };
    if (JSON.stringify(Object.keys(after)) !== JSON.stringify(Object.keys(before))
        || Object.keys(expected).some((key) => after[key] !== expected[key])) {
        throw new Error('Applying the edits changed es.json in an unexpected way; nothing was written.');
    }
    return result;
}

async function main() {
    const repo = process.cwd();
    const esPath = join(repo, 'MonteCarloSimulation.Web', 'wwwroot', 'i18n', 'es.json');
    const summary = (text) => process.env.GITHUB_STEP_SUMMARY && appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${text}\n`);

    const edits = decodePayload(process.env.DESCRIPTION);
    const text = readFileSync(esPath, 'utf8');
    const problems = validate(edits, JSON.parse(text));
    if (problems.length > 0) {
        for (const problem of problems) console.log(`::error::${JSON.stringify(problem)}`);
        summary(`Refused: ${problems.length} edit(s) break the translation rules; es.json was not changed.`);
        process.exit(1);
    }

    writeFileSync(esPath, applyEdits(text, edits), 'utf8');
    // The review sheet lists every string, so it changes with es.json (the i18n tests require it to be current)
    const { buildSheet, sheetPath } = await import(pathToFileURL(join(repo, 'tools', 'i18n', 'review-sheet.mjs')).href);
    writeFileSync(sheetPath, buildSheet(), 'utf8');

    const count = Object.keys(edits).length;
    console.log(`Applied ${count} edit(s): ${Object.keys(edits).map((key) => JSON.stringify(key)).join(', ')}`);
    summary(`Applied ${count} Spanish edit(s) to es.json and regenerated the review sheet.`);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `count=${count}\n`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    main().catch((error) => {
        console.log(`::error::${JSON.stringify(error.message)}`);
        process.exit(1);
    });
}
