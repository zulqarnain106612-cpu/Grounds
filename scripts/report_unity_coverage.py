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

Usage:
    python scripts/report_unity_coverage.py <artifacts-dir> <test-mode>
"""
from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


# CodeQL flags the argv-derived and environment-derived paths below as
# "uncontrolled data used in a path expression". In this repo neither is
# attacker-controlled: the only caller is unity-test.yml passing a matrix
# literal (editmode/playmode), and GITHUB_STEP_SUMMARY is written by the
# runner. The containment checks cost nothing and hold whatever calls this
# later, so the alert is closed by construction rather than by annotation.

def _roots() -> list[Path]:
    """Directories this script is ever allowed to touch."""
    roots = []
    for var in ("GITHUB_WORKSPACE", "RUNNER_TEMP"):
        value = os.environ.get(var)
        if value:
            try:
                roots.append(Path(value).resolve())
            except OSError:
                pass
    if not roots:
        roots.append(Path.cwd().resolve())
    return roots


def within(path: Path, roots: list[Path]) -> Path | None:
    """`path` resolved, or None when it escapes every allowed root."""
    try:
        resolved = path.resolve()
    except OSError:
        return None
    for root in roots:
        try:
            resolved.relative_to(root)
        except ValueError:
            continue
        return resolved
    return None


def find_summary(artifacts: Path) -> Path | None:
    """Unity's coverage package writes Report/Summary.xml under the artifacts
    directory. Its exact depth has moved between package versions, so search
    rather than hardcode."""
    roots = _roots()
    safe = within(artifacts, roots)
    if safe is None or not safe.is_dir():
        return None
    for candidate in sorted(safe.rglob("Summary.xml")):
        checked = within(candidate, roots)
        if checked is not None:
            return checked
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
    if len(argv) < 3:
        print("usage: report_unity_coverage.py <artifacts-dir> <test-mode>", file=sys.stderr)
        return 2
    artifacts, mode = Path(argv[1]), argv[2]

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

    body = "\n".join(lines) + "\n"
    print(body)

    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        target = within(Path(step_summary), _roots())
        if target is not None:
            with open(target, "a", encoding="utf-8") as handle:
                handle.write(body)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
