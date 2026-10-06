// Tests for check-handler-work.sh against real temporary git repositories (run by ci.yml: node --test). Each test sets
// up master and the handler's feature branch the way agent-prepare does, then does something a handler might do.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = join(dirname(fileURLToPath(import.meta.url)), 'check-handler-work.sh');
const ALLOWED = 'MonteCarloSimulation.Web/wwwroot/*.html';

function repo() {
    const dir = mkdtempSync(join(tmpdir(), 'handler-check-'));
    const git = (...args) => execFileSync('git', args, { cwd: dir, encoding: 'utf8' }).trim();
    const write = (path, text) => {
        mkdirSync(join(dir, dirname(path)), { recursive: true });
        writeFileSync(join(dir, path), text);
    };
    git('init', '-q', '-b', 'master');
    git('config', 'user.email', 'test@example.com');
    git('config', 'user.name', 'Test');
    write('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>Scenario runner</h1>');
    git('add', '.');
    git('commit', '-q', '-m', 'initial');
    const masterSha = git('rev-parse', 'HEAD');
    git('checkout', '-q', '-b', 'feature/SCRUM-1-agent-20261006-0000');
    const commit = (path, text) => { write(path, text); git('add', '.'); git('commit', '-q', '-m', 'change'); };
    const check = (env = {}) => spawnSync('bash', [script], {
        cwd: dir, encoding: 'utf8',
        env: { ...process.env, BRANCH: 'feature/SCRUM-1-agent-20261006-0000', MASTER_SHA: masterSha, LOCAL_MASTER_SHA: masterSha, ALLOWED_PATHS: ALLOWED, ...env },
    });
    return { dir, git, write, commit, check, masterSha, done: () => rmSync(dir, { recursive: true, force: true }) };
}

function refused(result, pattern) {
    assert.notEqual(result.status, 0, result.stdout + result.stderr);
    assert.match(result.stdout, pattern);
}

test('a committed change to an allowed page passes', () => {
    const r = repo();
    r.commit('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>New title</h1>');
    const result = r.check();
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.match(result.stdout, /Checks passed/);
    r.done();
});

test('committing to master is refused', () => {
    const r = repo();
    r.git('checkout', '-q', 'master');
    r.commit('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>Straight to master</h1>');
    refused(r.check(), /left feature\/SCRUM-1/);
    r.git('checkout', '-q', 'feature/SCRUM-1-agent-20261006-0000');
    refused(r.check(), /local master branch was changed/);
    r.done();
});

test('master as the branch to push is refused', () => {
    const r = repo();
    r.git('checkout', '-q', 'master');
    refused(r.check({ BRANCH: 'master' }), /No feature branch/);
    r.done();
});

test('switching to another branch is refused', () => {
    const r = repo();
    r.git('checkout', '-q', '-b', 'somewhere-else');
    r.commit('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>Elsewhere</h1>');
    refused(r.check(), /left feature\/SCRUM-1/);
    r.done();
});

test('a change under .github is refused, even if a pattern would allow it', () => {
    const r = repo();
    r.commit('.github/workflows/ci.yml', 'name: hijacked');
    refused(r.check({ ALLOWED_PATHS: '*' }), /under \.github/);
    r.done();
});

test('a change outside the scope is refused', () => {
    const r = repo();
    r.commit('MonteCarloSimulation.Web/Program.cs', '// sneaky');
    refused(r.check(), /outside its scope/);
    r.done();
});

test('uncommitted changes are refused', () => {
    const r = repo();
    r.commit('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>New title</h1>');
    r.write('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>Not committed</h1>');
    refused(r.check(), /uncommitted changes/);
    r.done();
});

test('no commits at all is refused', () => {
    const r = repo();
    refused(r.check(), /made no commits/);
    r.done();
});

test('a branch that does not build on master is refused', () => {
    const r = repo();
    r.git('checkout', '-q', '--orphan', 'unrelated');
    r.git('rm', '-q', '-rf', '.');
    r.commit('MonteCarloSimulation.Web/wwwroot/index.html', '<h1>Unrelated history</h1>');
    r.git('branch', '-q', '-D', 'feature/SCRUM-1-agent-20261006-0000');
    r.git('branch', '-q', '-m', 'feature/SCRUM-1-agent-20261006-0000');
    refused(r.check(), /doesn't build on master/);
    r.done();
});
