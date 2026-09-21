#!/usr/bin/env python3
"""Write the C# coverage number into the job summary. Reports; does not gate.

No floor is enforced on this number yet, and that is a deliberate, temporary
state rather than an oversight. The only runtime code in the repo is
JetController, JetFlightConfig and QualityTierManager, and the last of those
reads SystemInfo in Awake and has no test at all. A floor picked today would
be whatever number the current code happens to hit, which is exactly how a
gate becomes decoration -- the thing scripts/check_unity_results.py exists to
prevent on the other axis.

The floor lands with the cell that gives QualityTierManager a testable seam.
Until then this prints the number so the trend is visible in every run.

The report goes to stdout. The workflow appends it to the job summary with
a shell redirect rather than this script opening $GITHUB_STEP_SUMMARY: the
redirect is the same result with one less path for this process to touch.

Usage:
    python scripts/report_unity_coverage.py <test-mode>
"""
from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# The test modes unity-test.yml runs, mapped to the directory each one writes.
#
# A lookup, not `Path(f"{mode}-artifacts")`. Validating the mode and then
# interpolating it still carries argv into the path, and CodeQL does not treat
# a membership test as a barrier -- it kept flagging the search below, which is
# fair: a guard is only as good as the next edit that moves it. Reading the
# path out of this table means the value used is a constant either way, so
# nothing derived from input reaches a path expression at all.
ARTIFACTS = {
    "editmode": Path("editmode-artifacts"),
    "playmode": Path("playmode-artifacts"),
}
MODES = tuple(ARTIFACTS)

# Where the coverage report actually lands, which is NOT under artifactsPath.
#
# game-ci/unity-test-runner declares its coverage output separately from its
# test results -- `core.setOutput('coveragePath', 'CodeCoverage')` in the
# pinned commit -- so it writes to a CodeCoverage directory at the workspace
# root while the results XML goes to artifactsPath. This script searched the
# artifacts directory only, found nothing on every run, and printed "No
# coverage summary was produced" under a green check. Nobody reads a line that
# says a report is missing when the job is green, so the number was never
# measured and docs/VERIFICATION.md described it as reported-but-not-gated
# when it was not reported at all.
#
# Both are searched now. A literal, for the same reason ARTIFACTS is one: no
# path here comes from input. The two test modes run in separate jobs with
# separate workspaces, so one name needs no per-mode variant.
COVERAGE = Path("CodeCoverage")


def find_summary(artifacts: Path) -> Path | None:
    """The first Summary.xml in either place the report might be.

    The package's exact depth has moved between versions, so each root is
    searched rather than hardcoded. Artifacts first: if a future runner
    version does start writing there, that copy is the one belonging to this
    test mode.
    """
    for root in (artifacts, COVERAGE):
        if not root.is_dir():
            continue
        for candidate in sorted(root.rglob("Summary.xml")):
            return candidate
    return None


def line_coverage(summary: Path) -> str | None:
    """The one number worth surfacing, wherever this package version put it."""
    try:
        root = ET.parse(summary).getroot()
    except (OSError, ET.ParseError):
        return None
    node = root.find(".//Summary")
    node = root if node is None else node
    text = node.findtext("Linecoverage")
    if text:
        return text.strip()
    for key in ("linecoverage", "Linecoverage"):
        if node.get(key):
            return node.get(key)
    return None


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(f"usage: report_unity_coverage.py <{'|'.join(MODES)}>", file=sys.stderr)
        return 2
    mode = argv[1]
    artifacts = ARTIFACTS.get(mode)
    if artifacts is None:
        print(f"unknown test mode {mode!r}; expected one of {', '.join(MODES)}",
              file=sys.stderr)
        return 2

    lines = [f"## C# coverage -- {mode}", ""]
    summary = find_summary(artifacts)
    if summary is None:
        lines.append(
            f"No coverage summary was produced: no `Summary.xml` under "
            f"`{artifacts}` or `{COVERAGE}`. The test suites themselves still "
            f"gated in the step above; only this report is missing."
        )
    else:
        value = line_coverage(summary)
        if value is None:
            lines.append(f"Found `{summary}` but could not read a line-coverage figure from it.")
        else:
            lines.append(f"Line coverage: **{value}%** — reported, not gated (see this script's docstring).")

    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
