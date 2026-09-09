"""Cycle 3, branch phase3/difficulty-scaling (roadmap risk R5).

Criterion: "Time-to-kill never exceeds the ceiling across a PlayerPowerLevel
sweep" -- i.e. the run never becomes mathematically unwinnable.

`1 + k * power` alone is unbounded. At high power the enemy's health outruns
the player's DPS and the run is not hard, it is unwinnable -- with nothing in
the game distinguishing the two. The clamp is the whole cell; these tests hold
its shape, and the C# suite sweeps the guarantee itself.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
ENEMY = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Enemy"
MANAGER = ENEMY / "DifficultyManager.cs"
CURVE = ENEMY / "DifficultyCurve.cs"


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


def test_the_curve_is_a_pure_function():
    """"Never becomes unwinnable" is a claim about every point on the curve. A
    scene test can only ever visit a few; a static function can be swept."""
    assert "public static float ComputeStatMultiplier(DifficultyCurve curve," in code(MANAGER)
    assert "public static float TimeToKill(" in code(MANAGER)


def test_the_formula_is_the_roadmaps():
    body = _method_body(MANAGER, "public static float ComputeStatMultiplier(DifficultyCurve curve,")
    assert "1f + curve.k * Mathf.Max(0f, playerPowerLevel)" in body


def test_the_multiplier_is_clamped_by_the_time_to_kill_ceiling():
    """The defeatability floor. Without it the curve is unbounded."""
    body = _method_body(MANAGER, "public static float ComputeStatMultiplier(DifficultyCurve curve,")
    assert "curve.timeToKillCeilingSeconds * playerDps / baseEnemyHealth" in body
    assert "Mathf.Clamp(raw" in body


def test_scaling_is_power_relative_not_time_relative():
    """Roadmap section 4. Time-relative scaling punishes the player who is
    surviving without getting stronger -- exactly the player least able to
    absorb it."""
    source = code(MANAGER)
    assert "playerPowerLevel" in source
    for time_based in ("Time.time", "elapsedSeconds", "survivalTime"):
        assert time_based not in source


def test_a_non_damaging_player_does_not_get_an_unbounded_curve():
    """The one guarantee this class offers must not become conditional on data
    it does not control."""
    body = _method_body(MANAGER, "public static float ComputeStatMultiplier(DifficultyCurve curve,")
    assert "playerDps <= 0f || baseEnemyHealth <= 0f" in body


def test_enemies_never_fall_below_their_authored_stats():
    """The curve is a difficulty ramp, not a rubber band in both directions --
    a weak player should face the enemy as designed, not a weakened one."""
    body = _method_body(MANAGER, "public static float ComputeStatMultiplier(DifficultyCurve curve,")
    assert "minimumMultiplier" in body
    assert "Mathf.Max(floor" in body


def test_both_constants_are_data():
    """`k` and the ceiling will be retuned repeatedly against real play. In
    code that is a build per adjustment."""
    source = code(CURVE)
    assert "public float k" in source
    assert "public float timeToKillCeilingSeconds" in source
    assert ": ScriptableObject" in source
    manager = code(MANAGER)
    assert "0.35f" not in manager and "8f" not in manager, "a curve constant is hard-coded in the manager"


def test_variety_scales_as_well_as_stats():
    """Roadmap section 4: new archetypes read to the player as "getting
    stronger" without bullet-sponge inflation."""
    assert "GetUnlockedArchetypes" in code(MANAGER)
    assert "powerLevelThreshold" in code(CURVE)


def test_archetypes_unlock_at_the_threshold_inclusive():
    """A designer typing 2.0 means "from 2.0". Off-by-one here is invisible
    until someone wonders why an archetype never appears."""
    body = _method_body(MANAGER, "public static IReadOnlyList<EnemyDef> GetUnlockedArchetypes(DifficultyCurve curve,")
    assert "playerPowerLevel >= unlock.powerLevelThreshold" in body


def test_unlocking_accumulates_rather_than_replaces():
    """Earlier archetypes must keep spawning, or difficulty jumps rather than
    broadens."""
    body = _method_body(MANAGER, "public static IReadOnlyList<EnemyDef> GetUnlockedArchetypes(DifficultyCurve curve,")
    assert "unlocked.Add" in body
    assert "unlocked.Clear" not in body


def test_a_cleared_inspector_slot_cannot_spawn_a_null_enemy():
    body = _method_body(MANAGER, "public static IReadOnlyList<EnemyDef> GetUnlockedArchetypes(DifficultyCurve curve,")
    assert "unlock.archetype != null" in body


def test_a_null_curve_is_neutral_rather_than_fatal():
    body = _method_body(MANAGER, "public static float ComputeStatMultiplier(DifficultyCurve curve,")
    assert "curve == null" in body


@pytest.mark.parametrize("name", ["DifficultyCurve", "DifficultyManager"])
def test_the_difficulty_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.Enemy"


def test_the_guarantee_is_swept_not_spot_checked():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "DifficultyScalingTests.cs").read_text()
    assert "TimeToKillNeverExceedsTheCeilingAcrossThePowerSweep" in source
    assert "ScalingNeverMakesTheFightWorseThanItAlreadyWas" in source, \
        "sweeping power alone passes on a formula that ignores DPS and health"
    assert "AWinnableFightStaysInsideTheCeilingAtEveryPowerLevel" in source
    assert "power += 0.05f" in source


def test_the_boundary_of_the_guarantee_is_surfaced_not_hidden():
    """An enemy whose authored health already outlasts the ceiling at the
    player's DPS cannot be rescued by a multiplier whose floor is 1. Pretending
    otherwise would make the guarantee a claim the code cannot keep, so the
    case is reported instead -- the spawner uses it to pick something killable.
    """
    assert "public static bool ExceedsCeilingUnscaled(" in code(MANAGER)
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode"
              / "DifficultyScalingTests.cs").read_text()
    assert "AnEnemyTooToughBeforeScalingIsReportedRatherThanHidden" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_difficulty_scaling")
    assert {"DifficultyCurve", "DifficultyManager"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/difficulty-scaling`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
