"""Cycle 6, branch phase6/appstore-cert-checklist (ADR-006).

Criterion: archives cleanly with all required Privacy Manifests. An Xcode
archive is the criterion, so **the row stays open**.

What ships is the part that is cheap to automate and expensive to get wrong: a
missing `PrivacyInfo.xcprivacy` is rejected *at upload* -- after the archive,
after the build, with a release date already communicated. That is the most
expensive place in this project to discover a missing file.
"""
from __future__ import annotations

import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

REAL_ROOT = Path(__file__).resolve().parent.parent
CHECKLIST_PATH = REAL_ROOT / "config" / "appstore.checklist.json"
SCRIPT = REAL_ROOT / "scripts" / "check_appstore_readiness.py"
OWN_MANIFEST = REAL_ROOT / "Assets" / "_Game" / "Plugins" / "iOS" / "PrivacyInfo.xcprivacy"

sys.path.insert(0, str(REAL_ROOT / "scripts"))
import check_appstore_readiness as gate  # noqa: E402


@pytest.fixture
def checklist() -> dict:
    return json.loads(CHECKLIST_PATH.read_text())


def test_the_row_is_not_ticked():
    """An Xcode archive is the criterion, and a green checker is not one."""
    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase6/appstore-cert-checklist`" in l and l.startswith("|")]
    assert len(row) == 1
    assert row[0].startswith("| [ ] |"), "the submission row was ticked without an archive"
    assert "APPSTORE_SUBMISSION" in row[0], "the row does not say what is still owed"


def test_the_checklist_passes_in_its_current_state():
    """Everything this project owns is present; the SDK manifests are
    outstanding rather than broken."""
    problems, outstanding = gate.check(json.loads(CHECKLIST_PATH.read_text()), REAL_ROOT)
    assert problems == [], f"submission problems: {problems}"
    assert outstanding, "the SDK manifests should be reported as outstanding"


def test_release_mode_blocks_on_the_sdk_manifests():
    """The last moment an SDK manifest can be caught for free."""
    problems, _ = gate.check(json.loads(CHECKLIST_PATH.read_text()), REAL_ROOT, require_sdks=True)
    assert problems, "--release accepted a build with missing SDK manifests"


def test_outstanding_and_broken_are_reported_differently():
    """Reporting both identically would train everyone to ignore both."""
    source = SCRIPT.read_text()
    assert "::notice::" in source
    assert "::error::" in source


def test_the_projects_own_manifest_exists_and_parses():
    """Ours is a defect now, not a scheduled task."""
    assert OWN_MANIFEST.exists()
    root = ET.parse(OWN_MANIFEST).getroot()
    assert root.tag == "plist"


def test_the_own_manifest_declares_the_apis_the_game_actually_uses(checklist):
    """SaveService, RemoveAdsFlag and SettingsUI all touch persistentDataPath
    and call File.Exists, which reads a file timestamp."""
    entry = next(e for e in checklist["privacy_manifests"] if e["sdk"] == "JetFighter")
    declared = {n.text for n in ET.parse(OWN_MANIFEST).getroot().iter("string") if n.text}
    for api in entry["required_reason_apis"]:
        assert api in declared


def test_every_required_reason_api_has_a_reason_code():
    """Declaring the API but not its reason is the same rejection as not
    declaring it at all."""
    text = OWN_MANIFEST.read_text()
    assert "NSPrivacyAccessedAPITypeReasons" in text
    assert "C617.1" in text and "CA92.1" in text


def test_a_missing_owned_manifest_is_caught(tmp_path):
    problems = gate.check_manifest(
        {"sdk": "JetFighter", "path": "does/not/exist.xcprivacy", "provided_by": "project"},
        tmp_path)
    assert problems and "missing" in problems[0]


def test_a_malformed_manifest_is_caught(tmp_path):
    bad = tmp_path / "PrivacyInfo.xcprivacy"
    bad.write_text("<plist><dict>")
    problems = gate.check_manifest({"sdk": "X", "path": "PrivacyInfo.xcprivacy"}, tmp_path)
    assert problems and "parseable" in problems[0]


def test_an_undeclared_required_reason_api_is_caught(tmp_path):
    manifest = tmp_path / "PrivacyInfo.xcprivacy"
    manifest.write_text("<plist><dict><key>NSPrivacyTracking</key><false/></dict></plist>")
    problems = gate.check_manifest({
        "sdk": "X", "path": "PrivacyInfo.xcprivacy",
        "required_reason_apis": ["NSPrivacyAccessedAPICategoryUserDefaults"],
    }, tmp_path)
    assert problems and "does not declare" in problems[0]


def test_a_tracking_sdk_without_a_usage_description_is_caught(tmp_path):
    """They live in different files, so they drift -- and the combination is a
    guaranteed rejection."""
    problems, _ = gate.check({
        "privacy_manifests": [{"sdk": "Ads", "path": "x", "tracking": True, "provided_by": "sdk"}],
        "info_plist_keys": [{"key": "ITSAppUsesNonExemptEncryption"}],
        "manual_gates": ["something"],
    }, tmp_path)
    assert any("NSUserTrackingUsageDescription" in p for p in problems)


def test_the_export_compliance_key_is_required(tmp_path):
    """Omitting it means answering the question by hand on every upload."""
    problems, _ = gate.check({
        "privacy_manifests": [{"sdk": "X", "path": "x", "provided_by": "sdk"}],
        "info_plist_keys": [],
        "manual_gates": ["something"],
    }, tmp_path)
    assert any("ITSAppUsesNonExemptEncryption" in p for p in problems)


def test_the_bundle_id_must_agree_with_the_build_config(checklist):
    """Two files naming the bundle id is two places to change it, and the one
    that is wrong is discovered at upload."""
    ios = json.loads((REAL_ROOT / "config" / "ios.build.json").read_text())
    assert ios["player_settings"]["bundle_identifier"] == checklist["bundle_identifier"]
    assert ios["player_settings"]["target_minimum_ios_version"] == checklist["minimum_ios_version"]


def test_a_disagreeing_bundle_id_is_caught(tmp_path):
    (tmp_path / "config").mkdir()
    (tmp_path / "config" / "ios.build.json").write_text(json.dumps({
        "player_settings": {"bundle_identifier": "com.other.app",
                            "target_minimum_ios_version": "15.0"}}))
    problems, _ = gate.check({
        "bundle_identifier": "com.conquerer.jetfighter",
        "minimum_ios_version": "15.0",
        "privacy_manifests": [{"sdk": "X", "path": "x", "provided_by": "sdk"}],
        "info_plist_keys": [{"key": "ITSAppUsesNonExemptEncryption"}],
        "manual_gates": ["something"],
    }, tmp_path)
    assert any("bundle id disagrees" in p for p in problems)


def test_the_manual_gates_are_written_down_not_remembered(checklist):
    gates = checklist["manual_gates"]
    assert len(gates) >= 5
    joined = " ".join(gates).lower()
    assert "product ids" in joined
    assert "sandbox" in joined


def test_the_documentation_says_what_the_checker_cannot_verify():
    """That the declarations are *true* is a person reading each SDK's
    documentation. A checker that implied otherwise would be worse than none."""
    text = (REAL_ROOT / "docs" / "APPSTORE_SUBMISSION.md").read_text()
    assert "cannot verify" in text.lower()
    assert "not that it is honest" in text.lower()


def test_the_script_runs_as_a_program(tmp_path):
    proc = subprocess.run([sys.executable, str(SCRIPT)], capture_output=True, text=True, timeout=30)
    assert proc.returncode == 0, proc.stdout + proc.stderr


def test_a_missing_checklist_is_an_error_not_a_crash():
    with pytest.raises(gate.ChecklistError):
        gate.load_checklist(REAL_ROOT / "config" / "not-a-checklist.json")


def test_the_seed_node_exists_even_though_the_row_is_open():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase6_appstore_cert_checklist")
    assert "open" in node["tags"]
