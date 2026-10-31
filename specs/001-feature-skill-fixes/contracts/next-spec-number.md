# Contract: `tools/spec-number/next-spec-number.mjs`

## Command line

```text
node tools/spec-number/next-spec-number.mjs [--test]
```

- Run from any folder of the repo (the main folder or a worktree; they share refs), after `git fetch origin --prune`.
- **Output** (stdout, success): the number alone, three digits, e.g. `002`; exit code 0.
- `--test`: the next test-run number (900 and up) instead of a real one.
- **Errors** (stderr, exit code 1): a ref git can't read (names the ref); real numbering past 899; test numbering past 999.
- Read-only: never fetches, commits, pushes or writes files.

## Module

```text
export function nextSpecNumber(folderNames, { test = false } = {}) -> string
```

- `folderNames`: spec folder names from every branch read (duplicates allowed), e.g. `["001-feature-skill-fixes", "002-gordon"]`.
- Returns the next number as a three-digit string, by the rules in [data-model.md](../data-model.md); throws an `Error` past the range's end.
