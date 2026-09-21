"""Cycle 6, branch phase6/settings-ui (spec item 2c).

Criterion: a manual quality override persists across relaunch **and visibly
changes rendering**.

Both halves have a specific failure. An override that does not persist makes
the setting look broken. One that persists *without applying* makes it look
like a lie -- the toggle stays where the player put it and nothing changes --
and that is the harder half to notice in a playtest, because the UI is
correct.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
SETTINGS = SCRIPTS / "Settings" / "SettingsUI.cs"


def _method_body(path: Path, signature: str) -> str:
    source = code(path)
    start = source.index(signature)
    depth = 0
    for j in range(source.index("{", start), len(source)):
        if source[j] == "{":
            depth += 1
        elif source[j] == "}":
            depth -= 1
            if depth == 0:
                return source[start:j + 1]
    raise AssertionError(f"unbalanced braces after {signature}")


def test_setting_an_override_applies_before_it_saves():
    """A player who changes a setting and force-quits should still see the
    change they made, and a save that succeeded while the apply failed is the
    worse ordering."""
    body = _method_body(SETTINGS, "public void SetTierOverride(QualityTierManager.Tier tier, int frameRate = 0)")
    assert body.index("Apply()") < body.index("Save()")


def test_applying_goes_through_the_tier_manager():
    """Two places applying a tier is how a device ends up on low-tier visuals
    with a high-tier enemy count."""
    body = _method_body(SETTINGS, "public void Apply()")
    assert "QualityTierManager.ApplySettings(EffectiveTier)" in body
    assert "QualitySettings." not in code(SETTINGS), \
        "settings reaches past QualityTierManager into the engine directly"


def test_the_override_is_applied_on_load_not_just_remembered():
    """The half that makes the setting look like a lie."""
    body = _method_body(SETTINGS, "private void Awake()")
    assert body.index("Load()") < body.index("Apply()")


def test_detection_is_never_overwritten_by_an_override():
    """A player who picked High on a device that cannot sustain it needs a way
    back to "whatever this device can do", which is impossible if detection
    was replaced rather than layered over."""
    source = code(SETTINGS)
    assert "EffectiveTier => hasOverride ? overrideTier : DetectedTier" in source
    body = _method_body(SETTINGS, "public void SetTierOverride(QualityTierManager.Tier tier, int frameRate = 0)")
    assert "DetectedTier =" not in body


def test_clearing_returns_to_detection():
    body = _method_body(SETTINGS, "public void ClearOverride()")
    assert "hasOverride = false" in body
    assert "Apply()" in body and "Save()" in body


def test_the_low_tier_frame_rate_matches_what_the_game_was_tuned_against():
    """30 on low is the number the gun's cooldown and the banking convergence
    were both written against."""
    body = _method_body(SETTINGS, "public static int DefaultFrameRateFor(QualityTierManager.Tier tier)")
    assert "Tier.Low ? 30 : 60" in body


def test_a_persisted_tier_is_clamped_on_read():
    """A settings file from a newer build, or a hand-edited one, must not put
    the game on a tier that does not exist."""
    body = _method_body(SETTINGS, "public bool Load()")
    assert "Mathf.Clamp(" in body


def test_a_corrupt_settings_file_falls_back_to_detection():
    """Always a playable state. Refusing to start over a preference would be
    absurd."""
    body = _method_body(SETTINGS, "public bool Load()")
    assert "catch (Exception" in body
    assert "hasOverride = false" in body


def test_the_settings_write_is_atomic():
    body = _method_body(SETTINGS, "public bool Save()")
    assert ".tmp" in body
    assert "File.Move(temporary, path)" in body


def test_settings_are_not_stored_in_the_wallet_save():
    """Same reasoning as the remove-ads entitlement: the wallet save is the
    file most likely to be rewritten, and a preference is not worth risking an
    economy migration for."""
    source = code(SETTINGS)
    assert 'FileName = "settings.json"' in source
    assert "SaveService" not in source


def test_changes_are_announced():
    body = _method_body(SETTINGS, "public void Apply()")
    assert "OnSettingsChanged?.Invoke" in body


def test_the_settings_ui_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["SettingsUI"]["namespace"] == "JetFighter.Settings"


def test_both_halves_of_the_criterion_are_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "SettingsUITests.cs").read_text()
    assert "TheOverrideSurvivesARelaunch" in source
    assert "TheOverrideIsAppliedOnRelaunchNotJustRemembered" in source
    assert "AnOverrideVisiblyChangesTheRenderBudget" in source


def test_phase_1s_tier_manager_was_not_rewritten():
    """It was written in Cycle 0 with ApplySettings public precisely so this
    cell could layer on top of it."""
    source = code(SCRIPTS / "Build" / "QualityTierManager.cs")
    assert "SettingsUI" not in source
    assert "public static void ApplySettings(Tier tier)" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase6_settings_ui")
    assert "SettingsUI" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase6/settings-ui`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
