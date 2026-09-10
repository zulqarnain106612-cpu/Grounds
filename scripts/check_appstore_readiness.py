#!/usr/bin/env python3
"""Gate the App Store submission checklist.

iOS 17 requires a `PrivacyInfo.xcprivacy` for many third-party SDKs, and a
missing one is rejected at upload -- after the archive, after the build, with a
release date already communicated. That is the most expensive place in this
whole project to discover a missing file, so it is checked here instead.

What this can check: that every manifest the checklist names exists, is valid
XML, declares the required-reason APIs the checklist says it needs, and agrees
with itself about tracking. What it cannot: whether the declarations are
*true*. That is a human reading the SDK's documentation, and
`docs/APPSTORE_SUBMISSION.md` says so.

Usage:
    python scripts/check_appstore_readiness.py [--checklist config/appstore.checklist.json]
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


class ChecklistError(Exception):
    """The checklist itself is unusable."""


def load_checklist(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except FileNotFoundError as e:
        raise ChecklistError(f"{path} does not exist") from e
    except json.JSONDecodeError as e:
        raise ChecklistError(f"{path} is not valid JSON ({e})") from e


def _plist_strings(root: ET.Element) -> set[str]:
    return {node.text for node in root.iter("string") if node.text}


def check_manifest(entry: dict, repo_root: Path) -> list[str]:
    """Problems with one privacy manifest."""
    problems: list[str] = []
    sdk = entry.get("sdk", "<unnamed>")
    relative = entry.get("path")
    if not relative:
        return [f"{sdk}: no manifest path declared"]

    path = repo_root / relative
    if not path.exists():
        # The rejection-at-upload case -- but only a failure for a manifest
        # this project authors. An SDK's manifest ships inside its package, so
        # before that SDK is installed its absence is a fact about the
        # integration schedule rather than a defect. It becomes blocking under
        # --release, which is the last moment it can be caught for free.
        return [f"{sdk}: privacy manifest missing at {relative}"]

    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        return [f"{sdk}: {relative} is not parseable XML ({e})"]

    if root.tag != "plist":
        problems.append(f"{sdk}: {relative} root is <{root.tag}>, expected <plist>")

    declared = _plist_strings(root)
    for api in entry.get("required_reason_apis", []):
        if api not in declared:
            # Declaring the API but not its reason is the same rejection as
            # not declaring it at all.
            problems.append(f"{sdk}: {relative} does not declare {api}")

    tracking_claimed = bool(entry.get("tracking"))
    tracking_in_file = "NSPrivacyTracking" in ET.tostring(root, encoding="unicode")
    if tracking_claimed and not tracking_in_file:
        problems.append(f"{sdk}: checklist says it tracks, but {relative} has no NSPrivacyTracking key")

    return problems


def check(checklist: dict, repo_root: Path, require_sdks: bool = False) -> tuple[list[str], list[str]]:
    """Problems and outstanding items.

    Split deliberately. A missing manifest this project owns is a defect now.
    A missing SDK manifest before that SDK is installed is a scheduled task,
    and reporting the two identically would train everyone to ignore both.
    Under `require_sdks` -- release time -- everything is a problem.
    """
    problems: list[str] = []
    outstanding: list[str] = []

    manifests = checklist.get("privacy_manifests") or []
    if not manifests:
        problems.append("no privacy manifests declared; iOS 17 requires them for most SDKs")
    for entry in manifests:
        found = check_manifest(entry, repo_root)
        owned = entry.get("provided_by", "project") == "project"
        if owned or require_sdks:
            problems.extend(found)
        else:
            outstanding.extend(found)

    # A tracking SDK without the usage description string is a guaranteed
    # rejection, and the two live in different files so they drift.
    tracks = any(entry.get("tracking") for entry in manifests)
    keys = {item.get("key") for item in checklist.get("info_plist_keys") or []}
    if tracks and "NSUserTrackingUsageDescription" not in keys:
        problems.append(
            "an SDK declares tracking but NSUserTrackingUsageDescription is not in the checklist")

    if "ITSAppUsesNonExemptEncryption" not in keys:
        # Omitting it means answering the export-compliance question by hand
        # on every single upload.
        problems.append("ITSAppUsesNonExemptEncryption is not declared, so every upload will prompt")

    if not checklist.get("manual_gates"):
        problems.append("no manual gates listed; the human half of submission is undocumented")

    ios_config = repo_root / "config" / "ios.build.json"
    if ios_config.exists():
        ios = json.loads(ios_config.read_text())
        settings = ios.get("player_settings", {})
        # Two files naming the bundle id is two places to change it, and the
        # one that is wrong is discovered at upload.
        if settings.get("bundle_identifier") != checklist.get("bundle_identifier"):
            problems.append(
                f"bundle id disagrees: ios.build.json has "
                f"{settings.get('bundle_identifier')!r}, checklist has "
                f"{checklist.get('bundle_identifier')!r}")
        if settings.get("target_minimum_ios_version") != checklist.get("minimum_ios_version"):
            problems.append("minimum iOS version disagrees between ios.build.json and the checklist")

    return problems, outstanding


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checklist", type=Path,
                        default=ROOT / "config" / "appstore.checklist.json")
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--release", action="store_true",
                        help="treat SDK-provided manifests as required, for a submission build")
    args = parser.parse_args(argv)

    try:
        checklist = load_checklist(args.checklist)
    except ChecklistError as e:
        print(f"::error::{e}")
        return 1

    problems, outstanding = check(checklist, args.root, require_sdks=args.release)

    for item in outstanding:
        # A notice, not an error: these arrive with their SDK, and --release
        # is where they stop being acceptable.
        print(f"::notice::outstanding until the SDK is installed: {item}")
    for problem in problems:
        print(f"::error::{problem}")

    if problems:
        print(f"{len(problems)} submission problem(s); this archive would be rejected or prompt")
        return 1

    if outstanding:
        print(f"checklist consistent; {len(outstanding)} item(s) outstanding pending SDK installation. "
              f"Re-run with --release before submitting.")
    else:
        print("submission checklist: every declared privacy manifest is present and consistent")
    return 0


if __name__ == "__main__":
    sys.exit(main())
