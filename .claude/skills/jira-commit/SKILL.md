---
name: jira-commit
description: Commit and push a code change made for a Jira issue in this repo, embedding the issue key (e.g. SCRUM-12) in the commit message per this project's convention, linking the commit back to the Jira issue both ways (a comment on the Jira issue pointing at the commit, and a comment on the GitHub commit pointing at the issue), and transitioning the Jira issue to "In Review". Use this whenever the user says things like "commit this for SCRUM-12", "commit and push this Jira work", "wrap up this ticket", "finish up SCRUM-<id>", or when a code change was clearly made to satisfy a specific Jira issue and is ready to be saved. Also trigger for "link this commit to Jira" or "update the Jira issue with the commit".
---

# Jira-linked commit workflow

This project ties every commit for a Jira-driven change back to its issue key, so `git log` and the Jira issue can each point at the other. Use this any time you're about to commit code written to satisfy a specific `SCRUM-<id>` issue in the `ericscott411.atlassian.net` site (GitHub repo: `esscott1/MonteCarlo`).

## 1. Determine the issue key

- If the user names it directly ("SCRUM-12"), use that.
- Otherwise check the current branch name — branches follow `feature/SCRUM-<id>-short-description` or `fix/SCRUM-<id>-short-description` — and pull `<id>` from there.
- If neither gives you a key, ask which Jira issue this change belongs to. Don't guess — a wrong key silently mislinks the commit to the wrong ticket.

## 2. Review what's changing

Run `git status` and `git diff` before touching anything. Never commit blind. If you see files that look unrelated to this issue (stray scratch files, anything that might be a secret), flag it to the user instead of silently including or excluding it.

## 3. Stage and commit

- Stage only the files relevant to this change (`git add <specific files>`) — not `git add -A`.
- Commit message format: `SCRUM-<id> <clear description of changes>` — one line (a short body is fine if the change needs more explanation), focused on *why* the change was made, matching the voice of this repo's existing history (`git log --oneline` to check tone if unsure).
- Create a new commit rather than amending, unless the user explicitly asks to amend.

## 4. Push to remote

Every commit made through this workflow gets pushed — that's standing policy for this project, so there's no need to ask for confirmation each time the way you would for an ordinary push. Once the commit is made: `git push origin <branch>` (add `-u` the first time a new branch is pushed). If the push fails (e.g. the remote has diverged), stop and surface that to the user rather than force-pushing.

## 5. Link the commit back to Jira — do both directions

Putting the issue key in the commit message is necessary but not sufficient for Jira's "Development" panel to auto-show the commit — that only happens if this GitHub repo has the "GitHub for Jira" integration connected to this Jira site, which hasn't been confirmed for this project. Rather than assume it works, close the loop explicitly both ways every time:

1. **Comment on the Jira issue** (via the Atlassian MCP tools) with the commit's short SHA, a link to it (`https://github.com/esscott1/MonteCarlo/commit/<full-sha>`), the branch name, and a one-line summary of what the commit does.
2. **Comment on the GitHub commit itself** (`gh api repos/esscott1/MonteCarlo/commits/<full-sha>/comments -f body="..."`) referencing the issue key and a link to it (`https://ericscott411.atlassian.net/browse/SCRUM-<id>`).

Push before posting either comment — the GitHub link in the Jira comment won't resolve until the commit is on the remote.

## 6. Transition the issue to "In Review"

Committing and pushing through this workflow means the code change is done and ready for someone to look at, so move the issue to reflect that: call `getTransitionsForJiraIssue` on the issue, find the transition whose target status is named "In Review", and apply it with `transitionJiraIssue`. Look the transition up by name each time rather than hardcoding its ID — workflow IDs aren't guaranteed stable across projects or future workflow edits. If the issue is already in "In Review" or beyond (e.g. "Done"), or there's no such transition available from its current status, skip this step rather than forcing it, and mention that in your report back.

## 7. Report back

Tell the user the commit SHA, the branch, both links you posted (Jira comment URL, GitHub commit-comment URL), and the issue's resulting status. Don't just say "done" — the point of this workflow is traceability, so show the trace.
