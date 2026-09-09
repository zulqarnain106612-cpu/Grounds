#!/usr/bin/env bash
# Check status for exactly ONE pull request. Never a repo-wide run listing.
#
#   usage: scripts/pr_status.sh <pr-number>
#
# `gh pr checks` is PR-scoped by construction -- its own help reads "Show CI
# status for a single pull request" -- so there is no `gh run list` anywhere in
# this path. Exit code 8 means checks are still pending, which is a state to
# report, not a failure to propagate.
set -uo pipefail

PR="${1:-}"
if [ -z "$PR" ]; then
    echo "usage: scripts/pr_status.sh <pr-number>" >&2
    exit 2
fi

BRANCH=$(gh pr view "$PR" --json headRefName --jq .headRefName 2>/dev/null)
if [ -z "$BRANCH" ]; then
    echo "PR #$PR: not found" >&2
    exit 1
fi

echo "PR #$PR  branch=$BRANCH"

OUT=$(gh pr checks "$PR" --json bucket,state,workflow,name \
        --jq '.[] | "\(.bucket)\t\(.state)\t\(.workflow // "-")\t\(.name)"' 2>&1)
CODE=$?

if [ "$CODE" -ne 0 ] && [ "$CODE" -ne 8 ]; then
    # No checks at all is a normal state on a fresh PR, not an error.
    case "$OUT" in
        *"no checks reported"*) echo "no checks reported yet"; exit 0 ;;
        *) echo "$OUT" >&2; exit "$CODE" ;;
    esac
fi

echo "$OUT"

FAILED=$(printf '%s\n' "$OUT" | grep -c '^fail' || true)
echo "---"
echo "failed=$FAILED"
