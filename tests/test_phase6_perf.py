"""Cycle 6, branch phase6/perf-profiling-pass.

Criterion: sustained fps floor at low tier on the lowest supported device.

The spec makes this a measurement cell -- no new gameplay classes, and the
Burst decision "based on actual profiling data, not speculatively". A profiler
capture is the criterion and no CI run can produce one, so **the row is left
open**.

What ships is the property checks that stop a capture being invalidated by a
regression introduced after it was taken, and a procedure that says what to
measure and what evidence a passing pass produces.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
PROBE = SCRIPTS / "Build" / "FrameBudgetProbe.cs"
PROCEDURE = REAL_ROOT / "docs" / "PERF_PROFILING_PROCEDURE.md"

# Systems whose Update runs every frame while enemies are on screen.
HOT_PATHS = [
    SCRIPTS / "Weapon" / "Bullet.cs",
    SCRIPTS / "Weapon" / "MissileController.cs",
    SCRIPTS / "Weapon" / "PrimaryGunController.cs",
    SCRIPTS / "Enemy" / "EnemyController.cs",
    SCRIPTS / "PowerUp" / "PowerUpPickup.cs",
    SCRIPTS / "Player" / "JetController.cs",
    SCRIPTS / "Physics" / "PlaneConstraint.cs",
    SCRIPTS / "Enemy" / "EnemySpawner.cs",
]


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


def test_the_row_is_not_ticked():
    """A profiler capture is the criterion. Ticking on property checks alone
    would claim a sustained frame rate nobody measured."""
    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase6/perf-profiling-pass`" in l and l.startswith("|")]
    assert len(row) == 1
    assert row[0].startswith("| [ ] |"), "the profiling row was ticked without a capture"
    assert "PERF_PROFILING_PROCEDURE" in row[0], "the row does not say what is still owed"


@pytest.mark.parametrize("path", HOT_PATHS, ids=lambda p: p.name)
def test_no_hot_path_allocates_a_gameobject(path):
    """A GC spike is the single most common cause of a dropped frame on iOS,
    and it is invisible in an average frame time. The pools already bound
    this; here it is one property across every system."""
    source = code(path)
    for allocation in ("Instantiate(", "Destroy("):
        assert allocation not in source, f"{path.name} allocates in a hot path via {allocation}"


@pytest.mark.parametrize("path", HOT_PATHS, ids=lambda p: p.name)
def test_no_hot_path_uses_linq(path):
    """Every LINQ expression allocates an enumerator per call, times the
    on-screen entity cap, every frame."""
    assert "using System.Linq" not in code(path)


@pytest.mark.parametrize("path", HOT_PATHS, ids=lambda p: p.name)
def test_no_per_frame_component_lookup(path):
    """A GetComponent per frame times the enemy cap is a measurable cost that
    never shows up as one obvious frame."""
    source = code(path)
    for hook in ("private void Update()", "private void FixedUpdate()", "private void LateUpdate()"):
        if hook not in source:
            continue
        body = _method_body(path, hook)
        for lookup in ("GetComponent", "FindObjectOf", "FindFirstObjectByType"):
            assert lookup not in body, f"{path.name}.{hook} does a {lookup} every frame"


def test_pools_are_prewarmed():
    """An allocation spike at the exact moment the player is watching."""
    for path in (SCRIPTS / "Weapon" / "PrimaryGunController.cs",
                 SCRIPTS / "Weapon" / "MissileLauncher.cs",
                 SCRIPTS / "Enemy" / "EnemySpawner.cs"):
        assert "Prewarm" in code(path), f"{path.name} does not prewarm its pool"


def test_the_probe_measures_the_right_statistic():
    """The average is the wrong one: a steady 60fps run with one 200ms hitch
    every ten seconds averages fine and feels broken, and it is the hitch a
    player reports."""
    source = code(PROBE)
    assert "public float OnePercentLowMs()" in source
    assert "public float WorstFrameMs" in source
    assert "public int HitchCount" in source


def test_the_probe_does_not_allocate_per_frame():
    """A probe that allocated per frame would be measuring itself."""
    body = _method_body(PROBE, "public void Sample(float deltaSeconds)")
    for allocation in ("new ", "List<", "Linq"):
        assert allocation not in body, f"Sample allocates via {allocation}"


def test_the_window_is_bounded():
    """An unbounded history is its own leak over a five-minute run."""
    source = code(PROBE)
    assert "% window.Length" in source


def test_the_probe_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["FrameBudgetProbe"]["namespace"] == "JetFighter.Build"


def test_the_procedure_defers_the_burst_decision_to_data():
    """The spec's words: "based on actual profiling data, not speculatively".
    Burst on a path that is not the bottleneck costs build time and
    compilation complexity for nothing."""
    text = PROCEDURE.read_text()
    assert "Burst" in text
    assert "top three" in text
    assert "not adopted" in text, "the procedure does not allow for the likely outcome"


def test_the_procedure_names_its_evidence():
    text = PROCEDURE.read_text()
    for evidence in ("Profiler capture", "Device model", "1% low", "worst frame"):
        assert evidence.lower() in text.lower()


def test_no_new_gameplay_class_was_added():
    """The spec is explicit: this branch adds no new gameplay classes. The
    probe is instrumentation -- removing it changes no behaviour."""
    source = code(PROBE)
    for gameplay in ("JetController", "EnemyHealth", "Wallet", "PowerUpController"):
        assert gameplay not in source


def test_the_seed_node_exists_even_though_the_row_is_open():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase6_perf_profiling_pass")
    assert "FrameBudgetProbe" in node["symbols"]
    assert "open" in node["tags"]
