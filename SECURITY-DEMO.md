# Security Demo — Script Injection in the Autonomous Agent Workflow

This document walks through a real vulnerability that existed in
[`.github/workflows/claude-agent.yml`](.github/workflows/claude-agent.yml),
demonstrates it with **safe, non-exfiltrating** proof-of-concept payloads, and
shows how the hardened workflow blocks them.

The fix is in **[PR #24 — Harden claude-agent workflow against script injection](https://github.com/esscott1/MonteCarlo/pull/24)**.

> **Scope and ethics.** Every payload here is a *proof of concept*. None of them
> send a secret off the runner. They prove code execution and secret
> *reachability* only — the last step a real attacker would add (a network call
> that ships the key out) is deliberately omitted. Run this only against your
> own repository.

---

## The vulnerability

The workflow is triggered by a `repository_dispatch` event. Its payload
(`issue_key`, `summary`, `description`) originates as free text a visitor types
into the public change-request form on the site. The original workflow
interpolated those fields with `${{ ... }}` **directly into `run:` shell scripts
and into the agent prompt**:

```yaml
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

## Before you run

1. Check out the **pre-fix** workflow (any commit before PR #24 merges), so the
   vulnerable version is what runs.
2. **Set the repo secret `ANTHROPIC_API_KEY` to a throwaway value** for the
   duration of the demo, e.g. `sk-ant-DEMO-not-a-real-key`. Then even the
   fingerprint in Option 2 is computed over a dummy string and there is nothing
   to rotate afterward. Restore the real key when you're done.
3. Trigger each run the normal way: submit the change-request form on the site
   (or `POST` the `repository_dispatch` event) with the payload text as the
   **description** field. Watch the run under the repo's **Actions** tab.

---

## Option 1 — Execution marker

**Proves:** the description field reaches the runner's shell and executes.
Pure code-execution proof; the secret is never touched.

**Payload (paste as the change-request _description_):**

```
Change the homepage H1 to "Welcome" $(echo "::warning::INJECTION-PROOF actor=$(whoami) host=$(hostname)")
```

**Expected result:** the Actions run log shows a workflow annotation supplied by
the *visitor's* text:

```
INJECTION-PROOF actor=runner host=fv-az...
```

`whoami` / `hostname` are harmless; they just make the banner concrete. That the
banner appears at all proves the payload executed on the runner.

**File-artifact variant:** use `$(id > exec-proof.txt)` in the description
instead; the injected command writes a file the agent's commit then carries. The
annotation version is cleaner for a slide.

_Screenshot:_

<!-- ![Option 1 injection annotation](docs/security-demo/option1-annotation.png) -->

---

## Option 2 — "Secret was reachable" without leaking it  *(recommended)*

**Proves:** the injected command executes **and can read `ANTHROPIC_API_KEY`
from its environment** — the exact precondition for theft — while the key's
value never appears anywhere.

**Why it's safe:** it prints only the secret's **length** and a **truncated
SHA-256 fingerprint**. A SHA-256 of a high-entropy key is not reversible, and a
length is just a number. Reachability is demonstrated; disclosure is not.

**Payload (paste as the change-request _description_):**

```
Change the homepage H1 to "Welcome" $(echo "::warning::SECRET-REACHABLE len=${#ANTHROPIC_API_KEY} sha256=$(printf %s "$ANTHROPIC_API_KEY" | sha256sum | cut -c1-12)")
```

**Expected result:** an annotation such as:

```
SECRET-REACHABLE len=41 sha256=9f2a1c4b7e08
```

**Talking point:** *"The length and fingerprint prove the injected command read
the API key out of the environment. A real attacker would replace this line with
a network call to their own server. I print a fingerprint instead, so nothing
leaks — but the theft path is identical up to that final step."*

_Screenshot:_

<!-- ![Option 2 secret-reachable annotation](docs/security-demo/option2-annotation.png) -->

---

## After the fix (PR #24)

Re-run the **same** payloads against the hardened workflow:

- The payload fields are lifted into job-level `env:` vars and never interpolated
  into a `run:` script or the prompt. `$(whoami)` and `${#ANTHROPIC_API_KEY}` are
  now just characters in a string — they appear **verbatim in the PR body** and
  execute nothing.
- A malformed `issue_key` (anything not matching `^SCRUM-[0-9]+$`) stops the run
  **before any secret is in scope**.
- The agent's dotnet tool is scoped to `dotnet build` / `dotnet test`, and a
  guard step fails the run if the agent modified anything under `.github/`.

_Screenshot (same payload, now inert in the PR body):_

<!-- ![After fix: payload rendered as literal text](docs/security-demo/after-fix-pr-body.png) -->

---

## Suggested demo arc

1. Pre-fix workflow + dummy key → submit the Option 2 payload → capture the
   `SECRET-REACHABLE` annotation.
2. State plainly: *a real exploit swaps the fingerprint for a network call; I
   stop short of that on purpose.*
3. Merge PR #24 → resubmit the same payload → show it landing as literal text in
   the PR body, and a malformed-key attempt hitting the validation gate.
4. Close with the mitigations table from the PR description.

---

## Mitigations reference

| Fix | Closes |
|---|---|
| Payload fields → job-level `env:` vars, never `${{ }}` in scripts/prompt | Shell script injection |
| `issue_key` validated against `^SCRUM-[0-9]+$` before secrets are in scope | Injection via the key field; malformed branch/commit names |
| Request framed as untrusted data in the prompt; agent told not to obey it or touch secrets/network/`.github` | Prompt injection |
| Agent dotnet tool scoped to `build`/`test` | Arbitrary code execution via `dotnet run` |
| Guard step fails if the agent touched `.github/` | Self-modifying CI |
| PR title/body built from env vars, body via `--body-file` | Injection in the PR-creation step |

## Further reading

- GitHub: [Security hardening for GitHub Actions — untrusted input](https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions#understanding-the-risk-of-script-injections)
