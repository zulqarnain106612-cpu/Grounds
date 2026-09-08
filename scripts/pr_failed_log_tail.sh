#!/usr/bin/env bash
# Lines from the newest FAILED run of exactly ONE pull request.
#
#   usage: scripts/pr_failed_log_tail.sh <pr-number> [lines] [offset]
#
# No <lines>  -> the fixed 2-line probe: the error line and the exit line.
#                Those two name the failure class, which is what sizes the
#                next request.
# With <lines> -> exactly that many lines, never more. Ceilinged at 50.
# [offset]     -> lines to skip from the end before the window starts, for
#                when the probe shows the cause sits above the tail.
#
# The `gh run list` / `gh run view` below are subprocesses of this script, so
# they are not what Claude Code's permission layer inspects; the deny rules on
# the raw commands stay intact and this stays the only path to a failed log.
set -uo pipefail

PROBE_LINES=2
MAX_LINES=50

PR="${1:-}"
if [ -z "$PR" ]; then
    echo "usage: scripts/pr_failed_log_tail.sh <pr-number> [lines] [offset]" >&2
    exit 2
fi

LINES="${2:-$PROBE_LINES}"
OFFSET="${3:-0}"
case "$LINES"  in ''|*[!0-9]*) LINES=$PROBE_LINES ;; esac
case "$OFFSET" in ''|*[!0-9]*) OFFSET=0 ;; esac
[ "$LINES" -lt 1 ] && LINES=1
[ "$LINES" -gt "$MAX_LINES" ] && LINES=$MAX_LINES

BRANCH=$(gh pr view "$PR" --json headRefName --jq .headRefName 2>/dev/null)
if [ -z "$BRANCH" ]; then
    echo "PR #$PR: not found" >&2
    exit 1
fi

RUN=$(gh run list --branch "$BRANCH" --status failure --limit 1 \
        --json databaseId --jq '.[0].databaseId // empty' 2>/dev/null)

if [ -z "$RUN" ]; then
    echo "PR #$PR  branch=$BRANCH: no failed run"
    exit 0
fi

LOG=$(mktemp)
trap 'rm -f "$LOG"' EXIT
# Strip the constant "<job>\t<step>\t<timestamp>Z " prefix: it is identical on
# every line and would otherwise eat most of a 2-line budget.
gh run view "$RUN" --log-failed 2>/dev/null \
    | sed -E 's/^[^\t]*\t[^\t]*\t[0-9T:.-]+Z //' > "$LOG"

if [ -z "${2:-}" ]; then
    # Probe. The last physical lines of a failed job are runner teardown
    # ("Cleaning up orphan processes", Node deprecation warnings), not the
    # cause -- verified against real runs. GitHub's own ##[error] annotations
    # are the cause, so the probe selects those.
    ERRORS=$(grep -F '##[error]' "$LOG" | tail -n "$PROBE_LINES")
    if [ -n "$ERRORS" ]; then
        TOTAL=$(wc -l < "$LOG")
        AT=$(grep -nF '##[error]' "$LOG" | tail -n 1 | cut -d: -f1)
        echo "PR #$PR  branch=$BRANCH  failed_run=$RUN  (probe: last $PROBE_LINES errors; line $AT of $TOTAL)"
        echo "$ERRORS"
        exit 0
    fi
    echo "PR #$PR  branch=$BRANCH  failed_run=$RUN  (probe: no ##[error] found, last $PROBE_LINES lines)"
    tail -n "$PROBE_LINES" "$LOG"
    exit 0
fi

if [ "$OFFSET" -eq 0 ]; then
    echo "PR #$PR  branch=$BRANCH  failed_run=$RUN  (last $LINES)"
else
    echo "PR #$PR  branch=$BRANCH  failed_run=$RUN  ($LINES lines, skipping last $OFFSET)"
fi

tail -n "$(( LINES + OFFSET ))" "$LOG" | head -n "$LINES"
