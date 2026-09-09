"""Cycle 1, branch phase1/player-flight-rigidbody.

The flight model's own assertions are C# (EditMode for the arithmetic,
PlayMode for inertia and banking under a real tick) and cannot run until a
Unity licence is configured. These are the checks that hold today, and they
guard the two things a licence would not catch anyway: that the model stayed
testable, and that this branch really is the unconstrained one.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner

REAL_ROOT = Path(__file__).resolve().parent.parent
PLAYER = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Player"
CONTROLLER = PLAYER / "JetController.cs"
TESTS = REAL_ROOT / "Assets" / "_Game" / "Tests"


@pytest.fixture
def indexed(monkeypatch) -> dict[str, dict]:
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]
    return {s["name"]: s for s in symbols}


@pytest.mark.parametrize("name", ["JetController", "JetFlightConfig"])
def test_the_flight_types_are_retrievable(indexed, name):
    """A cell whose symbols are not in the index is invisible to
    `symbol_lookup`, which is how the next cell finds it."""
    assert name in indexed, f"{name} is missing from the symbol index"
    assert indexed[name]["namespace"] == "JetFighter.Player"


@pytest.mark.parametrize("fn", ["ComputeAcceleration", "ComputeTargetBankAngle", "StepTowardBank"])
def test_the_model_arithmetic_stays_a_pure_static_function(fn):
    """These are what the EditMode suite drives. Folding one back into an
    instance method that touches Rigidbody or Time would silently cost the
    whole no-scene-needed test layer, and nothing else would notice."""
    declarations = [l for l in CONTROLLER.read_text().splitlines() if f" {fn}(" in l]
    assert declarations, f"{fn} no longer exists"
    assert any("public static" in l for l in declarations), \
        f"{fn} is no longer a public static function"


def test_the_flight_model_is_free_of_engine_singletons(indexed):
    """The pure functions take deltaTime as an argument rather than reading
    Time.fixedDeltaTime, which is what makes the frame-rate independence test
    possible at all."""
    body = CONTROLLER.read_text()
    tail = body[body.index("// --- pure functions"):]
    for forbidden in ("Time.", "GetComponent", "FindObjectOf"):
        assert forbidden not in tail, f"the pure section reaches for {forbidden}"


def test_this_branch_is_deliberately_unconstrained():
    """docs/PHASE1_TECHNICAL_SPEC.md section 3: inertia and banking are
    validated in free 3D space *before* the plane lock, so a bad feel later
    has exactly one possible cause. A constraint sneaking in here would make
    the next branch's own test pass before that branch existed."""
    assert not (REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Physics" / "PlaneConstraint.cs").exists()
    # Comments may name it -- the controller's own docstring explains why the
    # lock is absent. Only executable lines are the concern here.
    code = [l for l in CONTROLLER.read_text().splitlines()
            if not l.lstrip().startswith(("//", "///", "*", "/*"))]
    assert not any("PlaneConstraint" in l for l in code)


def test_banking_never_rotates_the_rigidbody():
    """The physics body stays axis-aligned so collision stays predictable;
    only the child visual tilts."""
    source = CONTROLLER.read_text()
    assert "visual.localRotation" in source
    for forbidden in ("transform.localRotation =", "transform.rotation =", "body.rotation ="):
        assert forbidden not in source, f"banking assigns {forbidden}"


def test_both_test_modes_cover_the_flight_model():
    """The unity-test gate fails a mode that discovers nothing, and this
    branch deletes the Cycle 0 scaffold tests that were holding each mode
    open."""
    edit = (TESTS / "EditMode" / "JetFlightModelTests.cs").read_text()
    play = (TESTS / "PlayMode" / "JetFlightPlayModeTests.cs").read_text()
    assert edit.count("[Test]") >= 5
    assert play.count("[UnityTest]") >= 5
    assert not list(TESTS.rglob("Scaffold*.cs")), \
        "the scaffold smoke tests outlived their stated purpose"


def test_the_seed_node_matches_the_traceability_row():
    """docs/TRACEABILITY.md 'Closing a cell': a ticked row whose seed node is
    absent means phase-scoped retrieval returns nothing for that cell."""
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase1_player_flight_rigidbody")
    assert {"JetController", "JetFlightConfig"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase1/player-flight-rigidbody`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "`phase1_player_flight_rigidbody`" in row[0]
