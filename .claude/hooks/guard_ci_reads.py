#!/usr/bin/env python3
"""Deny repo-wide CI listings and full log reads, deterministically.

Permission rules match a command prefix. That is enough for `gh run view`, but
not for `gh api .../actions/runs/<id>/logs`, because a prefix rule cannot
constrain arguments -- Claude Code's own permissions docs call that pattern
fragile. A PreToolUse hook receives the entire command string instead, so it
catches the piped, substituted and API forms alike.

Blocks, per the repo policy in CLAUDE.md:
  * `gh run list`   -- repo-wide, every branch, no PR
  * `gh run view`   -- full logs, however they are piped afterwards
  * `gh run watch`  -- blocking on CI, already PROHIBITED by agent.config.json
  * `gh api` against actions runs/jobs/logs endpoints

The PR-scoped scripts are unaffected: a hook sees the command Claude runs, not
the subprocesses a script spawns, so scripts/pr_status.sh and
scripts/pr_failed_log_tail.sh remain the only path to this data.

Exit 0 allows, exit 2 denies and shows stderr to Claude.
"""
from __future__ import annotations

import json
import re
import sys

_RULES: list[tuple[re.Pattern[str], str]] = [
    (
        re.compile(r"\bgh\s+run\s+list\b"),
        "`gh run list` is repo-wide: it returns runs from every branch, with no PR anywhere in the result.",
    ),
    (
        re.compile(r"\bgh\s+run\s+view\b"),
        "`gh run view` returns the whole log. Piping it to head/tail still downloads all of it first.",
    ),
    (
        re.compile(r"\bgh\s+run\s+watch\b"),
        "`gh run watch` blocks on a CI run, which config/agent.config.json marks blocking_on_ci_runs: PROHIBITED.",
    ),
    (
        re.compile(r"\bgh\s+api\b[^|;&]*\bactions/(runs|jobs)\b"),
        "`gh api` against actions runs/jobs is the same repo-wide or full-log read by another route.",
    ),
]

_GUIDANCE = """
Use the PR-scoped commands instead:

  make ci-status PR=<n>              status for that PR only
  make ci-logs   PR=<n>              2-line error probe (reports error_line/total_lines)
  make ci-logs   PR=<n> LINES=12 OFFSET=17

Read the probe first, then ask for exactly the number of lines it justifies.
""".rstrip()


def main() -> int:
    try:
        event = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        # Never fail closed on a malformed event: that would block every Bash
        # call in the session over a parsing bug in this guard.
        return 0

    if event.get("tool_name") != "Bash":
        return 0

    command = (event.get("tool_input") or {}).get("command") or ""

    for pattern, reason in _RULES:
        if pattern.search(command):
            print(f"Blocked by .claude/hooks/guard_ci_reads.py: {reason}", file=sys.stderr)
            print(_GUIDANCE, file=sys.stderr)
            return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())
