# Implementation Plan: /feature skill fixes from the first trial run

**Branch**: `feature/110-feature-skill-fixes` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-feature-skill-fixes/spec.md` (issue #110)

## Summary

The `/feature` skill gets three rules. **The spec number** is chosen before Spec Kit runs, by a small script rather than by prose, so the rule is exact and tested: the next number below 900 that no spec folder uses on `origin/master` or any `origin/feature/*` branch, or from 900 up for a test run. The skill hands the chosen folder to `/speckit-specify` explicitly. **The branch name** comes from git and the spec header, never from Spec Kit's `BRANCH` output. **Abandoning a feature** removes its spec folder in a pushed commit before the draft PR is closed, with the owner's OK. `CLAUDE.md` says `002-gordon` and describes the rule.

## Technical Context

**Language/Version**: Markdown (the skill, `CLAUDE.md`); JavaScript for Node 20+ (the script), ES modules like the repo's other `tools/*` scripts.

**Primary Dependencies**: none beyond Node's standard library (`node:child_process`, `node:test`, `node:assert`) and `git`.

**Storage**: N/A. The script reads git refs; it writes nothing.

**Testing**: `node --test` (CI already runs `tools/*/*.test.mjs`). The rule is a pure function tested with fixed folder lists; the git reading is a thin wrapper around it.

**Target Platform**: the owner's Windows machine (PowerShell and Git Bash) and CI's Ubuntu runner (for the tests).

**Project Type**: developer tooling inside the existing repo.

**Performance Goals**: answers in under a few seconds with a handful of branches (one `git ls-tree` per branch).

**Constraints**: read-only (never fetches, commits or pushes; the skill fetches first). Stops with an error rather than guessing when a ref can't be read.

**Scale/Scope**: tens of branches, hundreds of specs at most.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Result |
|---|---|---|
| I. The goal decides | No model choice touched. | Pass (n/a) |
| II. One engine | No financial math. | Pass (n/a) |
| III. Tested before it ships | The numbering rule is new behaviour, so it gets a script with `node --test` tests in CI, rather than prose a session might misapply. The skill text and `CLAUDE.md` are checked by the quickstart. | Pass |
| IV. English and Spanish | No visible site text. | Pass (n/a) |
| V. Paid features in one place | No feature access. | Pass (n/a) |
| Infrastructure and records | No Terraform; no prices, secrets or ids. | Pass |
| Requirements and approvals | The gates are unchanged; abandoning still needs the owner's OK to close a PR. | Pass |
| Development workflow | Specs stay in `specs/NNN-name/`; the number rule refines how NNN is chosen. `CLAUDE.md` and the constitution stay in agreement (the constitution doesn't specify numbering, so it needs no change). | Pass |

**Post-design re-check**: unchanged, Pass. One deviation from the spec is flagged below for the owner.

**Deviation from the spec, confirmed by the owner at gate 2 (the spec's Assumptions now match):** the spec's Assumptions said "No site page, test or workflow changes; this is the skill and the project guide only." This plan adds a script and its test file under `tools/spec-number/`, because Constitution III asks for tests that pin new behaviour and a numbering rule followed from prose alone can't be tested. No site page or workflow changes. The owner chose the tested script.

## Project Structure

### Documentation (this feature)

```text
specs/001-feature-skill-fixes/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── next-spec-number.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
tools/spec-number/
├── next-spec-number.mjs        # new: the rule (pure function) + reading git refs, CLI
└── next-spec-number.test.mjs   # new: node --test cases for the rule

.claude/skills/feature/SKILL.md  # gate 1 picks the number with the script; branch rule; test runs; abandoning
CLAUDE.md                        # Spec Kit section: 002-gordon, the number rule in one sentence
```

**Structure Decision**: a `tools/<name>/` folder like `tools/scenario-link/` and `tools/gordon/`, so CI's existing `tools/*/*.test.mjs` glob runs the tests with no workflow change.
