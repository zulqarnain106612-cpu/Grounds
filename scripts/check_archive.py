#!/usr/bin/env python3
"""Report what the Xcode archive contains, and refuse an unsigned one.

`docs/TRACEABILITY.md` row `phase6/appstore-cert-checklist` closes on an
archive. An archive that exists is not the same as an archive that can be
uploaded, and the difference is invisible until App Store Connect rejects it:
xcodebuild succeeds while producing a `.xcarchive` whose payload was never
signed, and the error arrives at upload time with a release date already
communicated.

So: the archive must exist, contain an app, and that app must carry an
embedded provisioning profile and a code signature directory. Those are the
two artefacts cloud signing is supposed to have put there, and their absence
is the only mechanically checkable form of "this will be rejected".

What this cannot check is whether the signature is *valid* for the intended
distribution channel -- that is `xcrun altool`/`notarytool` at upload, and
`docs/APPSTORE_SUBMISSION.md` says so.

Takes no arguments; ARCHIVE is a literal. See check_ios_export.py.
"""
from __future__ import annotations

import plistlib
import sys
from pathlib import Path

ARCHIVE = Path("build/JetFighter.xcarchive")
APPS = ARCHIVE / "Products" / "Applications"
INFO = ARCHIVE / "Info.plist"


def _app_bundle() -> Path | None:
    if not APPS.is_dir():
        return None
    bundles = sorted(p for p in APPS.iterdir() if p.suffix == ".app")
    return bundles[0] if bundles else None


def _archive_version() -> str:
    """Version and build number, read from the archive's own Info.plist.

    Reported because a rejected upload is very often a build number that did
    not move, and the number is otherwise only visible inside Xcode.
    """
    try:
        with INFO.open("rb") as handle:
            props = plistlib.load(handle)
    except (OSError, plistlib.InvalidFileException):
        return "unknown (archive Info.plist unreadable)"
    app = props.get("ApplicationProperties", {})
    version = app.get("CFBundleShortVersionString", "?")
    build = app.get("CFBundleVersion", "?")
    identifier = app.get("CFBundleIdentifier", "?")
    return f"{identifier} {version} ({build})"


def main() -> int:
    if not ARCHIVE.is_dir():
        print(f"::error::{ARCHIVE} does not exist: xcodebuild produced no archive.")
        return 1

    app = _app_bundle()
    if app is None:
        print(f"::error::{ARCHIVE} contains no .app under Products/Applications.")
        print("::error::The archive step succeeded but staged nothing, which reaches")
        print("::error::App Store Connect as a rejected upload rather than as a build")
        print("::error::failure.")
        return 1

    problems = []
    if not (app / "embedded.mobileprovision").exists():
        problems.append(
            "no embedded.mobileprovision: -allowProvisioningUpdates did not "
            "obtain a profile, so this build cannot be installed or uploaded"
        )
    if not (app / "_CodeSignature").is_dir():
        problems.append(
            "no _CodeSignature directory: the payload was never signed"
        )

    if problems:
        for problem in problems:
            print(f"::error::{problem}")
        return 1

    print("### Xcode archive")
    print()
    print(f"- App: `{app.name}`")
    print(f"- Identity: {_archive_version()}")
    print("- Embedded provisioning profile: present")
    print("- Code signature: present")
    print()
    print("Signature *validity* is checked at upload, not here -- see")
    print("`docs/APPSTORE_SUBMISSION.md`.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
