"""Cycle 1, branch phase1/physics-plane-constraint (ADR-001).

The criterion -- locked-axis deviation inside an epsilon over a long run --
is a PlayMode soak and needs a Unity licence. What is checkable today is the
part that is easiest to get wrong and hardest to see in a playtest: the
execution order, and clamping position without also clearing velocity.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from gateway import symbol_scanner

from tests._csharp import mentions

REAL_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REAL_ROOT / "Assets" / "_Game" / "Scripts"
CONSTRAINT = SCRIPTS / "Physics" / "PlaneConstraint.cs"
CONTROLLER = SCRIPTS / "Player" / "JetController.cs"


def _execution_order(source: str) -> int:
    """The [DefaultExecutionOrder] on a MonoBehaviour, 0 when unset."""
    match = re.search(r"\[DefaultExecutionOrder\((-?\d+)\)\]", source)
    return int(match.group(1)) if match else 0


def test_the_constraint_runs_after_the_flight_model():
    """The whole design rests on this. If the clamp ran first, a tick's force
    would be added after it and the body would leave the plane before anything
    corrected it -- and the symptom is drift under load, which looks like a
    tuning problem, not an ordering one."""
    assert _execution_order(CONSTRAINT.read_text()) > _execution_order(CONTROLLER.read_text())


def test_the_clamp_zeroes_velocity_as_well_as_position():
    """Position alone leaves the body fighting the clamp every tick: visible
    jitter, and momentum that never resolves. Velocity alone lets anything
    that sets a position stay off-plane forever."""
    source = CONSTRAINT.read_text()
    assert "body.position = WithComponent" in source
    assert "body.linearVelocity = WithComponent" in source


def test_it_is_a_post_solve_correction_not_a_joint():
    """Roadmap section 3 point 2, and the reason ADR-001 is satisfiable at
    all: joints resolve through the solver and introduce jitter that fights
    the arcade feel."""
    for joint in ("ConfigurableJoint", "FixedJoint", "HingeJoint"):
        assert not mentions(CONSTRAINT, joint)
    assert not mentions(CONSTRAINT, "RigidbodyConstraints"), \
        "freezing an axis via RigidbodyConstraints is the solver path this avoids"


def test_the_locked_axis_is_configurable_not_hard_coded():
    """ADR-001 chose Z for Phase 1. A component that only understands Z makes
    that decision unrevisable, and Phase 2's ground enemies may want another
    plane."""
    source = CONSTRAINT.read_text()
    assert "enum Axis" in source
    assert re.search(r"lockedAxis\s*=\s*Axis\.Z", source), "Z must still be the default"


def test_the_constraint_type_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["PlaneConstraint"]["namespace"] == "JetFighter.Physics"
    assert symbols["PlaneConstraint"]["file"] == "Assets/_Game/Scripts/Physics/PlaneConstraint.cs"


@pytest.mark.parametrize("mode,name,minimum", [
    ("EditMode", "PlaneConstraintTests.cs", 3),
    ("PlayMode", "PlaneConstraintPlayModeTests.cs", 5),
])
def test_the_constraint_is_covered_in_both_modes(mode, name, minimum):
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / mode / name).read_text()
    assert source.count("[Test]") + source.count("[UnityTest]") >= minimum


def test_the_soak_asserts_an_epsilon_over_many_ticks():
    """The row's criterion is 'stays within epsilon over a long run'. A
    single-tick assertion would pass on a constraint that drifts."""
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "PlayMode"
              / "PlaneConstraintPlayModeTests.cs").read_text()
    assert "Epsilon" in source
    ticks = [int(n) for n in re.findall(r"i < (\d+); i\+\+", source)]
    assert max(ticks) >= 500, "the longest soak is too short to expose accumulated drift"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase1_physics_plane_constraint")
    assert "PlaneConstraint" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase1/physics-plane-constraint`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-001" in row[0]
