// Routes a Jira story dispatched by the "Trigger AI Agent on In Progress" rule to its handler workflow, by its label.
// Used by .github/workflows/agent-dispatcher.yml.
//
// The full label is a deterministic switch: it must exactly match one of HANDLERS - a label ONLY starting with "agent-"
// isn't enough. A story needs exactly one agent- label: none, or one with no handler, ends the run quietly (nothing to
// do); two or more is refused as ambiguous. The issue key must be SCRUM-<number>, since it becomes part of a branch name
// and a commit message.
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

// Add a label here only together with its handler workflow and the dispatcher job that calls it.
export const HANDLERS = ['agent-title-change'];

// The labels as the dispatcher receives them - toJSON(client_payload.labels): a JSON array (asJsonStringArray), a JSON
// string of comma-separated labels (join), or null when the rule didn't send any.
export function parseLabels(raw) {
    let value;
    try {
        value = JSON.parse(raw ?? 'null');
    } catch {
        value = raw;
    }
    const list = Array.isArray(value) ? value : typeof value === 'string' ? value.split(',') : [];
    return list.map((label) => String(label).trim()).filter((label) => label.length > 0);
}

// outcome: 'routed' (storyType is the handler's label), 'skipped' (nothing to do) or 'refused' (fail the run).
export function resolve({ issueKey, labels }) {
    if (!/^SCRUM-[0-9]+$/.test(issueKey ?? '')) {
        return { outcome: 'refused', storyType: '', reason: `The issue key ${JSON.stringify(issueKey ?? '')} is not a valid SCRUM-<number> key.` };
    }
    const agentLabels = [...new Set(labels.filter((label) => label.startsWith('agent-')))];
    if (agentLabels.length === 0) {
        return { outcome: 'skipped', storyType: '', reason: `${issueKey} has no agent- label (labels: ${JSON.stringify(labels)}), so there is nothing to do.` };
    }
    if (agentLabels.length > 1) {
        return { outcome: 'refused', storyType: '', reason: `${issueKey} has more than one agent- label (${JSON.stringify(agentLabels)}); a story must have exactly one.` };
    }
    const [label] = agentLabels;
    if (!HANDLERS.includes(label)) {
        return { outcome: 'skipped', storyType: '', reason: `${issueKey}'s label ${JSON.stringify(label)} has no handler, so there is nothing to do.` };
    }
    return { outcome: 'routed', storyType: label, reason: `${issueKey} routed to the ${label} handler.` };
}

// Run as a workflow step: ISSUE_KEY and LABELS_JSON in, story_type out (empty when there's nothing to do).
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    const labels = parseLabels(process.env.LABELS_JSON);
    const result = resolve({ issueKey: process.env.ISSUE_KEY, labels });

    // Untrusted text is only ever logged JSON-quoted and on one line, so it can't start a workflow command
    const reason = result.reason.replace(/[\r\n]+/g, ' ');
    console.log(`Labels received: ${JSON.stringify(labels)}`);
    console.log(`Outcome: ${result.outcome}. ${reason}`);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `story_type=${result.storyType}\n`);
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `**${result.outcome}**: ${reason}\n`);
    if (result.outcome === 'refused') {
        console.log(`::error::${reason}`);
        process.exit(1);
    }
}
