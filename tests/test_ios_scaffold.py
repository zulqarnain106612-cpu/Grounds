"""Cycle 0, branch phase1/build-ios-scaffold.

The branch's real acceptance criterion -- an Xcode archive on a physical
device -- needs hardware no runner has. These tests cover the half that is
mechanically checkable: the scaffold matches the layout the phase spec and
`source_globs` both assume, the symbol scanner can actually see it (that is
docs/TRACEABILITY.md's "symbols/index.json non-empty after ingest"), and the
iOS player settings the device build depends on are the values the spec
names, so a wrong bundle id or a dropped IL2CPP setting fails a PR instead of
an archive.

These read the real repo, not the tmp copy the conftest fixture builds, so
they assert what actually ships.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner

REAL_ROOT = Path(__file__).resolve().parent.parent
ASSETS = REAL_ROOT / "Assets" / "_Game"
IOS_CONFIG = REAL_ROOT / "config" / "ios.build.json"


@pytest.fixture
def ios_config() -> dict:
    return json.loads(IOS_CONFIG.read_text())


@pytest.fixture
def scaffold_symbols(monkeypatch) -> list[dict]:
    """Symbols the scanner finds in the real Assets tree.

    CONFIG_PATH is pointed back at the real config so the globs under test are
    the shipped ones; SYMBOLS_PATH stays on the conftest tmp copy, so nothing
    here writes to the tracked index.
    """
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    return symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]


# --- layout -----------------------------------------------------------------

@pytest.mark.parametrize("relative", [
    "Scripts/Build",
    "Scripts/Player",
    "Scripts/Physics",
    "Scripts/UI/Input",
    "Scripts/Weapon",
    "Scripts/Shared",
    "Editor",
])
def test_phase1_folder_layout_exists(relative):
    """docs/PHASE1_TECHNICAL_SPEC.md section 1 -- later cells drop files into
    these folders and must not have to invent the layout again."""
    assert (ASSETS / relative).is_dir(), f"missing scaffold folder Assets/_Game/{relative}"


def test_unity_project_root_files_exist():
    for relative in ("ProjectSettings/ProjectVersion.txt", "Packages/manifest.json"):
        assert (REAL_ROOT / relative).is_file(), f"{relative} missing -- Unity cannot open the project"


def test_project_pins_unity_6_lts(ios_config):
    """ADR-009: Unity 6 LTS, not the 2022 track the roadmap rejected."""
    version = (REAL_ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text()
    assert "m_EditorVersion: 6000." in version, "ProjectVersion.txt is not on the Unity 6 track"
    assert ios_config["unity_version"].startswith("6000."), "config/ios.build.json disagrees with ADR-009"
    assert ios_config["unity_version"] in version, "ios.build.json and ProjectVersion.txt name different editors"


def test_runtime_assembly_definition_is_valid_json():
    asmdef = json.loads((ASSETS / "Scripts" / "JetFighter.Runtime.asmdef").read_text())
    assert asmdef["name"] == "JetFighter.Runtime"
    assert asmdef["allowUnsafeCode"] is False


def test_editor_assembly_is_editor_only():
    """An editor assembly that leaks into a player build drags UnityEditor
    into the IL2CPP link step and fails the archive."""
    asmdef = json.loads((ASSETS / "Editor" / "JetFighter.Editor.asmdef").read_text())
    assert asmdef["includePlatforms"] == ["Editor"]
    assert "JetFighter.Runtime" in asmdef["references"]


# --- symbol index -----------------------------------------------------------

def test_scaffold_makes_the_symbol_index_non_empty(scaffold_symbols):
    """docs/TRACEABILITY.md Cycle 0 row 1, verbatim: the symbol index stops
    being empty once the scaffold exists."""
    assert scaffold_symbols, "source_globs matched nothing -- symbol_lookup would still return nothing"


def test_quality_tier_manager_is_indexed(scaffold_symbols):
    """docs/CYCLE0_BOOTSTRAP_SPEC.md section 4: a symbol_lookup for
    QualityTierManager must return a hit."""
    hits = [s for s in scaffold_symbols if s["name"] == "QualityTierManager"]
    assert len(hits) == 1, "QualityTierManager is not in the symbol index exactly once"
    assert hits[0]["kind"] == "class"
    assert hits[0]["namespace"] == "JetFighter.Build"
    assert hits[0]["file"] == "Assets/_Game/Scripts/Build/QualityTierManager.cs"


@pytest.mark.parametrize("method", ["DetectTier", "ApplySettings"])
def test_quality_tier_manager_methods_are_indexed(scaffold_symbols, method):
    assert any(s["name"] == method and s["kind"] == "method" for s in scaffold_symbols), \
        f"{method} is missing from the symbol index"


def test_editor_tooling_stays_out_of_the_symbol_index(scaffold_symbols):
    """source_globs covers Scripts/ only, deliberately: editor tooling is not
    game knowledge and must not pollute retrieval."""
    assert not any(s["name"] == "IOSPlayerSettings" for s in scaffold_symbols)
    assert all(s["file"].startswith("Assets/_Game/Scripts/") for s in scaffold_symbols)


# --- iOS player settings ----------------------------------------------------

def test_ios_settings_match_the_phase1_spec(ios_config):
    settings = ios_config["player_settings"]
    assert settings["scripting_backend"] == "IL2CPP"
    assert settings["graphics_api"] == "Metal"
    assert settings["architecture"] == "ARM64"
    assert settings["bundle_identifier"].count(".") >= 2
    assert not settings["bundle_identifier"].endswith(".")


def test_minimum_ios_version_is_a_supported_floor(ios_config):
    """Below iOS 13 Unity 6 will not build at all; the floor is a real
    decision, so assert it is a parseable version, not a placeholder."""
    major, _, minor = ios_config["player_settings"]["target_minimum_ios_version"].partition(".")
    assert major.isdigit() and int(major) >= 13
    assert minor.isdigit()


def test_editor_script_applies_the_committed_config():
    """The JSON is only a source of truth if something reads it. Assert the
    editor script points at this file and at the settings that matter."""
    source = (ASSETS / "Editor" / "IOSPlayerSettings.cs").read_text()
    assert "config/ios.build.json" in source
    assert "ScriptingImplementation.IL2CPP" in source
    assert "GraphicsDeviceType.Metal" in source
    assert source.lstrip().startswith("#if UNITY_EDITOR")
