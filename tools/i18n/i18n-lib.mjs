// Shared by review-sheet.mjs and i18n.test.mjs: where the site's English and Spanish text lives.
//   - Static page text: English in the HTML, marked data-i18n="key" (element content) or data-i18n-title /
//     -placeholder / -aria-label="key" (that attribute).
//   - Text the scripts build: English in i18n/en.json, used through t('key', ...).
//   - Spanish for both: i18n/es.json.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
export const wwwroot = join(repo, 'MonteCarloSimulation.Web', 'wwwroot');
export const PAGES = ['splash.html', 'optimal.html', 'index.html', 'model-info.html'];
export const SCRIPTS = ['optimal.js', 'app.js', 'quota.js', 'model-info.js'];

export const readJson = (name) => JSON.parse(readFileSync(join(wwwroot, 'i18n', name), 'utf8'));
export const readText = (name) => readFileSync(join(wwwroot, name), 'utf8');
export const readLab = () => JSON.parse(readFileSync(join(wwwroot, 'model-info', 'strategy-lab.json'), 'utf8'));

// Model Info translates the Strategy Lab's English labels under lab.label.<slug> and its definitions under
// lab.def.<code> (model-info.js: slug, labLabel, labDefinition) - the same slug as here
export const slug = (english) => english.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');

// Every label and definition from the lab's data that Model Info shows: [{ key, english }]
export function labTexts(s) {
    const labels = new Set();
    const addSlice = (text) => text.split(': ').forEach((part) => labels.add(part));
    s.definitions.forEach((d) => labels.add(d.name));
    s.slices.forEach((sl) => {
        labels.add(sl.dimension);
        sl.bins.forEach((b) => labels.add(b.label));
    });
    (s.funding?.slices ?? []).filter((sl) => sl.dimension.startsWith('Retires before')).forEach((sl) => sl.bins.forEach((b) => labels.add(b.label)));
    s.subsets.forEach((x) => labels.add(x.label));
    s.directQuestion.forEach((x) => labels.add(x.label));
    (s.orderChoice ?? []).forEach((x) => { labels.add(x.name); addSlice(x.worstSlice); });
    (s.conversionChoice?.rows ?? []).forEach((x) => { labels.add(x.name); addSlice(x.worstSlice); });
    return [
        ...[...labels].map((english) => ({ key: `lab.label.${slug(english)}`, english })),
        ...s.definitions.map((d) => ({ key: `lab.def.${d.code}`, english: d.definition })),
    ];
}

// Static keys and their English, in page order: [{ key, english, page }]
export function staticEntries() {
    const entries = [];
    for (const page of PAGES) {
        const html = readText(page);
        for (const m of html.matchAll(/<(\w+)\b[^>]*\sdata-i18n="([^"]+)"[^>]*>([\s\S]*?)<\/\1>/g)) {
            entries.push({ key: m[2], english: m[3].trim(), page });
        }
        for (const m of html.matchAll(/<[^>]*\sdata-i18n-(title|placeholder|aria-label)="([^"]+)"[^>]*>/g)) {
            const value = new RegExp(`\\s${m[1]}="([^"]*)"`).exec(m[0]);
            entries.push({ key: m[2], english: value ? value[1] : '', page });
        }
    }
    return entries;
}

// Every {placeholder} name in a template, sorted
export const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
