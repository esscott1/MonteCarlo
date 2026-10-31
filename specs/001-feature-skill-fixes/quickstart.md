# Quickstart: checking the /feature skill fixes

## Automated

From Git Bash in the worktree:

```bash
node --test tools/spec-number/next-spec-number.test.mjs     # the numbering rule
node --test .github/scripts/*.test.mjs tools/*/*.test.mjs   # every Node test, as CI runs them
dotnet test MonteCarlo.sln                                  # unchanged, still passes
```

## Against the real repo

1. `git fetch origin --prune`, then `node tools/spec-number/next-spec-number.mjs`. With this feature's branch pushed (holding `001`) and nothing merged, it prints `002`.
2. `node tools/spec-number/next-spec-number.mjs --test` prints `900`.

## Reading the skill

In `.claude/skills/feature/SKILL.md` check that:
- gate 1 runs the script and passes `specs/<NNN>-<slug>` to `/speckit-specify`, and a test run uses `--test`;
- every branch name comes from `git branch --show-current` and the spec's Feature Branch line, with Spec Kit's `BRANCH` output named as wrong;
- an "Abandoning a feature" section removes the spec folder in a pushed commit before closing the draft PR, with the owner's OK;
- `CLAUDE.md` has no `001-gordon` and names `002-gordon`.

## End to end (after merge)

The next real feature through `/feature` (Gordon) gets `002` at gate 1, chosen by the script.
