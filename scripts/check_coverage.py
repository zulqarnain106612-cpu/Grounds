#!/usr/bin/env python3
"""Enforce a coverage floor on every axis a Cobertura report actually carries.

GitHub's `code_coverage` ruleset rule evaluates **line coverage only**, and it
evaluates it on the aggregate. That leaves two ways to sit above the threshold
while a real gap exists:

  * a single badly-covered file hides behind a well-covered total, and
  * branch coverage rots while line coverage stays high (every line executed,
    only one side of each condition taken).

This script closes both. It reads coverage.xml and fails if the total, any
module (Cobertura "package"), or any single file falls below the threshold, on
line coverage *and* on branch coverage.

Branch coverage is computed from the per-line `condition-coverage` attributes
rather than Cobertura's `branch-rate`, because coverage.py emits branch-rate
0.0 for a file that contains no branches at all -- trusting it would fail
perfectly-covered branchless files. Files and modules with no branches skip the
branch check instead, which is the honest reading of "no branches to cover".

Usage:  python scripts/check_coverage.py [--min 99] [--report coverage.xml]
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

_CONDITION_RE = re.compile(r"\((\d+)/(\d+)\)")


class Tally:
    __slots__ = ("lines_total", "lines_hit", "branches_total", "branches_hit")

    def __init__(self) -> None:
        self.lines_total = 0
        self.lines_hit = 0
        self.branches_total = 0
        self.branches_hit = 0

    def add_line(self, element: ET.Element) -> None:
        self.lines_total += 1
        if int(element.get("hits", "0")) > 0:
            self.lines_hit += 1
        if element.get("branch") == "true":
            match = _CONDITION_RE.search(element.get("condition-coverage", ""))
            if match:
                hit, total = int(match.group(1)), int(match.group(2))
                self.branches_hit += hit
                self.branches_total += total

    def merge(self, other: "Tally") -> None:
        self.lines_total += other.lines_total
        self.lines_hit += other.lines_hit
        self.branches_total += other.branches_total
        self.branches_hit += other.branches_hit

    @property
    def line_pct(self) -> float | None:
        return None if self.lines_total == 0 else 100.0 * self.lines_hit / self.lines_total

    @property
    def branch_pct(self) -> float | None:
        return None if self.branches_total == 0 else 100.0 * self.branches_hit / self.branches_total


def _fmt(pct: float | None) -> str:
    return "  n/a " if pct is None else f"{pct:6.2f}%"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=float, default=99.0, help="minimum percentage (default: 99)")
    parser.add_argument("--report", default="coverage.xml", help="Cobertura XML path")
    args = parser.parse_args()

    report = Path(args.report)
    if not report.exists():
        # A missing report must never read as a pass -- that is the failure mode
        # that turns a coverage gate into decoration.
        print(f"::error::coverage report not found at {report}; the gate cannot pass without one")
        return 1

    root = ET.parse(report).getroot()

    total = Tally()
    modules: dict[str, Tally] = {}
    files: dict[str, Tally] = {}

    # Pass 1: tally every file from its <line> elements.
    for klass in root.iter("class"):
        file_name = klass.get("filename") or klass.get("name") or "(unknown)"
        tally = files.setdefault(file_name, Tally())
        for line in klass.iter("line"):
            tally.add_line(line)

    # Pass 2: roll files up into their module, and every file into the total.
    # Done from the file tallies rather than by re-walking <line>, so a file
    # appearing under two packages is counted once.
    for package in root.iter("package"):
        module = modules.setdefault(package.get("name") or "(root)", Tally())
        for klass in package.iter("class"):
            module.merge(files[klass.get("filename") or klass.get("name") or "(unknown)"])
    for tally in files.values():
        total.merge(tally)

    if not files:
        print("::error::coverage report contains no files; refusing to pass an empty gate")
        return 1

    failures: list[str] = []

    def check(scope: str, name: str, tally: Tally) -> None:
        for metric, pct in (("line", tally.line_pct), ("branch", tally.branch_pct)):
            if pct is not None and pct < args.min:
                failures.append(f"{scope} {name}: {metric} coverage {pct:.2f}% < {args.min:g}%")

    print(f"{'scope':<8} {'line':>7} {'branch':>7}  name")
    print("-" * 72)
    print(f"{'TOTAL':<8} {_fmt(total.line_pct)} {_fmt(total.branch_pct)}")
    check("total", "", total)

    for name in sorted(modules):
        print(f"{'module':<8} {_fmt(modules[name].line_pct)} {_fmt(modules[name].branch_pct)}  {name}")
        check("module", name, modules[name])

    for name in sorted(files):
        print(f"{'file':<8} {_fmt(files[name].line_pct)} {_fmt(files[name].branch_pct)}  {name}")
        check("file", name, files[name])

    print("-" * 72)
    if failures:
        for failure in failures:
            print(f"::error::{failure}")
        print(f"\n{len(failures)} coverage threshold failure(s) at >={args.min:g}%")
        return 1

    print(f"all scopes at or above {args.min:g}% on line and branch coverage")
    return 0


if __name__ == "__main__":
    sys.exit(main())
