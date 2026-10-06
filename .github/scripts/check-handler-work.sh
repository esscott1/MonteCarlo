#!/usr/bin/env bash
# Proves an agent handler stayed within its bounds before anything is pushed (run by the agent-finish action):
#   - it is still on the feature branch agent-prepare created, which isn't master;
#   - the local master branch didn't move, and the branch builds on the master it started from;
#   - it committed everything, committed something, and changed only files matching its allowed patterns - never
#     anything under .github/.
# Inputs (environment): BRANCH, MASTER_SHA, LOCAL_MASTER_SHA (empty if there was no local master) and ALLOWED_PATHS
# (glob patterns, one per line). Exits non-zero with a ::error:: message on the first problem.
set -euo pipefail
fail() { echo "::error::$1"; exit 1; }

[[ -n "${BRANCH:-}" && "$BRANCH" != "master" ]] || fail "No feature branch to push."
[[ "$(git rev-parse --abbrev-ref HEAD)" == "$BRANCH" ]] || fail "The handler left $BRANCH; refusing to push."
[[ "$(git rev-parse --verify --quiet refs/heads/master || true)" == "${LOCAL_MASTER_SHA:-}" ]] \
  || fail "The local master branch was changed; refusing to push."
git merge-base --is-ancestor "$MASTER_SHA" HEAD || fail "$BRANCH doesn't build on master; refusing to push."

[[ -z "$(git status --porcelain)" ]] || { git status --short; fail "The handler left uncommitted changes."; }
changed="$(git diff --name-only "$MASTER_SHA" HEAD)"
[[ -n "$changed" ]] || fail "The handler made no commits, so there's nothing to open a pull request for."

while IFS= read -r file; do
  [[ "$file" == .github/* ]] && fail "The handler changed $file under .github/; refusing to push."
  allowed=false
  while IFS= read -r pattern; do
    pattern="${pattern%$'\r'}"
    [[ -z "$pattern" ]] && continue
    # Unquoted on purpose: the pattern is a glob
    if [[ "$file" == $pattern ]]; then allowed=true; break; fi
  done <<< "${ALLOWED_PATHS:-}"
  $allowed || fail "The handler changed $file, which is outside its scope; refusing to push."
done <<< "$changed"

echo "Checks passed. Changed files:"
echo "$changed"
