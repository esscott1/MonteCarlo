# Data model: /feature skill fixes from the first trial run

## Spec folder name

`specs/<NNN>-<slug>`: `NNN` is three digits, `slug` is kebab-case. A name not starting with three digits and a hyphen is ignored by the numbering rule.

## Spec number

| Range | Used for | Next number |
|---|---|---|
| 001-899 | real features | highest in use below 900, plus one; 001 when none |
| 900-999 | test runs | highest in use at 900 or above, plus one; 900 when none |

- **In use** means a folder with that number exists on `origin/master` or on any `origin/feature/*` branch.
- **Errors**: real numbering past 899, or test numbering past 999, stops with a message; so does any branch git can't read.
- **Lifecycle**: a number is taken when its spec is pushed at gate 1, stays taken while the branch exists, becomes permanent when the branch merges, and is released when an abandoned branch is deleted (if it was the highest).

## Feature branch

`feature/<issue>-<slug>`, read from git (`git branch --show-current` in the worktree) and recorded in the spec's **Feature Branch** line. Never taken from Spec Kit's `BRANCH` output.
