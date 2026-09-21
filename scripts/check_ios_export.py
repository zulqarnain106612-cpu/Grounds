#!/usr/bin/env python3
"""Say whether Unity produced an Xcode project worth archiving.

The failure this catches: `game-ci/unity-builder` can finish having written
an incomplete tree, and the next job is a macOS runner. Discovering there that
`Unity-iPhone.xcodeproj` is missing costs the whole archive job -- roughly ten
times a Linux runner -- to learn something a directory listing on Linux knows
in milliseconds.

This one *does* gate, unlike the coverage reporter: a missing Xcode project
is not a missing nice-to-have, it is the artefact the rest of the pipeline
consumes.

Takes no arguments. EXPORT_DIR is a literal for the same reason
`report_unity_coverage.py` has an ARTIFACTS table: a path derived from argv or
from the environment is a CodeQL py/path-injection sink, and there is nothing
to gain by having one when the only caller passes a constant anyway.
"""
from __future__ import annotations

import sys
from pathlib import Path

EXPORT_DIR = Path("build/iOS")
XCODE_PROJECT = EXPORT_DIR / "Unity-iPhone.xcodeproj"

# What an iOS export always contains. Each is checked because each has its own
# failure: no .xcodeproj means nothing to archive at all; no project.pbxproj
# means the bundle exists but is empty, which xcodebuild reports as a parse
# error rather than as a missing file; no Data means the player built but its
# content was not staged, which produces an app that launches to nothing.
REQUIRED = (
    XCODE_PROJECT,
    XCODE_PROJECT / "project.pbxproj",
    EXPORT_DIR / "Data",
    EXPORT_DIR / "Classes",
    EXPORT_DIR / "Info.plist",
)


def _size_mb(path: Path) -> float:
    total = sum(f.stat().st_size for f in path.rglob("*") if f.is_file())
    return total / (1024 * 1024)


def main() -> int:
    if not EXPORT_DIR.is_dir():
        print(f"::error::{EXPORT_DIR} does not exist: Unity wrote no export.")
        print("::error::The builder step's own log says why. A build that failed")
        print("::error::still exits 0 in batchmode unless the entry point stops it,")
        print("::error::which is what IOSBuild.PerformBuild is for.")
        return 1

    missing = [str(path) for path in REQUIRED if not path.exists()]
    if missing:
        print("::error::The export is incomplete; these are missing:")
        for path in missing:
            print(f"::error::  {path}")
        return 1

    print("### iOS export")
    print()
    print(f"- Xcode project: `{XCODE_PROJECT}`")
    print(f"- Export size: {_size_mb(EXPORT_DIR):.1f} MB")
    print()
    print("Ready to archive.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
