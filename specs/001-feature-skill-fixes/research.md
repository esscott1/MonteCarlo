# Research: /feature skill fixes from the first trial run

## 1. Where the spec number comes from today

- **Decision**: choose the number before `/speckit-specify` runs, and pass the folder as `SPECIFY_FEATURE_DIRECTORY`.
- **Rationale**: `/speckit-specify` takes the next number after the folders in the worktree's own `specs/`, and a new worktree starts from `master`, so it can't see specs on other unmerged branches. Its resolution order puts an explicit `SPECIFY_FEATURE_DIRECTORY` first, so passing the folder overrides its scan without editing Spec Kit.
- **Alternatives considered**: editing `.specify/scripts/powershell/create-new-feature.ps1` (the skill doesn't call it, and a Spec Kit upgrade would overwrite it); timestamp numbering (`feature_numbering: timestamp` in `init-options.json`), which can't collide but loses the short, ordered numbers the owner chose.

## 2. Which branches to read

- **Decision**: `origin/master` and every `origin/feature/*` remote-tracking branch, after `git fetch origin --prune`. Read each with `git ls-tree -d --name-only <ref> specs/`.
- **Rationale**: every spec is pushed at gate 1, so remote branches are complete; `--prune` drops deleted ones, so an abandoned branch frees its number. `ls-tree` reads a ref without checking it out. A branch without `specs/` lists nothing, which isn't an error; a ref git can't read is.
- **Alternatives considered**: local branches (miss other machines' work, include stale ones); the GitHub API (needs network auth per branch, slower, same answer).

## 3. Max plus one, or fill gaps

- **Decision**: the next number after the highest one in use below 900; gaps are not refilled.
- **Rationale**: numbers then follow the order features started, as Spec Kit intends. An abandoned feature that was the newest frees its number (it was the highest); an older gap stays, which is harmless.
- **Alternatives considered**: lowest free number (reorders history: a new feature could sit before older ones).

## 4. Test runs

- **Decision**: a test run uses the highest number in use at 900 or above, plus one, starting at 900; real numbering only counts numbers below 900, so a test never shifts it.
- **Rationale**: the owner chose 900. Real specs would need 899 features to reach it; the script stops with an error at 899 rather than overflow into test numbers.

## 5. Script or prose

- **Decision**: a Node script with a pure `nextSpecNumber(names, { test })` and a CLI that reads git.
- **Rationale**: Constitution III wants tests for new behaviour; a pure function can be tested with fixed lists in CI (Ubuntu) without git state, and the skill gets one exact command instead of a procedure to reimplement each time. Node matches the repo's other tools and runs the same from PowerShell and Git Bash.
- **Alternatives considered**: PowerShell (not run in CI's Node tests); prose only (untestable; the owner can still choose it at gate 3).

## 6. The branch name

- **Decision**: the skill reads the branch with `git -C <worktree> branch --show-current` and checks it against the spec's **Feature Branch** line; it ignores `BRANCH` in Spec Kit's JSON output and says so.
- **Rationale**: without the `git` extension, Spec Kit's `check-prerequisites.ps1`/`setup-plan.ps1` report the feature folder's name as `BRANCH` (seen in this very feature: `001-feature-skill-fixes`).
