# Feature Specification: /feature skill fixes from the first trial run

**Issue**: #110 (https://github.com/esscott1/MonteCarlo/issues/110)

**Feature Branch**: `feature/110-feature-skill-fixes`

**Created**: 2026-10-31

**Status**: Draft

**Input**: The owner's confirmed summary of issue #110: three gaps in the `/feature` skill, found in its first trial run (issues #107 and #108). (1) The spec helper reports the spec folder's name as the branch, so anyone relying on it gets the wrong branch; the real branch comes from git and the spec header. (2) Two features in progress at once both start from `master` and get the same spec number, since each one only sees the specs already merged; the skill must pick the next number that's free across `master` and every open `feature/*` branch. (3) A test run uses the reserved number 900, and an abandoned feature has its spec folder deleted from its branch before its draft PR is closed. For the owner as developer; nothing changes for site visitors. Success also includes `CLAUDE.md`'s reference to Gordon's spec saying `002-gordon`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two features in progress get different numbers (Priority: P1)

The owner starts a spec for one issue while another issue's spec is still on its own unmerged branch. The second spec gets the next free number, not the same number as the first.

**Why this priority**: A duplicate number is the only one of the three gaps that produces wrong, lasting results: two `specs/NNN-…` folders with the same number end up on `master` once both merge.

**Independent Test**: With one feature branch holding `specs/002-x`, start gate 1 for another approved issue and check its spec folder is `003-…`.

**Acceptance Scenarios**:

1. **Given** `master` has specs up to `001` and an open feature branch has `002`, **When** gate 1 starts for another issue, **Then** the new spec is numbered `003`.
2. **Given** no spec exists anywhere, **When** gate 1 starts, **Then** the new spec is numbered `001`.
3. **Given** a feature branch was deleted with its spec (an abandoned feature), **When** gate 1 starts, **Then** that branch's number is used again if it was the highest in use; older gaps aren't refilled, so numbers keep the order features started.

---

### User Story 2 - The branch the skill reports is the real one (Priority: P2)

At every gate the skill names the feature's real branch (`feature/<issue>-<slug>`), taken from git and the spec header, never the spec folder's name that the spec helper reports as its branch.

**Why this priority**: The wrong name misleads anyone following the skill's output, but the work itself still lands on the right branch.

**Independent Test**: Run gate 1 for an issue and check every branch name the skill shows or uses is `feature/<issue>-<slug>`.

**Acceptance Scenarios**:

1. **Given** a feature at any gate, **When** the skill reports where the work is, **Then** it names `feature/<issue>-<slug>`, matching the spec's **Feature Branch** line.

---

### User Story 3 - Test runs and abandoned features leave no trace in the numbering (Priority: P3)

A trial of the workflow uses spec number `900`, so it can never be mistaken for, or take the number of, a real feature. When the owner abandons a feature, its spec folder is deleted from its branch before its draft PR is closed, so the branch holds no spec if it's ever reopened.

**Why this priority**: It keeps the record tidy; the numbering itself is already protected by Story 1.

**Independent Test**: Start a test run and check its spec folder is `900-…`; abandon a feature and check its branch's last commit removes the spec folder before the PR closes.

**Acceptance Scenarios**:

1. **Given** the owner says a run is a test, **When** gate 1 starts, **Then** its spec folder is numbered `900`, and real specs continue below 900.
2. **Given** the owner abandons a feature with a draft PR, **When** the skill cleans it up, **Then** it commits the removal of the spec folder to the branch, pushes it, and only then closes the PR, with the owner's OK to close it.

---

### Edge Cases

- A test run while another test's `900` is still on an open branch: the second test takes the highest test number in use, plus one (901).
- A remote branch that can't be read: the skill stops and says so rather than guessing a number.
- A feature branch that isn't named `feature/*` (for example the automated agents' `translations/*` branches): it isn't checked, because those branches never hold specs.
- Real numbering never enters the 900s; if real specs ever reach 899, the owner decides what comes next.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: At gate 1 the skill MUST choose the spec number itself: the next number, below 900, not used by any spec folder on `master` or on any open `feature/*` branch, and give that folder to the spec helper explicitly.
- **FR-002**: For a run the owner calls a test, the skill MUST use the highest test number in use (900 and up), plus one, starting at 900.
- **FR-003**: The skill MUST take the feature's branch from git and the spec's **Feature Branch** line, and MUST NOT use the branch name reported by the spec helper.
- **FR-004**: When the owner abandons a feature, the skill MUST delete its spec folder from its branch in a commit, push it, and then close its draft PR, closing only with the owner's OK for that PR.
- **FR-005**: The project guide's reference to Gordon's spec MUST say `002-gordon`.
- **FR-006**: The rules MUST be written in the skill so a session following it applies them without being told.

### Key Entities

- **Spec number**: the three-digit prefix of a spec folder (`001`, `002`, …), unique across `master` and open feature branches; `900` and up are reserved for test runs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a check with two features in progress at once, the two specs get different numbers in 100% of attempts.
- **SC-002**: Every branch name the skill reports during a gate-1 run matches the spec's **Feature Branch** line.
- **SC-003**: A test run's spec folder is numbered 900 or higher, and the next real spec after it takes the next number below 900.
- **SC-004**: After an abandoned feature is cleaned up, its branch's last commit removes its spec folder, and its PR is closed.
- **SC-005**: No reference to `001-gordon` remains in the project guide.

## Assumptions

- Open feature branches are the ones on GitHub under `feature/`; local-only branches that were never pushed aren't checked, since the skill pushes every spec at gate 1.
- The automated change-request pipeline's branches (`feature/SCRUM-<n>-agent-…`) are checked too, and simply hold no specs.
- The spec helper still creates the spec file; only the number and folder are decided by the skill beforehand.
- No site page or workflow changes: the skill, the project guide, and a small tested script under `tools/spec-number/` that picks the number (the owner chose a tested script over prose at gate 2).

## Clarifications

### Session 2026-10-31

- Q: Should a test run take the next real number or a reserved one? → A: A reserved number, 900.
- Q: What happens to an abandoned feature's spec folder? → A: It is deleted from the feature's branch before its draft PR is closed.
- Q: Should the numbering rule be a tested script or prose in the skill? → A: A tested script (owner, gate 2).
