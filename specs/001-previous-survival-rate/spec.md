# Feature Specification: Previous run's survival rate

**Issue**: #107 (https://github.com/esscott1/MonteCarlo/issues/107)

**Feature Branch**: `feature/107-previous-survival-rate`

**Created**: 2026-10-31

**Status**: Draft

**Input**: The owner's confirmed summary of issue #107: someone comparing two versions of a plan on the Scenario runner has to write down the first run's survival rate before changing the inputs. For every tier, Free included. Success: after a run, the survival rate shows the previous run's figure beside it, for example "82% (was 76%)".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compare a run with the one before it (Priority: P1)

A visitor runs a plan on the Scenario runner, changes an input (say, a lower monthly withdrawal), and runs again. The results summary shows the new survival rate with the previous run's rate beside it, so the visitor sees whether the change helped without noting anything down.

**Why this priority**: It is the whole request; without it there is no feature.

**Independent Test**: Run the defaults, change the monthly withdrawal, run again, and check that the summary shows the new rate followed by "(was N%)" with the first run's rate.

**Acceptance Scenarios**:

1. **Given** a visitor has run a plan once, **When** they change an input and run again, **Then** the survival rate reads like "82% (was 76%)", the figure in brackets being the first run's survival rate.
2. **Given** a visitor runs for the first time since opening the page, **When** the results appear, **Then** the survival rate shows no "was" figure.
3. **Given** two runs in a row with the same survival rate, **When** the second finishes, **Then** it still shows the previous figure (for example "80% (was 80%)").

---

### User Story 2 - The comparison in Spanish (Priority: P2)

A visitor using the site in Spanish sees the same comparison in Spanish, and switching language redraws it without re-running.

**Why this priority**: Every visible string on the site has its Spanish; the feature isn't finished without it, but the English comparison is usable on its own.

**Independent Test**: Run twice in English, switch to Spanish, and check the comparison is shown in Spanish with the same two figures.

**Acceptance Scenarios**:

1. **Given** two runs have finished, **When** the visitor switches between English and Spanish, **Then** the comparison is shown in the chosen language with the same figures and no new run.

---

### Edge Cases

- A run that fails validation or whose request fails: it doesn't count as a run, so the next successful run compares with the last successful one.
- Changing the number of iterations between runs: the comparison still shows, since both are survival rates.
- Survival rates that aren't whole numbers: both figures use the same rounding as the survival rate already shown.
- Which runs count as "previous": every successful run, whatever started it - Run, Optimize, Optimize Social Security, the Optimizer's hand-off, a shared link or Gordon. So an Optimize run shows the visitor's own plan's rate as its "was" figure, a before-and-after of the optimization.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: After every successful run except the first, the Scenario runner MUST show the previous successful run's survival rate beside the new one, in the results summary's first line.
- **FR-002**: The first successful run MUST show no previous figure.
- **FR-003**: A run that fails validation or fails to complete MUST NOT replace the remembered previous rate.
- **FR-004**: The comparison MUST be available to every tier, Free included, with no paid badge.
- **FR-005**: The comparison text MUST be translated to Spanish and redrawn when the language changes, without re-running.
- **FR-006**: The remembered previous rate MUST last only while the page stays open: a reload, a new tab or a new visit starts with no previous figure, and nothing is kept in the browser.
- **FR-008**: Every successful run MUST count as the previous run for the next one, whatever started it (Run, Optimize, Optimize Social Security, the Optimizer's hand-off, a shared link, Gordon).
- **FR-007**: The comparison MUST NOT change any simulation result, input, or the survival rate itself; it only displays the previous figure.

### Key Entities

- **Previous survival rate**: the survival rate of the last successful run on the Scenario runner, kept so the next run can show it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor comparing two plans can read both survival rates from the screen after the second run, without writing anything down.
- **SC-002**: In a check of the steps above, 100% of second and later runs show the previous figure, and no first run does.
- **SC-003**: The comparison appears in English and Spanish with identical figures.
- **SC-004**: Results take no noticeably longer to appear than before (no added waiting the visitor can notice).

## Assumptions

- The comparison is a figure in brackets, "(was 76%)", as in the issue's example; no arrow or points change is shown.
- Only the survival rate is compared; balances and other figures are out of scope.
- The Optimizer page is out of scope; only the Scenario runner shows the comparison.
- Nothing is sent to or stored on the server, and nothing is saved in the browser: the previous figure lives only as long as the open page.

## Clarifications

### Session 2026-10-31

- Q: Which runs count as the "previous" run? → A: Every successful run, whatever started it (Run, Optimize, the hand-off, a shared link).
- Q: How long is the previous rate remembered? → A: Only while the page stays open; a reload starts afresh.
