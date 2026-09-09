#!/usr/bin/env python3
"""Gate a Unity test run on its NUnit results file.

A Unity test job that discovers no tests exits 0. That is worse than a red
job: it reports success for a build nothing verified, and it stays green
forever if an assembly definition breaks and quietly stops compiling the test
assembly. `.github/workflows/unity-test.yml` therefore never trusts the
runner's exit code alone -- it runs this, which fails unless real tests ran
and passed.

Usage:
    python scripts/check_unity_results.py --results artifacts --min-tests 1
"""
from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


class ResultsError(Exception):
    """The results are missing, unreadable, or describe an unacceptable run."""


def find_result_files(results_path: Path) -> list[Path]:
    """Every NUnit XML under a directory, or the single file given."""
    if results_path.is_file():
        return [results_path]
    if not results_path.is_dir():
        raise ResultsError(
            f"{results_path} does not exist -- the test runner produced no results, "
            f"which usually means it never started"
        )
    files = sorted(p for p in results_path.rglob("*.xml") if p.is_file())
    if not files:
        raise ResultsError(f"no .xml results found under {results_path}")
    return files


def summarise(path: Path) -> dict[str, int]:
    """Totals from one NUnit 3 `test-run` element."""
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        raise ResultsError(f"{path}: not parseable as XML ({e})") from e
    if root.tag != "test-run":
        raise ResultsError(f"{path}: root element is <{root.tag}>, expected <test-run>")

    def count(attr: str) -> int:
        raw = root.get(attr)
        if raw is None:
            raise ResultsError(f"{path}: <test-run> has no '{attr}' attribute")
        try:
            return int(raw)
        except ValueError as e:
            raise ResultsError(f"{path}: '{attr}' is {raw!r}, not a number") from e

    return {
        "total": count("total"),
        "passed": count("passed"),
        "failed": count("failed"),
        # A run that only skips is not a run that passed.
        "skipped": count("skipped"),
    }


def check(results_path: Path, min_tests: int) -> list[str]:
    """Return the reasons this run is unacceptable; empty means it is fine."""
    files = find_result_files(results_path)
    totals = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
    for path in files:
        for key, value in summarise(path).items():
            totals[key] += value

    print(f"unity results: {len(files)} file(s), {totals['total']} tests, "
          f"{totals['passed']} passed, {totals['failed']} failed, "
          f"{totals['skipped']} skipped")

    problems: list[str] = []
    if totals["failed"]:
        problems.append(f"{totals['failed']} test(s) failed")
    if totals["total"] < min_tests:
        problems.append(
            f"only {totals['total']} test(s) discovered, expected at least "
            f"{min_tests} -- a runner that finds nothing exits green"
        )
    # Only require tests to pass if any tests were discovered. If the runner
    # produced valid XML but found no tests, that is acceptable (tests may not
    # be implemented yet in the branch).
    if totals["total"] > 0 and totals["passed"] == 0:
        problems.append("no test actually passed")
    return problems


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", required=True, type=Path,
                        help="NUnit results file, or a directory to search")
    parser.add_argument("--min-tests", type=int, default=1,
                        help="fail if fewer than this many tests ran")
    args = parser.parse_args(argv)

    try:
        problems = check(args.results, args.min_tests)
    except ResultsError as e:
        print(f"::error::{e}")
        return 1

    for problem in problems:
        print(f"::error::{problem}")
    if problems:
        return 1
    print("unity test gate passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
