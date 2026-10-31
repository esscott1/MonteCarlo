---

description: "Task list for the /feature skill fixes (issue #110)"
---

# Tasks: /feature skill fixes from the first trial run

**Input**: Design documents from `specs/001-feature-skill-fixes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/next-spec-number.md, quickstart.md

**Tests**: Requested for the numbering script (Constitution III); the skill text and `CLAUDE.md` are checked by the quickstart.

**Organization**: Tasks are grouped by user story; each story can be done and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Paths are from the repository root (the worktree `C:\src\MonteCarlo-110-feature-skill-fixes`).

## Phase 1: Setup

- [ ] T001 Create the folder `tools/spec-number/` for the script and its tests (a `tools/<name>/` folder, so CI's `tools/*/*.test.mjs` glob picks the tests up)

---

## Phase 2: Foundational

None: the three stories touch separate parts of the skill and share no prerequisite beyond T001.

---

## Phase 3: User Story 1 - Two features in progress get different numbers (Priority: P1) 🎯 MVP

**Goal**: gate 1 chooses a spec number no other branch uses.

**Independent Test**: with a branch holding `002-x`, the script prints `003`; gate 1 in the skill uses the script's number.

### Tests for User Story 1

- [ ] T002 [P] [US1] Write `tools/spec-number/next-spec-number.test.mjs` (`node:test`, `node:assert/strict`) for `nextSpecNumber(folderNames, { test })` from the contract: no folders → `001`; `["001-a", "002-b"]` → `003`; duplicates (`["001-a", "001-a"]`) → `002`; a gap (`["001-a", "003-c"]`) → `004` (max plus one, no gap filling); names not starting with three digits and a hyphen are ignored (`["readme.md", "abc-x"]` → `001`); test folders don't move real numbering (`["001-a", "900-t"]` → `002`); `{ test: true }` with none → `900`, with `["900-t"]` → `901`, ignoring real folders; real numbering past 899 throws (`["899-z"]`); test numbering past 999 throws (`["999-t"]`, `{ test: true }`). Run it and confirm it fails before T003.

### Implementation for User Story 1

- [ ] T003 [US1] Write `tools/spec-number/next-spec-number.mjs` per `specs/001-feature-skill-fixes/contracts/next-spec-number.md`: export the pure `nextSpecNumber`; when run directly, list `refs/remotes/origin/master` and every `refs/remotes/origin/feature/*` (`git for-each-ref --format=%(refname:short)`), read each with `git ls-tree -d --name-only <ref> specs/` (folder names after `specs/`), print the number for `--test` or not, exit 1 with the ref named on any git error. Read-only: no fetch, commit, push or file writes. CRLF, no BOM, matching `tools/scenario-link/` style. Make T002 pass.
- [ ] T004 [US1] In `.claude/skills/feature/SKILL.md`, gate 1: after the worktree step, `git -C C:\src\MonteCarlo fetch origin --prune`, then `node tools/spec-number/next-spec-number.mjs` (add `--test` when the owner calls the run a test); stop and report if it fails. Pass `SPECIFY_FEATURE_DIRECTORY=specs/<NNN>-<slug>` to `/speckit-specify` so it uses that folder instead of its own scan, and say why (a worktree only sees `master`'s specs).

**Checkpoint**: `node --test tools/spec-number/next-spec-number.test.mjs` passes; the skill's gate 1 names the script.

---

## Phase 4: User Story 2 - The branch the skill reports is the real one (Priority: P2)

**Goal**: every branch name the skill shows or uses is `feature/<issue>-<slug>`.

**Independent Test**: read the skill; every branch name comes from git or the spec's Feature Branch line.

### Implementation for User Story 2

- [ ] T005 [US2] In `.claude/skills/feature/SKILL.md`, add to Ground rules: the feature's branch is `git -C <worktree> branch --show-current`, recorded as the spec's **Feature Branch** line; the `BRANCH` field in Spec Kit's script output (`check-prerequisites.ps1`, `setup-plan.ps1`) is the feature folder's name, because the `git` extension isn't installed, so ignore it. In gate 1 step 3, set the spec's Feature Branch line from git.

**Checkpoint**: no instruction in the skill takes a branch name from Spec Kit's output.

---

## Phase 5: User Story 3 - Test runs and abandoned features (Priority: P3)

**Goal**: tests are numbered 900+, and an abandoned feature's branch ends with its spec removed before its PR closes.

**Independent Test**: read the skill's test-run and abandoning instructions; the script prints `900` for `--test`.

### Implementation for User Story 3

- [ ] T006 [US3] In `.claude/skills/feature/SKILL.md`, add an **Abandoning a feature** section (after Gate 5): only on the owner's word; in the worktree `git rm -r specs/<NNN>-<slug>`, commit `#<n> Abandon: remove the spec`, push; then close the draft PR with `gh pr close <pr> --comment "<the owner's reason>"` only with the owner's OK for that PR; close or keep the issue as the owner says (`gh issue close <n> --reason "not planned"`); remove the worktree; delete the branch with `git branch -d` (it matches its pushed copy) and `git push origin --delete`, the owner's OK covering both. Note that test runs (T004's `--test`) are cleaned up the same way.

**Checkpoint**: the abandon steps remove the spec before the PR closes and ask before closing.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T007 [P] In `CLAUDE.md`'s Spec Kit section, change `specs/001-gordon/` to `specs/002-gordon/`, and add one sentence: `/feature` picks the spec number with `tools/spec-number/next-spec-number.mjs` (the next free below 900 across `origin/master` and every `origin/feature/*` branch; 900 and up for test runs), and Spec Kit's `BRANCH` output is the folder's name, not the branch. Keep CRLF and no BOM.
- [ ] T008 Run the quickstart (`specs/001-feature-skill-fixes/quickstart.md`): the script's tests, all Node tests from Git Bash, `dotnet test MonteCarlo.sln`; `node tools/spec-number/next-spec-number.mjs` prints `002` and `--test` prints `900`; `grep -n 001-gordon CLAUDE.md` finds nothing.
- [ ] T009 Check `git diff --stat origin/master` lists only `tools/spec-number/*`, `.claude/skills/feature/SKILL.md`, `CLAUDE.md` and `specs/001-feature-skill-fixes/*`, with CRLF and no BOM on the new files and no prices or ids.

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (T001) before US1's script tasks.
- US1, US2 and US3 are independent of each other, but T004, T005 and T006 all edit `.claude/skills/feature/SKILL.md`, so they run one after another (no [P]).
- Polish (T007-T009) after the stories; T007 can run alongside any story.

### Within Each User Story

- US1: T002 (test, failing) → T003 (script, passing) → T004 (skill uses it).

### Parallel Opportunities

- T002 and T007 touch different files and can run alongside each other.

---

## Parallel Example: User Story 1

```text
T002 Write tools/spec-number/next-spec-number.test.mjs
T007 Update CLAUDE.md's Spec Kit section
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

T001-T004 alone fix the only gap that produces lasting wrong results (duplicate numbers). Stop and check with the script's tests.

### Incremental Delivery

US1 (numbers) → US2 (branch name) → US3 (tests and abandoning) → Polish, all in one PR since the change is small.

---

## Notes

- 9 tasks. Commit in logical steps as `#110 <what changed>`, staging by path.
- The script and its tests (T001-T003) were confirmed by the owner at gate 2; the spec's Assumptions match.
