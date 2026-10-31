// The spec number for a new feature, chosen by the /feature skill at gate 1 (see .claude/skills/feature/SKILL.md).
// A worktree only sees the specs merged to master, so Spec Kit's own scan would give two features in progress the
// same number; this reads origin/master and every origin/feature/* branch instead. Real specs are 001-899, test runs
// 900-999. Read-only: run `git fetch origin --prune` first; it never fetches, commits, pushes or writes files.
//
//   node tools/spec-number/next-spec-number.mjs [--test]   prints e.g. 002 (or 900 with --test)
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const SPEC_FOLDER = /^(\d{3})-/;
const RANGES = { real: { first: 1, last: 899 }, test: { first: 900, last: 999 } };

// The next number after the highest one in use in the range (gaps aren't refilled), as three digits.
export function nextSpecNumber(folderNames, { test = false } = {}) {
    const range = test ? RANGES.test : RANGES.real;
    let highest = range.first - 1;
    for (const name of folderNames) {
        const match = SPEC_FOLDER.exec(name);
        if (!match) continue;
        const number = Number(match[1]);
        if (number >= range.first && number <= range.last && number > highest) highest = number;
    }
    const next = highest + 1;
    if (next > range.last) {
        throw new Error(`No ${test ? 'test' : 'real'} spec number is left after ${range.last}; the owner decides what comes next.`);
    }
    return String(next).padStart(3, '0');
}

function git(args, what) {
    try {
        return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    } catch (error) {
        throw new Error(`Couldn't read ${what}: ${(error.stderr || error.message).trim()}`);
    }
}

// Spec folder names on origin/master and every origin/feature/* branch.
export function specFoldersOnBranches() {
    const refs = git(['for-each-ref', '--format=%(refname:short)', 'refs/remotes/origin/master', 'refs/remotes/origin/feature'], 'the branch list')
        .split('\n').map((line) => line.trim()).filter(Boolean);
    if (!refs.includes('origin/master')) throw new Error("Couldn't find origin/master; run `git fetch origin --prune` first.");
    const names = [];
    for (const ref of refs) {
        const listed = git(['ls-tree', '-d', '--name-only', ref, 'specs/'], ref);
        for (const line of listed.split('\n')) {
            const path = line.trim();
            if (path.startsWith('specs/')) names.push(path.slice('specs/'.length));
        }
    }
    return names;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    try {
        console.log(nextSpecNumber(specFoldersOnBranches(), { test: process.argv.includes('--test') }));
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
