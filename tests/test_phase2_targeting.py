"""Cycle 2, branch phase2/right-hand-targeting (ADR-002).

Criterion: right-half touches move the reticle, left-half touches have **zero**
targeting effect -- explicitly "mirrors Phase 1's proof".

A mirror written twice can drift, so the divide is defined once in
ScreenRegions and both hands delegate. That turns the criterion into a
partition property: for every on-screen x, exactly one hand claims it.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code

REAL_ROOT = Path(__file__).resolve().parent.parent
INPUT_DIR = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "UI" / "Input"
REGIONS = INPUT_DIR / "ScreenRegions.cs"
RETICLE = INPUT_DIR / "TargetReticleInput.cs"
JOYSTICK = INPUT_DIR / "JoystickInput.cs"


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


def test_the_divide_is_defined_once():
    """Written twice, the two halves drift -- into a dead column no touch
    answers, or an overlapping one where a single thumb flies the jet and
    moves the reticle. Neither is visible in a screenshot."""
    assert "ScreenRegions.IsInLeft" in code(JOYSTICK)
    assert "ScreenRegions.IsInRight" in code(RETICLE)
    for path in (JOYSTICK, RETICLE):
        body = _method_body(path, "public static bool IsInRegion(")
        assert "screenWidth *" not in body, f"{path.name} still computes the divide itself"


def test_the_region_check_gates_the_pointer_entry_point():
    """The same structural pattern as Phase 1: a rejection in OnPointerDown,
    not an assumption about where a RectTransform sits."""
    source = code(RETICLE)
    body = source[source.index("public void OnPointerDown"):source.index("public void OnDrag")]
    assert "IsInRegion" in body
    assert "return;" in body


def test_only_the_pointer_that_claimed_the_reticle_can_move_it():
    """Otherwise the left thumb drags the lock while flying."""
    body = _method_body(RETICLE, "public void OnDrag(")
    assert "activePointerId" in body


def test_the_lock_survives_the_finger_lifting():
    """The player raises the thumb to press the missile button. A lock that
    died with the touch would make the weapon unusable one-handed."""
    body = _method_body(RETICLE, "public void OnPointerUp(")
    assert "ClearTarget" not in body


def test_the_ground_restriction_is_a_layer_mask_not_a_type_check():
    """ADR-002 is Accepted *(implied)* -- nothing in the repo says missiles may
    not hit air targets; the mask just makes it so. Keeping it as data means
    reversing it is a mask change plus a priority rule, not a rewrite."""
    source = code(RETICLE)
    assert "LayerMask targetableLayers" in source
    assert "targetableLayers" in _method_body(RETICLE, "public void Aim(")
    assert "EnemyController" not in source, "targeting hard-codes an enemy type instead of a layer"


def test_a_stale_lock_is_cleared():
    """Enemies are pooled, so a killed one is deactivated rather than
    destroyed -- the launcher would otherwise fire at it."""
    body = _method_body(RETICLE, "private void Update()")
    assert "activeInHierarchy" in body
    assert "ClearTarget" in body


def test_the_reticle_implements_the_phase_1_seam():
    """`ITargetInput` was declared in Phase 1 and deliberately left
    unimplemented. This is the cell that implements it, and the router already
    holds the interface -- so nothing in Phase 1 changes."""
    assert "ITargetInput" in code(RETICLE)
    assert "ScreenTarget" in code(RETICLE)


def test_the_partition_is_asserted_not_just_the_two_halves():
    edit = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "ScreenRegionsTests.cs").read_text()
    assert "EveryOnScreenColumnBelongsToExactlyOneHand" in edit
    assert "AreNotEqual" in edit, "the partition is checked as two separate facts, not as one property"


def test_the_zero_effect_half_is_asserted_in_playmode():
    play = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "PlayMode"
            / "TargetReticlePlayModeTests.cs").read_text()
    assert "ALeftHalfTouchHasZeroTargetingEffect" in play
    assert "AirEnemiesAreNotLockable" in play, "the ADR-002 restriction is not tested"
    assert "IPointerDownHandler)reticle" in play, "the suite pokes fields instead of driving pointers"


@pytest.mark.parametrize("name", ["ScreenRegions", "TargetReticleInput"])
def test_the_targeting_types_are_retrievable(monkeypatch, name):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == "JetFighter.UI.Input"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase2_right_hand_targeting")
    assert {"TargetReticleInput", "ScreenRegions"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase2/right-hand-targeting`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
    assert "ADR-002" in row[0]
