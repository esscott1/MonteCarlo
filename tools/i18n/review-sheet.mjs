// Writes docs/i18n-review.csv: every translated string side by side for a Spanish reviewer - key, where it appears,
// English, current Spanish, and an empty column for their correction. Opens in Excel or Google Sheets.
//   node tools/i18n/review-sheet.mjs           regenerate the sheet
//   node tools/i18n/review-sheet.mjs --check   fail if the committed sheet is out of date (i18n.test.mjs does this too)
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { repo, readJson, staticEntries } from './i18n-lib.mjs';

export const sheetPath = join(repo, 'docs', 'i18n-review.csv');

// The part of the site a script-built key belongs to, from its prefix
function scriptArea(key) {
    if (key.startsWith('optimal.')) return 'optimal.js (landing page results)';
    if (key.startsWith('runner.') || key.startsWith('flyout.') || key.startsWith('quota.')) return 'app.js (Scenario runner)';
    if (key.startsWith('server.') || key.startsWith('field.')) return 'server messages and field names';
    return 'shared';
}

const cell = (value) => `"${String(value ?? '').replace(/"/g, '""')}"`;

// The sheet's text: a byte-order mark (so Excel opens the accents correctly), then one LF-terminated line per row
export function buildSheet() {
    const en = readJson('en.json');
    const es = readJson('es.json');
    const rows = [];
    const seen = new Set();
    for (const { key, english, page } of staticEntries()) {
        if (seen.has(key)) continue;
        seen.add(key);
        rows.push([key, page, english, es[key] ?? '', '']);
    }
    for (const [key, english] of Object.entries(en)) {
        if (seen.has(key)) continue;
        seen.add(key);
        rows.push([key, scriptArea(key), english, es[key] ?? '', '']);
    }
    const header = ['Key', 'Where', 'English', 'Spanish', 'Correction (leave empty if the Spanish is right)'];
    return '﻿' + [header, ...rows].map((row) => row.map(cell).join(',')).join('\n') + '\n';
}

// The committed sheet, with line endings as generated (git may check it out with CRLF on Windows)
export function readSheet() {
    return existsSync(sheetPath) ? readFileSync(sheetPath, 'utf8').split('\r\n').join('\n') : '';
}

if (process.argv[1] && process.argv[1].endsWith('review-sheet.mjs')) {
    const sheet = buildSheet();
    if (process.argv.includes('--check')) {
        if (readSheet() !== sheet) {
            console.error('docs/i18n-review.csv is out of date: run node tools/i18n/review-sheet.mjs');
            process.exit(1);
        }
        console.log('docs/i18n-review.csv is up to date.');
    } else {
        writeFileSync(sheetPath, sheet, 'utf8');
        console.log(`Wrote ${sheetPath}`);
    }
}
