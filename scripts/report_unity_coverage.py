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

# The test modes unity-test.yml runs, and the only values this script accepts.
# The artifacts directory is built from the chosen one rather than taken as a
# path argument: the caller passes a matrix value, so there is no reason to
# accept an arbitrary path, and not accepting one means there is no path here
# derived from input at all.
MODES = ("editmode", "playmode")


def find_summary(artifacts: Path) -> Path | None:
    """Unity's coverage package writes Report/Summary.xml under the artifacts
    directory. Its exact depth has moved between package versions, so search
    rather than hardcode."""
    if not artifacts.is_dir():
        return None
    for candidate in sorted(artifacts.rglob("Summary.xml")):
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
    if mode not in MODES:
        print(f"unknown test mode {mode!r}; expected one of {', '.join(MODES)}",
              file=sys.stderr)
        return 2
    artifacts = Path(f"{mode}-artifacts")

    lines = [f"## C# coverage -- {mode}", ""]
    summary = find_summary(artifacts)
    if summary is None:
        lines.append(
            "No coverage summary was produced. The test suites themselves still "
            "gated in the step above; only this report is missing."
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
