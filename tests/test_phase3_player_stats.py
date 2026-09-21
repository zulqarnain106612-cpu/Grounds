"""Cycle 3, branch phase3/player-stats-runtime.

Criterion: JetController and PrimaryGunController provably read only from
PlayerStatsRuntime, and the source ScriptableObjects are never mutated at
runtime -- the spec suggests verifying the second by asset diff after a play
session.

An asset diff is a manual ritual that only catches a mutation someone
remembers to look for. These assert the property in the source instead: no
write to an authored asset exists anywhere in the player-facing code.
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
STATS = SCRIPTS / "Player" / "PlayerStatsRuntime.cs"
FLIGHT_SEAM = SCRIPTS / "Player" / "IFlightStats.cs"
JET = SCRIPTS / "Player" / "JetController.cs"
GUN = SCRIPTS / "Weapon" / "PrimaryGunController.cs"

# Fields authored on the ScriptableObjects. A runtime write to any of these
# survives the play session in the editor.
AUTHORED_FIELDS = [
    "maxSpeed", "acceleration", "linearDrag", "angularDrag",
    "bankAngleMax", "bankResponsiveness",
    "fireRatePerSecond", "damage", "projectileSpeed", "projectileLifetime",
]


@pytest.mark.parametrize("path", [STATS, JET, GUN], ids=lambda p: p.name)
def test_no_authored_field_is_ever_assigned(path):
    """The half the spec verifies by asset diff. A ScriptableObject edited in
    play mode keeps its new value after the session, so one power-up collected
    during testing becomes the project's authored baseline -- arriving in git
    as a modified asset nobody remembers touching."""
    source = code(path)
    for field in AUTHORED_FIELDS:
        # `x.field =` but not `==`, and not a local of the same name.
        offenders = re.findall(rf"\w+\.{field}\s*=(?!=)", source)
        assert not offenders, f"{path.name} writes to an authored asset field: {offenders}"


def test_the_jet_flies_on_stats_not_on_the_asset():
    """`config.` anywhere in the flight path means one number a power-up
    cannot reach, and one place the criterion silently does not hold."""
    source = code(JET)
    assert "config." not in source, "the controller still reads the ScriptableObject directly"
    assert "Stats.MaxSpeed" in source
    assert "Stats.LinearDrag" in source


def test_the_pure_functions_take_numbers_not_the_asset():
    """Taking the ScriptableObject was an open invitation to write back to
    it, and it also tied the flight arithmetic to an asset type."""
    source = code(JET)
    assert "ComputeAcceleration(Vector2 input, Vector2 currentVelocity,\n            float maxSpeed, float acceleration)" in source
    assert "ComputeTargetBankAngle(float lateralVelocity, float maxSpeed, float bankAngleMax)" in source
    assert "JetFlightConfig config)" not in source


def test_the_runtime_implements_both_seams():
    """One declared in Phase 1, one in the Phase 2 retrofit. If either had to
    change to accept this component, declaring them early bought nothing."""
    source = code(STATS)
    assert "IFlightStats" in source and "IPlayerStats" in source


def test_nothing_in_the_weapons_or_flight_code_names_the_runtime():
    """They hold the interfaces. A concrete reference would put Phase 4's
    remote player -- which needs its own stats object -- back on this class."""
    for path in (JET, GUN):
        assert "PlayerStatsRuntime" not in code(path), f"{path.name} names the concrete stats class"


def test_the_baseline_is_a_snapshot_rather_than_the_asset():
    """Holding the asset and promising not to write to it is a convention.
    Holding a snapshot is a guarantee."""
    source = code(FLIGHT_SEAM)
    assert "sealed class FlightStatsSnapshot" in source
    assert "{ get; }" in source, "the snapshot exposes settable properties"
    assert "set;" not in source


def test_modifiers_compose_rather_than_assign():
    """Phase 3 stacks power-ups multiplicatively against a hard cap. Absolute
    setters would make "3x base fire rate" a number nobody could compute once
    two power-ups were live."""
    source = code(STATS)
    for setter in ("ScaleSpeed", "ScaleFireRate", "ScaleDamage"):
        assert f"public void {setter}(float factor)" in source
    assert "speedMultiplier * SanitizeFactor" in source


def test_a_bad_factor_cannot_ground_the_player():
    """Power-up magnitudes are authored data; a zero typed into an inspector
    should not stop the gun firing or fly the jet backwards."""
    source = code(STATS)
    assert "factor <= 0f || float.IsNaN(factor)" in source


def test_there_is_a_backstop_on_stacking():
    source = code(STATS)
    assert "absoluteMultiplierCeiling" in source
    assert "Mathf.Clamp(multiplier" in source


def test_stats_are_resettable_per_run():
    """A component surviving a scene reload would otherwise carry the last
    run's power-ups into the next one."""
    assert "public void ResetToBaseline()" in code(STATS)


def test_the_stats_run_before_everything_that_reads_them():
    source = code(STATS)
    assert re.search(r"\[DefaultExecutionOrder\(-\d+\)\]", source), \
        "the stats can be read before they are seeded"


def test_the_runtime_is_retrievable(monkeypatch):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols["PlayerStatsRuntime"]["namespace"] == "JetFighter.Player"
    assert symbols["IFlightStats"]["kind"] == "interface"


def test_the_no_mutation_claim_is_also_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "PlayerStatsRuntimeTests.cs").read_text()
    assert "ScalingSpeedDoesNotTouchTheAsset" in source
    assert "ScalingFireRateAndDamageDoesNotTouchTheWeaponAsset" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_player_stats_runtime")
    assert "PlayerStatsRuntime" in node["symbols"]

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/player-stats-runtime`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
