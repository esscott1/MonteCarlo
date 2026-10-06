# Security Demo — Script Injection in the Autonomous Agent Workflow

This is a **read-only portfolio artifact**: a written walkthrough of a real
script-injection vulnerability that existed in this repo's autonomous agent
workflow, the safe proof-of-concept payloads that demonstrated it, and the
hardening that closed it. It's meant to be *read* (and screenshotted), not run.
A [reproducibility appendix](#appendix-reproducing-it-historical) at the end
records how it was exercised at the time, for anyone who wants to rebuild it.

> **A note on dates (updated 2026-10-06).** The vulnerable workflow was
> `.github/workflows/claude-agent.yml`. It has since been **renamed and
> restructured**: the entry point is now
> [`agent-dispatcher.yml`](.github/workflows/agent-dispatcher.yml), which routes
> a story by its label to a handler workflow
> ([`agent-title-change.yml`](.github/workflows/agent-title-change.yml)), with
> the shared before/after steps in the `agent-prepare` and `agent-finish`
> composite actions. The code snippets below are shown **as they were** in the
> pre-fix `claude-agent.yml`; the "[after the fix](#after-the-fix)" section maps
> each mitigation to where it lives on `master` today. The fix landed in
> **[PR #24](https://github.com/esscott1/MonteCarlo/pull/24)** (merged) and the
> hardening described here is what runs now.

> **Scope and ethics.** Every payload here is a *proof of concept*. None of them
> send a secret off the runner. They prove code execution and secret
> *reachability* only — the last step a real attacker would add (a network call
> that ships the key out) is deliberately omitted. The reproduction steps are to
> be run only against your own repository.

---

## The vulnerability

The workflow is triggered by a `repository_dispatch` event. Its payload
(`issue_key`, `summary`, `description`) originates as free text a visitor types
into the public change-request form on the site. The original workflow
(`claude-agent.yml`, pre-fix) interpolated those fields with `${{ ... }}`
**directly into `run:` shell scripts and into the agent prompt**:

```yaml
# claude-agent.yml, as it was BEFORE the fix (historical — this file no longer exists)
- name: Create Feature Branch
  run: |
    BRANCH_NAME="feature/${{ github.event.client_payload.issue_key }}-agent-..."
# ...
- name: Run Claude Code Agent
  run: |
    claude -p ... -- "... Description: ${{ github.event.client_payload.description }} ..."
```

`${{ }}` is **textual substitution performed before the shell runs**. GitHub
pastes the raw payload into the script text, then bash parses the result. Inside
a double-quoted string bash still evaluates `$(...)`, backticks, and `${...}`,
and an unbalanced `"` ends the string early. So visitor text becomes **shell
code**, and separately becomes **agent instructions**.

### Why it matters

| Asset in scope | Reachable how |
|---|---|
| `ANTHROPIC_API_KEY` | Present in the "Run Claude Code Agent" step's environment |
| Write-scoped `GITHUB_TOKEN` | Present in the "Push Branch and Create PR" step (`contents: write`, `pull-requests: write`) |
| The live site | A push to an unprotected `master` deploys to Azure |
| The agent itself | `Bash(dotnet *)` allowed arbitrary code via `dotnet run` / MSBuild targets |

The human who triages the Jira issue is **not** a security control: they judge
whether the request makes sense, not whether it hides shell syntax, and a
payload can ride inside an otherwise reasonable-looking request.

---

## The proof-of-concept payloads

Two payloads demonstrated the flaw. Both were submitted as the change-request
**description** field against the pre-fix workflow. Each is shown with the
output it produced in the Actions log.

### Option 1 — Execution marker

**Proves:** the description field reaches the runner's shell and executes.
Pure code-execution proof; the secret is never touched.

**Payload (the change-request _description_):**

```
Change the homepage H1 to "Welcome" $(echo "::warning::INJECTION-PROOF actor=$(whoami) host=$(hostname)")
```

**Result:** the Actions run log shows a workflow annotation supplied by the
*visitor's* text:

```
INJECTION-PROOF actor=runner host=fv-az...
```

`whoami` / `hostname` are harmless; they just make the banner concrete. That the
banner appears at all proves the payload executed on the runner.

**File-artifact variant:** `$(id > exec-proof.txt)` in the description instead;
the injected command writes a file the agent's commit then carries. The
annotation version is cleaner for a slide.

### Option 2 — "Secret was reachable" without leaking it  *(the one to show)*

**Proves:** the injected command executes **and can read `ANTHROPIC_API_KEY`
from its environment** — the exact precondition for theft — while the key's
value never appears anywhere.

**Why it's safe:** it prints only the secret's **length** and a **truncated
SHA-256 fingerprint**. A SHA-256 of a high-entropy key is not reversible, and a
length is just a number. Reachability is demonstrated; disclosure is not.

**Payload (the change-request _description_):**

```
Change the homepage H1 to "Welcome" $(echo "::warning::SECRET-REACHABLE len=${#ANTHROPIC_API_KEY} sha256=$(printf %s "$ANTHROPIC_API_KEY" | sha256sum | cut -c1-12)")
```

**Result:** an annotation such as:

```
SECRET-REACHABLE len=41 sha256=9f2a1c4b7e08
```

**Talking point:** *"The length and fingerprint prove the injected command read
the API key out of the environment. A real attacker would replace this line with
a network call to their own server. I print a fingerprint instead, so nothing
leaks — but the theft path is identical up to that final step."*

---

## After the fix

The hardening in PR #24 is now on `master`, spread across the restructured
workflow. Re-running the **same** payloads against it, they are inert:

- The payload fields are lifted into job-level `env:` vars and never interpolated
  into a `run:` script or the prompt
  ([`agent-title-change.yml`](.github/workflows/agent-title-change.yml) builds
  the prompt from env vars via `printf`). `$(whoami)` and
  `${#ANTHROPIC_API_KEY}` are now just characters in a string — they appear
  **verbatim in the PR body** and execute nothing.
- A malformed `issue_key` (anything not matching `^SCRUM-[0-9]+$`) stops the run
  **before any secret is in scope**
  ([`agent-prepare/action.yml`](.github/actions/agent-prepare/action.yml)).
- The agent's dotnet tool is scoped to `dotnet build` / `dotnet test`, and
  [`check-handler-work.sh`](.github/scripts/check-handler-work.sh) fails the run
  (in [`agent-finish`](.github/actions/agent-finish/action.yml)) if the handler
  touched anything under `.github/` or outside its allowed paths.

### Where each mitigation lives now

| Fix | Closes | On `master` |
|---|---|---|
| Payload fields → job-level `env:` vars, never `${{ }}` in scripts/prompt | Shell script injection | `agent-title-change.yml` (env + `printf` prompt) |
| `issue_key` validated against `^SCRUM-[0-9]+$` before secrets are in scope | Injection via the key field; malformed branch/commit names | `agent-prepare/action.yml`; also `resolve-story-type.mjs` |
| Request framed as untrusted data in the prompt; agent told not to obey it or touch secrets/network/`.github` | Prompt injection | `.github/agent-prompts/title-change.md` |
| Agent dotnet tool scoped to `build`/`test` | Arbitrary code execution via `dotnet run` | `agent-title-change.yml` (`--allowed-tools`) |
| Guard fails the run if the handler touched `.github/` or left its scope | Self-modifying CI; scope escape | `check-handler-work.sh` (via `agent-finish`) |
| PR title/body built from env vars, body via `--body-file` | Injection in the PR-creation step | `agent-finish/action.yml` |

---

## Further reading

- GitHub: [Security hardening for GitHub Actions — untrusted input](https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions#understanding-the-risk-of-script-injections)

---

## Appendix: reproducing it (historical)

These are the steps used to exercise the demo **at the time**, against the
pre-fix `claude-agent.yml`. They're kept for reproducibility; today they'd need
adapting to the restructured workflow (the dispatcher + handler described in the
note at the top). Run only against your own repository.

1. Check out the **pre-fix** workflow (a commit before PR #24 merged), so the
   vulnerable version is what runs.
2. **Set the repo secret `ANTHROPIC_API_KEY` to a throwaway value** for the
   duration of the demo, e.g. `sk-ant-DEMO-not-a-real-key`. Then even the
   fingerprint in Option 2 is computed over a dummy string and there is nothing
   to rotate afterward. Restore the real key when you're done.
3. Trigger each run the normal way: submit the change-request form on the site
   (or `POST` the `repository_dispatch` event) with the payload text as the
   **description** field. Watch the run under the repo's **Actions** tab.
4. For the "after the fix" comparison, run the same payloads against current
   `master` and show them landing as literal text in the PR body, plus a
   malformed-key attempt hitting the validation gate.

### Suggested demo arc

1. Pre-fix workflow + dummy key → submit the Option 2 payload → capture the
   `SECRET-REACHABLE` annotation.
2. State plainly: *a real exploit swaps the fingerprint for a network call; I
   stop short of that on purpose.*
3. Show the fix on `master` → resubmit the same payload → it lands as literal
   text in the PR body, and a malformed-key attempt hits the validation gate.
4. Close with the "where each mitigation lives now" table.

### Screenshots

Capture these from your own runs and drop them in
[`docs/security-demo/`](docs/security-demo/) (filenames the `README.md` there
lists). They are **not** included in the repo — a portfolio piece shouldn't ship
fabricated evidence; capture real ones if you want them.

<!-- ![Option 2 secret-reachable annotation](docs/security-demo/option2-annotation.png) -->
<!-- ![After fix: payload rendered as literal text](docs/security-demo/after-fix-pr-body.png) -->
<!-- ![Malformed key hits the validation gate](docs/security-demo/after-fix-validation-gate.png) -->
