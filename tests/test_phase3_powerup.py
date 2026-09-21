"""Cycle 3, branch phase3/powerup-core (ADR-003).

Criterion: a fire-rate pickup changes the fire interval within one frame, and
stacking respects the hard cap under repeated pickups.

The cap is the load-bearing half. Without it the 1/sec baseline that the whole
difficulty curve is calibrated against stops meaning anything about a minute
into a run, and every later balance number is measured against a moving
target.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
POWERUP = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "PowerUp"
CONTROLLER = POWERUP / "PowerUpController.cs"
DEF = POWERUP / "PowerUpDef.cs"
PICKUP = POWERUP / "PowerUpPickup.cs"


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


def test_stacking_is_multiplicative():
    """Additive stacking makes the tenth pickup worth as much as the first,
    which is the opposite of how a power fantasy should read."""
    body = _method_body(CONTROLLER, "public float MultiplierFor(")
    assert "product *=" in body
    assert "product +=" not in body


def test_the_cap_is_applied_to_the_composed_multiplier():
    """ADR-003 and the spec's "fire rate can't exceed 3x base"."""
    body = _method_body(CONTROLLER, "public float MultiplierFor(")
    assert "Mathf.Clamp(product, 1f, maxMultiplier)" in body


def test_the_cap_does_not_work_by_refusing_pickups():
    """A refused pickup is invisible: the player collected something and saw
    nothing happen. A capped one still shows its effect and stops adding."""
    body = _method_body(CONTROLLER, "public bool Apply(PowerUpDef def)")
    assert "maxMultiplier" not in body, "Apply refuses pickups instead of capping the result"


def test_stats_are_recomposed_from_scratch_rather_than_deltas():
    """Dividing out an expired stack after the cap clamped the product does
    not return where the player started, and that error accumulates over a run
    until the jet is quietly faster or slower than any pickup explains."""
    body = _method_body(CONTROLLER, "private void Recompose()")
    assert "ResetToBaseline" in body
    for stat in ("ScaleSpeed", "ScaleFireRate", "ScaleDamage"):
        assert stat in body


def test_duration_zero_means_permanent_for_the_run():
    """ADR-003's sentinel, named so no call site has to remember it."""
    assert "public bool IsPermanentForRun => duration <= 0f;" in code(DEF)
    assert "IsPermanentForRun" in code(CONTROLLER)


def test_permanent_stacks_are_skipped_by_the_expiry_tick():
    body = _method_body(CONTROLLER, "public void Tick(float deltaTime)")
    assert "stack.Permanent" in body
    assert "continue" in body


def test_expiry_iterates_backwards():
    """Removing while iterating forwards skips the next element, so one of two
    stacks expiring on the same frame would survive."""
    body = _method_body(CONTROLLER, "public void Tick(float deltaTime)")
    assert "for (int i = active.Count - 1; i >= 0; i--)" in body


def test_power_level_is_derived_from_active_stacks():
    """ADR-003's stated consequence: difficulty falls again as power-ups
    expire, which is what makes the scaling power-relative rather than
    time-relative. A player who loses their stacks must not stay in the deep
    end."""
    body = _method_body(CONTROLLER, "public float PlayerPowerLevel")
    assert "foreach (ActiveStack stack in active)" in body
    assert "elapsed" not in body and "Time." not in body


def test_a_mis_authored_power_up_cannot_debuff():
    """There is no design for debuffs; silently slowing the player would read
    as a bug."""
    body = _method_body(CONTROLLER, "public bool Apply(PowerUpDef def)")
    assert "def.magnitude <= 1f" in body
    assert "Mathf.Max(1f, stack.Def.magnitude)" in code(CONTROLLER)


def test_the_tick_takes_delta_time():
    """A cap test that needs a real minute of pickups is a cap test nobody
    runs."""
    assert "public void Tick(float deltaTime)" in code(CONTROLLER)


def test_the_pickup_only_answers_to_a_collector_layer():
    """An enemy flying through must not consume the pickup, nor silently
    remove it from the player."""
    source = code(PICKUP)
    assert "collectorLayers" in source
    assert "IsCollector" in _method_body(PICKUP, "private void OnTriggerEnter(")


def test_the_pickup_is_consumed_exactly_once():
    body = _method_body(PICKUP, "public void Finish()")
    assert "if (collected)" in body
    assert "collected = true" in body


def test_the_pickup_deactivates_rather_than_destroys():
    """These come from a drop table next cell and will be pooled."""
    source = code(PICKUP)
    assert "SetActive(false)" in source
    assert "Destroy(" not in source


def test_a_recycled_pickup_is_rearmed():
    assert "public void Arm(" in code(PICKUP)


def test_a_broken_pickup_cannot_be_farmed():
    """Consumed even when Apply refuses the asset -- leaving it in the world
    would let the player collect the same broken pickup forever."""
    body = _method_body(PICKUP, "public bool TryCollect(")
    apply_at = body.index("controller.Apply(def)")
    assert "Finish()" in body[apply_at:]


@pytest.mark.parametrize("name", ["PowerUpDef", "PowerUpController", "PowerUpPickup"])
def test_the_powerup_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.PowerUp"


def test_both_criteria_are_asserted_in_csharp():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "PowerUpCoreTests.cs").read_text()
    assert "AFireRatePickupChangesTheFireIntervalImmediately" in source
    assert "StackingNeverExceedsTheHardCap" in source
    assert "ExpiryReturnsTheExactBaselineNotAnApproximationOfIt" in source


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase3_powerup_core")
    assert {"PowerUpDef", "PowerUpController", "PowerUpPickup"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase3/powerup-core`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-003" in row[0]
