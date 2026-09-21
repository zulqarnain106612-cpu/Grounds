"""Cycle 1, branch phase1/input-joystick-mapping.

The row's criterion has two halves: a left-region-only guarantee test, and an
on-device touch test. The device half is a capture. This file guards the
structural half -- that the region check is a real gate in the pointer path
and not an assumption about where a RectTransform was anchored.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from gateway import symbol_scanner
from tests._csharp import code, mentions

REAL_ROOT = Path(__file__).resolve().parent.parent
INPUT_DIR = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "UI" / "Input"
JOYSTICK = INPUT_DIR / "JoystickInput.cs"
ROUTER = INPUT_DIR / "PlayerInputRouter.cs"
CONTROLLER = REAL_ROOT / "Assets" / "_Game" / "Scripts" / "Player" / "JetController.cs"


def test_the_region_check_gates_the_pointer_entry_point():
    """The guarantee has to live in OnPointerDown. A joystick that merely sits
    on the left half is one canvas-anchor change away from stealing the touch
    Phase 2 needs for missile lock -- and that failure looks like an input bug
    in a system nobody has written yet."""
    source = code(JOYSTICK)
    down = source[source.index("public void OnPointerDown"):]
    body = down[:down.index("public void OnDrag")]
    assert "IsInRegion" in body, "OnPointerDown does not consult the region check"
    assert "return;" in body, "the region check does not actually reject the touch"


def test_the_region_check_is_a_pure_static_function():
    """So the criterion is an assertion rather than a device observation:
    'we tried the right half and nothing happened' does not prove zero."""
    assert "public static bool IsInRegion(" in JOYSTICK.read_text()


def test_only_the_pointer_that_claimed_the_stick_can_move_it():
    """Without this a second finger anywhere in the left half yanks the jet
    sideways mid-turn."""
    source = code(JOYSTICK)
    for handler in ("OnDrag", "OnPointerUp"):
        section = source[source.index(f"public void {handler}"):]
        section = section[:section.index("\n    }") + 1]
        assert "activePointerId" in section, f"{handler} does not check pointer ownership"


def test_the_flight_model_is_still_input_agnostic():
    """JetController takes a Vector2 and does not care where it came from --
    which is what let the whole model be unit-tested with no input system.
    A reference back would undo that."""
    assert not mentions(CONTROLLER, "JoystickInput")
    assert not mentions(CONTROLLER, "PlayerInputRouter")


def test_the_router_reads_input_on_frames_not_physics_ticks():
    """Touches arrive on frame boundaries. Sampling them in FixedUpdate drops
    or double-reads them depending on the tick rate, and this project ships
    two (QualityTierManager caps the low tier at 30)."""
    source = code(ROUTER)
    assert "void Update()" in source
    assert "void FixedUpdate()" not in source


def test_a_missing_joystick_reads_as_no_input():
    """Not as the last input. A stale vector flies the jet into a wall while
    the UI is being rebuilt."""
    assert "Vector2.zero" in code(ROUTER)


def test_the_router_holds_the_seam_not_the_implementation():
    """ADR-002 gave targeting to Phase 2, and Phase 1 declared `ITargetInput`
    so it would not grow a right-hand code path it had to unpick.

    Phase 2's `TargetReticleInput` now implements it — which is the point of
    having declared it. What must stay true is that the router never learns
    the concrete type, so a third targeting source (a gamepad, a Phase 4
    remote player) drops in without touching this class."""
    interface = (INPUT_DIR / "ITargetInput.cs").read_text()
    assert "interface ITargetInput" in interface
    assert "ITargetInput" in code(ROUTER)
    assert "TargetReticleInput" not in code(ROUTER), \
        "the router named a concrete targeting source"


@pytest.mark.parametrize("name,namespace", [
    ("JoystickInput", "JetFighter.UI.Input"),
    ("PlayerInputRouter", "JetFighter.UI.Input"),
    ("ITargetInput", "JetFighter.UI.Input"),
])
def test_the_input_types_are_retrievable(monkeypatch, name, namespace):
    monkeypatch.setattr(symbol_scanner, "CONFIG_PATH", REAL_ROOT / "config" / "agent.config.json")
    symbols = {s["name"]: s for s in symbol_scanner.rebuild_symbol_index(REAL_ROOT)["symbols"]}
    assert symbols[name]["namespace"] == namespace


def test_the_runtime_assembly_can_see_ugui():
    """EventSystems lives in UnityEngine.UI. Without the reference the whole
    runtime assembly stops compiling -- and the unity-test gate would report
    that as zero discovered tests."""
    asmdef = json.loads(
        (REAL_ROOT / "Assets" / "_Game" / "Scripts" / "JetFighter.Runtime.asmdef").read_text())
    assert "UnityEngine.UI" in asmdef["references"]


def test_the_right_half_is_covered_by_a_sweep_not_a_single_point():
    source = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "EditMode" / "JoystickInputTests.cs").read_text()
    assert "for (float x" in source, "the right half is spot-checked, not swept"
    play = (REAL_ROOT / "Assets" / "_Game" / "Tests" / "PlayMode"
            / "PlayerInputRouterPlayModeTests.cs").read_text()
    assert "IPointerDownHandler)joystick" in play, \
        "the PlayMode suite pokes fields instead of driving the real pointer path"


def test_the_seed_node_matches_the_traceability_row():
    seeds = json.loads((REAL_ROOT / "knowledge" / "seeds.json").read_text())
    node = next(n for n in seeds["nodes"] if n["id"] == "phase1_input_joystick_mapping")
    assert {"JoystickInput", "PlayerInputRouter"} <= set(node["symbols"])

    row = [l for l in (REAL_ROOT / "docs" / "TRACEABILITY.md").read_text().splitlines()
           if "`phase1/input-joystick-mapping`" in l and l.startswith("|")]
    assert len(row) == 1 and row[0].startswith("| [x] |")
