// Tests for the dispatcher's routing (run by ci.yml: node --test .github/scripts/)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { HANDLERS, parseLabels, resolve } from './resolve-story-type.mjs';

test('labels arrive as a JSON array, a comma-separated string, or not at all', () => {
    assert.deepEqual(parseLabels('["agent-title-change","frontend"]'), ['agent-title-change', 'frontend']);
    assert.deepEqual(parseLabels('"agent-title-change, frontend"'), ['agent-title-change', 'frontend']);
    assert.deepEqual(parseLabels('agent-title-change'), ['agent-title-change']);
    assert.deepEqual(parseLabels('null'), []);
    assert.deepEqual(parseLabels(undefined), []);
    assert.deepEqual(parseLabels('[]'), []);
});

test('a supported label routes to its handler; other labels are ignored', () => {
    const result = resolve({ issueKey: 'SCRUM-60', labels: ['frontend', 'agent-title-change'] });
    assert.equal(result.outcome, 'routed');
    assert.equal(result.storyType, 'agent-title-change');
});

test('translation updates route to their own handler', () => {
    const result = resolve({ issueKey: 'SCRUM-61', labels: ['agent-translation-update'] });
    assert.equal(result.outcome, 'routed');
    assert.equal(result.storyType, 'agent-translation-update');
    assert.equal(resolve({ issueKey: 'SCRUM-61', labels: ['agent-translation-update', 'agent-title-change'] }).outcome, 'refused');
});

test('the label must match exactly - starting with agent- is not enough', () => {
    for (const label of ['agent-title-change-now', 'agent-title', 'agent-delete-everything', 'Agent-Title-Change', 'agent-translation', 'agent-translation-updates']) {
        const result = resolve({ issueKey: 'SCRUM-60', labels: [label] });
        assert.equal(result.storyType, '', label);
        assert.notEqual(result.outcome, 'routed', label);
    }
});

test('no agent- label is a quiet no-op', () => {
    const result = resolve({ issueKey: 'SCRUM-60', labels: ['frontend'] });
    assert.equal(result.outcome, 'skipped');
    assert.equal(resolve({ issueKey: 'SCRUM-60', labels: [] }).outcome, 'skipped');
});

test('an agent- label with no handler is a quiet no-op', () => {
    const result = resolve({ issueKey: 'SCRUM-60', labels: ['agent-something-new'] });
    assert.equal(result.outcome, 'skipped');
    assert.match(result.reason, /no handler/);
});

test('two agent- labels are refused as ambiguous', () => {
    const result = resolve({ issueKey: 'SCRUM-60', labels: ['agent-title-change', 'agent-something-new'] });
    assert.equal(result.outcome, 'refused');
});

test('the same label twice is still one label', () => {
    assert.equal(resolve({ issueKey: 'SCRUM-60', labels: ['agent-title-change', 'agent-title-change'] }).outcome, 'routed');
});

test('the issue key must be SCRUM-<number>', () => {
    for (const key of ['', 'SCRUM-', 'scrum-60', 'SCRUM-60; rm -rf /', 'SCRUM-60\n::set-output', 'OTHER-1']) {
        assert.equal(resolve({ issueKey: key, labels: ['agent-title-change'] }).outcome, 'refused', JSON.stringify(key));
    }
});

test('every handler label starts with agent- (the Jira rule only dispatches those)', () => {
    for (const label of HANDLERS) assert.match(label, /^agent-[a-z-]+$/);
});
